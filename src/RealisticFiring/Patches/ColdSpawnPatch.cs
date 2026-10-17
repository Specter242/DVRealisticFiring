using System.Linq;
using DV.Simulation.Cars;
using HarmonyLib;
using LocoSim.Definitions;
using RealisticFiring.Sim;

namespace RealisticFiring.Patches
{
    /// <summary>
    /// A cold engine has no steam. The oil-fired mods spawn with 3 bar in the
    /// boiler (2 on the gauge) so Custom Car Loader's stoker, which needs 2,
    /// can feed their oil; with the realistic burner they spawn cold, as the
    /// stock engines do, and raise their first steam on a shed's steam line.
    /// Saved engines keep their saved pressure.
    /// </summary>
    [HarmonyPatch(typeof(SimController), nameof(SimController.Initialize))]
    internal static class ColdSpawnPatch
    {
        private static void Prefix(SimController __instance, TrainCar trainCar) =>
            SetupGuard.Run(trainCar, "cold spawn", () => Add(__instance, trainCar));

        private static void Add(SimController __instance, TrainCar trainCar)
        {
            var graph = __instance.connectionsDefinition;
            if (trainCar == null || graph == null || graph.executionOrder == null || !OilBurnerFit.IsOilBurner(trainCar))
                return;
            foreach (var boiler in graph.executionOrder.OfType<BoilerDefinition>().Where(b => b.spawnPressure > 1f))
            {
                if (Main.Settings.LogFireboxCreation)
                    Main.Mod.Logger.Log($"{trainCar.carLivery.id}: spawns cold, at 1 bar instead of its mod's {boiler.spawnPressure:F1}");
                boiler.spawnPressure = 1f;
            }
        }
    }
}
