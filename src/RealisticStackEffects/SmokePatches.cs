using System;
using DV.Simulation.Cars;
using DV.Simulation.Controllers;
using HarmonyLib;
using LocoSim.Implementations;
using RealisticStackEffects.Api;
using UnityModManagerNet;

namespace RealisticStackEffects
{
    /// <summary>
    /// Hands every steam locomotive's chimney smoke to <see cref="ChimneyPlume"/>
    /// once the stock reader has found its ports and particle systems, read
    /// from a fire another mod feeds it (<see cref="StackSources"/>) or else
    /// its own firebox (<see cref="StockStack"/>). Off altogether beside a
    /// Realistic Firing from before the hand-off (1.15 and older), which
    /// draws the smoke itself. After Realistic Firing's own patch, whatever
    /// the load order, so should that check ever miss, its plume, there
    /// first, keeps this one off its engines (<see cref="ChimneyPlume.Attach"/>).
    /// </summary>
    [HarmonyPatch(typeof(SteamSmokeParticlePortReader), nameof(SteamSmokeParticlePortReader.Init))]
    [HarmonyAfter("RealisticFiring")]
    internal static class SmokeReaderInitPatch
    {
        private static void Postfix(SteamSmokeParticlePortReader __instance, SimulationFlow simFlow)
        {
            try
            {
                var car = TrainCar.Resolve(__instance.gameObject);
                if (car == null || OlderFiring())
                    return;
                // Another mod's fire where one feeds it, else the game's own.
                string how = null;
                var fire = StackSources.For(car, () => StockStack.For(car, __instance, simFlow, out how), out var by)
                           ?? StockStack.For(car, __instance, simFlow, out how);
                if (by != null)
                    how = $"the fire from {by}";
                var result = ChimneyPlume.Attach(__instance, simFlow, car, fire);
                if (Main.Settings.LogSetup)
                    Main.Mod.Logger.Log($"{LogName(car)}: {result}; {how}");
            }
            catch (Exception e)
            {
                Main.Mod.Logger.LogException("Chimney smoke setup failed; keeping the stock smoke", e);
            }
        }

        /// <summary>Whether a Realistic Firing from before the hand-off has been found beside this mod.</summary>
        internal static bool OlderFiringFound { get; private set; }

        // A Realistic Firing from before the hand-off (its assembly loaded,
        // even if its load then failed, without StackEffectsLink) draws its
        // own engines' smoke with its own copy of this plume, and two plume
        // systems drawing at half resolution write the same globals (Sol's
        // audit, 2026-10-26): this mod attaches no more plumes, and its own
        // pass stops (HalfResolutionAllowed). Looked for at each setup until
        // found, since Unity Mod Manager can load a mod switched on mid-game.
        private static bool OlderFiring()
        {
            if (OlderFiringFound)
                return true;
            var firing = UnityModManager.FindMod("RealisticFiring");
            var assembly = firing?.Assembly;
            if (assembly == null || assembly.GetType("RealisticFiring.StackEffectsLink") != null)
                return false;
            OlderFiringFound = true;
            Main.Mod.Logger.Warning($"Realistic Firing {firing.Info.Version} draws the chimney smoke itself; update it to " +
                                    "1.16.0 or newer for Realistic Stack Effects to draw it, fed by the realistic fire. Until then this mod stays off.");
            return true;
        }

        // The game numbers a car only after building its sim graph, and
        // reading its ID before then logs an error: until then, its livery.
        private static string LogName(TrainCar car) =>
            car.logicCar != null ? car.logicCar.ID
            : car.carLivery != null ? car.carLivery.id
            : car.name;
    }

    /// <summary>
    /// A shovelful landing on a fire this mod reads from the game's own
    /// firebox (<see cref="StockStack"/>), for its smoke-out: told by the
    /// game's coal transfer, which the shovel, the coal pile, and the
    /// keyboard's shoveling go through and a stoker does not. Only what the
    /// firebox takes counts (the game's start-up helper sends 9,999,999 kg).
    /// On a multiplayer guest, the guest's own throws.
    /// </summary>
    [HarmonyPatch(typeof(FireboxSimController), nameof(FireboxSimController.TransferCoal))]
    internal static class ShovelfulPatch
    {
        private static void Prefix(FireboxSimController __instance, out float __state)
        {
            // The room left in the firebox, kg, before the coal lands.
            try
            {
                __state = Math.Max(0f, __instance.FireboxCapacity - __instance.FireboxContents);
            }
            catch (Exception)
            {
                __state = 0f;
            }
        }

        private static void Postfix(FireboxSimController __instance, float coalMass, float __state)
        {
            try
            {
                if (!(coalMass > 0f) || !(__instance.coalConsumptionMultiplier > 0f))
                    return;
                var landed = Math.Min(coalMass / __instance.coalConsumptionMultiplier, __state);
                foreach (var stack in StockStack.On(TrainCar.Resolve(__instance.gameObject)))
                    stack.Thrown(landed);
            }
            catch (Exception e)
            {
                Main.Mod.Logger.LogException("A shovelful's smoke-out failed", e);
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
