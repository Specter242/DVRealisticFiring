using System;
using LocoSim.Definitions;
using LocoSim.Implementations;
using Newtonsoft.Json.Linq;
using RealisticFiring.Core;
using RealisticFiring.Sim;

namespace RealisticFiring.AdapterCheck
{
    internal static class QjBackendChecks
    {
        internal static void Run(Action<bool, string> check)
        {
            var character = FireboxCharacters.ForLivery("S2102-870");
            var prototype = character.PrototypeGrateArea;
            check(prototype.HasValue && Math.Abs(prototype.Value - 6.8f) < 1e-5f,
                "QJ declares its 6.8 square meter physical grate independently of its visible coal mesh");
            float Area(string id, float measured) => FireboxCharacters.GrateAreaForLivery(id, measured);
            check(Math.Abs(Area("S2102-870", 0) - 6.8f) < 1e-5f,
                "QJ physical grate area is available before any interior or coal mesh loads");
            check(Math.Abs(Area("S2102-870", 1.40f * 1.95f) - 6.8f) < 1e-5f
                  && Math.Abs(Area("S2102-870", 20) - 6.8f) < 1e-5f,
                "QJ mesh measurements cannot overwrite the physical grate area");
            check(Area("LocoS282A", 2.12f) == 2.12f && Area("Jk3A", 1.43f) == 1.43f
                  && Area("unknown", 3) == 3 && Area(null, 0) == 0,
                "other engines retain their measured grate areas and unloaded fallback");
            var fire = new FireModel(new FireboxParameters(160, 146, .5f)) { GrateArea = Area("S2102-870", 2.73f) };
            check(Math.Abs(fire.AshpanCapacity - 646) < .001
                  && Math.Abs(fire.ForcedZoneRate * GrateLayout.ZoneCount - 600f * 6.8f / 3600) < 1e-5f,
                "QJ ashpan capacity and firing intensity use the physical grate instead of the rendered footprint");

            var fitted = EngineSteamPath.TryFit("S2102-870", 1200, 9, out var spec);
            check(fitted, "QJ has a characteristic throttle circuit");
            if (fitted)
            {
                check(spec.Regulator == RegulatorLocation.BeforeSuperheater && spec.PipeVolume == 500
                      && spec.ChestVolume == 1200 && spec.MaxFlow == 9,
                    "QJ regulates the superheater circuit while preserving its maker's chest and flow limit");
                var path = new SteamPath(spec);
                var charged = path.Step(17.3, 205, 400, 1, 0, 10);
                check(Math.Abs(path.Mass - charged.InletMass) < 1e-8
                      && Math.Abs(path.Heat - charged.InletHeat - charged.HeaterHeat) < .001,
                    "QJ circuit accounts for all boiler steam and carried heat while charging");
                var saved = path.State;
                var copy = new SteamPath(spec);
                check(copy.Restore(saved) && Math.Abs(copy.Mass - path.Mass) < 1e-8
                      && Math.Abs(copy.Heat - path.Heat) < .001,
                    "QJ circuit inventory can round-trip without duplicating or losing steam");
                var closed = copy.Step(17.3, 205, 400, 0, .5, 1);
                check(closed.InletMass == 0 && closed.RegulatorMass == 0 && closed.DeliveredMass > 0
                      && Math.Abs(copy.Mass + closed.DeliveredMass - path.Mass) < 1e-8,
                    "QJ can use residual steam after closure but cannot draw through its shut regulator");
                var before = copy.Mass;
                var loss = copy.Bleed(900, 205, SteamPath.LeakSeconds(spec.RegulatedVolume, .65, 300));
                check(copy.ChestPressure <= 1.00001 && copy.PipePressure <= 1.00001
                      && Math.Abs(before - copy.Mass - loss.LeakedMass - loss.CondensedMass) < 1e-8,
                    "QJ closed steam bleeds to atmosphere with all lost vapor accounted for");
                var cold = new SteamPath(spec);
                // The adapter's cold-boundary path first clamps carried heat
                // to the loaded boiler, then condenses excess vapor at t=0.
                var cooled = saved;
                cooled.PipeHeat = cooled.PipeMass * SteamPath.SpecificHeat(25);
                cooled.ChestHeat = cooled.ChestMass * SteamPath.SpecificHeat(25);
                cold.Restore(cooled);
                var condensed = cold.Bleed(0, 25, SteamPath.LeakSeconds(spec.RegulatedVolume, .65, 300));
                check(cold.ChestPressure <= 1.00001 && cold.PipePressure <= 1.00001
                      && condensed.CondensedMass > 0,
                    "QJ trapped steam cannot retain starting pressure against a cold loaded boiler");
                CheckSavedCircuit(spec, saved, check);
            }
        }

        private static void CheckSavedCircuit(SteamPathSpec spec, SteamPathState charge, Action<bool, string> check)
        {
            var settings = RealisticFiring.Main.Settings;
            var client = Net.ClientCheck;
            var clock = EngineSteamPath.ReadGameClock;
            try
            {
                RealisticFiring.Main.Settings = new Settings { RealisticCylinders = true };
                Net.ClientCheck = () => false;
                EngineSteamPath.ReadGameClock = () => 1234;
                var engine = new ReciprocatingSteamEngine(new ReciprocatingSteamEngineDefinition
                    { ID = "qj", steamChestVolume = 1200, throttleMaxFlow = 9, cylinderBore = .65f, pistonStroke = .8f });
                engine.intakePressure.SetPortReference(new Port("pressure", new PortDefinition(PortType.READONLY_OUT, PortValueType.PRESSURE, "OUT"), 17.3f));
                engine.intakeTemperature.SetPortReference(new Port("temperature", new PortDefinition(PortType.READONLY_OUT, PortValueType.TEMPERATURE, "OUT"), 400));
                engine.throttleControl.SetPortReference(new Port("throttle", new PortDefinition(PortType.READONLY_OUT, PortValueType.CONTROL, "OUT"), 1));
                var path = EngineSteamPath.Attach(engine, spec);
                EngineSteamPath.BeginTick(engine);
                path.Model.Restore(charge);
                var saved = new JObject();
                EngineSteamPath.Save(engine, saved);
                path.Model.Reset(0, 0);
                EngineSteamPath.Load(engine, JObject.Parse(saved.ToString()));
                check(Math.Abs(path.Model.State.PipeMass - charge.PipeMass) < 1e-8
                      && Math.Abs(path.Model.State.ChestMass - charge.ChestMass) < 1e-8
                      && Math.Abs(path.Model.Heat - charge.PipeHeat - charge.ChestHeat) < .001,
                    "QJ steam inventory survives the production JSON save/load path without duplication");
                EngineSteamPath.Load(engine, new JObject());
                check(path.Model.Mass == 0 && path.Model.ChestPressure == 1,
                    "a QJ save without RF steam data cannot inherit a previous world's circuit charge");
            }
            finally
            {
                RealisticFiring.Main.Settings = settings;
                Net.ClientCheck = client;
                EngineSteamPath.ReadGameClock = clock;
            }
        }
    }
}
