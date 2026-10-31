using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DV.Simulation.Cars;
using HarmonyLib;
using LocoSim.Implementations;
using Newtonsoft.Json.Linq;
using RealisticFiring.Core;

namespace RealisticFiring.Sim
{
    internal struct SteamPathSnapshot
    {
        public bool Active;
        // 0 is a legacy snapshot whose layout was inferred from PipeVolume.
        public int Layout;
        public double PipeVolume, ChestVolume, PipeMass, ChestMass, PipeHeat, ChestHeat;
        public float InletRate, HeaterRate, DeliveredRate, AdmissionPressure;
        public int RetentionVersion;
        public double ClosedSeconds, ClosedPressure, WallTemperature;
        public double SavedClockSeconds;
    }

    /// <summary>Owns circuit and chest inventory; the stock mass field is a view of the actual chest.</summary>
    internal sealed class EngineSteamPath
    {
        private const string SaveKey = "RealisticFiring_SteamPath_v1";
        private static readonly ConditionalWeakTable<ReciprocatingSteamEngine, EngineSteamPath> Engines =
            new ConditionalWeakTable<ReciprocatingSteamEngine, EngineSteamPath>();
        private sealed class NativeIdle
        {
            internal readonly SteamPath Model;
            internal NativeIdle(ReciprocatingSteamEngine engine) => Model = new SteamPath(
                new SteamPathSpec(RegulatorLocation.AfterSuperheater, 0, engine.steamChestVolume, engine.throttleMaxFlow));
        }
        private static readonly ConditionalWeakTable<ReciprocatingSteamEngine, NativeIdle> Saturated =
            new ConditionalWeakTable<ReciprocatingSteamEngine, NativeIdle>();
        private static readonly List<WeakReference<ReciprocatingSteamEngine>> SaturatedEngines = new List<WeakReference<ReciprocatingSteamEngine>>();
        private static readonly ConditionalWeakTable<TrainCar, EngineSteamPath> Cars =
            new ConditionalWeakTable<TrainCar, EngineSteamPath>();
        private static readonly List<WeakReference<TrainCar>> KnownCars = new List<WeakReference<TrainCar>>();
        private sealed class Saved { public SteamPathSnapshot Value; }
        private static readonly ConditionalWeakTable<ReciprocatingSteamEngine, Saved> Pending =
            new ConditionalWeakTable<ReciprocatingSteamEngine, Saved>();
        private static readonly AccessTools.FieldRef<ReciprocatingSteamEngine, float> NativeMass =
            AccessTools.FieldRefAccess<ReciprocatingSteamEngine, float>("steamChestSteamMass");

        private readonly ReciprocatingSteamEngine engine;
        private bool initialized, active;
        private bool parked;
        private double parkedMass, parkedHeat;
        private double closedSeconds, closedPressure = 1, wallTemperature;
        private bool wasClosed;
        private bool loadedBoundaryPending;
        private double loadedClockSeconds;
        internal static Func<double> ReadGameClock = GameClockSeconds;
        internal SteamPath Model { get; }
        internal float InletRate { get; private set; }
        internal float RegulatorRate { get; private set; }
        internal float HeaterRate { get; private set; }
        internal float DeliveredRate { get; private set; }
        internal bool Active => active;
        internal float AdmissionPressure { get; private set; } = 1;
        // Retain the maker's calibrated cylinder filling relation at the
        // measured chest density. This is an equivalent admission mass, not
        // permission to withdraw the boiler-side header through a shut valve.
        // Actual stroke debits are owned and bounded by SteamPath.Take.
        internal float AdmissionMass => (float)(Model.RegulatedMass * engine.steamChestVolume / Model.Spec.RegulatedVolume);

        private EngineSteamPath(ReciprocatingSteamEngine engine, SteamPathSpec spec)
        { this.engine = engine; Model = new SteamPath(spec); }

