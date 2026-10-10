using System;
using System.Reflection;
using HarmonyLib;
using LocoSim.Implementations;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Patches
{
    /// <summary>
    /// The stock engine's steam admission, from the realistic cylinders: the
    /// valve's events in the reverser's notch, the clearance and cushion, and
    /// the port's opening over the admission's real crank angle (the stock
    /// takes the cut-off's share of the stroke's time).
    /// </summary>
    [HarmonyPatch(typeof(ReciprocatingSteamEngine), "SimulateValveFlow")]
    internal static class CylinderAdmissionPatch
    {
        private static bool Prefix(ReciprocatingSteamEngine __instance, float ___cutoff, float ___steamChestSteamMass, float ___cylinderVolume,
            ref float ___steamMassPerChuff, ref float ___admissionPressure)
        {
            if (!EngineCylinders.Enabled)
                return true;
            var cylinders = EngineCylinders.Of(__instance);
            var model = cylinders.Model;
            model.Set(___cutoff, __instance.exhaustFlowReadOut.Value, __instance.intakePressure.Value, __instance.intakeTemperature.Value);
            var turns = Mathf.Abs(__instance.crankRpm.Value) / 60f;
            var chest = __instance.steamChestPressureReadOut.Value;
            if (___cutoff == 0f || turns == 0f)
            {
                ___admissionPressure = chest;
                return false;
            }
            var admission = model.Admit(chest, ___steamChestSteamMass, __instance.steamChestVolume, ___cylinderVolume, turns,
                EngineCylinders.SteamVolume, EngineCylinders.SteamPressure);
            ___steamMassPerChuff = admission.Mass;
            ___admissionPressure = admission.Pressure;
            cylinders.Admitted = admission.Mass;
            return false;
        }
    }

    /// <summary>
    /// The engine's torque at speed from the indicator diagram's mean
    /// effective pressure: expansion to release, the toe, the back pressure,
    /// and the cushion's compression (the stock takes a hyperbola against
    /// one bar). Like the stock, the cylinders never drive against the gear.
    /// </summary>
    [HarmonyPatch(typeof(ReciprocatingSteamEngine), "SimulateMeanTorque")]
    internal static class CylinderMeanTorquePatch
    {
        private static bool Prefix(ReciprocatingSteamEngine __instance, float admissionPressure, float ___pistonArea, ref float __result)
        {
            if (!EngineCylinders.Enabled)
                return true;
            var mean = EngineCylinders.Of(__instance).Model.Cycle(admissionPressure).MeanEffectivePressure;
            __result = Mathf.Max(0f, mean) * 100000f * ___pistonArea * (2f / (float)Math.PI) * __instance.pistonStroke / 2f
                       * __instance.numCylinders * Mathf.Sign(__instance.reverserControl.Value);
            return false;
        }
    }

    /// <summary>
    /// Each piston's force at low speed from the same diagram: the working
    /// face's pressure at the piston's place against the other face's
    /// exhaust and cushion (the stock takes one bar).
    /// </summary>
    [HarmonyPatch]
    internal static class CylinderPistonForcePatch
    {
        private static readonly AccessTools.FieldRef<ReciprocatingSteamEngine, float> PistonArea =
            AccessTools.FieldRefAccess<ReciprocatingSteamEngine, float>("pistonArea");

        private static MethodBase TargetMethod() =>
            AccessTools.Method(AccessTools.Inner(typeof(ReciprocatingSteamEngine), "CylinderData"), "CalculatePistonForce");

        private static bool Prefix(ReciprocatingSteamEngine ___sim, float ___admissionPressure, float ___positionWithinStroke, ref float __result)
        {
            if (!EngineCylinders.Enabled || ___sim == null)
                return true;
            var cycle = EngineCylinders.Of(___sim).Model.Cycle(Mathf.Abs(___admissionPressure));
            var s = Mathf.Clamp01(___positionWithinStroke);
            var net = cycle.Working(s) - cycle.Returning(1f - s);
            __result = Mathf.Sign(___admissionPressure) * Mathf.Max(0f, net) * 100000f * PistonArea(___sim);
            return false;
        }
    }

    /// <summary>
    /// The exhaust pressure each beat carries: the cylinder's pressure at
    /// release (the stock takes the cut-off times the admission pressure).
    /// The chuffs' sound and the smoke read it.
    /// </summary>
    [HarmonyPatch(typeof(ReciprocatingSteamEngine), nameof(ReciprocatingSteamEngine.Tick))]
    internal static class CylinderExhaustPatch
    {
        private static void Postfix(ReciprocatingSteamEngine __instance, float ___admissionPressure)
        {
            if (!EngineCylinders.Enabled || !EngineCylinders.TryGet(__instance, out var cylinders))
                return;
            // The pressure acting on the pistons, after the cocks' and a crack's leaks, as the stock works it out.
            var gauge = Mathf.Max(0f, ___admissionPressure - 1f);
            var leaks = Mathf.Lerp(0f, 0.1f, __instance.cylinderCockControl.Value) * gauge
                        + (__instance.isCylinderCrackedReadOut.Value > 0f ? 0.5f * gauge : 0f);
            var cycle = cylinders.Model.Cycle(Mathf.Max(1f, ___admissionPressure - leaks));
            cylinders.Cycle = cycle;
            __instance.exhaustPressureReadOut.Value = cycle.ReleasePressure;
        }
    }
}
