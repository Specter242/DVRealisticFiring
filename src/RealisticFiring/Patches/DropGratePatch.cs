using System.Linq;
using DV.Simulation.Cars;
using HarmonyLib;
using LocoSim.Definitions;
using RealisticFiring.Sim;

namespace RealisticFiring.Patches
{
    /// <summary>
    /// Before a supported loco builds its sim graph, adds the drop grate's
    /// lever as a saved cab control and the ashpan's hopper lever; the
    /// realistic firebox opens the drop grate and the ashpan from them (see
    /// <see cref="RealisticFirebox.DropGrateControl"/> and
    /// <see cref="RealisticFirebox.AshpanControl"/>).
    /// </summary>
    [HarmonyPatch(typeof(SimController), nameof(SimController.Initialize))]
    internal static class DropGratePatch
    {
        private static void Prefix(SimController __instance, TrainCar trainCar) =>
            SetupGuard.Run(trainCar, "drop grate and ashpan controls", () => Add(__instance, trainCar));

        private static void Add(SimController __instance, TrainCar trainCar)
        {
            var graph = __instance.connectionsDefinition;
            if (!Main.ReplacementEnabled || !FireboxRegistry.IsSupported(trainCar) || graph == null || graph.executionOrder == null)
                return;
            if (!graph.executionOrder.OfType<FireboxDefinition>().Any()
                || graph.executionOrder.Any(d => d != null && d.ID == RealisticFirebox.DropGrateControl.Id))
                return;
            var dropGrate = graph.gameObject.AddComponent<ExternalControlDefinition>();
            dropGrate.ID = RealisticFirebox.DropGrateControl.Id;
            dropGrate.defaultValue = RealisticFirebox.DropGrateControl.Default;
            dropGrate.saveState = true;
            // The ashpan's hopper lever springs shut, so it is not saved.
            var ashpan = graph.gameObject.AddComponent<ExternalControlDefinition>();
            ashpan.ID = RealisticFirebox.AshpanControl.Id;
            ashpan.defaultValue = RealisticFirebox.AshpanControl.Default;
            ashpan.saveState = false;
            var added = new System.Collections.Generic.List<SimComponentDefinition> { dropGrate, ashpan };
            // The over-fire jets' valve, on engines whose box can have them;
            // saved, so a valve left open stays open.
            if (FireboxCharacters.For(trainCar).Jets != null)
            {
                var jets = graph.gameObject.AddComponent<ExternalControlDefinition>();
                jets.ID = RealisticFirebox.JetsControl.Id;
                jets.defaultValue = RealisticFirebox.JetsControl.Default;
                jets.saveState = true;
                added.Add(jets);
            }
            graph.executionOrder = added.Concat(graph.executionOrder).ToArray();
        }
    }
}