        internal static bool TryFit(string livery, float volume, float capacity, out SteamPathSpec spec)
        {
            spec = null;
            var upstream = livery == "LocoS282A" || livery == "em_lms_8f_loc_black" || livery == "em_lms_8f_loc_grey";
            var front = livery == "S464-730" || livery == "FEF-3" || livery == "S484-840A" || livery == "A.T.&S.F. 2901-A";
            if ((!upstream && !front) || !Finite(volume) || volume <= 0 || !Finite(capacity) || capacity <= 0) return false;
            var pipe = livery == "LocoS282A" ? 300 : upstream ? 120
                : livery == "S464-730" ? 600 : livery == "A.T.&S.F. 2901-A" ? 800 : 500;
            spec = new SteamPathSpec(upstream ? RegulatorLocation.BeforeSuperheater : RegulatorLocation.AfterSuperheater,
                pipe, volume, capacity);
            return true;
        }

        internal static string Attach(TrainCar car, SimulationFlow flow)
        {
            var engine = flow?.OrderedSimComps.OfType<ReciprocatingSteamEngine>().FirstOrDefault();
            if (engine != null && (car?.carLivery?.id == "LocoS060" || car?.carLivery?.id == "Jk3A"))
            {
                AttachSaturated(engine);
                return "native saturated throttle and chest; closed-circuit leakage and cooling";
            }
            if (engine == null || car?.carLivery == null || !TryFit(car.carLivery.id, engine.steamChestVolume, engine.throttleMaxFlow, out var spec))
                return null;
            var path = Attach(engine, spec);
            if (!Cars.TryGetValue(car, out _)) KnownCars.Add(new WeakReference<TrainCar>(car));
            Cars.Remove(car); Cars.Add(car, path);
            return $"experimental throttle characteristic: {spec.Regulator}, {spec.PipeVolume:F0} L inferred circuit + {spec.ChestVolume:F0} L maker chest, {spec.RegulatedVolume:F0} L regulated, {spec.MaxFlow:F2} kg/s";
        }

        internal static EngineSteamPath Attach(ReciprocatingSteamEngine engine, SteamPathSpec spec)
        {
            SteamPathSnapshot? previous = null;
            if (Engines.TryGetValue(engine, out var existing))
            {
                if (existing.Model.Spec.Regulator == spec.Regulator && existing.Model.Spec.PipeVolume == spec.PipeVolume
                    && existing.Model.Spec.ChestVolume == spec.ChestVolume && existing.Model.Spec.MaxFlow == spec.MaxFlow) return existing;
                if (existing.initialized || existing.parked) previous = existing.Snapshot();
                Engines.Remove(engine);
            }
            var path = new EngineSteamPath(engine, spec);
            Engines.Add(engine, path);
            if (previous.HasValue) path.ApplySaved(previous.Value);
            if (Pending.TryGetValue(engine, out var saved))
            {
                if (!path.ApplySaved(saved.Value)) ClearLoadedInventory(engine);
                Pending.Remove(engine);
            }
            return path;
        }

        internal static bool TryGet(ReciprocatingSteamEngine engine, out EngineSteamPath path)
        { path = null; return engine != null && Engines.TryGetValue(engine, out path); }
        internal static bool TryGet(TrainCar car, out EngineSteamPath path)
        { path = null; return car != null && Cars.TryGetValue(car, out path); }

        internal static IEnumerable<TrainCar> AllCars()
        {
            for (var i = KnownCars.Count - 1; i >= 0; i--)
            {
                if (KnownCars[i].TryGetTarget(out var car) && car != null) yield return car;
                else KnownCars.RemoveAt(i);
            }
        }

