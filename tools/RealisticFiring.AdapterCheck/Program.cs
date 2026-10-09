using System;
using System.IO;
using System.Linq;
using System.Reflection;
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

        private static int Main()
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
            Check(save.Value<bool>("fireOn") && save["coalMass"] != null && save["coalEnvDmg"] != null && save["rfZones"] is JArray,
                "save has the stock keys plus rfZones");
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
            CheckStoker(gameParams, def);
            CheckBooster();

            CheckPatchTargets();
            Console.WriteLine(failures == 0 ? "All adapter checks passed" : $"{failures} adapter checks failed");
            return failures == 0 ? 0 : 1;
        }

        private static void Run(SimComponent component, float seconds)
        {
            for (var t = 0f; t < seconds; t += 0.02f)
                component.Tick(0.02f);
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

            var slammed = NewBoiler(12f, lever);
            InjectorRig.TryCreate(null, slammed, out _);
            InjectorRig.TryGet(slammed, out var slammedRig);
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
            Check(rig.BoilerVessel.mass <= hotMass, "a hose cannot fill a boiler under pressure");
            rig.HydrantFilling = false;

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
        }

        private static RealisticFirebox Create(FireboxDefinition def, SimGameParams gameParams)
        {
            var firebox = new RealisticFirebox(def, null);
            firebox.SetGameParams(gameParams);
            return firebox;
        }

        private static Port P(SimComponent component, string portId) =>
            component.GetAllPorts().Single(p => p.id == $"{Id}.{portId}");

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
                    var targetMethod = HarmonyLib.AccessTools.DeclaredMethod(type, "TargetMethod");
                    MethodBase original;
                    if (targetMethod != null)
                    {
                        original = (MethodBase)targetMethod.Invoke(null, null);
                    }
                    else
                    {
                        var info = HarmonyLib.HarmonyMethod.Merge(HarmonyLib.HarmonyMethodExtensions.GetFromType(type));
                        original = info.methodType == HarmonyLib.MethodType.Constructor
                            ? (MethodBase)HarmonyLib.AccessTools.DeclaredConstructor(info.declaringType, info.argumentTypes)
                            : HarmonyLib.AccessTools.DeclaredMethod(info.declaringType, info.methodName, info.argumentTypes);
                    }
                    if (original == null)
                        bad.Add($"{type.Name}: no target");
                }
                catch (Exception e)
                {
                    bad.Add($"{type.Name}: {e.GetBaseException().Message}");
                }
            }
            Check(bad.Count == 0, $"all {patches.Count} Harmony patch targets resolve" + (bad.Count > 0 ? $" (not: {string.Join("; ", bad)})" : ""));
        }

        private static void Check(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}");
            if (!ok)
                failures++;
        }
    }
}
