using System.Collections.Generic;
using System.Linq;
using DV.Simulation.Cars;
using LocoSim.Definitions;

namespace RealisticFiring.Sim
{
 internal static class OilBurnerFit
    {
        public const string StokerTypeName = "CCL.Importer.Components.Simulation.SteamMechanicalStokerDefinitionInternal";

        /// <summary>The atomizer valve: steam to the burner. Cracked by default, so a loco runs as before.</summary>
        public static readonly StokerJetControls.Valve Atomizer = new StokerJetControls.Valve("rfAtomizer", "Atomizer", null, 0.25f);

        /// <summary>The oil heater's valve: steam to the tender's oil. Shut by default.</summary>
        public static readonly StokerJetControls.Valve Heater = new StokerJetControls.Valve("rfOilHeater", "Oil heater", null, 0f);

        private static readonly Dictionary<TrainCar, bool> Known = new Dictionary<TrainCar, bool>();

        /// <summary>Whether <paramref name="car"/> fires its firebox with fuel oil through a CCL stoker.</summary>
        public static bool IsOilBurner(TrainCar car)
        {
            if (car == null || !Main.Settings.RealisticOilBurners)
                return false;
            if (Known.TryGetValue(car, out var known))
                return known;
            foreach (var stale in Known.Keys.Where(c => c == null).ToList())
                Known.Remove(stale);
            var simController = car.SimController != null ? car.SimController : car.GetComponentInChildren<SimController>(true);
            var oil = FeedsFuel(simController != null ? simController.connectionsDefinition : null, out _);
            Known[car] = oil;
            return oil;
        }

        /// <summary>
        /// Whether a CCL stoker in <paramref name="graph"/> draws fuel; names the
        /// stoker. A fuel container behind its coal-amount reference, or a port
        /// of fuel (as a tender's forwarded ports are), makes it an oil burner.
        /// </summary>
        public static bool FeedsFuel(SimConnectionDefinition graph, out string stokerId)
        {
            stokerId = null;
            if (graph == null || graph.executionOrder == null || graph.portReferenceConnections == null)
                return false;
            foreach (var stoker in graph.executionOrder.Where(d => d != null && d.GetType().FullName == StokerTypeName))
            {
                var supply = graph.portReferenceConnections.FirstOrDefault(r => r.portReferenceId == stoker.ID + ".COAL_AMOUNT")?.portId;
                var dot = supply != null ? supply.LastIndexOf('.') : -1;
                if (dot <= 0)
                    continue;
                var supplyId = supply.Substring(0, dot);
                var portName = supply.Substring(dot + 1);
                var source = graph.executionOrder.FirstOrDefault(d => d != null && d.ID == supplyId);
                var fuel = source is FuelContainerDefinition
                           || source is ConfigurablePortsDefinition ports && ports.ports != null
                           && ports.ports.Any(p => p != null && p.ID == portName && p.valueType == PortValueType.FUEL);
                if (fuel)
                {
                    stokerId = stoker.ID;
                    return true;
                }
            }
            return false;
        }
    }
}