        internal static void BeginTick(ReciprocatingSteamEngine engine, float delta = 0)
        {
            if (!TryGet(engine, out var path)) { BleedSaturated(engine, delta); return; }
            if (!EngineCylinders.Works(engine))
            {
                if (path.active && path.initialized)
                {
                    // Stock owns its original chest again. Park the added
                    // circuit; compressing it all into the smaller stock
                    // volume would create a pressure jump on this setting.
                    var state = path.Model.State;
                    path.parked = true;
                    path.parkedMass = state.PipeMass; path.parkedHeat = state.PipeHeat;
                    NativeMass(engine) = (float)state.ChestMass;
                    engine.steamChestPressureReadOut.Value = (float)path.Model.ChestPressure;
                    path.active = false; path.initialized = false;
                }
                if (path.parked && !Net.IsClient && engine.throttleControl.Value <= 0 && delta > 0 && Finite(delta))
                {
                    var share = Math.Exp(-delta / path.LeakTime());
                    path.parkedMass *= share; path.parkedHeat *= share;
                }
                return;
            }
            if (Net.IsClient) { if (path.initialized) path.Show(); return; }
            if (!path.initialized)
            {
                var temperature = EngineSuperheater.TryGet(engine, out var heater)
                    ? heater.SteamTemperature(engine.intakePressure.Value) : engine.intakeTemperature.Value;
                if (!Finite(temperature)) temperature = engine.intakeTemperature.Value;
                temperature = Finite(temperature) ? Math.Max(0, Math.Min(1000, temperature)) : 100;
                if (path.parked)
                {
                    var native = NativeMass(engine);
                    var mass = Finite(native) ? Math.Max(0, native) : 0;
                    if (!path.Model.Restore(new SteamPathState { PipeMass = path.parkedMass, PipeHeat = path.parkedHeat,
                        ChestMass = mass, ChestHeat = mass * SteamPath.SpecificHeat(temperature) }))
                        path.Model.Reset(path.parkedMass + mass, temperature);
                    path.parked = false; path.parkedMass = path.parkedHeat = 0;
                }
                else path.Model.Reset(NativeMass(engine), temperature);
                path.AdmissionPressure = (float)path.Model.ChestPressure;
                path.initialized = true;
            }
            path.active = true;
            if (path.loadedBoundaryPending)
            {
                if (engine.throttleControl.Value <= 0)
                {
                    var source = engine.intakePressure.Value;
                    var temperature = engine.intakeTemperature.Value;
                    if (Finite(source) && source >= 1 && source <= 1.05 && Finite(temperature) && temperature < 99)
                        path.ConstrainColdVapor(Math.Max(0, temperature));
                    var now = ReadGameClock();
                    if (path.loadedClockSeconds > 0 && now > path.loadedClockSeconds)
                        path.AdvanceIdle(now - path.loadedClockSeconds);
                }
                path.loadedBoundaryPending = false;
            }
            path.AdvanceIdle(delta);
            path.Show();
        }

        internal void AdvanceIdle(double seconds)
        {
            if (Net.IsClient || !initialized || !Finite(seconds) || seconds < 0) return;
            if (engine.throttleControl.Value > 0)
            {
                wasClosed = false; closedSeconds = 0; closedPressure = 1;
                wallTemperature = Math.Max(0, Model.ChestTemperature);
                return;
            }
            if (!wasClosed)
            {
                closedPressure = Math.Max(Model.ChestPressure,
                    Model.Spec.Regulator == RegulatorLocation.BeforeSuperheater ? Model.PipePressure : 1);
                wallTemperature = Math.Max(0, Model.ChestTemperature);
                wasClosed = true;
            }
            closedSeconds = Math.Min(1e9, closedSeconds + seconds);
            var pressure = engine.intakePressure.Value;
            var temperature = engine.intakeTemperature.Value;
            var boundary = Finite(pressure) && pressure > 1.05
                ? SteamCylinders.SaturationTemperature(pressure)
                : Finite(temperature) ? Math.Max(0, Math.Min(100, temperature)) : 25;
            wallTemperature = boundary + (wallTemperature - boundary) * Math.Exp(-seconds / 180);
            Model.Bleed(seconds, wallTemperature, LeakTime());
            AdmissionPressure = (float)Model.ChestPressure;
        }

        private double LeakTime() => SteamPath.LeakSeconds(Model.Spec.RegulatedVolume, engine.cylinderBore, 300);

        private void ConstrainColdVapor(double temperature)
        {
            var state = Model.State; var heat = SteamPath.SpecificHeat(temperature);
            state.ChestHeat = Math.Min(state.ChestHeat, state.ChestMass * heat);
            if (Model.Spec.Regulator == RegulatorLocation.BeforeSuperheater)
                state.PipeHeat = Math.Min(state.PipeHeat, state.PipeMass * heat);
            if (Model.Restore(state)) Model.Bleed(0, temperature, LeakTime());
            wallTemperature = Math.Min(wallTemperature, temperature);
            AdmissionPressure = (float)Model.ChestPressure;
        }

