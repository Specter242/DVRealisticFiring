using System.Linq;
using DV.Simulation.Cars;
using DV.Wheels;
using HarmonyLib;
using LocoSim.Definitions;
using RealisticFiring.Core;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Patches
{
 [HarmonyPatch(typeof(SimController), nameof(SimController.Initialize))]
    internal static class BoosterPatch
    {
        /// <summary>Rearranged S282's Harmony ID (its UMM mod ID).</summary>
        internal const string RearrangedS282Id = "RearrangedS282";

        [HarmonyAfter(RearrangedS282Id)]
        [HarmonyPriority(Priority.Low)]
        private static void Prefix(SimController __instance, TrainCar trainCar) =>
            SetupGuard.Run(trainCar, "booster", () => Add(__instance, trainCar));

        private static void Add(SimController __instance, TrainCar trainCar)
        {
            var graph = __instance.connectionsDefinition;
            if (trainCar == null || trainCar.carLivery == null || LeftStock.IsExcluded(trainCar) || graph == null || graph.executionOrder == null || graph.connections == null)
                return;
            if (!BoosterFit.TryGet(trainCar.carLivery.id, out var fit) || graph.executionOrder.Any(d => d != null && d.ID == BoosterFit.BoosterId))
                return;

            var engine = graph.executionOrder.OfType<ReciprocatingSteamEngineDefinition>().FirstOrDefault();
            var traction = graph.executionOrder.OfType<TractionDefinition>().FirstOrDefault();
            var boiler = graph.executionOrder.OfType<BoilerDefinition>().FirstOrDefault();
            var reverser = graph.executionOrder.OfType<ReverserDefinition>().FirstOrDefault();
            var sander = graph.executionOrder.OfType<SanderDefinition>().FirstOrDefault();
            var tractionIn = traction != null ? $"{traction.ID}.{traction.torqueIn.ID}" : null;
            var drive = graph.connections.FirstOrDefault(c => c != null && c.fullPortIdIn == tractionIn);
            if (engine == null || boiler == null || reverser == null || drive == null)
            {
                Main.Mod.Logger.Warning($"{trainCar.carLivery.id}: no booster fitted: its graph lacks a steam engine, boiler, reverser, or drive to the traction");
                return;
            }
            var wheelslip = trainCar.GetComponentInChildren<WheelslipController>(true);

            var booster = graph.gameObject.AddComponent<RealisticBoosterDefinition>();
            booster.ID = BoosterFit.BoosterId;
            booster.MinCutoff = engine.minCutoff;
            booster.MaxCutoff = engine.maxCutoff;
            booster.TrailerLoad = fit.TrailerLoad;
            booster.DrivingAxles = fit.DrivingAxles;
            booster.PoweredAxlesPortId = wheelslip != null ? wheelslip.numberOfPoweredAxlesPortId : null;
            // As built: Rearranged sets its count for an arrangement only after the graph is built.
            var axlesId = BoosterWiring.ComponentOf(booster.PoweredAxlesPortId);
            var axles = axlesId != null ? graph.executionOrder.OfType<ConfigurablePortDefinition>().FirstOrDefault(d => d != null && d.ID == axlesId) : null;
            booster.BuiltPoweredAxles = axles != null && axles.value > 0f ? Mathf.RoundToInt(axles.value) : 0;

            // After whatever feeds the traction: the engine on a stock S282,
            // Rearranged's sum of its engines' torque on its graph.
            var order = graph.executionOrder.ToList();
            var at = BoosterWiring.InsertAt(order.Select(d => d != null ? d.ID : null).ToList(), drive.fullPortIdOut, engine.ID);
            order.Insert(at > 0 ? at : order.IndexOf(engine) + 1, booster);
            order.Insert(0, Control(graph, BoosterFit.Latch));
            order.Insert(0, Control(graph, BoosterFit.Idle));
            graph.executionOrder = order.ToArray();

            // The main engine's torque now goes through the booster.
            var mainTorque = drive.fullPortIdOut;
            drive.fullPortIdOut = $"{booster.ID}.{booster.TorqueOut.ID}";

            string Ref(PortReferenceDefinition reference) => $"{booster.ID}.{reference.ID}";
            var references = new[]
            {
                new PortReferenceConnection(Ref(booster.MainTorque), mainTorque),
                new PortReferenceConnection(Ref(booster.Latch), BoosterFit.Latch.PortId),
                new PortReferenceConnection(Ref(booster.Idle), BoosterFit.Idle.PortId),
                new PortReferenceConnection(Ref(booster.ChestPressure), $"{engine.ID}.{engine.steamChestPressureReadOut.ID}"),
                new PortReferenceConnection(Ref(booster.BoilerPressure), $"{boiler.ID}.{boiler.pressureReadOut.ID}"),
                new PortReferenceConnection(Ref(booster.Reverser), $"{reverser.ID}.{reverser.reverserReadOut.ID}"),
            }.ToList();
            if (sander != null)
                references.Add(new PortReferenceConnection(Ref(booster.SandCoefficient), $"{sander.ID}.{sander.sandCoefReadOut.ID}"));
            graph.portReferenceConnections = (graph.portReferenceConnections ?? new PortReferenceConnection[0]).Concat(references).ToArray();
        }

        private static SimComponentDefinition Control(SimConnectionDefinition graph, StokerJetControls.Valve valve)
        {
            var control = graph.gameObject.AddComponent<ExternalControlDefinition>();
            control.ID = valve.Id;
            control.defaultValue = valve.Default;
            control.saveState = true;
            return control;
        }
    }
}
