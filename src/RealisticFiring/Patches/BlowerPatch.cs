using HarmonyLib;
using LocoSim.Implementations;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Patches
{
    /// <summary>
    /// A fired engine's draft from its period blower (<see cref="BlowerFit"/>):
    /// the stock exhaust's sums with the blower's own steam, its entrainment,
    /// and the natural draft fitted, and the air pump's exhaust joining the
    /// cylinders' in the blast pipe; the whistle as the game works it out.
    /// Exhausts with no fit keep every stock step.
    /// </summary>
    [HarmonyPatch(typeof(SteamExhaust), nameof(SteamExhaust.Tick))]
    internal static class BlowerPatch
    {
        private static bool Prefix(SteamExhaust __instance)
        {
            if (!BlowerFit.TryGet(__instance, out var draft))
                return true;
            var pressure = __instance.boilerPressure.Value;
            var blower = BlowerFit.NozzleSteam(__instance, draft, out var blowerFromBoiler);
            var whistle = Mathf.Min(Mathf.InverseLerp(1f, 2f, pressure), __instance.whistleControl.Value);
            __instance.whistleFlowNormalizedReadOut.Value = whistle;
            var exhaust = __instance.exhaustFlow.Value + BlowerFit.PumpSteam(__instance);
            var air = draft.Air(exhaust, blower, __instance.damperControl.Value);
            __instance.airFlowReadOut.Value = air;
            var maxTotal = draft.MaxTotal(__instance.engineMaxFlowReader.Value);
            __instance.totalFlowNormalizedReadOut.Value = maxTotal > 0f ? (blower + exhaust + air) / maxTotal : 0f;
            __instance.steamConsumptionReadOut.Value = blowerFromBoiler + whistle * __instance.maxWhistleFlow;
            return false;
        }
    }

    /// <summary>
    /// A fired engine's air pump on period steam (<see cref="BlowerFit"/>):
    /// it compresses as the game works it out, taking its fitted steam for
    /// the share of its full rate it is pumping.
    /// </summary>
    [HarmonyPatch(typeof(SteamCompressor), nameof(SteamCompressor.Tick))]
    internal static class AirPumpPatch
    {
        private static void Postfix(SteamCompressor __instance)
        {
            if (BlowerFit.TryGetPump(__instance, out var maxSteam))
                __instance.steamConsumptionReadOut.Value = Mathf.Clamp01(__instance.productionRateNormalizedReadOut.Value) * maxSteam;
        }
    }
}
