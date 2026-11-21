using DV.Shops;
using UnityEngine;

namespace RealisticFiring.Items
{
    /// <summary>
    /// The shops' stock of an item the mod sells. The game counts a bought
    /// copy by its <see cref="ShopRestocker"/>: the purchase marks it, a save
    /// keeps the mark, the load counts every marked copy against the stock,
    /// and destroying one gives the stock back. A stock item sold in the shops
    /// has one on its prefab; a template made from an item that is not sold
    /// (the paper booklets, from the vehicle catalog) needs one added, or the
    /// purchase logs "Missing ShopRestocker component" and the copy is never
    /// counted (job 3175, 2026-11-21, the firing checklists).
    /// </summary>
    internal static class ShopStock
    {
        /// <summary>
        /// Gives <paramref name="template"/> a shop restocker if it has none;
        /// true if one was added. The template stays inactive, so its
        /// restocker wakes only on a copy, reading that copy's prefab name.
        /// </summary>
        internal static bool Keep(GameObject template)
        {
            if (template == null || template.GetComponent<ShopRestocker>() != null)
                return false;
            template.AddComponent<ShopRestocker>();
            return true;
        }
    }
}
