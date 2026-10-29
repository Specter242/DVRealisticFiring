using System.Collections.Generic;
using System.Linq;
using DV.Simulation.Cars;
using DV.Simulation.Controllers;
using HarmonyLib;
using LocoSim.Definitions;
using LocoSim.Implementations;
using LocoSim.Resources;
using RealisticFiring.Core;
using RealisticFiring.Visuals;
using UnityEngine;

namespace RealisticFiring.Sim
{
 internal sealed class FiremanRig
    {
        // Standing: slower than this, in m/s. Braking: either brake past this.
        private const float StandingSpeed = 0.3f;
        private const float BrakesOff = 0.1f;

        // How fast he moves each control, in its travel per second: the door
        // swings open in a third of a second, a lever takes a second or so
        // end to end, and a valve's wheel two.
        private const float DoorPace = 3f;
        private const float LeverPace = 1.2f;
        private const float ValvePace = 0.5f;

        // The road: read every this often, in s, while moving faster than
        // this, in m/s. For the fire he looks at what the train will be on
        // 45 to 90 s out (300 m to 2.5 km), for the water from there to five
        // minutes out (to 6 km), the hardest 200 m stretch counting; the
        // grade he is on is the next 150 m.
        private const float RoadEvery = 2f;
        private const float RoadFromSpeed = 2f;
        private const float FireLeadTime = 90f;
        private const float WaterLeadTime = 300f;
        private const float Stretch = 200f;
        private const float NowStretch = 150f;
        private const float RoadSpacing = 20f;

        // For the glass, the steepest 100 m the engine's front meets within the
        // water's lead: the engine tilts with the grade under it, not a long
        // stretch's average.
        private const float TiltStretch = 100f;

        // A skilled fireman's first reckoning of his engine's pull at full
        // work: its rated steam at about 0.3 MJ of pull a kg. He learns the
        // rest from steady running.
        private const float PullPerSteam = 0.3e6f;

        // He hears the work over a few seconds; running counts as steady when
        // speed and work hold within these over one reading of the road.
        private const float WorkHearTime = 3f;
        private const float SteadySpeed = 0.3f;
        private const float SteadyWork = 0.05f;

        // He cannot throw through a door the player has shut.
        private const float DoorOpenEnough = 0.3f;

        private static readonly AccessTools.FieldRef<PortReference, Port> ReferencedPort =
            AccessTools.FieldRefAccess<PortReference, Port>("port");
        private static readonly AccessTools.FieldRef<Boiler, float> SafetyValveOpeningPressure =
            AccessTools.FieldRefAccess<Boiler, float>("safetyValveOpeningPressure");

        private static readonly Dictionary<TrainCar, FiremanRig> ByCar = new Dictionary<TrainCar, FiremanRig>();

        private readonly SimController controller;
        private readonly Boiler boiler;
        private readonly List<ReciprocatingSteamEngine> engines;
        // The auxiliaries he hears: the blast pipe's blower, the air pumps, the dynamo.
        private readonly SteamExhaust exhaust;
        private readonly List<SteamCompressor> airPumps;
        private readonly List<Dynamo> dynamos;
        private readonly RealisticStoker stoker;
        private readonly ResourceContainer sand;
        private readonly FiremanHand door;
        private readonly FiremanHand injector;
        private readonly FiremanHand injectorSteam;
        private readonly FiremanHand pump;
        private readonly FiremanHand blower;
        private readonly FiremanHand damper;
        private readonly FiremanHand blowdown;
        private FiremanHand firingValve;
        private readonly FiremanHand atomizer;
        private readonly FiremanHand heater;
        private FiremanHand stokerThrottle;
        private FiremanHand stokerLever;
        private FiremanHand[] stokerJets;
        private float clock;
        private float nextTableHook;
        private float lastDoor;
        private RoadReckoner reckoner;
        private float lastTick;
        private float roadClock;
        private float heardWork;
        private float lastSpeed;
        private float lastWork;