        private static double GameClockSeconds()
        {
            // No wall-clock aging while the game is closed. The weather's
            // saved clock detects only in-world time skipped since this save.
            if (Main.Mod == null) return 0;
            var weather = DV.Utils.SingletonBehaviour<DV.WeatherSystem.WeatherDriver>.Instance;
            return weather && weather.manager != null ? weather.manager.DateTime.Ticks / (double)TimeSpan.TicksPerSecond : 0;
        }

        internal static void AdvanceClosedCircuits(double seconds)
        {
            if (!Finite(seconds) || seconds <= 0 || Net.IsClient) return;
            foreach (var car in AllCars())
                if (TryGet(car, out var path) && path.Active) { path.AdvanceIdle(seconds); path.Show(); }
            for (var i = SaturatedEngines.Count - 1; i >= 0; i--)
                if (SaturatedEngines[i].TryGetTarget(out var engine)) BleedSaturated(engine, seconds);
                else SaturatedEngines.RemoveAt(i);
        }

        internal static void AttachSaturated(ReciprocatingSteamEngine engine)
        {
            if (Saturated.TryGetValue(engine, out _)) return;
            Saturated.Add(engine, new NativeIdle(engine));
            SaturatedEngines.Add(new WeakReference<ReciprocatingSteamEngine>(engine));
        }

        private static void BleedSaturated(ReciprocatingSteamEngine engine, double seconds)
        {
            if (!EngineCylinders.Works(engine) || Net.IsClient || engine.throttleControl.Value > 0
                || !(seconds > 0) || !Finite(seconds) || !Saturated.TryGetValue(engine, out var idle)) return;
            var pressure = engine.intakePressure.Value;
            var temperature = Finite(pressure) && pressure > 1.05 ? SteamCylinders.SaturationTemperature(pressure)
                : Finite(engine.intakeTemperature.Value) ? Math.Max(0, Math.Min(100, engine.intakeTemperature.Value)) : 25;
            idle.Model.Reset(NativeMass(engine), temperature);
            idle.Model.Bleed(seconds, temperature, SteamPath.LeakSeconds(engine.steamChestVolume, engine.cylinderBore, 300));
            NativeMass(engine) = (float)idle.Model.State.ChestMass;
            engine.steamChestPressureReadOut.Value = (float)idle.Model.ChestPressure;
        }

        internal bool Step(float lossRate, int chuffs, float massPerChuff, float delta)
        {
            if (!EngineCylinders.Works(engine)) return false;
            if (Net.IsClient) { if (initialized) Show(); return true; }
            if (!active || !initialized) return false;
            if (!(delta > 0) || !Finite(delta)) return true;
            var source = engine.intakePressure.Value;
            if (!Finite(source) || source < 1)
            {
                // An invalid/unconnected inlet supplies nothing. Still pay for
                // steam already used by strokes and losses; never repeat the
                // preceding tick's boiler draw while retaining that steam.
                AdmissionPressure = (float)Model.ChestPressure;
                var used = Model.Take(Math.Max(0, chuffs) * Math.Max(0, massPerChuff));
                var losses = Model.Take(Math.Max(0, lossRate) * delta);
                InletRate = HeaterRate = RegulatorRate = 0;
                DeliveredRate = (float)((used.DeliveredMass + losses.DeliveredMass) / delta);
                Show();
                return true;
            }
            var cold = SteamCylinders.SaturationTemperature(source);
            var hot = EngineSuperheater.TryGet(engine, out var heater) ? heater.SteamTemperature(source) : engine.intakeTemperature.Value;
            // Retain the stock pressure sampling point and discrete chuff debit.
            // Only the inlet/pipe transfer is integrated between those events.
            AdmissionPressure = (float)Model.ChestPressure;
            var inletPressure = Model.Spec.Regulator == RegulatorLocation.BeforeSuperheater ? Model.PipePressure : Model.ChestPressure;
            var stroke = Model.Take(Math.Max(0, chuffs) * Math.Max(0, massPerChuff));
            // Retain the native discrete equality gate for a front-end chest.
            // An upstream regulator feeds a continuously integrated circuit:
            // gating its whole frame on an initially full small pipe starves
            // the chest after that pipe starts discharging within the frame.
            var opening = Model.Spec.Regulator == RegulatorLocation.AfterSuperheater
                && Math.Abs(inletPressure - source) < SteamPath.PressureTolerance ? 0 : engine.throttleControl.Value;
            var flow = Model.Step(source, cold, hot, opening, Math.Max(0, lossRate), delta);
            InletRate = (float)(flow.InletMass / delta);
            RegulatorRate = (float)(flow.RegulatorMass / delta);
            HeaterRate = (float)((flow.HeaterMass + stroke.HeaterMass) / delta);
            DeliveredRate = (float)((flow.DeliveredMass + stroke.DeliveredMass) / delta);
            Show();
            return true;
        }

