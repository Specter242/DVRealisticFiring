using System;
using System.Collections.Generic;
using System.Linq;
using DV.CabControls;
using DV.CashRegister;
using DV.Shops;
using HarmonyLib;
using RealisticFiring.Visuals;
using UnityEngine;

namespace RealisticFiring.Items
{
    /// <summary>
    /// Sells the fire iron in every shop that sells the shovel, at the
    /// shovel's price and stock. Before the shops' controller sets up its
    /// data, the iron gets its own entry; after, each such shop gets a copy
    /// of the shovel's shelf tag (its scanner target and price label) selling
    /// the iron, shown as the iron scaled to the shovel's display, appended
    /// last so the stock shelf layouts stay as they are. Buying it spawns a
    /// full-size iron at the shop's pick-up spot, as for the shovel.
    /// </summary>
    [HarmonyPatch(typeof(GlobalShopController), "Awake")]
    internal static class FireIronShop
    {
        private const string ShovelPrefab = "shovel";
        private const string ShelfName = "RealisticFiring_FireIron_ShelfItem";

        private static void Prefix(GlobalShopController __instance)
        {
            try
            {
                var spec = Spec();
                if (spec == null || __instance.shopItemsData == null || __instance.shopItemsData.Any(d => d != null && d.item == spec))
                    return;
                var shovel = __instance.shopItemsData.FirstOrDefault(d => d?.item != null && d.item.ItemPrefabName == ShovelPrefab);
                var amountField = AccessTools.Field(typeof(ShopItemData), "amount");
                var data = new ShopItemData
                {
                    item = spec,
                    basePrice = shovel?.basePrice ?? 300f,
                    isGlobal = shovel?.isGlobal ?? true,
                    careerOnly = false,
                    soldOnlyAt = new List<Shop>(),
                };
                amountField.SetValue(data, shovel != null ? (int)amountField.GetValue(shovel) : 5);
                __instance.shopItemsData.Add(data);
            }
            catch (Exception e)
            {
                Main.Mod.Logger.LogException("Could not add the fire iron to the shops' items", e);
            }
        }

        private static void Postfix(GlobalShopController __instance)
        {
            try
            {
                var spec = Spec();
                if (spec == null || __instance.globalShopList == null)
                    return;
                var shops = 0;
                var shown = "";
                var where = new List<string>();
                foreach (var shop in __instance.globalShopList.Where(s => s != null && s.scanItemResourceModules != null))
                {
                    if (shop.scanItemResourceModules.Any(m => m != null && m.sellingItemSpec == spec))
                        continue;
                    var shovelTag = shop.scanItemResourceModules.FirstOrDefault(m => m != null && m.sellingItemSpec != null
                                                                                      && m.sellingItemSpec.ItemPrefabName == ShovelPrefab);
                    if (shovelTag == null)
                        continue;
                    var tag = SellIron(shovelTag, spec, out var swapped);
                    if (shops == 0)
                        shown = swapped > 0
                            ? $"; the shelf shows the iron in place of {swapped} shovel meshes ({displayNote})"
                            : "; no shovel mesh found on the shelf to swap (meshes: " +
                              string.Join(", ", shovelTag.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh != null ? f.sharedMesh.name : f.name)) + ")";
                    shop.scanItemResourceModules = shop.scanItemResourceModules.Append(tag).ToArray();
                    if (shop.cashRegister != null && shop.cashRegister.registerModules != null)
                    {
                        shop.cashRegister.registerModules = shop.cashRegister.registerModules.Append(tag).ToArray();
                        HearTag(shop.cashRegister, tag);
                    }
                    tag.ItemPurchased += __instance.AddItemToInstantiationQueue;
                    where.Add(NearestStation(shop.transform.position));
                    shops++;
                }
                if (Main.Settings.LogFireboxCreation)
                    Main.Mod.Logger.Log($"Fire iron for sale in {shops} shops ({string.Join(", ", where)}) at {__instance.GetShopItemData(spec)?.basePrice:F0} " +
                                        $"(the shovel's price; the game makes everything free outside Career), stock {__instance.GetShopItemData(spec)?.ItemsInStock}{shown}");
            }
            catch (Exception e)
            {
                Main.Mod.Logger.LogException("Could not put the fire iron on the shops' shelves", e);
            }
        }

        // A shop has no name of its own: say which station it stands at.
        private static readonly AccessTools.FieldRef<CashRegisterWithModules, ButtonBase> BuyButton =
            AccessTools.FieldRefAccess<CashRegisterWithModules, ButtonBase>("buyButton");

        // A register hooks up its shelf tags once, when it comes on; one already
        // on missed the iron's, so scanning it showed $0 and Buy took nothing
        // (a player, 2026-10-12). One not yet hooked up takes it with the rest.
        private static void HearTag(CashRegisterWithModules register, CashRegisterModule tag)
        {
            if (!register.isActiveAndEnabled || BuyButton(register) == null)
                return;
            var heard = AccessTools.MethodDelegate<Action>(AccessTools.Method(typeof(CashRegisterWithModules), "OnUnitsToBuyChanged"), register);
            tag.OnUnitsToBuyChanged -= heard;
            tag.OnUnitsToBuyChanged += heard;
        }

        private static string NearestStation(Vector3 position)
        {
            var stations = StationController.allStations;
            var nearest = stations?.Where(s => s != null && s.stationInfo != null)
                .OrderBy(s => (s.transform.position - position).sqrMagnitude)
                .FirstOrDefault();
            return nearest != null
                ? $"{nearest.stationInfo.Name} ({nearest.stationInfo.YardID}, {Vector3.Distance(nearest.transform.position, position):F0} m)"
                : "station unknown";
        }

