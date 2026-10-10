using System.Linq;
using DV.Simulation.Cars;
using HarmonyLib;
using LocoSim.Implementations;
using LocoSim.Resources;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Visuals
{
 internal sealed class FlueSandBox : MonoBehaviour
    {
        // A shovelful of sand, as the stock shovel's chunks hold it, in kg.
        public const float Shovelful = 3f;

        private static readonly Color Crate = new Color(0.18f, 0.17f, 0.16f);
        private static readonly Color Sand = new Color(0.74f, 0.64f, 0.45f);
        private static readonly Vector3 Size = new Vector3(0.42f, 0.28f, 0.32f);

        /// <summary>Sand on the player's shovel, in kg (pile units).</summary>
        public static float Loaded { get; private set; }

        private ResourceContainer sand;

        public TrainCar Car { get; private set; }

        /// <summary>Takes up to <paramref name="kg"/> of loaded sand as it goes into a firebox; returns what was sand.</summary>
        public static float TakeLoaded(float kg)
        {
            var taken = Mathf.Min(Loaded, Mathf.Max(0f, kg));
            Loaded -= taken;
            return taken;
        }

        /// <summary>
        /// Builds the box on the floor at <paramref name="floor"/>, under
        /// <paramref name="parent"/>. Returns it, or null without sand to draw on.
        /// </summary>
        public static FlueSandBox Build(TrainCar car, Transform parent, Vector3 floor)
        {
            var flow = car.SimController != null ? car.SimController.SimulationFlow : null;
            var container = flow?.OrderedSimComps.OfType<ResourceContainer>().FirstOrDefault(c => c.resourceType == ResourceContainerType.SAND);
            if (container == null)
                return null;

            var root = new GameObject("flue sand box");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(floor, car.transform.rotation);
            var standard = Shader.Find("Standard");
            var body = Part(root.transform, "crate", new Vector3(0f, Size.y / 2f, 0f), Size, standard, Crate);
            Part(root.transform, "sand", new Vector3(0f, Size.y - 0.03f, 0f), new Vector3(Size.x - 0.04f, 0.04f, Size.z - 0.04f), standard, Sand);

            // The shovel finds piles by a trigger on the cab's layer.
            var trigger = root.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, Size.y / 2f, 0f);
            trigger.size = Size;
            root.layer = LayerMask.NameToLayer("Train_Interior");
            var pile = root.AddComponent<ShovelCoalPile>();
            pile.isInfinite = true;
            // Infinite piles report their own chunk mass; the standard shovel
            // holds three chunks (more for the bigger shovels).
            pile.coalChunkMass = Shovelful / 3f;
            var box = root.AddComponent<FlueSandBox>();
            box.Car = car;
            box.sand = container;
            root.AddComponent<HoverName>().Source = () => $"Flue sand: {container.amountReadOut.Value:F0} kg (load the shovel, throw it in under a hard pull)";
            Object.Destroy(body.GetComponent<Collider>());
            return box;
        }

        private static GameObject Part(Transform parent, string name, Vector3 at, Vector3 size, Shader shader, Color color)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            Object.Destroy(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);
            part.transform.localPosition = at;
            part.transform.localScale = size;
            var renderer = part.GetComponent<MeshRenderer>();
            if (shader != null)
                renderer.material = new Material(shader) { color = color };
            return part;
        }

        public float Available => sand != null ? Mathf.Max(0f, sand.amountReadOut.Value) : 0f;

        public float Take(float kg)
        {
            var taken = Mathf.Min(Available, Mathf.Max(0f, kg));
            if (taken > 0f && sand != null)
                sand.consumeExtIn.Value = taken;
            Loaded += taken;
            return taken;
        }

        public float PutBack(float kg)
        {
            var back = Mathf.Min(Loaded, Mathf.Max(0f, kg));
            Loaded -= back;
            if (back > 0f && sand != null)
                sand.refillExtIn.Value = back;
            return back;
        }
    }

    /// <summary>The sand box's sand, as a coal pile's coal to the shovel.</summary>
    [HarmonyPatch(typeof(ShovelCoalPile), nameof(ShovelCoalPile.TryRemoveCoal))]
    internal static class FlueSandTakePatch
    {
        private static bool Prefix(ShovelCoalPile __instance, float coalAmount, ref float __result)
        {
            if (!__instance.TryGetComponent<FlueSandBox>(out var box))
                return true;
            __result = box.Take(coalAmount);
            return false;
        }
    }

    [HarmonyPatch(typeof(ShovelCoalPile), nameof(ShovelCoalPile.TryAddCoal))]
    internal static class FlueSandPutBackPatch
    {
        private static bool Prefix(ShovelCoalPile __instance, float coalAmount, ref float __result)
        {
            if (!__instance.TryGetComponent<FlueSandBox>(out var box))
                return true;
            __result = box.PutBack(coalAmount);
            return false;
        }
    }

    [HarmonyPatch(typeof(ShovelCoalPile), nameof(ShovelCoalPile.CoalAvailable))]
    internal static class FlueSandAvailablePatch
    {
        private static bool Prefix(ShovelCoalPile __instance, ref float __result)
        {
            if (!__instance.TryGetComponent<FlueSandBox>(out var box))
                return true;
            __result = box.Available;
            return false;
        }
    }
}
