using System.Collections.Generic;
using System.Linq;
using DV.Damage;
using DV.JObjectExtstensions;
using HarmonyLib;
using LocoSim.Implementations;
using Newtonsoft.Json.Linq;
using RealisticFiring.Core;
using UnityEngine;

namespace RealisticFiring.Sim
{
 internal sealed class BoilerWaterRig
    {
        public enum Guard
        {
            None,
            FusiblePlug,
            LowWaterAlarm,
        }

        /// <summary>What players near the loco are told when its plug melts.</summary>
        public const string PlugMeltedNotice = "The fusible plug has melted: steam is blowing into the firebox and has put the fire out. " +
                                               "Have the engine repaired at a service station, or wash out the boiler with a shed stand's water hose (boiler cold), to renew it.";

        /// <summary>What players near the loco are told when its plug melts, on an engine with no shed water connection.</summary>
        public const string PlugMeltedRepairOnlyNotice = "The fusible plug has melted: steam is blowing into the firebox and has put the fire out. " +
                                                         "Have the engine repaired at a service station to renew it.";

        /// <summary>The melted-plug notice for <paramref name="car"/>: the hose washout only where it has a shed water connection.</summary>
        public static string PlugMeltedNoticeFor(TrainCar car) =>
            car != null && car.carLivery != null && !Shed.ShedConnections.HasWaterConnection(car.carLivery.id) ? PlugMeltedRepairOnlyNotice : PlugMeltedNotice;

        private const string SolidsKey = "rfSolids";
        private const string SettledKey = "rfSludgeSettled";
        private const string SuspendedKey = "rfSludgeSuspended";
        private const string PlugMeltedKey = "rfPlugMelted";
        private const string WaterTempKey = "rfWaterTemp";
        private const string ScaleKey = "rfScale";
        private const string SootKey = "rfSoot";
        private const string FoulingWearKey = "rfFoulingWear";
        private const string ThermalShockWearKey = "rfThermalShockWear";
        private const string ThermalShockEngineWearKey = "rfThermalShockEngineWear";
        private const string SuperheaterEngineWearKey = "rfSuperheaterEngineWear";
        private const string GlassChokeKey = "rfGlassChoke";
        private const string GlassColumnKey = "rfGlassColumn";
        private const string GlassBlowAgeKey = "rfGlassBlowAge";

        // The heating surface for each m³ of boiler (inferred: the USRA light
        // Mikado's some 280 m² in a boiler of the S282's 17 m³).
        private const float HeatingSurfacePerVolume = 16f;

        // Fouling stands as this much engine wear for each share of heat it
        // keeps from the water; wear is charged or given back in steps of at
        // least this share of the engine's health.
        private const float WearPerLoss = 2f;
        private const float LeastWear = 1e-4f;

        // A cold boiler's water is the air's (Ambient.BoilerWater): the
        // game's 25 °C, toward which the stock boiler cools, or DV Seasons'.
        internal static float AmbientWater => Ambient.BoilerWater;

        // A boiler at no more than this pressure, bar absolute, is at the
        // atmosphere's: cold, or not yet boiling. Below this temperature, °C,
        // its water is saved.
        private const float AtmosphericPressure = 1.01f;
        private const float BelowBoiling = 99f;

        // A melted plug's jet holds the crown sheet this far below failing.
        private const float PlugHoldsCrownBelow = 0.9f;

        private const float RepairedHealth = 0.95f;
        private const float RepairRise = 1e-4f;
        private const int RepairWatchFrom = 50;

        private static readonly System.Reflection.FieldInfo SpawnPressure = AccessTools.Field(typeof(Boiler), "spawnPressure");
        private static readonly System.Reflection.FieldInfo SpawnWaterMass = AccessTools.Field(typeof(Boiler), "spawnWaterMass");

        private static readonly AccessTools.FieldRef<Boiler, WaterPressureVessel> Vessel =
            AccessTools.FieldRefAccess<Boiler, WaterPressureVessel>("vessel");
        private static readonly AccessTools.FieldRef<Boiler, float> TotalVolume =
            AccessTools.FieldRefAccess<Boiler, float>("totalVolume");
        private static readonly AccessTools.FieldRef<Boiler, float> ExteriorConductance =
            AccessTools.FieldRefAccess<Boiler, float>("exteriorThermalConductance");
        private static readonly AccessTools.FieldRef<Boiler, float> MaxBlowdownRate =
            AccessTools.FieldRefAccess<Boiler, float>("maxBlowdownRate");
        private static readonly AccessTools.FieldRef<Boiler, float> CrownSheetLevel =
            AccessTools.FieldRefAccess<Boiler, float>("crownSheetNormalizedWaterLevel");
        private static readonly AccessTools.FieldRef<Boiler, float> CrownSheetSmoothTime =
            AccessTools.FieldRefAccess<Boiler, float>("crownSheetTempSmoothTime");
        private static readonly AccessTools.FieldRef<Boiler, float> CrownSheetOverheat =
            AccessTools.FieldRefAccess<Boiler, float>("crownSheetOverheatTemp");
        private static readonly AccessTools.FieldRef<Boiler, float> CrownSheetTemperature =
            AccessTools.FieldRefAccess<Boiler, float>("crownSheetTemperature");
        private static readonly AccessTools.FieldRef<Boiler, float> MinimumExplosionPressure =
            AccessTools.FieldRefAccess<Boiler, float>("minimumExplosionPressure");
        private static readonly AccessTools.FieldRef<SimComponent, SimGameParams> GameParams =
            AccessTools.FieldRefAccess<SimComponent, SimGameParams>("gameParams");
        private static readonly AccessTools.FieldRef<DamageController, GameParams> DamageGameParams =
            AccessTools.FieldRefAccess<DamageController, GameParams>("gameParams");

        private static readonly Dictionary<Boiler, BoilerWaterRig> ByBoiler = new Dictionary<Boiler, BoilerWaterRig>();
        private static readonly Dictionary<TrainCar, BoilerWaterRig> ByCar = new Dictionary<TrainCar, BoilerWaterRig>();
        // Held weakly: a loco left stock never takes its save up, and its
        // boiler stayed here for the session (audit 2026-11-09).
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Boiler, JObject> PendingSaves =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Boiler, JObject>();

