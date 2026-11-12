using DV.Shops;
using HarmonyLib;

namespace RealisticFiring.Items
{
    /// <summary>
    /// At quit Unity destroys objects in no set order. The fire iron's
    /// template, listed among the shops' items, can go before the shops'
    /// controller, whose OnDestroy then reads the dead item and logs a
    /// NullReferenceException (every isolated run and 1.17.1 player logs).
    /// Drop dead entries first; live ones are untouched.
    /// </summary>
    [HarmonyPatch(typeof(GlobalShopController), "OnDestroy")]
    internal static class ShopShutdownGuard
    {
        private static void Prefix(GlobalShopController __instance)
        {
            __instance.shopItemsData?.RemoveAll(d => d == null || d.item == null);
        }
    }
}
