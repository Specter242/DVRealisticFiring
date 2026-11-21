using System;
using System.Collections.Generic;
using System.Linq;
using DV.Simulation.Cars;
using DV.Simulation.Controllers;
using LocoSim.Definitions;
using LocoSim.Implementations;
using RealisticStackEffects.Core;
using UnityEngine;

namespace RealisticStackEffects
{
 internal sealed class StockStack : IStackFire
    {
        // The stock firebox's coal heat, J/kg (Firebox.COAL_HEAT_CONTENT).
        private const float CoalHeat = Firebox.COAL_HEAT_CONTENT;

        // The air a kg of coal burned needs, with the stock 20 % excess, kg
        // (Firebox.AIR_MASS_FACTOR).
        private const float AirPerCoal = Firebox.AIR_MASS_FACTOR;

 internal const float SootShare = 0.35f;

        // CCL's mechanical stoker definition type name (used by oil-fired CCL locos
        // such as the Santa Fe 2901 to feed liquid fuel into the stock firebox).
        internal const string CclStokerTypeName = "CCL.Importer.Components.Simulation.SteamMechanicalStokerDefinitionInternal";

        private readonly TrainCar car;
        private readonly SteamSmokeParticlePortReader reader;
        private readonly bool unityReader;
        private readonly Port fireOn;
        private readonly Port heat;
        private readonly Port smoke;
        private readonly Port coal;
        private readonly Port temperature;
        private readonly Port combustion;
        private readonly PortReference air;
        private readonly float mostHeat;
        private readonly float burnTime;
        private readonly bool oilFired;
        private readonly StockCharge charge = new StockCharge();


        // Each car's stock stacks, every one of its smoke readers', for the
        // shovelfuls landing on it (ShovelfulPatch).
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TrainCar, List<StockStack>> OnCar =
            new System.Runtime.CompilerServices.ConditionalWeakTable<TrainCar, List<StockStack>>();
        private static readonly List<StockStack> None = new List<StockStack>();

        /// <summary>
        /// The fire of a firebox built from <paramref name="definition"/>,
        /// its ports found by <paramref name="port"/> (null where missing) and
        /// the draft it burns with, <paramref name="air"/>; without the
        /// definition, only whether the fire is on. Keyed on <paramref name="car"/>
        /// by <paramref name="reader"/> when given, so re-initializing the same
        /// smoke reader replaces its previous entry instead of duplicating it.
        /// </summary>
        internal StockStack(TrainCar car, Port fireOn, FireboxDefinition definition, Func<PortDefinition, Port> port, PortReference air,
            SteamSmokeParticlePortReader reader = null, bool register = true, bool oilFired = false)
        {
            this.car = car;
            this.reader = reader;
            this.oilFired = oilFired;
            // A live Unity component compares unequal to null when created and
            // equal to null once destroyed; an uninitialized test instance outside
            // Unity compares equal to null from the start and is not pruned.
            unityReader = !ReferenceEquals(reader, null) && reader != null;
            if (register)
                Register(car, this);
            this.fireOn = fireOn;
            // The definition as given: Unity's overloaded null check takes
            // one made outside the engine (the adapter check's) for none.
            if (ReferenceEquals(definition, null) || !(definition.burnTime > 0f) || port == null)
                return;
            heat = port(definition.heatReadOut);
            smoke = port(definition.smokeDensityReadOut);
            coal = port(definition.coalLevelReadOut);
            temperature = port(definition.temperatureReadOut);
            combustion = port(definition.combustionRateNormalizedReadOut);
            this.air = air;
            mostHeat = definition.maxCoalCapacity / definition.burnTime * CoalHeat * definition.efficiencyAtMaxCombustion;
            burnTime = definition.burnTime;
        }

        /// <summary>
        /// The firebox under <paramref name="reader"/>'s smoke: the one whose
        /// fire the reader watches. Without one, a fire that is only on or
        /// off; <paramref name="how"/> says which, for the log.
        /// </summary>
        public static StockStack For(TrainCar car, SteamSmokeParticlePortReader reader, SimulationFlow flow, out string how) =>
            Create(car, reader, flow, register: true, out how);