        private readonly Boiler boiler;
        private readonly ReciprocatingSteamEngine engine;
        private WaterPressureVessel knownVessel;
        private float massAtTick;
        private float fed;
        private float blown;
        private float pendingVent;
        private float pendingTubeLeak;
        private float pendingCockWater;
        private float pendingCockSteam;
        private bool wasOn;
        private DamageController damage;
        private float bodyHealth = float.NaN;
        private float engineHealth = float.NaN;
        private int ticks;
        // The engine's health charged now for fouling, thermal shock, and
        // superheater wear, and the drifting wear not yet charged.
        private float foulingWear;
        private float thermalShockWear;
        private float superheaterWear;
        private float driftWear;
        private bool? mirroredThermalShockActive;
        private bool hasMirroredImpurities;
        private bool mirroredImpuritiesOn;
        private float mirroredFoam;
        private float mirroredFoamingPpm;

        private BoilerWaterRig(TrainCar car, Boiler boiler, ReciprocatingSteamEngine engine, Guard guard)
        {
            Car = car;
            this.boiler = boiler;
            this.engine = engine;
            var vessel = Vessel(boiler);
            Water = new BoilerWater(Main.Settings.BoilerWaterQuality, vessel != null ? vessel.mass : 0f);
            if (guard == Guard.FusiblePlug)
                Plug = new FusiblePlug(vessel != null ? vessel.waterTemp : 20f);
            else if (guard == Guard.LowWaterAlarm)
                Alarm = new LowWaterAlarm();
            knownVessel = vessel;
            TrueLevel = boiler.waterLevelReadOut.Value;
            TrueSurfaceLevel = TrueLevel;
            wasOn = Main.Settings.BoilerWaterImpurities;
            var carried = SpawnWaterMass != null ? (float)SpawnWaterMass.GetValue(boiler) : vessel != null ? vessel.mass : 0f;
            Steel = new BoilerMetal(carried, vessel != null ? vessel.waterTemp : AmbientWater);
        }

        /// <summary>The boiler's steel, holding heat beside the water.</summary>
        public BoilerMetal Steel { get; }

        /// <summary>The steam in the water, which stands the glass above the water alone.</summary>
        public BoilerSwell Swell { get; } = new BoilerSwell();

        /// <summary>Soot in the tubes and scale on the water side.</summary>
        public BoilerFouling Fouling { get; } = new BoilerFouling();

        /// <summary>Thermal stress and tube/stay leak wear from forcing a cold boiler too fast.</summary>
        public BoilerThermalShock ThermalShock { get; } = new BoilerThermalShock();

        /// <summary>Water/steam currently weeping from strained tubes/stays, in kg/s.</summary>
        public float TubeLeakRate { get; internal set; }

        /// <summary>
        /// Whether boiler thermal-shock care is active for this boiler: the
        /// host's mirrored setting on a multiplayer guest, otherwise local settings.
        /// </summary>
        public bool ThermalShockActive =>
            mirroredThermalShockActive ?? (Main.Settings == null || Main.Settings.BoilerThermalShock);

        /// <summary>Share of fire heat lost through strained tubes/stays (0 when thermal shock care is off).</summary>
        public float ThermalShockEfficiencyLoss =>
            ThermalShockActive ? ThermalShock.EfficiencyLoss : 0f;

        /// <summary>Applies host-authoritative thermal-shock state on a multiplayer guest.</summary>
        internal void MirrorThermalShock(float wear, float stress, bool warmingTooFast, float tubeLeakRate, bool active)
        {
            mirroredThermalShockActive = active;
            ThermalShock.Mirror(wear, stress, warmingTooFast);
            ThermalShock.SetEnabled(active);
            TubeLeakRate = active ? Mathf.Max(0f, tubeLeakRate) : 0f;
        }

        /// <summary>Lower-passage choking and blow-through drawdown on the cab gauge glass.</summary>
        public GaugeGlassChoke GlassChoke { get; } = new GaugeGlassChoke();

        /// <summary>Whether this locomotive has glass blow-through cocks and try cocks.</summary>
        public bool HasGlassCocks => TryCocks != null;

        /// <summary>The boiler's heating surface, m².</summary>
        public float HeatingSurface => TotalVolume(boiler) / 1000f * HeatingSurfacePerVolume;

        /// <summary>The share of the fire's heat soot and scale keep from the water.</summary>
        public float FoulingLoss => Fouling.Loss(HeatingSurface);

        /// <summary>Whether the engine drifts with the throttle shut and the reverser hooked up, drawing smokebox gas into its cylinders.</summary>
        public bool DriftingHookedUp => engine != null && Car != null
                                        && Drifting.HookedUp(engine.throttleControl.Value, engine.reverserControl.Value, Car.GetForwardSpeed());

        /// <summary>The engine's drifting now: its chests and fittings (<see cref="EngineDrift"/>).</summary>
        public DriftState Drift => engine != null ? EngineDrift.Read(Car, engine) : DriftState.NotDrifting;

        /// <summary>Whether drifting hooked up wears the engine now, after its fittings and drifting steam.</summary>
        public bool DriftingWearing => engine != null && Car != null
                                       && Drifting.Wear(engine.throttleControl.Value, engine.reverserControl.Value, Car.GetForwardSpeed(), Drift) > 0f;

        public TrainCar Car { get; }

        public BoilerWater Water { get; }

        /// <summary>The crown sheet's fusible plug, on a loco whose prototype had one; else null.</summary>
        public FusiblePlug Plug { get; }

        /// <summary>The low-water alarm, on a loco whose prototype had one; else null.</summary>
        public LowWaterAlarm Alarm { get; }

        /// <summary>The water level over the crown sheet as it is, tilt included, 0 to 1.</summary>
        public float TrueLevel { get; private set; }

        /// <summary>Whether <see cref="TrueSurfaceLevel"/> has been computed on the host or mirrored to a guest.</summary>
        public bool HasTrueSurface { get; private set; }

        /// <summary>True boiler water surface at the backhead (true level + swell + foam), unaffected by glass choking.</summary>
        public float TrueSurfaceLevel { get; private set; }

        /// <summary>The level the glass column stands at, foam, swell, and choking included (without transient blow-through drawdown).</summary>
        public float GlassLevel => boiler.waterLevelReadOut.Value;

        /// <summary>Whether boiler water impurities are active on this boiler (the host's setting on a multiplayer guest).</summary>
        public bool ImpuritiesActive =>
            hasMirroredImpurities ? mirroredImpuritiesOn
            : Main.Settings != null && Main.Settings.BoilerWaterImpurities;

