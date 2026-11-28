using System.Collections.Generic;
using System.Linq;
using LocoSim.Definitions;
using LocoSim.Implementations;
using RealisticFiring.Core;
using UnityEngine;

namespace RealisticFiring.Sim
{
    /// <summary>
    /// A trailing-truck booster in a loco's sim graph: between the main
    /// engine's torque and the traction, it adds the booster's pull, takes
    /// its steam from the boiler, drops the cab's latch when the booster
    /// cuts out, and counts the trailer axle as powered only while in gear.
    /// </summary>
    internal sealed class RealisticBoosterDefinition : SimComponentDefinition
    {
        /// <summary>The main engine's cut-off range, for the reverser's position.</summary>
        public float MinCutoff = 0.05f;

        public float MaxCutoff = 0.9f;

        /// <summary>Weight on the booster axle, kg.</summary>
        public float TrailerLoad;

        /// <summary>Powered axles without the booster: the drivers.</summary>
        public int DrivingAxles;

        /// <summary>Wheel-rail friction for the trailer axle; 0 takes the car's own.</summary>
        public float FrictionCoefficient;

        /// <summary>The port holding the powered axle count, for the slip limit.</summary>
        public string PoweredAxlesPortId;

        /// <summary>
        /// The loco's configured powered-axle count as it was built, before any
        /// other mod rearranged its wheels; 0 when not known (then the drivers).
        /// </summary>
        public int BuiltPoweredAxles;

        public readonly PortDefinition TorqueOut = new PortDefinition(PortType.OUT, PortValueType.TORQUE, "TORQUE_OUT");
        public readonly PortReferenceDefinition MainTorque = new PortReferenceDefinition(PortValueType.TORQUE, "MAIN_TORQUE");
        public readonly PortReferenceDefinition Latch = new PortReferenceDefinition(PortValueType.CONTROL, "LATCH", writeAllowed: true);
        public readonly PortReferenceDefinition Idle = new PortReferenceDefinition(PortValueType.CONTROL, "IDLE");
        public readonly PortReferenceDefinition ChestPressure = new PortReferenceDefinition(PortValueType.PRESSURE, "CHEST_PRESSURE");
        public readonly PortReferenceDefinition BoilerPressure = new PortReferenceDefinition(PortValueType.PRESSURE, "BOILER_PRESSURE");
        public readonly PortReferenceDefinition Reverser = new PortReferenceDefinition(PortValueType.CONTROL, "REVERSER");
        public readonly PortReferenceDefinition SandCoefficient = new PortReferenceDefinition(PortValueType.GENERIC, "SAND_COEF");

        public override SimComponent InstantiateImplementation() => new RealisticBooster(this, GetComponentInParent<TrainCar>());
    }

    internal sealed class RealisticBooster : SimComponent
    {
        private static readonly Dictionary<TrainCar, RealisticBooster> ByCar = new Dictionary<TrainCar, RealisticBooster>();

        private readonly TrainCar car;
        private readonly RealisticBoosterDefinition def;
        private readonly Port torqueOut;
        private readonly PortReference mainTorque;
        private readonly PortReference latch;
        private readonly PortReference idle;
        private readonly PortReference chestPressure;
        private readonly PortReference boilerPressure;
        private readonly PortReference reverser;
        private readonly PortReference sandCoefficient;

        private bool resolved;
        private Boiler boiler;
        private Port poweredAxles;
        private float pendingSteam;
        private float wheelRadius = 1f;
        private float friction = 0.3f;

        public RealisticBooster(RealisticBoosterDefinition def, TrainCar car)
            : base(def.ID)
        {
            this.def = def;
            this.car = car;
            upgrade = car != null && BoosterFit.TryGet(car.carLivery.id, out var fit) && fit.Upgrade;
            Model = new Booster(BoosterFit.SpecFor(car));
            torqueOut = AddPort(def.TorqueOut);
            mainTorque = AddPortReference(def.MainTorque);
            latch = AddPortReference(def.Latch);
            idle = AddPortReference(def.Idle);
            chestPressure = AddPortReference(def.ChestPressure);
            boilerPressure = AddPortReference(def.BoilerPressure);
            reverser = AddPortReference(def.Reverser);
            sandCoefficient = AddPortReference(def.SandCoefficient);
            if (car != null)
            {
                foreach (var gone in ByCar.Keys.Where(c => c == null).ToList())
                    ByCar.Remove(gone);
                ByCar[car] = this;
            }
        }

