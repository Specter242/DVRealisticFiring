using System;
using System.Collections.Generic;
using System.Linq;
using DV.Items;
using HarmonyLib;

namespace RealisticFiring.Items
{
    [HarmonyPatch(typeof(StartingItemsController), "GetStartingItemsPrefabs")]
    internal static class FiringManualStartingItems
    {
        private static void Postfix(ref List<StartingItems.StartingItem> __result)
        {
            try { __result = WithManual(__result, () => FiringManual.Template?.GetComponent<InventoryItemSpec>()); }
            catch (Exception error) { Main.Mod?.Logger.LogException("Could not add the firing manual to steam starting equipment", error); }
        }

        internal static List<StartingItems.StartingItem> WithManual(List<StartingItems.StartingItem> items, Func<InventoryItemSpec> create)
        {
            if (items == null || !items.Any(i => i.item?.ItemPrefabName == "shovel")
                || items.Any(i => i.item?.ItemPrefabName == FiringManual.PrefabName)) return items;
            var spec = create();
            if (spec?.ItemPrefabName != FiringManual.PrefabName) return items;
            return new List<StartingItems.StartingItem>(items)
            {
                new StartingItems.StartingItem { item = spec, preferredRelativeSlot = 0, backpackPriority = true, allowDuplicates = true },
            };
        }
    }
}
