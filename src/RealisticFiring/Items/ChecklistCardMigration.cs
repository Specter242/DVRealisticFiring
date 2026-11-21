using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace RealisticFiring.Items
{
    /// <summary>
    /// Turns the retired pocket checklist card (a gadget item, only in preview
    /// builds) into the firing checklists booklet as a save loads, before the
    /// game resolves the starting items or makes any item. Without it the game
    /// skips a saved item whose prefab is gone (an error in the log, and the
    /// card lost). A card kept in the inventory, the world, lost and found, or
    /// an item container becomes a booklet in the same place, opening on its
    /// cover (the card's own data means nothing to a booklet). A booklet can't
    /// be mounted, so a card fitted to a gadget mount goes to lost and found,
    /// as the game does with a gadget whose mount is gone.
    /// </summary>
    [HarmonyPatch(typeof(StartingItemsController), "CombineAndResolveIllegalDuplicates")]
    internal static class ChecklistCardMigration
    {
        /// <summary>The card's item name in saves made by the builds that had it.</summary>
        internal const string CardPrefabName = "RealisticFiring_FiringChecklists";

        private static void Prefix(List<StorageItemData> itemsInventory, List<StorageItemData> itemsLostAndFound,
            List<StorageItemData> itemsWorld, List<StorageItemData> itemsInstalledGadgets, List<StorageItemData> itemsItemContainers)
        {
            try
            {
                var moved = Migrate(itemsInventory, itemsLostAndFound, itemsWorld, itemsInstalledGadgets, itemsItemContainers);
                if (moved > 0)
                    Main.Mod?.Logger.Log($"Replaced {moved} saved checklist card{(moved == 1 ? "" : "s")} with the firing checklists booklet");
            }
            catch (Exception error)
            {
                Main.Mod?.Logger.LogException("Could not replace the saved checklist cards with the booklet", error);
            }
        }

        /// <summary>Renames every saved card to the booklet in place, and moves mounted ones to lost and found; how many.</summary>
        internal static int Migrate(List<StorageItemData> inventory, List<StorageItemData> lostAndFound,
            List<StorageItemData> world, List<StorageItemData> installedGadgets, List<StorageItemData> containers)
        {
            var moved = 0;
            foreach (var list in new[] { inventory, lostAndFound, world, containers })
            {
                if (list == null) continue;
                foreach (var item in list)
                {
                    if (item?.itemPrefabName != CardPrefabName) continue;
                    item.itemPrefabName = ChecklistBooklet.PrefabName;
                    item.state = null;
                    moved++;
                }
            }
            if (installedGadgets == null) return moved;
            for (var i = installedGadgets.Count - 1; i >= 0; i--)
            {
                var item = installedGadgets[i];
                if (item?.itemPrefabName != CardPrefabName) continue;
                installedGadgets.RemoveAt(i);
                moved++;
                if (lostAndFound == null) continue;
                // Lost and found places an item saved without a position itself.
                lostAndFound.Add(new StorageItemData(ChecklistBooklet.PrefabName, StorageItemTransformController.IGNORE_POSITION_VALUE,
                    Quaternion.identity, item.belongsToPlayer));
            }
            return moved;
        }
    }
}
