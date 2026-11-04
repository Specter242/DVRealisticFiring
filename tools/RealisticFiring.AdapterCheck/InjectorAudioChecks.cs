using System;
using System.Linq;
using HarmonyLib;
using RealisticFiring.Core;
using RealisticFiring.Visuals;

namespace RealisticFiring.AdapterCheck
{
    internal static class InjectorAudioChecks
    {
        internal static void Run(Action<bool, string> check)
        {
            var update = AccessTools.Method(typeof(InjectorOverflow), "UpdateOverflow");
            var instructions = PatchProcessor.GetOriginalInstructions(update);
            check(instructions.Any(i => i.Calls(AccessTools.Method(typeof(InjectorOverflowAudio), nameof(InjectorOverflowAudio.Step)))),
                "live injector overflow audio uses the tested gain and fade model");
            check(instructions.Any(i => i.Calls(AccessTools.PropertyGetter(typeof(InjectorSpec), nameof(InjectorSpec.WaterPerSteam)))),
                "injector overflow audio derives steam capacity from the injector's own water/steam ratio");
        }
    }
}
