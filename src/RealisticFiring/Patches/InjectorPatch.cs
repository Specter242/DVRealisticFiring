using HarmonyLib;
using LocoSim.Implementations;
using RealisticFiring.Diagnostics;
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
            var start = PerfMeter.Start();
            rig.Simulate(delta);
            PerfMeter.Stop(PerfSection.Injector, start);
            return false;
        }
    }
}
