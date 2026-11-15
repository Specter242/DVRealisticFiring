using System.Linq;
using DV.Simulation.Cars;
using HarmonyLib;
using LocoSim.Definitions;
using RealisticFiring.Sim;

namespace RealisticFiring.Patches
{
    /// <summary>
    /// Before an engine with try cocks builds its sim graph, adds a cab
    /// control for each (<see cref="TryCockRig"/>). Not saved: a cock left
    /// open is shut after a reload, as a crew shuts it before leaving.
    /// </summary>
    [HarmonyPatch(typeof(SimController), nameof(SimController.Initialize))]
    internal static class TryCockPatch
    {
        private static void Prefix(SimController __instance, TrainCar trainCar) =>
            SetupGuard.Run(trainCar, "try cocks' controls", () => Add(__instance, trainCar));

        private static void Add(SimController __instance, TrainCar trainCar)
        {
            var graph = __instance.connectionsDefinition;
            var livery = trainCar != null && trainCar.carLivery != null ? trainCar.carLivery.id : null;
            if (!TryCockRig.Fits(livery) || graph == null || graph.executionOrder == null)
                return;
            if (!graph.executionOrder.OfType<BoilerDefinition>().Any()
                || graph.executionOrder.Any(d => d != null && d.ID == TryCockRig.Valves[0].Id))
                return;
            var added = TryCockRig.Valves.Select(valve =>
            {
                var control = graph.gameObject.AddComponent<ExternalControlDefinition>();
                control.ID = valve.Id;
                control.defaultValue = valve.Default;
                control.saveState = false;
                return (SimComponentDefinition)control;
            }).ToList();
            graph.executionOrder = added.Concat(graph.executionOrder).ToArray();
        }
    }
}
