using System.Linq;
using DV.Simulation.Controllers;
using LocoSim.Definitions;
using LocoSim.Implementations;
using RealisticFiring.Core;
using RealisticFiring.Diagnostics;
using UnityEngine;

namespace RealisticFiring.Sim
{
    /// <summary>
    /// What a Custom Car Loader stoker definition says: its ID, ports, port
    /// references, and settings. Read once from CCL's definition by
    /// <c>StokerDefinitionPatch</c>, so the stoker itself needs no CCL types.
    /// </summary>
    internal sealed class StokerDefinitionValues
    {
        public string Id;

        /// <summary>Feed at full speed, in tender kg/s (CCL's MaxTransferRate).</summary>
        public float MaxTransferRate;

        public float MaxWorkingPressure;
        public float SmoothTime;

        public PortDefinition SteamConsumptionReadOut;
        public PortDefinition StokingNormalizedReadOut;
        public PortReferenceDefinition Control;
        public PortReferenceDefinition SteamPressure;
        public PortReferenceDefinition FireboxCoalLevel;
        public PortReferenceDefinition FireboxCoalCapacity;
        public PortReferenceDefinition FireboxCoalControl;
        public PortReferenceDefinition CoalAmount;
        public PortReferenceDefinition CoalConsume;
    }

    /// <summary>
    /// Replaces CCL's SteamMechanicalStoker under the same component ID,
    /// ports, and port references, driving a <see cref="Stoker"/>. The cab's
    /// stoker valve is the engine throttle; until the mod's jet controls are
    /// in the cab, the jets stay at the working setting while the stoker runs
    /// and the master jet is cracked while it is shut. Coal is drawn from the
    /// tender through the loco's own firebox multiplier, and the engine and
    /// jets take their steam from the boiler unless the loco already wires the
    /// stoker's steam readout into the boiler's consumption.
    /// </summary>
    internal sealed class RealisticStoker : SimComponent
    {
        // CCL's stoker turns from 2 bar.
        private const float MinWorkingPressure = 2f;

        // With the stoker shut the fireman shuts the jets but leaves the
        // master valve cracked, so the table doesn't overheat.
        private const float CrackedMasterJet = 0.03f;

        // Without a known firebox burn time, for the steam estimate only.
        private const float FallbackBurnTime = 120f;

        // A boiler holds tens of tonnes, where a float steps by about 4 g; the
        // stoker's few grams a tick would round away, so its steam is taken in
        // lots of this size, in kg.
        internal const float SteamLot = 0.25f;

        private readonly Port steamConsumption;
        private readonly Port stokingNormalized;
        private readonly PortReference control;
        private readonly PortReference steamPressure;
        private readonly PortReference fireboxCoalLevel;
        private readonly PortReference fireboxCoalCapacity;
        private readonly PortReference fireboxCoalControl;
        private readonly PortReference coalAmount;
        private readonly PortReference coalConsume;

        private readonly TrainCar car;
        private readonly float maxTransferRate;
        private readonly float fullSpeedPressure;
        private readonly float smoothTime;
        private readonly float coalMultiplier;
        private readonly bool steamWired;

        private RealisticFirebox firebox;
        private Boiler boiler;
        private Stoker stoker;
        private bool resolved;
        private float pendingSteam;

        // The jet valves' control ports, in StokerJetControls.Valves order;
        // null on locos without them. The operating lever's, if any.
        private Port[] jetPorts;
        private Port reversePort;

