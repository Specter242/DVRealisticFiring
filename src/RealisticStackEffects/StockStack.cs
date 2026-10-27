using System;
using System.Collections.Generic;
using System.Linq;
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

        private readonly TrainCar car;
        private readonly Port fireOn;
        private readonly Port heat;
        private readonly Port smoke;
        private readonly Port coal;
        private readonly Port temperature;
        private readonly Port combustion;
        private readonly PortReference air;
        private readonly float mostHeat;
        private readonly float burnTime;
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
        /// definition, only whether the fire is on.
        /// </summary>
        internal StockStack(TrainCar car, Port fireOn, FireboxDefinition definition, Func<PortDefinition, Port> port, PortReference air)
        {
            this.car = car;
            if (!ReferenceEquals(car, null))
                OnCar.GetValue(car, _ => new List<StockStack>()).Add(this);
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
        public static StockStack For(TrainCar car, SteamSmokeParticlePortReader reader, SimulationFlow flow, out string how)
        {
            flow.TryGetPort(reader.fireOnPortId, out var fireOn);
            var id = ComponentOf(reader.fireOnPortId);
            var graph = car.SimController != null ? car.SimController.connectionsDefinition : null;
            var definition = graph != null && graph.executionOrder != null
                ? graph.executionOrder.OfType<FireboxDefinition>().FirstOrDefault(d => d.ID == id)
                : null;
            if (definition == null || !(definition.burnTime > 0f))
            {
                how = $"no firebox '{id}' in the graph: the stack shows only whether the fire is on";
                return new StockStack(car, fireOn, null, null, null);
            }
            Port Find(PortDefinition port) => flow.TryGetPort($"{id}.{port.ID}", out var found) ? found : null;
            // The draft the firebox burns with, as it reads it.
            var air = flow.OrderedSimComps.FirstOrDefault(c => c.id == id)?.GetAllPortReferences()
                .FirstOrDefault(r => r.id == $"{id}.{definition.airFlow.ID}");
            var missing = new[] { definition.heatReadOut, definition.smokeDensityReadOut, definition.coalLevelReadOut }
                .Where(port => Find(port) == null).Select(port => port.ID).ToList();
            how = $"the smoke from firebox '{id}'" + (missing.Count > 0 ? $" (missing {string.Join(", ", missing)})" : "");
            return new StockStack(car, fireOn, definition, Find, air);
        }

        /// <summary>The stock stacks reading <paramref name="car"/>'s fire, one a smoke reader; none for another mod's fire.</summary>
        internal static IReadOnlyList<StockStack> On(TrainCar car) =>
            !ReferenceEquals(car, null) && OnCar.TryGetValue(car, out var stacks) ? stacks : None;

        /// <summary>
        /// A shovelful landing on this fire, <paramref name="kg"/> as the
        /// firebox took it: smoke-out only on a fire burning as it lands (the
        /// game's start-up helper fills a cold box, then lights it).
        /// </summary>
        internal void Thrown(float kg)
        {
            if (fireOn != null && fireOn.Value > 0f)
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
            reading.AirThrough = drawn;
            // No smoke to see and air to spare beyond the coal's need: a clear
            // stack. A fire that is out burns none of it.
            var need = reading.FireOn && coal != null && burnTime > 0f ? Mathf.Max(0f, coal.Value) / burnTime * AirPerCoal : 0f;
            reading.ClearStack = PlumeModel.CoalClearStack(reading.Soot, drawn > 0f ? Mathf.Max(0f, drawn - need) / drawn : 0f);
            // A shovelful's smoke-out, through the tubes to the stack.
            charge.Step(dt, reading.FireOn, temperature != null ? temperature.Value : 0f, combustion != null ? combustion.Value : 0f);
            reading.FireboxSmokeOut = charge.StackRate;
            reading.Cinders = true;
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
