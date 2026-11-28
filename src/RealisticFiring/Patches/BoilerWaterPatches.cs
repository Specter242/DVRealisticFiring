using DV.Simulation.Cars;
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
            // Not on a guest, whose boiler the host simulates: this prefix
            // runs though the tick is skipped, and it traded heat with, and
            // wore, a vessel that is only a shadow (audit 2026-11-09).
            if (Net.IsClient)
                return;
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

    /// <summary>
    /// The stock boiler's load. A save by 1.26.0 or earlier keeps the water
    /// beside the stock boiler's keys (<c>rfSolids</c> and the rest), read
    /// here; a save without them starts the water fresh, until the car's own
    /// entry (<see cref="BoilerWaterSavePatch"/>) is read after it.
    /// </summary>
    [HarmonyPatch(typeof(Boiler), nameof(Boiler.SetSaveStateData))]
    internal static class BoilerLoadPatch
    {
        private static void Postfix(Boiler __instance, JObject savedData)
        {
            BoilerWaterRig.OnLoaded(__instance, savedData);
        }
    }

    /// <summary>
    /// Saves each boiler's water in the car's own save, under
    /// <see cref="BoilerWaterRig.CarSaveKey"/>, not in the stock boiler's save
    /// object: other mods read that object as the game's alone (JimTaplin's
    /// Rearranged S282 3.0.0 refused a boiler with more than its four stock
    /// fields when syncing a locomotive in multiplayer). The game ignores the
    /// unknown key, so removing the mod leaves saves readable.
    /// </summary>
    [HarmonyPatch(typeof(SimCarStateSave), nameof(SimCarStateSave.GetStateSaveData))]
    internal static class BoilerWaterSavePatch
    {
        internal static readonly AccessTools.FieldRef<SimCarStateSave, SimulationFlow> Flow =
            AccessTools.FieldRefAccess<SimCarStateSave, SimulationFlow>("simFlow");

        private static void Postfix(SimCarStateSave __instance, JObject __result)
        {
            try
            {
                BoilerWaterRig.SaveCar(Flow(__instance), __result);
            }
            catch (System.Exception e)
            {
                Faults.Report("saving the boiler water", __instance != null ? __instance.GetComponent<TrainCar>() : null, e);
            }
        }
    }

    /// <summary>After the stock car's load, and so after its boilers': the water as the car saved it.</summary>
    [HarmonyPatch(typeof(SimCarStateSave), nameof(SimCarStateSave.SetStateSaveData))]
    internal static class BoilerWaterLoadPatch
    {
        private static void Postfix(SimCarStateSave __instance, JObject data)
        {
            try
            {
                BoilerWaterRig.LoadCar(BoilerWaterSavePatch.Flow(__instance), data);
            }
            catch (System.Exception e)
            {
                Faults.Report("loading the boiler water", __instance != null ? __instance.GetComponent<TrainCar>() : null, e);
            }
        }
    }
}
