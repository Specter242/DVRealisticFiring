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
        internal const string GivenKey = "RealisticFiring_FiringManualGiven";

        private static void Postfix(SaveGameData data, ref List<StartingItems.StartingItem> __result)
        {
            try { __result = WithManualOnce(data, __result, () => FiringManual.Template?.GetComponent<InventoryItemSpec>()); }
            catch (Exception error) { Main.Mod?.Logger.LogException("Could not add the firing manual beside the starting manuals", error); }
        }

        internal static List<StartingItems.StartingItem> WithManualOnce(SaveGameData data,
            List<StartingItems.StartingItem> items, Func<InventoryItemSpec> create)
        {
            if (data == null || items == null || data.GetBool(GivenKey) == true) return items;
            var result = WithManual(items, create);
            // The stock safeguard checks every saved storage before granting the
            // offered manual. Remember that offer even if a saved copy satisfies it.
            // Missing templates leave the flag unset so a later load can retry.
            if (!ReferenceEquals(result, items) || items.Any(i => i.item?.ItemPrefabName == FiringManual.PrefabName))
                data.SetBool(GivenKey, true);
            return result;
        }

        internal static List<StartingItems.StartingItem> WithManual(List<StartingItems.StartingItem> items, Func<InventoryItemSpec> create)
        {
            if (items == null || !items.Any(i => i.item?.ItemPrefabName == FiringManual.SourcePrefab
                || i.item?.ItemPrefabName == "shovel")
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
