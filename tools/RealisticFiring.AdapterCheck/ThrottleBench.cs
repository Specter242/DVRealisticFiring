using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using LocoSim.Definitions;
using LocoSim.Implementations;
using Newtonsoft.Json;
using RealisticFiring.Core;
using RealisticFiring.Patches;
using RealisticFiring.Sim;

namespace RealisticFiring.AdapterCheck
{
    // Offline engine boundary bench. No TrainCar, Unity scene, game launch,
    // save, installation or runtime tuning change is involved.
    internal static class ThrottleBench
    {
        private sealed class Profile
        {
            public string Name, Livery;
            public float Volume, Flow, Bore, Stroke;
            public bool StockSteam, NoWetWalls;
        }

        // Installed-definition ledger, October 30. These are effective game
        // volumes, not measured prototype piping dimensions.
        private static readonly Profile[] Profiles =
        {
            new Profile { Name = "S282", Livery = "LocoS282A", Volume = 1000, Flow = 6, Bore = .61f, Stroke = .711f },
            new Profile { Name = "8F", Livery = "em_lms_8f_loc_black", Volume = 800, Flow = 5, Bore = .47f, Stroke = .711f },
            new Profile { Name = "Hudson", Livery = "S464-730", Volume = 1500, Flow = 15, Bore = .696f, Stroke = .813f },
            new Profile { Name = "844", Livery = "FEF-3", Volume = 1500, Flow = 10, Bore = .655f, Stroke = .833f },
            new Profile { Name = "S484-840", Livery = "S484-840A", Volume = 1400, Flow = 20, Bore = .645f, Stroke = .823f },
            new Profile { Name = "2901", Livery = "A.T.&S.F. 2901-A", Volume = 1200, Flow = 10, Bore = .715f, Stroke = .82f },
            new Profile { Name = "S060", Livery = "LocoS060", Volume = 200, Flow = 2, Bore = .42f, Stroke = .61f, StockSteam = true, NoWetWalls = true },
            new Profile { Name = "JK3", Livery = "Jk3A", Volume = 300, Flow = 2.4f, Bore = .65f, Stroke = .51f, StockSteam = true },
        };

        internal sealed class Sample
        {
            public double Time, ChestGaugeBar, TorqueNm, BoilerKgPerSecond, RegulatorKgPerSecond, CarriedTemperatureC;
        }

        internal sealed class Measurement
        {
            public int Case;
            public string Engine, Path;
            public float PressureAbsoluteBar, Rpm, Cutoff, Opening, SuperheatC;
            public int Hz;
            public float InitialPhase;
            public bool TemperaturePinned;
            public double SteadyChestGaugeBar, SteadyTorqueNm, BoilerDrawKg, ShutOffWorkJ, MaxClosedInletKgPerSecond;
            public double? OpenToHalfSteadyS, OpenToNinetyPercentBoilerS, ShutToHalfSteadyS, ShutToTenthSteadyS, ReopenToHalfSteadyS;
            public double? OpenToOneBarGaugeS;
            public double FinalGaugeBar, FinalTorqueNm;
            public List<Sample> Trace;
        }

