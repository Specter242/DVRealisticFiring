using System;
using DV;
using DV.Booklets.Rendered;
using DV.CabControls;
using DV.Utils;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RealisticFiring.Items
{
    /// <summary>The selected O1 manual on the stock portrait booklet, including its grips and page saves.</summary>
    internal static class FiringManual
    {
        internal const string PrefabName = "RealisticFiring_FiringManual";
        internal const string DisplayName = "Firing manual";
        internal const string SourcePrefab = "VehicleCatalog";
        internal const float PaperScale = 0.8f;
        internal const float PageWidth = 0.297f * PaperScale, PageHeight = 0.42f * PaperScale;
        private static GameObject template;
        private static bool failed;

        internal static GameObject Template
        {
            get
            {
                if (template == null && !failed)
                {
                    template = Build();
                    failed = template == null;
                }
                return template;
            }
        }

        private static GameObject Build()
        {
            GameObject holder = null;
            try
            {
                var pages = FiringManualAssets.Pages;
                var source = Resources.Load<GameObject>(SourcePrefab);
                if (source == null || pages == null)
                    return null;
                holder = new GameObject("[RealisticFiring manual template]");
                holder.SetActive(false);
                Object.DontDestroyOnLoad(holder);
                var item = Object.Instantiate(source, holder.transform, false);
                item.name = PrefabName;
                // Prevent the catalog renderer from replacing our pages when this clone wakes.
                foreach (var generator in item.GetComponents<RenderedTexturesBase>())
                    Object.DestroyImmediate(generator);
                var book = item.GetComponent<PageBook>();
                book.pageTextures = pages;
                book.currentPage = 0;
                book.generateOnStart = true;
                book.destroyTemplate = true;
                // The stock catalog's paper is slightly stretched; use a uniform paper scale.
                book.pageTemplate.transform.localScale = Vector3.one * PaperScale;
                var inventory = item.GetComponent<InventoryItemSpec>();
                AccessTools.Field(typeof(InventoryItemSpec), "itemPrefabName").SetValue(inventory, PrefabName);
                inventory.NameOverride = DisplayName;
                inventory.IsEssential = false;
                inventory.ImmuneToDumpster = false;
                inventory.PreviewBounds = new Bounds(Vector3.zero, new Vector3(PageWidth, 0.025f, PageHeight));
                inventory.ItemIconSprite = inventory.ItemIconSpriteSimple = inventory.ItemIconSpriteDropped = FiringManualAssets.Icon;
                var preview = Object.Instantiate(inventory.PreviewPrefab, holder.transform, false);
                preview.name = "Firing manual preview";
                foreach (var renderer in preview.GetComponentsInChildren<Renderer>(true))
                {
                    var material = new Material(renderer.sharedMaterial) { mainTexture = pages[0] };
                    renderer.sharedMaterial = material;
                }
                inventory.PreviewPrefab = preview;
                var box = item.GetComponent<BoxCollider>();
                box.size = new Vector3(PageWidth, box.size.y, PageHeight);
                // Copying VehicleCatalog retains the proven portrait desktop/placer/VR anchors.
                item.GetComponent<DV.CabControls.Spec.Item>().preventNestedGrabWhenUnGrabbed = false;
                item.AddComponent<FiringManualDescription>();
                FiringManualControl.Install(item);
                item.AddComponent<FiringManualLinks>();
                Main.Mod?.Logger.Log($"Firing manual built from the portrait VehicleCatalog, with {FiringManualContents.PageCount} pages and linked contents");
                return item;
            }
            catch (Exception error)
            {
                if (holder != null) Object.Destroy(holder);
                Main.Mod?.Logger.Warning("Cannot build the firing manual; saved manuals will be skipped: " + error);
                return null;
            }
        }

        internal static bool RegisterAsGameItem()
        {
            var items = Globals.G?.Items?.items;
            var spec = Template != null ? Template.GetComponent<InventoryItemSpec>() : null;
            if (items == null || spec == null) return false;
            if (!items.Contains(spec)) items.Add(spec);
            return true;
        }

        internal static string GiveToPlayer()
        {
            var player = PlayerManager.PlayerTransform;
            if (player == null) return "No manual: load a game first";
            var source = Template;
            if (source == null) return "The firing manual could not be built; see the mod log";
            var forward = Vector3.ProjectOnPlane(player.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.5f) forward = Vector3.forward;
            // The printed top is local -z: place it away from the player, as in the brake booklet fix.
            var copy = Object.Instantiate(source, player.position + forward * 0.6f + Vector3.up * 1.2f, Quaternion.LookRotation(-forward));
            copy.name = PrefabName;
            copy.GetComponent<InventoryItemSpec>().BelongsToPlayer = true;
            SingletonBehaviour<StorageController>.Instance.AddItemToWorldStorage(copy.GetComponent<ItemBase>());
            return "The firing manual is in front of you; its contents are just inside the cover";
        }
    }

    internal sealed class FiringManualDescription : MonoBehaviour, IInventoryItemLocalizer
    {
        private void Awake() => GetComponent<InventoryItemSpec>().NameOverride = FiringManual.DisplayName;
        public string GetNameParam() => FiringManual.DisplayName;
        public string GetCustomDescription() => "An illustrated firing manual. Turn the cover to the contents and select a subject to open its page. " +
            "Use mouse mode to select a contents row while holding it on desktop. Normal booklet page controls still work; your page is saved.";
    }
}
