using System;
using System.Linq;
using System.Reflection;
using LocoSim.Definitions;
using LocoSim.Implementations;
using Newtonsoft.Json.Linq;
using RealisticFiring.Core;
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

            CheckInjector(gameParams);

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
            lever.Value = 1f;
            var spilled = Run(rig, 0.5f);
            Check(rig.Flow.State == InjectorState.PickingUp && spilled > 0f && rig.BoilerVessel.mass <= massBefore,
                $"at {pressureBefore:F0} bar it spills while picking up ({spilled:F0} tender units in 0.5 s, nothing into the boiler)");
            var drawn = Run(rig, 3f);
            Check(rig.Flow.State == InjectorState.Working && rig.BoilerVessel.mass > massBefore + 10f
                  && rig.Flow.FeedTemperature > 55f && rig.Flow.FeedTemperature < 95f && drawn > 0f,
                $"then it feeds the boiler ({rig.BoilerVessel.mass - massBefore:F0} kg in, {rig.Flow.WaterIn:F1} kg/s at {rig.Flow.FeedTemperature:F0} C, " +
                $"steam {rig.Flow.SteamUsed:F2} kg/s, pressure {pressureBefore:F1} -> {rig.BoilerVessel.pressure:F1} bar)");

            var cold = NewBoiler(2f, lever);
            InjectorRig.TryCreate(null, cold, out _);
            InjectorRig.TryGet(cold, out var coldRig);
            var coldMass = coldRig.BoilerVessel.mass;
            var coldSpill = Run(coldRig, 3f);
            Check(coldRig.Flow.State == InjectorState.KnockedOff && coldSpill > 0f && coldRig.BoilerVessel.mass <= coldMass,
                $"below its starting pressure it only spills ({coldSpill:F0} tender units in 3 s)");

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

        private static void Check(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}");
            if (!ok)
                failures++;
        }
    }
}
