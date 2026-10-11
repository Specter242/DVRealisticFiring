using System.Linq;
using HarmonyLib;
using LocoSim.Implementations;
using UnityEngine;

namespace RealisticFiring.Sim
{
    /// <summary>
    /// Stands in for the CCL stoker an oil burner fakes its feed with,
    /// under the same component ID, ports, and port references. Its control
    /// (the cab's "stoker" valve) is now the firing valve, which the
    /// <see cref="RealisticOilFirebox"/> reads; the firebox draws its oil
    /// through here from the tender at the stoker's own multiplier, as CCL's
    /// stoker would (it takes amount × multiplier from the tender for amount
    /// into the firebox), so each mod keeps its fuel economy. The atomizer's
    /// and heater's steam goes through the stoker's steam read-out where the
    /// loco wires it into the boiler's consumption, and straight from the
    /// boiler otherwise.
    /// </summary>
    internal sealed class OilFeed : SimComponent
    {
        private static readonly AccessTools.FieldRef<PortReference, Port> ReferencedPort =
            AccessTools.FieldRefAccess<PortReference, Port>("port");

        private readonly Port steamConsumption;
        private readonly Port stokingNormalized;
        private readonly PortReference control;
        private readonly PortReference coalAmount;
        private readonly PortReference coalConsume;

        private readonly TrainCar car;
        private readonly float multiplier;
        private readonly bool steamWired;
        private RealisticOilFirebox firebox;
        private Boiler boiler;
        private bool resolved;
        private float pendingSteam;

        public OilFeed(StokerDefinitionValues def, TrainCar car, float tenderPerFireboxKg, bool steamWired)
            : base(def.Id)
        {
            this.car = car;
            multiplier = tenderPerFireboxKg > 0f ? tenderPerFireboxKg : 1f;
            this.steamWired = steamWired;
            ControlName = def.Control != null ? def.Control.ID : "";

            steamConsumption = AddPort(def.SteamConsumptionReadOut);
            stokingNormalized = AddPort(def.StokingNormalizedReadOut);
            control = AddPortReference(def.Control);
            AddPortReference(def.SteamPressure);
            AddPortReference(def.FireboxCoalLevel);
            AddPortReference(def.FireboxCoalCapacity);
            AddPortReference(def.FireboxCoalControl);
            coalAmount = AddPortReference(def.CoalAmount);
            coalConsume = AddPortReference(def.CoalConsume);
        }

        /// <summary>The control its firing valve is, by the stoker's reference name.</summary>
        public string ControlName { get; }

        /// <summary>The cab control's port the firing valve is, once the graph is connected; null before.</summary>
        public string ControlPortId => control.IsConnected ? ReferencedPort(control)?.id : null;

        /// <summary>The firing valve's cab port, for the fireman's hand.</summary>
        internal Port ControlPort => control.IsConnected ? ReferencedPort(control) : null;

        /// <summary>The firing valve, 0 shut to 1 wide open.</summary>
        public float FiringValve => Mathf.Clamp01(control.Value);

        /// <summary>Fuel the tender can give, in firebox kg.</summary>
        public float TenderFuel => Mathf.Max(0f, coalAmount.Value) / (multiplier * Modifier);

        private float Modifier
        {
            get
            {
                var modifier = gameParams != null ? gameParams.ResourceConsumptionModifier : 1f;
                return modifier > 0f ? modifier : 1f;
            }
        }

        public override void Tick(float delta)
        {
            // On a multiplayer client the host simulates; its ports carry the host's values.
            if (Net.IsClient)
                return;
            if (!resolved)
                Resolve();
            stokingNormalized.Value = FiringValve;
        }

        /// <summary>Takes <paramref name="fireboxKg"/> of oil from the tender, and the burner's steam.</summary>
        public void Draw(float fireboxKg, float steamPerSecond, float delta)
        {
            // Every tick, nothing included, as CCL's stoker writes it: a fuel
            // container takes and resets it, a tender's forwarded port is read.
            coalConsume.Value = Mathf.Max(0f, fireboxKg) * multiplier * Modifier;
            steamConsumption.Value = steamPerSecond;
            if (steamWired || !(delta > 0f))
                return;
            pendingSteam += steamPerSecond * delta;
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
            if (RealisticOilFirebox.TryGet(car, out firebox))
                firebox.Feed = this;
            var flow = car.SimController != null ? car.SimController.SimulationFlow : null;
            boiler = flow != null ? flow.OrderedSimComps.OfType<Boiler>().FirstOrDefault() : null;
        }

        /// <summary>Joins the firebox at once, before either ticks.</summary>
        internal void JoinFirebox()
        {
            if (car != null && RealisticOilFirebox.TryGet(car, out firebox))
                firebox.Feed = this;
        }
    }
}
