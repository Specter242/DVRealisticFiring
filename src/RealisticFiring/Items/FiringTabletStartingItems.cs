using System;
using System.Collections.Generic;
using System.Linq;
using DV.Items;
using HarmonyLib;

namespace RealisticFiring.Items
{
    /// <summary>Adds the tablet to the resolved starting kit when it includes the steam shovel.</summary>
    [HarmonyPatch(typeof(StartingItemsController), "GetStartingItemsPrefabs")]
    internal static class FiringTabletStartingItems
    {
        private static void Postfix(ref List<StartingItems.StartingItem> __result)
        {
            try
            {
                __result = WithTablet(__result, () => FiringTablet.Template?.GetComponent<InventoryItemSpec>());
            }
            catch (Exception error)
            {
                // A tablet failure must not interrupt the stock saved-item loader.
                Main.Mod?.Logger.LogException("Could not add the firing tablet to the starting equipment", error);
            }
        }

        internal static List<StartingItems.StartingItem> WithTablet(List<StartingItems.StartingItem> items,
            Func<InventoryItemSpec> tabletSpec)
        {
            if (items == null || !items.Any(i => i.item?.ItemPrefabName == "shovel")
                || items.Any(i => i.item?.ItemPrefabName == FiringTablet.PrefabName))
                return items;
            var spec = tabletSpec();
            if (spec?.ItemPrefabName != FiringTablet.PrefabName)
                return items;
            // GetStartingItems can return the asset's own list. Keep it intact:
            // the stock safeguard allocates a free slot and checks all saved
            // storage, including mounted gadgets, before adding a missing item.
            return new List<StartingItems.StartingItem>(items)
            {
                new StartingItems.StartingItem
                {
                    item = spec,
                    preferredRelativeSlot = 0,
                    backpackPriority = true,
                    allowDuplicates = true,
                },
            };
        }
    }
}
