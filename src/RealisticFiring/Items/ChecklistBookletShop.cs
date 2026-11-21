using System;
using System.Collections.Generic;
using System.Linq;
using DV.Items;
using DV.Shops;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RealisticFiring.Items
{
    /// <summary>
    /// Sells the firing checklists beside the stock clinometer in gadget shops
    /// (always available, including alongside the Comms Tablet). The shelf
    /// shows a small copy of the booklet lying where the card lay, cover up.
    /// </summary>
    [HarmonyPatch(typeof(GlobalShopController), "Awake")]
    internal static class ChecklistBookletShop
    {
        internal const string ShelfSource = "Clinometer";
        private const float BasePrice = 50f;
        /// <summary>
        /// Shelf offset beside the clinometer / firing tablet so the booklet's
        /// display, scan tag, and collider never stack on top of them when Comms
        /// Tablet is not installed.
        /// </summary>
        internal static readonly Vector3 ShelfOffset = new Vector3(0.20f, 0f, 0f);
        /// <summary>The shelf copy: the booklet at the size the card had there, its thickness along the display's Z.</summary>
        internal static readonly Vector3 ShelfSize = new Vector3(0.15f, 0.15f / PaperBooklet.PageWidth * PaperBooklet.PageHeight, 0.008f) * 0.65f;
        private static Material shelfMaterial;

        private static void Prefix(GlobalShopController __instance)
        {
            try
            {
                var prefab = ChecklistBooklet.Template;
                var spec = prefab != null ? prefab.GetComponent<InventoryItemSpec>() : null;
                if (spec == null || __instance.shopItemsData == null || __instance.shopItemsData.Any(d => d?.item == spec))
                    return;
                var source = __instance.shopItemsData.FirstOrDefault(d => d?.item?.ItemPrefabName == ShelfSource);
                if (source == null)
                    return;
                var data = new ShopItemData
                {
                    item = spec,
                    basePrice = BasePrice,
                    isGlobal = source.isGlobal,
                    careerOnly = false,
                    soldOnlyAt = new List<Shop>(source.soldOnlyAt ?? new List<Shop>()),
                };
                var amount = AccessTools.Field(typeof(ShopItemData), "amount");
                amount.SetValue(data, amount.GetValue(source));
                __instance.shopItemsData.Add(data);
            }
            catch (Exception e)
            {
                Main.Mod?.Logger.LogException("Could not add the firing checklists to the shops' items", e);
            }
        }

        private static void Postfix(GlobalShopController __instance)
        {
            try
            {
                var prefab = ChecklistBooklet.Template;
                var spec = prefab != null ? prefab.GetComponent<InventoryItemSpec>() : null;
                if (spec == null || __instance.globalShopList == null)
                    return;
                var count = 0;
                foreach (var shop in __instance.globalShopList)
                {
                    if (shop == null || shop.scanItemResourceModules == null || shop.scanItemResourceModules.Any(m => m?.sellingItemSpec == spec))
                        continue;
                    var source = shop.scanItemResourceModules.FirstOrDefault(m => m?.sellingItemSpec?.ItemPrefabName == ShelfSource);
                    if (source == null)
                        continue;
                    var holder = new GameObject("[RealisticFiring checklists shelf template]");
                    holder.SetActive(false);
                    var copy = Object.Instantiate(source.gameObject, holder.transform, false);
                    copy.name = "RealisticFiring_ChecklistBooklet_ShelfItem";
                    var tag = copy.GetComponent<ScanItemCashRegisterModule>();
                    tag.sellingItemSpec = spec;
                    var display = copy.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "gadget_clinometer_base");
                    if (display != null)
                    {
                        foreach (var renderer in display.GetComponentsInChildren<Renderer>(true))
                            renderer.enabled = false;
                        ShelfCopy(display, BookletAssets.Checklists.Pages?[0]);
                    }
                    copy.transform.SetParent(source.transform.parent, false);
                    copy.transform.localPosition = source.transform.localPosition + source.transform.localRotation * ShelfOffset;
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
                Main.Mod?.Logger.Log($"Firing checklists for sale beside the clinometer in {count} shops");
            }
            catch (Exception e)
            {
                Main.Mod?.Logger.LogException("Could not put the firing checklists on the shops' shelves", e);
            }
        }

        /// <summary>
        /// A thin box the booklet's size in the clinometer display's XY plane,
        /// where the card lay. The display's Z is up on the shelf (read in game),
        /// so the box lies flat, cover up; both large faces show the cover.
        /// </summary>
        private static void ShelfCopy(Transform display, Texture cover)
        {
            var copy = GameObject.CreatePrimitive(PrimitiveType.Cube);
            copy.name = "[firing checklists on the shelf]";
            Object.Destroy(copy.GetComponent<Collider>());
            copy.transform.SetParent(display, false);
            copy.transform.localScale = ShelfSize;
            if (shelfMaterial == null)
            {
                shelfMaterial = new Material(Shader.Find("Standard")) { name = "RealisticFiring_ChecklistsShelf", mainTexture = cover };
                shelfMaterial.SetFloat("_Glossiness", 0.15f);
            }
            copy.GetComponent<MeshRenderer>().sharedMaterial = shelfMaterial;
        }
    }

    /// <summary>
    /// Offers the firing checklists once per save when the starting kit
    /// includes the locomotive catalog or steam shovel, respecting disposal on
    /// subsequent loads (exact same pattern as <see cref="FiringManualStartingItems"/>).
    /// A save from before the booklet is offered it once on its next load;
    /// one whose old checklist card became the booklet already holds it.
    /// </summary>
    [HarmonyPatch(typeof(StartingItemsController), "GetStartingItemsPrefabs")]
    internal static class ChecklistBookletStartingItems
    {
        internal const string GivenKey = "RealisticFiring_ChecklistBookletGiven";

        private static void Postfix(SaveGameData data, ref List<StartingItems.StartingItem> __result)
        {
            try { __result = WithBookletOnce(data, __result, () => ChecklistBooklet.Template?.GetComponent<InventoryItemSpec>()); }
            catch (Exception error) { Main.Mod?.Logger.LogException("Could not add the firing checklists to the starting equipment", error); }
        }

        internal static List<StartingItems.StartingItem> WithBookletOnce(SaveGameData data,
            List<StartingItems.StartingItem> items, Func<InventoryItemSpec> create)
        {
            if (data == null || items == null || data.GetBool(GivenKey) == true) return items;
            var result = WithBooklet(items, create);
            if (!ReferenceEquals(result, items) || items.Any(i => i.item?.ItemPrefabName == ChecklistBooklet.PrefabName))
                data.SetBool(GivenKey, true);
            return result;
        }

        internal static List<StartingItems.StartingItem> WithBooklet(List<StartingItems.StartingItem> items, Func<InventoryItemSpec> create)
        {
            if (items == null || !items.Any(i => i.item?.ItemPrefabName == FiringManual.SourcePrefab
                || i.item?.ItemPrefabName == "shovel")
                || items.Any(i => i.item?.ItemPrefabName == ChecklistBooklet.PrefabName)) return items;
            var spec = create();
            if (spec?.ItemPrefabName != ChecklistBooklet.PrefabName) return items;
            return new List<StartingItems.StartingItem>(items)
            {
                new StartingItems.StartingItem { item = spec, preferredRelativeSlot = 0, backpackPriority = true, allowDuplicates = true },
            };
        }
    }
}
