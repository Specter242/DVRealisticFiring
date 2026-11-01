using HarmonyLib;
using LocoSim.Implementations;
using RealisticFiring.Diagnostics;

namespace RealisticFiring.Patches
{
    /// <summary>Includes the stock engine tick and RF's cylinder work, unlike the firebox timer.</summary>
    [HarmonyPatch(typeof(ReciprocatingSteamEngine), nameof(ReciprocatingSteamEngine.Tick))]
    internal static class EngineTimingPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(out long __state) => __state = PerfMeter.Start();

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(long __state) => PerfMeter.Stop(PerfSection.EngineTick, __state);
    }
}