        /// <summary>Current boiler water foam (0 to 1), on host or mirrored on a multiplayer guest; 0 when impurities are off.</summary>
        public float FoamNow => !ImpuritiesActive ? 0f : hasMirroredImpurities ? mirroredFoam : Water.Foam;

        /// <summary>Current foaming concentration (ppm), on host or mirrored on a multiplayer guest; 0 when impurities are off.</summary>
        public float FoamingConcentrationNow => !ImpuritiesActive ? 0f : hasMirroredImpurities ? mirroredFoamingPpm : Water.FoamingConcentration;

        /// <summary>Applies the host's mirrored boiler impurities state, surface, foam, and gauge glass choke state on a multiplayer guest.</summary>
        internal void MirrorGlass(bool impuritiesActive, bool hasSurface, float trueSurface, float foam, float foamingPpm, float choke, float column, float blowAge, bool blowing)
        {
            hasMirroredImpurities = true;
            mirroredImpuritiesOn = impuritiesActive;
            HasTrueSurface = hasSurface;
            if (hasSurface)
                TrueSurfaceLevel = trueSurface;
            mirroredFoam = impuritiesActive ? foam : 0f;
            mirroredFoamingPpm = impuritiesActive ? foamingPpm : 0f;
            GlassChoke.Mirror(impuritiesActive ? choke : 0f, column, impuritiesActive ? blowAge : 0f, blowing);
        }

        /// <summary>The boiler readout used by the cab's calibrated gauge glass.</summary>
        public string WaterLevelPortId => boiler.waterLevelReadOut.id;

        // The game's boiler shifts its glass by its length over its diameter,
        // halved, times the tangent of its tilt (Boiler.SimulateWaterLevel).
        private static readonly AccessTools.FieldRef<Boiler, float> AspectRatio =
            AccessTools.FieldRefAccess<Boiler, float>("aspectRatio");

        /// <summary>
        /// How far the glass of <paramref name="boiler"/> moves for a grade of
        /// 1 (rise over run) under the engine's front, in shares of a glass
        /// spanning <paramref name="span"/> of the boiler's height.
        /// </summary>
        public static float GlassPerGrade(Boiler boiler, float span) =>
            boiler != null && span > 1e-3f ? 0.5f * AspectRatio(boiler) / span : 0f;

        /// <summary>
        /// How much higher the glass of <paramref name="boiler"/> reads than
        /// the water stands level, from its tilt now, in shares of a glass
        /// spanning <paramref name="span"/>: the game's own shift.
        /// </summary>
        public static float GlassTiltNow(Boiler boiler, float span) =>
            boiler != null ? GlassPerGrade(boiler, span) * (float)System.Math.Tan(-boiler.angleExtIn.Value * System.Math.PI / 180.0) : 0f;

        public float CrownSheetLevelNormalized => CrownSheetLevel(boiler);

        /// <summary>What blows through a melted plug into the firebox, in kg/s.</summary>
        public float PlugVent { get; internal set; }

        public float BoilerPressure => Vessel(boiler)?.pressure ?? 1f;

        /// <summary>Where the safety valve lifts, absolute bar.</summary>
        public float SafetyValve => SafetyValveFit.Lift(boiler);

        /// <summary>Where the boiler would burst now, absolute bar; infinite when it cannot.</summary>
        public float BurstPressure => SafetyValveFit.BurstPressure(boiler);

        public float BlowdownRate => MaxBlowdownRate(boiler);

        /// <summary>
        /// Water from the crown sheet to the steam outlet, in kg: what the
        /// glass spans, as the boiler's fireman knows it.
        /// </summary>
        public float GlassMass
        {
            get
            {
                var vessel = Vessel(boiler);
                if (vessel == null || vessel.waterVolume <= 0f)
                    return 0f;
                var span = Mathf.Max(0f, boiler.steamOutletNormalizedWaterLevel - CrownSheetLevel(boiler));
                return TotalVolume(boiler) * span * vessel.mass / vessel.waterVolume;
            }
        }

        internal static Guard GuardFor(string livery) =>
            LocomotiveProfiles.TryGet(livery, out var profile) ? profile.LowWaterGuard
            : MakerFitRegistry.TryGetLowWaterGuard(livery, out var maker) ? maker
            : Guard.None;

        /// <summary>
        /// Sets up the water of each boiler on <paramref name="car"/>. Returns a
        /// line for the log, or null when the car has no boiler.
        /// </summary>
        public static string Attach(TrainCar car, SimulationFlow flow)
        {
            var boilers = flow.OrderedSimComps.OfType<Boiler>().ToList();
            if (boilers.Count == 0)
                return null;
            var engine = flow.OrderedSimComps.OfType<ReciprocatingSteamEngine>().FirstOrDefault();
            var livery = car != null && car.carLivery != null ? car.carLivery.id : null;
            var guard = GuardFor(livery);
            // A plug needs a firebox it can drown, and the injector rig whose
            // water hose washes the boiler out to renew it.
            if (guard == Guard.FusiblePlug && (!Main.Session.FusiblePlugs || !FireboxRegistry.TryGet(car, out _) || !InjectorRig.TryGet(car, out _)))
                guard = Guard.None;

            PruneDestroyedCars();
            var notes = new List<string>();
            foreach (var boiler in boilers)
            {
                var rig = new BoilerWaterRig(car, boiler, engine, guard);
                ByBoiler[boiler] = rig;
                if (car != null)
                    ByCar[car] = rig;
                if (PendingSaves.TryGetValue(boiler, out var saved))
                {
                    PendingSaves.Remove(boiler);
                    rig.Load(saved);
                }
                else
                    rig.ChillIfCold();
                var vessel = Vessel(boiler);
                notes.Add($"boiler water on '{boiler.id}': {(vessel != null ? vessel.mass : 0f):F0} kg in {TotalVolume(boiler):F0} L, " +
                          $"crown sheet at {CrownSheetLevel(boiler):F2}, outlet at {boiler.steamOutletNormalizedWaterLevel:F2}, " +
                          $"blowdown {MaxBlowdownRate(boiler):F0} kg/s, throttle {(engine != null ? engine.maxFlowReadOut.Value : 0f):F1} kg/s, " +
                          (guard == Guard.FusiblePlug ? "fusible plug" : guard == Guard.LowWaterAlarm ? "low-water alarm" : "no low-water guard"));
            }
            return string.Join("; ", notes);
        }