        private void Show()
        {
            NativeMass(engine) = (float)Model.State.ChestMass;
            engine.steamChestPressureReadOut.Value = AdmissionPressure;
            engine.intakeFlowReadOut.Value = InletRate;
            engine.intakeFlowNormalizedReadOut.Value = InletRate / engine.throttleMaxFlow;
        }

        internal SteamPathSnapshot Snapshot()
        {
            var value = new SteamPathSnapshot
            {
                Active = active, PipeVolume = Model.Spec.PipeVolume, ChestVolume = Model.Spec.ChestVolume,
                Layout = (int)Model.Spec.Regulator + 1,
                PipeMass = Model.State.PipeMass, ChestMass = Model.State.ChestMass,
                PipeHeat = Model.State.PipeHeat, ChestHeat = Model.State.ChestHeat,
                InletRate = InletRate, HeaterRate = HeaterRate, DeliveredRate = DeliveredRate, AdmissionPressure = AdmissionPressure,
                RetentionVersion = 1, ClosedSeconds = closedSeconds, ClosedPressure = closedPressure, WallTemperature = wallTemperature,
                SavedClockSeconds = ReadGameClock(),
            };
            if (!active)
            {
                value.PipeMass = parked ? parkedMass : 0; value.PipeHeat = parked ? parkedHeat : 0;
                var native = NativeMass(engine);
                var temperature = engine.intakeTemperature.Value;
                value.ChestMass = Finite(native) ? Math.Max(0, native) : 0;
                temperature = Finite(temperature) ? Math.Max(0, Math.Min(1000, temperature)) : 100;
                value.ChestHeat = value.ChestMass * SteamPath.SpecificHeat(temperature);
            }
            return value;
        }

        internal bool Apply(SteamPathSnapshot value)
        {
            if (Layout(value) != (int)Model.Spec.Regulator || value.PipeVolume != Model.Spec.PipeVolume || value.ChestVolume != Model.Spec.ChestVolume
                || !Finite(value.InletRate) || !Finite(value.HeaterRate) || !Finite(value.DeliveredRate)
                || value.HeaterRate < 0 || value.DeliveredRate < 0
                || !Finite(value.AdmissionPressure) || value.AdmissionPressure < 1 || value.AdmissionPressure > 100
                || !ValidRetention(value)) return false;
            if (value.RetentionVersion == 0)
            { value.ClosedSeconds = 0; value.ClosedPressure = 1; value.WallTemperature = 0; }
            var state = new SteamPathState { PipeMass = value.PipeMass, ChestMass = value.ChestMass, PipeHeat = value.PipeHeat, ChestHeat = value.ChestHeat };
            if (!Model.Restore(state)) return false;
            initialized = true; active = value.Active;
            InletRate = value.InletRate; HeaterRate = value.HeaterRate; DeliveredRate = value.DeliveredRate;
            AdmissionPressure = value.AdmissionPressure;
            closedSeconds = value.ClosedSeconds; closedPressure = Math.Max(1, value.ClosedPressure);
            wallTemperature = value.RetentionVersion == 0 ? Math.Max(0, Model.ChestTemperature) : value.WallTemperature;
            wasClosed = closedSeconds > 0;
            parked = !active;
            parkedMass = parked ? value.PipeMass : 0; parkedHeat = parked ? value.PipeHeat : 0;
            if (active) Show();
            else { NativeMass(engine) = (float)value.ChestMass; initialized = false; }
            return true;
        }

