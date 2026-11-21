using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LocoSim.Definitions;
using LocoSim.Implementations;
using RealisticFiring.Core;
using UnityEngine;

namespace RealisticFiring.Sim
{
    /// <summary>
    /// A boiler's realistic injectors, standing in for the stock
    /// <c>Boiler.SimulateInjector</c> (a valve that adds water at 110 °C at
    /// any pressure). The cab's injector lever is the steam valve of a
    /// <see cref="Injector"/>; its feed and live steam go into the boiler's
    /// own water vessel, and what it takes from the tender (spills included)
    /// is reported on the boiler's stock water-consumption port, so the
    /// tender, saves, and HUD work as before. Also carries the hand pump and
    /// the shed's water hose for filling a boiler with no steam.
    /// </summary>
    internal sealed partial class InjectorRig
    {
        /// <summary>
        /// The tender's water, °C (<see cref="Ambient.TenderWater"/>): DV
        /// Seasons' air, kept liquid, else 15 °C. The game does not model it.
        /// </summary>
        public static float TenderWaterTemperature => Ambient.TenderWater;

        /// <summary>A two-man boiler hand pump, from the tender, in kg/s while worked.</summary>
        public const float HandPumpRate = 1.5f;

        /// <summary>The hand pump cannot force water in above this, absolute bar.</summary>
        public const float HandPumpMaxPressure = 4f;

        private static readonly AccessTools.FieldRef<Boiler, WaterPressureVessel> Vessel =
            AccessTools.FieldRefAccess<Boiler, WaterPressureVessel>("vessel");
        private static readonly AccessTools.FieldRef<Boiler, float> TotalVolume =
            AccessTools.FieldRefAccess<Boiler, float>("totalVolume");
        private static readonly AccessTools.FieldRef<Boiler, float> MaxInjectorRate =
            AccessTools.FieldRefAccess<Boiler, float>("maxInjectorRate");
        private static readonly AccessTools.FieldRef<Boiler, float> WaterConsumptionMultiplier =
            AccessTools.FieldRefAccess<Boiler, float>("waterConsumptionMultiplier");
        private static readonly AccessTools.FieldRef<Boiler, float> SafetyValveOpeningPressure =
            AccessTools.FieldRefAccess<Boiler, float>("safetyValveOpeningPressure");
        private static readonly AccessTools.FieldRef<SimComponent, SimGameParams> GameParams =
            AccessTools.FieldRefAccess<SimComponent, SimGameParams>("gameParams");
        private static readonly AccessTools.FieldRef<PortReference, Port> ReferencedPort =
            AccessTools.FieldRefAccess<PortReference, Port>("port");

        private static readonly Dictionary<Boiler, InjectorRig> ByBoiler = new Dictionary<Boiler, InjectorRig>();
        private static readonly Dictionary<TrainCar, InjectorRig> ByCar = new Dictionary<TrainCar, InjectorRig>();
        private static readonly GameSteamTables Steam = new GameSteamTables();

        private readonly Boiler boiler;

        // Multiplayer guests working the hand pump on this boiler, by player:
        // one letting go leaves the others pumping (audit 2026-10-10).
        private readonly HashSet<byte> remotePumpers = new HashSet<byte>();

        private InjectorFlow flow;

        // What went into the boiler in the last step, kg, and its heat, kg °C.
        private float fedMass;
        private float fedHeat;

        private InjectorRig(TrainCar car, Boiler boiler, IReadOnlyList<InjectorCircuit> circuits)
        {
            Car = car;
            this.boiler = boiler;
            Circuits = circuits;
        }

        public TrainCar Car { get; }

        public IReadOnlyList<InjectorCircuit> Circuits { get; }

        public float MaxDelivery => Circuits.Sum(c => c.Injector.Spec.MaxDelivery);

        public InjectorCircuit CircuitFor(string portId) => Circuits.FirstOrDefault(c => c.Control.id == portId);

        /// <summary>The first injector's cab control port; two handles may work this same valve.</summary>
        public string LeverPortId => Circuits[0].Control.id;

        /// <summary>The stock combined opening, used to locate the last handle moved on a shared valve.</summary>
        public float LeverValue => boiler.injectorControl.Value;

        /// <summary>Total rates on the last tick; state and body heat belong to the first circuit.</summary>
        public InjectorFlow Flow
        {
            get => flow;
            internal set
            {
                flow = value;
                if (Circuits.Count == 1)
                    Circuits[0].Flow = value;
            }
        }

        /// <summary>The boiler's pressure, absolute bar: on a multiplayer guest, the host's (<see cref="BoilerReadout"/>).</summary>
        public float BoilerPressure => BoilerReadout.Pressure(boiler);

        /// <summary>Where the safety valves lift, absolute bar: the boiler's working pressure.</summary>
        public float WorkingPressure => SafetyValveOpeningPressure(boiler);