        public static bool TryGet(Boiler boiler, out BoilerWaterRig rig) => ByBoiler.TryGetValue(boiler, out rig);

        /// <summary>Every rig on a car, by car.</summary>
        public static IEnumerable<BoilerWaterRig> All => ByCar.Where(pair => pair.Key != null).Select(pair => pair.Value).ToList();

        public static bool TryGet(TrainCar car, out BoilerWaterRig rig)
        {
            rig = null;
            return car != null && ByCar.TryGetValue(car, out rig);
        }

        /// <summary>
        /// The rig the player tends from <paramref name="car"/>: its own, or a
        /// coupled loco's, the one the player was last in between two.
        /// </summary>
        public static bool TryGetTended(TrainCar car, out BoilerWaterRig rig) =>
            TryGet(CarCouplings.Tended(car, ByCar), out rig);

        /// <summary>
        /// Washes the boiler out, as the shed does with the boiler cold:
        /// fresh water and sludge cleared, and a new fusible plug.
        /// </summary>
        public void Washout()
        {
            var vessel = Vessel(boiler);
            var renewed = vessel != knownVessel;
            knownVessel = vessel;
            Water.Washout(vessel != null ? vessel.mass : 0f);
            Fouling.Washout();
            GlassChoke.Reset();
            Plug?.Renew(vessel != null ? vessel.waterTemp : 20f);
            PlugVent = 0f;
            SetDrowning(0f);
            if (renewed)
                Settle(vessel);
            Log("boiler washed out: fresh water, sludge cleared" + (Plug != null ? ", new fusible plug" : ""));
        }

        /// <summary>For testing: makes the water as foul as <paramref name="ppm"/>, as the feed would, part of it stirred-up sludge.</summary>
        public void Foul(float ppm)
        {
            var mass = MassNow;
            var solids = mass * ppm * 1e-6f;
            Water.Restore(solids * (1f - BoilerWater.SludgeShare), 0f, solids * BoilerWater.SludgeShare, mass);
            Log($"boiler water fouled to {ppm:F0} ppm for testing");
        }

        /// <summary>For testing: melts the plug, if the boiler has one. Returns whether it had.</summary>
        public bool MeltPlug()
        {
            if (Plug == null || Plug.Melted)
                return Plug != null;
            Plug.Restore(true, FusiblePlug.MeltingPoint);
            Log("fusible plug melted for testing");
            return true;
        }

        /// <summary>
        /// Start of the boiler's tick: notices a boiler the game reset, trades
        /// heat between the water and the steel, and notes the water before the feed.
        /// </summary>
        public void BeginTick(float delta)
        {
            WatchRepairs();
            var vessel = Vessel(boiler);
            if (vessel != knownVessel)
            {
                knownVessel = vessel;
                Washout();
                ChillIfCold();
                Settle(vessel);
                if (!Net.IsClient)
                    ThermalShock.ResetTransient();
            }
            if (vessel != null && vessel.mass > 0f)
            {
                vessel.AddEnergy(Steel.Exchange(delta, vessel.waterTemp, vessel.mass * BoilerMetal.WaterHeat));
                // What soot, scale, and thermal-shock tube leaks keep from the water goes up the stack.
                var lost = Mathf.Min(1f, FoulingLoss + ThermalShockEfficiencyLoss);
                if (lost > 0f && delta > 0f)
                    vessel.AddEnergy(-Mathf.Max(0f, boiler.heat.Value) * delta * lost);
                StepThermalShock(delta, vessel);
            }
            else if (vessel != null)
            {
                CoolEmpty(delta);
                if (!Net.IsClient)
                {
                    ThermalShock.ResetTransient();
                    TubeLeakRate = 0f;
                }
            }
            ChargeWear(delta);
            massAtTick = vessel != null ? vessel.mass : 0f;
        }

        private float StartupBoost() =>
            Car != null && FireboxRegistry.TryGet(Car, out var coal) ? coal.StartupMultiplier
            : Car != null && RealisticOilFirebox.TryGet(Car, out var oil) ? oil.StartupBoost
            : 1f;

        private void StepThermalShock(float delta, WaterPressureVessel vessel)
        {
            if (Net.IsClient || !(delta > 0f))
                return;
            var enabled = Main.Settings == null || Main.Settings.BoilerThermalShock;
            var directSteaming = InjectorRig.TryGet(boiler, out var injectors) && (injectors.Steaming || injectors.SteamedLastStep > 0f);
            var working = engine != null
                && engine.throttleControl.Value >= Drifting.CrackedThrottle
                && (engine.intakeFlowReadOut.Value > 0.05f * Mathf.Max(0.5f, engine.maxFlowReadOut.Value)
                    || engine.exhaustFlowReadOut.Value > 0.05f * Mathf.Max(0.5f, engine.maxFlowReadOut.Value)
                    || Car != null && Mathf.Abs(Car.GetForwardSpeed()) > 0.5f);
            ThermalShock.Step(delta, new BoilerThermalShockInputs
            {
                BoilerTemperature = vessel.waterTemp,
                StartupMultiplier = StartupBoost(),
                Working = working,
                DirectSteaming = directSteaming,
                Enabled = enabled,
            });
            TubeLeakRate = enabled ? ThermalShock.LeakRate(vessel.pressure) : 0f;
            if (!(TubeLeakRate > 0f))
                return;
            pendingTubeLeak += TubeLeakRate * delta;
            if (pendingTubeLeak < RealisticStoker.SteamLot)
                return;
            vessel.RemoveWater(Mathf.Min(pendingTubeLeak, vessel.mass));
            pendingTubeLeak = 0f;
        }

        // A drained boiler has no water to carry the stock exterior heat
        // loss. Do not leave its steel permanently hot after the fire is out.
        internal void CoolEmpty(float seconds)
        {
            var vessel = Vessel(boiler);
            if (vessel != null && vessel.mass <= 0f && !(boiler.heat.Value > 0f))
                Steel.Cool(seconds, AmbientWater, ExteriorConductance(boiler));
        }

        internal static void CoolEmptyBoilers(float seconds)
        {
            if (Net.IsClient)
                return;
            foreach (var rig in ByBoiler.Values)
                rig.CoolEmpty(seconds);
        }

        // DV's damage-sensitivity difficulty modifier (1 on Standard, 0 with damage off).
        private float DamageSensitivity()
        {
            var gp = damage != null ? DamageGameParams(damage) : null;
            return gp != null ? Mathf.Max(0f, gp.DamageSensitivityModifier) : 1f;
        }

