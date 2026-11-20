using System;
using System.Runtime.CompilerServices;
using DV.ModularAudioCar;
using HarmonyLib;
using RealisticFiring.Visuals;

namespace RealisticFiring.AdapterCheck
{
    /// <summary>
    /// The chuffs (Visuals/ChuffPlayer.cs) read the game's chuff reader's
    /// private ports, its loops' active flag, and its loop update; a game
    /// update that renames one fails only when the first engine loads, so
    /// they are looked for here.
    /// </summary>
    internal static class ChuffChecks
    {
        internal static void Run(Action<bool, string> check)
        {
            var reader = typeof(ChuffClipsSimReader);
            var missing = new System.Collections.Generic.List<string>();
            foreach (var name in new[] { "exhaustPressurePort", "chuffFrequencyPort", "cylinderWaterNormalizedPort", "cylinderCockControlPort", "ashesInPipesPort" })
                if (AccessTools.Field(reader, name) == null)
                    missing.Add(name);
            if (AccessTools.Method(reader, "UpdateLoops", Type.EmptyTypes) == null)
                missing.Add("UpdateLoops()");
            if (AccessTools.Field(typeof(ChuffClipsSimReader.ChuffLoop), "isActive") == null)
                missing.Add("ChuffLoop.isActive");
            check(missing.Count == 0, "the chuff reader's ports, loop flag and loop update are where the chuffs read them"
                                      + (missing.Count > 0 ? $" (missing: {string.Join(", ", missing)})" : ""));

            string failure = null;
            try
            {
                RuntimeHelpers.RunClassConstructor(typeof(ChuffPlayer).TypeHandle);
            }
            catch (Exception e)
            {
                failure = (e.InnerException ?? e).Message;
            }
            check(failure == null, "the chuffs bind the reader's private members" + (failure != null ? $" (not: {failure})" : ""));
        }
    }
}
