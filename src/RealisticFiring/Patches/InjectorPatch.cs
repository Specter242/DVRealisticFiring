using HarmonyLib;
using LocoSim.Implementations;
#if !PUBLIC_RELEASE
using RealisticFiring.Diagnostics;
#endif
using RealisticFiring.Sim;

namespace RealisticFiring.Patches
{
    /// <summary>
    /// Runs a boiler's realistic injector in place of the stock injector step,
    /// for boilers whose injector is worked by a cab control. Every other
    /// boiler keeps the stock step.
    /// </summary>
    [HarmonyPatch(typeof(Boiler), "SimulateInjector")]
    internal static class InjectorPatch
    {
        private static bool Prefix(Boiler __instance, float delta)
        {
            if (!InjectorRig.TryGet(__instance, out var rig))
                return true;
#if !PUBLIC_RELEASE
            var start = PerfMeter.Start();
#endif
            rig.Simulate(delta);
#if !PUBLIC_RELEASE
            PerfMeter.Stop(PerfSection.Injector, start);
#endif
            return false;
        }
    }
}