        // Charges or refunds a tracked share of mechanicalPT health, recording
        // only the hit points actually removed by ApplyDamage (when clamped at
        // 0 or ignored), so disabling care or calling Renew can never refund
        // more HP than was actually removed or repair unrelated engine wear.
        private static void ApplyTrackedWear(TrainDamage train, float target, ref float tracked)
        {
            if (!(train.fullHitPoints > 0f))
                return;
            var change = Mathf.Max(0f, target) - tracked;
            if (change >= LeastWear)
            {
                var before = train.CurrentHitPoints;
                if (!(before > 0f))
                    return;
                train.ApplyDamage(train.fullHitPoints * change);
                var actualRemoved = Mathf.Max(0f, before - train.CurrentHitPoints);
                tracked += actualRemoved / train.fullHitPoints;
            }
            else if (change <= -LeastWear)
            {
                var refund = Mathf.Min(tracked, -change);
                if (refund >= LeastWear)
                {
                    train.RepairDamage(train.fullHitPoints * refund);
                    tracked = Mathf.Max(0f, tracked - refund);
                }
            }
        }

        // The engine's wear: the fouling's, thermal shock's, and superheater's
        // shares as they stand, scaled by DV's damage sensitivity and charged
        // or given back, plus drifting hooked up. The host's sim charges it.
        private void ChargeWear(float delta)
        {
            if (Net.IsClient)
                return;
            if (engine != null && Car != null && delta > 0f)
                driftWear += Drifting.Wear(engine.throttleControl.Value, engine.reverserControl.Value, Car.GetForwardSpeed(), Drift) * delta;
            var train = damage != null ? damage.mechanicalPT : null;
            if (train == null)
                return;
            // A paid engine standing with its fire in: its soot wear waits
            // (Patches.ServiceGrace), charged once the grace ends.
            if (Patches.ServiceGrace.Holds(Car))
                return;
            var sensitivity = DamageSensitivity();
            ApplyTrackedWear(train, Mathf.Min(1f, WearPerLoss * FoulingLoss * sensitivity), ref foulingWear);
            var shockEnabled = Main.Settings == null || Main.Settings.BoilerThermalShock;
            var shockTarget = shockEnabled ? Mathf.Min(1f, BoilerThermalShock.EngineWearWeight * ThermalShock.Wear * sensitivity) : 0f;
            ApplyTrackedWear(train, shockTarget, ref thermalShockWear);
            var superheaterEnabled = Main.Settings == null || Main.Settings.SuperheaterCare;
            var superheaterTarget = superheaterEnabled && engine != null && EngineSuperheater.TryGet(engine, out var superheater)
                ? Mathf.Min(1f, Superheater.EngineWearWeight * superheater.Model.Wear * sensitivity)
                : 0f;
            ApplyTrackedWear(train, superheaterTarget, ref superheaterWear);
            if (driftWear >= LeastWear)
            {
                var driftCharge = driftWear * sensitivity;
                if (driftCharge > 0f)
                    train.ApplyDamage(train.fullHitPoints * driftCharge);
                driftWear = 0f;
            }
            // Its own charges are not a repair.
            engineHealth = train.HealthPercentage;
        }

        // A boiler made or loaded anew: its steel at its water's temperature, no steam in the water yet.
        private void Settle(WaterPressureVessel vessel)
        {
            Steel.Settle(vessel != null && vessel.mass > 0f ? vessel.waterTemp : AmbientWater);
            Swell.Reset();
        }

        // A pit stop's repair of the body or engine renews a melted plug as
        // the game renews a burst boiler: a new boiler at its spawn pressure
        // and water, which the tick then finds and washes out, fresh water
        // and a new plug.
        private void WatchRepairs()
        {
            if (Car == null)
                return;
            if (damage == null && (damage = Car.GetComponent<DamageController>()) == null)
                return;
            NoteHealth(damage.bodyDamage != null ? damage.bodyDamage.HealthPercentage : 1f,
                damage.mechanicalPT != null ? damage.mechanicalPT.HealthPercentage : 1f);
        }

        /// <summary>The body's and the engine's health this tick, 0 to 1; a repair with the plug melted renews the boiler.</summary>
        internal void NoteHealth(float body, float engine)
        {
            var repaired = ticks >= RepairWatchFrom
                           && (body > bodyHealth + RepairRise && body > RepairedHealth
                               || engine > engineHealth + RepairRise && engine > RepairedHealth);
            // A repair of the engine is the shed's work on the boiler and
            // superheater too: tubes cleaned/re-expanded, scale chipped out,
            // and cooked superheater elements renewed for the share repaired.
            if (ticks >= RepairWatchFrom && engine > engineHealth + RepairRise)
            {
                var share = Mathf.Clamp01((engine - engineHealth) / Mathf.Max(1e-4f, 1f - engineHealth));
                var repairedBoiler = false;
                if (FoulingLoss > 0f)
                {
                    Fouling.Clean(share);
                    foulingWear *= 1f - share;
                    repairedBoiler = true;
                }
                if (ThermalShock.Wear > 0f || thermalShockWear > 0f)
                {
                    ThermalShock.Repair(share);
                    thermalShockWear *= 1f - share;
                    repairedBoiler = true;
                }
                if (this.engine != null && EngineSuperheater.TryGet(this.engine, out var superheater) && (superheater.Model.Wear > 0f || superheaterWear > 0f))
                {
                    superheater.Model.Repair(share);
                    superheaterWear *= 1f - share;
                    repairedBoiler = true;
                }
                if (repairedBoiler && share > 0.99f)
                    Log("engine repaired: boiler tubes, scale, and superheater elements restored");
            }
            ticks++;
            bodyHealth = body;
            engineHealth = engine;
            if (repaired && Plug != null && Plug.Melted)
                Renew($"repaired at {body:P0} body, {engine:P0} engine");
        }

