using System.Linq;
using DV.Simulation.Cars;
using HarmonyLib;
using LocoSim.Definitions;
using RealisticFiring.Sim;

namespace RealisticFiring.Patches
{
    /// <summary>
    /// Before a supported loco with a stoker builds its sim graph, adds the
    /// stoker's jet valves to it as saved external controls, so the game
    /// stores and restores them like any cab control. The stock S282, built
    /// without a stoker, is fitted with the mod's own: its engine throttle,
    /// the jets, and the stoker itself, wired to the boiler and firebox, ahead
    /// of the firebox in the graph; it feeds from the tender's coal pile.
    /// </summary>
    [HarmonyPatch(typeof(SimController), nameof(SimController.Initialize))]
    internal static class StokerControlsPatch
    {
        private const string CclStokerType = "SteamMechanicalStokerDefinitionInternal";
        private const string RetrofitLivery = "LocoS282A";

        // A retrofitted stoker feeds a quarter more than the firebox can burn at most.
        private const float RetrofitFeedMargin = 1.25f;

        private static void Prefix(SimController __instance, TrainCar trainCar)
        {
            var graph = __instance.connectionsDefinition;
            if (!Main.ReplacementEnabled || !FireboxRegistry.IsSupported(trainCar) || graph == null || graph.executionOrder == null)
                return;
            if (graph.executionOrder.Any(d => d != null && d.ID == StokerJetControls.Master.Id))
                return;
            var hasStoker = graph.executionOrder.Any(d => d != null && d.GetType().Name == CclStokerType);
            if (!hasStoker && !Retrofit(__instance, trainCar, graph))
                return;

            var valves = StokerJetControls.Valves.Append(StokerJetControls.Reverse).Select(valve => Control(graph, valve));
            graph.executionOrder = valves.Concat(graph.executionOrder).ToArray();
        }

        private static SimComponentDefinition Control(SimConnectionDefinition graph, StokerJetControls.Valve valve)
        {
            var control = graph.gameObject.AddComponent<ExternalControlDefinition>();
            control.ID = valve.Id;
            control.defaultValue = valve.Default;
            control.saveState = true;
            return control;
        }

        // Fits the stock S282 with the engine throttle and the mod's stoker,
        // just ahead of the firebox. Returns whether it did.
        private static bool Retrofit(SimController controller, TrainCar car, SimConnectionDefinition graph)
        {
            if (!Main.Session.RetrofitS282Stoker || car.carLivery.id != RetrofitLivery)
                return false;
            var firebox = graph.executionOrder.OfType<FireboxDefinition>().FirstOrDefault();
            var boiler = graph.executionOrder.OfType<BoilerDefinition>().FirstOrDefault();
            if (firebox == null || boiler == null || controller.firebox == null)
                return false;

            var multiplier = controller.firebox.coalConsumptionMultiplier > 0f ? controller.firebox.coalConsumptionMultiplier : 1f;
            var stoker = graph.gameObject.AddComponent<RealisticStokerDefinition>();
            stoker.ID = StokerJetControls.StokerId;
            stoker.CoalMultiplier = multiplier;
            stoker.MaxTransferRate = RetrofitFeedMargin * firebox.maxCoalCapacity / firebox.burnTime * multiplier;

            var order = graph.executionOrder.ToList();
            order.Insert(order.IndexOf(firebox), stoker);
            order.Insert(0, Control(graph, StokerJetControls.Engine));
            graph.executionOrder = order.ToArray();

            string Ref(PortReferenceDefinition reference) => $"{stoker.ID}.{reference.ID}";
            graph.portReferenceConnections = (graph.portReferenceConnections ?? new PortReferenceConnection[0]).Concat(new[]
            {
                new PortReferenceConnection(Ref(stoker.Control), StokerJetControls.Engine.PortId),
                new PortReferenceConnection(Ref(stoker.SteamPressure), $"{boiler.ID}.{boiler.pressureReadOut.ID}"),
                new PortReferenceConnection(Ref(stoker.FireboxCoalLevel), $"{firebox.ID}.{firebox.coalLevelReadOut.ID}"),
                new PortReferenceConnection(Ref(stoker.FireboxCoalCapacity), $"{firebox.ID}.{firebox.coalCapacityReadOut.ID}"),
                new PortReferenceConnection(Ref(stoker.FireboxCoalControl), $"{firebox.ID}.{firebox.coalControlExtIn.ID}"),
            }).ToArray();
            return true;
        }
    }
}
