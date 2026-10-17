using System.Linq;
using DV.Simulation.Cars;
using HarmonyLib;
using LocoSim.Definitions;
using RealisticFiring.Sim;

namespace RealisticFiring.Patches
{
    /// <summary>
    /// Before an oil burner builds its sim graph, adds the burner's atomizer,
    /// oil heater, and shop steam valves, and the peep hole's cover where its
    /// sim lacks one, as saved cab controls (see
    /// <see cref="OilBurnerFit"/>); the oil firebox reads them.
    /// </summary>
    [HarmonyPatch(typeof(SimController), nameof(SimController.Initialize))]
    internal static class OilControlsPatch
    {
        private static void Prefix(SimController __instance, TrainCar trainCar) =>
            SetupGuard.Run(trainCar, "oil burner's controls", () => Add(__instance, trainCar));

        private static void Add(SimController __instance, TrainCar trainCar)
        {
            var graph = __instance.connectionsDefinition;
            if (graph == null || graph.executionOrder == null || !OilBurnerFit.IsOilBurner(trainCar))
                return;
            if (graph.executionOrder.Any(d => d != null && d.ID == OilBurnerFit.Atomizer.Id))
                return;
            var added = new[] { OilBurnerFit.Atomizer, OilBurnerFit.Heater, OilBurnerFit.ShopSteam }.Select(valve =>
            {
                var control = graph.gameObject.AddComponent<ExternalControlDefinition>();
                control.ID = valve.Id;
                control.defaultValue = valve.Default;
                control.saveState = true;
                return (SimComponentDefinition)control;
            }).ToList();
            // The peep hole's cover, where a cab has one wired to a control its sim lacks.
            if (!graph.executionOrder.Any(d => d != null && d.ID == OilBurnerFit.PeepHoleId))
            {
                var peepHole = graph.gameObject.AddComponent<ExternalControlDefinition>();
                peepHole.ID = OilBurnerFit.PeepHoleId;
                peepHole.saveState = true;
                added.Add(peepHole);
            }
            graph.executionOrder = added.Concat(graph.executionOrder).ToArray();
        }
    }
}