        public RealisticStoker(StokerDefinitionValues def, TrainCar car, float coalMultiplier, bool steamWired)
            : base(def.Id)
        {
            this.car = car;
            maxTransferRate = def.MaxTransferRate;
            fullSpeedPressure = def.MaxWorkingPressure;
            smoothTime = def.SmoothTime;
            this.coalMultiplier = coalMultiplier > 0f ? coalMultiplier : 1f;
            this.steamWired = steamWired;

            steamConsumption = AddPort(def.SteamConsumptionReadOut);
            stokingNormalized = AddPort(def.StokingNormalizedReadOut);
            control = AddPortReference(def.Control);
            steamPressure = AddPortReference(def.SteamPressure);
            fireboxCoalLevel = AddPortReference(def.FireboxCoalLevel);
            fireboxCoalCapacity = AddPortReference(def.FireboxCoalCapacity);
            fireboxCoalControl = AddPortReference(def.FireboxCoalControl);
            coalAmount = AddPortReference(def.CoalAmount);
            coalConsume = AddPortReference(def.CoalConsume);
        }

        /// <summary>The stoker model, once the first tick has found the firebox.</summary>
        internal Stoker Model => stoker;

        /// <summary>Where its coal goes: the zoned fire, or the coal port of a stock firebox.</summary>
        internal bool FeedsZonedFire => firebox != null;

        /// <summary>Gives the stoker its firebox and boiler directly, for checks outside the game.</summary>
        internal void Attach(RealisticFirebox realisticFirebox, Boiler steamSource)
        {
            firebox = realisticFirebox;
            boiler = steamSource;
            Build();
        }

        public override void Tick(float delta)
        {
            var start = PerfMeter.Start();
            Step(delta);
            PerfMeter.Stop(PerfSection.Stoker, start);
        }

        private void Step(float delta)
        {
            if (!resolved)
                Resolve();
            if (!(delta > 0f))
                return;

            var modifier = gameParams != null ? gameParams.ResourceConsumptionModifier : 1f;
            // Tender coal through the loco's tender ports (CCL locos), or
            // straight from the tender's coal pile, as the shovel takes it.
            var pile = coalAmount.IsConnected ? null : TenderPile();
            var available = pile != null
                ? pile.CoalAvailable() / coalMultiplier
                : Mathf.Max(0f, coalAmount.Value) / (coalMultiplier * (modifier > 0f ? modifier : 1f));
            var throttle = Mathf.Clamp01(control.Value);
            var pressure = steamPressure.Value;
            var controls = ReadControls(throttle);

            StokerFlow flow;
            if (firebox != null)
            {
                flow = firebox.Stoke(stoker, new StokerInputs
                {
                    Controls = controls,
                    BoilerPressure = pressure,
                    TenderCoal = available,
                }, delta);
            }
            else
            {
                // A stock firebox takes coal through its port, as from CCL's stoker.
                flow = new StokerFlow();
                var space = Mathf.Max(0f, fireboxCoalCapacity.Value - fireboxCoalLevel.Value);
                var offered = Mathf.Min(stoker.RunEngine(delta, throttle, pressure) * delta, Mathf.Min(available, space));
                fireboxCoalControl.Value = offered;
                flow.Drawn = offered;
                flow.Fed = offered;
                stoker.ReadGauges(controls, pressure, ref flow);
            }

            if (pile != null)
            {
                if (flow.Drawn > 0f)
                    pile.TryRemoveCoal(flow.Drawn * coalMultiplier);
            }
            else
            {
                coalConsume.Value = flow.Drawn * coalMultiplier * modifier;
            }
            stokingNormalized.Value = stoker.EngineSpeed;
            steamConsumption.Value = flow.SteamUsed;
            if (!steamWired)
            {
                pendingSteam += flow.SteamUsed * delta;
                var vessel = InjectorRig.VesselOf(boiler);
                if (vessel != null && pendingSteam >= SteamLot)
                {
                    vessel.RemoveSteam(Mathf.Min(pendingSteam, vessel.mass));
                    pendingSteam = 0f;
                }
            }
        }