        internal static void Save(ReciprocatingSteamEngine engine, JObject data)
        {
            if (data == null) return;
            if (TryGet(engine, out var path) && (path.initialized || path.parked))
                data[SaveKey] = JObject.FromObject(path.Snapshot());
            else if (Pending.TryGetValue(engine, out var saved)) data[SaveKey] = JObject.FromObject(saved.Value);
        }

        internal static void Load(ReciprocatingSteamEngine engine, JObject data)
        {
            Pending.Remove(engine);
            try
            {
                if (data?[SaveKey] is JObject token && TryLoad(engine, token)) return;
            }
            catch (Exception error) when (error is ArgumentException || error is Newtonsoft.Json.JsonException || error is OverflowException)
            { Main.Mod?.Logger.Warning("Ignoring invalid saved throttle storage: " + error.Message); }
            // A save belongs to the loaded world. Missing, corrupt or
            // incompatible data must not retain the preceding world's steam.
            // Network Apply intentionally keeps its no-mutation-on-error rule.
            ClearLoadedInventory(engine);
        }

        private static bool TryLoad(ReciprocatingSteamEngine engine, JObject token)
        {
            var value = token.ToObject<SteamPathSnapshot>();
            if (!Finite(value.InletRate) || !Finite(value.HeaterRate) || !Finite(value.DeliveredRate)
                || value.HeaterRate < 0 || value.DeliveredRate < 0
                || !Finite(value.AdmissionPressure) || value.AdmissionPressure < 1 || value.AdmissionPressure > 100
                || !ValidRetention(value)) return false;
            var layout = Layout(value);
            if (layout < 0) return false;
            var regulator = (RegulatorLocation)layout;
            // Validate the saved partition before migrating it or retaining a
            // load that arrives before the receiver is created.
            var check = new SteamPath(new SteamPathSpec(regulator, value.PipeVolume, value.ChestVolume, engine.throttleMaxFlow));
            if (!CompatibleBudget(engine, check.Spec.TotalVolume) || !check.Restore(new SteamPathState
                { PipeMass = value.PipeMass, ChestMass = value.ChestMass, PipeHeat = value.PipeHeat, ChestHeat = value.ChestHeat })) return false;
            if (TryGet(engine, out var path)) return path.ApplySaved(value);
            Pending.Add(engine, new Saved { Value = value });
            return true;
        }

