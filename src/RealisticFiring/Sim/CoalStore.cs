using System.Linq;
using System.Runtime.CompilerServices;
using DV.JObjectExtstensions;
using LocoSim.Definitions;
using LocoSim.Implementations;
using LocoSim.Resources;
using Newtonsoft.Json.Linq;
using RealisticFiring.Core;
using RealisticStackEffects;
using UnityEngine;

namespace RealisticFiring.Sim
{
 internal sealed class CoalStore
    {
        public const string MoistureKey = "rfCoalMoisture";

        // Held weakly: a container is a simulation object, never Unity's
        // null, so a car's container gone with it was never let go (200 of
        // 200 kept after a full collection; performance review, 2026-10-25).
        private static readonly ConditionalWeakTable<ResourceContainer, CoalStore> ByContainer = new ConditionalWeakTable<ResourceContainer, CoalStore>();
        private static readonly ConditionalWeakTable<ResourceContainer, SavedMoisture> PendingMoisture = new ConditionalWeakTable<ResourceContainer, SavedMoisture>();

        // A save's water for a container whose store isn't made yet.
        private sealed class SavedMoisture
        {
            public float Value;
        }

        /// <summary>
        /// Covers over the coal, by their control's port on the car carrying
        /// it, each shut at 0 and open at 1: the S464-730 Hudson tender's coal
        /// bunker door (the model's lever, its animation's "Close" clip at 0
        /// and "Open" at 1; shut as the tender comes, its place saved).
        /// </summary>
        public static readonly string[] CoverPorts = { "coalbunkedoorControl.EXT_IN" };

        private readonly ResourceContainer container;
        private readonly Port cover;
        private float lastAmount = float.NaN;

        private CoalStore(ResourceContainer container, Port cover)
        {
            this.container = container;
            this.cover = cover;
        }

        /// <summary>Whether a cover can shut over the coal.</summary>
        public bool HasCover => cover != null;

        /// <summary>How far the coal is open to the rain, 0 covered to 1 open.</summary>
        public float Open => cover != null ? Mathf.Clamp01(cover.Value) : 1f;

        /// <summary>The rain's water in the coal.</summary>
        public CoalMoisture Water { get; } = new CoalMoisture();

        /// <summary>The water's share of the coal now at the shovel plate, 0 to 1.</summary>
        public float Moisture => Water.Moisture;

        /// <summary>
        /// The coal <paramref name="engine"/> fires from: its own bunker's, or
        /// its tender's behind it. Null where it has none (a tender uncoupled).
        /// </summary>
        public static CoalStore For(TrainCar engine)
        {
            var pileCar = PileCar(engine);
            var container = Container(pileCar);
            if (container == null)
                return null;
            return ByContainer.TryGetValue(container, out var store) ? store : Made(container, Cover(pileCar));
        }

        /// <summary>
        /// Soaks or dries the coal for <paramref name="dt"/> seconds as wet as
        /// the world is (<see cref="GameWeather.Wetness"/>, 0 dry to 1
        /// soaked), and mixes in coal loaded since the last step. Covered, the
        /// rain stays off it, and what it holds drains as in dry weather.
        /// </summary>
        public void Step(float dt, float wetness)
        {
            var amount = container.amountReadOut.Value;
            if (!float.IsNaN(lastAmount) && amount > lastAmount)
                Water.Load(lastAmount, amount - lastAmount);
            lastAmount = amount;
            Water.Step(dt, wetness * Open);
        }

        /// <summary>Adds the water to a coal container's save, where it has a store.</summary>
        public static void Save(ResourceContainer container, JObject data)
        {
            if (data != null && container != null && ByContainer.TryGetValue(container, out var store))
                data.SetFloat(MoistureKey, store.Moisture);
        }

        /// <summary>Restores the water from a coal container's save, now or when its store is made.</summary>
        public static void Load(ResourceContainer container, JObject data)
        {
            var saved = data?.GetFloat(MoistureKey);
            if (container == null || !saved.HasValue)
                return;
            if (ByContainer.TryGetValue(container, out var store))
                store.Water.Restore(saved.Value);
            else
                PendingMoisture.GetOrCreateValue(container).Value = saved.Value;
        }

        /// <summary>Gives <paramref name="container"/> a store directly, for checks outside the game.</summary>
        internal static CoalStore Create(ResourceContainer container, Port cover = null) => Made(container, cover);

        // A container's store made, in place of any it had, with its save's water.
        private static CoalStore Made(ResourceContainer container, Port cover)
        {
            var store = new CoalStore(container, cover);
            ByContainer.Remove(container);
            ByContainer.Add(container, store);
            if (PendingMoisture.TryGetValue(container, out var saved))
            {
                PendingMoisture.Remove(container);
                store.Water.Restore(saved.Value);
            }
            return store;
        }

        // The car carrying the engine's coal: its own, or the tender behind it.
        private static TrainCar PileCar(TrainCar engine)
        {
            if (engine == null)
                return null;
            if (engine.SimController != null && engine.SimController.coalPile != null)
                return engine;
            var tender = engine.rearCoupler != null && engine.rearCoupler.coupledTo != null ? engine.rearCoupler.coupledTo.train : null;
            return tender != null && tender.SimController != null && tender.SimController.coalPile != null ? tender : null;
        }

        // The cover over the coal on its car, where it has one.
        private static Port Cover(TrainCar car)
        {
            var flow = car != null && car.SimController != null ? car.SimController.SimulationFlow : null;
            if (flow == null)
                return null;
            foreach (var id in CoverPorts)
            {
                // Looked for by its component first: the port lookup logs an error for every car without it.
                var component = id.Substring(0, id.IndexOf('.'));
                if (flow.OrderedSimComps.Any(c => c != null && c.id == component)
                    && flow.TryGetPort(id, out var port) && port != null && port.type == PortType.EXTERNAL_IN)
                    return port;
            }
            return null;
        }

        private static ResourceContainer Container(TrainCar car)
        {
            var flow = car != null && car.SimController != null ? car.SimController.SimulationFlow : null;
            return flow?.OrderedSimComps.OfType<ResourceContainer>().FirstOrDefault(c => c.resourceType == ResourceContainerType.COAL);
        }
    }
}
