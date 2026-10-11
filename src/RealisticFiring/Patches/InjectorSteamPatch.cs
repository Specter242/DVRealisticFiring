using System.Linq;
using DV.Simulation.Cars;
using HarmonyLib;
using LocoSim.Definitions;
using RealisticFiring.Sim;

namespace RealisticFiring.Patches
{
    /// <summary>
    /// Before a fired engine builds its sim graph, adds each injector's steam
    /// valve as a saved cab control (<see cref="InjectorValves"/>); the
    /// injector rig works each injector from it and the stock lever.
    /// </summary>
    [HarmonyPatch(typeof(SimController), nameof(SimController.Initialize))]
    internal static class InjectorSteamPatch
    {
        private static void Prefix(SimController __instance, TrainCar trainCar)
        {
            var graph = __instance.connectionsDefinition;
            var livery = trainCar != null && trainCar.carLivery != null ? trainCar.carLivery.id : null;
            if (!Main.RealisticInjectorsEnabled || graph == null || graph.executionOrder == null || !InjectorValves.Fits(livery))
                return;
            for (var i = 0; i < InjectorValves.Count(livery); i++)
            {
                var valve = InjectorValves.For(i);
                if (graph.executionOrder.Any(d => d != null && d.ID == valve.Id))
                    continue;
                var control = graph.gameObject.AddComponent<ExternalControlDefinition>();
                control.ID = valve.Id;
                control.defaultValue = valve.Default;
                control.saveState = true;
                graph.executionOrder = new SimComponentDefinition[] { control }.Concat(graph.executionOrder).ToArray();
            }
        }
    }
}
