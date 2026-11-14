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

    /// <summary>The stock boiler tick with the mod's water rig in it; the injector has its own section.</summary>
    [HarmonyPatch(typeof(Boiler), nameof(Boiler.Tick))]
    internal static class BoilerTimingPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(out (long Start, long Injector) __state) =>
            __state = (PerfMeter.Start(), PerfMeter.Gathered(PerfSection.Injector));

        [HarmonyPriority(Priority.Last)]
        private static void Postfix((long Start, long Injector) __state) =>
            PerfMeter.StopLess(PerfSection.Boiler, __state.Start, PerfSection.Injector, __state.Injector);
    }

    /// <summary>The stock exhaust tick with the mod's blower and draft in it.</summary>
    [HarmonyPatch(typeof(SteamExhaust), nameof(SteamExhaust.Tick))]
    internal static class DraftTimingPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(out long __state) => __state = PerfMeter.Start();

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(long __state) => PerfMeter.Stop(PerfSection.Draft, __state);
    }
}
