using System;
using System.Collections.Generic;
using System.Linq;
using DV.CabControls;
using DV.InventorySystem;
using DV.Items;
using DV.ThingTypes;
using DV.Utils;
using HarmonyLib;
using RealisticFiring.Visuals;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RealisticFiring.Items
{
 internal static class SandScoop
    {
        public const string PrefabName = "RealisticFiring_SandScoop";
        public const string DisplayName = "Sand scoop";

        /// <summary>A scoopful, kg: "about a quart at a time" (K02 p. 46).</summary>
        public const float Scoopful = FlueSandBox.Scoopful;

        // Sheet steel and a short ash handle, kg.
        private const float Mass = 0.6f;

        // A scoop farther than this from its cab's rest, m, not in hand or carried, comes back.
        internal const float ComeBackBeyond = 25f;

        private const string SourcePrefab = "shovel";
        private static readonly string[] ShovelComponents = { "ShovelAudio", "Shovel", "ShovelNonPhysicalCoal", "ShopRestocker" };
        private static readonly string[] ShovelChildren = { "scoop pivot", "[spawn anchors]", "[visual coal anchor]" };


        private static GameObject template;
        private static bool noShovel;

        // Each oil burner's own scoop.
        private static readonly Dictionary<TrainCar, SandScoopUse> Scoops = new Dictionary<TrainCar, SandScoopUse>();

        /// <summary>The dormant template, built on first use and again if destroyed; null without the shovel.</summary>
        public static GameObject Template
        {
            get
            {
                if (template == null && !noShovel)
                {
                    template = Build();
                    noShovel = template == null;
                }
                return template;
            }
        }

        private static GameObject Build()
        {
            var shovel = Resources.Load<GameObject>(SourcePrefab);
            if (shovel == null)
            {
                Main.Mod.Logger.Warning($"No '{SourcePrefab}' prefab to make the sand scoop from");
                return null;
            }
            var holder = new GameObject("[RealisticFiring sand scoop template]");
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);
            var scoop = Object.Instantiate(shovel, holder.transform, false);
            scoop.name = PrefabName;

            var spec = scoop.GetComponent<InventoryItemSpec>();
            AccessTools.Field(typeof(InventoryItemSpec), "itemPrefabName").SetValue(spec, PrefabName);
            spec.NameOverride = DisplayName;
            spec.IsEssential = false;
            SetIcon(spec);

            // The shovel's scoop trigger's layer, which a VR shovel's scoop uses into the coal: the scoop's tip takes it.
            var scoopLayer = scoop.transform.Find("scoop pivot")?.GetComponentInChildren<Collider>(true)?.gameObject.layer;
            foreach (var name in ShovelComponents)
            {
                foreach (var component in scoop.GetComponents<MonoBehaviour>().Where(c => c != null && c.GetType().Name == name).ToList())
                    Object.DestroyImmediate(component);
            }
            foreach (var name in ShovelChildren)
            {
                var child = scoop.transform.Find(name);
                if (child != null)
                    Object.DestroyImmediate(child.gameObject);
            }

            // Its shape and look on the shovel's model.
            var model = scoop.transform.Find("model");
            var renderer = model != null ? model.GetComponent<MeshRenderer>() : null;
            if (model != null)
            {
                foreach (var lod in model.GetComponents<LODGroup>().ToList())
                    Object.DestroyImmediate(lod);
                model.localPosition = Vector3.zero;
                model.localRotation = Quaternion.identity;
                model.localScale = Vector3.one;
                model.GetComponent<MeshFilter>().sharedMesh = SandScoopMesh.Create();
            }
            // It wears the shovel's own material, its UVs in the shovel's steel and wood (SandScoopMesh): the game's look.
            var basis = renderer != null ? renderer.sharedMaterial : null;

            // The sand it carries, shown when it does.
            var sand = new GameObject(SandScoopUse.SandName);
            sand.transform.SetParent(scoop.transform, false);
            sand.AddComponent<MeshFilter>().sharedMesh = SandScoopMesh.CreateSand();
            var sandRenderer = sand.AddComponent<MeshRenderer>();
            sandRenderer.sharedMaterial = FlueSandBox.LitSandMaterial(basis);
            sandRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sand.SetActive(false);

            // Held by the handle and the body.
            Reshape(scoop.transform.Find("stick"), new Vector3(0f, 0f, (SandScoopMesh.HandleStart + SandScoopMesh.BodyStart) / 2f),
                SandScoopMesh.BodyStart - SandScoopMesh.HandleStart, SandScoopMesh.HandleRadius + 0.004f, 2);
            Reshape(scoop.transform.Find("handle"), new Vector3(0f, 0f, (SandScoopMesh.BodyStart + SandScoopMesh.Tip) / 2f),
                SandScoopMesh.Tip - SandScoopMesh.BodyStart, SandScoopMesh.BackRadius, 2);
            var item = scoop.GetComponent<DV.CabControls.Spec.Item>();
            if (item != null)
            {
                item.rigidbodyMass = Mass;
                // A small thing in one hand, as a piece of firewood is: not a shovel, so no shovel mount, hanger, or belt.
                item.controllerAttachMethod = DV.CabControls.Spec.ItemControllerAttachMethod.ReparentToController;
                item.precisionGrab = true;
                item.itemUseApproach = DV.CabControls.Spec.ItemUseApproach.OneShotNonVROnly;
                item.interactionPriority = 1;
                item.preventNestedGrabWhenUnGrabbed = false;
                item.allowedSnapPointTypes = DV.CabControls.Spec.SnapPointTypes.None;
                item.coalAmount = 0f;
                item.colliderGameObjects = new[] { "stick", "handle" }
                    .Select(n => scoop.transform.Find(n))
                    .Where(t => t != null)
                    .Select(t => t.gameObject)
                    .ToArray();
                // Never saved (it comes with the oil burner): a save neither keeps nor duplicates it.
                item.excludeFromStorageSerialization = (StorageType)Enum.GetValues(typeof(StorageType)).Cast<int>().Aggregate(0, (a, b) => a | b);
            }

            // Carried low in front in the right hand, the scoop ahead, as a split of firewood is.
            var anchor = scoop.GetComponent<FovBasedNonVRGrabAnchor>();
            if (anchor != null)
            {
                anchor.localPosition = new Vector3(0.03f, -0.04f, 0.07f);
                anchor.localRotation = new Vector3(8f, -12f, 0f);
                anchor.fovRotationXOffset = AnimationCurve.Constant(30f, 120f, 0f);
                anchor.fovDistanceOffset = AnimationCurve.Constant(30f, 120f, 0f);
            }

            // Its own shape when set down (holding R) and in the game's icon, not the shovel's.
            spec.PreviewBounds = SandScoopMesh.Bounds;
            var preview = new GameObject("Sand scoop preview");
            preview.transform.SetParent(holder.transform, false);
            var previewModel = new GameObject("model");
            previewModel.transform.SetParent(preview.transform, false);
            previewModel.AddComponent<MeshFilter>().sharedMesh = model != null ? model.GetComponent<MeshFilter>().sharedMesh : SandScoopMesh.Create();
            previewModel.AddComponent<MeshRenderer>().sharedMaterials = renderer != null ? renderer.sharedMaterials : new Material[0];
            spec.PreviewPrefab = preview;
            // Its icon rendered from that shape in the shovel's own view, a scoopful in it; the drawn one if that fails.
            RenderIcon(scoop);

            // Where the working animation puts it: its lip. In VR, the lip's touch fills and empties it.
            var tip = new GameObject(SandScoopUse.TipName);
            tip.transform.SetParent(scoop.transform, false);
            tip.transform.localPosition = new Vector3(0f, -0.015f, SandScoopMesh.Tip - 0.02f);
            var touch = tip.AddComponent<SphereCollider>();
            touch.isTrigger = true;
            touch.radius = 0.045f;
            if (scoopLayer is int layer)
                tip.layer = layer;

            scoop.AddComponent<SandScoopUse>();
            Main.Mod.Logger.Log($"Sand scoop made from the '{SourcePrefab}' prefab: components " +
                                string.Join(", ", scoop.GetComponents<Component>().Select(c => c.GetType().Name)) +
                                $"; never saved ({item?.excludeFromStorageSerialization})");
            return scoop;
        }

        private static void Reshape(Transform holder, Vector3 center, float height, float radius, int direction)
        {
            var capsule = holder != null ? holder.GetComponent<CapsuleCollider>() : null;
            if (capsule == null)
                return;
            holder.localPosition = Vector3.zero;
            holder.localRotation = Quaternion.identity;
            capsule.center = center;
            capsule.height = height;
            capsule.radius = radius;
            capsule.direction = direction;
        }

        /// <summary>
        /// Sets the scoop's icon rendered from <paramref name="scoop"/>'s own
        /// shape and the shovel's steel and wood, with a scoopful of sand in
        /// it (<see cref="ItemIcons"/>); it keeps the drawn icon if that fails.
        /// </summary>
        internal static void RenderIcon(GameObject scoop)
        {
            var model = IconModel(scoop);
            try
            {
                ItemIcons.Apply(scoop.GetComponent<InventoryItemSpec>(), model, IconView(scoop.GetComponent<InventoryItemSpec>()), PrefabName);
            }
            finally
            {
                if (model != null)
                    Object.DestroyImmediate(model);
            }
        }

        /// <summary>
        /// The scoop's icon view: the shovel's (whose spec it copies), leaned
        /// 35 degrees into a diagonal as the stock hand tools lie and tipped
        /// toward the viewer, its sand showing. Chosen in game among ten
        /// (jobs 3389 and 3390).
        /// </summary>
        internal static Quaternion IconView(InventoryItemSpec spec) => Quaternion.Euler(-15f, 0f, 35f) * ItemIcons.StockView(spec);

        /// <summary>A loose copy of <paramref name="scoop"/>'s body and sand, its sand shown, for an icon; the caller destroys it.</summary>
        internal static GameObject IconModel(GameObject scoop)
        {
            if (scoop == null)
                return null;
            var model = new GameObject("[sand scoop icon model]") { hideFlags = HideFlags.HideAndDontSave };
            foreach (var name in new[] { "model", SandScoopUse.SandName })
            {
                var part = scoop.transform.Find(name);
                var filter = part != null ? part.GetComponent<MeshFilter>() : null;
                var renderer = part != null ? part.GetComponent<MeshRenderer>() : null;
                if (filter == null || renderer == null)
                    continue;
                var copy = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
                copy.transform.SetParent(model.transform, false);
                copy.transform.localPosition = part.localPosition;
                copy.transform.localRotation = part.localRotation;
                copy.transform.localScale = part.localScale;
                copy.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                copy.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
            }
            return model;
        }

        // Its own icon, drawn, if the rendered one fails: a hand scoop, its short handle up to the left, its round body down to the right, sand in its open front.
        private static void SetIcon(InventoryItemSpec spec)
        {
            const int n = 128;
            var pixels = new Color32[n * n];
            var steel = (Color32)new Color(0.66f, 0.66f, 0.68f);
            var edge = (Color32)new Color(0.42f, 0.42f, 0.45f);
            var wood = (Color32)new Color(0.72f, 0.55f, 0.35f);
            var sand = (Color32)new Color(0.84f, 0.74f, 0.52f);
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                // Along the scoop from the handle's butt (0) at the top left to the lip (1) at the bottom right.
                var along = ((x - 10f) + (n - 10f - y)) / (2f * (n - 20f));
                var across = ((x - 10f) - (n - 10f - y)) / Mathf.Sqrt(2f);
                Color32 c = default;
                if (along >= 0f && along < 0.36f && Mathf.Abs(across) < 6f)
                    c = along < 0.3f ? wood : steel;
                else if (along >= 0.36f && along <= 1f)
                {
                    var t = (along - 0.36f) / 0.64f;
                    var half = Mathf.Lerp(26f, 20f, t);
                    // The back rounds off; the front is open, the sand showing inside its rim.
                    var rounded = t < 0.08f ? half * Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow((0.08f - t) / 0.08f, 2f))) : half;
                    var a = Mathf.Abs(across);
                    if (a < rounded)
                        c = a > rounded - 3f || along > 0.98f ? edge : t > 0.3f && a < rounded - 5f ? sand : steel;
                }
                pixels[y * n + x] = c;
            }
            var texture = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "RealisticFiring sand scoop icon" };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f), 100f);
            foreach (var field in new[] { "itemIconSprite", "itemIconSpriteStandard", "itemIconSpriteDropped" })
                AccessTools.Field(typeof(InventoryItemSpec), field)?.SetValue(spec, sprite);
        }

        /// <summary>
        /// Gives <paramref name="car"/> its scoop at a pose by its sand box: a
        /// new one if it has none (or lost it), its own put back if it lies
        /// more than <see cref="ComeBackBeyond"/> away and is not in hand or
        /// carried; otherwise leaves it be. Returns a note for the log when it
        /// placed one. On a multiplayer guest whose items are the host's, the
        /// host's game places them.
        /// </summary>
        internal static string Provide(TrainCar car, Vector3 position, Quaternion rotation)
        {
            if (car == null)
                return null;
            Sweep();
            if (Net.ItemsRemote)
                return null;
            if (Scoops.TryGetValue(car, out var scoop) && scoop != null)
            {
                if (Carried(scoop) || (scoop.transform.position - position).sqrMagnitude <= ComeBackBeyond * ComeBackBeyond)
                    return null;
                Place(scoop.gameObject, position, rotation);
                return "its sand scoop, left behind, is back by the sand box";
            }
            var made = Spawn(position, rotation);
            if (made == null)
                return "no sand scoop (no shovel to make it from)";
            Scoops[car] = made;
            return "a sand scoop by the sand box";
        }

        /// <summary><paramref name="car"/>'s scoop, if it has one now.</summary>
        internal static SandScoopUse Of(TrainCar car) => car != null && Scoops.TryGetValue(car, out var scoop) && scoop != null ? scoop : null;

        // Scoops of engines gone from the world go with them, unless carried.
        private static void Sweep()
        {
            foreach (var car in Scoops.Keys.Where(c => c == null).ToList())
            {
                var scoop = Scoops[car];
                Scoops.Remove(car);
                if (scoop != null && !Carried(scoop))
                    Object.Destroy(scoop.gameObject);
            }
        }

        /// <summary>Whether <paramref name="scoop"/> is in a hand or the player's inventory.</summary>
        internal static bool Carried(SandScoopUse scoop)
        {
            var item = scoop != null ? scoop.GetComponent<ItemBase>() : null;
            if (item == null)
                return false;
            if (item.IsGrabbed())
                return true;
            var inventory = SingletonBehaviour<Inventory>.Instance;
            return inventory != null && inventory.IndexOf(scoop.gameObject) >= 0;
        }

        private static SandScoopUse Spawn(Vector3 position, Quaternion rotation)
        {
            var source = Template;
            if (source == null)
                return null;
            var copy = Object.Instantiate(source, position, rotation);
            copy.name = PrefabName;
            // Nobody's: not in the world's storage, so never swept to Lost & Found.
            copy.GetComponent<InventoryItemSpec>().BelongsToPlayer = false;
            return copy.GetComponent<SandScoopUse>();
        }

        private static void Place(GameObject scoop, Vector3 position, Quaternion rotation)
        {
            scoop.transform.SetPositionAndRotation(position, rotation);
            var body = scoop.GetComponent<Rigidbody>();
            if (body == null)
                return;
            body.position = position;
            body.rotation = rotation;
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }

    /// <summary>
    /// An oil burner cab's place for its sand scoop, by its sand box: a moment
    /// after the cab loads, and every few seconds while it stays loaded, the
    /// scoop is laid out there if the engine has none or left it behind
    /// (<see cref="SandScoop.Provide"/>). The pose is in the engine's frame.
    /// </summary>
    internal sealed class SandScoopRest : MonoBehaviour
    {
        private const float FirstAfter = 1.5f;
        private const float Every = 5f;

        internal TrainCar Car;
        internal Vector3 Position;
        internal Quaternion Rotation = Quaternion.identity;
        private float next;

        /// <summary>Adds the rest under <paramref name="root"/> for <paramref name="car"/>, its pose given in world space now.</summary>
        internal static SandScoopRest Add(GameObject root, TrainCar car, Vector3 worldPosition, Quaternion worldRotation)
        {
            var rest = root.AddComponent<SandScoopRest>();
            rest.Car = car;
            rest.Position = car.transform.InverseTransformPoint(worldPosition);
            rest.Rotation = Quaternion.Inverse(car.transform.rotation) * worldRotation;
            return rest;
        }

        private void OnEnable() => next = Time.time + FirstAfter;

        private void Update()
        {
            if (Time.time < next || Car == null)
                return;
            next = Time.time + Every;
            var note = SandScoop.Provide(Car, Car.transform.TransformPoint(Position), Car.transform.rotation * Rotation);
            if (note != null)
                Main.Mod?.Logger.Log($"{Car.ID}: {note}");
        }
    }
}
