using System;
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
        // The paper of both booklets (PaperBooklet); scripts/check-manual-interaction.py reads these lines.
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
                var assets = BookletAssets.Manual;
                var item = PaperBooklet.Clone("[RealisticFiring manual template]", PrefabName, DisplayName, assets.Pages, assets.Icon, out holder);
                if (item == null)
                    return null;
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

        internal static bool RegisterAsGameItem() => PaperBooklet.Register(Template);

        internal static string GiveToPlayer()
        {
            if (PlayerManager.PlayerTransform == null) return "No manual: load a game first";
            if (Template == null) return "The firing manual could not be built; see the mod log";
            PaperBooklet.PlaceInFront(Template, PrefabName);
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