        public Booster Model { get; }

        private readonly bool upgrade;
        private int fittedVersion = -1;
        private string fittedGuid;
        private bool fitted;

        /// <summary>
        /// Whether the booster is on the engine now: always, but for the stock
        /// S282's, an upgrade bought at a career manager (<see cref="Upgrades.Fittings.HasBooster"/>).
        /// Unfitted, it is built but idle, so buying it fits it at once.
        /// </summary>
        public bool Fitted
        {
            get
            {
                if (!upgrade)
                    return true;
                var guid = Upgrades.Fittings.CarGuidOf(car);
                if (fittedVersion != Upgrades.Fittings.Version || fittedGuid != guid)
                {
                    fittedVersion = Upgrades.Fittings.Version;
                    fittedGuid = guid;
                    fitted = Upgrades.Fittings.HasBooster(car);
                }
                return fitted;
            }
        }

        /// <summary>The loco it is fitted to.</summary>
        public TrainCar Car => car;

        /// <summary>What it did on the last tick.</summary>
        public BoosterFlow Flow { get; internal set; }

        /// <summary>Whether the cab's latch is up (the host's, on a multiplayer client).</summary>
        public bool Latched => latch.Value > 0.5f;

        /// <summary>Clashes since the loco was loaded.</summary>
        public int Clashes { get; internal set; }

        /// <summary>Every booster, by car.</summary>
        public static IEnumerable<RealisticBooster> All => ByCar.Where(pair => pair.Key != null).Select(pair => pair.Value).ToList();

        public static bool TryGet(TrainCar car, out RealisticBooster booster)
        {
            booster = null;
            // Destroyed cars are dropped as a new booster registers, not on
            // each look: this is asked from the overlay's drawing.
            return car != null && ByCar.TryGetValue(car, out booster);
        }

        public override void Tick(float delta)
        {
            // On a multiplayer client the host simulates; its ports carry the host's values.
            if (Net.IsClient)
                return;
            if (!resolved)
                Resolve();
            if (def.FrictionCoefficient > 0f)
                friction = def.FrictionCoefficient;
            var main = mainTorque.Value;
            if (!(delta > 0f))
            {
                torqueOut.Value = main;
                return;
            }
            if (!Fitted || !ArrangementSuits)
            {
                // Not bought, or on a wheel arrangement it wasn't built for:
                // the main engine's torque straight through, the latch held
                // down, no steam, and its axle out of the powered count.
                torqueOut.Value = main;
                if (latch.Value > 0f)
                    latch.Value = 0f;
                Flow = default;
                SetTrailerAxle(false);
                return;
            }

            var rev = reverser.Value;
            var cutoff = Mathf.Sign(rev) * Mathf.Lerp(def.MinCutoff, def.MaxCutoff, Mathf.Abs(rev));
            var sand = sandCoefficient.IsConnected ? Mathf.Clamp(sandCoefficient.Value, 1f, 2f) : 1f;
            var flow = Model.Step(delta, new BoosterInputs
            {
                Latch = latch.Value > 0.5f,
                Idling = idle.IsConnected && idle.Value > 0.5f,
                ChestPressure = chestPressure.Value,
                BoilerPressure = boilerPressure.Value,
                Cutoff = cutoff,
                Speed = car != null && car.rb != null ? car.GetForwardSpeed() : 0f,
                AdhesionLimit = friction * sand * def.TrailerLoad * 9.81f,
            }, pressure => SteamTables.SteamSpecificVolume(pressure) / 1000f);
            Flow = flow;

            if (flow.LatchDropped && latch.Value > 0f)
                latch.Value = 0f;
            if (flow.Event == BoosterEvent.Clash)
                Clashes++;
#if !PUBLIC_RELEASE
            if (flow.Event != BoosterEvent.None && Main.Settings.LogCoalDeliveries)
                Main.Mod.Logger.Log($"{car?.ID}: booster {flow.Event} at {(car != null ? car.GetForwardSpeed() * 2.237f : 0f):F1} mph");
#endif

            torqueOut.Value = main + flow.Force * wheelRadius;
            SetTrailerAxle(Model.InGear);

            pendingSteam += flow.SteamUsed * delta;
            var vessel = InjectorRig.VesselOf(boiler);
            if (vessel != null && pendingSteam >= RealisticStoker.SteamLot)
            {
                vessel.RemoveSteam(Mathf.Min(pendingSteam, vessel.mass));
                pendingSteam = 0f;
            }
        }

