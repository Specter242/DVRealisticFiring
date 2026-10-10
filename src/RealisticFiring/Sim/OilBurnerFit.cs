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

        /// <summary>
        /// The fire door's peep hole, the sand hole on an oil burner ("sand is
        /// poured through the sand or peep hole", ICS pp. 54-55). The 2901's cab
        /// has its cover wired to this control, which its sim never had.
        /// </summary>
        public const string PeepHoleId = "peephole";
        public const string PeepHolePortId = PeepHoleId + ".EXT_IN";

        /// <summary>The shop steam valve: a shed's steam line on the burner's valves, at a pit stop. Shut by default.</summary>
        public static readonly StokerJetControls.Valve ShopSteam = new StokerJetControls.Valve("rfShopSteam", "Shop steam", null, 0f);

 private static readonly HashSet<string> Unsupported = new HashSet<string> { "S040F", "S484-841A" };

        private static readonly Dictionary<TrainCar, bool> Known = new Dictionary<TrainCar, bool>();
        private static readonly HashSet<string> Skipped = new HashSet<string>();

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
            var type = car.carLivery != null && car.carLivery.parentType != null ? car.carLivery.parentType.id : null;
            if (oil && type != null && Unsupported.Contains(type))
            {
                oil = false;
                if (Skipped.Add(type))
                    Main.Mod?.Logger.Log($"{type}: oil-fired, but not supported; it keeps its own firing");
            }
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
