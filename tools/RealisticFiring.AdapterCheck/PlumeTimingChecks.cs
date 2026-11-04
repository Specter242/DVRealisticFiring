using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RealisticFiring.Diagnostics;
using RealisticStackEffects;
using RealisticFiring.Visuals;

namespace RealisticFiring.AdapterCheck
{
    internal static class PlumeTimingChecks
    {
        internal static void Run(Action<bool, string> check)
        {
            var elapsed = (long[])AccessTools.Field(typeof(PerfMeter), "Elapsed").GetValue(null);
            var children = (long[])AccessTools.Field(typeof(PerfMeter), "PlumeElapsed").GetValue(null);
            var calls = (int[])AccessTools.Field(typeof(PerfMeter), "PlumeCalls").GetValue(null);
            var original = (long[])elapsed.Clone();
            var oldChildren = (long[])children.Clone();
            var oldCalls = (int[])calls.Clone();
            var oldSettings = StackHost.Settings;
            var oldCallback = StackHost.PhaseTimed;
            var oldInterval = RealisticFiring.Main.Settings.FireStatusLogInterval;
            try
            {
                Array.Clear(elapsed, 0, elapsed.Length);
                Array.Clear(children, 0, children.Length);
                Array.Clear(calls, 0, calls.Length);
                elapsed[(int)PerfSection.ChimneyPlume] = 100;
                children[(int)PlumePhase.Motion] = 80;
                children[(int)PlumePhase.Shadows] = 30;
                check(PerfMeter.TotalTicks == 100,
                    "nested plume motion and shadow timings do not inflate the main-thread total");
                // Use RF's real settings adapter. RF and SE build separate
                // copies of their shared enums, so a fake using the other
                // assembly's enum cannot implement this interface.
                StackHost.Settings = (IStackSettings)Activator.CreateInstance(
                    typeof(FiringStackHost).GetNestedType("StackSettings", BindingFlags.NonPublic), true);
                RealisticFiring.Main.Settings.FireStatusLogInterval = 0;
                var received = 0;
                StackHost.PhaseTimed = (phase, started) => { received++; PerfMeter.StopPlumePhase(phase, started); };
                using (StackHost.TimePhase(PlumePhase.Weather)) { }
                check(received == 0, "plume sub-timing stays off when status logging is disabled");
                RealisticFiring.Main.Settings.FireStatusLogInterval = 10;
                using (StackHost.TimePhase(PlumePhase.Weather)) { }
                check(received == 1 && calls[(int)PlumePhase.Weather] == 1 && PerfMeter.TotalTicks == 100,
                    "enabled plume sub-timing records one child call without changing the parent total");
                check(PerfMeter.PlumeBreakdown(0, 0).Contains("Weather 0.00 (0.0 calls/frame)")
                      && Enum.GetValues(typeof(PlumePhase)).Cast<PlumePhase>().All(p => PerfMeter.PlumeBreakdown(1, 1).Contains(p.ToString())),
                    "plume report names every subphase and handles a zero-frame reporting window");
            }
            finally
            {
                Array.Copy(original, elapsed, elapsed.Length);
                Array.Copy(oldChildren, children, children.Length);
                Array.Copy(oldCalls, calls, calls.Length);
                StackHost.Settings = oldSettings;
                StackHost.PhaseTimed = oldCallback;
                RealisticFiring.Main.Settings.FireStatusLogInterval = oldInterval;
            }
        }
    }
}
