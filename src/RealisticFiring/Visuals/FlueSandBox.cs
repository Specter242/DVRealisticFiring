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
        // A scoopful of sand, in kg: "about a quart at a time" (K02 p. 46).
        public const float Scoopful = 1.5f;

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

        private ResourceContainer sand;
        private SimulationFlow boundFlow;
        private bool usesTender;
        private TrainCar carrier;
        private ControlImplBase cover;
        private Collider[] pickupColliders;
        private Mesh surfaceMesh;

        public TrainCar Car { get; private set; }

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

 internal static FlueSandBox BuildOnModel(TrainCar car, Transform parent, Vector3 center, Vector3 size, float sandBelowRim, Material litBasis)
        {
            var root = new GameObject("flue sand box");
            root.SetActive(false);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = center;
            root.transform.localRotation = Quaternion.identity;
            var box = root.AddComponent<FlueSandBox>();
            if (!box.BindEngine(car))
            {
                Object.Destroy(root);
                return null;
            }
            // The sand in it, under its open rim, as the mod's own sand box shows it.
            box.SandSurface(new Vector3(0f, size.y / 2f - sandBelowRim, 0f), new Vector2(size.x - 2f * Wall, size.z - 2f * Wall),
                LitSandMaterial(litBasis));
            box.BuildPickup(Vector3.zero, size + new Vector3(0.02f, 0.04f, 0.02f));
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
            // Where the scoop's working animation takes its spout: into the sand, tipped down.
            var pose = new GameObject("[animation_pose_target]").transform;
            pose.SetParent(transform, false);
            pose.localPosition = center + new Vector3(0f, size.y * 0.3f, 0f);
            pose.localRotation = Quaternion.Euler(35f, 0f, 0f);
            gameObject.AddComponent<HoverName>().Source = () => $"Flue sand: {Available:F0} kg (fill the sand scoop here, a quart at a time; throw it in through the peephole under a hard pull)";
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
        /// The sand scoop's target into an oil burner's firebox
        /// (<see cref="FlueSandTarget"/>), built as the sand box is (the stock
        /// coal targets' way: a target on the cab's layer, and an interactable
        /// child redirecting to it, which the item-use click sees), for a box
        /// of <paramref name="size"/> m at a pose, its +z into the firebox. The
        /// scoop's working animation takes its spout to the box's far face.
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
            target.AddComponent<FlueSandTarget>();
            var use = target.AddComponent<ItemUseTarget>();
            var pose = new GameObject("[animation_pose_target]").transform;
            pose.SetParent(target.transform, false);
            pose.localPosition = new Vector3(0f, 0f, size.z / 2f - 0.01f);
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
        private void SandSurface(Vector3 at, Vector2 size, Material material = null)
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
            sheet.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material != null ? material : SandMaterial();
        }

        private static Material litSand;
        private static Texture2D sandGrains;

        // Sand drawn with a lit material (a copy of <paramref name="basis"/>, Standard-like): fine grains, matte.
        internal static Material LitSandMaterial(Material basis)
        {
            if (litSand != null)
                return litSand;
            if (basis == null)
                return null;
            litSand = new Material(basis) { name = "RealisticFiring flue sand (lit)" };
            void Texture(string name, Texture value)
            {
                if (litSand.HasProperty(name))
                {
                    litSand.SetTexture(name, value);
                    litSand.SetTextureScale(name, Vector2.one);
                    litSand.SetTextureOffset(name, Vector2.zero);
                }
            }
            Texture("_MainTex", SandGrains());
            Texture("_BumpMap", null);
            Texture("_MetallicGlossMap", null);
            Texture("_OcclusionMap", null);
            Texture("_DetailAlbedoMap", null);
            Texture("_DetailNormalMap", null);
            Texture("_EmissionMap", null);
            litSand.DisableKeyword("_NORMALMAP");
            litSand.DisableKeyword("_METALLICGLOSSMAP");
            litSand.DisableKeyword("_DETAIL_MULX2");
            litSand.DisableKeyword("_EMISSION");
            if (litSand.HasProperty("_Color"))
                litSand.SetColor("_Color", Color.white);
            if (litSand.HasProperty("_Metallic"))
                litSand.SetFloat("_Metallic", 0f);
            if (litSand.HasProperty("_Glossiness"))
                litSand.SetFloat("_Glossiness", 0.04f);
            if (litSand.HasProperty("_EmissionColor"))
                litSand.SetColor("_EmissionColor", Color.black);
            return litSand;
        }

        // Dry sand's face: grains a texel or two across in the sand's color, a few darker and paler.
        private static Texture2D SandGrains()
        {
            if (sandGrains != null)
                return sandGrains;
            const int n = 256;
            var random = new System.Random(282);
            var pixels = new Color32[n * n];
            for (var i = 0; i < pixels.Length; i++)
            {
                var grain = (float)random.NextDouble();
                var tone = 0.86f + 0.22f * grain + (grain > 0.97f ? -0.35f : 0f);
                var tint = (float)random.NextDouble() * 0.06f - 0.03f;
                pixels[i] = new Color(Mathf.Clamp01(Sand.r * tone + tint), Mathf.Clamp01(Sand.g * tone), Mathf.Clamp01(Sand.b * tone - tint), 1f);
            }
            sandGrains = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "RealisticFiring flue sand grains", wrapMode = TextureWrapMode.Repeat };
            sandGrains.SetPixels32(pixels);
            sandGrains.Apply(true, true);
            return sandGrains;
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

        /// <summary>Takes up to <paramref name="kg"/> of the loco's sand onto the scoop; returns what it took.</summary>
        public float Scoop(float kg)
        {
            var taken = Mathf.Min(Available, Mathf.Max(0f, kg));
            if (taken > 0f && sand != null)
                sand.consumeExtIn.Value = taken;
            return taken;
        }

        /// <summary>Why there is no sand to scoop, for the hover.</summary>
        public string Why => !SupplyConnected ? "Couple the tender to the engine for its sand"
            : !Accessible ? "Open the sandbox lid"
            : "No sand left: fill the engine's sandboxes at a sand tower";
    }

    /// <summary>
    /// An oil burner's firebox target for flue sand: the sand scoop full of
    /// sand, used here with the peephole open (or the door), throws it in
    /// (<see cref="Items.SandScoopUse"/>). Built in front of the peephole
    /// and where the cab's own coal targets were (<see cref="FlueSandBox.BuildFireboxTarget"/>).
    /// </summary>
    internal sealed class FlueSandTarget : MonoBehaviour
    {
        // The peephole open this far takes the scoop's spout.
        private const float PeepOpen = 0.5f;

        private TrainCar car;

        internal TrainCar Car => car != null ? car : car = TrainCar.Resolve(gameObject);

        internal bool TryGetFirebox(out RealisticOilFirebox oil) => RealisticOilFirebox.TryGet(Car, out oil);

        /// <summary>Whether sand goes in: the peephole open, or the fire door open as far as for a shovel.</summary>
        internal static bool IsOpen(RealisticOilFirebox oil) =>
            oil != null && (oil.PeepHole >= PeepOpen || oil.DoorOpening >= ShovelNonPhysicalCoal.REQUIRED_DOOR_OPENED_PERCENTAGE_NON_VR);
    }
}