        /// <summary>
        /// Renews the boiler as the game renews a burst one: a new vessel at
        /// its spawn pressure and water, which the next tick washes out, fresh
        /// water and a new plug. Returns false where the game's boiler cannot be read.
        /// </summary>
        public bool Renew(string why)
        {
            if (SpawnPressure == null || SpawnWaterMass == null)
                return false;
            var vessel = new WaterPressureVessel(TotalVolume(boiler), (float)SpawnPressure.GetValue(boiler), (float)SpawnWaterMass.GetValue(boiler));
            Vessel(boiler) = vessel;
            CrownSheetTemperature(boiler) = vessel.waterTemp;
            ThermalShock.Repair(1f);
            ThermalShock.ResetTransient();
            pendingTubeLeak = 0f;
            TubeLeakRate = 0f;
            if (engine != null && EngineSuperheater.TryGet(engine, out var superheater))
                superheater.Model.Repair(1f);
            // Credit back any tracked boiler/superheater engine-wear charge so a
            // body-only repair or recovery renew never leaves orphaned damage on
            // mechanicalPT (after a full engine repair NoteHealth already zeroed both).
            if (damage == null && Car != null)
                damage = Car.GetComponent<DamageController>();
            var train = damage != null ? damage.mechanicalPT : null;
            if (train != null && !Net.IsClient)
            {
                var refund = Mathf.Max(0f, thermalShockWear) + Mathf.Max(0f, superheaterWear);
                if (refund >= LeastWear)
                    train.RepairDamage(train.fullHitPoints * refund);
                thermalShockWear = 0f;
                superheaterWear = 0f;
                engineHealth = train.HealthPercentage;
            }
            Log($"{why}: the boiler renewed" + (Plug != null ? " with a new fusible plug" : ""));
            return true;
        }

        /// <summary>The boiler's water, °C.</summary>
        public float WaterTemperature => Vessel(boiler)?.waterTemp ?? AmbientWater;

        /// <summary>Whether the water is below boiling at the atmosphere's pressure, the gauge at nothing.</summary>
        public bool BelowBoilingNow
        {
            get
            {
                var vessel = Vessel(boiler);
                return vessel != null && vessel.mass > 0f && vessel.pressure <= AtmosphericPressure && vessel.waterTemp < BelowBoiling;
            }
        }

        // The game's cold boiler, at the atmosphere's pressure, is boiling:
        // its water is put at the air's temperature.
        private void ChillIfCold()
        {
            var vessel = Vessel(boiler);
            if (vessel == null || vessel.mass <= 0f || vessel.pressure > AtmosphericPressure || vessel.waterTemp <= AmbientWater + 1f)
                return;
            SetWaterTemperature(vessel, AmbientWater);
            Log($"cold boiler: water at {vessel.waterTemp:F0} C, not boiling");
        }

        // Its water at <paramref name="temperature"/>, below boiling: the
        // stock vessel then holds the atmosphere's pressure until it boils.
        private void SetWaterTemperature(WaterPressureVessel vessel, float temperature)
        {
            vessel.enthalpy = vessel.mass * SteamTables.WaterSpecificEnthalpyByTemp(temperature);
            vessel.Update();
            CrownSheetTemperature(boiler) = vessel.waterTemp;
            Plug?.Restore(Plug.Melted, vessel.waterTemp);
            Settle(vessel);
        }

        /// <summary>
        /// After the injector step: what the injectors, pump, hose, or hand
        /// pump put in; a repair stand's steam is pure water, no feed.
        /// </summary>
        public void AfterFeed()
        {
            var vessel = Vessel(boiler);
            var steamed = InjectorRig.TryGet(boiler, out var injectors) ? injectors.SteamedLastStep : 0f;
            if (vessel != null)
                fed += Mathf.Max(0f, vessel.mass - massAtTick - steamed);
        }

        public float MassNow => Vessel(boiler)?.mass ?? 0f;

        /// <summary>After the blowdown step, with the water before it.</summary>
        public void AfterBlowdown(float massBefore) => blown += Mathf.Max(0f, massBefore - MassNow);

        /// <summary>The engine's try cocks, on an engine with them; else null.</summary>
        public TryCockRig TryCocks { get; internal set; }

        /// <summary>After the stock level step: steps the water, then shows its foam and swell in the glass, and the foam in the steam; then what the try cocks let out at that level leaves the boiler.</summary>
        public void AfterWaterLevel(float delta)
        {
            StepWaterLevel(delta);
            DrainTryCocks(delta);
        }

        // The try cocks' water and steam out of the boiler, gathered into
        // lots as the plug's steam is (a tick's few grams are lost to a
        // vessel's rounding).
        private void DrainTryCocks(float delta)
        {
            var vessel = Vessel(boiler);
            if (TryCocks == null || vessel == null || !(delta > 0f) || !TryCocks.AnyOpen)
                return;
            var (water, steam) = TryCocks.Total();
            pendingCockWater += water * delta;
            pendingCockSteam += steam * delta;
            if (pendingCockWater + pendingCockSteam < RealisticStoker.SteamLot)
                return;
            vessel.RemoveWater(Mathf.Min(pendingCockWater, vessel.mass));
            vessel.RemoveSteam(Mathf.Min(pendingCockSteam, vessel.mass));
            pendingCockWater = pendingCockSteam = 0f;
        }

