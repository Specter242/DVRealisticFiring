using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RealisticFiring.Items
{
    /// <summary>
    /// The firing checklists: a paper booklet in the manual's style, a cover
    /// and then one checklist a page (<see cref="ChecklistBookletContents"/>).
    /// Static paper; the firing tablet's Check tab is the live, ticking copy.
    /// Pages turn and the page is saved as in any stock booklet.
    /// </summary>
    internal static class ChecklistBooklet
    {
        internal const string PrefabName = "RealisticFiring_ChecklistBooklet";
        internal const string DisplayName = "Firing checklists";
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
                var assets = BookletAssets.Checklists;
                var item = PaperBooklet.Clone("[RealisticFiring checklist booklet template]", PrefabName, DisplayName, assets.Pages, assets.Icon, out holder);
                if (item == null)
                    return null;
                item.AddComponent<ChecklistBookletDescription>();
                // Sold in the shops: the catalog it is made from is not, so it has no restocker of its own.
                var restocker = ShopStock.Keep(item) ? "a shop restocker added" : "the catalog's shop restocker";
                Main.Mod?.Logger.Log($"Firing checklists built from the portrait VehicleCatalog, with {ChecklistBookletContents.PageCount} pages and {restocker}");
                return item;
            }
            catch (Exception error)
            {
                if (holder != null) Object.Destroy(holder);
                Main.Mod?.Logger.Warning("Cannot build the firing checklists; saved copies will be skipped: " + error);
                return null;
            }
        }

        internal static bool RegisterAsGameItem() => PaperBooklet.Register(Template);

        internal static string GiveToPlayer()
        {
            if (PlayerManager.PlayerTransform == null) return "No checklists: load a game first";
            if (Template == null) return "The firing checklists could not be built; see the mod log";
            PaperBooklet.PlaceInFront(Template, PrefabName);
            return "The firing checklists are in front of you; turn the pages as in any booklet";
        }
    }

    internal sealed class ChecklistBookletDescription : MonoBehaviour, IInventoryItemLocalizer
    {
        private void Awake() => GetComponent<InventoryItemSpec>().NameOverride = ChecklistBooklet.DisplayName;
        public string GetNameParam() => ChecklistBooklet.DisplayName;
        public string GetCustomDescription() =>
            "A booklet of six firing checklists, one a page: cold starts on coal and oil, injectors and water, cleaning the fire, " +
            "the stoker, and drifting and layover. Turn the pages as in any booklet; your page is saved. " +
            "The tablet's Check tab ticks the same steps off live.";
    }
}
