using System;
using DV;
using DV.CabControls;
using DV.Customization;
using DV.Customization.Gadgets;
using DV.Interaction;
using DV.Utils;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RealisticFiring.Items
{
    /// <summary>A saved stock gadget item, with a separate mounted form and a shared page state.</summary>
    internal static class FiringTablet
    {
        public const string PrefabName = "RealisticFiring_FiringTablet";
        public const string DisplayName = "Firing tablet";
        internal const string SourcePrefab = "Clinometer";
        // The stock gadget body and nested-control layers, respectively.
        internal const int BodyLayer = 13, ControlLayer = 22;
        internal const float Thickness = 0.016f;
        internal static readonly Vector3 Size = new Vector3(0.34f, 0.30f, Thickness);
        private static GameObject template;
        private static bool buildFailed;

        public static GameObject Template
        {
            get
            {
                if (template == null && !buildFailed)
                {
                    template = Build();
                    buildFailed = template == null;
                }
                return template;
            }
        }

        private static GameObject Build()
        {
            GameObject holder = null;
            try
            {
                return BuildTemplate(ref holder);
            }
            catch (Exception error)
            {
                // A changed stock prefab must not abort the saved-item loader.
                if (holder != null) Object.Destroy(holder);
                Main.Mod?.Logger.Warning("Cannot build the firing tablet; saved tablets will be skipped: " + error);
                return null;
            }
        }

        private static GameObject BuildTemplate(ref GameObject holder)
        {
            var source = Resources.Load<GameObject>(SourcePrefab);
            if (source == null)
            {
                Main.Mod?.Logger.Warning("No '" + SourcePrefab + "' prefab to make the firing tablet from");
                return null;
            }
            holder = new GameObject("[RealisticFiring tablet templates]");
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);
            var item = Object.Instantiate(source, holder.transform, false);
            item.name = PrefabName;
            item.transform.localScale = Vector3.one;
            FiringTabletSetup.RemoveStockModel(item.transform);
            foreach (var collider in item.GetComponents<Collider>())
                Object.DestroyImmediate(collider);
            var box = item.AddComponent<BoxCollider>();
            box.size = Size;
            var spec = item.GetComponent<DV.CabControls.Spec.Item>();
            spec.colliderGameObjects = new[] { item };
            spec.rigidbodyMass = 1.4f;
            spec.allowPlayerRotationXAxisVr = true;
            spec.preventNestedGrabWhenUnGrabbed = false;
            var inventory = item.GetComponent<InventoryItemSpec>();
            AccessTools.Field(typeof(InventoryItemSpec), "itemPrefabName").SetValue(inventory, PrefabName);
            inventory.NameOverride = DisplayName;
            inventory.IsEssential = false;
            inventory.ImmuneToDumpster = false;
            inventory.PreviewBounds = new Bounds(Vector3.zero, Size);
            inventory.PreviewPrefab = FiringTabletBody.Build(holder.transform).gameObject;
            inventory.PreviewPrefab.name = "Firing tablet preview";
            FiringTabletBody.SetIcons(inventory);
            FiringTabletBody.Build(item.transform);

            var mounted = new GameObject("RealisticFiring_FiringTablet_Gadget");
            mounted.transform.SetParent(holder.transform, false);
            var gadget = mounted.AddComponent<FiringTabletGadget>();
            ConfigureGadget(gadget);
            FiringTabletBody.Build(mounted.transform);
            mounted.AddComponent<BoxCollider>().size = Size;
            mounted.layer = BodyLayer;
            mounted.AddComponent<FiringTabletScreen>();

            var itemGadget = item.GetComponent<GadgetItem>();
            AccessTools.Field(typeof(GadgetItem), "gadgetPrefab").SetValue(itemGadget, gadget);
            AccessTools.Field(typeof(GadgetItem), "onlyPlaceInOneAxis").SetValue(itemGadget, true);
            item.AddComponent<FiringTabletScreen>();
            item.AddComponent<FiringTabletDescription>();
            item.AddComponent<FiringTabletHold>();
            Main.Mod?.Logger.Log("Firing tablet built from the stock Clinometer item; uses stock gadget placement and saves");
            return item;
        }

        private static void ConfigureGadget(GadgetBase gadget)
        {
            var bounds = new Bounds(Vector3.zero, Size);
            bounds.Encapsulate(new Vector3(0f, 0f, FiringTabletScreen.ButtonFront));
            AccessTools.Field(typeof(GadgetBase), "boundsCenter").SetValue(gadget, bounds.center);
            AccessTools.Field(typeof(GadgetBase), "boundsSize").SetValue(gadget, bounds.size);
            AccessTools.Field(typeof(GadgetBase), "requiredMountPoints").SetValue(gadget, 2);
            AccessTools.Field(typeof(GadgetBase), "removalMethod").SetValue(gadget,
                GadgetBase.GadgetRemovalMethod.EmptyHand | GadgetBase.GadgetRemovalMethod.Remover);
            AccessTools.Field(typeof(GadgetBase), "vrIgnoreColliders").SetValue(gadget, new Collider[0]);
            AccessTools.Field(typeof(GadgetBase), "highlightMeshes").SetValue(gadget, new MeshFilter[0]);
            // A self-powered display; no diesel electrics or soldering requirement.
            AccessTools.Field(typeof(TrainCarCustomization.TrainCarCustomizerBase), "requirements").SetValue(gadget,
                new TrainCarCustomization.TrainCarCustomizerBase.TrainCarRequirements
                {
                    trainCarPresence = TrainCarCustomization.TrainCarCustomizerBase.CustomizerTrainCarRequirements.RequireInterior,
                });
        }

        public static bool RegisterAsGameItem()
        {
            var items = Globals.G?.Items?.items;
            var spec = Template != null ? Template.GetComponent<InventoryItemSpec>() : null;
            if (items == null || spec == null)
                return false;
            if (!items.Contains(spec))
                items.Add(spec);
            return true;
        }

        public static string GiveToPlayer()
        {
            var source = Template;
            var camera = PlayerManager.ActiveCamera;
            if (camera == null)
                return "No tablet: load a game first";
            if (source == null)
                return "The firing tablet could not be built; see the mod log";
            var item = Object.Instantiate(source, camera.transform.position + camera.transform.forward * 0.6f, camera.transform.rotation);
            item.name = PrefabName;
            item.GetComponent<InventoryItemSpec>().BelongsToPlayer = true;
            var impl = item.GetComponent<ItemBase>();
            if (impl == null)
            {
                Object.Destroy(item);
                return "The firing tablet could not become an inventory item";
            }
            SingletonBehaviour<StorageController>.Instance.AddItemToWorldStorage(impl);
            return "A firing tablet is in front of you; pick it up or fit it to a large gadget mount";
        }
    }

    internal sealed class FiringTabletGadget : GadgetBase
    {
        internal const int FiremanPage = 3;
        internal const int Pages = 4;
        private int page;
        internal int Page
        {
            get => page;
            set => page = ValidPage(value);
        }

        internal static int ValidPage(int value) => Math.Max(0, Math.Min(Pages - 1, value));

        internal static int SavedPage(JObject src)
        {
            var token = src?["rfTabletPage"];
            return token?.Type == JTokenType.Integer
                ? (int)Math.Max(0L, Math.Min(Pages - 1L, token.Value<long>())) : 0;
        }

        public override void SaveDataRequested(JObject dst)
        {
            base.SaveDataRequested(dst);
            dst["rfTabletPage"] = Page;
        }

        public override void SaveDataLoaded(JObject src)
        {
            base.SaveDataLoaded(src);
            Page = SavedPage(src);
        }
    }

    internal sealed class FiringTabletDescription : MonoBehaviour, IInventoryItemLocalizer
    {
        private void Awake() => GetComponent<InventoryItemSpec>().NameOverride = FiringTablet.DisplayName;
        public string GetNameParam() => FiringTablet.DisplayName;
        public string GetCustomDescription() => "A portable firing display. Fire, Water, and Engine show the same readings as F6; Fireman has his orders and the engineer's calls. " +
            "Press the screen controls to change pages or give orders (mouse mode while held on desktop). While held, it reads the engine you are tending; " +
            "on a large gadget mount, it reads that engine. Pick it up again with an empty hand. No wiring is needed.";
    }

    /// <summary>Keeps the readable desktop screen beside the placement ray, independently of zoom.</summary>
    internal sealed class FiringTabletHold : MonoBehaviour, ICustomNonVRGrabAnchor
    {
        public (Vector3 localPos, Quaternion localRot) GetGrabAnchor()
        {
            var camera = PlayerManager.ActiveCamera;
            var grabber = GetComponent<GrabHandlerItem>()?.GetGrabber();
            // The stock inventory also asks for this anchor when positioning an inventory drop.
            var attach = grabber?.Cursor?.Rig?.GetAttachPoint()
                ?? PlayerManager.PlayerTransform?.GetComponentInChildren<IPlayerRig>()?.GetAttachPoint();
            if (camera == null || attach == null)
                return (Vector3.zero, Quaternion.identity);
            var fov = GamePreferences.Get<float>(Preferences.FieldOfView);
            // The live attach pose includes stash and placement animations.
            // A hotbar switch grabs during them, so cancel only the resting offset.
            var sway = attach.parent != null ? attach.parent.GetComponentInChildren<ItemSway>() : null;
            var origin = sway != null
                ? new Vector3(sway.itemOffsetXY.x, sway.itemOffsetXY.y, sway.itemDistance.Evaluate(fov))
                : Quaternion.Inverse(attach.rotation) * (attach.position - camera.transform.position);
            return (CameraOffset(fov, camera.aspect) - origin, Quaternion.identity);
        }

        internal static Vector3 CameraOffset(float fieldOfView, float aspect)
        {
            // Holding the shell to the right clears the placement ray without
            // putting the screen as far into the backhead as a grip below center.
            var angle = Math.Max(30f, Math.Min(120f, fieldOfView)) * Math.PI / 360d;
            const float right = 0.21f;
            var halfWidth = right + FiringTablet.Size.x * 0.5d + 0.04d;
            var halfHeight = Math.Max(0.21d, halfWidth / Math.Max(1d, aspect));
            var distance = (float)Math.Max(0.34d, halfHeight / Math.Tan(angle)) - FiringTabletScreen.ButtonFront;
            return new Vector3(right, -0.035f, distance);
        }
    }
}