        /// <summary>
        /// Whether the loco still has the powered axles it was built with, the
        /// drivers the booster was fitted behind. JimTaplin's Rearranged S282 changes the S282's wheel
        /// arrangement and sets its powered-axle count to suit; on another
        /// count (an articulated engine, a Pacific, a 4-12-2) the booster stays
        /// idle, as unfitted, and leaves the count as Rearranged set it.
        /// </summary>
        public bool ArrangementSuits
        {
            get
            {
                var built = def.BuiltPoweredAxles > 0 ? def.BuiltPoweredAxles : def.DrivingAxles;
                if (poweredAxles == null || built <= 0)
                    return true;
                var suits = Mathf.Approximately(trailerAxle.Own(poweredAxles.Value, Configured), built);
                if (suits != suitedLast)
                {
                    suitedLast = suits;
                    Main.Mod?.Logger.Log(suits
                        ? $"{car?.ID}: booster back in use: {built} powered axles again"
                        : $"{car?.ID}: booster idle: the engine now has {trailerAxle.Own(poweredAxles.Value, Configured):F0} powered axles, not the {built} it was built with");
                }
                return suits;
            }
        }

        private readonly TrailerAxle trailerAxle = new TrailerAxle();
        private bool suitedLast = true;

        // The loco's configured powered-axle count: the stock port's
        // definition, which Rearranged sets with the port for each arrangement.
        private ConfigurablePortDefinition poweredAxlesDefinition;

        private float? Configured => poweredAxlesDefinition != null ? poweredAxlesDefinition.value : (float?)null;

        // One axle more while in gear, on the count the loco has, and taken
        // off again after: never an absolute count over another mod's.
        private void SetTrailerAxle(bool inGear)
        {
            if (poweredAxles == null)
                return;
            var next = trailerAxle.Next(poweredAxles.Value, inGear, Configured);
            if (next.HasValue)
                poweredAxles.Value = next.Value;
        }

        private void Resolve()
        {
            resolved = true;
            if (car == null)
                return;
            wheelRadius = car.carLivery.parentType.wheelRadius > 0f ? car.carLivery.parentType.wheelRadius : 1f;
            friction = car.carLivery.parentType.WheelslipFrictionCoef > 0f ? car.carLivery.parentType.WheelslipFrictionCoef : 0.2f;
            var flow = car.SimController != null ? car.SimController.SimulationFlow : null;
            boiler = flow != null ? flow.OrderedSimComps.OfType<Boiler>().FirstOrDefault() : null;
            if (flow != null && !string.IsNullOrEmpty(def.PoweredAxlesPortId))
                flow.TryGetPort(def.PoweredAxlesPortId, out poweredAxles);
            var axlesId = BoosterWiring.ComponentOf(def.PoweredAxlesPortId);
            var graph = car.SimController != null ? car.SimController.connectionsDefinition : null;
            poweredAxlesDefinition = poweredAxles != null && axlesId != null && graph != null && graph.executionOrder != null
                ? graph.executionOrder.OfType<ConfigurablePortDefinition>().FirstOrDefault(d => d != null && d.ID == axlesId)
                : null;
            Main.Mod.Logger.Log($"{car.ID}: booster '{id}' on the trailer axle: {Model.Spec.StartingPull(Model.Spec.MaxInletPressure) / 4.44822f:F0} lb at " +
                                $"{Model.Spec.MaxInletPressure:F1} bar, axle {def.TrailerLoad:F0} kg, " +
                                $"{(poweredAxles != null ? $"powered axles {def.DrivingAxles} + 1 in gear ({(poweredAxlesDefinition != null ? $"configured {poweredAxlesDefinition.value:F0}" : "no configured count")})" : "no powered-axle port")}, " +
                                $"steam {(boiler != null ? "from the boiler" : "nowhere: no boiler found")}");
        }
    }
}
