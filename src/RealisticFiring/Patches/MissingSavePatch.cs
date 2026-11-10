using System;
using HarmonyLib;
using LocoSim.Implementations;
using Newtonsoft.Json.Linq;

namespace RealisticFiring.Patches
{
    /// <summary>
    /// A save from before the mod, or before one of its controls, has no
    /// entry for that control, and the game logs "Unexpected state: Missing
    /// save data" for each as the car loads: about ten red lines for a
    /// stoker-fitted S282 the first time, harmless and alarming (audit
    /// 2026-11-09). Each of the mod's own components missing from the save
    /// is given its state as it stands, which loading then leaves as it is.
    /// </summary>
    [HarmonyPatch(typeof(SimulationFlow), nameof(SimulationFlow.SetSaveStateData))]
    internal static class MissingSavePatch
    {
        // The mod's own components' IDs begin so (StokerJetControls,
        // RealisticStokerDefinition, and the rest).
        internal const string OwnPrefix = "rf";

        private static void Prefix(SimulationFlow __instance, JObject savedData)
        {
            // Never in the way of the game's own loading.
            try
            {
                if (savedData == null || __instance.OrderedSimComps == null)
                    return;
                foreach (var component in __instance.OrderedSimComps)
                {
                    if (component == null || !component.HasSaveData || component.id == null || savedData[component.id] != null)
                        continue;
                    // The mod's: a control it added, or a component it put in
                    // a stock one's place under the stock ID (a stoker).
                    if (!component.id.StartsWith(OwnPrefix, StringComparison.Ordinal) && component.GetType().Assembly != typeof(Main).Assembly)
                        continue;
                    var state = component.GetSaveStateData();
                    if (state != null)
                        savedData[component.id] = state;
                }
            }
            catch (Exception e)
            {
                Faults.Report("filling in a save's missing controls", null, e);
            }
        }
    }
}