        internal static int Calibration(string destination)
        {
            if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
                throw new IOException("Calibration output must be empty.");
            Directory.CreateDirectory(destination);
            RealisticFiring.Main.Settings = new Settings { RealisticCylinders = true, LogCoalDeliveries = false };
            var client = Net.ClientCheck; Net.ClientCheck = () => false;
            var harmony = new Harmony("RealisticFiring.ThrottleCalibration");
            try
            {
                foreach (var patch in new[] { typeof(CylinderAdmissionPatch), typeof(CylinderMeanTorquePatch), typeof(CylinderPistonForcePatch),
                    typeof(CylinderExhaustPatch), typeof(SuperheaterTickPatch), typeof(SteamPathChestPatch) })
                    harmony.CreateClassProcessor(patch).Patch();
                var results = new List<Measurement>(); var number = 0;
                foreach (var profile in Profiles)
                foreach (var rpm in new[] { 0f, 120f })
                foreach (var opening in new[] { .1f, .25f, .5f, 1f })
                {
                    var heat = profile.Name == "S282" ? 136 : profile.Name == "8F" ? 100 : profile.Name == "Hudson" ? 175
                        : profile.Name == "2901" ? 180 : profile.StockSteam ? 0 : 158;
                    results.Add(Measure(++number, profile, 14, rpm, .5f, opening, heat, true, calibration: true));
                    if (profile.Name == "S282")
                        results.Add(Measure(number, profile, 14, rpm, .5f, opening, heat, true, calibration: true, oldCircuit: true));
                }
                foreach (var result in results.Where(r => r.Rpm == 0))
                    if (result.FinalGaugeBar > .00001 || Math.Abs(result.FinalTorqueNm) > .001)
                        throw new Exception("A stopped, bled-down engine retains starting force: " + result.Engine);
                File.WriteAllText(Path.Combine(destination, "calibration.json"), JsonConvert.SerializeObject(results, Formatting.Indented));
                File.WriteAllText(Path.Combine(destination, "metadata.json"), JsonConvert.SerializeObject(new {
                    Source = typeof(EngineSteamPath).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                    Protocol = "Actual patched engine.Tick, 60 Hz, 14 bar absolute, 50% cutoff, each engine's nominal full-rate superheat, stationary and imposed 120 rpm. Three seconds shut preconditioning; open 30 s; shut 900 s. No train or boiler feedback.",
                    Opening = "1 bar gauge first-useful pressure, and 90% boiler gauge; null means not attained.",
                    Closing = "10% of last three seconds' mean gauge pressure, from handle closure. Final stationary torque must be zero after 900 s.",
                    Fits = "Maker effective chest and flow retained; added circuits replaced by dimensional/density estimates. S282 old-circuit comparison uses 918's 75% volume with the corrected loss model.",
                    Runs = results.Count,
                }, Formatting.Indented));
                Console.WriteLine("Throttle calibration: " + results.Count + " runs; all eight stopped engines have zero final starting torque.");
                return 0;
            }
            finally { harmony.UnpatchAll(harmony.Id); Net.ClientCheck = client; }
        }

        internal static int Run(string destination, bool quick)
        {
            if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
                throw new IOException("The bench output directory must be empty.");
            Directory.CreateDirectory(destination);
            RealisticFiring.Main.Settings = new Settings { RealisticCylinders = true, LogCoalDeliveries = false };
            var oldClient = Net.ClientCheck;
            Net.ClientCheck = () => false;
            var harmony = new Harmony("RealisticFiring.ThrottleBench");
            var results = new List<Measurement>();
            try
            {
                foreach (var patch in new[] { typeof(CylinderAdmissionPatch), typeof(CylinderMeanTorquePatch), typeof(CylinderPistonForcePatch),
                    typeof(CylinderExhaustPatch), typeof(SuperheaterTickPatch), typeof(SteamPathChestPatch) })
                    harmony.CreateClassProcessor(patch).Patch();
                var number = 0;
                foreach (var profile in quick ? Profiles.Take(1) : Profiles)
                foreach (var pressure in quick ? new[] { 14f } : new[] { 6f, 10f, 14f })
                foreach (var rpm in quick ? new[] { 120f } : new[] { 20f, 120f, 240f })
                foreach (var cutoff in quick ? new[] { .5f } : new[] { .25f, .5f })
                foreach (var opening in quick ? new[] { 1f } : new[] { .5f, 1f })
                foreach (var superheat in quick ? new[] { 150f } : new[] { 0f, 150f })
                {
                    number++;
                    foreach (var stored in new[] { false, true })
                        results.Add(Measure(number, profile, pressure, rpm, cutoff, opening, superheat, stored));
                }
                var controls = 0;
                foreach (var pair in results.GroupBy(r => r.Case).Where(p => p.First().Engine == "S060" || p.First().Engine == "JK3"))
                {
                    var a = pair.First(); var b = pair.Last();
                    if (Math.Abs(a.SteadyChestGaugeBar - b.SteadyChestGaugeBar) > 1e-6 || Math.Abs(a.SteadyTorqueNm - b.SteadyTorqueNm) > 1e-6)
                        throw new Exception("A saturated engine's unchanged opening differs between paths.");
                    controls++;
                }
                var metadata = new
                {
                    Source = typeof(EngineSteamPath).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                    CaseCount = number, RunCount = results.Count, ControlPairsChecked = controls, Hz = 60,
                    Boundary = "Actual patched ReciprocatingSteamEngine.Tick at imposed speed and constant pressure/temperature; no boiler, superheater warm-up, drivetrain or train feedback",
                    Schedule = "Three seconds shut preconditioning, then open 0-12 s; shut 12-24 s; reopen 24-36 s; shut 36-60 s. Cutoff and speed fixed.",
                    Definitions = "Effective game engine dimensions/volumes from the October 30 installed-definition ledger; maker chest retained; separate circuit estimated from element surface, tube bore, manifolds and steam density. Saturated opening unchanged; closed-throttle leakage is intentional.",
                    Temperature = "0 or 150 C over saturation as an imposed boundary; S060/JK3 remain saturated controls",
                    Metrics = "Chest crossings use each run's mean 9-12 s gauge pressure, except open90 which uses 90% of boiler gauge; null means not reached. Shut-off work is positive shaft work at imposed rpm, 12-24 s.",
                };
                File.WriteAllText(System.IO.Path.Combine(destination, "metadata.json"), JsonConvert.SerializeObject(metadata, Formatting.Indented));
                File.WriteAllText(System.IO.Path.Combine(destination, "measurements.json"), JsonConvert.SerializeObject(results, Formatting.Indented));
                Console.WriteLine($"Throttle bench: {number} cases, {results.Count} runs; {controls} unchanged-control pairs checked. Output: {destination}");
                return 0;
            }
            finally { harmony.UnpatchAll(harmony.Id); Net.ClientCheck = oldClient; }
        }