        /// <summary>
        /// Builds a <see cref="StockStack"/> for <paramref name="reader"/>
        /// without registering it on <paramref name="car"/> yet, so a failed
        /// plume attach or rebind cannot overwrite an active plume's entry.
        /// Call <see cref="Register"/> once the plume binds it.
        /// </summary>
        internal static StockStack CreateCandidate(TrainCar car, SteamSmokeParticlePortReader reader, SimulationFlow flow, out string how) =>
            Create(car, reader, flow, register: false, out how);

        private static StockStack Create(TrainCar car, SteamSmokeParticlePortReader reader, SimulationFlow flow, bool register, out string how)
        {
            flow.TryGetPort(reader.fireOnPortId, out var fireOn);
            var id = ComponentOf(reader.fireOnPortId);
            var simController = car != null
                ? (car.SimController != null ? car.SimController : car.GetComponentInChildren<SimController>(true))
                : null;
            var graph = simController != null ? simController.connectionsDefinition : null;
            var oilFired = IsOilFired(graph, flow);
            var definition = graph != null && graph.executionOrder != null
                ? graph.executionOrder.OfType<FireboxDefinition>().FirstOrDefault(d => d.ID == id)
                : null;
            if (definition == null || !(definition.burnTime > 0f))
            {
                how = $"no firebox '{id}' in the graph: the stack shows only whether the fire is on{(oilFired ? " (oil-fired)" : "")}";
                return new StockStack(car, fireOn, null, null, null, reader, register, oilFired);
            }
            Port Find(PortDefinition port) => flow.TryGetPort($"{id}.{port.ID}", out var found) ? found : null;
            // The draft the firebox burns with, as it reads it.
            var air = flow.OrderedSimComps.FirstOrDefault(c => c.id == id)?.GetAllPortReferences()
                .FirstOrDefault(r => r.id == $"{id}.{definition.airFlow.ID}");
            var missing = new[] { definition.heatReadOut, definition.smokeDensityReadOut, definition.coalLevelReadOut }
                .Where(port => Find(port) == null).Select(port => port.ID).ToList();
            how = $"the smoke from firebox '{id}'{(oilFired ? " (oil-fired)" : "")}"
                  + (missing.Count > 0 ? $" (missing {string.Join(", ", missing)})" : "");
            return new StockStack(car, fireOn, definition, Find, air, reader, register, oilFired);
        }

        private bool ReaderDestroyed => unityReader && reader == null;

        /// <summary>
        /// Registers <paramref name="stack"/> on <paramref name="car"/>,
        /// replacing any prior entry for the same smoke reader and pruning
        /// entries whose readers were destroyed.
        /// </summary>
        internal static void Register(TrainCar car, StockStack stack)
        {
            if (ReferenceEquals(car, null) || stack == null || stack.ReaderDestroyed)
                return;
            var stacks = OnCar.GetValue(car, _ => new List<StockStack>());
            PruneDestroyed(stacks);
            if (!ReferenceEquals(stack.reader, null))
            {
                for (var i = 0; i < stacks.Count; i++)
                {
                    if (ReferenceEquals(stacks[i].reader, stack.reader))
                    {
                        stacks[i] = stack;
                        return;
                    }
                }
            }
            else
            {
                for (var i = 0; i < stacks.Count; i++)
                {
                    if (ReferenceEquals(stacks[i], stack))
                        return;
                }
            }
            stacks.Add(stack);
        }

        /// <summary>
        /// Removes any stock stack registered on <paramref name="car"/> for
        /// <paramref name="reader"/>, and prunes any whose reader was destroyed.
        /// </summary>
        internal static void Remove(TrainCar car, SteamSmokeParticlePortReader reader)
        {
            if (ReferenceEquals(car, null) || !OnCar.TryGetValue(car, out var stacks))
                return;
            for (var i = stacks.Count - 1; i >= 0; i--)
            {
                if (stacks[i].ReaderDestroyed || (!ReferenceEquals(reader, null) && ReferenceEquals(stacks[i].reader, reader)))
                    stacks.RemoveAt(i);
            }
            if (stacks.Count == 0)
                OnCar.Remove(car);
        }

