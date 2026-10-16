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

        private static readonly Dictionary<string, Guard> Guards = new Dictionary<string, Guard>
        {
            ["LocoS282A"] = Guard.FusiblePlug,
            ["LocoS060"] = Guard.FusiblePlug,
            ["S464-730"] = Guard.LowWaterAlarm,
            ["S484-840A"] = Guard.FusiblePlug,
            ["FEF-3"] = Guard.FusiblePlug,
            // British practice: lead plugs in the firebox crown.
            ["em_lms_8f_loc_black"] = Guard.FusiblePlug,
            ["em_lms_8f_loc_grey"] = Guard.FusiblePlug,
        };

        /// <summary>What players near the loco are told when its plug melts.</summary>
        public const string PlugMeltedNotice = "The fusible plug has melted: steam is blowing into the firebox and has put the fire out. " +
                                               "Have the engine repaired at a service station, or wash out the boiler on a service track (hydrant hose, boiler cold), to renew it.";

        private const string SolidsKey = "rfSolids";
        private const string SettledKey = "rfSludgeSettled";
        private const string SuspendedKey = "rfSludgeSuspended";
        private const string PlugMeltedKey = "rfPlugMelted";
        private const string WaterTempKey = "rfWaterTemp";
        private const string ScaleKey = "rfScale";
        private const string SootKey = "rfSoot";
        private const string FoulingWearKey = "rfFoulingWear";

        // The heating surface for each m³ of boiler (inferred: the USRA light
        // Mikado's some 280 m² in a boiler of the S282's 17 m³).
        private const float HeatingSurfacePerVolume = 16f;

        // Fouling stands as this much engine wear for each share of heat it
        // keeps from the water; wear is charged or given back in steps of at
        // least this share of the engine's health.
        private const float WearPerLoss = 2f;
        private const float LeastWear = 1e-4f;

        /// <summary>The air's temperature, °C, toward which the stock boiler cools, and a cold boiler's water.</summary>
        public const float AmbientWater = 25f;

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

        private static readonly Dictionary<Boiler, BoilerWaterRig> ByBoiler = new Dictionary<Boiler, BoilerWaterRig>();
        private static readonly Dictionary<TrainCar, BoilerWaterRig> ByCar = new Dictionary<TrainCar, BoilerWaterRig>();
        private static readonly Dictionary<Boiler, JObject> PendingSaves = new Dictionary<Boiler, JObject>();

        private readonly Boiler boiler;
        private readonly ReciprocatingSteamEngine engine;
        private WaterPressureVessel knownVessel;
        private float massAtTick;
        private float fed;
        private float blown;
        private float pendingVent;
        private bool wasOn;
        private DamageController damage;
        private float bodyHealth = float.NaN;
        private float engineHealth = float.NaN;
        private int ticks;
        // The engine's health charged now for the fouling, and the drifting
        // wear not yet charged.
        private float foulingWear;
        private float driftWear;

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

        /// <summary>The boiler's heating surface, m².</summary>
        public float HeatingSurface => TotalVolume(boiler) / 1000f * HeatingSurfacePerVolume;

        /// <summary>The share of the fire's heat soot and scale keep from the water.</summary>
        public float FoulingLoss => Fouling.Loss(HeatingSurface);

        /// <summary>Whether the engine drifts with the throttle shut and the reverser hooked up, drawing smokebox gas into its cylinders.</summary>
        public bool DriftingHookedUp => engine != null && Car != null
                                        && Drifting.HookedUp(engine.throttleControl.Value, engine.reverserControl.Value, Car.GetForwardSpeed());

        public TrainCar Car { get; }

        public BoilerWater Water { get; }

        /// <summary>The crown sheet's fusible plug, on a loco whose prototype had one; else null.</summary>
        public FusiblePlug Plug { get; }

        /// <summary>The low-water alarm, on a loco whose prototype had one; else null.</summary>
        public LowWaterAlarm Alarm { get; }

        /// <summary>The water level over the crown sheet as it is, tilt included, 0 to 1.</summary>
        public float TrueLevel { get; private set; }

        /// <summary>The level the glass shows, foam included.</summary>
        public float GlassLevel => boiler.waterLevelReadOut.Value;

        public float CrownSheetLevelNormalized => CrownSheetLevel(boiler);

        /// <summary>What blows through a melted plug into the firebox, in kg/s.</summary>
        public float PlugVent { get; internal set; }

        public float BoilerPressure => Vessel(boiler)?.pressure ?? 1f;

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
            var guard = livery != null && Guards.TryGetValue(livery, out var fitted) ? fitted : Guard.None;
            // A plug needs a firebox it can drown, and the hydrant hose that
            // washes the boiler out to renew it.
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

        /// <summary>The rig the player tends from <paramref name="car"/>: its own, or a coupled loco's.</summary>
        public static bool TryGetTended(TrainCar car, out BoilerWaterRig rig)
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

        /// <summary>
        /// Washes the boiler out, as the shed does with the boiler cold:
        /// fresh water and sludge cleared, and a new fusible plug.
        /// </summary>
        public void Washout()
        {
            var vessel = Vessel(boiler);
            knownVessel = vessel;
            Water.Washout(vessel != null ? vessel.mass : 0f);
            Fouling.Washout();
            Plug?.Renew(vessel != null ? vessel.waterTemp : 20f);
            PlugVent = 0f;
            SetDrowning(0f);
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
            }
            if (vessel != null && vessel.mass > 0f)
            {
                vessel.AddEnergy(Steel.Exchange(delta, vessel.waterTemp));
                // What soot and scale keep from the water goes up the stack.
                var lost = FoulingLoss;
                if (lost > 0f && delta > 0f)
                    vessel.AddEnergy(-Mathf.Max(0f, boiler.heat.Value) * delta * lost);
            }
            ChargeWear(delta);
            massAtTick = vessel != null ? vessel.mass : 0f;
        }

        // The engine's wear: the fouling's share as it stands, charged or given
        // back, and drifting hooked up. The host's sim charges it.
        private void ChargeWear(float delta)
        {
            if (engine != null && Car != null && delta > 0f)
                driftWear += Drifting.Wear(engine.throttleControl.Value, engine.reverserControl.Value, Car.GetForwardSpeed()) * delta;
            var train = damage != null ? damage.mechanicalPT : null;
            if (train == null || Net.IsClient)
                return;
            var change = WearPerLoss * FoulingLoss - foulingWear;
            if (change >= LeastWear)
                train.ApplyDamage(train.fullHitPoints * change);
            else if (change <= -LeastWear)
                train.RepairDamage(train.fullHitPoints * -change);
            if (Mathf.Abs(change) >= LeastWear)
                foulingWear += change;
            if (driftWear >= LeastWear)
            {
                train.ApplyDamage(train.fullHitPoints * driftWear);
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
            // A repair of the engine is the shed's work on the boiler too: the
            // tubes cleaned and the scale chipped out, for the share of the
            // engine's wear repaired.
            if (ticks >= RepairWatchFrom && engine > engineHealth + RepairRise && FoulingLoss > 0f)
            {
                var share = Mathf.Clamp01((engine - engineHealth) / Mathf.Max(1e-4f, 1f - engineHealth));
                Fouling.Clean(share);
                foulingWear *= 1f - share;
                if (share > 0.99f)
                    Log("engine repaired: tubes cleaned and the boiler descaled");
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

        /// <summary>After the injector step: what the injectors, pump, hose, or hand pump put in.</summary>
        public void AfterFeed()
        {
            var vessel = Vessel(boiler);
            if (vessel != null)
                fed += Mathf.Max(0f, vessel.mass - massAtTick);
        }

        public float MassNow => Vessel(boiler)?.mass ?? 0f;

        /// <summary>After the blowdown step, with the water before it.</summary>
        public void AfterBlowdown(float massBefore) => blown += Mathf.Max(0f, massBefore - MassNow);

        /// <summary>After the stock level step: steps the water, then shows its foam and swell in the glass, and the foam in the steam.</summary>
        public void AfterWaterLevel(float delta)
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
            }
            if (!on || vessel == null || !(delta > 0f))
            {
                fed = blown = 0f;
                boiler.waterLevelReadOut.Value = TrueLevel + Swell.Rise;
                return;
            }

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
            boiler.waterLevelReadOut.Value = TrueLevel + Water.GlassRise + Swell.Rise;
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
                FireController.Announce(PlugMeltedNotice);
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
            foulingWear = data.GetFloat(FoulingWearKey) ?? 0f;
        }

        /// <summary>Called on every stock boiler save load: applies ours now, or once the rig is made.</summary>
        public static void OnLoaded(Boiler boiler, JObject data)
        {
            if (data == null)
                return;
            if (TryGet(boiler, out var rig))
                rig.Load(data);
            else
                PendingSaves[boiler] = data;
        }

        private void Log(string text) => Main.Mod?.Logger.Log($"{Car?.ID}: {text}");

        /// <summary>Gives <paramref name="boiler"/> a rig directly, for checks outside the game.</summary>
        internal static BoilerWaterRig Create(Boiler boiler, ReciprocatingSteamEngine engine, Guard guard)
        {
            var rig = new BoilerWaterRig(null, boiler, engine, guard);
            ByBoiler[boiler] = rig;
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
