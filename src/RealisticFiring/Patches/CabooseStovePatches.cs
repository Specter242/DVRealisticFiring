using System;
using DV.Simulation.Cars;
using DV.ThingTypes;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Patches
{
    /// <summary>
    /// Attaches <see cref="CabooseStoveRig"/> whenever a caboose wakes up.
    /// </summary>
    [HarmonyPatch(typeof(CabooseController), "Awake")]
    internal static class CabooseAwakePatch
    {
        private static void Postfix(CabooseController __instance)
        {
            if (__instance == null)
                return;
            var car = __instance.GetComponent<TrainCar>();
            if (car == null)
                return;
            SetupGuard.Run(car, "caboose stove", () => CabooseStoveRig.Attach(car));
        }
    }

    /// <summary>
    /// A caboose's stove state in its car's save, under
    /// <see cref="CabooseStoveSave.SaveKey"/>; cold empty stoves write nothing,
    /// the game ignores the unknown key, so removing the mod leaves saves
    /// cleanly readable, and the state is kept even while
    /// <see cref="Settings.CabooseStove"/> is off. The stock caboose has a
    /// damage controller, so it saves through <see cref="SimCarStateSave"/>;
    /// the game gives <see cref="CarStateSave"/> only to cars with cargo or
    /// without one (TrainCar), which a custom caboose may be.
    /// </summary>
    internal static class CabooseStoveSaveData
    {
        internal static void Write(Component save, JObject result)
        {
            if (save == null || result == null)
                return;
            var car = save.GetComponent<TrainCar>();
            try
            {
                if (!CabooseStoveRig.TryGet(car, out var rig))
                    return;
                var state = CabooseStoveSave.WriteForSave(rig);
                if (state != null)
                    result[CabooseStoveSave.SaveKey] = state;
            }
            catch (Exception e)
            {
                Faults.Report("saving a caboose stove", car, e);
            }
        }

        /// <summary>Restores the stove, tolerating older saves without the key.</summary>
        internal static void Read(Component save, JObject data)
        {
            if (save == null || data == null)
                return;
            var car = save.GetComponent<TrainCar>();
            try
            {
                var saved = data[CabooseStoveSave.SaveKey] as JObject;
                if (saved == null || car == null)
                    return;
                var rig = CabooseStoveRig.TryGet(car, out var existing)
                    ? existing
                    : (car.carLivery != null && CarTypes.IsCaboose(car.carLivery)) || car.GetComponent<CabooseController>() != null
                        ? CabooseStoveRig.Attach(car)
                        : null;
                if (rig != null)
                    CabooseStoveSave.LoadFromSave(saved, rig, Ambient.Air);
            }
            catch (Exception e)
            {
                Faults.Report("loading a caboose stove", car, e);
            }
        }
    }

    [HarmonyPatch(typeof(SimCarStateSave), nameof(SimCarStateSave.GetStateSaveData))]
    internal static class CabooseStoveSimSavePatch
    {
        private static void Postfix(SimCarStateSave __instance, JObject __result) => CabooseStoveSaveData.Write(__instance, __result);
    }

    [HarmonyPatch(typeof(SimCarStateSave), nameof(SimCarStateSave.SetStateSaveData))]
    internal static class CabooseStoveSimLoadPatch
    {
        private static void Postfix(SimCarStateSave __instance, JObject data) => CabooseStoveSaveData.Read(__instance, data);
    }

    [HarmonyPatch(typeof(CarStateSave), nameof(CarStateSave.GetCarStateSaveData))]
    internal static class CabooseStoveSavePatch
    {
        private static void Postfix(CarStateSave __instance, JObject __result) => CabooseStoveSaveData.Write(__instance, __result);
    }

    [HarmonyPatch(typeof(CarStateSave), nameof(CarStateSave.SetCarStateSaveData))]
    internal static class CabooseStoveLoadPatch
    {
        private static void Postfix(CarStateSave __instance, JObject data) => CabooseStoveSaveData.Read(__instance, data);
    }
}