        // What he did since his last line in the log while the player was
        // not aboard: to find why he stops firing with the player in
        // another loco (whydidimake, Discord, 2026-10-16).
        private const float AwayLogEvery = 30f;
        private float awayClock;
        private int awayThrows;
        private float awayCoal;
        private int awayRefused;
        private int awayWanted;

        private FiremanRig(TrainCar car, SimController controller, Boiler boiler, List<ReciprocatingSteamEngine> engines,
            SteamExhaust exhaust, RealisticFirebox coal, RealisticOilFirebox oil)
        {
            Car = car;
            Firebox = coal;
            OilFirebox = oil;
            CoalFireman = coal != null ? new AutoFireman() : null;
            OilFireman = coal == null && oil != null ? new AutoOilFireman() : null;
            this.controller = controller;
            this.boiler = boiler;
            this.engines = engines;
            this.exhaust = exhaust;
            var flow = controller.simFlow;
            airPumps = flow.OrderedSimComps.OfType<SteamCompressor>().ToList();
            dynamos = flow.OrderedSimComps.OfType<Dynamo>().ToList();
            var doorId = controller.firebox != null ? controller.firebox.fireboxDoorPortId : null;
            door = new FiremanHand("door", flow.TryGetPort(doorId, out var doorPort, canBeNullOrEmpty: true) ? Control(doorPort) : null, DoorPace);
            // The first cab injector valve: the only one on most locos, the
            // right-hand one on the JK3, whose two valves feed through a sum.
            var rig = InjectorRig.TryGet(boiler, out var found) ? found : null;
            injector = new FiremanHand("injector", rig != null && rig.Circuits.Count > 0
                ? rig.Circuits[0].Feed
                : Control(boiler.injectorControl), LeverPace);
            // Its own steam valve, where it has one: the feed hand then works its water valve.
            var steamValve = rig != null && rig.Circuits.Count > 0 ? rig.Circuits[0].SteamValve : null;
            injectorSteam = steamValve != null ? new FiremanHand("injector steam", Control(steamValve), DoorPace) : null;
            pump = rig != null && rig.Pump != null ? new FiremanHand("feed pump", Control(rig.PumpValvePort), ValvePace) : null;
            blowdown = new FiremanHand("blowdown", Control(boiler.blowdownControl), LeverPace);
            blower = new FiremanHand("blower", exhaust != null ? Control(exhaust.blowerControl) : null, ValvePace);
            damper = new FiremanHand("damper", exhaust != null ? Control(exhaust.damperControl) : null, LeverPace);
            if (OilFireman != null)
            {
                atomizer = new FiremanHand("atomizer", Control(oil.AtomizerPort), ValvePace);
                heater = new FiremanHand("oil heater", Control(oil.HeaterPort), ValvePace);
            }
            stoker = coal != null ? flow.OrderedSimComps.OfType<RealisticStoker>().FirstOrDefault() : null;
            // The loco's sand, which the oil fireman throws through the peep hole.
            sand = oil != null ? flow.OrderedSimComps.OfType<ResourceContainer>().FirstOrDefault(c => c.resourceType == ResourceContainerType.SAND) : null;
        }

        public TrainCar Car { get; }

        /// <summary>The coal firebox he fires, or null on an oil burner.</summary>
        public RealisticFirebox Firebox { get; }

        /// <summary>The oil burner he fires, or null on a coal engine.</summary>
        public RealisticOilFirebox OilFirebox { get; }

        public AutoFireman CoalFireman { get; }

        public AutoOilFireman OilFireman { get; }

        /// <summary>The fireman, coal or oil.</summary>
        public AutoFiremanBase Fireman => (AutoFiremanBase)CoalFireman ?? OilFireman;

        /// <summary>The road ahead as he last read it.</summary>
        public RoadView Road { get; private set; }

