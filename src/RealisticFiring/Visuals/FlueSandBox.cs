using System.Collections.Generic;
using System.Linq;
using DV.CabControls;
using DV.Interaction;
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
        // A load of sand on the shovel, in kg: "about a quart at a time" (K02 p. 46).
        public const float Shovelful = 1.5f;

        private static readonly Color Crate = new Color(0.18f, 0.17f, 0.16f);
        internal static readonly Color Sand = new Color(0.74f, 0.64f, 0.45f);
        private static readonly Vector3 Size = new Vector3(0.42f, 0.28f, 0.32f);

        // An open box of steel plate this thick, m, the sand this far below its rim.
        private const float Wall = 0.015f;
        private const float SandBelowRim = 0.05f;

        // Sand grains across the box, on the fire bed's lumps.
        private const float GrainsAcross = 14f;

        private static Material sandMaterial;
        private static Material plainCrate;

        /// <summary>Sand on the player's shovel, in kg (pile units).</summary>
        public static float Loaded { get; private set; }

        private ResourceContainer sand;
        private SimulationFlow boundFlow;
        private bool usesTender;
        private TrainCar carrier;
        private ControlImplBase cover;
        private Collider[] pickupColliders;
        private Mesh surfaceMesh;

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
            var root = new GameObject("flue sand box");
            root.SetActive(false);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(floor, car.transform.rotation);
            var box = root.AddComponent<FlueSandBox>();
            if (!box.BindEngine(car))
            {
                Object.Destroy(root);
                return null;
            }
            // An open box in the plate of the floor it stands on, the sand in it.
            var plate = FloorMaterial(car, floor) ?? PlainCrate();
            Part(root.transform, "bottom", new Vector3(0f, Wall / 2f, 0f), new Vector3(Size.x, Wall, Size.z), plate);
            Part(root.transform, "left", new Vector3((Wall - Size.x) / 2f, Size.y / 2f, 0f), new Vector3(Wall, Size.y, Size.z), plate);
            Part(root.transform, "right", new Vector3((Size.x - Wall) / 2f, Size.y / 2f, 0f), new Vector3(Wall, Size.y, Size.z), plate);
            Part(root.transform, "front", new Vector3(0f, Size.y / 2f, (Size.z - Wall) / 2f), new Vector3(Size.x - 2f * Wall, Size.y, Wall), plate);
            Part(root.transform, "back", new Vector3(0f, Size.y / 2f, (Wall - Size.z) / 2f), new Vector3(Size.x - 2f * Wall, Size.y, Wall), plate);
            box.SandSurface(new Vector3(0f, Size.y - SandBelowRim, 0f), new Vector2(Size.x - 2f * Wall, Size.z - 2f * Wall));
            box.BuildPickup(new Vector3(0f, Size.y / 2f, 0f), Size);
            root.SetActive(true);
            return box;
        }

        /// <summary>Adds only a shovel target; the maker's sand surface and materials stay intact.</summary>
        internal static FlueSandBox BuildInTender(TrainCar engine, TrainCar tender, Transform anchor,
            ControlImplBase lid, Vector3 center, Vector2 size)
        {
            var root = new GameObject(TenderFlueSandBox.RootName);
            root.SetActive(false);
            root.transform.SetParent(anchor, false);
            root.transform.localPosition = center;
            // The native hatch opens around X. Its fixed parent's Z points up;
            // turn the X/Z pickup into that parent's X/Y hatch plane.
            root.transform.localRotation = TenderFlueSandBox.PickupRotation;
            var box = root.AddComponent<FlueSandBox>();
            box.usesTender = true;
            box.carrier = tender;
            box.cover = lid;
            if (!box.BindEngine(engine))
            {
                Object.Destroy(root);
                return null;
            }
            box.BuildPickup(new Vector3(0f, 0.035f, 0f), new Vector3(size.x, 0.08f, size.y));
            box.RefreshAccess();
            root.SetActive(true);
            return box;
        }

        private void BuildPickup(Vector3 center, Vector3 size)
        {
            // Built as the stock tender's pile is: the pile and its trigger on
            // the cab's layer, and a second trigger on the interactable layer,
            // which the item-use raycast sees, redirecting to the pile's target.
            var (pileLayer, useLayer) = PileLayers();
            var trigger = gameObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = center;
            trigger.size = size;
            gameObject.layer = pileLayer;
            var use = gameObject.AddComponent<ItemUseTarget>();
            var interactable = new GameObject("flue sand box interactable");
            interactable.layer = useLayer;
            interactable.transform.SetParent(transform, false);
            var useTrigger = interactable.AddComponent<BoxCollider>();
            useTrigger.isTrigger = true;
            useTrigger.center = trigger.center;
            useTrigger.size = trigger.size;
            RedirectTarget(interactable.AddComponent<ItemUseRedirect>()) = use;
            use.targetColliders = new Collider[] { trigger, useTrigger };
            pickupColliders = use.targetColliders;
            var pile = gameObject.AddComponent<ShovelCoalPile>();
            pile.isInfinite = true;
            // Infinite piles report their own chunk mass; the standard shovel
            // holds three chunks (more for the bigger shovels).
            pile.coalChunkMass = Shovelful / 3f;
            gameObject.AddComponent<HoverName>().Source = () => $"Flue sand: {Available:F0} kg (load a quart on the shovel, throw it in under a hard pull)";
        }

        internal bool BindEngine(TrainCar engine)
        {
            var flow = engine != null && engine.SimController != null ? engine.SimController.SimulationFlow : null;
            if (Car != engine || !ReferenceEquals(boundFlow, flow))
            {
                Car = engine;
                boundFlow = flow;
                sand = flow?.OrderedSimComps.OfType<ResourceContainer>().FirstOrDefault(c => c.resourceType == ResourceContainerType.SAND);
            }
            return sand != null;
        }

        private bool SupplyConnected => sand != null && (!usesTender || (carrier != null && Car != null
            && Car.SimController != null && ReferenceEquals(boundFlow, Car.SimController.SimulationFlow)
            && carrier.frontCoupler.CoupledCar() == Car));

        private bool Accessible => SupplyConnected && (!usesTender || (cover != null
            && TenderFlueSandBox.CanScoop(true, cover.Value)));

        private void Update()
        {
            if (usesTender)
                RefreshAccess();
        }

        private void RefreshAccess()
        {
            var accessible = Accessible;
            foreach (var collider in pickupColliders)
                collider.enabled = accessible;
        }

        private void OnDestroy()
        {
            if (surfaceMesh != null)
                Object.Destroy(surfaceMesh);
        }

        private static void Part(Transform parent, string name, Vector3 at, Vector3 size, Material material)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            Object.Destroy(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);
            part.transform.localPosition = at;
            part.transform.localScale = size;
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        // The material of the floor under the box: the renderer of the floor's
        // collider, or the smallest one whose top is the floor there.
        private static Material FloorMaterial(TrainCar car, Vector3 floor)
        {
            var up = car.transform.up;
            Physics.SyncTransforms();
            foreach (var hit in Physics.RaycastAll(floor + up * 0.1f, -up, 0.3f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
            {
                if (TrainCar.Resolve(hit.collider.gameObject) != car)
                    continue;
                var own = hit.collider.GetComponent<MeshRenderer>();
                if (own != null && own.sharedMaterial != null)
                    return own.sharedMaterial;
                break;
            }
            var interior = car.loadedInterior != null ? car.loadedInterior.transform : null;
            if (interior == null)
                return null;
            MeshRenderer best = null;
            var smallest = float.MaxValue;
            foreach (var renderer in interior.GetComponentsInChildren<MeshRenderer>(true))
            {
                var bounds = renderer.bounds;
                if (renderer.sharedMaterial == null || Mathf.Abs(bounds.max.y - floor.y) > 0.03f
                    || floor.x < bounds.min.x || floor.x > bounds.max.x || floor.z < bounds.min.z || floor.z > bounds.max.z)
                    continue;
                var volume = bounds.size.x * bounds.size.z;
                if (volume < smallest)
                {
                    smallest = volume;
                    best = renderer;
                }
            }
            return best != null ? best.sharedMaterial : null;
        }

        private static readonly AccessTools.FieldRef<ItemUseRedirect, ItemUseTarget> RedirectTarget =
            AccessTools.FieldRefAccess<ItemUseRedirect, ItemUseTarget>("target");

        private static (int Pile, int Use)? pileLayers;

        /// <summary>
        /// A shovel target into an oil burner's firebox, built as the sand box
        /// is (the stock coal targets' way: a target with the coal-target mark
        /// on the cab's layer, and an interactable child redirecting to it),
        /// for a box of <paramref name="size"/> m at a pose. The oil-fired mods'
        /// own targets sit on the default layer, which the shovel's click does
        /// not see. Thrown in through the open door, the shovel's load is sand.
        /// </summary>
        public static GameObject BuildFireboxTarget(Transform parent, Vector3 center, Quaternion rotation, Vector3 size)
        {
            var (pileLayer, useLayer) = PileLayers();
            var target = new GameObject("[RealisticFiring flue sand target]");
            target.layer = pileLayer;
            target.transform.SetParent(parent, false);
            target.transform.SetPositionAndRotation(center, rotation);
            var trigger = target.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = size;
            target.AddComponent<NonPhysicsCoalTarget>();
            var use = target.AddComponent<ItemUseTarget>();
            var interactable = new GameObject("interactable");
            interactable.layer = useLayer;
            interactable.transform.SetParent(target.transform, false);
            var useTrigger = interactable.AddComponent<BoxCollider>();
            useTrigger.isTrigger = true;
            useTrigger.size = size;
            RedirectTarget(interactable.AddComponent<ItemUseRedirect>()) = use;
            use.targetColliders = new Collider[] { trigger, useTrigger };
            return target;
        }

        // The layers of the stock S282 tender's coal pile and of its
        // interactable trigger, which the shovel loads from.
        private static (int Pile, int Use) PileLayers()
        {
            if (pileLayers.HasValue)
                return pileLayers.Value;
            var layers = (Pile: LayerMask.NameToLayer("Train_Interior"), Use: LayerMask.NameToLayer("Interactable"));
            if (DV.Globals.G.Types.TryGetLivery("LocoS282B", out var tender) && tender.prefab != null)
            {
                var pile = tender.prefab.GetComponentsInChildren<ShovelCoalPile>(true).FirstOrDefault();
                if (pile != null)
                {
                    layers.Pile = pile.gameObject.layer;
                    var child = pile.GetComponentsInChildren<Collider>(true).FirstOrDefault(c => c.gameObject != pile.gameObject);
                    if (child != null)
                        layers.Use = child.gameObject.layer;
                }
            }
            pileLayers = layers;
            Main.Mod?.Logger.Log($"Flue sand boxes as the S282 tender's coal pile: pile on '{LayerMask.LayerToName(layers.Pile)}' ({layers.Pile}), " +
                                 $"its interactable on '{LayerMask.LayerToName(layers.Use)}' ({layers.Use})");
            return layers;
        }

        private static Material PlainCrate() =>
            plainCrate != null ? plainCrate : plainCrate = new Material(Shader.Find("Standard")) { color = Crate };

        // The sand: a flat bed of fine grains, drawn with the fire bed's shader
        // as all ash in the sand's color; flat-colored without the effects.
        private void SandSurface(Vector3 at, Vector2 size)
        {
            var sheet = new GameObject("sand").transform;
            sheet.SetParent(transform, false);
            sheet.localPosition = at;
            var mesh = new Mesh { name = "RealisticFiring flue sand" };
            surfaceMesh = mesh;
            mesh.vertices = new[]
            {
                new Vector3(-size.x / 2f, 0f, -size.y / 2f), new Vector3(size.x / 2f, 0f, -size.y / 2f),
                new Vector3(-size.x / 2f, 0f, size.y / 2f), new Vector3(size.x / 2f, 0f, size.y / 2f),
            };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, size.y / size.x), new Vector2(1f, size.y / size.x) };
            // R heat, G green coal, B ash, A clinker: all ash.
            mesh.colors = Enumerable.Repeat(new Color(0f, 0f, 1f, 0f), 4).ToArray();
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            sheet.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            sheet.gameObject.AddComponent<MeshRenderer>().sharedMaterial = SandMaterial();
        }

        private static Material SandMaterial()
        {
            if (sandMaterial != null)
                return sandMaterial;
            var bed = PlumeAssets.FireboxPrefab != null ? PlumeAssets.FireboxPrefab.transform.Find("Bed") : null;
            var source = bed != null ? bed.GetComponent<MeshRenderer>() : null;
            if (source != null && source.sharedMaterial != null)
            {
                sandMaterial = new Material(source.sharedMaterial) { name = "RealisticFiring flue sand" };
                sandMaterial.SetColor("_AshColor", Sand);
                sandMaterial.SetFloat("_Tiling", GrainsAcross);
                sandMaterial.SetFloat("_GlowStrength", 0f);
                sandMaterial.SetColor("_AimColor", new Color(0f, 0f, 0f, 0f));
            }
            else
            {
                sandMaterial = new Material(Shader.Find("Standard")) { name = "RealisticFiring flue sand", color = Sand };
            }
            return sandMaterial;
        }

        public float Available => Accessible ? Mathf.Max(0f, sand.amountReadOut.Value) : 0f;

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
            if (!Accessible)
                return 0f;
            var back = Mathf.Min(Mathf.Min(Loaded, Mathf.Max(0f, kg)), Mathf.Max(0f, sand.capacity - sand.amountReadOut.Value));
            Loaded -= back;
            if (back > 0f && sand != null)
                sand.refillExtIn.Value = back;
            return back;
        }
    }

    /// <summary>
    /// On an oil burner, sand goes in through the open peep hole with the door
    /// shut: to the stock shovel the door is as open as it needs while the
    /// shovel carries sand and the peep hole is open.
    /// </summary>
    [HarmonyPatch(typeof(ShovelNonPhysicalCoal), "IsFireboxDoorOpen")]
    internal static class SandThroughPeepHolePatch
    {
        private static void Postfix(FireboxSimController fireboxController, ref bool __result)
        {
            if (__result || !(FlueSandBox.Loaded > 0f) || fireboxController == null)
                return;
            var car = TrainCar.Resolve(fireboxController.gameObject);
            if (RealisticOilFirebox.TryGet(car, out var oil) && oil.PeepHole >= 0.5f)
                __result = true;
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

    /// <summary>
    /// A shovel carrying flue sand shows it: the game draws a load as coal
    /// lumps on the blade, switched on and off as it fills and empties, and
    /// while the load is sand they wear the sand box's sand color.
    /// </summary>
    [HarmonyPatch(typeof(ShovelNonPhysicalCoal), "UpdateLoadedCoal")]
    internal static class SandOnShovelPatch
    {
        private static readonly AccessTools.FieldRef<ShovelNonPhysicalCoal, GameObject[]> Loads =
            AccessTools.FieldRefAccess<ShovelNonPhysicalCoal, GameObject[]>("loadedCoalVisuals");

        private static void Postfix(ShovelNonPhysicalCoal __instance)
        {
            var loads = Loads(__instance);
            if (loads == null || loads.Length == 0)
                return;
            var look = __instance.GetComponent<ShovelSandLook>() ?? __instance.gameObject.AddComponent<ShovelSandLook>();
            look.Show(loads, FlueSandBox.Loaded > 0f);
        }
    }

    /// <summary>A shovel's load lumps in coal's materials or sand's.</summary>
    internal sealed class ShovelSandLook : MonoBehaviour
    {
        // Sand's version of each coal material the lumps wear, made once.
        private static readonly Dictionary<Material, Material> SandOf = new Dictionary<Material, Material>();

        private readonly Dictionary<Renderer, Material[]> coal = new Dictionary<Renderer, Material[]>();
        private bool sand;

        public void Show(GameObject[] loads, bool asSand)
        {
            if (asSand == sand && coal.Count > 0)
                return;
            sand = asSand;
            foreach (var load in loads)
            {
                if (load == null)
                    continue;
                foreach (var renderer in load.GetComponentsInChildren<Renderer>(true))
                {
                    if (!coal.TryGetValue(renderer, out var own))
                        coal[renderer] = own = renderer.sharedMaterials;
                    renderer.sharedMaterials = asSand ? own.Select(Sanded).ToArray() : own;
                }
            }
        }

        // The coal's material made sand: its lumps' shape and relief kept,
        // their color the box's sand, dull.
        private static Material Sanded(Material material)
        {
            if (material == null)
                return null;
            if (SandOf.TryGetValue(material, out var sanded) && sanded != null)
                return sanded;
            sanded = new Material(material) { name = material.name + " (flue sand)" };
            if (sanded.HasProperty("_MainTex"))
                sanded.SetTexture("_MainTex", null);
            if (sanded.HasProperty("_Color"))
                sanded.SetColor("_Color", FlueSandBox.Sand);
            if (sanded.HasProperty("_Glossiness"))
                sanded.SetFloat("_Glossiness", 0.05f);
            if (sanded.HasProperty("_Metallic"))
                sanded.SetFloat("_Metallic", 0f);
            if (sanded.HasProperty("_EmissionColor"))
                sanded.SetColor("_EmissionColor", Color.black);
            SandOf[material] = sanded;
            return sanded;
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
