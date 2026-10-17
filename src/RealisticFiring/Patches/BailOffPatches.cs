using DV.HUD;
using DV.Simulation.Brake;
using HarmonyLib;
using RealisticFiring.Sim;
using RealisticFiring.Visuals;

namespace RealisticFiring.Patches
{
    /// <summary>
    /// An engine bailed off (<see cref="BailOffs"/>): its control valve aims
    /// for the train brake's cylinder pressure less what has been bailed off.
    /// The engine's own valve and a car's both ask for this target; only a
    /// bailed engine has a bail. A tender copying the engine follows it.
    /// </summary>
    [HarmonyPatch(typeof(BrakeSystem), "TargetCylinderPressure")]
    internal static class BailOffTargetPatch
    {
        private static void Postfix(BrakeSystem __instance, ref float __result)
        {
            if (!BailOffs.TryGet(__instance, out var bail))
                return;
            var taking = bail.Requested;
            __result = bail.Apply(__result);
            if (taking && PlayerManager.Car == TrainCar.Resolve(__instance.gameObject))
                FireController.Announce(BailOffs.Notice(bail));
        }
    }

    /// <summary>Watches each cab's independent brake lever for a click to bail off, as the cab's controls are set up.</summary>
    [HarmonyPatch(typeof(InteriorControlsManager), "SetBaseControlsReferences")]
    internal static class BailOffLeverPatch
    {
        private static void Postfix(InteriorControlsManager __instance)
        {
            if (__instance.TryGetControl(InteriorControlsManager.ControlType.IndBrake, out var lever) && lever.controlImplBase != null)
                IndependentBailClick.Attach(lever.controlImplBase, __instance.Car);
        }
    }
}