        internal static int Sensitivity(string destination, bool quick)
        {
            if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
                throw new IOException("The sensitivity output directory must be empty.");
            Directory.CreateDirectory(destination);
            RealisticFiring.Main.Settings = new Settings { RealisticCylinders = true, LogCoalDeliveries = false };
            var oldClient = Net.ClientCheck; Net.ClientCheck = () => false;
            var harmony = new Harmony("RealisticFiring.ThrottleSensitivity");
            var results = new List<Measurement>();
            try
            {
                foreach (var patch in new[] { typeof(CylinderAdmissionPatch), typeof(CylinderMeanTorquePatch), typeof(CylinderPistonForcePatch),
                    typeof(CylinderExhaustPatch), typeof(SuperheaterTickPatch), typeof(SteamPathChestPatch) })
                    harmony.CreateClassProcessor(patch).Patch();
                var number = 0;
                foreach (var profile in quick ? Profiles.Take(1) : Profiles.Where(p => p.Name == "S282" || p.Name == "8F" || p.Name == "844" || p.StockSteam))
                foreach (var condition in quick ? new[] { 0 } : new[] { 0, 1 })
                foreach (var superheat in quick ? new[] { 150f } : new[] { 0f, 150f })
                foreach (var hz in quick ? new[] { 60 } : new[] { 10, 30, 60, 144 })
                foreach (var phase in quick ? new[] { 0f } : Enumerable.Range(0, 8).Select(i => i / 8f))
                {
                    number++;
                    var pressure = condition == 0 ? 6f : 14f;
                    var rpm = condition == 0 ? 240f : 120f;
                    var cutoff = condition == 0 ? .25f : .5f;
                    results.Add(Measure(number, profile, pressure, rpm, cutoff, 1, superheat, false, hz, phase));
                    results.Add(Measure(number, profile, pressure, rpm, cutoff, 1, superheat, true, hz, phase));
                    if (!profile.StockSteam)
                        results.Add(Measure(number, profile, pressure, rpm, cutoff, 1, superheat, true, hz, phase, true));
                }
                foreach (var pair in results.Where(r => r.Engine == "S060" || r.Engine == "JK3").GroupBy(r => r.Case))
                {
                    var a = pair.First(); var b = pair.Last();
                    if (a.SteadyChestGaugeBar != b.SteadyChestGaugeBar || a.SteadyTorqueNm != b.SteadyTorqueNm)
                        throw new Exception("Sensitivity changed a saturated opening control.");
                }
                File.WriteAllText(Path.Combine(destination, "sensitivity.json"), JsonConvert.SerializeObject(results, Formatting.Indented));
                File.WriteAllText(Path.Combine(destination, "metadata.json"), JsonConvert.SerializeObject(new {
                    Source = typeof(EngineSteamPath).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                    Cases = number, Runs = results.Count,
                    Boundary = "Patched game engine; fixed speed, inlet pressure and temperature. Not a live train or full-energy calculation.",
                    Phase = "Initial crank rotation and previous rotation set together; 0 to 7/8 revolution in eighths.",
                    TemperaturePin = "Diagnostic only: preserve each node mass, replace its sensible heat with the constant inlet temperature before each engine tick. No production parameter is changed.",
                    Schedule = "Three seconds shut preconditioning, then open 0-12 s; shut 12-24 s; reopen 24-36 s; shut 36-60 s.",
                }, Formatting.Indented));
                Console.WriteLine($"Throttle sensitivity: {number} phase/step cases, {results.Count} runs; saturated opening controls agree.");
                return 0;
            }
            finally { harmony.UnpatchAll(harmony.Id); Net.ClientCheck = oldClient; }
        }