        private static void PruneDestroyed(List<StockStack> stacks)
        {
            for (var i = stacks.Count - 1; i >= 0; i--)
            {
                if (stacks[i].ReaderDestroyed)
                    stacks.RemoveAt(i);
            }
        }

        /// <summary>
        /// Whether a locomotive's sim graph fires its firebox with fuel oil
        /// through a CCL mechanical stoker feeding from a <see cref="PortValueType.FUEL"/>
        /// port, so standalone stacks suppress coal cinders and shovelful smoke
        /// on oil-fired CCL locomotives (matching <c>OilBurnerFit.FeedsFuel</c>).
        /// Lubricating oil (<see cref="OilContainerDefinition"/> / <see cref="PortValueType.OIL"/>)
        /// and unconnected <see cref="FuelContainerDefinition"/> components never
        /// mark an engine as oil-fired.
        /// </summary>
        internal static bool IsOilFired(SimConnectionDefinition graph, SimulationFlow flow)
        {
            // ReferenceEquals on UnityEngine.Object definitions so graphs built
            // outside the engine (adapter checks) are read the same as live ones.
            if (ReferenceEquals(graph, null) || graph.executionOrder == null || graph.portReferenceConnections == null)
                return false;

            foreach (var stoker in graph.executionOrder.Where(d => !ReferenceEquals(d, null) && d.GetType().FullName == CclStokerTypeName))
            {
                var supply = graph.portReferenceConnections
                    .FirstOrDefault(r => r != null && r.portReferenceId == stoker.ID + ".COAL_AMOUNT")?.portId;
                if (string.IsNullOrEmpty(supply))
                    continue;
                var port = flow?.AllPorts?.FirstOrDefault(p => p != null && p.id == supply);
                if (port != null)
                {
                    if (port.valueType == PortValueType.FUEL)
                        return true;
                    continue;
                }
                var dot = supply.LastIndexOf('.');
                if (dot <= 0 || dot >= supply.Length - 1)
                    continue;
                var supplyId = supply.Substring(0, dot);
                var portName = supply.Substring(dot + 1);
                var source = graph.executionOrder.FirstOrDefault(d => !ReferenceEquals(d, null) && d.ID == supplyId);
                if (IsFuelPort(source, portName))
                    return true;
            }

            return false;
        }

        private static bool IsFuelPort(SimComponentDefinition source, string portName)
        {
            if (ReferenceEquals(source, null) || string.IsNullOrEmpty(portName))
                return false;
            if (source is FuelContainerDefinition fuel)
                return IsFuelPortDefinition(fuel.amountReadOut, portName)
                       || IsFuelPortDefinition(fuel.normalizedReadOut, portName)
                       || IsFuelPortDefinition(fuel.capacityReadOut, portName)
                       || IsFuelPortDefinition(fuel.fuelRefillExtIn, portName)
                       || IsFuelPortDefinition(fuel.fuelConsumeExtIn, portName);
            if (source is PortForwardFuelInDefinition fuelIn)
                return IsFuelPortDefinition(fuelIn.forwardIn, portName)
                       || IsFuelPortDefinition(fuelIn.simOut, portName);
            if (source is PortForwardFuelOutDefinition fuelOut)
                return IsFuelPortDefinition(fuelOut.simIn, portName)
                       || IsFuelPortDefinition(fuelOut.forwardOut, portName);
            if (source is ConfigurablePortsDefinition ports && ports.ports != null)
                return ports.ports.Any(p => IsFuelPortDefinition(p, portName));
            return false;
        }

        private static bool IsFuelPortDefinition(PortDefinition port, string portName) =>
            port != null && port.ID == portName && port.valueType == PortValueType.FUEL;

        /// <summary>The stock stacks reading <paramref name="car"/>'s fire, one a smoke reader; none for another mod's fire.</summary>
        internal static IReadOnlyList<StockStack> On(TrainCar car)
        {
            if (ReferenceEquals(car, null) || !OnCar.TryGetValue(car, out var stacks))
                return None;
            PruneDestroyed(stacks);
            if (stacks.Count == 0)
            {
                OnCar.Remove(car);
                return None;
            }
            return stacks;
        }

