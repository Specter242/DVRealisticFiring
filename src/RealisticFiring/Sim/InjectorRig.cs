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
    /// A boiler's realistic injector, standing in for the stock
    /// <c>Boiler.SimulateInjector</c> (a valve that adds water at 110 °C at
    /// any pressure). The cab's injector lever is the steam valve of a
    /// <see cref="Injector"/>; its feed and live steam go into the boiler's
    /// own water vessel, and what it takes from the tender (spills included)
    /// is reported on the boiler's stock water-consumption port, so the
    /// tender, saves, and HUD work as before. Also carries the hand pump and
    /// the hydrant hose for filling a boiler with no steam.
    /// </summary>
    internal sealed class InjectorRig
    {
        /// <summary>The game does not model tender water temperature.</summary>
        public const float TenderWaterTemperature = 15f;

        /// <summary>A two-man boiler hand pump, from the tender, in kg/s while worked.</summary>
        public const float HandPumpRate = 1.5f;

        /// <summary>The hand pump cannot force water in above this, absolute bar.</summary>
        public const float HandPumpMaxPressure = 4f;

        /// <summary>A shed hydrant hose, from the mains, in kg/s.</summary>
        public const float HydrantRate = 15f;

        /// <summary>A hose only fills a boiler with no pressure in it, absolute bar.</summary>
        public const float HydrantMaxPressure = 1.5f;

        /// <summary>The hose fill stops at this water level, as a fireman fills to the top of the glass.</summary>
        public const float HydrantFillLevel = 0.85f;

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
        private InjectorState lastState;

        private InjectorRig(TrainCar car, Boiler boiler, Injector injector, string leverPortId)
        {
            Car = car;
            this.boiler = boiler;
            Injector = injector;
            LeverPortId = leverPortId;
        }

        public TrainCar Car { get; }

        public Injector Injector { get; }

        /// <summary>The cab control port its lever (or levers, one each side) works.</summary>
        public string LeverPortId { get; }

        /// <summary>The steam valve's opening, as the last lever moved set it.</summary>
        public float LeverValue => boiler.injectorControl.Value;

        /// <summary>The feedwater heater and pump, on a loco whose prototype had one; else null.</summary>
        public FeedPump Pump { get; private set; }

        /// <summary>Its make and type.</summary>
        public string PumpName { get; private set; }

        /// <summary>What the pump did on the last tick.</summary>
        public FeedPumpFlow PumpFlow { get; internal set; }

        /// <summary>The pump's operating valve, 0 to 1.</summary>
        public float PumpValve => pumpValve != null ? pumpValve.Value : 0f;

        private Port pumpValve;
        private Port exhaustFlow;
        private Port fullExhaustFlow;
        private Port chestPressure;
        private float pendingPumpSteam;

        // Fits the heater and pump when the loco's prototype had one and its
        // sim has the operating valve. Returns a line for the log, or null.
        private string FitPump(SimulationFlow flow)
        {
            if (Car == null || flow == null || !FeedwaterFit.TryGet(Car.carLivery.id, out var fit)
                || !flow.TryGetPort(FeedwaterFit.Valve.PortId, out var valve))
                return null;
            var engine = flow.OrderedSimComps.OfType<ReciprocatingSteamEngine>().FirstOrDefault();
            AttachPump(new FeedPump(new FeedPumpSpec(fit.Type, fit.CapacityShare * Injector.Spec.MaxDelivery), TenderWaterTemperature), fit.Name, valve,
                engine?.exhaustFlowReadOut, engine?.maxFlowReadOut, engine?.steamChestPressureReadOut);
            return $"{fit.Name} feedwater heater and pump: {Pump.Spec.Capacity:F1} kg/s, worked by '{valve.id}'" +
                   (engine == null ? "; no steam engine to heat it" : "");
        }

        /// <summary>Gives the rig a pump directly, for checks outside the game.</summary>
        internal void AttachPump(FeedPump pump, string name, Port valve, Port exhaust, Port fullExhaust, Port chest)
        {
            Pump = pump;
            PumpName = name;
            pumpValve = valve;
            exhaustFlow = exhaust;
            fullExhaustFlow = fullExhaust;
            chestPressure = chest;
        }

        /// <summary>What the injector did on the last tick.</summary>
        public InjectorFlow Flow { get; internal set; }

        public float BoilerPressure => Vessel(boiler)?.pressure ?? 1f;

        /// <summary>Boiler water level, 0 to 1, from the stock read-out.</summary>
        public float WaterLevel => boiler.waterLevelReadOut.Value;

        /// <summary>Set by the cab controls while the hand pump is being worked.</summary>
        public bool HandPumping { get; set; }

        /// <summary>Set while a multiplayer client works the hand pump on this boiler.</summary>
        public bool RemoteHandPumping { get; set; }

        /// <summary>Set by the cab controls while a hydrant hose is filling the boiler.</summary>
        public bool HydrantFilling { get; set; }

        /// <summary>Water the hand pump or hose put in on the last tick, in kg/s.</summary>
        public float FillRate { get; internal set; }

        public bool CanHandPump => BoilerPressure <= HandPumpMaxPressure;

        public bool CanHydrantFill => BoilerPressure <= HydrantMaxPressure && WaterLevel < HydrantFillLevel;

        /// <summary>
        /// Sets up a realistic injector on each boiler of <paramref name="car"/>
        /// whose injector is worked by a cab control. A boiler whose injector
        /// port is driven by anything else (a modded loco's axle pump or
        /// mechanical injector) keeps the stock behavior. Returns a line for
        /// the log, or null when the car has no boiler.
        /// </summary>
        public static string Attach(TrainCar car, SimulationFlow flow)
        {
            var boilers = flow.OrderedSimComps.OfType<Boiler>().ToList();
            if (boilers.Count == 0)
                return null;

            var notes = new List<string>();
            foreach (var boiler in boilers)
            {
                if (TryCreate(car, boiler, out var note) && TryGet(boiler, out var rig) && rig.FitPump(flow) is string pump)
                    note += "; " + pump;
                notes.Add(note);
            }
            return string.Join("; ", notes);
        }

        /// <summary>
        /// Gives <paramref name="boiler"/> a realistic injector if a cab control
        /// works it, and says what was decided.
        /// </summary>
        internal static bool TryCreate(TrainCar car, Boiler boiler, out string note)
        {
            var control = boiler.injectorControl.IsConnected ? ReferencedPort(boiler.injectorControl) : null;
            if (control == null || control.type != PortType.EXTERNAL_IN)
            {
                note = $"boiler '{boiler.id}' keeps the stock injector (driven by {(control != null ? $"'{control.id}'" : "nothing")})";
                return false;
            }

            var rated = SafetyValveOpeningPressure(boiler);
            var maxRate = MaxInjectorRate(boiler);
            if (maxRate <= 0f || rated <= InjectorSpec.DefaultStartPressure)
            {
                note = $"boiler '{boiler.id}' keeps the stock injector (rate {maxRate}, safety valve {rated} bar)";
                return false;
            }

            var rig = new InjectorRig(car, boiler, new Injector(new InjectorSpec(maxRate, rated)), control.id);
            PruneDestroyedCars();
            ByBoiler[boiler] = rig;
            if (car != null)
                ByCar[car] = rig;
            note = $"realistic injector on '{boiler.id}' worked by '{control.id}': {maxRate:F1} kg/s at {rated:F1} bar, " +
                   $"picks up from {InjectorSpec.DefaultStartPressure:F1} bar";
            return true;
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

        /// <summary>The rig the player tends from <paramref name="car"/>: its own, or a coupled loco's.</summary>
        public static bool TryGetTended(TrainCar car, out InjectorRig rig)
        {
            if (TryGet(car, out rig))
                return true;
            if (car == null)
                return false;
            foreach (var coupler in new[] { car.frontCoupler, car.rearCoupler })
            {
                var neighbor = coupler != null && coupler.coupledTo != null ? coupler.coupledTo.train : null;
                if (TryGet(neighbor, out rig))
                    return true;
            }
            return false;
        }

        /// <summary>Runs in place of the stock injector step.</summary>
        public void Simulate(float delta)
        {
            var vessel = Vessel(boiler);
            if (vessel == null || !(delta > 0f))
                return;

            var multiplier = WaterConsumptionMultiplier(boiler) > 0f ? WaterConsumptionMultiplier(boiler) : 1f;
            var tender = Mathf.Max(0f, boiler.water.Value / multiplier);
            var space = Mathf.Max(0f, TotalVolume(boiler) - vessel.waterVolume);
            var broken = boiler.isBrokenReadOut.Value != 0f;

            Injector.Spec.TechniqueAndHeat = Main.Settings.InjectorTechniqueAndHeat;
            var flow = Injector.Step(delta, new InjectorInputs
            {
                SteamValve = broken ? 0f : boiler.injectorControl.Value,
                BoilerPressure = vessel.pressure,
                TenderWater = tender,
                BoilerSpace = space,
                TenderWaterTemperature = TenderWaterTemperature,
            }, Steam);
            Flow = flow;

            var fed = flow.WaterIn * delta;
            if (fed > 0f)
                vessel.AddWater(fed, flow.FeedTemperature);
            var steam = Mathf.Min(flow.SteamUsed * delta, vessel.mass);
            if (steam > 0f)
                vessel.RemoveSteam(steam);
            var drawn = flow.TenderDraw * delta;

            space = Mathf.Max(0f, space - fed);
            if (Pump != null)
            {
                var pumpFlow = Pump.Step(delta, new FeedPumpInputs
                {
                    Valve = broken ? 0f : PumpValve,
                    BoilerPressure = vessel.pressure,
                    BoilerSpace = space,
                    TenderWater = Mathf.Max(0f, tender - drawn),
                    TenderWaterTemperature = TenderWaterTemperature,
                    ExhaustFlow = exhaustFlow != null ? exhaustFlow.Value : 0f,
                    FullExhaustFlow = fullExhaustFlow != null ? fullExhaustFlow.Value : 0f,
                    ChestPressure = chestPressure != null ? chestPressure.Value : vessel.pressure,
                }, Steam);
                LogPumpChange(pumpFlow);
                PumpFlow = pumpFlow;
                var pumped = pumpFlow.WaterIn * delta;
                if (pumped > 0f)
                    vessel.AddWater(pumped, pumpFlow.FeedTemperature);
                space = Mathf.Max(0f, space - pumped);
                drawn += pumpFlow.TenderDraw * delta;
                // A boiler's mass is too big for a float to take a few grams a tick.
                pendingPumpSteam += pumpFlow.SteamUsed * delta;
                if (pendingPumpSteam >= RealisticStoker.SteamLot)
                {
                    vessel.RemoveSteam(Mathf.Min(pendingPumpSteam, vessel.mass));
                    pendingPumpSteam = 0f;
                }
            }

            // Filling with no steam: the hose from the mains, or the hand pump from the tender.
            FillRate = 0f;
            if (HydrantFilling && !broken && CanHydrantFill)
            {
                var hose = Mathf.Min(HydrantRate * delta, space);
                vessel.AddWater(hose, TenderWaterTemperature);
                FillRate = hose / delta;
            }
            else if ((HandPumping || RemoteHandPumping) && !broken && CanHandPump)
            {
                var pumped = Mathf.Min(HandPumpRate * delta, space, Mathf.Max(0f, tender - drawn));
                vessel.AddWater(pumped, TenderWaterTemperature);
                drawn += pumped;
                FillRate = pumped / delta;
            }

            boiler.injectorFlowNormalizedReadOut.Value = flow.WaterIn > 0f ? boiler.injectorControl.Value : 0f;
            if (drawn > 0f)
            {
                var gameParams = GameParams(boiler);
                boiler.waterConsumption.Value = drawn * multiplier * (gameParams != null ? gameParams.ResourceConsumptionModifier : 1f);
            }

            LogStateChange(flow);
        }

        private FeedPumpState lastPumpState;

        private void LogPumpChange(FeedPumpFlow flow)
        {
            if (flow.State == lastPumpState)
                return;
            var from = lastPumpState;
            lastPumpState = flow.State;
            if (Main.Settings.LogCoalDeliveries)
                Main.Mod.Logger.Log($"{Car?.ID}: feed pump {from} -> {flow.State} at {BoilerPressure:F1} bar, valve {PumpValve:F2}, " +
                                    $"feed {flow.WaterIn:F2} kg/s at {flow.FeedTemperature:F0} C, steam {flow.SteamUsed:F2} kg/s, heater {flow.HeaterTemperature:F0} C");
        }

        private void LogStateChange(InjectorFlow flow)
        {
            if (flow.State == lastState)
                return;
            var from = lastState;
            lastState = flow.State;
            if (!Main.Settings.LogCoalDeliveries)
                return;
            Main.Mod.Logger.Log($"{Car?.ID}: injector {from} -> {flow.State} at {BoilerPressure:F1} bar, lever {boiler.injectorControl.Value:F2}, " +
                                $"feed {flow.WaterIn:F2} kg/s at {flow.FeedTemperature:F0} C, steam {flow.SteamUsed:F2} kg/s, " +
                                $"overflow {flow.Overflow:F2} kg/s water, {flow.OverflowSteam:F2} kg/s steam, " +
                                $"injector {flow.InjectorTemperature:F0} C (takes below {flow.RestartLimit:F0} C)");
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
