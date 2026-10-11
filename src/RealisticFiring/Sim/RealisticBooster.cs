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
                ByCar[car] = this;
        }

        public Booster Model { get; }

        /// <summary>The loco it is fitted to.</summary>
        public TrainCar Car => car;

        /// <summary>What it did on the last tick.</summary>
        public BoosterFlow Flow { get; internal set; }

        /// <summary>Clashes since the loco was loaded.</summary>
        public int Clashes { get; internal set; }

        /// <summary>Every booster, by car.</summary>
        public static IEnumerable<RealisticBooster> All => ByCar.Where(pair => pair.Key != null).Select(pair => pair.Value).ToList();

        public static bool TryGet(TrainCar car, out RealisticBooster booster)
        {
            booster = null;
            if (car == null)
                return false;
            foreach (var gone in ByCar.Keys.Where(c => c == null).ToList())
                ByCar.Remove(gone);
            return ByCar.TryGetValue(car, out booster);
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
            if (flow.Event != BoosterEvent.None && Main.Settings.LogCoalDeliveries)
                Main.Mod.Logger.Log($"{car?.ID}: booster {flow.Event} at {(car != null ? car.GetForwardSpeed() * 2.237f : 0f):F1} mph");

            torqueOut.Value = main + flow.Force * wheelRadius;
            if (poweredAxles != null)
            {
                var axles = def.DrivingAxles + (Model.InGear ? 1 : 0);
                if (!Mathf.Approximately(poweredAxles.Value, axles))
                    poweredAxles.Value = axles;
            }

            pendingSteam += flow.SteamUsed * delta;
            var vessel = InjectorRig.VesselOf(boiler);
            if (vessel != null && pendingSteam >= RealisticStoker.SteamLot)
            {
                vessel.RemoveSteam(Mathf.Min(pendingSteam, vessel.mass));
                pendingSteam = 0f;
            }
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
            if (Main.Settings.LogFireboxCreation)
                Main.Mod.Logger.Log($"{car.ID}: booster '{id}' on the trailer axle: {Model.Spec.StartingPull(Model.Spec.MaxInletPressure) / 4.44822f:F0} lb at " +
                                    $"{Model.Spec.MaxInletPressure * 14.5038f:F0} psi, axle {def.TrailerLoad:F0} kg, " +
                                    $"{(poweredAxles != null ? $"powered axles {def.DrivingAxles} + 1 in gear" : "no powered-axle port")}, " +
                                    $"steam {(boiler != null ? "from the boiler" : "nowhere: no boiler found")}");
        }
    }
}
