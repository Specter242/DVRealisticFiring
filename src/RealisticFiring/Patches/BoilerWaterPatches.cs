using HarmonyLib;
using LocoSim.Implementations;
using Newtonsoft.Json.Linq;
using RealisticFiring.Sim;

namespace RealisticFiring.Patches
{
    /// <summary>Let a drained, unheated boiler cool during the game's clock skip too.</summary>
    [HarmonyPatch(typeof(DV.TimeAdvance), nameof(DV.TimeAdvance.AdvanceTime))]
    internal static class EmptyBoilerTimeAdvancePatch
    {
        private static void Prefix(bool force, out bool __state)
        {
            var weather = DV.Utils.SingletonBehaviour<DV.WeatherSystem.WeatherDriver>.Instance;
            __state = force || !weather || !weather.TimeOfDayHours.IsOverridden;
        }

        private static void Postfix(float amountOfTimeToSkipInSeconds, bool __state, bool __runOriginal)
        {
            // Multiplayer may suppress the original call. Harmony still runs
            // postfixes, so a requested skip is not proof that time advanced.
            if (__state && __runOriginal)
            {
                BoilerWaterRig.CoolEmptyBoilers(amountOfTimeToSkipInSeconds);
                EngineSteamPath.AdvanceClosedCircuits(amountOfTimeToSkipInSeconds);
            }
        }
    }

    /// <summary>
    /// Hooks a boiler's water rig into the stock boiler's tick: the water at
    /// the start and the steel's heat, what the feed put in, what the blowdown
    /// took, the foam and swell after the level step, and the crown sheet on
    /// the true level. Boilers without a rig keep every stock step.
    /// </summary>
    [HarmonyPatch(typeof(Boiler), nameof(Boiler.Tick))]
    internal static class BoilerTickPatch
    {
        private static void Prefix(Boiler __instance, float delta)
        {
            if (BoilerWaterRig.TryGet(__instance, out var rig))
                rig.BeginTick(delta);
        }
    }

    [HarmonyPatch(typeof(Boiler), "SimulateInjector")]
    internal static class BoilerFeedPatch
    {
        private static void Postfix(Boiler __instance)
        {
            if (BoilerWaterRig.TryGet(__instance, out var rig))
                rig.AfterFeed();
        }
    }

    [HarmonyPatch(typeof(Boiler), "SimulateBlowdown")]
    internal static class BoilerBlowdownPatch
    {
        private static void Prefix(Boiler __instance, out float __state)
        {
            __state = BoilerWaterRig.TryGet(__instance, out var rig) ? rig.MassNow : 0f;
        }

        private static void Postfix(Boiler __instance, float __state)
        {
            if (BoilerWaterRig.TryGet(__instance, out var rig))
                rig.AfterBlowdown(__state);
        }
    }

    [HarmonyPatch(typeof(Boiler), "SimulateWaterLevel")]
    internal static class BoilerWaterLevelPatch
    {
        private static void Postfix(Boiler __instance, float delta)
        {
            if (BoilerWaterRig.TryGet(__instance, out var rig))
                rig.AfterWaterLevel(delta);
        }
    }

    [HarmonyPatch(typeof(Boiler), "SimulateCrownSheet")]
    internal static class BoilerCrownSheetPatch
    {
        private static bool Prefix(Boiler __instance, float delta)
        {
            if (!BoilerWaterRig.TryGet(__instance, out var rig))
                return true;
            rig.SimulateCrownSheet(delta);
            return false;
        }
    }

    /// <summary>Saves the water beside the stock boiler's keys, which the stock load ignores.</summary>
    [HarmonyPatch(typeof(Boiler), nameof(Boiler.GetSaveStateData))]
    internal static class BoilerSavePatch
    {
        private static void Postfix(Boiler __instance, JObject __result)
        {
            if (BoilerWaterRig.TryGet(__instance, out var rig))
                rig.Save(__result);
        }
    }

    [HarmonyPatch(typeof(Boiler), nameof(Boiler.SetSaveStateData))]
    internal static class BoilerLoadPatch
    {
        private static void Postfix(Boiler __instance, JObject savedData)
        {
            BoilerWaterRig.OnLoaded(__instance, savedData);
        }
    }
}
