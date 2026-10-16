using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DV.CabControls;
using DV.InventorySystem;
using DV.Shops;
using DV.Utils;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RealisticFiring.Items
{
    /// <summary>
    /// Keeps a failed purchase from locking every shop (a player, 2026-10-13:
    /// the fire iron's receipt printed, no iron came, and every register
    /// stuck at "processing transaction"). The game marks all registers busy,
    /// then makes the bought items; when that throws, nothing marks them free
    /// again. This ends it cleanly instead: items already made go to the
    /// counter, the rest go back in stock and are refunded, the registers are
    /// freed, and the log says what failed.
    /// </summary>
    [HarmonyPatch]
    internal static class PurchaseGuard
    {
        private static readonly FieldInfo Queue = AccessTools.Field(typeof(GlobalShopController), "itemInstantiationQueue");
        private static readonly FieldInfo Made = AccessTools.Field(typeof(GlobalShopController), "staggeredItemActivationCollection");
        private static readonly FieldInfo Instantiating = AccessTools.Field(typeof(GlobalShopController), "isInstantiatingItems");
        private static readonly MethodInfo Processing = AccessTools.Method(typeof(GlobalShopController), "SetShopProcessingTransactionState");

        /// <summary>Whether the game's fields and method the recovery needs are there, of the types it reads.</summary>
        internal static bool Resolves =>
            Queue?.FieldType == typeof(List<ShoppingCartEntry>) && Instantiating?.FieldType == typeof(bool) && Processing != null
            && Made?.FieldType == typeof(List<(Transform, ItemBase, Shop)>);

        internal static MethodBase TargetMethod() =>
            AccessTools.EnumeratorMoveNext(AccessTools.DeclaredMethod(typeof(GlobalShopController), "InstantiatePurchasedItems"));

        private static Exception Finalizer(Exception __exception, ref bool __result)
        {
            if (__exception == null)
                return null;
            try
            {
                var shops = SingletonBehaviour<GlobalShopController>.Instance;
                var queue = Queue.GetValue(shops) as List<ShoppingCartEntry> ?? new List<ShoppingCartEntry>();
                var made = Made.GetValue(shops) as List<(Transform itemTransform, ItemBase itemBase, Shop shop)>
                           ?? new List<(Transform, ItemBase, Shop)>();
                var bought = string.Join(", ", queue.Select(e => $"{e.desiredAmount} × {e.specs?.ItemPrefabName ?? "?"}"));

                // Items made whole go to the counter, as the game would have put them.
                var delivered = new Dictionary<InventoryItemSpec, int>();
                foreach (var (transform, item, shop) in made)
                {
                    var spec = transform != null ? transform.GetComponent<InventoryItemSpec>() : null;
                    if (item == null || shop == null || shop.itemSpawnTransform == null)
                    {
                        if (transform != null)
                            Object.Destroy(transform.gameObject);
                        continue;
                    }
                    if (!item.gameObject.activeSelf)
                    {
                        SingletonBehaviour<StorageController>.Instance.RemoveItemFromLostAndFound(item);
                        SingletonBehaviour<StorageController>.Instance.AddItemToWorldStorage(item);
                        transform.position = shop.itemSpawnTransform.position;
                        item.gameObject.SetActive(true);
                        if (item.ItemRigidbody != null)
                        {
                            item.ItemRigidbody.velocity = Vector3.zero;
                            item.ItemRigidbody.angularVelocity = Vector3.zero;
                        }
                    }
                    if (spec != null)
                        delivered[MatchSpec(queue, spec)] = delivered.TryGetValue(MatchSpec(queue, spec), out var n) ? n + 1 : 1;
                }

                // Fire irons the game could not make are made from the template
                // (a player, 2026-10-15: "the object you want to instantiate is
                // null"); the rest go back in stock, and the player gets the
                // money back.
                var refund = 0.0;
                var lost = new List<string>();
                var handed = 0;
                foreach (var group in queue.Where(e => e.specs != null).GroupBy(e => e.specs))
                {
                    var missing = group.Sum(e => e.desiredAmount) - (delivered.TryGetValue(group.Key, out var got) ? got : 0);
                    if (group.Key.ItemPrefabName == FireIron.PrefabName)
                    {
                        foreach (var entry in group)
                        {
                            for (var i = 0; i < entry.desiredAmount && missing > 0 && FireIron.HandOver(entry.shop); i++)
                            {
                                missing--;
                                handed++;
                            }
                        }
                    }
                    if (missing <= 0)
                        continue;
                    var data = shops.GetShopItemData(group.Key);
                    if (data != null)
                    {
                        data.purchasedItems = Math.Max(0, data.purchasedItems - missing);
                        refund += missing * (double)data.basePrice;
                    }
                    lost.Add($"{missing} × {group.Key.ItemPrefabName}");
                }
                if (refund > 0.0)
                    SingletonBehaviour<Inventory>.Instance.AddMoney(refund);
                shops.Fire_GlobalShopDataChanged();

                queue.Clear();
                made.Clear();
                Instantiating.SetValue(shops, false);
                Processing.Invoke(shops, new object[] { false });
                __result = false;
                Main.Mod.Logger.Warning($"The shops failed to hand over a purchase ({bought}): {__exception.GetType().Name}: {__exception.Message}. " +
                                        $"Freed the registers; {(handed > 0 ? $"handed over {handed} fire iron{(handed > 1 ? "s" : "")} made from the template; " : "")}" +
                                        $"{(lost.Count > 0 ? $"not delivered, restocked and refunded ${refund:N0}: {string.Join(", ", lost)}" : "everything was delivered")}. " +
                                        FireIronState() + Environment.NewLine + __exception);
                return null;
            }
            catch (Exception e)
            {
                Main.Mod.Logger.LogException("Could not recover the shops from a failed purchase", e);
                return __exception;
            }
        }

        // The queue's own spec for an item made from it: the template's, not the copy's.
        private static InventoryItemSpec MatchSpec(List<ShoppingCartEntry> queue, InventoryItemSpec made) =>
            queue.Select(e => e.specs).FirstOrDefault(s => s != null && s.ItemPrefabName == made.ItemPrefabName) ?? made;

        private static string FireIronState()
        {
            var template = FireIron.Template;
            if (template == null)
                return "The fire iron's template is gone.";
            return $"The fire iron's template: item spec {(template.GetComponent<InventoryItemSpec>() != null ? "present" : "missing")}, " +
                   $"components {string.Join(", ", template.GetComponents<Component>().Select(c => c != null ? c.GetType().Name : "missing script"))}.";
        }
    }
}
