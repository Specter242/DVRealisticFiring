using DV;
using DV.Booklets.Rendered;
using DV.CabControls;
using DV.Utils;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RealisticFiring.Items
{
    /// <summary>
    /// The mod's paper booklets, the firing manual and the firing checklists,
    /// made from the stock portrait <c>VehicleCatalog</c> booklet: its grips,
    /// page turning (<see cref="PageBook"/>), and saved page stay stock; the
    /// pages, name, icon, and preview are the booklet's own.
    /// </summary>
    internal static class PaperBooklet
    {
        internal const string SourcePrefab = FiringManual.SourcePrefab;
        internal const float PaperScale = FiringManual.PaperScale;
        internal const float PageWidth = FiringManual.PageWidth, PageHeight = FiringManual.PageHeight;

        /// <summary>
        /// An inactive copy of the stock catalog refitted as a booklet under
        /// <paramref name="prefabName"/>, inside a new inactive holder kept for
        /// the session; null when the catalog is missing. The caller adds its
        /// own description, and destroys <paramref name="holder"/> if it fails.
        /// </summary>
        internal static GameObject Clone(string holderName, string prefabName, string displayName, Texture[] pages, Sprite icon, out GameObject holder)
        {
            holder = null;
            var source = Resources.Load<GameObject>(SourcePrefab);
            if (source == null || pages == null)
                return null;
            holder = new GameObject(holderName);
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);
            var item = Object.Instantiate(source, holder.transform, false);
            item.name = prefabName;
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
            AccessTools.Field(typeof(InventoryItemSpec), "itemPrefabName").SetValue(inventory, prefabName);
            inventory.NameOverride = displayName;
            inventory.IsEssential = false;
            inventory.ImmuneToDumpster = false;
            inventory.PreviewBounds = new Bounds(Vector3.zero, new Vector3(PageWidth, 0.025f, PageHeight));
            inventory.ItemIconSprite = inventory.ItemIconSpriteSimple = inventory.ItemIconSpriteDropped = icon;
            var preview = Object.Instantiate(inventory.PreviewPrefab, holder.transform, false);
            preview.name = displayName + " preview";
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
            return item;
        }

        /// <summary>Lists a booklet among the game's items (for the Multiplayer mod); false without its template.</summary>
        internal static bool Register(GameObject template)
        {
            var items = Globals.G?.Items?.items;
            var spec = template != null ? template.GetComponent<InventoryItemSpec>() : null;
            if (items == null || spec == null) return false;
            if (!items.Contains(spec)) items.Add(spec);
            return true;
        }

        /// <summary>A copy of <paramref name="template"/>, the player's, in the world in front of the player; null with no game loaded.</summary>
        internal static GameObject PlaceInFront(GameObject template, string prefabName)
        {
            var player = PlayerManager.PlayerTransform;
            if (player == null || template == null) return null;
            var forward = Vector3.ProjectOnPlane(player.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.5f) forward = Vector3.forward;
            // The printed top is local -z: place it away from the player, as in the brake booklet fix.
            var copy = Object.Instantiate(template, player.position + forward * 0.6f + Vector3.up * 1.2f, Quaternion.LookRotation(-forward));
            copy.name = prefabName;
            copy.GetComponent<InventoryItemSpec>().BelongsToPlayer = true;
            SingletonBehaviour<StorageController>.Instance.AddItemToWorldStorage(copy.GetComponent<ItemBase>());
            return copy;
        }
    }
}
