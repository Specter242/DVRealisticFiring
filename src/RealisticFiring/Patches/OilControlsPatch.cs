using System.Linq;
using DV.Simulation.Cars;
using HarmonyLib;
using LocoSim.Definitions;
using RealisticFiring.Sim;

namespace RealisticFiring.Patches
{
    /// <summary>
    /// Before an oil burner builds its sim graph, adds the burner's atomizer
    /// and oil heater valves as saved cab controls (see
    /// <see cref="OilBurnerFit"/>); the oil firebox reads them.
    /// </summary>
    [HarmonyPatch(typeof(SimController), nameof(SimController.Initialize))]
    internal static class OilControlsPatch
    {
        private static void Prefix(SimController __instance, TrainCar trainCar)
        {
            var graph = __instance.connectionsDefinition;
            if (graph == null || graph.executionOrder == null || !OilBurnerFit.IsOilBurner(trainCar))
                return;
            if (graph.executionOrder.Any(d => d != null && d.ID == OilBurnerFit.Atomizer.Id))
                return;
            var added = new[] { OilBurnerFit.Atomizer, OilBurnerFit.Heater }.Select(valve =>
            {
                var control = graph.gameObject.AddComponent<ExternalControlDefinition>();
                control.ID = valve.Id;
                control.defaultValue = valve.Default;
                control.saveState = true;
                return (SimComponentDefinition)control;
            }).ToArray();
            graph.executionOrder = added.Concat(graph.executionOrder).ToArray();
        }
    }
}