        /// <summary>Water in the tender, in kg as the boiler takes it.</summary>
        public float TenderWater
        {
            get
            {
                var multiplier = WaterConsumptionMultiplier(boiler) > 0f ? WaterConsumptionMultiplier(boiler) : 1f;
                return Mathf.Max(0f, boiler.water.Value / multiplier);
            }
        }

        /// <summary>Boiler water level, 0 to 1, from the stock read-out (the glass reading).</summary>
        public float WaterLevel => boiler.waterLevelReadOut.Value;

        // Physical boiler surface level for shed fill limits so a choked glass never fools the hose or direct steam.
        private float PhysicalWaterLevel =>
            BoilerWaterRig.TryGet(boiler, out var water) && water.HasTrueSurface && (water.GlassChoke.Choke > 0f || water.GlassChoke.Blowing)
                ? water.TrueSurfaceLevel
                : WaterLevel;

        /// <summary>The port the cab's gauge glass reads the water level from.</summary>
        public string WaterLevelPortId => boiler.waterLevelReadOut.id;

        /// <summary>Set by the cab controls while the hand pump is being worked.</summary>
        public bool HandPumping { get; set; }

        /// <summary>Whether any multiplayer guest works the hand pump on this boiler.</summary>
        public bool RemoteHandPumping => remotePumpers.Count > 0;

        /// <summary>A multiplayer guest starts or stops working the hand pump here.</summary>
        public void RemotePump(byte player, bool pumping)
        {
            if (pumping)
                remotePumpers.Add(player);
            else
                remotePumpers.Remove(player);
        }

        /// <summary>Water the hand pump or hose put in on the last tick, in kg/s.</summary>
        public float FillRate { get; internal set; }

        public bool CanHandPump => BoilerPressure <= HandPumpMaxPressure;

        public bool CanHoseFill => BoilerPressure <= HoseMaxPressure && PhysicalWaterLevel < HoseFillLevel;

        /// <summary>The water fed in the last step, kg: injectors, pump, hose, and hand pump.</summary>
        public float FedLastStep => fedMass;

        /// <summary>The temperature it went in at, °C.</summary>
        public float FeedTemperatureLastStep => fedMass > 0f ? fedHeat / fedMass : TenderWaterTemperature;

        /// <summary>As if the engine had stood long past finding its saved levers: a lever opened now is a fresh start.</summary>
        internal void EndResume()
        {
            foreach (var circuit in Circuits)
                circuit.Injector.Resume(0f);
        }

        /// <summary>The boiler's water and steam, for checks outside the game.</summary>
        internal WaterPressureVessel BoilerVessel => Vessel(boiler);

        /// <summary>Any boiler's water and steam, for other appliances that use its steam.</summary>
        internal static WaterPressureVessel VesselOf(Boiler boiler) => boiler != null ? Vessel(boiler) : null;

        public static bool TryGet(Boiler boiler, out InjectorRig rig) => ByBoiler.TryGetValue(boiler, out rig);

        /// <summary>Every rig on a car, by car.</summary>
        public static IEnumerable<InjectorRig> All => ByCar.Where(pair => pair.Key != null).Select(pair => pair.Value).ToList();

        public static bool TryGet(TrainCar car, out InjectorRig rig)
        {
            rig = null;
            return car != null && ByCar.TryGetValue(car, out rig);
        }

        /// <summary>
        /// The rig the player tends from <paramref name="car"/>: its own, or a
        /// coupled loco's, the one the player was last in between two.
        /// </summary>
        public static bool TryGetTended(TrainCar car, out InjectorRig rig) =>
            TryGet(CarCouplings.Tended(car, ByCar), out rig);

        private void Feed(WaterPressureVessel vessel, float mass, float temperature)
        {
            vessel.AddWater(mass, temperature);
            fedMass += mass;
            fedHeat += mass * temperature;
        }

        private static void PruneDestroyedCars()
        {
            foreach (var car in ByCar.Keys.Where(car => car == null).ToList())
            {
                var rig = ByCar[car];
                ByCar.Remove(car);
                ByBoiler.Remove(rig.boiler);
            }
        }

        /// <summary>The game's own steam tables, so the injector agrees with its boiler.</summary>
        private sealed class GameSteamTables : ISteamProperties
        {
            public float SteamEnthalpy(float pressure) => SteamTables.SteamSpecificEnthalpy(pressure);

            public float WaterEnthalpy(float temperature) => SteamTables.WaterSpecificEnthalpyByTemp(temperature);

            public float WaterTemperature(float enthalpy) => SteamTables.WaterTempBySpecificEnthalpy(enthalpy);

            public float SaturationTemperature(float pressure) =>
                SteamTables.WaterTempBySpecificEnthalpy(SteamTables.WaterSpecificEnthalpy(pressure));
        }
    }
}