        /// <summary>The road ahead as the fireman reads it, for the log and his window.</summary>
        internal struct RoadView
        {
            public bool Known;
            public float GradeNow;
            public float GradeSoon;
            public float GradeAhead;
            public bool Forward;
            public float SteepestClimb;
            public float SteepestDescent;
            public float FireLead;
            public float WaterLead;
            public float Mass;
            public float WorkSoon;
            public float WorkAhead;
        }

        /// <summary>What he read and did on the last tick he worked.</summary>
        public FiremanReadings Readings { get; private set; }
        public FiremanActions Actions { get; private set; }

        /// <summary>The current water as the cab glass and F6 show it.</summary>
        public float GlassShown => GaugeGlass.Read(Car, boiler.waterLevelReadOut.id, boiler.waterLevelReadOut.Value);

        /// <summary>The fireman's holding target on that same visible glass scale.</summary>
        public float GlassTargetShown
        {
            get
            {
                // Feeding, mass, and tilt stay in the core's crown-to-outlet
                // coordinates. Convert the target back to boiler height before
                // applying the cab's indicator calibration, just as for F6.
                var crown = BoilerWaterRig.TryGet(boiler, out var water) ? water.CrownSheetLevelNormalized : 0.6f;
                var outlet = boiler.steamOutletNormalizedWaterLevel > crown ? boiler.steamOutletNormalizedWaterLevel : 1f;
                var level = crown + Fireman.GlassTarget * (outlet - crown);
                return GaugeGlass.Read(Car, boiler.waterLevelReadOut.id, level);
            }
        }

        private IEnumerable<FiremanHand> AllHands =>
            new[] { door, injector, injectorSteam, pump, blower, damper, blowdown, firingValve, atomizer, heater, stokerThrottle, stokerLever }
                .Concat(stokerJets ?? new FiremanHand[0])
                .Where(h => h != null);

        /// <summary>The controls he works, and whether the player has one of them.</summary>
        public IEnumerable<(string Name, bool Fitted, float YieldLeft)> Hands =>
            AllHands.Select(h => (h.Name, h.Port != null, Mathf.Max(0f, h.YieldUntil - clock)));

        /// <summary>
        /// Gives a coal-fired loco its fireman. Returns a line for the log,
        /// or null when the loco has no boiler.
        /// </summary>
        public static string Attach(TrainCar car, SimController controller, RealisticFirebox firebox)
        {
            var rig = Create(car, controller, firebox, null);
            if (rig == null)
                return null;
            firebox.Fireman = rig;
            if (firebox.SavedFiremanMode != FiremanMode.Off)
                rig.SetMode(firebox.SavedFiremanMode);
            return rig.Ready();
        }

        /// <summary>Gives an oil burner its fireman. Returns a line for the log, or null when the loco has no boiler.</summary>
        public static string AttachOil(TrainCar car, SimController controller, RealisticOilFirebox oil)
        {
            var rig = Create(car, controller, null, oil);
            if (rig == null)
                return null;
            oil.Fireman = rig;
            if (oil.SavedFiremanMode != FiremanMode.Off)
                rig.SetMode(oil.SavedFiremanMode);
            return rig.Ready();
        }

        private static FiremanRig Create(TrainCar car, SimController controller, RealisticFirebox coal, RealisticOilFirebox oil)
        {
            var flow = controller.simFlow;
            var boiler = flow.OrderedSimComps.OfType<Boiler>().FirstOrDefault();
            if (boiler == null)
                return null;
            var engines = flow.OrderedSimComps.OfType<ReciprocatingSteamEngine>().ToList();
            var exhaust = flow.OrderedSimComps.OfType<SteamExhaust>().FirstOrDefault();
            var rig = new FiremanRig(car, controller, boiler, engines, exhaust, coal, oil);
            PruneDestroyedCars();
            ByCar[car] = rig;
            return rig;
        }

        private string Ready()
        {
            var missing = Hands.Where(h => !h.Fitted).Select(h => h.Name).ToList();
            return (OilFireman != null ? "oil fireman ready" : stoker != null ? "fireman ready, with the stoker" : "fireman ready") +
                   (missing.Count > 0 ? $" (no hand on the {string.Join(", ", missing)})" : "");
        }