        /// <summary>
        /// A shovelful landing on this fire, <paramref name="kg"/> as the
        /// firebox took it: smoke-out only on a fire burning as it lands (the
        /// game's start-up helper fills a cold box, then lights it).
        /// </summary>
        internal void Thrown(float kg)
        {
            if (!oilFired && fireOn != null && fireOn.Value > 0f)
                charge.Throw(kg);
        }

        public bool Tended
        {
            get
            {
                var on = PlayerManager.Car;
                return on != null && (on == car || Coupled(on.frontCoupler) == car || Coupled(on.rearCoupler) == car);
            }
        }

        public void Read(float dt, SteamExhaust exhaust, ref StackReading reading)
        {
            reading.FireOn = fireOn != null && fireOn.Value > 0f;
            reading.Heat = heat != null ? Mathf.Max(0f, heat.Value) : 0f;
            reading.MostHeat = mostHeat;
            reading.Soot = smoke != null ? SootShare * Mathf.Clamp01(smoke.Value) : 0f;
            var drawn = Mathf.Max(0f, air != null ? air.Value : exhaust != null ? exhaust.airFlowReadOut.Value : 0f);
            // The steam meets only the air its own blast draws: the exhaust's
            // passive draft goes on whatever the steam, and counted in, a
            // little steam met many times its weight of hot gas and showed no
            // white at all (78youbaba, Nexus, 2026-10-28: the white went as
            // the regulator was eased; SteamExhaust.Tick).
            var passive = exhaust != null
                ? exhaust.passiveExhaust * Mathf.Lerp(SteamExhaust.MIN_DAMPER, 1f, exhaust.damperControl.Value)
                : 0f;
            reading.AirThrough = Mathf.Max(0f, drawn - passive);
            // No smoke to see and air to spare beyond the coal's need: a clear
            // stack. A fire that is out burns none of it.
            var need = reading.FireOn && coal != null && burnTime > 0f ? Mathf.Max(0f, coal.Value) / burnTime * AirPerCoal : 0f;
            reading.ClearStack = PlumeModel.CoalClearStack(reading.Soot, drawn > 0f ? Mathf.Max(0f, drawn - need) / drawn : 0f);
            // A shovelful's smoke-out, through the tubes to the stack.
            charge.Step(dt, reading.FireOn, temperature != null ? temperature.Value : 0f, combustion != null ? combustion.Value : 0f);
            reading.FireboxSmokeOut = charge.StackRate;
            reading.Cinders = !oilFired;
            reading.ExhaustWetness = float.NaN;
            if (exhaust == null)
            {
                reading.NaturalFlow = float.NaN;
                return;
            }
            var blower = exhaust.maxBlowerFlow
                         * Mathf.Min(Mathf.InverseLerp(1f, exhaust.pressureForMaxBlowerFlow, exhaust.boilerPressure.Value), exhaust.blowerControl.Value);
            reading.BlowerSteam = Mathf.Max(0f, blower);
            reading.StackSteam = Mathf.Max(0f, exhaust.exhaustFlow.Value) + reading.BlowerSteam;
            reading.NaturalFlow = NaturalFlow(exhaust);
        }

        // The stack's natural draft as a share of the stock exhaust's total
        // flow, which counts it even through a dead engine, as the exhaust
        // works it out (SteamExhaust.Tick).
        private static float NaturalFlow(SteamExhaust exhaust)
        {
            var engineMax = exhaust.engineMaxFlowReader.Value;
            var maxTotal = engineMax + exhaust.passiveExhaust + exhaust.entrainmentRatio * engineMax;
            if (!(maxTotal > 0f))
                return float.NaN;
            return exhaust.passiveExhaust * Mathf.Lerp(SteamExhaust.MIN_DAMPER, 1f, exhaust.damperControl.Value) / maxTotal;
        }

        private static TrainCar Coupled(Coupler coupler) =>
            coupler != null && coupler.coupledTo != null ? coupler.coupledTo.train : null;

        private static string ComponentOf(string portId)
        {
            var dot = portId != null ? portId.IndexOf('.') : -1;
            return dot > 0 ? portId.Substring(0, dot) : portId;
        }
    }
}
