using System;
using System.Collections.Generic;
using System.Linq;
using DV.Shops;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RealisticFiring.Items
{
    /// <summary>Sells the tablet beside the stock clinometer, using its price and stock.</summary>
    [HarmonyPatch(typeof(GlobalShopController), "Awake")]
    internal static class FiringTabletShop
    {
        private static void Prefix(GlobalShopController __instance)
        {
            try
            {
                var prefab = FiringTablet.Template;
                var spec = prefab != null ? prefab.GetComponent<InventoryItemSpec>() : null;
                if (spec == null || __instance.shopItemsData == null || __instance.shopItemsData.Any(d => d?.item == spec))
                    return;
                var source = __instance.shopItemsData.FirstOrDefault(d => d?.item?.ItemPrefabName == FiringTablet.SourcePrefab);
                if (source == null)
                    return;
                var data = new ShopItemData
                {
                    item = spec,
                    basePrice = source.basePrice,
                    isGlobal = source.isGlobal,
                    careerOnly = source.careerOnly,
                    soldOnlyAt = new List<Shop>(source.soldOnlyAt ?? new List<Shop>()),
                };
                var amount = AccessTools.Field(typeof(ShopItemData), "amount");
                amount.SetValue(data, amount.GetValue(source));
                __instance.shopItemsData.Add(data);
            }
            catch (Exception e)
            {
                Main.Mod?.Logger.LogException("Could not add the firing tablet to the shops' items", e);
            }
        }

        private static void Postfix(GlobalShopController __instance)
        {
            try
            {
                var prefab = FiringTablet.Template;
                var spec = prefab != null ? prefab.GetComponent<InventoryItemSpec>() : null;
                if (spec == null || __instance.globalShopList == null)
                    return;
                var count = 0;
                foreach (var shop in __instance.globalShopList)
                {
                    if (shop == null || shop.scanItemResourceModules == null || shop.scanItemResourceModules.Any(m => m?.sellingItemSpec == spec))
                        continue;
                    var source = shop.scanItemResourceModules.FirstOrDefault(m => m?.sellingItemSpec?.ItemPrefabName == FiringTablet.SourcePrefab);
                    if (source == null)
                        continue;
                    var holder = new GameObject("[RealisticFiring tablet shelf template]");
                    holder.SetActive(false);
                    var copy = Object.Instantiate(source.gameObject, holder.transform, false);
                    copy.name = "RealisticFiring_FiringTablet_ShelfItem";
                    var tag = copy.GetComponent<ScanItemCashRegisterModule>();
                    tag.sellingItemSpec = spec;
                    var display = copy.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "gadget_clinometer_base");
                    if (display != null)
                    {
                        foreach (var renderer in display.GetComponentsInChildren<Renderer>(true))
                            renderer.enabled = false;
                        FiringTabletBody.Build(display).localScale = Vector3.one * 0.42f;
                    }
                    copy.transform.SetParent(source.transform.parent, false);
                    copy.transform.localPosition = source.transform.localPosition;
                    copy.transform.localRotation = source.transform.localRotation;
                    Object.Destroy(holder);
                    shop.scanItemResourceModules = shop.scanItemResourceModules.Append(tag).ToArray();
                    if (shop.cashRegister != null && shop.cashRegister.registerModules != null)
                    {
                        shop.cashRegister.registerModules = shop.cashRegister.registerModules.Append(tag).ToArray();
                        FireIronShop.HearTag(shop.cashRegister, tag);
                    }
                    tag.ItemPurchased += __instance.AddItemToInstantiationQueue;
                    count++;
                }
                Main.Mod?.Logger.Log($"Firing tablet for sale beside the clinometer in {count} shops");
            }
            catch (Exception e)
            {
                Main.Mod?.Logger.LogException("Could not put the firing tablet on the shops' shelves", e);
            }
        }
    }
}
