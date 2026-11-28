using System.Linq;
using DV.Simulation.Cars;
using HarmonyLib;
using LocoSim.Definitions;
using RealisticFiring.Sim;

namespace RealisticFiring.Patches
{
    /// <summary>
    /// Before an S282 converted to oil builds its sim graph
    /// (<see cref="S282OilConversion"/>), adds its firing valve, a saved cab
    /// control, and its oil feed (<see cref="S282OilFeedDefinition"/>) ahead
    /// of the firebox, as a CCL oil burner has its stoker. The firebox
    /// becomes the oil burner's (FireboxDefinitionPatch), and its atomizer,
    /// heater, and peep hole come as on any oil burner (OilControlsPatch).
    /// </summary>
    [HarmonyPatch(typeof(SimController), nameof(SimController.Initialize))]
    internal static class S282OilBurnerPatch
    {
        private static void Prefix(SimController __instance, TrainCar trainCar) =>
            SetupGuard.Run(trainCar, "S282 oil burner's feed", () => Add(__instance, trainCar));

        private static void Add(SimController controller, TrainCar car)
        {
            var graph = controller.connectionsDefinition;
            if (graph == null || graph.executionOrder == null || !Main.Session.RealisticOilBurners || !S282OilConversion.IsOil(car))
                return;
            if (graph.executionOrder.Any(d => d != null && d.ID == S282OilConversion.FeedId))
                return;
            var firebox = graph.executionOrder.OfType<FireboxDefinition>().FirstOrDefault();
            if (firebox == null || controller.firebox == null)
            {
                Main.Mod.Logger.Warning($"{car.carLivery.id}: no firebox to fit the oil burner's feed to; it stays a coal engine without a fire");
                return;
            }

            var coalMultiplier = controller.firebox.coalConsumptionMultiplier > 0f ? controller.firebox.coalConsumptionMultiplier : 1f;
            var feed = graph.gameObject.AddComponent<S282OilFeedDefinition>();
            feed.ID = S282OilConversion.FeedId;
            feed.TenderPerFireboxKg = coalMultiplier * S282OilConversion.LitresPerFireboxKg;

            var valve = graph.gameObject.AddComponent<ExternalControlDefinition>();
            valve.ID = S282OilConversion.FiringValve.Id;
            valve.defaultValue = S282OilConversion.FiringValve.Default;
            valve.saveState = true;

            var order = graph.executionOrder.ToList();
            order.Insert(order.IndexOf(firebox), feed);
            order.Insert(0, valve);
            graph.executionOrder = order.ToArray();
            graph.portReferenceConnections = (graph.portReferenceConnections ?? new PortReferenceConnection[0]).Concat(new[]
            {
                new PortReferenceConnection($"{feed.ID}.{feed.Control.ID}", S282OilConversion.FiringValve.PortId),
            }).ToArray();
            Main.Mod.Logger.Log($"{car.carLivery.id}: oil burner's feed and firing valve fitted, {feed.TenderPerFireboxKg:F2} L of oil from the tender a firebox kg");
        }
    }
}
