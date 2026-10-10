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
            CheckBoilerWater();
            CheckStoker(gameParams, def);
            CheckOilBurner(gameParams);
            CheckClientMirror(gameParams, def);
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
            Check(loaded.Model.FireOn && Math.Abs(loaded.Model.BrickTemperature - oil.Model.BrickTemperature) < 1f
                  && Math.Abs(loaded.Model.OilTemperature - oil.Model.OilTemperature) < 0.1f,
                $"the oil fire saves and loads (brick {oil.Model.BrickTemperature:F0} C, oil {oil.Model.OilTemperature:F0} C)");
        }

        // What a multiplayer host sends clients' views of a loco: captured from
        // the real parts and shown on fresh ones, it reads back the same.
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

        private static void Check(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}");
            if (!ok)
                failures++;
        }
    }
}
