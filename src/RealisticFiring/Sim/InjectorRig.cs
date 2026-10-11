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

 private static readonly Dictionary<string, float> PeriodDelivery = new Dictionary<string, float>
        {
            // USRA light Mikado: "Nathan non-lifting injectors", size not
            // given (RME Aug 1918 p.438); the bigger heavy Mikado had "Hancock
            // No. 11 non-lifting injectors" (RME Sept 1918 p.494). A No. 11,
            // 4,400 gal/h at 200 psi (Hancock's manual, 1921, p.24). The game's
            // is 20 kg/s.
            ["LocoS282A"] = 4.6f,
            // USRA 0-6-0: "No. 11 non-lifting Nathan injector" (RME Nov 1918). The game's is 5.
            ["LocoS060"] = 4.6f,
            // NYC J-3a: no injector given (RME May 1938); the NYC fitted
            // Nathan non-lifters beside its feedwater heaters (L-3, RME Jan
            // 1941), about 10,000 gal/h (inferred). The game's is 36.
            ["S464-730"] = 10.5f,
            // UP FEF: "one Nathan non-lifting injector ... 10,000 gal. per hr."
            // (FEF-1, RME Feb 1938 p.47); the FEF-3 was built like the FEF-1
            // and FEF-2. The game's are 60 (844) and 50.
            ["FEF-3"] = 10.5f,
            ["S484-840A"] = 10.5f,
            // Santa Fe 2900 class: no injector found; the road's 1938 2-10-4s
            // had "a Chicago non-lifting, having a capacity of 13,500 gal. per
            // hour" (RME Feb 1939 p.51), so taken as the same (inferred). The
            // game's is 35.
            ["A.T.&S.F. 2901-A"] = 14.2f,
        };

        private static readonly Dictionary<Boiler, InjectorRig> ByBoiler = new Dictionary<Boiler, InjectorRig>();
        private static readonly Dictionary<TrainCar, InjectorRig> ByCar = new Dictionary<TrainCar, InjectorRig>();
        private static readonly GameSteamTables Steam = new GameSteamTables();

        private readonly Boiler boiler;

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

        /// <summary>The feedwater heater and pump, on a loco whose prototype had one; else null.</summary>
        public FeedPump Pump { get; private set; }

        /// <summary>Its make and type.</summary>
        public string PumpName { get; private set; }

        /// <summary>What the pump did on the last tick.</summary>
        public FeedPumpFlow PumpFlow { get; internal set; }

        /// <summary>The pump's operating valve, 0 to 1.</summary>
        public float PumpValve => pumpValve != null ? pumpValve.Value : 0f;

        private Port pumpValve;

        /// <summary>The feed pump's cab valve port, for the fireman's hand; null without a pump.</summary>
        internal Port PumpValvePort => pumpValve;
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
            AttachPump(new FeedPump(new FeedPumpSpec(fit.Type, fit.Capacity), TenderWaterTemperature), fit.Name, valve,
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
        private InjectorFlow flow;

        public float BoilerPressure => Vessel(boiler)?.pressure ?? 1f;

        /// <summary>Water in the tender, in kg as the boiler takes it.</summary>
        public float TenderWater
        {
            get
            {
                var multiplier = WaterConsumptionMultiplier(boiler) > 0f ? WaterConsumptionMultiplier(boiler) : 1f;
                return Mathf.Max(0f, boiler.water.Value / multiplier);
            }
        }

        /// <summary>Boiler water level, 0 to 1, from the stock read-out.</summary>
        public float WaterLevel => boiler.waterLevelReadOut.Value;

        /// <summary>Set by the cab controls while the hand pump is being worked.</summary>
        public bool HandPumping { get; set; }

        // Multiplayer guests working the hand pump on this boiler, by player:
        // one letting go leaves the others pumping (audit 2026-10-10).
        private readonly HashSet<byte> remotePumpers = new HashSet<byte>();

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

        /// <summary>Set by the cab controls while a hydrant hose is filling the boiler.</summary>
        public bool HydrantFilling { get; set; }

        // The hose checks this often, in s, that its train is still at the water point.
        private const float HoseCheckInterval = 1f;
        private float hoseCheck;
        private string hoseStopped;

        /// <summary>Why the hose last came off by itself, once, for a notice; null if it has not.</summary>
        public string TakeHoseStopped()
        {
            var reason = hoseStopped;
            hoseStopped = null;
            return reason;
        }

        // The hose runs only while it can fill this boiler at a water point.
        // The host's rig checks it, so an engine nobody tends shuts it too,
        // and one that leaves the water point (audit 2026-10-10).
        private void GuardHose(float delta)
        {
            if (!CanHydrantFill)
                StopHose(BoilerPressure > HydrantMaxPressure ? "the boiler has pressure" : "the boiler is full");
            else if (Car != null && (hoseCheck -= delta) <= 0f)
            {
                hoseCheck = HoseCheckInterval;
                if (!WaterPoints.Serves(Car))
                    StopHose("the train left the water point");
            }
        }

        private void StopHose(string reason)
        {
            HydrantFilling = false;
            hoseStopped = reason;
            Main.Mod?.Logger.Log($"{Car?.ID}: hydrant hose off: {reason}");
        }

        /// <summary>Water the hand pump or hose put in on the last tick, in kg/s.</summary>
        public float FillRate { get; internal set; }

        public bool CanHandPump => BoilerPressure <= HandPumpMaxPressure;

        public bool CanHydrantFill => BoilerPressure <= HydrantMaxPressure && WaterLevel < HydrantFillLevel;

        /// <summary>
        /// Sets up a realistic injector on each boiler of <paramref name="car"/>
        /// whose injector is worked by a cab control. A boiler whose injector
        /// port is driven by anything else keeps the stock behavior, except
        /// JK3's verified two-valve averaging graph. Returns a line for
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
                if (TryCreate(car, boiler, car.carLivery.id, flow.OrderedSimComps, out var note)
                    && TryGet(boiler, out var rig) && rig.FitPump(flow) is string pump)
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
            => TryCreate(car, boiler, car != null ? car.carLivery.id : null, null, out note);

        internal static bool TryCreate(TrainCar car, Boiler boiler, string livery, IEnumerable<SimComponent> components, out string note)
        {
            var control = boiler.injectorControl.IsConnected ? ReferencedPort(boiler.injectorControl) : null;
            var controls = control != null && control.type == PortType.EXTERNAL_IN
                ? new[] { control }
                : Jk3Controls(livery, components, control);
            if (controls == null)
            {
                note = $"boiler '{boiler.id}' keeps the stock injector (driven by {(control != null ? $"'{control.id}'" : "nothing")})";
                return false;
            }

            var rated = SafetyValveOpeningPressure(boiler);
            var stockRate = MaxInjectorRate(boiler);
            var period = 0f;
            var resized = car != null && car.carLivery != null && PeriodDelivery.TryGetValue(car.carLivery.id, out period);
            var maxRate = resized ? period : stockRate;
            if (maxRate <= 0f || rated <= InjectorSpec.DefaultStartPressure)
            {
                note = $"boiler '{boiler.id}' keeps the stock injector (rate {maxRate}, safety valve {rated} bar)";
                return false;
            }

            var ports = controls.Length > 1 ? components.SelectMany(c => c.GetAllPorts()).ToList() : null;
            var circuits = controls.Select((port, i) => new InjectorCircuit(port,
                new Injector(new InjectorSpec(maxRate / controls.Length, rated)),
                controls.Length == 1 ? "" : i == 0 ? "right" : "left",
                ports?.SingleOrDefault(p => p.id == $"injectorParticles.INJECTOR_{(i == 0 ? "R" : "L")}_PARTICLES_CLAMP")))
                .ToList().AsReadOnly();
            var rig = new InjectorRig(car, boiler, circuits);
            PruneDestroyedCars();
            ByBoiler[boiler] = rig;
            if (car != null)
                ByCar[car] = rig;
            note = $"realistic injector{(controls.Length > 1 ? "s" : "")} on '{boiler.id}' worked by " +
                   string.Join(" and ", controls.Select(p => $"'{p.id}'")) +
                   $": {maxRate / controls.Length:F1} kg/s{(controls.Length > 1 ? " each" : "")} at {rated:F1} bar" +
                   (resized ? $" (a period size; the game's is {stockRate:F1})" : "") +
                   $", picks up from {InjectorSpec.DefaultStartPressure:F1} bar";
            return true;
        }

        // JK3 1.0.1 averages two independent cab valves. Accept that exact
        // graph, without treating arbitrary calculated or mechanical feeds
        // as cab injectors, or changing its ports and control save data.
        private static Port[] Jk3Controls(string livery, IEnumerable<SimComponent> components, Port control)
        {
            if (livery != "Jk3A" || components == null || control == null)
                return null;
            var all = components.ToList();
            var normalized = all.OfType<ConfigurableMultiplier>().SingleOrDefault(c => c.id == "injectorNormalized");
            var add = all.OfType<ConfigurableAdd>().SingleOrDefault(c => c.id == "injectorAdd");
            var divisor = all.OfType<ConfigurablePorts>().SingleOrDefault(c => c.id == "injectorDivisor")
                ?.ports.SingleOrDefault(p => p.id == "injectorDivisor.DIVISOR");
            var right = all.OfType<ExternalControl>().SingleOrDefault(c => c.id == "injector")?.controlExtIn;
            var left = all.OfType<ExternalControl>().SingleOrDefault(c => c.id == "injector2")?.controlExtIn;
            Port Source(PortReference r) => r.IsConnected ? ReferencedPort(r) : null;
            bool CabValve(Port p, string id) => p != null && p.id == id && p.type == PortType.EXTERNAL_IN
                && p.valueType == PortValueType.CONTROL;
            if (normalized == null || add == null || divisor == null
                || normalized.invertA || normalized.invertB || add.negativeA || add.negativeB
                || normalized.mulReadOut != control || control.id != "injectorNormalized.INJECTOR_NORMALIZED"
                || Source(normalized.aReader) != add.addReadOut || Source(normalized.bReader) != divisor
                || divisor.type != PortType.IN || divisor.IsConnectedPort || divisor.Value != 0.5f
                || !CabValve(right, "injector.EXT_IN") || !CabValve(left, "injector2.EXT_IN")
                || Source(add.aReader) != right || Source(add.bReader) != left)
                return null;
            return new[] { right, left };
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

            var drawn = 0f;
            var workingValve = 0f;
            var total = default(InjectorFlow);
            var feedEnthalpy = 0f;
            foreach (var circuit in Circuits)
            {
                circuit.Injector.Spec.TechniqueAndHeat = Main.Settings.InjectorTechniqueAndHeat;
                var flow = circuit.Injector.Step(delta, new InjectorInputs
                {
                    SteamValve = broken ? 0f : circuit.Control.Value,
                    BoilerPressure = vessel.pressure,
                    TenderWater = Mathf.Max(0f, tender - drawn),
                    BoilerSpace = space,
                    TenderWaterTemperature = TenderWaterTemperature,
                }, Steam);
                circuit.Flow = flow;
                var fed = flow.WaterIn * delta;
                if (fed > 0f)
                    vessel.AddWater(fed, flow.FeedTemperature);
                var steam = Mathf.Min(flow.SteamUsed * delta, vessel.mass);
                if (steam > 0f)
                    vessel.RemoveSteam(steam);
                drawn += flow.TenderDraw * delta;
                space = Mathf.Max(0f, space - fed);
                if (flow.WaterIn > 0f)
                    workingValve += circuit.Control.Value / Circuits.Count;
                total.WaterIn += flow.WaterIn;
                total.SteamUsed += flow.SteamUsed;
                total.TenderDraw += flow.TenderDraw;
                total.Overflow += flow.Overflow;
                total.OverflowSteam += flow.OverflowSteam;
                feedEnthalpy += flow.WaterIn * Steam.WaterEnthalpy(flow.FeedTemperature);
                LogStateChange(circuit);
            }
            total.State = Circuits[0].Flow.State;
            total.InjectorTemperature = Circuits[0].Flow.InjectorTemperature;
            total.RestartLimit = Circuits[0].Flow.RestartLimit;
            total.FeedTemperature = total.WaterIn > 0f ? Steam.WaterTemperature(feedEnthalpy / total.WaterIn) : TenderWaterTemperature;
            Flow = Circuits.Count == 1 ? Circuits[0].Flow : total;
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
            if (HydrantFilling && !Net.IsClient)
                GuardHose(delta);
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

            boiler.injectorFlowNormalizedReadOut.Value = workingValve;
            if (drawn > 0f)
            {
                var gameParams = GameParams(boiler);
                boiler.waterConsumption.Value = drawn * multiplier * (gameParams != null ? gameParams.ResourceConsumptionModifier : 1f);
            }
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

        private void LogStateChange(InjectorCircuit circuit)
        {
            var flow = circuit.Flow;
            if (flow.State == circuit.LastLoggedState)
                return;
            var from = circuit.LastLoggedState;
            circuit.LastLoggedState = flow.State;
            if (!Main.Settings.LogCoalDeliveries)
                return;
            var name = string.IsNullOrEmpty(circuit.Name) ? "injector" : circuit.Name + " injector";
            Main.Mod.Logger.Log($"{Car?.ID}: {name} {from} -> {flow.State} at {BoilerPressure:F1} bar, lever {circuit.Control.Value:F2}, " +
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
