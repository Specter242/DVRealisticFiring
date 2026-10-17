using System.Linq;
using DV.Simulation.Cars;
using HarmonyLib;
using LocoSim.Definitions;
using RealisticFiring.Sim;

namespace RealisticFiring.Patches
{
    /// <summary>
    /// Before a loco whose prototype had a feedwater heater builds its sim
    /// graph, adds the pump's operating valve as a saved cab control; the
    /// injector rig runs the pump from it.
    /// </summary>
    [HarmonyPatch(typeof(SimController), nameof(SimController.Initialize))]
    internal static class FeedPumpPatch
    {
        private static void Prefix(SimController __instance, TrainCar trainCar) =>
            SetupGuard.Run(trainCar, "feed pump's control", () => Add(__instance, trainCar));

        private static void Add(SimController __instance, TrainCar trainCar)
        {
            var graph = __instance.connectionsDefinition;
            if (!Main.RealisticInjectorsEnabled || trainCar == null || graph == null || graph.executionOrder == null)
                return;
            if (!FeedwaterFit.TryGet(trainCar.carLivery.id, out _) || graph.executionOrder.Any(d => d != null && d.ID == FeedwaterFit.Valve.Id))
                return;
            var control = graph.gameObject.AddComponent<ExternalControlDefinition>();
            control.ID = FeedwaterFit.Valve.Id;
            control.defaultValue = FeedwaterFit.Valve.Default;
            control.saveState = true;
            graph.executionOrder = new SimComponentDefinition[] { control }.Concat(graph.executionOrder).ToArray();
        }
    }
}
