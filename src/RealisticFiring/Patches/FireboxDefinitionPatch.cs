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
            var replace = Main.ReplacementEnabled && FireboxRegistry.IsSupported(car);
            var character = "";

            if (replace)
            {
                try
                {
                    var firebox = new RealisticFirebox(__instance, car);
                    FireboxRegistry.Register(firebox);
                    __result = firebox;
                    character = $"; {firebox.Character.Summary}, grate slope {firebox.Model.GrateSlope:F1}°";
                }
                catch (Exception e)
                {
                    Main.Mod.Logger.LogException($"Realistic firebox failed on {carName}; keeping the stock firebox", e);
                    replace = false;
                }
            }

            if (Main.Settings.LogFireboxCreation)
            {
                Main.Mod.Logger.Log(
                    $"{(replace ? "Realistic" : "Stock")} firebox '{__instance.ID}' created on {carName} " +
                    $"(capacity {__instance.maxCoalCapacity} kg, burn time {__instance.burnTime} s){character}");
            }

            return !replace;
        }
    }
}