        private void StepWaterLevel(float delta)
        {
            var vessel = Vessel(boiler);
            StepFouling(delta, vessel);
            // Cold water stands lower than the game's boiler, which takes all
            // water below boiling at its volume at 100 °C, shows it.
            var coldDrop = vessel != null && BelowBoilingNow
                ? vessel.waterVolume / Mathf.Max(1f, TotalVolume(boiler)) * ColdWater.Shrink(vessel.waterTemp)
                : 0f;
            TrueLevel = boiler.waterLevelReadOut.Value - coldDrop;
            StepSwell(delta, vessel);
            var on = Main.Settings.BoilerWaterImpurities;
            if (on != wasOn)
            {
                wasOn = on;
                if (on && vessel != null)
                    Water.Washout(vessel.mass);
                if (!on)
                    GlassChoke.Clear();
            }
            float surface;
            if (!on || vessel == null || !(delta > 0f))
            {
                fed = blown = 0f;
                surface = TrueLevel + Swell.Rise;
            }
            else
            {
                Water.Quality = Main.Settings.BoilerWaterQuality;
                var maxFlow = engine != null ? engine.maxFlowReadOut.Value : 0f;
                var raw = vessel.waterVolume / Mathf.Max(1f, TotalVolume(boiler)) - coldDrop;
                Water.Step(delta, new BoilerWaterInputs
                {
                    WaterMass = vessel.mass,
                    Fed = fed,
                    BlownDown = blown,
                    Work = maxFlow > 0f ? Mathf.Clamp01(boiler.steamConsumption.Value / maxFlow) : 0f,
                    Fullness = Mathf.InverseLerp(CrownSheetLevel(boiler), boiler.steamOutletNormalizedWaterLevel, raw + Swell.Rise),
                });
                fed = blown = 0f;

                boiler.outletSteamQualityReadOut.Value *= 1f - Water.Carryover;
                surface = TrueLevel + Water.GlassRise + Swell.Rise;
            }
            hasMirroredImpurities = false;
            TrueSurfaceLevel = surface;
            HasTrueSurface = true;

            if (TryCocks != null && delta > 0f)
            {
                var waterMass = vessel != null ? Mathf.Max(1f, vessel.mass) : 1f;
                var maxFlow = engine != null ? engine.maxFlowReadOut.Value : 0f;
                GlassChoke.Step(delta, new GaugeGlassChokeInputs
                {
                    SurfaceLevel = surface,
                    GlassBottom = TryCocks.Levels[TryCockRig.GlassBottom],
                    GlassTop = TryCocks.Levels[TryCockRig.GlassTop],
                    ImpuritiesEnabled = on && vessel != null,
                    FoamingConcentration = on ? Water.FoamingConcentration : 0f,
                    SuspendedSludgePpm = on ? Water.SuspendedSludge / waterMass * 1e6f : 0f,
                    Foam = on ? Water.Foam : 0f,
                    Work = maxFlow > 0f ? Mathf.Clamp01(boiler.steamConsumption.Value / maxFlow) : 0f,
                    Pressure = vessel != null ? vessel.pressure : 1f,
                    Boiling = vessel != null && vessel.mass > 0f && !BelowBoilingNow,
                    BottomCock = TryCocks.Opening(TryCockRig.GlassBottom),
                    TopCock = TryCocks.Opening(TryCockRig.GlassTop),
                });
                // Write the standing glass column (with choking lag/sticking) to the
                // stock port without leaking transient blow-through drawdown; GlassBlow
                // draws the momentary drawdown and rebound visually on the cab glass.
                boiler.waterLevelReadOut.Value = GlassChoke.ColumnLevel;
            }
            else
            {
                boiler.waterLevelReadOut.Value = TryCocks != null && GlassChoke.HasLevel ? GlassChoke.ColumnLevel : surface;
            }
        }

        // Soot from a coal fire's smoke and the gas through the tubes; scale
        // from the feed's solids as the water boils off.
        private void StepFouling(float delta, WaterPressureVessel vessel)
        {
            if (vessel == null || !(delta > 0f))
                return;
            var pressure = vessel.pressure;
            var latent = (SteamTables.SteamSpecificEnthalpy(pressure) - SteamTables.WaterSpecificEnthalpy(pressure)) * 1000f;
            var soot = 0f;
            var flow = 0f;
            if (Car != null && FireboxRegistry.TryGet(Car, out var coal))
            {
                soot = coal.Model.SootDensity;
                flow = coal.Model.CombustionRateNormalized;
            }
            Fouling.Step(delta, new BoilerFoulingInputs
            {
                Boiled = BelowBoilingNow || !(latent > 0f) ? 0f : Mathf.Max(0f, boiler.heat.Value) * delta / latent,
                FeedSolids = Main.Settings.BoilerWaterImpurities ? Water.FeedConcentration : 0f,
                HeatingSurface = HeatingSurface,
                Soot = soot,
                GasFlow = flow,
            });
        }

        // The steam in the water: what the fire boils in it, less what the
        // feed condenses, and the pressure's fall or rise.
        private void StepSwell(float delta, WaterPressureVessel vessel)
        {
            if (vessel == null || !(delta > 0f))
                return;
            var pressure = vessel.pressure;
            var boilingWater = SteamTables.WaterSpecificEnthalpy(pressure);
            var feed = fed;
            var feedTemperature = boiler.feedwaterTemperature.Value;
            if (Car != null && InjectorRig.TryGet(Car, out var injectors))
            {
                feed = injectors.FedLastStep;
                feedTemperature = injectors.FeedTemperatureLastStep;
            }
            Swell.Step(delta, new BoilerSwellInputs
            {
                Heat = boiler.heat.Value,
                LatentHeat = (SteamTables.SteamSpecificEnthalpy(pressure) - boilingWater) * 1000f,
                FullSteam = engine != null ? engine.maxFlowReadOut.Value : 0f,
                Fed = feed,
                FeedSubcooling = Mathf.Max(0f, boilingWater - SteamTables.WaterSpecificEnthalpyByTemp(feedTemperature)) * 1000f,
                Pressure = pressure,
                Boiling = vessel.mass > 0f && !BelowBoilingNow,
            });
        }

        /// <summary>
        /// Runs in place of the stock crown sheet step, on the true level:
        /// bared, the sheet heats toward the fire with the stock time constant.
        /// Then the plug and the alarm.
        /// </summary>
        public void SimulateCrownSheet(float delta)
        {
            var vessel = Vessel(boiler);
            if (vessel == null)
                return;
            var waterTemp = vessel.waterTemp;
            var crownLevel = CrownSheetLevel(boiler);
            var bared = TrueLevel <= crownLevel;
            var target = bared ? Mathf.Max(waterTemp, boiler.fireboxTemperature.Value) : waterTemp;
            ref var crown = ref CrownSheetTemperature(boiler);
            var rate = (target - crown) / Mathf.Max(0.01f, CrownSheetSmoothTime(boiler));
            crown = rate > 0f ? Mathf.Min(target, crown + rate * delta) : Mathf.Max(target, crown + rate * delta);

            if (Plug != null)
                StepPlug(delta, vessel, bared, ref crown);
            Alarm?.Step(TrueLevel, crownLevel);

            boiler.normalizedCrownSheetTemperatureReadOut.Value = Mathf.InverseLerp(waterTemp, CrownSheetOverheat(boiler), crown);
        }

