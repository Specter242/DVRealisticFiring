using System;
using DV.Simulation.Controllers;
using HarmonyLib;
using LocoSim.Implementations;
using RealisticFiring.Sim;
using RealisticFiring.Visuals;
using RealisticStackEffects;

namespace RealisticFiring.Patches
{
    /// <summary>
    /// Hands a realistic firebox's or oil burner's chimney smoke to
    /// <see cref="ChimneyPlume"/> once the stock reader has found its ports
    /// and particle systems.
    /// </summary>
    [HarmonyPatch(typeof(SteamSmokeParticlePortReader), nameof(SteamSmokeParticlePortReader.Init))]
    internal static class SmokeReaderInitPatch
    {
        private static void Postfix(SteamSmokeParticlePortReader __instance, SimulationFlow simFlow)
        {
            // Realistic Stack Effects draws every stack when it runs.
            if (!Main.RealisticSmokeEnabled || StackEffectsLink.HandedOff)
                return;
            try
            {
                var car = TrainCar.Resolve(__instance.gameObject);
                string result;
                if (FireboxRegistry.TryGet(car, out var firebox))
                    result = ChimneyPlume.Attach(__instance, simFlow, car, new CoalStack(car, firebox));
                else if (RealisticOilFirebox.TryGet(car, out var oil))
                    result = ChimneyPlume.Attach(__instance, simFlow, car, new OilStack(car, oil));
                else
                    return;
                // Car numbers are assigned after the sim graph is built.
                if (Main.Settings.LogFireboxCreation)
                    Main.Mod.Logger.Log($"{car.LogName()}: {result}");
            }
            catch (Exception e)
            {
                Main.Mod.Logger.LogException("Chimney smoke setup failed; keeping the stock smoke", e);
            }
        }
    }

    /// <summary>Keeps the stock reader from starting or stopping smoke the plume drives.</summary>
    [HarmonyPatch(typeof(SteamSmokeParticlePortReader), "UpdateParticles")]
    internal static class SmokeReaderUpdatePatch
    {
        private static bool Prefix(SteamSmokeParticlePortReader __instance) =>
            !(__instance.TryGetComponent<ChimneyPlume>(out var plume) && plume.Active);
    }
}
