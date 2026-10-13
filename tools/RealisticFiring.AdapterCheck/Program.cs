using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LocoSim.Definitions;
using LocoSim.Implementations;
using Newtonsoft.Json.Linq;
using RealisticFiring.Core;
using RealisticFiring.Patches;
using RealisticFiring.Sim;

namespace RealisticFiring.AdapterCheck
{
 internal static class Program
    {
        private const string Id = "firebox";

        private static int failures;

        private static int Main(string[] args)
        {
            RealisticFiring.Main.Settings = new Settings { LogCoalDeliveries = false };
            var gameParams = new SimGameParams(false, false, false, 1f, 1f);
            var def = new FireboxDefinition { ID = Id };

            var stock = new Firebox(def);
            var mine = Create(def, gameParams);
            Check(Describe(stock) == Describe(mine),
                $"same ports, types, defaults, and port references ({stock.GetAllPorts().Count} ports, {stock.GetAllPortReferences().Count} references)");
            Check(stock.id == mine.id && mine.HasSaveData, "same component ID, with save data");

            // Add-and-reset delivery, as FireboxSimController.TransferCoal and CCL's stoker write it.
            foreach (var c in new SimComponent[] { stock, mine })
            {
                P(c, "COAL_CONTROL_EXT_IN").Value += 7.5f;
                P(c, "COAL_CONTROL_EXT_IN").Value += 100f;
            }
            Check(P(mine, "COAL_CONTROL_EXT_IN").Value == 0f, "delivery port resets to 0");
            Check(P(mine, "COAL_LEVEL").Value == P(stock, "COAL_LEVEL").Value,
                $"overfilled coal level matches stock ({P(mine, "COAL_LEVEL").Value} kg)");
            Check(mine.LastDeliveryMass > 0f && mine.LastDeliveryMass < 100f,
                $"the last delivery shows what landed, not what was sent ({mine.LastDeliveryMass:F1} of 100 kg)");
            foreach (var c in new SimComponent[] { stock, mine })
                P(c, "COAL_CONTROL_EXT_IN").Value += -20f;
            Check(Math.Abs(P(mine, "COAL_LEVEL").Value - P(stock, "COAL_LEVEL").Value) < 1e-3f, "negative delivery removes coal like stock");

            // A steady draft, as SteamExhaust would provide.
            var air = new Port("exhaust", new PortDefinition(PortType.READONLY_OUT, PortValueType.MASS_RATE, "AIR_FLOW"), 4f);
            mine.GetAllPortReferences().Single(r => r.id == $"{Id}.AIR_FLOW").SetPortReference(air);

            P(mine, "IGNITION_EXT_IN").ExternalValueUpdate(1f);
            mine.Tick(0.02f);
            Check(P(mine, "FIRE_ON").Value == 1f && P(mine, "IGNITION_EXT_IN").Value == 0f, "ignition request lights the fire and resets");
            for (var i = 0; i < 500; i++)
                mine.Tick(0.02f);
            Check(P(mine, "COAL_ENV_DAMAGE_METER").Value > 0f, $"environmental damage meter accumulates ({P(mine, "COAL_ENV_DAMAGE_METER").Value:F2} kg)");
            Check(P(mine, "TEMPERATURE").Value > 100f && P(mine, "HEAT").Value > 0f, "fire heats");

            // The fire-dump lever (COAL_DUMP_CONTROL, 0.5 at center) doubles as the rocking grate lever.
            var lever = new Port("coalDump", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"), 0.5f);
            var grate = Create(def, gameParams);
            grate.GetAllPortReferences().Single(r => r.id == $"{Id}.COAL_DUMP_CONTROL").SetPortReference(lever);
            grate.GetAllPortReferences().Single(r => r.id == $"{Id}.AIR_FLOW").SetPortReference(air);
            grate.SetSaveStateData(new JObject { ["fireOn"] = true, ["coalMass"] = 60f });
            Run(grate, 60f);
            var ashBeforeRock = grate.Model.AshMass;
            var dumpedBeforeRock = grate.Model.CoalDumped;
            lever.Value = 0f;
            Run(grate, 0.3f);
            lever.Value = 0.5f;
            Run(grate, 0.3f);
            Check(grate.Model.AshMass < 0.6f * ashBeforeRock && grate.Model.CoalDumped == dumpedBeforeRock,
                $"a quick rock of the dump lever shakes the grate (ash {ashBeforeRock:F2} -> {grate.Model.AshMass:F2} kg) without dumping");
            P(grate, "COAL_CONTROL_EXT_IN").Value += 60f;
            var dumpedBeforeHold = grate.Model.CoalDumped;
            lever.Value = 0f;
            Run(grate, 3f);
            lever.Value = 0.5f;
            Check(grate.Model.CoalDumped - dumpedBeforeHold > 5f,
                $"holding the dump lever over dumps the fire ({grate.Model.CoalDumped - dumpedBeforeHold:F1} kg dumped)");

            var save = mine.GetSaveStateData();
            Check(save.Value<bool>("fireOn") && save["coalMass"] != null && save["coalEnvDmg"] != null && save["rfZones"] is JArray
                  && save["rfFireman"] == null,
                "save has the stock keys plus rfZones (and no fireman's mode while he is off)");
            var stockReader = new Firebox(def);
            stockReader.SetGameParams(gameParams);
            stockReader.SetSaveStateData(save);
            stockReader.Tick(0.02f);
            Check(Math.Abs(P(stockReader, "COAL_LEVEL").Value - save.Value<float>("coalMass")) < 0.05f && P(stockReader, "FIRE_ON").Value == 1f,
                "the stock firebox loads the mod's save");

            var copy = Create(def, gameParams);
            copy.SetSaveStateData(save);
            Check(P(copy, "COAL_LEVEL").Value == P(mine, "COAL_LEVEL").Value && P(copy, "FIRE_ON").Value == 1f
                  && save["rfZones"].ToString() == copy.GetSaveStateData()["rfZones"].ToString(),
                "the mod loads its own save, zones included");

            var fromStock = Create(def, gameParams);
            fromStock.SetSaveStateData(new JObject { ["fireOn"] = true, ["coalMass"] = 36f, ["coalEnvDmg"] = 12f });
            Check(P(fromStock, "COAL_LEVEL").Value == 36f && P(fromStock, "FIRE_ON").Value == 1f
                  && P(fromStock, "COAL_ENV_DAMAGE_METER").Value == 12f, "the mod loads a stock save");

            // Door air through the port FireboxSimController names.
            var closedDoorAir = mine.Model.DoorAirFlow;
            var door = new Port("door", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"));
            mine.AttachDoorPort(door);
            door.Value = 1f;
            mine.Tick(0.02f);
            Check(mine.DoorOpening == 1f && mine.Model.DoorAirFlow > 5f * closedDoorAir,
                $"door port feeds air above the fire ({closedDoorAir:F2} -> {mine.Model.DoorAirFlow:F2} kg/s)");

            P(mine, "EXTINGUISH_EXT_IN").ExternalValueUpdate(1f);
            mine.Tick(0.02f);
            Check(P(mine, "FIRE_ON").Value == 0f && P(mine, "EXTINGUISH_EXT_IN").Value == 0f, "extinguish request puts the fire out and resets");

            // Boiler-broken shutdown fires on the referenced port's change.
            var broken = new Port("boiler", new PortDefinition(PortType.READONLY_OUT, PortValueType.STATE, "IS_BROKEN"));
            var lit = Create(def, gameParams);
            lit.GetAllPortReferences().Single(r => r.id == $"{Id}.BOILER_BROKEN_STATE").SetPortReference(broken);
            lit.InitializationAfterConnecting();
            lit.SetSaveStateData(new JObject { ["fireOn"] = true, ["coalMass"] = 20f });
            broken.Value = 1f;
            Check(P(lit, "FIRE_ON").Value == 0f && !lit.Model.FireOn, "a broken boiler puts the fire out");

            // The loco start-up (StartupHelper.Startup) fills the firebox with
            // TransferCoal(9999999) and lights it; StartupPatch then lays the
            // ready fire.
            var started = Create(def, gameParams);
            started.SetSaveStateData(new JObject { ["fireOn"] = true, ["coalMass"] = 10f });
            P(started, "COAL_CONTROL_EXT_IN").Value += 9999999f / 4f;
            P(started, "IGNITION_EXT_IN").ExternalValueUpdate(1f);
            started.PrepareFire();
            started.Tick(0.02f);
            Check(started.Model.FireOn && P(started, "FIRE_ON").Value == 1f
                  && started.Model.Zones.All(z => z.Fresh == 0f && z.Burning > 0f)
                  && Math.Abs(P(started, "COAL_LEVEL").Value - started.Model.Parameters.MaxCoalCapacity) < 0.5f
                  && started.LastDeliveryMass == 0f,
                $"start-up lays a full, burning fire ({P(started, "COAL_LEVEL").Value:F1} kg), not green coal");

            // A stoker's per-tick trickle is continuous feed, not a shovelful.
            var stoked = Create(def, gameParams);
            stoked.SetSaveStateData(new JObject { ["fireOn"] = true, ["coalMass"] = 20f });
            for (var i = 0; i < 10; i++)
                P(stoked, "COAL_CONTROL_EXT_IN").Value += 0.2f;
            Check(stoked.LastDeliveryMass == 0f && Math.Abs(stoked.TakeContinuousFeed() - 2f) < 1e-3f
                  && Math.Abs(P(stoked, "COAL_LEVEL").Value - 22f) < 1e-3f,
                "a stoker's trickle feeds the fire as continuous feed, not as shovelfuls");

            CheckInjector(gameParams);
            CheckInjectorSteamValve(gameParams);
            CheckJk3Injectors(gameParams);
            CheckJk3Firebox(gameParams);
            CheckBoilerWater();
            CheckStoker(gameParams, def);
            CheckOilBurner(gameParams);
            CheckClientMirror(gameParams, def);
            CheckBooster();
            CheckCylinders(gameParams);
            CheckBlower(gameParams);

            CheckControlSteps();
            CheckShovelLoads();
            CheckFiremanHand();
            CheckPatchTargets();
            CheckCarPoolGuard();
            CheckPurchaseGuard();
            CheckFireIronLoading();
            if (args.Length > 0 && File.Exists(args[0]))
                CheckMultiplayerCabHooks(args[0]);
            Console.WriteLine(failures == 0 ? "All adapter checks passed" : $"{failures} adapter checks failed");
            return failures == 0 ? 0 : 1;
        }

        private static void Run(SimComponent component, float seconds)
        {
            for (var t = 0f; t < seconds; t += 0.02f)
                component.Tick(0.02f);
        }

        private static void CheckMultiplayerCabHooks(string path)
        {
            const string harmonyId = "RealisticFiring.AdapterCheck.MultiplayerCab";
            var modFolder = Path.GetDirectoryName(path);
            var gameFolder = Path.GetDirectoryName(Path.GetDirectoryName(modFolder));
            // Resolve game/mod dependencies without adding the game's
            // mscorlib to MONO_PATH and replacing Mono's own core library.
            ResolveEventHandler resolve = (_, request) =>
            {
                var name = new AssemblyName(request.Name).Name;
                if (name == "mscorlib" || name == "netstandard" || name.StartsWith("System", StringComparison.Ordinal))
                    return null;
                foreach (var folder in new[] { modFolder, Path.Combine(gameFolder, "DerailValley_Data", "Managed") })
                {
                    var file = Path.Combine(folder, name + ".dll");
                    if (File.Exists(file))
                        return Assembly.LoadFrom(file);
                }
                return null;
            };
            AppDomain.CurrentDomain.AssemblyResolve += resolve;
            try
            {
                var assembly = Assembly.LoadFrom(path);
                var type = assembly.GetType("Multiplayer.Components.Networking.Train.NetworkedTrainCar", throwOnError: true);
                var targets = Multiplayer.CabControlsCompatibility.PatchTargets(type);
                var helpers = targets.SelectMany(method => Multiplayer.CabControlsCompatibility.Transpile(
                    HarmonyLib.PatchProcessor.GetOriginalInstructions(method)))
                    .Select(instruction => instruction.operand as MethodInfo)
                    .Where(method => method?.DeclaringType == typeof(Multiplayer.CabControlsCompatibility)).ToArray();
                Check(targets.Length == 4 && helpers.Count(method => method.Name == "Discover") == 1
                    && helpers.Count(method => method.Name == "ResolvePort") == 2 && helpers.Count(method => method.Name == "Kind") == 2,
                    "installed Multiplayer cab hookup uses feeder discovery, both port reads, and null-safe scroll logging");
                var warnings = new System.Collections.Generic.List<string>();
                Multiplayer.CabControlsCompatibility.Install(harmonyId, _ => { }, warnings.Add);
                var load = HarmonyLib.AccessTools.DeclaredMethod(type, "OnTrainCarInteriorLoaded");
                Check(warnings.Count == 0 && targets.Concat(new MethodBase[] { load }).All(method =>
                    HarmonyLib.Harmony.GetPatchInfo(method)?.Owners.Contains(harmonyId) == true),
                    "all five Multiplayer cab patches compile and install against the installed assembly"
                    + (warnings.Count > 0 ? ": " + string.Join("; ", warnings) : ""));
                // The unload notification exits before touching a Unity
                // component; invoking the patched original proves that path.
                load.Invoke(System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type), new object[] { null });
                Check(true, "a null interior notification skips Multiplayer's load coroutine");
            }
            catch (Exception e)
            {
                Check(false, "Multiplayer cab compatibility: " + e.GetBaseException().Message);
            }
            finally
            {
                new HarmonyLib.Harmony(harmonyId).UnpatchAll(harmonyId);
                AppDomain.CurrentDomain.AssemblyResolve -= resolve;
            }
        }

        private static void CheckJk3Firebox(SimGameParams gameParams)
        {
            // Lilla and Cruzer's JK3 1.0.1 release bundle (2026-10-10).
            // Keep its smaller firebox and tender consumption, rather than
            // substituting the S060's or S282's figures.
            var def = new FireboxDefinition
            {
                ID = Id,
                maxCoalCapacity = 70f,
                burnTime = 200f,
                efficiencyAtMaxCombustion = 0.5f,
                coalDumpRate = 4f,
                combustionRateSmoothTime = 5f,
                temperatureSmoothTime = 15f,
                startupMaxPressure = 13f,
            };
            var stock = new Firebox(def);
            var fire = Create(def, gameParams);
            Check(FireboxRegistry.SupportedLiveries.Contains("Jk3A") && !FireboxRegistry.SupportedLiveries.Contains("Jk3B"),
                "the JK3 engine gets a realistic firebox; its tender does not");
            Check(Describe(stock) == Describe(fire), "the JK3 firebox preserves its stock ports and defaults");
            Check(fire.Model.Parameters.MaxCoalCapacity == 70f && fire.Model.Parameters.BurnTime == 200f,
                "the JK3 keeps its 70 kg firebox and 200 s burn time");

            fire.SetSaveStateData(new JObject { ["fireOn"] = true, ["coalMass"] = 30f, ["coalEnvDmg"] = 12f });
            var pileMass = ShovelSize.PileCapacity(3f, ShovelSize.StandardShovelChunks, 1.8f);
            P(fire, "COAL_CONTROL_EXT_IN").Value += pileMass / 1.8f;
            Check(Math.Abs(P(fire, "COAL_LEVEL").Value - 33f) < 1e-4f && P(fire, "COAL_CONTROL_EXT_IN").Value == 0f,
                "a JK3 shovelful takes 5.4 tender kg and lands 3 kg on the grate");

            var saved = fire.GetSaveStateData();
            stock.SetSaveStateData(saved);
            Check(stock.GetSaveStateData().Value<float>("coalMass") == 33f
                  && stock.GetSaveStateData().Value<bool>("fireOn")
                  && stock.GetSaveStateData().Value<float>("coalEnvDmg") == 12f,
                "the stock JK3 firebox can load the realistic fire's coal, ignition, and damage state");

            var character = FireboxCharacters.ForLivery("Jk3A");
            var jk3 = new FireModel(fire.Model.Parameters) { Character = character.Grate, GrateArea = 1.430f };
            var smallSwitcher = new FireModel(fire.Model.Parameters) { Character = FireboxCharacters.ForLivery("LocoS060").Grate, GrateArea = 1.430f };
            var typical = new FireModel(fire.Model.Parameters) { GrateArea = 1.430f };
            float Pull(FireModel model) => model.DraftWeight(GrateZone.Front) / model.DraftWeight(GrateZone.UnderDoor);
            Check(Pull(smallSwitcher) < Pull(jk3) && Pull(jk3) < Pull(typical) && !character.PrototypeSlope.HasValue,
                $"the level JK3 grate draws moderately forward (front/back {Pull(jk3):F2}, S060 {Pull(smallSwitcher):F2}, typical {Pull(typical):F2})");

            float FreshRound(FireModel model)
            {
                model.Restore(fireOn: true, coalMass: 35f);
                var inputs = new FireInputs { AirFlow = 0.5f * model.Parameters.MaxCombustionRate * FireModel.AirPerCoal,
                    StartupMultiplier = 1f, BoilerTemperature = 180f };
                for (var t = 0f; t < 60f; t += 0.02f)
                    model.Step(0.02f, inputs);
                model.AddCoalEvenly(17.5f);
                for (var t = 0f; t < 10f; t += 0.02f)
                    model.Step(0.02f, inputs);
                return model.SootDensity;
            }
            var jk3Smoke = FreshRound(jk3);
            var switcherSmoke = FreshRound(smallSwitcher);
            var typicalSmoke = FreshRound(typical);
            Check(jk3Smoke > typicalSmoke && jk3Smoke < switcherSmoke,
                $"a fresh JK3 round smokes more than the typical box, less than the S060 profile (soot {jk3Smoke:F2}, typical {typicalSmoke:F2}, S060 {switcherSmoke:F2})");
        }

        // The realistic injector against a stock boiler with the S282's figures.
        private static void CheckInjector(SimGameParams gameParams)
        {
            var lever = new Port("injector", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"));
            var mechanical = new Port("axlePump", new PortDefinition(PortType.OUT, PortValueType.CONTROL, "OUT"));
            var tenderWater = new Port("tender", new PortDefinition(PortType.READONLY_OUT, PortValueType.WATER, "AMOUNT"), 40000f);
            var consumed = new Port("tender", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.WATER, "CONSUME_EXT_IN"));

            Boiler NewBoiler(float pressure, Port control)
            {
                var def = new BoilerDefinition
                {
                    ID = "boiler",
                    diameter = 1.7f,
                    length = 8.8f,
                    capacityMultiplier = 0.85f,
                    maxInjectorRate = 20f,
                    waterConsumptionMultiplier = 4f,
                    safetyValveOpeningPressure = 15f,
                    safetyValveClosingPressure = 14.8f,
                    spawnPressure = pressure,
                    spawnWaterLevel = 9000f,
                };
                var boiler = (Boiler)def.InstantiateImplementation();
                boiler.SetGameParams(gameParams);
                boiler.GetAllPortReferences().Single(r => r.id == "boiler.INJECTOR").SetPortReference(control);
                boiler.GetAllPortReferences().Single(r => r.id == "boiler.WATER").SetPortReference(tenderWater);
                boiler.GetAllPortReferences().Single(r => r.id == "boiler.WATER_CONSUMPTION").SetPortReference(consumed);
                return boiler;
            }

            // Runs the rig, collecting what it reports taken from the tender.
            float Run(InjectorRig rig, float seconds)
            {
                var taken = 0f;
                for (var t = 0f; t < seconds; t += 0.02f)
                {
                    consumed.Value = 0f;
                    rig.Simulate(0.02f);
                    taken += consumed.Value;
                    rig.BoilerVessel.Update();
                }
                return taken;
            }

            Check(!InjectorRig.TryCreate(null, NewBoiler(12f, mechanical), out var mechanicalNote) && mechanicalNote.Contains("stock"),
                "an injector driven by another component's output keeps the stock injector");
            Check(InjectorRig.TryCreate(null, NewBoiler(12f, lever), out var leverNote) && leverNote.Contains("realistic"),
                "an injector worked by a cab control gets the realistic injector");

            var hot = NewBoiler(12f, lever);
            InjectorRig.TryCreate(null, hot, out _);
            InjectorRig.TryGet(hot, out var rig);
            var massBefore = rig.BoilerVessel.mass;
            var pressureBefore = rig.BoilerVessel.pressure;
            lever.Value = 0.1f;
            var spilled = Run(rig, 0.5f);
            Check(rig.Flow.State == InjectorState.Priming && spilled > 0f && rig.BoilerVessel.mass <= massBefore,
                $"cracked at {pressureBefore:F0} bar it spills while priming ({spilled:F0} tender units in 0.5 s, nothing into the boiler)");
            Run(rig, 1.5f);
            lever.Value = 1f;
            var drawn = Run(rig, 3f);
            Check(rig.Flow.State == InjectorState.Working && rig.BoilerVessel.mass > massBefore + 10f
                  && rig.Flow.FeedTemperature > 55f && rig.Flow.FeedTemperature < 95f && drawn > 0f,
                $"primed and opened, it feeds the boiler ({rig.BoilerVessel.mass - massBefore:F0} kg in, {rig.Flow.WaterIn:F1} kg/s at {rig.Flow.FeedTemperature:F0} C, " +
                $"steam {rig.Flow.SteamUsed:F2} kg/s, pressure {pressureBefore:F1} -> {rig.BoilerVessel.pressure:F1} bar)");

            // An engine set up with its injector open to feed, as a save loads: it carries on.
            var loaded = NewBoiler(12f, lever);
            InjectorRig.TryCreate(null, loaded, out _);
            InjectorRig.TryGet(loaded, out var loadedRig);
            var loadedMass = loadedRig.BoilerVessel.mass;
            Run(loadedRig, 2f);
            Check(loadedRig.Flow.State == InjectorState.Working && loadedRig.Flow.OverflowSteam == 0f && loadedRig.BoilerVessel.mass > loadedMass,
                $"loaded with its injector open, it carries on feeding ({loadedRig.BoilerVessel.mass - loadedMass:F0} kg in 2 s)");

            var slammed = NewBoiler(12f, lever);
            InjectorRig.TryCreate(null, slammed, out _);
            InjectorRig.TryGet(slammed, out var slammedRig);
            slammedRig.EndResume();
            var slammedMass = slammedRig.BoilerVessel.mass;
            var slammedPressure = slammedRig.BoilerVessel.pressure;
            Run(slammedRig, 2f);
            Check(slammedRig.Flow.State == InjectorState.Broken && slammedRig.BoilerVessel.mass < slammedMass
                  && slammedRig.Flow.OverflowSteam > 0f && slammedRig.BoilerVessel.pressure < slammedPressure,
                $"opened wide before it primes, the jet breaks and blows boiler steam ({slammedMass - slammedRig.BoilerVessel.mass:F1} kg in 2 s, " +
                $"injector at {slammedRig.Flow.InjectorTemperature:F0} C)");

            lever.Value = 0.1f;
            var cold = NewBoiler(2f, lever);
            InjectorRig.TryCreate(null, cold, out _);
            InjectorRig.TryGet(cold, out var coldRig);
            var coldMass = coldRig.BoilerVessel.mass;
            var coldSpill = Run(coldRig, 3f);
            Check(coldRig.Flow.State == InjectorState.LowPressure && coldSpill > 0f && coldRig.BoilerVessel.mass <= coldMass,
                $"below its starting pressure it only spills water ({coldSpill:F0} tender units in 3 s)");

            lever.Value = 0f;
            var empty = NewBoiler(1f, lever);
            InjectorRig.TryCreate(null, empty, out _);
            InjectorRig.TryGet(empty, out var emptyRig);
            var emptyMass = emptyRig.BoilerVessel.mass;
            emptyRig.HandPumping = true;
            var pumpedFromTender = Run(emptyRig, 10f);
            emptyRig.HandPumping = false;
            var afterPump = emptyRig.BoilerVessel.mass;
            emptyRig.HydrantFilling = true;
            var hoseFromTender = Run(emptyRig, 10f);
            emptyRig.HydrantFilling = false;
            Check(afterPump - emptyMass > 10f && pumpedFromTender > 0f && emptyRig.BoilerVessel.mass - afterPump > 100f && hoseFromTender == 0f,
                $"with no steam the hand pump lifts from the tender ({afterPump - emptyMass:F0} kg in 10 s) " +
                $"and the hydrant hose fills from the mains ({emptyRig.BoilerVessel.mass - afterPump:F0} kg in 10 s)");

            rig.HydrantFilling = true;
            var hotMass = rig.BoilerVessel.mass;
            lever.Value = 0f;
            Run(rig, 2f);
            // The rig itself takes the hose off, and says why once (audit
            // 2026-10-10: only the tended loco's controls checked it).
            var why = rig.TakeHoseStopped();
            Check(rig.BoilerVessel.mass <= hotMass && !rig.HydrantFilling && why == "the boiler has pressure" && rig.TakeHoseStopped() == null,
                "a hose cannot fill a boiler under pressure, and the rig takes it off by itself");
            rig.HydrantFilling = false;

            // Two guests on the hand pump: one letting go leaves the other
            // pumping (audit 2026-10-10: one boolean for every player).
            var shared = NewBoiler(1f, lever);
            InjectorRig.TryCreate(null, shared, out _);
            InjectorRig.TryGet(shared, out var sharedRig);
            sharedRig.RemotePump(1, true);
            sharedRig.RemotePump(2, true);
            sharedRig.RemotePump(1, false);
            var sharedMass = sharedRig.BoilerVessel.mass;
            Run(sharedRig, 2f);
            var stillPumped = sharedRig.BoilerVessel.mass > sharedMass;
            sharedRig.RemotePump(2, false);
            sharedRig.RemotePump(2, false);
            var stoppedMass = sharedRig.BoilerVessel.mass;
            Run(sharedRig, 2f);
            Check(stillPumped && !sharedRig.RemoteHandPumping && sharedRig.BoilerVessel.mass <= stoppedMass,
                "two guests on the hand pump: one letting go leaves the other pumping, and it stops when both have");

            // A Worthington SA beside the injector, matched to an engine exhausting 8 kg/s.
            var fed = NewBoiler(12f, lever);
            InjectorRig.TryCreate(null, fed, out _);
            InjectorRig.TryGet(fed, out var pumpRig);
            var valve = new Port("rfFeedPump", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"));
            var exhaust = new Port("steamEngine", new PortDefinition(PortType.READONLY_OUT, PortValueType.MASS_RATE, "EXHAUST_FLOW"), 8f);
            var fullExhaust = new Port("steamEngine", new PortDefinition(PortType.READONLY_OUT, PortValueType.MASS_RATE, "MAX_FLOW"), 15f);
            var chest = new Port("steamEngine", new PortDefinition(PortType.READONLY_OUT, PortValueType.PRESSURE, "STEAM_CHEST_PRESSURE"), 12f);
            pumpRig.AttachPump(new FeedPump(new FeedPumpSpec(FeedHeaterType.Open, 18f), InjectorRig.TenderWaterTemperature),
                "Worthington SA", valve, exhaust, fullExhaust, chest);
            valve.Value = 8f / 18f;
            var pumpMass = pumpRig.BoilerVessel.mass;
            var pumpTaken = Run(pumpRig, 400f) / 4f;
            var pumpAdded = pumpRig.BoilerVessel.mass - pumpMass;
            Check(pumpRig.PumpFlow.State == FeedPumpState.Feeding && pumpAdded > 2000f && pumpRig.PumpFlow.FeedTemperature > 95f && pumpTaken < pumpAdded,
                $"the feed pump fills the boiler with heated water ({pumpAdded:F0} kg in 400 s, now at {pumpRig.PumpFlow.FeedTemperature:F0} C, " +
                $"{pumpTaken:F0} kg from the tender, the rest condensed exhaust; pressure {pumpRig.BoilerVessel.pressure:F1} bar)");
            exhaust.Value = 0f;
            chest.Value = 1f;
            valve.Value = 0.25f;
            Run(pumpRig, 5f);
            Check(pumpRig.PumpFlow.State == FeedPumpState.Choked, "with the throttle shut its drifting valve chokes it to half");
        }

        // The boiler water rig on a stock boiler with the S282's crown sheet,
        // its hooks run between the stock steps in tick order as the patches do
        // (the stock damage step needs Unity's curves, so its crown sheet test
        // is read instead).
        private static void CheckBoilerWater()
        {
            var failuresOn = new SimGameParams(true, false, false, 1f, 1f);
            var injector = new Port("injector", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"));
            var blowdown = new Port("blowdown", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"));
            var tenderWater = new Port("tender", new PortDefinition(PortType.READONLY_OUT, PortValueType.WATER, "AMOUNT"), 40000f);
            var consumed = new Port("tender", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.WATER, "CONSUME_EXT_IN"));
            var heat = new Port("firebox", new PortDefinition(PortType.READONLY_OUT, PortValueType.HEAT_RATE, "HEAT"));
            var fireTemperature = new Port("firebox", new PortDefinition(PortType.READONLY_OUT, PortValueType.TEMPERATURE, "TEMPERATURE"), 1000f);
            var steamDraw = new Port("steam", new PortDefinition(PortType.READONLY_OUT, PortValueType.MASS_RATE, "CONSUMPTION"));

            Boiler NewBoiler(float waterVolume)
            {
                var def = new BoilerDefinition
                {
                    ID = "boiler",
                    diameter = 1.7f,
                    length = 8.8f,
                    capacityMultiplier = 0.85f,
                    maxInjectorRate = 20f,
                    waterConsumptionMultiplier = 4f,
                    maxBlowdownRate = 100f,
                    safetyValveOpeningPressure = 15f,
                    safetyValveClosingPressure = 14.8f,
                    spawnPressure = 14f,
                    spawnWaterLevel = waterVolume,
                    crownSheetNormalizedWaterLevel = 0.6f,
                    crownSheetTempSmoothTime = 60f,
                    crownSheetOverheatTemp = 600f,
                    minimumExplosionPressure = 5f,
                    steamOutletNormalizedWaterLevel = 0.98f,
                };
                var boiler = (Boiler)def.InstantiateImplementation();
                boiler.SetGameParams(failuresOn);
                void Connect(string name, Port port) => boiler.GetAllPortReferences().Single(r => r.id == "boiler." + name).SetPortReference(port);
                Connect("INJECTOR", injector);
                Connect("BLOWDOWN", blowdown);
                Connect("WATER", tenderWater);
                Connect("WATER_CONSUMPTION", consumed);
                Connect("HEAT", heat);
                Connect("FIREBOX_TEMPERATURE", fireTemperature);
                Connect("STEAM_CONSUMPTION", steamDraw);
                return boiler;
            }

            void Call(Boiler boiler, string step, float dt) =>
                typeof(Boiler).GetMethod(step, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(boiler, new object[] { dt });

            void Run(Boiler boiler, BoilerWaterRig rig, float seconds)
            {
                const float dt = 0.02f;
                for (var t = 0f; t < seconds; t += dt)
                {
                    rig.BeginTick();
                    Call(boiler, "SimulateInjector", dt);
                    rig.AfterFeed();
                    Call(boiler, "SimulateSteamConsumption", dt);
                    Call(boiler, "SimulateSafetyValve", dt);
                    var before = rig.MassNow;
                    Call(boiler, "SimulateBlowdown", dt);
                    rig.AfterBlowdown(before);
                    Call(boiler, "SimulateEvaporation", dt);
                    Call(boiler, "SimulateWaterLevel", dt);
                    rig.AfterWaterLevel(dt);
                    rig.SimulateCrownSheet(dt);
                }
            }

            RealisticFiring.Main.Settings.BoilerWaterQuality = FeedWaterQuality.Average;

            // The feed brings solids at the setting's concentration; the blowdown takes them out.
            var fed = NewBoiler(12000f);
            var fedRig = BoilerWaterRig.Create(fed, null, BoilerWaterRig.Guard.None);
            var massBefore = fedRig.MassNow;
            var solidsBefore = fedRig.Water.Solids + fedRig.Water.SuspendedSludge;
            injector.Value = 0.5f;
            Run(fed, fedRig, 10f);
            injector.Value = 0f;
            var added = fedRig.MassNow - massBefore;
            var brought = fedRig.Water.Solids + fedRig.Water.SuspendedSludge + fedRig.Water.SettledSludge - solidsBefore;
            var expected = added * fedRig.Water.FeedConcentration * 1e-6f;
            Check(added > 50f && Math.Abs(brought - expected) < 0.1f * expected,
                $"the feed brings its solids ({added:F0} kg fed, {brought * 1000f:F1} g of solids, {expected * 1000f:F1} g expected)");
            blowdown.Value = 1f;
            var beforeBlow = fedRig.MassNow;
            Run(fed, fedRig, 3f);
            blowdown.Value = 0f;
            Check(beforeBlow - fedRig.MassNow > 200f && fedRig.Water.BlownOut > 0f,
                $"a 3-second blowdown takes the water and its solids ({beforeBlow - fedRig.MassNow:F0} kg, {fedRig.Water.BlownOut * 1000f:F1} g of solids)");

            // Water past foaming shows in the glass and carries water over.
            var foamy = NewBoiler(12000f);
            var foamyRig = BoilerWaterRig.Create(foamy, null, BoilerWaterRig.Guard.None);
            foamyRig.Water.Restore(foamyRig.MassNow * 6000e-6f, 0f, 0f, foamyRig.MassNow);
            Run(foamy, foamyRig, 20f);
            var rise = 0f;
            for (var i = 0; i < 50; i++)
            {
                Run(foamy, foamyRig, 0.1f);
                rise = Math.Max(rise, foamyRig.GlassLevel - foamyRig.TrueLevel);
            }
            var quality = foamy.outletSteamQualityReadOut.Value;
            Check(foamyRig.Water.Foam > 0.1f && rise > 0.01f && quality < 0.99f,
                $"foaming water lifts the glass and wets the steam (foam {foamyRig.Water.Foam:F2}, glass up to {rise:F3} high, steam quality {quality:F3})");

            // A bared crown sheet: the plug melts and saves the sheet; without one it fails.
            var plugged = NewBoiler(8000f);
            var plugRig = BoilerWaterRig.Create(plugged, null, BoilerWaterRig.Guard.FusiblePlug);
            var bare = NewBoiler(8000f);
            var bareRig = BoilerWaterRig.Create(bare, null, BoilerWaterRig.Guard.None);
            // A new boiler's crown sheet starts at 0 C: let it reach the water's
            // temperature under a dead fire before the fire heats the bared sheet.
            fireTemperature.Value = 0f;
            Run(plugged, plugRig, 600f);
            Run(bare, bareRig, 600f);
            fireTemperature.Value = 1000f;
            var pressureBefore = plugRig.BoilerPressure;
            var meltTime = 0f;
            while (!plugRig.Plug.Melted && meltTime < 60f)
            {
                Run(plugged, plugRig, 0.5f);
                meltTime += 0.5f;
            }
            Run(plugged, plugRig, 120f);
            Run(bare, bareRig, 120f + meltTime);
            Check(plugRig.Plug.Melted && meltTime < 15f && !plugRig.CrownSheetWouldFail && plugRig.PlugVent > 0f && plugRig.BoilerPressure < pressureBefore,
                $"bared, the plug melts in {meltTime:F1} s and blows into the firebox ({plugRig.PlugVent:F2} kg/s, {pressureBefore:F1} -> {plugRig.BoilerPressure:F1} bar), " +
                $"holding the crown sheet at {plugRig.CrownSheetTemperatureNow:F0} C");
            Check(bareRig.CrownSheetWouldFail, $"without a plug the stock crown sheet fails ({bareRig.CrownSheetTemperatureNow:F0} C at {bareRig.BoilerPressure:F1} bar)");

            var alarmed = NewBoiler(8000f);
            var alarmRig = BoilerWaterRig.Create(alarmed, null, BoilerWaterRig.Guard.LowWaterAlarm);
            Run(alarmed, alarmRig, 1f);
            var quiet = NewBoiler(12000f);
            var quietRig = BoilerWaterRig.Create(quiet, null, BoilerWaterRig.Guard.LowWaterAlarm);
            Run(quiet, quietRig, 1f);
            Check(alarmRig.Alarm.Sounding && !quietRig.Alarm.Sounding,
                $"the alarm blows at {alarmRig.TrueLevel:F2} of the boiler and not at {quietRig.TrueLevel:F2} (crown sheet at 0.60)");

            // Saved and loaded beside the stock keys; a washout renews it all.
            var data = plugged.GetSaveStateData();
            plugRig.Save(data);
            var loaded = NewBoiler(8000f);
            var loadedRig = BoilerWaterRig.Create(loaded, null, BoilerWaterRig.Guard.FusiblePlug);
            loaded.SetSaveStateData(data);
            BoilerWaterRig.OnLoaded(loaded, data);
            Check(loadedRig.Plug.Melted && Math.Abs(loadedRig.Water.Solids - plugRig.Water.Solids) < 1e-4f,
                "the water and the melted plug save and load with the boiler");
            loadedRig.Washout();
            Check(!loadedRig.Plug.Melted && loadedRig.Water.SettledSludge == 0f && loadedRig.PlugVent == 0f,
                "a washout renews the plug and freshens the water");

            // A pit stop's repair renews a melted plug, as the game renews a
            // burst boiler; the damage loading, a repair short of 95 %, and a
            // repair with the plug whole do not.
            var repairedBoiler = NewBoiler(8000f);
            var repairRig = BoilerWaterRig.Create(repairedBoiler, null, BoilerWaterRig.Guard.FusiblePlug);
            repairRig.MeltPlug();
            blowdown.Value = 1f;
            Run(repairedBoiler, repairRig, 5f);
            blowdown.Value = 0f;
            repairRig.NoteHealth(0.4f, 1f);
            repairRig.NoteHealth(1f, 1f);
            var meltedAtLoad = repairRig.Plug.Melted;
            for (var i = 0; i < 60; i++)
                repairRig.NoteHealth(0.5f, 0.5f);
            repairRig.NoteHealth(0.9f, 0.5f);
            var meltedShort = repairRig.Plug.Melted;
            var drained = repairRig.MassNow;
            repairRig.NoteHealth(0.9f, 1f);
            Run(repairedBoiler, repairRig, 0.02f);
            Check(meltedAtLoad && meltedShort && !repairRig.Plug.Melted && repairRig.PlugVent == 0f && repairRig.MassNow > drained,
                $"a repair of the engine renews a melted plug and refills the boiler ({drained:F0} -> {repairRig.MassNow:F0} kg); the damage loading and a repair short of 95 % do not");
            var whole = NewBoiler(8000f);
            var wholeRig = BoilerWaterRig.Create(whole, null, BoilerWaterRig.Guard.FusiblePlug);
            for (var i = 0; i < 60; i++)
                wholeRig.NoteHealth(0.5f, 1f);
            var wholeMass = wholeRig.MassNow;
            wholeRig.NoteHealth(1f, 1f);
            Check(!wholeRig.Plug.Melted && wholeRig.MassNow == wholeMass, "a repair with the plug whole leaves the boiler as it is");
        }

        // An oil burner: the oil firebox has the stock firebox's contract, the
        // oil feed CCL's stoker's; lit waste and oil with the atomizer light the
        // fire, the tender pays at the stoker's multiplier, and the burner's
        // steam comes from the boiler.
        private static void CheckOilBurner(SimGameParams gameParams)
        {
            var fireboxDef = new FireboxDefinition { ID = Id, maxCoalCapacity = 200f, burnTime = 100f, efficiencyAtMaxCombustion = 0.6f, startupMaxPressure = 22f };
            var stock = new Firebox(fireboxDef);
            var oil = new RealisticOilFirebox(fireboxDef, null);

            // Oil pooled in the pan and its vapor survive a save, and old
            // saves keep the pooled oil they wrote under the stock coal key
            // (audit 2026-10-10: both came back zero).
            var pooled = new RealisticOilFirebox(fireboxDef, null);
            var hazard = pooled.Model.Snapshot();
            hazard.PooledOil = 3f;
            hazard.Vapor = 1f;
            pooled.Model.Mirror(hazard);
            var oilSave = pooled.GetSaveStateData();
            var reloaded = new RealisticOilFirebox(fireboxDef, null);
            reloaded.SetSaveStateData(oilSave);
            var oldSave = new RealisticOilFirebox(fireboxDef, null);
            oldSave.SetSaveStateData(new JObject { ["coalMass"] = 2f });
            Check(reloaded.Model.PooledOil == 3f && reloaded.Model.Vapor == 1f && oldSave.Model.PooledOil == 2f && oldSave.Model.Vapor == 0f,
                $"pooled oil and vapor survive a save ({reloaded.Model.PooledOil:F0} and {reloaded.Model.Vapor:F0} kg), and an old save keeps its pooled oil");
            oil.SetGameParams(gameParams);
            Check(Describe(stock) == Describe(oil) && stock.id == oil.id && oil.HasSaveData,
                $"the oil firebox keeps the stock firebox's ID, ports, and references ({oil.GetAllPorts().Count} ports, {oil.GetAllPortReferences().Count} references)" +
                Difference(Describe(stock), Describe(oil)));

            var dv = Environment.GetEnvironmentVariable("DV_INSTALL_DIR")
                     ?? Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? "", ".local/share/Steam/steamapps/common/Derail Valley");
            var importer = Path.Combine(dv, "Mods", "DVCustomCarLoader", "CCL.Importer.dll");
            if (!File.Exists(importer))
            {
                Console.WriteLine("skip oil feed checks: Custom Car Loader is not installed");
                return;
            }
            var cclDefType = Assembly.LoadFrom(importer).GetType(OilBurnerFit.StokerTypeName);
            var cclDef = (SimComponentDefinition)Activator.CreateInstance(cclDefType);
            cclDef.ID = "autostoker";
            var ccl = cclDef.InstantiateImplementation();
            var feed = new OilFeed(StokerDefinitionPatch.Read(cclDef), null, 0.5f, steamWired: false);
            feed.SetGameParams(gameParams);
            Check(Describe(ccl) == Describe(feed) && ccl.id == feed.id,
                $"the oil feed keeps CCL's stoker's ID, ports, and references ({feed.GetAllPorts().Count} ports, {feed.GetAllPortReferences().Count} references)");

            var valve = new Port("stokerControl", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"));
            var tender = new Port("tenderCoal", new PortDefinition(PortType.READONLY_OUT, PortValueType.FUEL, "AMOUNT"), 5000f);
            var consume = new Port("tenderCoal", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.FUEL, "CONSUME_EXT_IN"));
            var atomizer = new Port(OilBurnerFit.Atomizer.Id, new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"), 0.3f);
            feed.GetAllPortReferences().Single(r => r.id == "autostoker.CONTROL").SetPortReference(valve);
            feed.GetAllPortReferences().Single(r => r.id == "autostoker.COAL_AMOUNT").SetPortReference(tender);
            feed.GetAllPortReferences().Single(r => r.id == "autostoker.COAL_CONSUMPTION").SetPortReference(consume);
            void Feed(string name, float value) =>
                oil.GetAllPortReferences().Single(r => r.id == $"{Id}.{name}").SetPortReference(
                    new Port("src", new PortDefinition(PortType.READONLY_OUT, PortValueType.GENERIC, name), value));
            // The draft a well-set fire of 0.92 kg/s wants: its design excess air.
            Feed("AIR_FLOW", 0.92f * FireModel.AirPerCoal);
            Feed("BOILER_PRESSURE", 15f);
            Feed("BOILER_TEMPERATURE", 198f);
            // Moving, so the stock steam-raising speedup (it burns fuel and
            // makes heat faster at a stand) stays out of the comparison.
            Feed("FORWARD_SPEED", 10f);
            oil.Feed = feed;
            oil.AttachPorts(null, atomizer, null);

            P(oil, "IGNITION_EXT_IN").ExternalValueUpdate(1f);
            valve.Value = 0.4f;
            float tenderDrawn = 0f;
            for (var t = 0f; t < 30f; t += 0.02f)
            {
                consume.Value = 0f;
                oil.Tick(0.02f);
                feed.Tick(0.02f);
                tenderDrawn += consume.Value;
            }
            var burned = oil.Model.FeedRate;
            Check(P(oil, "FIRE_ON").Value == 1f && P(oil, "HEAT").Value > 1e6f && oil.Model.SootDensity < 0.2f,
                $"lit waste and oil with the atomizer light the fire ({P(oil, "HEAT").Value / 1e6f:F1} MW at {burned:F2} kg/s, soot {oil.Model.SootDensity:F2}, " +
                $"excess air {oil.Model.ExcessAir:F2}, atomizing {oil.Model.Atomizing:F2})");
            var feedSteam = feed.GetAllPorts().Single(p => p.id == "autostoker.STEAM_CONSUMPTION").Value;
            var fed = oil.Model.FeedRate * 30f;
            Check(tenderDrawn > 0f && Math.Abs(tenderDrawn - 0.5f * fed) < 0.25f * fed && feedSteam > 0f,
                $"the tender pays at the stoker's multiplier ({tenderDrawn:F1} units for about {fed:F1} kg) and the burner takes steam ({feedSteam:F3} kg/s)");

            var data = oil.GetSaveStateData();
            var loaded = new RealisticOilFirebox(fireboxDef, null);
            loaded.SetSaveStateData(data);
            // The oil fireman's mode is saved as the coal fireman's is, and
            // waits for his cab to be set up.
            var firemanSave = (JObject)data.DeepClone();
            firemanSave[RealisticFirebox.FiremanSaveKey] = (int)FiremanMode.Road;
            var manned = new RealisticOilFirebox(fireboxDef, null);
            manned.SetSaveStateData(firemanSave);
            Check(data[RealisticFirebox.FiremanSaveKey] == null && manned.SavedFiremanMode == FiremanMode.Road
                  && manned.GetSaveStateData().Value<int>(RealisticFirebox.FiremanSaveKey) == (int)FiremanMode.Road,
                $"the oil fireman's mode saves and loads ({manned.SavedFiremanMode}), and none is written while he is off");
            Check(loaded.Model.FireOn && Math.Abs(loaded.Model.BrickTemperature - oil.Model.BrickTemperature) < 1f
                  && Math.Abs(loaded.Model.OilTemperature - oil.Model.OilTemperature) < 0.1f,
                $"the oil fire saves and loads (brick {oil.Model.BrickTemperature:F0} C, oil {oil.Model.OilTemperature:F0} C)");

            // A dead engine (1 bar) has no steam to atomize its oil until a
            // shed's steam line is hooked up, whose steam is not the boiler's.
            var dead = new RealisticOilFirebox(fireboxDef, null);
            dead.SetGameParams(gameParams);
            void DeadFeed(string name, float value) =>
                dead.GetAllPortReferences().Single(r => r.id == $"{Id}.{name}").SetPortReference(
                    new Port("src", new PortDefinition(PortType.READONLY_OUT, PortValueType.GENERIC, name), value));
            DeadFeed("AIR_FLOW", 0.92f * FireModel.AirPerCoal);
            DeadFeed("BOILER_PRESSURE", 1f);
            DeadFeed("BOILER_TEMPERATURE", 25f);
            DeadFeed("FORWARD_SPEED", 0f);
            dead.Feed = feed;
            var heater = new Port(OilBurnerFit.Heater.Id, new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"), 1f);
            dead.AttachPorts(null, atomizer, heater);
            valve.Value = 0f;
            for (var t = 0f; t < 1f; t += 0.02f)
                dead.Tick(0.02f);
            var coldOil = dead.Model.OilTemperature;
            P(dead, "IGNITION_EXT_IN").ExternalValueUpdate(1f);
            valve.Value = 0.4f;
            for (var t = 0f; t < 10f; t += 0.02f)
                dead.Tick(0.02f);
            var unlit = P(dead, "FIRE_ON").Value;
            var pooledCold = dead.Model.PooledOil;
            // At a pit stop on the line, the heater warms the oil for ten minutes, then the fire is lit.
            RealisticOilFirebox.ServedByPitStop = _ => true;
            dead.SteamLine = true;
            valve.Value = 0f;
            for (var t = 0f; t < 600f; t += 0.02f)
                dead.Tick(0.02f);
            var warmed = dead.Model.OilTemperature;
            P(dead, "IGNITION_EXT_IN").ExternalValueUpdate(1f);
            valve.Value = 0.4f;
            for (var t = 0f; t < 10f; t += 0.02f)
            {
                dead.Tick(0.02f);
                feed.Tick(0.02f);
            }
            var lineSteam = feed.GetAllPorts().Single(p => p.id == "autostoker.STEAM_CONSUMPTION").Value;
            Check(coldOil < 20f && unlit == 0f && pooledCold == 0f && P(dead, "FIRE_ON").Value == 1f && dead.Model.OnSteamLine && lineSteam == 0f,
                $"a dead engine's cold oil will not run and it will not light; on a shed's steam line its heater warms the oil " +
                $"({coldOil:F0} -> {warmed:F0} C in ten minutes) and it lights without drawing the boiler's steam " +
                $"({dead.Model.FeedRate:F2} kg/s of oil, atomizing {dead.Model.Atomizing:F2})");

            // The cab's shop steam valve opens the line as the key does, as far as it is open, at a pit stop only.
            var valved = new RealisticOilFirebox(fireboxDef, null);
            valved.SetGameParams(gameParams);
            foreach (var reference in valved.GetAllPortReferences())
                reference.SetPortReference(new Port("src", new PortDefinition(PortType.READONLY_OUT, PortValueType.GENERIC, "OUT"),
                    reference.id.EndsWith("BOILER_PRESSURE") ? 1f : 0f));
            var shopSteam = new Port(OilBurnerFit.ShopSteam.Id, new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"), 0.5f);
            valved.AttachPorts(null, atomizer, heater, shopSteam);
            RealisticOilFirebox.ServedByPitStop = _ => false;
            for (var t = 0f; t < 2f; t += 0.02f)
                valved.Tick(0.02f);
            var away = valved.Model.OnSteamLine;
            RealisticOilFirebox.ServedByPitStop = _ => true;
            for (var t = 0f; t < 2f; t += 0.02f)
                valved.Tick(0.02f);
            Check(!away && valved.Model.OnSteamLine && Math.Abs(valved.SteamLineOpening - 0.5f) < 1e-4f,
                $"the shop steam valve puts the line on the burner at a pit stop only, as far as it is open ({valved.SteamLineOpening:P0})");
            RealisticOilFirebox.ServedByPitStop = WaterPoints.Serves;
        }

        // What a multiplayer host sends clients' views of a loco: captured from
        // the real parts and shown on fresh ones, it reads back the same.
        private static (Boiler Boiler, ExternalControl Right, ExternalControl Left, SimComponent[] Components, Port Water, Port Consumed)
            Jk3InjectorSetup(SimGameParams gameParams, float divisorValue = 0.5f, bool negative = false, bool invert = false, bool swap = false)
        {
            var right = new ExternalControl(new ExternalControlDefinition { ID = "injector", saveState = true });
            var left = new ExternalControl(new ExternalControlDefinition { ID = "injector2", saveState = true });
            var add = new ConfigurableAdd(new ConfigurableAddDefinition
            {
                ID = "injectorAdd", negativeA = negative,
                aReader = new PortReferenceDefinition(PortValueType.GENERIC, "INJECTOR1"),
                bReader = new PortReferenceDefinition(PortValueType.GENERIC, "INJECTOR2"),
                addReadOut = new PortDefinition(PortType.READONLY_OUT, PortValueType.GENERIC, "INJECTOR_X2"),
            });
            add.aReader.SetPortReference(swap ? left.controlExtIn : right.controlExtIn);
            add.bReader.SetPortReference(swap ? right.controlExtIn : left.controlExtIn);
            var divisor = new ConfigurablePorts(new ConfigurablePortsDefinition
            {
                ID = "injectorDivisor", ports = new[] { new PortDefinition(PortType.IN, PortValueType.GENERIC, "DIVISOR") },
                startingValues = new[] { divisorValue },
            });
            var normalized = new ConfigurableMultiplier(new ConfigurableMultiplierDefinition
            {
                ID = "injectorNormalized", invertB = invert,
                aReader = new PortReferenceDefinition(PortValueType.GENERIC, "INJECTOR_X2"),
                bReader = new PortReferenceDefinition(PortValueType.GENERIC, "DIVISOR"),
                mulReadOut = new PortDefinition(PortType.READONLY_OUT, PortValueType.GENERIC, "INJECTOR_NORMALIZED"),
            });
            normalized.aReader.SetPortReference(add.addReadOut);
            normalized.bReader.SetPortReference(divisor.ports[0]);
            var particles = new ConfigurablePorts(new ConfigurablePortsDefinition
            {
                ID = "injectorParticles", startingValues = new[] { 0f, 0f },
                ports = new[]
                {
                    new PortDefinition(PortType.READONLY_OUT, PortValueType.GENERIC, "INJECTOR_R_PARTICLES_CLAMP"),
                    new PortDefinition(PortType.READONLY_OUT, PortValueType.GENERIC, "INJECTOR_L_PARTICLES_CLAMP"),
                },
            });
            var boiler = new Boiler(new BoilerDefinition
            {
                ID = "boiler", diameter = 1.33f, length = 4.7f, capacityMultiplier = 0.85f,
                maxInjectorRate = 8f, waterConsumptionMultiplier = 1.5f,
                safetyValveOpeningPressure = 15f, safetyValveClosingPressure = 14.7f,
                spawnPressure = 15f, spawnWaterLevel = 4000f,
            });
            boiler.SetGameParams(gameParams);
            boiler.injectorControl.SetPortReference(normalized.mulReadOut);
            var water = new Port("tender", new PortDefinition(PortType.READONLY_OUT, PortValueType.WATER, "AMOUNT"), 8250f);
            var consumed = new Port("tender", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.WATER, "CONSUME_EXT_IN"));
            boiler.water.SetPortReference(water);
            boiler.waterConsumption.SetPortReference(consumed);
            return (boiler, right, left, new SimComponent[] { divisor, right, left, add, normalized, particles, boiler }, water, consumed);
        }

        // An engine fitted with its own injector steam valve: the stock lever
        // is the water valve, the new control shut, primed, or wide open.
        private static void CheckInjectorSteamValve(SimGameParams gameParams)
        {
            var water = new Port("injector", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"));
            var tenderWater = new Port("tender", new PortDefinition(PortType.READONLY_OUT, PortValueType.WATER, "AMOUNT"), 40000f);
            var consumed = new Port("tender", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.WATER, "CONSUME_EXT_IN"));
            var steamControl = (ExternalControl)new ExternalControlDefinition { ID = InjectorValves.Steam.Id }.InstantiateImplementation();
            var def = new BoilerDefinition
            {
                ID = "boiler", diameter = 1.7f, length = 8.8f, capacityMultiplier = 0.85f, maxInjectorRate = 5f,
                waterConsumptionMultiplier = 4f, safetyValveOpeningPressure = 15f, safetyValveClosingPressure = 14.8f,
                spawnPressure = 13f, spawnWaterLevel = 9000f,
            };
            var boiler = (Boiler)def.InstantiateImplementation();
            boiler.SetGameParams(gameParams);
            boiler.GetAllPortReferences().Single(r => r.id == "boiler.INJECTOR").SetPortReference(water);
            boiler.GetAllPortReferences().Single(r => r.id == "boiler.WATER").SetPortReference(tenderWater);
            boiler.GetAllPortReferences().Single(r => r.id == "boiler.WATER_CONSUMPTION").SetPortReference(consumed);
            var made = InjectorRig.TryCreate(null, boiler, "LocoS060", new SimComponent[] { boiler, steamControl }, out var note);
            InjectorRig.TryGet(boiler, out var rig);
            var steam = steamControl.controlExtIn;
            InjectorFlow Run(float steamAt, float waterAt, float seconds)
            {
                steam.Value = steamAt;
                water.Value = waterAt;
                for (var t = 0f; t < seconds; t += 0.02f)
                {
                    consumed.Value = 0f;
                    rig.Simulate(0.02f);
                    rig.BoilerVessel.Update();
                }
                return rig.Circuits[0].Flow;
            }
            if (!made || rig == null || rig.Circuits[0].Steam == null)
            {
                Check(false, $"the S060 gets its injector steam valve ({note})");
                return;
            }
            var gravity = Run(0f, 1f, 1f);
            Run(InjectorSpec.SteamPrime, 1f, 3f);
            var full = Run(1f, 1f, 2f);
            var part = Run(1f, 0.6f, 2f);
            Check(gravity.State == InjectorState.Off && gravity.Overflow > 0f && full.State == InjectorState.Working
                  && part.State == InjectorState.Working && Math.Abs(part.WaterIn / full.WaterIn - 0.6f) < 0.05f,
                $"with its own steam valve the S060's water runs from the overflow until it is primed ({gravity.Overflow:F2} kg/s), " +
                $"then feeds as its water valve sets ({part.WaterIn:F2} kg/s at 60 %, {full.WaterIn:F2} wide open)");
        }

        private static void CheckJk3Injectors(SimGameParams gameParams)
        {
            InjectorRig Rig((Boiler Boiler, ExternalControl Right, ExternalControl Left, SimComponent[] Components, Port Water, Port Consumed) setup)
            {
                InjectorRig.TryCreate(null, setup.Boiler, "Jk3A", setup.Components, out _);
                InjectorRig.TryGet(setup.Boiler, out var rig);
                rig?.EndResume();
                return rig;
            }
            void Tick(InjectorRig rig, float seconds)
            {
                for (var i = 0; i < (int)(seconds / 0.02f); i++)
                    rig.Simulate(0.02f);
            }
            var setup = Jk3InjectorSetup(gameParams);
            Check(!InjectorRig.TryCreate(null, setup.Boiler, out _)
                  && !InjectorRig.TryCreate(null, setup.Boiler, "OtherLoco", setup.Components, out _),
                "calculated injector inputs remain stock without the JK3 livery and verified graph");
            foreach (var invalid in new[]
            {
                Jk3InjectorSetup(gameParams, divisorValue: 1f), Jk3InjectorSetup(gameParams, negative: true),
                Jk3InjectorSetup(gameParams, invert: true), Jk3InjectorSetup(gameParams, swap: true),
            })
                Check(!InjectorRig.TryCreate(null, invalid.Boiler, "Jk3A", invalid.Components, out _),
                    "a changed JK3 injector calculation retains stock behavior");
            Check(!InjectorRig.TryCreate(null, setup.Boiler, "Jk3A", setup.Components.Where(c => c.id != "injector2"), out _),
                "a missing JK3 valve retains stock behavior");

            var before = string.Join(";", setup.Components.Select(Describe));
            var rig = Rig(setup);
            Check(rig != null && rig.Circuits.Count == 2 && rig.Circuits.All(c => c.Injector.Spec.MaxDelivery == 4f)
                  && rig.MaxDelivery == 8f && before == string.Join(";", setup.Components.Select(Describe)),
                "JK3 has two 4 kg/s injectors without changing any stock ports or defaults");
            setup.Right.controlExtIn.Value = 0.1f;
            setup.Left.controlExtIn.Value = 1f;
            Tick(rig, 2f);
            // Once it sings, opened past its least so it feeds clean.
            setup.Right.controlExtIn.Value = 0.6f;
            Tick(rig, 0.5f);
            Check(rig.Circuits[0].Flow.State == InjectorState.Working && rig.Circuits[1].Flow.State == InjectorState.Broken
                  && rig.Circuits[0].Flow.OverflowSteam == 0f && rig.Circuits[1].Flow.OverflowSteam > 0f
                  && rig.Circuits[1].Flow.InjectorTemperature > rig.Circuits[0].Flow.InjectorTemperature + 30f,
                "priming one JK3 valve does not prime the other; jet failure, heat, and overflow stay independent");
            setup.Right.controlExtIn.Value = 1f;
            rig.Simulate(0.02f);
            Check(Math.Abs(rig.Flow.WaterIn - 4f) < 0.001f && setup.Boiler.injectorFlowNormalizedReadOut.Value == 0.5f,
                "one working JK3 injector delivers half capacity and only its valve contributes to the boiler readout");
            var particles = setup.Components.OfType<ConfigurablePorts>().Single(c => c.id == "injectorParticles").ports;
            Check(particles[0].Value == 1f && particles[1].Value == 0f,
                "JK3's stock side particle readouts follow each injector's actual feed");
            setup.Right.controlExtIn.Value = 0f;
            rig.Simulate(0.02f);
            Check(rig.Circuits[0].Flow.State == InjectorState.Off && rig.Circuits[1].Flow.State == InjectorState.Broken,
                "shutting one JK3 injector does not reset the other injector's broken jet");
            var description = FireController.Describe(rig);
            Check(description.Contains("right injector off") && description.Contains("left injector BROKEN"),
                "the overlay describes the JK3 injectors separately");
            Check(setup.Right.GetSaveStateData().Value<float>("val") == 0f
                  && setup.Left.GetSaveStateData().Value<float>("val") == 1f,
                "JK3's two controls keep their independent stock save keys");

            var both = Jk3InjectorSetup(gameParams);
            var bothRig = Rig(both);
            both.Right.controlExtIn.Value = both.Left.controlExtIn.Value = 0.1f;
            Tick(bothRig, 2f);
            both.Right.controlExtIn.Value = both.Left.controlExtIn.Value = 1f;
            var mass = bothRig.BoilerVessel.mass;
            both.Consumed.Value = 0f;
            bothRig.Simulate(0.5f);
            Check(Math.Abs(bothRig.Flow.WaterIn - 8f) < 0.001f && both.Boiler.injectorFlowNormalizedReadOut.Value == 1f
                  && Math.Abs(bothRig.BoilerVessel.mass - mass - (bothRig.Flow.WaterIn - bothRig.Flow.SteamUsed) * 0.5f) < 0.002f
                  && Math.Abs(both.Consumed.Value - bothRig.Flow.TenderDraw * 0.5f * 1.5f) < 0.001f,
                "both JK3 injectors deliver 8 kg/s total, with steam deducted and both tender draws reported once");
            both.Water.Value = 0.03f * 1.5f;
            both.Consumed.Value = 0f;
            bothRig.Simulate(0.5f);
            Check(bothRig.Flow.TenderDraw * 0.5f <= 0.03001f && both.Consumed.Value <= 0.04501f
                  && bothRig.Circuits[1].Flow.State == InjectorState.NoWater,
                "the paired JK3 injectors share the remaining tender water without overdrawing it");
            both.Water.Value = 8250f;
            var volume = (float)typeof(Boiler).GetField("totalVolume", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(both.Boiler);
            // These stock vessel readouts are the rig's pressure and space inputs.
            bothRig.BoilerVessel.waterVolume = volume - 1f;
            bothRig.Simulate(2f);
            Check(Math.Abs(bothRig.Flow.WaterIn * 2f - 1f) < 0.001f && bothRig.Circuits[1].Flow.WaterIn == 0f,
                "the JK3 injectors share the last kilogram of boiler space without overfilling it");
            bothRig.BoilerVessel.waterVolume = volume;
            bothRig.Simulate(0.02f);
            Check(bothRig.Flow.WaterIn == 0f && bothRig.Circuits.All(c => c.Flow.Overflow > 0f),
                "both JK3 injectors spill when the shared boiler has no room for more water");

            var client = Jk3InjectorSetup(gameParams);
            var clientRig = Rig(client);
            var data = ClientMirror.Capture(null, rig, null, null, null);
            ClientMirror.Apply(data, null, clientRig, null, null, null);
            Check(clientRig.Circuits.Select(c => c.Flow).SequenceEqual(rig.Circuits.Select(c => c.Flow))
                  && clientRig.Flow.Equals(rig.Flow),
                "multiplayer mirrors each JK3 injector's state, heat, and overflow by control port");
            both.Boiler.isBrokenReadOut.Value = 1f;
            bothRig.Simulate(0.02f);
            Check(bothRig.Circuits.All(c => c.Flow.State == InjectorState.Off) && bothRig.Flow.TenderDraw == 0f,
                "a broken JK3 boiler shuts down both injectors");
        }

        private static void CheckClientMirror(SimGameParams gameParams, FireboxDefinition def)
        {
            var host = Create(def, gameParams);
            host.SetSaveStateData(new JObject { ["fireOn"] = true, ["coalMass"] = 40f });
            host.DeliverShovelful(5f, GrateZone.Front);
            host.LastStoker = new StokerFlow { Fed = 0.4f, Jammed = true, ManifoldPressure = 3.2f };
            host.StokerEngineSpeed = 0.6f;

            Boiler NewBoiler()
            {
                var boilerDef = new BoilerDefinition
                {
                    ID = "boiler", diameter = 1.7f, length = 8.8f, capacityMultiplier = 0.85f, maxInjectorRate = 20f,
                    waterConsumptionMultiplier = 4f, safetyValveOpeningPressure = 15f, safetyValveClosingPressure = 14.8f,
                    spawnPressure = 12f, spawnWaterLevel = 9000f, crownSheetNormalizedWaterLevel = 0.6f,
                };
                var boiler = (Boiler)boilerDef.InstantiateImplementation();
                boiler.SetGameParams(gameParams);
                boiler.GetAllPortReferences().Single(r => r.id == "boiler.INJECTOR")
                    .SetPortReference(new Port("injector", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN")));
                return boiler;
            }
            var hostBoiler = NewBoiler();
            InjectorRig.TryCreate(null, hostBoiler, out _);
            InjectorRig.TryGet(hostBoiler, out var hostRig);
            hostRig.Flow = new InjectorFlow { State = InjectorState.Priming, Overflow = 1.3f, OverflowSteam = 0.2f, WaterIn = 0f };
            hostRig.HydrantFilling = true;
            var hostWater = BoilerWaterRig.Create(hostBoiler, null, BoilerWaterRig.Guard.FusiblePlug);
            hostWater.MeltPlug();
            hostWater.PlugVent = 0.8f;
            var oilDef = new FireboxDefinition { ID = Id, maxCoalCapacity = 200f, burnTime = 100f, efficiencyAtMaxCombustion = 0.6f };
            var hostOil = new RealisticOilFirebox(oilDef, null);
            hostOil.Model.PrepareFire();

            var data = ClientMirror.Capture(host, hostRig, hostWater, null, hostOil);

            var client = Create(def, gameParams);
            var clientBoiler = NewBoiler();
            InjectorRig.TryCreate(null, clientBoiler, out _);
            InjectorRig.TryGet(clientBoiler, out var clientRig);
            var clientWater = BoilerWaterRig.Create(clientBoiler, null, BoilerWaterRig.Guard.FusiblePlug);
            var clientOil = new RealisticOilFirebox(oilDef, null);
            ClientMirror.Apply(data, client, clientRig, clientWater, null, clientOil);

            Check(client.Model.FireOn && client.Model.SaveZones().SequenceEqual(host.Model.SaveZones())
                  && client.LastStoker is StokerFlow flow && flow.Jammed && flow.Fed == 0.4f && client.StokerEngineSpeed == 0.6f,
                $"a client's view gets the host's bed zone by zone and its stoker's reading ({data.Length} bytes; " +
                $"front zone {client.Model.Zones[(int)GrateZone.Front].Coal:F1} kg)");
            Check(clientRig.Flow.State == InjectorState.Priming && clientRig.Flow.Overflow == 1.3f && clientRig.HydrantFilling
                  && clientWater.Plug.Melted && clientWater.PlugVent == 0.8f,
                "and the injector's overflow, the hose, and the melted plug's blow");
            Check(clientOil.Model.Snapshot().Equals(hostOil.Model.Snapshot()), "and the oil fire's state");

            // Damaged state from the wire is dropped, section by section,
            // without throwing or reading one section into the next (audit
            // 2026-10-10: a one-byte payload threw out of the decoder).
            bool Survives(byte[] bytes)
            {
                try
                {
                    ClientMirror.Apply(bytes, client, clientRig, clientWater, null, clientOil);
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            }
            var injectorAt = 0;
            while (data[injectorAt] != 2)
                injectorAt += 3 + (data[injectorAt + 1] | data[injectorAt + 2] << 8);
            var injectorLength = data[injectorAt + 1] | data[injectorAt + 2] << 8;
            // The injector section with a NaN for its first float and the rest
            // of the stream intact: the injector is left alone, the others apply.
            var poisoned = (byte[])data.Clone();
            var nan = BitConverter.GetBytes(float.NaN);
            Array.Copy(nan, 0, poisoned, injectorAt + 3 + 4, 4);
            clientRig.Flow = new InjectorFlow { State = InjectorState.Off };
            client.StokerEngineSpeed = 0f;
            var poisonedOk = Survives(poisoned) && clientRig.Flow.State == InjectorState.Off && client.StokerEngineSpeed == 0.6f;
            // A section that claims to be shorter than it reads: it fails alone.
            var shortened = (byte[])data.Clone();
            shortened[injectorAt + 1] = 8;
            shortened[injectorAt + 2] = 0;
            var tail = data.Length - (injectorAt + 3 + injectorLength);
            var shortStream = new byte[injectorAt + 3 + 8 + tail];
            Array.Copy(shortened, 0, shortStream, 0, injectorAt + 3 + 8);
            Array.Copy(data, injectorAt + 3 + injectorLength, shortStream, injectorAt + 3 + 8, tail);
            clientWater.PlugVent = 0f;
            var shortOk = Survives(shortStream) && clientWater.PlugVent == 0.8f;
            Check(Survives(new byte[] { 1 }) && Survives(new byte[] { 1, 200 }) && Survives(new byte[] { 1, 255, 255, 0 })
                  && Survives(new byte[] { 99, 2, 0, 7, 7 }) && Survives(new byte[0]) && poisonedOk && shortOk,
                "damaged state from the host is dropped section by section: a broken header, a length past the end, an unknown tag, " +
                "a NaN, and a section shorter than it reads, the good sections still applied");
        }

        // The Hudson's booster in the sim: between the main engine's torque and
        // the traction, on the steam chest and the reverser, dropping its latch
        // when the reverser is hooked up.
        private static void CheckBooster()
        {
            var def = new RealisticBoosterDefinition
            {
                ID = BoosterFit.BoosterId,
                MinCutoff = 0.05f,
                MaxCutoff = 0.9f,
                TrailerLoad = 25174f,
                DrivingAxles = 3,
                FrictionCoefficient = 0.3f, // the Hudson's
            };
            var booster = new RealisticBooster(def, null);
            Port Out(string id, PortValueType type, float value) =>
                new Port(id, new PortDefinition(PortType.READONLY_OUT, type, "OUT"), value);
            var main = Out("steamEngine", PortValueType.TORQUE, 100000f);
            var chest = Out("chest", PortValueType.PRESSURE, 1f + 250f / 14.5038f);
            var boilerPressure = Out("boiler", PortValueType.PRESSURE, 19f);
            var reverser = Out("reverser", PortValueType.CONTROL, 1f);
            var latch = new Port(BoosterFit.Latch.Id, new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"));
            var idle = new Port(BoosterFit.Idle.Id, new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"));
            void Wire(PortReferenceDefinition reference, Port port) =>
                booster.GetAllPortReferences().Single(r => r.id == $"{def.ID}.{reference.ID}").SetPortReference(port);
            Wire(def.MainTorque, main);
            Wire(def.ChestPressure, chest);
            Wire(def.BoilerPressure, boilerPressure);
            Wire(def.Reverser, reverser);
            Wire(def.Latch, latch);
            Wire(def.Idle, idle);
            var torque = booster.GetAllPorts().Single(port => port.id == $"{def.ID}.{def.TorqueOut.ID}");

            Run(booster, 1f);
            Check(Math.Abs(torque.Value - main.Value) < 1f, $"cut out, the booster passes the main engine's torque through ({torque.Value:F0} N·m)");

            idle.Value = 1f;
            Run(booster, 180f);
            idle.Value = 0f;
            Run(booster, 20f);
            latch.Value = 1f;
            Run(booster, 25f);
            var added = (torque.Value - main.Value) / 4.44822f;
            Check(booster.Model.State == BoosterState.Working && added > 11900f && added < 12300f,
                $"idled, then latched in at a stand with the reverser forward, it adds {added:F0} lb on a 1 m wheel after its cocks close");

            reverser.Value = 0.5f;
            Run(booster, 0.5f);
            Check(latch.Value == 0f && booster.Model.State == BoosterState.CutOut && Math.Abs(torque.Value - main.Value) < 1f,
                "hooking the reverser back drops the cab's latch and cuts it out");
        }

        // The replacement for CCL's stoker, against CCL's own, on the S464-730's
        // figures: 10 tender kg/s, firebox multiplier 4.
        private static void CheckStoker(SimGameParams gameParams, FireboxDefinition fireboxDef)
        {
            var dv = Environment.GetEnvironmentVariable("DV_INSTALL_DIR")
                     ?? Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? "", ".local/share/Steam/steamapps/common/Derail Valley");
            var importer = Path.Combine(dv, "Mods", "DVCustomCarLoader", "CCL.Importer.dll");
            if (!File.Exists(importer))
            {
                Console.WriteLine("skip stoker checks: Custom Car Loader is not installed");
                return;
            }
            var cclDefType = Assembly.LoadFrom(importer).GetType("CCL.Importer.Components.Simulation.SteamMechanicalStokerDefinitionInternal");
            var cclDef = (SimComponentDefinition)Activator.CreateInstance(cclDefType);
            cclDef.ID = "autostoker";
            var ccl = cclDef.InstantiateImplementation();
            var values = StokerDefinitionPatch.Read(cclDef);
            const float multiplier = 4f;
            var mine = new RealisticStoker(values, null, multiplier, steamWired: false);
            mine.SetGameParams(gameParams);
            Check(Describe(ccl) == Describe(mine) && ccl.id == mine.id,
                $"the stoker keeps CCL's ID, ports, and references ({ccl.GetAllPorts().Count} ports, {ccl.GetAllPortReferences().Count} references)");

            // The mod's own stoker (the S282's retrofit) has the same contract.
            var ownDef = new RealisticStokerDefinition { ID = "autostoker", MaxTransferRate = 10f, CoalMultiplier = multiplier };
            var own = new RealisticStoker(ownDef.Values(), null, multiplier, steamWired: false);
            Check(Describe(ccl) == Describe(own),
                "the mod's own stoker for the S282 has CCL's stoker's ports and references");

            var boilerDef = new BoilerDefinition
            {
                ID = "boiler", diameter = 2.3f, length = 13f, capacityMultiplier = 0.85f, maxInjectorRate = 36f,
                waterConsumptionMultiplier = 1.5f, safetyValveOpeningPressure = 20.3f, safetyValveClosingPressure = 20f,
                spawnPressure = 15f, spawnWaterLevel = 36000f,
            };
            var boiler = (Boiler)boilerDef.InstantiateImplementation();
            boiler.SetGameParams(gameParams);
            var vessel = InjectorRig.VesselOf(boiler);

            var throttle = new Port("stokerControl", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"), 1f);
            var pressure = new Port("boilerGauge", new PortDefinition(PortType.READONLY_OUT, PortValueType.PRESSURE, "PRESSURE"), 15f);
            var tender = new Port("tenderCoal", new PortDefinition(PortType.READONLY_OUT, PortValueType.COAL, "AMOUNT"), 10000f);
            var consume = new Port("tenderCoal", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.COAL, "CONSUME_EXT_IN"));

            void Connect(SimComponent stoker, SimComponent firebox)
            {
                PortReference R(string name) => stoker.GetAllPortReferences().Single(r => r.id == $"autostoker.{name}");
                R("CONTROL").SetPortReference(throttle);
                R("STEAM_PRESSURE").SetPortReference(pressure);
                R("COAL_AMOUNT").SetPortReference(tender);
                R("COAL_CONSUMPTION").SetPortReference(consume);
                R("FIREBOX_COAL_LEVEL").SetPortReference(firebox.GetAllPorts().Single(p => p.id == $"{Id}.COAL_LEVEL"));
                R("FIREBOX_COAL_CAPACITY").SetPortReference(firebox.GetAllPorts().Single(p => p.id == $"{Id}.COAL_CAPACITY"));
                R("FIREBOX_COAL_CONTROL").SetPortReference(firebox.GetAllPorts().Single(p => p.id == $"{Id}.COAL_CONTROL_EXT_IN"));
            }

            var zoned = Create(fireboxDef, gameParams);
            zoned.SetSaveStateData(new JObject { ["fireOn"] = true, ["coalMass"] = 10f });
            Connect(mine, zoned);
            mine.Attach(zoned, boiler);
            var steamBefore = vessel.mass;
            float drawn = 0f, consumed = 0f, steam = 0f;
            for (var t = 0f; t < 6f; t += 0.02f)
            {
                consume.Value = 0f;
                mine.Tick(0.02f);
                consumed += consume.Value;
                drawn += zoned.LastStoker.Value.Drawn;
                steam += zoned.LastStoker.Value.SteamUsed * 0.02f;
            }
            var zones = zoned.Model.Zones;
            Check(mine.FeedsZonedFire && drawn > 1f && Math.Abs(consumed - multiplier * drawn) < 0.01f * consumed
                  && zones[(int)GrateZone.Front].Coal > zones[(int)GrateZone.UnderDoor].Coal,
                $"it throws coal onto the zones and takes {multiplier:F0} tender kg per firebox kg ({drawn:F1} kg fed, {consumed:F1} tender kg)");
            Check(steamBefore - vessel.mass > 0f && Math.Abs(steamBefore - vessel.mass - steam) < RealisticStoker.SteamLot + 0.02f * steam
                  && mine.GetAllPorts().Single(p => p.id == "autostoker.STOKING_NORMALIZED").Value > 0.6f,
                $"its engine runs and takes its steam from the boiler ({steam:F2} kg in 6 s, {steamBefore - vessel.mass:F2} kg taken)");

            // With the cab's jet valves, the stoker follows them: master shut
            // drops the coal at the door.
            var jets = StokerJetControls.Valves
                .Select(v => new Port(v.Id, new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"), v.Default))
                .ToArray();
            jets[0].Value = 0f;
            var heel = Create(fireboxDef, gameParams);
            heel.SetSaveStateData(new JObject { ["fireOn"] = true, ["coalMass"] = 0.1f });
            var valved = new RealisticStoker(values, null, multiplier, steamWired: true);
            valved.SetGameParams(gameParams);
            Connect(valved, heel);
            valved.Attach(heel, boiler);
            valved.AttachJets(jets);
            for (var t = 0f; t < 6f; t += 0.02f)
                valved.Tick(0.02f);
            var heelZones = heel.Model.Zones;
            var backRow = heelZones[(int)GrateZone.BackLeft].Coal + heelZones[(int)GrateZone.UnderDoor].Coal + heelZones[(int)GrateZone.BackRight].Coal;
            Check(heel.LastStoker.Value.ManifoldPressure == 0f && backRow > 0.75f * heel.Model.CoalMass,
                $"it follows the cab's jet valves: master jet shut, {backRow / heel.Model.CoalMass:P0} of the coal lands at the door");

            // Without the zoned fire, coal goes through a stock firebox's port.
            var stock = new Firebox(fireboxDef);
            stock.SetGameParams(gameParams);
            var fallback = new RealisticStoker(values, null, multiplier, steamWired: true);
            fallback.SetGameParams(gameParams);
            Connect(fallback, stock);
            fallback.Attach(null, boiler);
            var stockConsumed = 0f;
            for (var t = 0f; t < 6f; t += 0.02f)
            {
                consume.Value = 0f;
                fallback.Tick(0.02f);
                stockConsumed += consume.Value;
            }
            var stockCoal = P(stock, "COAL_LEVEL").Value;
            Check(!fallback.FeedsZonedFire && stockCoal > 1f && Math.Abs(stockConsumed - multiplier * stockCoal) < 0.01f * stockConsumed,
                $"without the zoned fire it feeds a stock firebox's port at the right multiplier ({stockCoal:F1} kg, {stockConsumed:F1} tender kg)");

            // A jam survives a save, whether the stoker loads before or after
            // its model is built (audit 2026-10-10: a recreated stoker came
            // back unjammed).
            valved.Model.Jam();
            var jamSave = valved.GetSaveStateData();
            var early = new RealisticStoker(values, null, multiplier, steamWired: true);
            early.SetGameParams(gameParams);
            Connect(early, heel);
            early.SetSaveStateData(jamSave);
            early.Attach(heel, boiler);
            var late = new RealisticStoker(values, null, multiplier, steamWired: true);
            late.SetGameParams(gameParams);
            Connect(late, heel);
            late.Attach(heel, boiler);
            late.SetSaveStateData(jamSave);
            var clear = new RealisticStoker(values, null, multiplier, steamWired: true);
            clear.SetGameParams(gameParams);
            Connect(clear, heel);
            clear.Attach(heel, boiler);
            clear.SetSaveStateData(new JObject());
            Check(valved.HasSaveData && jamSave.Value<bool>(RealisticStoker.JammedSaveKey) && early.Model.Jammed && late.Model.Jammed
                  && !clear.Model.Jammed && clear.GetSaveStateData()[RealisticStoker.JammedSaveKey] == null,
                "a jammed stoker stays jammed through a save and a recreated stoker; a clear one saves nothing");
        }

        private static RealisticFirebox Create(FireboxDefinition def, SimGameParams gameParams)
        {
            var firebox = new RealisticFirebox(def, null);
            firebox.SetGameParams(gameParams);
            return firebox;
        }

        private static Port P(SimComponent component, string portId) =>
            component.GetAllPorts().Single(p => p.id == $"{Id}.{portId}");

        // What one description has that the other lacks, for a failed comparison.
        private static string Difference(string expected, string actual)
        {
            if (expected == actual)
                return "";
            var a = expected.Split('\n');
            var b = actual.Split('\n');
            return " [missing: " + string.Join("; ", a.Except(b)) + "] [extra: " + string.Join("; ", b.Except(a)) + "]";
        }

        private static string Describe(SimComponent component)
        {
            var writeAllowed = typeof(PortReference).GetField("writeAllowed", BindingFlags.Instance | BindingFlags.NonPublic);
            var ports = component.GetAllPorts().Select(p => $"port {p.id} {p.type} {p.valueType} = {p.Value}");
            var references = component.GetAllPortReferences().Select(r => $"ref {r.id} = {r.Value} write {writeAllowed.GetValue(r)}");
            return string.Join("\n", ports.Concat(references).OrderBy(line => line, StringComparer.Ordinal));
        }

        // Every Harmony patch class's target resolves to exactly one method in
        // the game's assemblies, as PatchAll will look it up. One that does
        // not (missing, or ambiguous like Resources.Load(string) against
        // Load<T>(string)) makes PatchAll throw and the whole mod fail to load.
        // The realistic cylinders on the stock engine with the S282's figures,
        // patched as in game, against the same engine with the setting off.
        private static void CheckCylinders(SimGameParams gameParams)
        {
            var harmony = new HarmonyLib.Harmony("RealisticFiring.AdapterCheck.Cylinders");
            foreach (var patch in new[] { typeof(CylinderAdmissionPatch), typeof(CylinderMeanTorquePatch), typeof(CylinderPistonForcePatch), typeof(CylinderExhaustPatch) })
                harmony.CreateClassProcessor(patch).Patch();
            var settings = RealisticFiring.Main.Settings;
            try
            {
                // Reverser for a cut-off: the stock notches from 5 % to 90 %.
                float Notch(float cutoff) => (cutoff - 0.05f) / 0.85f;

                var stockStart = RunEngine(gameParams, false, 1f, 10f);
                var realStart = RunEngine(gameParams, true, 1f, 10f);
                Check(realStart.Torque > 0.9f * stockStart.Torque && realStart.Torque < stockStart.Torque,
                    $"in full gear at a walk the realistic cylinders pull nearly the stock torque ({realStart.Torque:F0} against {stockStart.Torque:F0} N·m)");

                var stockQuarter = RunEngine(gameParams, false, Notch(0.25f), 180f);
                var realQuarter = RunEngine(gameParams, true, Notch(0.25f), 180f);
                Check(realQuarter.Torque > 1.3f * stockQuarter.Torque && realQuarter.Exhaust > stockQuarter.Exhaust,
                    $"notched up to 25 % at 3 turns a second they hold their steam ({realQuarter.Torque:F0} against {stockQuarter.Torque:F0} N·m, " +
                    $"exhaust {realQuarter.Exhaust:F1} against {stockQuarter.Exhaust:F1} bar)");

                var realFull = RunEngine(gameParams, true, 1f, 180f);
                Check(realFull.Flow < 6.6f && realFull.Chest < 13f,
                    $"in full gear at speed the throttle's flow limits them through the steam chest ({realFull.Flow:F2} kg/s, chest {realFull.Chest:F1} bar)");

                // A loco left stock (the C-16) keeps the game's own cylinders with the setting on.
                var keptQuarter = RunEngine(gameParams, true, Notch(0.25f), 180f, leftStock: true);
                Check(Math.Abs(keptQuarter.Torque - stockQuarter.Torque) < 0.01f * Math.Abs(stockQuarter.Torque) + 1f,
                    $"a loco left stock keeps the game's cylinders ({keptQuarter.Torque:F0} against the stock {stockQuarter.Torque:F0} N·m)");

                var slow = RunEngine(gameParams, true, Notch(0.5f), 40f);
                var fast = RunEngine(gameParams, true, Notch(0.5f), 70f);
                Check(Math.Abs(slow.Torque - fast.Torque) < 0.15f * fast.Torque,
                    $"each piston's force averages to the mean pressure's torque across the stock's blend ({slow.Torque:F0} at 40 rpm, {fast.Torque:F0} at 70)");
            }
            finally
            {
                settings.RealisticCylinders = true;
                harmony.UnpatchAll(harmony.Id);
            }
        }

        // The period blower on the game's exhaust, patched as in game: the
        // S060's stock exhaust, then with its fitted blower and natural draft.
        private static void CheckBlower(SimGameParams gameParams)
        {
            var harmony = new HarmonyLib.Harmony("RealisticFiring.AdapterCheck.Blower");
            harmony.CreateClassProcessor(typeof(BlowerPatch)).Patch();
            try
            {
                SteamExhaust New(out Port air, out Port steam)
                {
                    var def = new SteamExhaustDefinition { ID = "exhaust", passiveExhaust = 0.2f, entrainmentRatio = 1.95f, maxBlowerFlow = 0.2f, pressureForMaxBlowerFlow = 3f };
                    var exhaust = (SteamExhaust)def.InstantiateImplementation();
                    exhaust.SetGameParams(gameParams);
                    void Wire(PortReferenceDefinition reference, PortValueType type, float value) =>
                        exhaust.GetAllPortReferences().Single(r => r.id == $"{def.ID}.{reference.ID}")
                            .SetPortReference(new Port(reference.ID.ToLowerInvariant(), new PortDefinition(PortType.READONLY_OUT, type, "OUT"), value));
                    Wire(def.exhaustFlow, PortValueType.MASS_RATE, 0f);
                    Wire(def.engineMaxFlowReader, PortValueType.MASS_RATE, 2f);
                    Wire(def.boilerPressure, PortValueType.PRESSURE, 13.4f);
                    Wire(def.blowerControl, PortValueType.CONTROL, 1f);
                    Wire(def.whistleControl, PortValueType.CONTROL, 0f);
                    Wire(def.damperControl, PortValueType.CONTROL, 1f);
                    air = exhaust.GetAllPorts().Single(p => p.id == $"{def.ID}.{def.airFlowReadOut.ID}");
                    steam = exhaust.GetAllPorts().Single(p => p.id == $"{def.ID}.{def.steamConsumptionReadOut.ID}");
                    return exhaust;
                }

                var stock = New(out var stockAir, out var stockSteam);
                stock.Tick(0.02f);
                var fitted = New(out var fittedAir, out var fittedSteam);
                BlowerFit.Fit(fitted, new SteamDraft(0.4f, 0.3f, fitted.entrainmentRatio, 13.4f));
                fitted.Tick(0.02f);
                Check(Math.Abs(stockAir.Value - 0.59f) < 0.01f && Math.Abs(fittedAir.Value - 2.3f) < 0.01f && Math.Abs(fittedSteam.Value - 0.4f) < 0.01f,
                    $"the S060's period blower wide open draws {fittedAir.Value:F2} kg/s of air for {fittedSteam.Value:F2} kg/s of steam " +
                    $"(the game's {stockAir.Value:F2} for {stockSteam.Value:F2})");
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
            }
        }

        private struct EngineRun
        {
            public float Torque;
            public float Exhaust;
            public float Chest;
            public float Flow;
        }

        // The S282's engine at a steady crank speed and full throttle on 14 bar
        // of saturated steam for 20 s; its torque averaged over the last 4 s.
        private static EngineRun RunEngine(SimGameParams gameParams, bool realistic, float reverser, float rpm, bool leftStock = false)
        {
            RealisticFiring.Main.Settings.RealisticCylinders = realistic;
            var def = new ReciprocatingSteamEngineDefinition
            {
                ID = "steamEngine",
                numCylinders = 2,
                cylinderBore = 0.61f,
                pistonStroke = 0.711f,
                minCutoff = 0.05f,
                maxCutoff = 0.9f,
                throttleMaxFlow = 6f,
                steamChestVolume = 1000f,
            };
            var engine = new ReciprocatingSteamEngine(def);
            engine.SetGameParams(gameParams);
            if (leftStock)
                LeftStock.Keep(engine);
            void Wire(PortReferenceDefinition reference, PortValueType type, float value) =>
                engine.GetAllPortReferences().Single(r => r.id == $"{def.ID}.{reference.ID}")
                    .SetPortReference(new Port(reference.ID.ToLowerInvariant(), new PortDefinition(PortType.READONLY_OUT, type, "OUT"), value));
            Wire(def.throttleControl, PortValueType.CONTROL, 1f);
            Wire(def.reverserControl, PortValueType.CONTROL, reverser);
            Wire(def.cylinderCockControl, PortValueType.CONTROL, 0f);
            Wire(def.intakePressure, PortValueType.PRESSURE, 14f);
            Wire(def.intakeTemperature, PortValueType.TEMPERATURE, 195f);
            Wire(def.intakeQuality, PortValueType.GENERIC, 1f);
            Wire(def.crankRpm, PortValueType.RPM, rpm);
            Wire(def.lubricationNormalized, PortValueType.OIL, 1f);
            Port Out(PortDefinition port) => engine.GetAllPorts().Single(p => p.id == $"{def.ID}.{port.ID}");
            var torque = Out(def.torqueOut);
            var sum = 0.0;
            var samples = 0;
            for (var t = 0f; t < 20f; t += 0.02f)
            {
                engine.Tick(0.02f);
                if (t >= 16f)
                {
                    sum += torque.Value;
                    samples++;
                }
            }
            return new EngineRun
            {
                Torque = (float)(sum / samples),
                Exhaust = Out(def.exhaustPressureReadOut).Value,
                Chest = Out(def.steamChestPressureReadOut).Value,
                Flow = Out(def.exhaustFlowReadOut).Value,
            };
        }

        // ControlSteps reaches a live lever's and valve's notches by name.
        private static void CheckControlSteps()
        {
            const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var missing = new System.Collections.Generic.List<string>();
            void Field(Type type, string name)
            {
                if (HarmonyLib.AccessTools.Field(type, name) == null)
                    missing.Add($"{type.Name}.{name}");
            }
            Field(typeof(DV.CabControls.LeverBase), "spec");
            Field(typeof(DV.CabControls.LeverBase), "steppedJoint");
            Field(typeof(DV.CabControls.RotaryBase), "steppedJoint");
            Field(typeof(DV.CabControls.RotaryBase), "scrollWheelHoverScroll");
            if (typeof(DV.CabControls.RotaryBase).GetProperty("Spec", any) == null)
                missing.Add("RotaryBase.Spec");
            // The specs live in DV.CabControls.Spec, which this check does not reference.
            var lever = HarmonyLib.AccessTools.TypeByName("DV.CabControls.Spec.Lever");
            var rotary = HarmonyLib.AccessTools.TypeByName("DV.CabControls.Spec.Rotary");
            if (lever == null || rotary == null)
                missing.Add("the Lever and Rotary specs");
            else
            {
                Field(lever, "notches");
                Field(lever, "scrollWheelHoverScroll");
                Field(rotary, "notches");
                Field(rotary, "scrollWheelHoverScroll");
            }
            if (typeof(SteppedJoint).GetProperty("SingleNotchAngle", any)?.GetSetMethod(true) == null)
                missing.Add("SteppedJoint.SingleNotchAngle (set)");
            Check(missing.Count == 0, "the members ControlSteps resteps a live control by exist" + (missing.Count > 0 ? $" (not: {string.Join(", ", missing)})" : ""));
        }

        // The fireman's hand on a cab control's port, with a notched lever as
        // the cab's feeder and stepped joint put it back (to tenths), and the
        // player taking it.
        private static void CheckFiremanHand()
        {
            var port = new Port("blower", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"));
            port.ValueUpdatedInternally += v =>
            {
                var notch = (float)Math.Round(v * 10f) / 10f;
                if (notch != v)
                    port.ExternalValueUpdate(notch);
            };
            var moves = 0;
            port.ValueUpdatedInternally += _ => moves++;
            var hand = new FiremanHand("blower", port, 0.5f);
            var t = 0f;
            void Run(float want, float seconds)
            {
                for (var end = t + seconds; t < end; t += 0.02f)
                    hand.Work(want, t);
            }

            Run(0.37f, 2f);
            var travelled = moves;
            Run(0.37f, 10f);
            Check(port.Value == 0.4f && !hand.Yielded(t) && travelled >= 3 && moves - travelled <= 4,
                $"he moves a notched lever at his pace ({travelled} moves on the way) and takes the notch it settles on " +
                $"without fighting it ({moves - travelled} moves in the next 10 s)");
            port.ExternalValueUpdate(0.8f);
            Run(0.37f, 1f);
            Check(hand.Yielded(t) && port.Value == 0.8f, "a lever the player moves is theirs");
            Run(0.37f, FiremanHand.YieldTime);
            Check(!hand.Yielded(t) && port.Value == 0.4f, "after a minute he takes it back");

            var smooth = new Port("injector", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"));
            var dragged = new FiremanHand("injector", smooth, 1.2f);
            for (t = 0f; t < 3f; t += 0.02f)
                dragged.Work(0.5f, t);
            // The player eases it open a little at a time, slower than he would notice tick by tick.
            for (var i = 0; i < 100; i++, t += 0.02f)
            {
                smooth.ExternalValueUpdate(smooth.Value + 0.002f);
                dragged.Work(0.5f, t);
            }
            Check(dragged.Yielded(t) && smooth.Value > 0.6f, $"a slow drag is the player's too (left at {smooth.Value:F2})");
            dragged.Release();
            for (var end = t + 1f; t < end; t += 0.02f)
                dragged.Work(0.5f, t);
            Check(!dragged.Yielded(t) && smooth.Value == 0.5f, "a new order gives it back to him at once");

            var doorPort = new Port("fireboxDoor", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"));
            var door = new FiremanHand("door", doorPort, 3f);
            var halfway = 0f;
            for (t = 0f; t < 1f; t += 0.02f)
            {
                door.Work(1f, t);
                if (t <= 0.17f)
                    halfway = doorPort.Value;
            }
            Check(halfway > 0.2f && halfway < 0.8f && doorPort.Value == 1f,
                $"he swings the door open over a third of a second, not in a jump ({halfway:F2} open at 0.17 s)");
        }

        // SandOnShovelPatch dresses the shovel's load lumps as sand.
        private static void CheckShovelLoads()
        {
            var field = HarmonyLib.AccessTools.Field(typeof(ShovelNonPhysicalCoal), "loadedCoalVisuals");
            Check(field != null && field.FieldType.FullName == "UnityEngine.GameObject[]",
                "the shovel keeps its load lumps in 'loadedCoalVisuals', which a load of flue sand dresses as sand");
        }

        private static void CheckPatchTargets()
        {
            var patches = typeof(RealisticFiring.Main).Assembly.GetTypes()
                .Where(t => t.GetCustomAttributes(typeof(HarmonyLib.HarmonyPatch), true).Length > 0)
                .ToList();
            var bad = new System.Collections.Generic.List<string>();
            foreach (var type in patches)
            {
                try
                {
                    // As Harmony does, skip a patch whose Prepare says no (one for a mod that is not loaded).
                    var prepare = HarmonyLib.AccessTools.DeclaredMethod(type, "Prepare");
                    if (prepare != null && prepare.GetParameters().Length == 0 && !(bool)prepare.Invoke(null, null))
                        continue;
                    var targetMethods = HarmonyLib.AccessTools.DeclaredMethod(type, "TargetMethods");
                    if (targetMethods != null)
                    {
                        var originals = ((System.Collections.Generic.IEnumerable<MethodBase>)targetMethods.Invoke(null, null)).ToList();
                        if (originals.Count == 0 || originals.Any(m => m == null))
                            bad.Add($"{type.Name}: missing target methods");
                        continue;
                    }
                    var targetMethod = HarmonyLib.AccessTools.DeclaredMethod(type, "TargetMethod");
                    if (targetMethod != null)
                    {
                        if (targetMethod.Invoke(null, null) == null)
                            bad.Add($"{type.Name}: no target");
                        continue;
                    }
                    // The class's attributes, merged with each patch method's
                    // own where it names its target, as PatchAll does.
                    var classInfo = HarmonyLib.HarmonyMethodExtensions.GetFromType(type);
                    var methodInfos = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                        .Select(m => HarmonyLib.HarmonyMethodExtensions.GetFromMethod(m))
                        .Where(list => list.Count > 0)
                        .ToList();
                    var targets = methodInfos.Count > 0
                        ? methodInfos.Select(list => HarmonyLib.HarmonyMethod.Merge(classInfo.Concat(list).ToList())).ToList()
                        : new System.Collections.Generic.List<HarmonyLib.HarmonyMethod> { HarmonyLib.HarmonyMethod.Merge(classInfo) };
                    foreach (var info in targets)
                    {
                        var original = info.methodType == HarmonyLib.MethodType.Constructor
                            ? (MethodBase)HarmonyLib.AccessTools.DeclaredConstructor(info.declaringType, info.argumentTypes)
                            : HarmonyLib.AccessTools.DeclaredMethod(info.declaringType, info.methodName, info.argumentTypes);
                        if (original == null)
                            bad.Add($"{type.Name}: no target for {info.methodName ?? "(unnamed)"}");
                    }
                }
                catch (Exception e)
                {
                    bad.Add($"{type.Name}: {e.GetBaseException().Message}");
                }
            }
            Check(bad.Count == 0, $"all {patches.Count} Harmony patch targets resolve" + (bad.Count > 0 ? $" (not: {string.Join("; ", bad)})" : ""));
        }

        private static void CheckFireIronLoading()
        {
            var targets = Items.FireIronLoading.TargetMethods().ToList();
            Check(targets.Count == 2 && targets.All(m => m != null && m.DeclaringType.Assembly == typeof(StartingItemsController).Assembly),
                "fire iron patches only saved-item and purchased-item game methods");
            var resourceLoad = Items.FireIronLoading.ResourceLoad;
            foreach (var target in targets.Where(m => m != null))
            {
                var original = HarmonyLib.PatchProcessor.GetOriginalInstructions(target);
                var patched = Items.FireIronLoading.Transpiler(original).ToList();
                var loadIndexes = Enumerable.Range(0, original.Count).Where(i => original[i].Calls(resourceLoad)).ToList();
                var replacement = HarmonyLib.AccessTools.DeclaredMethod(typeof(Items.FireIronLoading), "LoadPrefab");
                Check(loadIndexes.Count == 1 && patched.Count == original.Count
                      && loadIndexes.All(i => patched[i].Calls(replacement)
                          && patched[i].labels.SequenceEqual(original[i].labels)
                          && patched[i].blocks.SequenceEqual(original[i].blocks))
                      && Enumerable.Range(0, original.Count).Except(loadIndexes).All(i => ReferenceEquals(patched[i], original[i])),
                    $"{target.DeclaringType.Name}.{target.Name}: redirects only the prefab lookup, preserving the stock item lifecycle");
            }
            var patches = typeof(RealisticFiring.Main).Assembly.GetTypes()
                .Where(t => t.GetCustomAttributes(typeof(HarmonyLib.HarmonyPatch), true).Length > 0);
            Check(!patches.Any(t => HarmonyLib.HarmonyMethodExtensions.GetFromType(t).Any(info => info.declaringType == typeof(UnityEngine.Resources)))
                  && !targets.Contains(resourceLoad), "no global Unity Resources.Load patch");
        }

        // The load-hang guard patches the game's pool coroutine and TrainCar.Awake
        // with finalizers: Harmony must accept both against the installed game.
        // A purchase that fails must free the registers: the guard's finalizer
        // installs beside the fire iron's transpiler on the shops' coroutine,
        // and the fields it recovers with are there.
        private static void CheckPurchaseGuard()
        {
            var harmony = new HarmonyLib.Harmony("RealisticFiring.AdapterCheck.Purchase");
            try
            {
                harmony.CreateClassProcessor(typeof(Items.FireIronLoading)).Patch();
                harmony.CreateClassProcessor(typeof(Items.PurchaseGuard)).Patch();
                var info = HarmonyLib.Harmony.GetPatchInfo(Items.PurchaseGuard.TargetMethod());
                Check(info?.Finalizers.Count == 1 && info.Transpilers.Count == 1 && Items.PurchaseGuard.Resolves,
                    "a failed purchase frees the registers: the guard installs beside the iron's lookup and finds the shops' queue");
            }
            catch (Exception e)
            {
                Check(false, $"the purchase guard installs ({e.GetType().Name}: {e.Message})");
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
            }
        }

        private static void CheckCarPoolGuard()
        {
            var harmony = new HarmonyLib.Harmony("RealisticFiring.AdapterCheck.CarPool");
            try
            {
                harmony.CreateClassProcessor(typeof(RealisticFiring.Patches.CarPoolGuard)).Patch();
                harmony.CreateClassProcessor(typeof(RealisticFiring.Patches.TrainCarAwakeReport)).Patch();
                var pool = HarmonyLib.AccessTools.EnumeratorMoveNext(HarmonyLib.AccessTools.Method(typeof(CarSpawner), "InitPoolCoro"));
                var awake = HarmonyLib.AccessTools.Method(typeof(TrainCar), "Awake");
                Check(HarmonyLib.Harmony.GetPatchInfo(pool)?.Finalizers.Count == 1 && HarmonyLib.Harmony.GetPatchInfo(awake)?.Finalizers.Count == 1,
                    "the load-hang guard's finalizers install on the car pool's coroutine and TrainCar.Awake");
            }
            catch (Exception e)
            {
                Check(false, $"the load-hang guard's finalizers install ({e.GetType().Name}: {e.Message})");
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
            }
        }

        private static void Check(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}");
            if (!ok)
                failures++;
        }
    }
}