        /// <summary>Other fireman mods that are running, which would fight this one; null when none.</summary>
        public static string Rivals { get; private set; }

        /// <summary>Notes other fireman mods (FiremanAssist, DV_Fireman) at load, and warns.</summary>
        public static void NoteRivals()
        {
            var rivals = UnityModManagerNet.UnityModManager.modEntries
                .Where(m => m.Active && m.Info.Id != Main.Mod.Info.Id && m.Info.Id.IndexOf("fireman", System.StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(m => m.Info.DisplayName ?? m.Info.Id)
                .ToList();
            Rivals = rivals.Count > 0 ? string.Join(", ", rivals) : null;
            if (Rivals != null)
                Main.Mod.Logger.Warning($"{Rivals} is running too: its fireman and this one will fight over the fire; use one");
        }

        public static bool TryGet(TrainCar car, out FiremanRig rig)
        {
            rig = null;
            return car != null && ByCar.TryGetValue(car, out rig);
        }

        /// <summary>
        /// The fireman the player is with on <paramref name="car"/>: its own,
        /// or a coupled loco's, the one the player was last in between two.
        /// </summary>
        public static bool TryGetTended(TrainCar car, out FiremanRig rig) =>
            TryGet(CarCouplings.Tended(car, ByCar), out rig);

        /// <summary>Sets his mode, giving every control back to him.</summary>
        public void SetMode(FiremanMode mode)
        {
            Fireman.Mode = mode;
            foreach (var hand in AllHands)
                hand.Release();
            Main.Mod.Logger.Log($"{Car.ID}: fireman {mode}");
        }

        /// <summary>Runs on the host's sim tick, after the fire's.</summary>
        public void Step(float dt)
        {
            clock += dt;
            lastTick = dt;
            if (Fireman.Mode == FiremanMode.Off || !(dt > 0f))
                return;
            var readings = Read();
            var actions = CoalFireman != null
                ? CoalFireman.Step(dt, Firebox.Model, readings)
                : OilFireman.Step(dt, OilFirebox.Model, readings);
            Readings = readings;
            Actions = actions;

            if (actions.Shovelful > 0f)
                awayWanted++;
            if (CoalFireman != null)
            {
                // The scoop from the tender as he opens the door for a throw.
                if (actions.Door >= 1f && lastDoor < 1f && CoalFireman.RoundLeft > 0)
                    FiremanSounds.Scoop(Car, Firebox);
                lastDoor = actions.Door;
                if (actions.Shovelful > 0f)
                    Throw(actions.Shovelful, actions.Aim);
                if (actions.Shake.HasValue)
                    Firebox.ShakeGrate(actions.Shake.Value);
                if (readings.HasStoker)
                    WorkStoker(actions);
            }
            else
            {
                // The firing valve is the oil feed's control, which joins the
                // firebox as the sim starts.
                if (firingValve == null && OilFirebox.Feed != null)
                    firingValve = new FiremanHand("firing valve", Control(OilFirebox.Feed.ControlPort), ValvePace);
                // Never under the valve's stop; to shut it he takes the stop out.
                firingValve?.Work(OilFirebox.Stop.ForFireman(actions.FiringValve), clock);
                atomizer?.Work(actions.Atomizer, clock);
                heater?.Work(actions.Heater, clock);
                if (actions.Sand > 0f)
                    ThrowSand(actions.Sand);
            }

            door.Work(actions.Door, clock);
            // His injector's tender water valve, open before he works it.
            if (actions.Injector > 0f && InjectorRig.TryGet(boiler, out var rig))
                rig.OpenTenderValve(0);
            injector.Work(actions.Injector, clock);
            injectorSteam?.Work(actions.InjectorSteam, clock);
            pump?.Work(actions.Pump, clock);
            blower.Work(actions.Blower, clock);
            damper.Work(actions.Damper, clock);
            blowdown.Work(actions.Blowdown, clock);
            LogAway(dt, readings);
        }

        // Every half minute with the player not aboard, a line on what he
        // did and what held him back.
        private void LogAway(float dt, FiremanReadings readings)
        {
            if (PlayerManager.Car == Car || Main.Settings.FireStatusLogInterval <= 0f)
            {
                awayClock = 0f;
                awayThrows = awayRefused = awayWanted = 0;
                awayCoal = 0f;
                return;
            }
            if ((awayClock += dt) < AwayLogEvery)
                return;
            var yielded = string.Join(", ", Hands.Where(h => h.YieldLeft > 0f).Select(h => $"{h.Name} {h.YieldLeft:F0} s"));
            var player = PlayerManager.Car != null ? PlayerManager.Car.ID : "on foot";
            Main.Mod.Logger.Log($"{Car.ID} fireman ({Fireman.Mode}) with the player not aboard ({player}): " +
                                $"{awayWanted} throws wanted, {awayThrows} made ({awayCoal:F1} kg), {awayRefused} refused at the door; " +
                                $"door {(door.Port != null ? door.Port.Value : -1f):F2}, yielded to the player: {(yielded.Length > 0 ? yielded : "none")}; " +
                                $"pressure {readings.Pressure:F1} bar, glass {readings.Glass:P0}, work {readings.Work:P0}, tender coal {readings.TenderCoal:F0} kg" +
                                (Firebox != null ? $", fire {Firebox.Model.CoalMass:F0} kg" : "") +
                                (readings.HasStoker && Firebox != null
                                    ? $", stoker engine {Firebox.StokerEngineSpeed:P0} {(stoker != null ? stoker.Direction.ToString().ToLowerInvariant() : "")} feeding {(Firebox.LastStoker is StokerFlow flow ? flow.Fed / Mathf.Max(1e-4f, lastTick) : 0f):F2} kg/s" +
                                      (stoker != null ? $", {stoker.CoalReached:F0} kg of coal at it from {stoker.CoalFrom}" : "")
                                    : "") +
                                $"; cab {(Car.loadedInterior != null ? "loaded" : "not loaded")}");
            awayClock = 0f;
            awayThrows = awayRefused = awayWanted = 0;
            awayCoal = 0f;
        }

        // The stoker's engine throttle, operating lever, and jets.
        private void WorkStoker(FiremanActions actions)
        {
            if (actions.ClearStokerTable && clock >= nextTableHook && Firebox != null
                && Firebox.ClearStokerTable() > 0f)
                nextTableHook = clock + 4f;
            if (stokerThrottle == null)
            {
                var flow = controller.simFlow;
                stokerThrottle = new FiremanHand("stoker engine", Control(stoker.ThrottlePort), ValvePace);
                stokerLever = new FiremanHand("stoker lever",
                    flow.TryGetPort(StokerJetControls.Reverse.PortId, out var lever, canBeNullOrEmpty: true) ? Control(lever) : null, DoorPace);
                stokerJets = StokerJetControls.Valves
                    .Select(v => new FiremanHand(v.Label.ToLowerInvariant(),
                        flow.TryGetPort(v.PortId, out var port, canBeNullOrEmpty: true) ? Control(port) : null, ValvePace))
                    .ToArray();
            }
            stokerThrottle.Work(actions.StokerThrottle, clock);
            stokerLever.Work(actions.StokerDirection == StokerDirection.Forward ? 1f
                : actions.StokerDirection == StokerDirection.Reverse ? 0f : 0.5f, clock);
            for (var i = 0; i < stokerJets.Length; i++)
            {
                var jet = StokerJetControls.Valves[i].Jet;
                var want = jet == null ? actions.MasterJet
                    : jet == StokerJet.LeftBack || jet == StokerJet.RightBack ? actions.BackJets
                    : jet == StokerJet.LeftFront || jet == StokerJet.RightFront ? actions.FrontJets
                    : actions.FinesJet;
                stokerJets[i].Work(want, clock);
            }
        }

        private FiremanReadings Read()
        {
            BoilerWaterRig.TryGet(Car, out var water);
            InjectorRig.TryGet(boiler, out var rig);
            var pressure = water != null ? water.BoilerPressure : rig != null ? rig.BoilerPressure : 1f;
            var crown = water != null ? water.CrownSheetLevelNormalized : 0.6f;
            var outlet = boiler.steamOutletNormalizedWaterLevel > crown ? boiler.steamOutletNormalizedWaterLevel : 1f;
            var level = water != null ? water.GlassLevel : boiler.waterLevelReadOut.Value;

            // What he can perceive of the engine: the exhaust's beats against
            // its rated steam (he knows his engine), and the engineer's
            // throttle and reverser. Never the steam it draws.
            var exhaust = 0f;
            var rated = 0f;
            var throttle = 0f;
            var reverser = 0f;
            foreach (var engine in engines)
            {
                exhaust += engine.exhaustFlowReadOut.Value;
                rated += engine.maxFlowReadOut.Value;
                throttle = Mathf.Max(throttle, engine.throttleControl.Value);
                reverser = Mathf.Max(reverser, Mathf.Abs(engine.reverserControl.Value));
            }
            var work = rated > 0f ? Mathf.Max(0f, exhaust) / rated : 0f;
            var road = ReadRoad(work, rated, Car.GetForwardSpeed());
            var (tilt, tiltHigh, tiltLow) = GlassTilts(boiler, outlet - crown);

            var brakes = Car.brakeSystem;
            var braked = brakes != null && (brakes.trainBrakePosition > BrakesOff || brakes.independentBrakePosition > BrakesOff);
            var standing = Mathf.Abs(Car.GetForwardSpeed()) < StandingSpeed;

            var jet = rig != null && rig.Circuits.Count > 0 ? rig.Circuits[0].Injector : null;
            var injectorState = jet != null ? jet.State
                : injector.Port != null && injector.Port.Value > InjectorSpec.OpenThreshold ? InjectorState.Working : InjectorState.Off;
            var stokerModel = stoker != null && stoker.ThrottlePort != null ? stoker.Model : null;
            return new FiremanReadings
            {
                Pressure = pressure,
                SafetyValve = SafetyValveOpeningPressure(boiler),
                BurstPressure = SafetyValveFit.BurstPressure(boiler),
                Work = work,
                FullWorkSteam = rated,
                RoadKnown = road.Known,
                WorkSoon = road.WorkSoon,
                WorkAhead = road.WorkAhead,
                GlassMass = water != null ? water.GlassMass : 0f,
                Throttle = throttle,
                Cutoff = reverser,
                Braking = braked,
                Standing = standing,
                StartupMultiplier = Firebox != null ? Firebox.StartupMultiplier : 1f,
                AuxiliarySteam = AuxiliarySteam(),
                Glass = (level - crown) / (outlet - crown),
                GlassTilt = tilt,
                GlassTiltHigh = tiltHigh,
                GlassTiltLow = tiltLow,
                TenderWater = rig != null ? rig.TenderWater : boiler.water.Value,
                TenderCoal = OilFirebox != null ? OilFirebox.Feed != null ? OilFirebox.Feed.TenderFuel : 0f : TenderCoal(),
                Injector = injectorState,
                InjectorLever = injector.Port != null ? injector.Port.Value : 0f,
                InjectorSteamValve = injectorSteam != null && injectorSteam.Port != null,
                InjectorSteam = injectorSteam != null && injectorSteam.Port != null ? injectorSteam.Port.Value : 0f,
                InjectorCrack = jet != null ? jet.Spec.CrackOpening : 0.2f,
                InjectorStartPressure = jet != null ? jet.Spec.StartPressure : InjectorSpec.DefaultStartPressure,
                InjectorFullFeed = jet != null ? jet.FullDelivery(pressure) : 4f,
                InjectorLeast = jet != null ? jet.Spec.LeastShare(pressure) : 0f,
                HasPump = rig != null && rig.Pump != null && pump?.Port != null,
                Pump = rig != null ? rig.PumpFlow.State : FeedPumpState.Off,
                PumpFullFeed = rig != null && rig.Pump != null ? rig.Pump.Spec.Capacity : 0f,
                HasStoker = stokerModel != null,
                StokerJammed = stokerModel != null && stokerModel.Jammed,
                StokerTableLoaded = Firebox != null && Firebox.StokerTableLoaded,
                StokerEnginePressure = Firebox?.LastStoker?.EnginePressure ?? 0f,
                StokerEngineSpeed = Firebox?.StokerEngineSpeed ?? 0f,
                StokerThrottle = stoker?.ThrottlePort?.Value ?? 0f,
                StokerWorkingPressure = stokerModel?.Spec.EngineWorkingPressure ?? 0f,
                StokerRunningForward = stoker != null && stoker.Direction == StokerDirection.Forward,
                StokerFullFeed = stokerModel != null ? stokerModel.Spec.MaxFeedRate : 0f,
                Sand = OilFirebox != null ? SandAvailable() : 0f,
                Solids = water != null ? water.Water.Concentration : 0f,
                ShovelMass = ShovelfulMass(),
            };
        }

        // The steam the auxiliaries take from the boiler now, kg/s: the air
        // pumps (he hears them pound), the dynamo, and the blower as he has it
        // open, only what the boiler gives it (not a shed's blower line).
        private float AuxiliarySteam()
        {
            var steam = 0f;
            foreach (var pump in airPumps)
                steam += Mathf.Max(0f, pump.steamConsumptionReadOut.Value);
            foreach (var dynamo in dynamos)
                steam += Mathf.Max(0f, dynamo.steamConsumptionReadOut.Value);
            if (exhaust != null)
            {
                if (BlowerFit.TryGet(exhaust, out var draft))
                {
                    BlowerFit.NozzleSteam(exhaust, draft, out var fromBoiler);
                    steam += fromBoiler;
                }
                else
                    steam += BlowerFit.BlowerSteam(exhaust);
            }
            return steam;
        }

        // The glass's shift from the engine's tilt, in glass shares: now, from
        // the boiler's own tilt input (the game's shift exactly), and the most
        // and least on the road ahead, its climbs and descents turned to the
        // engine's front up or down by the way it is going.
        private (float Now, float High, float Low) GlassTilts(Boiler boiler, float span)
        {
            var perGrade = BoilerWaterRig.GlassPerGrade(boiler, span);
            var now = BoilerWaterRig.GlassTiltNow(boiler, span);
            if (!Road.Known)
                return (now, 0f, 0f);
            var climb = float.IsNaN(Road.SteepestClimb) ? 0f : Road.SteepestClimb;
            var descent = float.IsNaN(Road.SteepestDescent) ? 0f : Road.SteepestDescent;
            return Road.Forward
                ? (now, perGrade * climb, perGrade * descent)
                : (now, -perGrade * descent, -perGrade * climb);
        }

        // The road ahead: every few seconds while moving, its grades, turned
        // into the work they will take by what he has learned of his engine.
        private RoadView ReadRoad(float work, float rated, float speed)
        {
            var dt = lastTick;
            heardWork += (work - heardWork) * Mathf.Min(1f, dt / WorkHearTime);
            roadClock += dt;
            if (roadClock < RoadEvery)
                return Road;
            var step = roadClock;
            roadClock = 0f;
            var v = Mathf.Abs(speed);
            if (v < RoadFromSpeed)
                return Road = default(RoadView);

            var fireLead = Mathf.Clamp(v * FireLeadTime, 300f, 2500f);
            var waterLead = Mathf.Clamp(v * WaterLeadTime, 1000f, 6000f);
            var road = RoadScout.Ahead(Car, speed > 0f, waterLead + Stretch, RoadSpacing);
            var mass = RoadScout.TrainMass(Car);
            var now = RoadAhead.Grade(road, 0f, NowStretch);
            var soon = RoadAhead.Steepest(road, 0.5f * fireLead, fireLead, Stretch);
            var ahead = RoadAhead.Steepest(road, fireLead, waterLead, Stretch);

            if (reckoner == null)
                reckoner = new RoadReckoner(Mathf.Max(1f, rated) * PullPerSteam);
            var steady = Mathf.Abs(v - lastSpeed) < SteadySpeed && Mathf.Abs(heardWork - lastWork) < SteadyWork;
            reckoner.Learn(step, heardWork, mass, v, now, steady);
            lastSpeed = v;
            lastWork = heardWork;

            var known = !float.IsNaN(now) && !float.IsNaN(soon);
            var workSoon = known ? reckoner.WorkAt(heardWork, mass, v, now, soon) : 0f;
            return Road = new RoadView
            {
                Known = known,
                GradeNow = now,
                GradeSoon = soon,
                GradeAhead = ahead,
                Forward = speed > 0f,
                SteepestClimb = RoadAhead.Steepest(road, 0f, waterLead, TiltStretch),
                SteepestDescent = RoadAhead.SteepestDescent(road, 0f, waterLead, TiltStretch),
                FireLead = fireLead,
                WaterLead = waterLead,
                Mass = mass,
                WorkSoon = workSoon,
                WorkAhead = known && !float.IsNaN(ahead) ? reckoner.WorkAt(heardWork, mass, v, now, ahead) : workSoon,
            };
        }

        // His shovelful, as the player's own (always a real one; the stock
        // shovel's 36 kg on the S282 at the setting's 0 is gone).
        private float ShovelfulMass() => Main.Settings.Shovelful;

        // Tender coal as it goes on the fire: the pile's mass over the
        // firebox's consumption multiplier, as the game's shovel moves it.
        private float TenderCoal()
        {
            var pile = Pile();
            return pile != null ? pile.CoalAvailable() / Multiplier : 0f;
        }

        private float Multiplier =>
            controller.firebox != null && controller.firebox.coalConsumptionMultiplier > 0f ? controller.firebox.coalConsumptionMultiplier : 1f;

        // The loco's own coal pile, or its tender's, as the game's keyboard shoveling finds it.
        private CoalPileSimController Pile()
        {
            var pile = controller.coalPile;
            if (pile == null && Car.rearCoupler != null && Car.rearCoupler.coupledTo != null)
                pile = Car.rearCoupler.coupledTo.train.SimController?.coalPile;
            return pile;
        }

        private void Throw(float mass, GrateZone aim)
        {
            var pile = Pile();
            if (pile == null || door.Port != null && door.Yielded(clock) && door.Port.Value < DoorOpenEnough)
            {
                awayRefused++;
                return;
            }
            var taken = pile.TryRemoveCoal(mass * Multiplier) / Multiplier;
            if (taken <= 0f)
                return;
            awayThrows++;
            awayCoal += taken;
            Firebox.DeliverShovelful(taken, aim);
            FiremanSounds.Throw(Car, Firebox);
        }

        private float SandAvailable() => sand != null ? Mathf.Max(0f, sand.amountReadOut.Value) : 0f;

        private void ThrowSand(float kg)
        {
            var taken = sand != null ? Mathf.Min(kg, Mathf.Max(0f, sand.amountReadOut.Value)) : 0f;
            if (taken <= 0f)
                return;
            sand.consumeExtIn.Value = taken;
            OilFirebox.ThrowSand(taken);
        }

        // A cab control's port, if it is one he can work.
        private static Port Control(PortReference reference)
        {
            var port = reference != null && reference.IsConnected ? ReferencedPort(reference) : null;
            return Control(port);
        }

        private static Port Control(Port port) => port != null && port.type == PortType.EXTERNAL_IN ? port : null;

        private static void PruneDestroyedCars()
        {
            foreach (var car in ByCar.Keys.Where(car => car == null).ToList())
                ByCar.Remove(car);
        }
    }
}
