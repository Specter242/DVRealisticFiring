using System;
using HarmonyLib;
using LocoSim.Definitions;
using LocoSim.Implementations;
using RealisticFiring.Sim;

namespace RealisticFiring.Patches
{
    /// <summary>
    /// SimulationFlow calls InstantiateImplementation once per car while
    /// building the sim graph. On supported stock locos, with replacement
    /// enabled at game start, this returns the realistic firebox instead of
    /// the stock one. Every other loco, CCL locos included, keeps the stock
    /// Firebox.
    /// </summary>
    [HarmonyPatch(typeof(FireboxDefinition), nameof(FireboxDefinition.InstantiateImplementation))]
    internal static class FireboxDefinitionPatch
    {
        private static bool Prefix(FireboxDefinition __instance, ref SimComponent __result)
        {
            var car = __instance.GetComponentInParent<TrainCar>();
            var carName = car != null && car.carLivery != null ? car.carLivery.id : __instance.transform.root.name;
            if (OilBurnerFit.IsOilBurner(car))
            {
                var makersOil = BoilerFit.Apply(__instance, car, oil: true);
                try
                {
                    __result = new RealisticOilFirebox(__instance, car);
                    if (Main.Settings.LogFireboxCreation)
                        Main.Mod.Logger.Log($"Realistic oil firebox '{__instance.ID}' created on {carName} " +
                                            $"({__instance.maxCoalCapacity} kg over {__instance.burnTime:F0} s{Sized(makersOil)})");
                    return false;
                }
                catch (Exception e)
                {
                    if (makersOil.HasValue)
                        __instance.burnTime = makersOil.Value;
                    Main.Mod.Logger.LogException($"Realistic oil firebox failed on {carName}; keeping the stock firebox", e);
                    return true;
                }
            }
            var replace = Main.ReplacementEnabled && FireboxRegistry.IsSupported(car);
            var character = "";
            float? makers = null;

            if (replace)
            {
                makers = BoilerFit.Apply(__instance, car, oil: false);
                try
                {
                    var firebox = new RealisticFirebox(__instance, car);
                    FireboxRegistry.Register(firebox);
                    __result = firebox;
                    character = $"; {firebox.Character.Summary}, grate slope {firebox.Model.GrateSlope:F1}°";
                }
                catch (Exception e)
                {
                    if (makers.HasValue)
                        __instance.burnTime = makers.Value;
                    makers = null;
                    Main.Mod.Logger.LogException($"Realistic firebox failed on {carName}; keeping the stock firebox", e);
                    replace = false;
                }
            }

            if (Main.Settings.LogFireboxCreation)
            {
                Main.Mod.Logger.Log(
                    $"{(replace ? "Realistic" : "Stock")} firebox '{__instance.ID}' created on {carName} " +
                    $"(capacity {__instance.maxCoalCapacity} kg, burn time {__instance.burnTime:F0} s{Sized(makers)}){character}");
            }

            return !replace;
        }

        private static string Sized(float? makers) =>
            makers.HasValue ? $", its maker's {makers.Value:F0} s: sized to its prototype's boiler" : "";
    }
}