        // The cab's jet valves when the loco has them; otherwise the working
        // setting while the stoker runs, with the master jet cracked while it
        // is shut, as a fireman would leave it.
        private StokerControls ReadControls(float throttle)
        {
            var controls = StokerControls.Working(throttle);
            if (reversePort != null)
                controls.Direction = StokerJetControls.Direction(reversePort.Value);
            if (jetPorts == null)
            {
                if (throttle <= 0f)
                    controls.MasterJet = CrackedMasterJet;
                return controls;
            }

            var valves = StokerJetControls.Valves;
            for (var i = 0; i < valves.Count; i++)
            {
                var value = Mathf.Clamp01(jetPorts[i].Value);
                switch (valves[i].Jet)
                {
                    case null: controls.MasterJet = value; break;
                    case StokerJet.LeftBack: controls.LeftBack = value; break;
                    case StokerJet.RightBack: controls.RightBack = value; break;
                    case StokerJet.LeftFront: controls.LeftFront = value; break;
                    case StokerJet.RightFront: controls.RightFront = value; break;
                    case StokerJet.Fines: controls.Fines = value; break;
                }
            }
            return controls;
        }

        // The coal pile the stoker draws from: the loco's own, or its tender's
        // behind it, as MagicShoveling finds it; none when uncoupled.
        private CoalPileSimController TenderPile()
        {
            if (car == null)
                return null;
            var own = car.SimController != null ? car.SimController.coalPile : null;
            if (own != null)
                return own;
            var tender = car.rearCoupler != null && car.rearCoupler.coupledTo != null ? car.rearCoupler.coupledTo.train : null;
            return tender != null && tender.SimController != null ? tender.SimController.coalPile : null;
        }

        /// <summary>Gives the stoker its jet valves' ports directly, for checks outside the game.</summary>
        internal void AttachJets(Port[] ports) => jetPorts = ports;

        private void Resolve()
        {
            resolved = true;
            if (car != null)
            {
                FireboxRegistry.TryGet(car, out firebox);
                var flow = car.SimController != null ? car.SimController.SimulationFlow : null;
                boiler = flow != null ? flow.OrderedSimComps.OfType<Boiler>().FirstOrDefault() : null;
                if (flow != null)
                {
                    var ports = StokerJetControls.Valves
                        .Select(valve => flow.TryGetPort(valve.PortId, out var port) ? port : null)
                        .ToArray();
                    jetPorts = ports.All(port => port != null) ? ports : null;
                    reversePort = flow.TryGetPort(StokerJetControls.Reverse.PortId, out var lever) ? lever : null;
                }
            }
            Build();
            if (car != null && Main.Settings.LogFireboxCreation)
            {
                Main.Mod.Logger.Log(
                    $"{car.ID}: realistic stoker '{id}' feeding {(firebox != null ? "the zoned fire" : "a stock firebox's coal port")}: " +
                    $"{stoker.Spec.MaxFeedRate:F2} kg/s at full speed ({maxTransferRate:F1} tender kg/s over the firebox's multiplier of {coalMultiplier:F0}), " +
                    $"steam {(steamWired ? "through the loco's own wiring" : boiler != null ? "from the boiler" : "nowhere: no boiler found")}, " +
                    $"jets {(jetPorts != null ? "worked from the cab's jet valves" : "fixed at the working setting")}");
            }
        }

        private void Build()
        {
            var parameters = firebox != null
                ? firebox.Model.Parameters
                : new FireboxParameters(Mathf.Max(1f, fireboxCoalCapacity.Value), FallbackBurnTime);
            stoker = new Stoker(new StokerSpec(parameters, maxTransferRate / coalMultiplier)
            {
                // Jams need the operating lever to clear them.
                MeanCoalBetweenJams = Main.Settings.StokerJams && reversePort != null ? new StokerSpec(parameters, 1f).MeanCoalBetweenJams : 0f,
                MinWorkingPressure = MinWorkingPressure,
                FullSpeedPressure = Mathf.Max(MinWorkingPressure + 0.1f, fullSpeedPressure),
                EngineSmoothTime = Mathf.Max(0.1f, smoothTime),
            }, parameters);
        }
    }
}
