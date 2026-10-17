using System.Linq;
using DV.Simulation.Cars;
using HarmonyLib;
using LocoSim.Definitions;
using RealisticFiring.Sim;

namespace RealisticFiring.Patches
{
    /// <summary>
    /// Before a fired engine builds its sim graph, adds each injector's steam
    /// valve (or water regulator, where the cab's own lever is the steam
    /// valve) as a saved cab control (<see cref="InjectorValves"/>); the
    /// injector rig works each injector from it and the stock lever. Before
    /// a tender with water valves builds its own, adds those.
    /// </summary>
    [HarmonyPatch(typeof(SimController), nameof(SimController.Initialize))]
    internal static class InjectorSteamPatch
    {
        private static void Prefix(SimController __instance, TrainCar trainCar) =>
            SetupGuard.Run(trainCar, "injector steam controls", () => Add(__instance, trainCar));

        private static void Add(SimController __instance, TrainCar trainCar)
        {
            var graph = __instance.connectionsDefinition;
            var livery = trainCar != null && trainCar.carLivery != null ? trainCar.carLivery.id : null;
            if (!Main.RealisticInjectorsEnabled || graph == null || graph.executionOrder == null)
                return;
            if (InjectorValves.HasTenderValves(livery))
            {
                Add(graph, InjectorValves.TenderWater);
                Add(graph, InjectorValves.TenderWaterLeft);
                return;
            }
            if (!InjectorValves.Fits(livery))
                return;
            for (var i = 0; i < InjectorValves.Count(livery); i++)
                Add(graph, InjectorValves.For(i, livery));
        }

        private static void Add(SimConnectionDefinition graph, StokerJetControls.Valve valve)
        {
            if (graph.executionOrder.Any(d => d != null && d.ID == valve.Id))
                return;
            var control = graph.gameObject.AddComponent<ExternalControlDefinition>();
            control.ID = valve.Id;
            control.defaultValue = valve.Default;
            control.saveState = true;
            graph.executionOrder = new SimComponentDefinition[] { control }.Concat(graph.executionOrder).ToArray();
        }
    }
}