        private static Measurement Measure(int number, Profile p, float pressure, float rpm, float cutoff, float opening, float superheat, bool storage,
            int hz = 60, float initialPhase = 0, bool pinTemperature = false, bool calibration = false, bool oldCircuit = false)
        {
            var def = new ReciprocatingSteamEngineDefinition
            {
                ID = "bench", numCylinders = 2, cylinderBore = p.Bore, pistonStroke = p.Stroke,
                minCutoff = .05f, maxCutoff = .9f, throttleMaxFlow = p.Flow, steamChestVolume = p.Volume,
            };
            var engine = new ReciprocatingSteamEngine(def);
            engine.SetGameParams(new SimGameParams(false, false, false, 1f, 1f));
            AccessTools.Field(typeof(ReciprocatingSteamEngine), "crankRotation").SetValue(engine, initialPhase);
            AccessTools.Field(typeof(ReciprocatingSteamEngine), "prevCrankRotation").SetValue(engine, initialPhase);
            Port Wire(PortReferenceDefinition reference, PortValueType type, float value)
            {
                var port = new Port(reference.ID, new PortDefinition(PortType.READONLY_OUT, type, "OUT"), value);
                engine.GetAllPortReferences().Single(r => r.id == "bench." + reference.ID).SetPortReference(port);
                return port;
            }
            var throttle = Wire(def.throttleControl, PortValueType.CONTROL, opening);
            Wire(def.reverserControl, PortValueType.CONTROL, (cutoff - .05f) / .85f);
            Wire(def.cylinderCockControl, PortValueType.CONTROL, 0);
            Wire(def.intakePressure, PortValueType.PRESSURE, pressure);
            Wire(def.intakeTemperature, PortValueType.TEMPERATURE, SteamCylinders.SaturationTemperature(pressure) + (p.StockSteam ? 0 : superheat));
            Wire(def.intakeQuality, PortValueType.GENERIC, 1);
            Wire(def.crankRpm, PortValueType.RPM, rpm);
            Wire(def.lubricationNormalized, PortValueType.OIL, 1);
            if (p.StockSteam)
                ((ConditionalWeakTable<ReciprocatingSteamEngine, object>)AccessTools.Field(typeof(EngineCylinders), "StockSteamEngines").GetValue(null))
                    .Add(engine, new object());
            if (p.StockSteam && storage) EngineSteamPath.AttachSaturated(engine);
            if (p.NoWetWalls) EngineCylinders.Of(engine).Model.WallCondensation = false;
            var active = storage && EngineSteamPath.TryFit(p.Livery, p.Volume, p.Flow, out _);
            if (active)
            {
                EngineSteamPath.TryFit(p.Livery, p.Volume, p.Flow, out var fit);
                if (oldCircuit) fit = new SteamPathSpec(fit.Regulator, p.Volume * .75, p.Volume, p.Flow);
                EngineSteamPath.Attach(engine, fit);
            }
            var torque = engine.GetAllPorts().Single(o => o.id == "bench." + def.torqueOut.ID);
            var samples = new List<Sample>(60 * hz);
            var dt = 1f / hz;
            // Before a handle movement, a front-end engine's hot circuit is
            // boiler-side and charged. An upstream circuit stays drained.
            throttle.Value = 0;
            for (var i = 0; i < 3 * hz; i++) engine.Tick(dt);
            AccessTools.Field(typeof(ReciprocatingSteamEngine), "crankRotation").SetValue(engine, initialPhase);
            AccessTools.Field(typeof(ReciprocatingSteamEngine), "prevCrankRotation").SetValue(engine, initialPhase);
            var openedUntil = calibration ? 30 : 12;
            var closedUntil = calibration ? 930 : 24;
            for (var i = 0; i < (calibration ? closedUntil : 60) * hz; i++)
            {
                var t = (double)i / hz;
                throttle.Value = t < openedUntil || (!calibration && t >= 24 && t < 36) ? opening : 0;
                if (active && pinTemperature && EngineSteamPath.TryGet(engine, out var pinned))
                {
                    var state = pinned.Model.State;
                    var heat = SteamPath.SpecificHeat(SteamCylinders.SaturationTemperature(pressure) + superheat);
                    state.PipeHeat = state.PipeMass * heat; state.ChestHeat = state.ChestMass * heat;
                    if (!pinned.Model.Restore(state)) throw new Exception("Temperature-pin state rejected.");
                }
                engine.Tick(dt);
                var sample = new Sample
                {
                    Time = (double)(i + 1) / hz,
                    ChestGaugeBar = Math.Max(0, engine.steamChestPressureReadOut.Value - 1),
                    TorqueNm = torque.Value, BoilerKgPerSecond = engine.intakeFlowReadOut.Value,
                    RegulatorKgPerSecond = active && EngineSteamPath.TryGet(engine, out var regulated)
                        ? regulated.RegulatorRate : engine.intakeFlowReadOut.Value,
                    CarriedTemperatureC = active && EngineSteamPath.TryGet(engine, out var carried) ? carried.Model.ChestTemperature : engine.intakeTemperature.Value,
                };
                if (!Finite(sample.ChestGaugeBar) || !Finite(sample.TorqueNm) || !Finite(sample.BoilerKgPerSecond)
                    || (active && (!EngineSteamPath.TryGet(engine, out var path) || path.Model.Mass < 0 || !Finite(path.Model.Mass))))
                    throw new Exception("Nonfinite or negative state in throttle bench case " + number);
                samples.Add(sample);
            }
            var plateau = samples.Where(s => s.Time > openedUntil - 3 && s.Time <= openedUntil).ToArray();
            var steady = plateau.Average(s => s.ChestGaugeBar);
            double? Crossing(double from, double to, Func<Sample, bool> threshold)
            {
                var hit = samples.FirstOrDefault(s => s.Time > from && s.Time <= to && threshold(s));
                return hit == null ? (double?)null : hit.Time - from;
            }
            var closed = samples.Where(s => s.Time > openedUntil && s.Time <= closedUntil).ToArray();
            var shut = samples.Where(s => (s.Time > openedUntil && s.Time <= closedUntil) || (!calibration && s.Time > 36)).ToArray();
            if (shut.Any(s => Math.Abs(s.RegulatorKgPerSecond) > 1e-6))
                throw new Exception("Steam crosses a shut regulator in case " + number);
            var maxClosedInlet = shut.Where(s => s.Time > openedUntil + .2 && (s.Time <= closedUntil || s.Time > 36.2)).Max(s => Math.Abs(s.BoilerKgPerSecond));
            if (maxClosedInlet > 1e-6) throw new Exception("A shut throttle's settled circuit still draws boiler steam in case " + number);
            return new Measurement
            {
                Case = number, Engine = p.Name, Path = oldCircuit ? "918 circuit" : storage ? "current" : "prior-chest", PressureAbsoluteBar = pressure,
                Rpm = rpm, Cutoff = cutoff, Opening = opening, SuperheatC = p.StockSteam ? 0 : superheat,
                Hz = hz, InitialPhase = initialPhase, TemperaturePinned = pinTemperature,
                SteadyChestGaugeBar = steady, SteadyTorqueNm = plateau.Average(s => s.TorqueNm),
                BoilerDrawKg = samples.Sum(s => s.BoilerKgPerSecond * dt),
                ShutOffWorkJ = closed.Sum(s => Math.Max(0, s.TorqueNm) * (rpm * 2 * Math.PI / 60) * dt),
                MaxClosedInletKgPerSecond = maxClosedInlet,
                OpenToHalfSteadyS = steady > .01 ? Crossing(0, openedUntil, s => s.ChestGaugeBar >= .5 * steady) : null,
                OpenToNinetyPercentBoilerS = Crossing(0, openedUntil, s => s.ChestGaugeBar >= .9 * (pressure - 1)),
                OpenToOneBarGaugeS = Crossing(0, openedUntil, s => s.ChestGaugeBar >= 1),
                ShutToHalfSteadyS = steady > .01 ? Crossing(openedUntil, closedUntil, s => s.ChestGaugeBar <= .5 * steady) : null,
                ShutToTenthSteadyS = steady > .01 ? Crossing(openedUntil, closedUntil, s => s.ChestGaugeBar <= .1 * steady) : null,
                FinalGaugeBar = samples.Last().ChestGaugeBar, FinalTorqueNm = samples.Last().TorqueNm,
                ReopenToHalfSteadyS = steady > .01 ? Crossing(24, 36, s => s.ChestGaugeBar >= .5 * steady) : null,
                Trace = initialPhase == 0 && hz == 60 && pressure == 14 && rpm == 120 && cutoff == .5f && opening == 1 && (superheat == 150 || p.StockSteam)
                    ? samples.Where((s, i) => i % 12 == 11).ToList() : null,
            };
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