        private bool ApplySaved(SteamPathSnapshot value)
        {
            if (Layout(value) != (int)Model.Spec.Regulator
                || (!CompatibleBudget(engine, value.PipeVolume + value.ChestVolume)
                    && Math.Abs(value.PipeVolume + value.ChestVolume - Model.Spec.TotalVolume) > 0.001)) return false;
            if (value.PipeVolume != Model.Spec.PipeVolume || value.ChestVolume != Model.Spec.ChestVolume)
            {
                // Shrinking a space must not compress saved steam or mix it
                // across a shut front-end regulator. Vent the displaced share;
                // an expanded space keeps its mass and starts at lower pressure.
                if (Model.Spec.Regulator == RegulatorLocation.BeforeSuperheater
                    && Math.Abs(value.PipeVolume + value.ChestVolume - Model.Spec.TotalVolume) < .001)
                {
                    var mass = value.PipeMass + value.ChestMass; var heat = value.PipeHeat + value.ChestHeat;
                    var share = Model.Spec.PipeVolume / Model.Spec.TotalVolume;
                    value.PipeMass = mass * share; value.ChestMass = mass - value.PipeMass;
                    value.PipeHeat = heat * share; value.ChestHeat = heat - value.PipeHeat;
                }
                else
                {
                    var pipeShare = value.PipeVolume > 0 ? Math.Min(1, Model.Spec.PipeVolume / value.PipeVolume) : 0;
                    var chestShare = Math.Min(1, Model.Spec.ChestVolume / value.ChestVolume);
                    value.PipeMass *= pipeShare; value.PipeHeat *= pipeShare;
                    value.ChestMass *= chestShare; value.ChestHeat *= chestShare;
                }
                value.PipeVolume = Model.Spec.PipeVolume; value.ChestVolume = Model.Spec.ChestVolume;
                value.AdmissionPressure = (float)SteamPath.Pressure(value.ChestMass, value.ChestVolume);
            }
            value.Layout = (int)Model.Spec.Regulator + 1;
            if (value.RetentionVersion == 1 && value.ClosedSeconds > 0)
            {
                CapSavedNode(ref value.ChestMass, ref value.ChestHeat, Model.Spec.ChestVolume, value);
                if (Model.Spec.Regulator == RegulatorLocation.BeforeSuperheater)
                    CapSavedNode(ref value.PipeMass, ref value.PipeHeat, Model.Spec.PipeVolume, value);
                // Also apply the saved vapor's saturation bound. The actual
                // loaded boiler boundary is checked before first admission.
                var check = new SteamPath(Model.Spec);
                if (!check.Restore(new SteamPathState { PipeMass = value.PipeMass, PipeHeat = value.PipeHeat,
                    ChestMass = value.ChestMass, ChestHeat = value.ChestHeat })) return false;
                check.Bleed(0, value.WallTemperature, LeakTime());
                value.PipeMass = check.State.PipeMass; value.PipeHeat = check.State.PipeHeat;
                value.ChestMass = check.State.ChestMass; value.ChestHeat = check.State.ChestHeat;
                value.AdmissionPressure = (float)check.ChestPressure;
            }
            if (!Apply(value)) return false;
            loadedBoundaryPending = true; loadedClockSeconds = value.SavedClockSeconds;
            return true;
        }

        private void CapSavedNode(ref double mass, ref double heat, double volume, SteamPathSnapshot value)
        {
            if (volume <= 0 || mass <= 0) return;
            var cap = SteamPath.RemainingMass(value.ClosedPressure, volume, value.ClosedSeconds, LeakTime());
            if (SteamPath.Pressure(cap, volume) <= 1.005) cap = volume / SteamPath.VolumePerKg(1);
            var share = Math.Min(1, cap / mass);
            mass *= share; heat *= share;
        }

        private static int Layout(SteamPathSnapshot value) => value.Layout == 0
            ? (int)(value.PipeVolume > 0 ? RegulatorLocation.BeforeSuperheater : RegulatorLocation.AfterSuperheater)
            : value.Layout == 1 || value.Layout == 2 ? value.Layout - 1 : -1;

        private static bool CompatibleBudget(ReciprocatingSteamEngine engine, double total) =>
            total >= engine.steamChestVolume - .001 && total <= engine.steamChestVolume * 1.75 + .001;

        private static bool ValidRetention(SteamPathSnapshot value) => value.RetentionVersion == 0
            || (value.RetentionVersion == 1 && Finite(value.ClosedSeconds) && value.ClosedSeconds >= 0 && value.ClosedSeconds <= 1e9
                && Finite(value.ClosedPressure) && value.ClosedPressure >= 1 && value.ClosedPressure <= 100
                && Finite(value.WallTemperature) && value.WallTemperature >= 0 && value.WallTemperature <= 2000
                && Finite(value.SavedClockSeconds) && value.SavedClockSeconds >= 0 && value.SavedClockSeconds <= 315537897600);

        private static void ClearLoadedInventory(ReciprocatingSteamEngine engine)
        {
            if (!TryGet(engine, out var old)) return;
            old.Model.Reset(0, 0);
            old.parked = false; old.parkedMass = old.parkedHeat = 0;
            old.active = old.initialized = false;
            old.InletRate = old.HeaterRate = old.DeliveredRate = 0;
            old.RegulatorRate = 0;
            old.wasClosed = false; old.closedSeconds = old.wallTemperature = 0; old.closedPressure = 1;
            old.loadedBoundaryPending = false; old.loadedClockSeconds = 0;
            old.AdmissionPressure = 1;
            NativeMass(engine) = 0;
            engine.steamChestPressureReadOut.Value = 1;
            engine.intakeFlowReadOut.Value = engine.intakeFlowNormalizedReadOut.Value = 0;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