        private void StepPlug(float delta, WaterPressureVessel vessel, bool bared, ref float crown)
        {
            var failures = GameParams(boiler);
            if (!Plug.Melted && (failures == null || failures.DrivetrainFailuresAllowed)
                && Plug.Step(delta, bared, crown, vessel.waterTemp))
            {
                Log($"fusible plug melted at {vessel.pressure:F1} bar, crown sheet {crown:F0} C, water {TrueLevel:F2} " +
                    $"(crown sheet at {CrownSheetLevel(boiler):F2}, glass showing {GlassLevel:F2})");
                FireController.Announce(PlugMeltedNoticeFor(Car));
                Net.Broadcast(Car, FireEvent.PlugMelted);
            }
            if (!Plug.Melted)
                return;

            // Steam and water blow through the bore and the jet cools the sheet.
            PlugVent = Plug.Vent(vessel.pressure);
            pendingVent += PlugVent * delta;
            if (pendingVent >= RealisticStoker.SteamLot)
            {
                vessel.RemoveSteam(Mathf.Min(pendingVent, vessel.mass));
                pendingVent = 0f;
            }
            crown = Mathf.Min(crown, PlugHoldsCrownBelow * CrownSheetOverheat(boiler));
            SetDrowning(Plug.Drowning(vessel.pressure));
        }

        private void SetDrowning(float drowning)
        {
            if (FireboxRegistry.TryGet(Car, out var firebox))
                firebox.PlugDrowning = drowning;
        }

        public void Save(JObject data)
        {
            if (data == null)
                return;
            data.SetFloat(SolidsKey, Water.Solids);
            data.SetFloat(SettledKey, Water.SettledSludge);
            data.SetFloat(SuspendedKey, Water.SuspendedSludge);
            if (Plug != null && Plug.Melted)
                data.SetBool(PlugMeltedKey, true);
            if (BelowBoilingNow)
                data.SetFloat(WaterTempKey, WaterTemperature);
            if (Fouling.ScaleMass > 0f || Fouling.SootLoss > 0f || foulingWear != 0f)
            {
                data.SetFloat(ScaleKey, Fouling.ScaleMass);
                data.SetFloat(SootKey, Fouling.SootLoss);
                data.SetFloat(FoulingWearKey, foulingWear);
            }
            if (ThermalShock.Wear > 0f || thermalShockWear != 0f)
            {
                data.SetFloat(ThermalShockWearKey, ThermalShock.Wear);
                data.SetFloat(ThermalShockEngineWearKey, thermalShockWear);
            }
            if (superheaterWear != 0f)
                data.SetFloat(SuperheaterEngineWearKey, superheaterWear);
            if (GlassChoke.Choke > 0f || GlassChoke.TimeSinceBlowThrough > 0f)
            {
                data.SetFloat(GlassChokeKey, GlassChoke.Choke);
                if (GlassChoke.HasLevel)
                    data.SetFloat(GlassColumnKey, GlassChoke.ColumnLevel);
                data.SetFloat(GlassBlowAgeKey, GlassChoke.TimeSinceBlowThrough);
            }
        }

        /// <summary>After the stock boiler has loaded its save.</summary>
        public void Load(JObject data)
        {
            var vessel = Vessel(boiler);
            knownVessel = vessel;
            var mass = vessel != null ? vessel.mass : 0f;
            var solids = data.GetFloat(SolidsKey);
            if (solids.HasValue)
                Water.Restore(solids.Value, data.GetFloat(SettledKey) ?? 0f, data.GetFloat(SuspendedKey) ?? 0f, mass);
            else
                Water.Washout(mass);
            if (Plug != null)
                Plug.Restore(data.GetBool(PlugMeltedKey) == true, vessel != null ? vessel.waterTemp : 20f);
            // The stock load makes water at the atmosphere's pressure boiling;
            // a save from before the water's temperature was kept stays so.
            var temperature = data.GetFloat(WaterTempKey);
            if (temperature.HasValue && vessel != null && vessel.mass > 0f && vessel.pressure <= AtmosphericPressure)
                SetWaterTemperature(vessel, Mathf.Clamp(temperature.Value, 0f, BelowBoiling));
            Settle(vessel);
            // The engine's saved health already holds the fouling's wear.
            Fouling.Restore(data.GetFloat(ScaleKey) ?? 0f, data.GetFloat(SootKey) ?? 0f);
            foulingWear = Mathf.Clamp01(data.GetFloat(FoulingWearKey) ?? 0f);
            ThermalShock.Restore(data.GetFloat(ThermalShockWearKey) ?? 0f);
            ThermalShock.SetEnabled(ThermalShockActive);
            thermalShockWear = Mathf.Clamp01(data.GetFloat(ThermalShockEngineWearKey) ?? 0f);
            superheaterWear = Mathf.Clamp01(data.GetFloat(SuperheaterEngineWearKey) ?? 0f);
            pendingTubeLeak = 0f;
            TubeLeakRate = 0f;
            var choke = data.GetFloat(GlassChokeKey);
            if (choke.HasValue)
                GlassChoke.Restore(choke.Value, data.GetFloat(GlassColumnKey) ?? float.NaN, data.GetFloat(GlassBlowAgeKey) ?? 0f);
            else
                GlassChoke.Reset();
        }

        /// <summary>Called on every stock boiler save load: applies ours now, or once the rig is made.</summary>
        public static void OnLoaded(Boiler boiler, JObject data)
        {
            if (data == null)
                return;
            if (TryGet(boiler, out var rig))
                rig.Load(data);
            else
            {
                PendingSaves.Remove(boiler);
                PendingSaves.Add(boiler, data);
            }
        }

        private void Log(string text) => Main.Mod?.Logger.Log($"{Car.LogName()}: {text}");

        /// <summary>Gives <paramref name="boiler"/> a rig directly, for checks outside the game.</summary>
        internal static BoilerWaterRig Create(Boiler boiler, ReciprocatingSteamEngine engine, Guard guard, TrainCar car = null)
        {
            var rig = new BoilerWaterRig(car, boiler, engine, guard);
            ByBoiler[boiler] = rig;
            if (car != null)
                ByCar[car] = rig;
            rig.ChillIfCold();
            return rig;
        }

        /// <summary>The boiler's crown sheet temperature, in °C, for checks outside the game.</summary>
        internal float CrownSheetTemperatureNow => CrownSheetTemperature(boiler);

        /// <summary>Whether the stock damage step would burst the boiler on its crown sheet now.</summary>
        internal bool CrownSheetWouldFail =>
            CrownSheetTemperature(boiler) > CrownSheetOverheat(boiler) && BoilerPressure > MinimumExplosionPressure(boiler);

        private static void PruneDestroyedCars()
        {
            foreach (var car in ByCar.Keys.Where(car => car == null).ToList())
            {
                var rig = ByCar[car];
                ByCar.Remove(car);
                ByBoiler.Remove(rig.boiler);
            }
        }
    }
}