        private static InventoryItemSpec Spec()
        {
            var template = FireIron.Template;
            return template != null ? template.GetComponent<InventoryItemSpec>() : null;
        }

        // A copy of the shovel's shelf tag selling the iron, built while
        // inactive so it wakes with the iron as its item, then set beside the
        // original so it belongs to the same shop.
        private static ScanItemCashRegisterModule SellIron(ScanItemCashRegisterModule shovelTag, InventoryItemSpec spec, out int swapped)
        {
            var holder = new GameObject("[RealisticFiring shelf template]");
            holder.SetActive(false);
            var copy = UnityEngine.Object.Instantiate(shovelTag.gameObject, holder.transform, false);
            copy.name = ShelfName;
            var tag = copy.GetComponent<ScanItemCashRegisterModule>();
            tag.sellingItemSpec = spec;
            swapped = ShowIron(copy.transform);
            copy.transform.SetParent(shovelTag.transform.parent, false);
            copy.transform.localPosition = shovelTag.transform.localPosition;
            copy.transform.localRotation = shovelTag.transform.localRotation;
            UnityEngine.Object.Destroy(holder);
            return tag;
        }

        // The shovel on display becomes the iron, lying along the shovel's
        // length with its ring flat on the shelf, scaled to the shovel's
        // length and resting where the shovel rested. The iron is modeled
        // along z with its ring in the y-z plane; each display gets its own
        // copy turned into the shovel mesh's space, so its transform stays.
        private static string displayNote = "";

        private static int ShowIron(Transform shelf)
        {
            var swapped = 0;
            var iron = FireIronMesh.Create();
            var material = PlumeAssets.FireIronMaterial;
            foreach (var filter in shelf.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (mesh == null || mesh.name.IndexOf("shovel", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                // The tag stands upright on whatever shelf the shop gives it.
                var up = filter.transform.InverseTransformDirection(shelf.up).normalized;
                if (swapped == 0)
                    displayNote = $"shovel mesh '{mesh.name}' {mesh.bounds.size:F2}, shelf up in its space {up:F2}";
                filter.sharedMesh = Display(iron, mesh.bounds, up);
                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer != null && material != null)
                    renderer.sharedMaterials = new[] { material };
                swapped++;
            }
            UnityEngine.Object.Destroy(iron);
            return swapped;
        }

        // The iron in the shovel mesh's space: its bar along the shovel's
        // longest side, its ring's face (x) as near the shelf's up as that
        // allows, as long as the shovel, centred on it across the shelf, and
        // its underside on the shovel's.
        private static Mesh Display(Mesh iron, Bounds shovel, Vector3 up)
        {
            var size = shovel.size;
            var axis = size.x >= size.y && size.x >= size.z ? Vector3.right : size.y >= size.z ? Vector3.up : Vector3.forward;
            var flat = up - Vector3.Dot(up, axis) * axis;
            if (flat.sqrMagnitude < 1e-6f)
                flat = Vector3.Cross(axis, axis == Vector3.up ? Vector3.right : Vector3.up);
            flat.Normalize();
            var turn = Quaternion.LookRotation(axis, Vector3.Cross(axis, flat));
            var scale = MaxExtent(shovel) / Mathf.Max(1e-3f, iron.bounds.size.z);

            var vertices = iron.vertices;
            var normals = iron.normals;
            var tangents = iron.tangents;
            for (var i = 0; i < vertices.Length; i++)
                vertices[i] = turn * (vertices[i] * scale);
            for (var i = 0; i < normals.Length; i++)
                normals[i] = turn * normals[i];
            for (var i = 0; i < tangents.Length; i++)
            {
                var t = turn * new Vector3(tangents[i].x, tangents[i].y, tangents[i].z);
                tangents[i] = new Vector4(t.x, t.y, t.z, tangents[i].w);
            }

            // Centred on the shovel, then down onto the shelf where it lay.
            var turned = new Bounds(vertices.Length > 0 ? vertices[0] : Vector3.zero, Vector3.zero);
            foreach (var v in vertices)
                turned.Encapsulate(v);
            var offset = shovel.center - turned.center;
            var ironBottom = float.MaxValue;
            foreach (var v in vertices)
                ironBottom = Mathf.Min(ironBottom, Vector3.Dot(v + offset, up));
            var shovelBottom = float.MaxValue;
            for (var corner = 0; corner < 8; corner++)
            {
                var point = new Vector3((corner & 1) == 0 ? shovel.min.x : shovel.max.x,
                    (corner & 2) == 0 ? shovel.min.y : shovel.max.y, (corner & 4) == 0 ? shovel.min.z : shovel.max.z);
                shovelBottom = Mathf.Min(shovelBottom, Vector3.Dot(point, up));
            }
            offset += up * (shovelBottom - ironBottom);
            for (var i = 0; i < vertices.Length; i++)
                vertices[i] += offset;

            var display = new Mesh { name = "RealisticFiring_FireIron_Display" };
            display.vertices = vertices;
            display.normals = normals;
            display.tangents = tangents;
            display.uv = iron.uv;
            display.triangles = iron.triangles;
            display.RecalculateBounds();
            return display;
        }

        private static float MaxExtent(Bounds bounds) => Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
    }
}
