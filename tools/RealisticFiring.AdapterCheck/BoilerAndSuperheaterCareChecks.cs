using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Xml.Serialization;
using DV.Damage;
using DV.Simulation.Cars;
using HarmonyLib;
using LocoSim.Definitions;
using LocoSim.Implementations;
using Newtonsoft.Json.Linq;
using RealisticFiring.Core;
using RealisticFiring.Sim;

namespace RealisticFiring.AdapterCheck
{
    /// <summary>
    /// End-to-end adapter checks for boiler thermal shock and superheater care:
    /// old Relaxed Settings.xml migration, toggles truly disabling all effects,
    /// cold boiler measured heating rate, water leak and efficiency loss,
    /// save/load round-trip and legacy save tolerance, damage sensitivity and
    /// paid engine repair / body-only Renew accounting, and ClientMirror syncing.
    /// </summary>
    internal static class BoilerAndSuperheaterCareChecks
    {
        internal static void Run(Action<bool, string> check)
        {
            var prevSettings = Main.Settings;
            try
            {
                Main.Settings = new Settings();
                CheckSettingsPresetsAndRelaxedMigration(check);
                CheckThermalShockRigAndSaveRepair(check);
                CheckSuperheaterSaveRepairAndMirror(check);
                CheckDamageSensitivityTogglesAndRenew(check);
                CheckNearZeroHealthRefundCap(check);
                CheckGuestHostAuthoritativeFlags(check);
            }
            finally
            {
                Main.Settings = prevSettings;
            }
        }

        private static void CheckSettingsPresetsAndRelaxedMigration(Action<bool, string> check)
        {
            var settings = new Settings();
            check(!settings.BoilerThermalShock && settings.SuperheaterCare && settings.IsStandard,
                "as it comes (Standard), BoilerThermalShock is off and SuperheaterCare on");
            settings.ApplyRelaxed();
            check(!settings.BoilerThermalShock && !settings.SuperheaterCare && settings.IsRelaxed && !settings.IsStandard,
                "Relaxed preset turns off BoilerThermalShock and SuperheaterCare");
            settings.BoilerThermalShock = true;
            settings.ApplyStandard();
            check(!settings.BoilerThermalShock && settings.SuperheaterCare && settings.IsStandard && !settings.IsRelaxed,
                "ApplyStandard turns SuperheaterCare back on and leaves BoilerThermalShock off");
            settings.BoilerThermalShock = true;
            check(!settings.IsStandard && !settings.IsRelaxed, "BoilerThermalShock switched on by the player is neither preset");
            settings.BoilerThermalShock = false;

            // An existing pre-PR Relaxed Settings.xml (DefaultsVersion 3, lacking the
            // two new elements) must migrate cleanly with both toggles kept off.
            const string oldRelaxedXml = "<?xml version=\"1.0\" encoding=\"utf-8\"?><Settings>"
                + "<InjectorTechniqueAndHeat>false</InjectorTechniqueAndHeat>"
                + "<BoilerWaterImpurities>false</BoilerWaterImpurities>"
                + "<RainWetsCoal>false</RainWetsCoal>"
                + "<StokerJams>false</StokerJams>"
                + "<ClinkerTendency>0.3</ClinkerTendency>"
                + "<KeysVersion>1</KeysVersion><DefaultsVersion>3</DefaultsVersion></Settings>";
            var serializer = new XmlSerializer(typeof(Settings));
            Settings relaxed;
            using (var reader = new StringReader(oldRelaxedXml))
                relaxed = (Settings)serializer.Deserialize(reader);
            var migrated = relaxed.ApplyNewDefaults();
            check(migrated && relaxed.DefaultsVersion >= 4 && !relaxed.BoilerThermalShock && !relaxed.SuperheaterCare && relaxed.IsRelaxed,
                "old Relaxed Settings.xml migrates cleanly with BoilerThermalShock and SuperheaterCare kept off");

            // Once migrated, a Relaxed player turning a toggle on keeps their choice.
            relaxed.BoilerThermalShock = true;
            check(!relaxed.ApplyNewDefaults() && relaxed.BoilerThermalShock,
                "migrated Relaxed player turning BoilerThermalShock on later keeps their choice");

            // An existing Standard Settings.xml (DefaultsVersion 3) gets both on.
            const string oldStandardXml = "<?xml version=\"1.0\" encoding=\"utf-8\"?><Settings>"
                + "<KeysVersion>1</KeysVersion><DefaultsVersion>3</DefaultsVersion></Settings>";
            Settings standard;
            using (var reader = new StringReader(oldStandardXml))
                standard = (Settings)serializer.Deserialize(reader);
            check(standard.ApplyNewDefaults() && !standard.BoilerThermalShock && standard.SuperheaterCare && standard.IsStandard,
                "old Standard Settings.xml migrates with SuperheaterCare on and BoilerThermalShock off");
        }

        private static Boiler NewColdBoiler(out Port heat)
        {
            var def = new BoilerDefinition
            {
                ID = "boiler", diameter = 1.7f, length = 8.8f,
                spawnPressure = 1f, spawnWaterLevel = 10000f,
                safetyValveOpeningPressure = 15f, safetyValveClosingPressure = 14.8f,
                crownSheetNormalizedWaterLevel = 0.6f, steamOutletNormalizedWaterLevel = 0.98f,
            };
            var boiler = (Boiler)def.InstantiateImplementation();
            heat = new Port("firebox", new PortDefinition(PortType.READONLY_OUT, PortValueType.HEAT_RATE, "HEAT"));
            boiler.GetAllPortReferences().Single(r => r.id == "boiler.HEAT").SetPortReference(heat);
            return boiler;
        }

        private static ReciprocatingSteamEngine NewEngine(out Port intakePressure, out Port throttle, out Port heat)
        {
            var def = new ReciprocatingSteamEngineDefinition
            {
                ID = "steamEngine", numCylinders = 2, cylinderBore = 0.61f, pistonStroke = 0.711f,
                minCutoff = 0.05f, maxCutoff = 0.9f, steamChestVolume = 1000f, throttleMaxFlow = 6f,
            };
            var engine = (ReciprocatingSteamEngine)def.InstantiateImplementation();
            intakePressure = new Port("boiler", new PortDefinition(PortType.READONLY_OUT, PortValueType.PRESSURE, "PRESSURE")) { Value = 14f };
            throttle = new Port("throttle", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN")) { Value = 0f };
            heat = new Port("firebox", new PortDefinition(PortType.READONLY_OUT, PortValueType.HEAT_RATE, "HEAT")) { Value = 20e6f };
            engine.intakePressure.SetPortReference(intakePressure);
            engine.throttleControl.SetPortReference(throttle);
            return engine;
        }

        private static GameParams NewGameParams(float sensitivity)
        {
            var gp = (GameParams)FormatterServices.GetUninitializedObject(typeof(GameParams));
            AccessTools.Field(typeof(UnityEngine.Object), "m_CachedPtr").SetValue(gp, new IntPtr(1));
            gp.DamageSensitivityModifier = sensitivity;
            return gp;
        }

        private static DamageController NewDamageController(float sensitivity, out TrainDamage mechanicalPT)
        {
            var controller = (DamageController)FormatterServices.GetUninitializedObject(typeof(DamageController));
            AccessTools.Field(typeof(UnityEngine.Object), "m_CachedPtr").SetValue(controller, new IntPtr(1));
            mechanicalPT = new TrainDamage(100f);
            controller.mechanicalPT = mechanicalPT;
            AccessTools.Field(typeof(DamageController), "gameParams").SetValue(controller, NewGameParams(sensitivity));
            return controller;
        }

        private static void CheckThermalShockRigAndSaveRepair(Action<bool, string> check)
        {
            Main.Settings = new Settings { BoilerThermalShock = true }; 
            var boiler = NewColdBoiler(out var heat);
            var rig = BoilerWaterRig.Create(boiler, null, BoilerWaterRig.Guard.None);
            var vessel = (WaterPressureVessel)AccessTools.Field(typeof(Boiler), "vessel").GetValue(boiler);

            // Forced cold heating builds thermal shock wear and warns (measured from vessel.waterTemp rise).
            heat.Value = 22e6f;
            var warned = false;
            for (var i = 0; i < 150; i++)
            {
                rig.BeginTick(1f);
                warned |= rig.ThermalShock.WarmingTooFast;
                vessel.AddEnergy(heat.Value * 1f);
                vessel.Update();
            }
            var wear = rig.ThermalShock.Wear;
            check(warned && wear > 0.05f && rig.ThermalShockEfficiencyLoss > 0f,
                $"forcing a cold boiler through BoilerWaterRig warns and builds tube/stay wear ({wear:P1})");

            // At working pressure (fire eased), worn tubes weep water slowly without adding more shock wear.
            heat.Value = 0f;
            vessel.pressure = 14f;
            vessel.waterTemp = 195f;
            var massBefore = vessel.mass;
            for (var i = 0; i < 200; i++)
                rig.BeginTick(1f);
            check(rig.TubeLeakRate > 0f && vessel.mass < massBefore - 1f,
                $"strained tubes weep water under pressure ({rig.TubeLeakRate * 60f:F2} kg/min, lost {massBefore - vessel.mass:F1} kg)");

            // Save and load round-trip preserves wear; legacy save without keys loads at 0.
            var saved = new JObject();
            rig.Save(saved);
            var reloadedBoiler = NewColdBoiler(out _);
            var reloadedRig = BoilerWaterRig.Create(reloadedBoiler, null, BoilerWaterRig.Guard.None);
            BoilerWaterRig.OnLoaded(reloadedBoiler, saved);
            check(Math.Abs(reloadedRig.ThermalShock.Wear - wear) < 1e-4f,
                $"thermal-shock wear survives boiler save/load ({reloadedRig.ThermalShock.Wear:P1})");

            var legacyBoiler = NewColdBoiler(out _);
            var legacyRig = BoilerWaterRig.Create(legacyBoiler, null, BoilerWaterRig.Guard.None);
            BoilerWaterRig.OnLoaded(legacyBoiler, new JObject());
            check(legacyRig.ThermalShock.Wear == 0f, "older boiler save without thermal-shock keys loads cleanly at 0");

            // Paid engine repair clears tube/stay leak wear proportionally.
            for (var i = 0; i < 60; i++)
                reloadedRig.NoteHealth(1f, 0.8f);
            reloadedRig.NoteHealth(1f, 0.9f);
            var half = reloadedRig.ThermalShock.Wear;
            reloadedRig.NoteHealth(1f, 1f);
            check(Math.Abs(half - wear * 0.5f) < 0.01f && reloadedRig.ThermalShock.Wear == 0f,
                $"engine repair clears boiler thermal-shock wear proportionally (half {half:P1}, full {reloadedRig.ThermalShock.Wear:P1})");
        }

        private static void CheckSuperheaterSaveRepairAndMirror(Action<bool, string> check)
        {
            Main.Settings = new Settings { BoilerThermalShock = true }; 
            var engine = NewEngine(out _, out var throttle, out var heat);
            var boiler = NewColdBoiler(out _);
            var waterRig = BoilerWaterRig.Create(boiler, engine, BoilerWaterRig.Guard.None);
            var spec = new SuperheaterSpec
            {
                Kind = "Type A", FullSuperheat = 136f, HalfSuperheat = 98f, StandingSuperheat = 400f, TimeConstant = 75f,
            };
            var sh = EngineSuperheater.Attach(null, engine, spec, heat, 20e6f);

            // Start working normally, then shut throttle on roaring fire.
            throttle.Value = 1f;
            engine.intakeFlowReadOut.Value = 8f;
            sh.Step(engine, 1f);
            throttle.Value = 0f;
            engine.intakeFlowReadOut.Value = 0f;
            for (var i = 0; i < 500; i++)
                sh.Step(engine, 1f);
            var wear = sh.Model.Wear;
            check(sh.Model.Overheating && wear > 0.1f && sh.Model.ElementTemperature > Superheater.SafeElementTemperature,
                $"shut-off hot fire overheats EngineSuperheater ({sh.Model.ElementTemperature:F0} C, wear {wear:P1})");

            // Turning SuperheaterCare off restores SuperheatEfficiency to 1f even with saved wear > 0.
            Main.Settings.SuperheaterCare = false;
            sh.Step(engine, 1f);
            check(sh.Model.SuperheatEfficiency == 1f && !sh.Model.Overheating && Math.Abs(sh.Model.Superheat - sh.Model.Elements) < 0.1f,
                "turning SuperheaterCare off restores full SuperheatEfficiency even with saved wear");
            Main.Settings.SuperheaterCare = true;
            sh.Step(engine, 1f);
            wear = sh.Model.Wear;

            // Save and load round-trip on ReciprocatingSteamEngine, including pending load before Attach.
            var saved = new JObject();
            EngineSuperheater.Save(engine, saved);
            var loadedEngine = NewEngine(out _, out _, out var loadedHeat);
            EngineSuperheater.Load(loadedEngine, saved);
            var loadedSh = EngineSuperheater.Attach(null, loadedEngine, spec, loadedHeat, 20e6f);
            check(Math.Abs(loadedSh.Model.Wear - wear) < 1e-4f,
                $"superheater wear survives save/load even when loaded before Attach ({loadedSh.Model.Wear:P1})");

            // Legacy save without key resets wear to 0.
            EngineSuperheater.Load(loadedEngine, new JObject());
            check(loadedSh.Model.Wear == 0f, "older engine save without superheater wear key loads cleanly at 0");

            // ClientMirror syncs both BoilerThermalShock and EngineSuperheater to guests.
            waterRig.ThermalShock.Restore(0.3f);
            waterRig.TubeLeakRate = 0.04f;
            var guestBoiler = NewColdBoiler(out _);
            var guestWater = BoilerWaterRig.Create(guestBoiler, null, BoilerWaterRig.Guard.None);
            var guestEngine = NewEngine(out _, out _, out var guestHeat);
            var guestSh = EngineSuperheater.Attach(null, guestEngine, spec, guestHeat, 20e6f);
            var packet = ClientMirror.Capture(null, null, waterRig, null, null, null, null, sh);
            ClientMirror.Apply(packet, null, null, guestWater, null, null, "guest", null, null, guestSh);
            check(Math.Abs(guestWater.ThermalShock.Wear - 0.3f) < 1e-4f && Math.Abs(guestWater.TubeLeakRate - 0.04f) < 1e-4f
                  && Math.Abs(guestSh.Model.Wear - wear) < 1e-4f && guestSh.Model.Overheating && guestSh.Mirrored
                  && Math.Abs(guestSh.Model.ElementTemperature - sh.Model.ElementTemperature) < 0.1f,
                "ClientMirror syncs boiler thermal shock and superheater element state to multiplayer guests");

            // Paid engine repair on BoilerWaterRig also repairs attached EngineSuperheater.
            for (var i = 0; i < 60; i++)
                waterRig.NoteHealth(1f, 0.8f);
            waterRig.NoteHealth(1f, 1f);
            check(sh.Model.Wear == 0f, "paid engine repair clears superheater element wear");
        }

        private static void CheckDamageSensitivityTogglesAndRenew(Action<bool, string> check)
        {
            Main.Settings = new Settings { BoilerThermalShock = true }; 
            var engine = NewEngine(out _, out _, out var heat);
            var boiler = NewColdBoiler(out _);
            var rig = BoilerWaterRig.Create(boiler, engine, BoilerWaterRig.Guard.FusiblePlug);
            var spec = new SuperheaterSpec
            {
                Kind = "Type A", FullSuperheat = 136f, HalfSuperheat = 98f, StandingSuperheat = 400f, TimeConstant = 75f,
            };
            var sh = EngineSuperheater.Attach(null, engine, spec, heat, 20e6f);

            // Wire a DamageController with 0.5x damage sensitivity.
            var damageField = AccessTools.Field(typeof(BoilerWaterRig), "damage");
            var damage = NewDamageController(0.5f, out var mechanicalPT);
            damageField.SetValue(rig, damage);

            // Apply 50% thermal shock wear and 50% superheater wear at 0.5x sensitivity.
            rig.ThermalShock.Restore(0.5f);
            sh.Model.Restore(0.5f);
            rig.BeginTick(0.1f);
            var expectedCharge = (BoilerThermalShock.EngineWearWeight * 0.5f + Superheater.EngineWearWeight * 0.5f) * 0.5f;
            check(Math.Abs((1f - mechanicalPT.HealthPercentage) - expectedCharge) < 1e-3f,
                $"ChargeWear respects DV damage sensitivity ({1f - mechanicalPT.HealthPercentage:P2} vs expected {expectedCharge:P2})");

            // Turning both toggles off refunds the charged mechanicalPT wear cleanly.
            Main.Settings.BoilerThermalShock = false;
            Main.Settings.SuperheaterCare = false;
            rig.BeginTick(0.1f);
            check(Math.Abs(mechanicalPT.HealthPercentage - 1f) < 1e-3f
                  && rig.ThermalShockEfficiencyLoss == 0f && rig.TubeLeakRate == 0f,
                "toggles off refund engine wear charge and zero thermal-shock efficiency loss and leak");

            // Re-enable at 1.0x sensitivity, add unrelated 20% engine wear, then trigger
            // body-only Renew (melted plug): Renew must credit back ONLY the tracked
            // thermal-shock + superheater share and leave zero orphaned damage.
            Main.Settings.BoilerThermalShock = true;
            Main.Settings.SuperheaterCare = true;
            AccessTools.Field(typeof(DamageController), "gameParams").SetValue(damage, NewGameParams(1f));
            rig.ThermalShock.SetEnabled(true);
            sh.Model.SetCareEnabled(true);
            rig.BeginTick(0.1f);
            mechanicalPT.ApplyDamage(20f); // 20% unrelated cylinder wear
            var expectedTracked = BoilerThermalShock.EngineWearWeight * 0.5f + Superheater.EngineWearWeight * 0.5f;
            check(Math.Abs(mechanicalPT.HealthPercentage - (0.80f - expectedTracked)) < 1e-3f,
                $"before body-only Renew: health reflects unrelated + tracked wear ({mechanicalPT.HealthPercentage:P2})");

            rig.Renew("body-only repair check");
            check(Math.Abs(mechanicalPT.HealthPercentage - 0.80f) < 1e-3f && rig.ThermalShock.Wear == 0f && sh.Model.Wear == 0f,
                $"Renew credits back tracked boiler/superheater wear cleanly without leaving orphaned damage ({mechanicalPT.HealthPercentage:P2})");
        }

        private static void CheckNearZeroHealthRefundCap(Action<bool, string> check)
        {
            Main.Settings = new Settings { BoilerThermalShock = true }; 
            var engine = NewEngine(out _, out _, out var heat);
            var boiler = NewColdBoiler(out _);
            var rig = BoilerWaterRig.Create(boiler, engine, BoilerWaterRig.Guard.FusiblePlug);
            var spec = new SuperheaterSpec
            {
                Kind = "Type A", FullSuperheat = 136f, HalfSuperheat = 98f, StandingSuperheat = 400f, TimeConstant = 75f,
            };
            var sh = EngineSuperheater.Attach(null, engine, spec, heat, 20e6f);

            var damage = NewDamageController(1f, out var mechanicalPT);
            AccessTools.Field(typeof(BoilerWaterRig), "damage").SetValue(rig, damage);

            // Engine already worn down to 2 HP out of 100 HP (98 HP unrelated wear).
            mechanicalPT.ApplyDamage(98f);
            check(Math.Abs(mechanicalPT.CurrentHitPoints - 2f) < 1e-3f,
                $"near-zero health setup starts at 2 HP ({mechanicalPT.CurrentHitPoints:F2} HP)");

            // Request a 12 HP thermal-shock charge + 10 HP superheater charge (22 HP total).
            // Only 2 HP can actually be removed before clamping at 0.
            rig.ThermalShock.Restore(1f);
            sh.Model.Restore(1f);
            rig.BeginTick(0.1f);
            check(mechanicalPT.CurrentHitPoints == 0f,
                $"near-zero engine clamps at 0 HP after requested care charge ({mechanicalPT.CurrentHitPoints:F2} HP)");

            // Disabling care must refund AT MOST the 2 HP actually removed, never 12 or 22 HP.
            Main.Settings.BoilerThermalShock = false;
            Main.Settings.SuperheaterCare = false;
            rig.BeginTick(0.1f);
            check(Math.Abs(mechanicalPT.CurrentHitPoints - 2f) < 1e-2f,
                $"disabling care at near-zero health restores only the 2 HP actually removed, never repairing unrelated wear ({mechanicalPT.CurrentHitPoints:F2} HP)");

            // Re-enable care (2 -> 0 HP again), then call Renew: must restore AT MOST 2 HP.
            Main.Settings.BoilerThermalShock = true;
            Main.Settings.SuperheaterCare = true;
            rig.ThermalShock.SetEnabled(true);
            sh.Model.SetCareEnabled(true);
            rig.BeginTick(0.1f);
            check(mechanicalPT.CurrentHitPoints == 0f,
                $"re-enabling care re-charges the remaining 2 HP to 0 ({mechanicalPT.CurrentHitPoints:F2} HP)");
            rig.Renew("near-zero-health renew check");
            check(Math.Abs(mechanicalPT.CurrentHitPoints - 2f) < 1e-2f,
                $"Renew at near-zero health restores only the 2 HP actually removed ({mechanicalPT.CurrentHitPoints:F2} HP)");

            // When TrainDamage ignores damage, 0 HP is removed so 0 HP is tracked/refunded.
            mechanicalPT.RepairDamage(48f); // now 50 HP
            mechanicalPT.IgnoreDamage(true);
            rig.ThermalShock.Restore(1f);
            sh.Model.Restore(1f);
            rig.BeginTick(0.1f);
            Main.Settings.BoilerThermalShock = false;
            Main.Settings.SuperheaterCare = false;
            rig.BeginTick(0.1f);
            rig.Renew("ignored-damage renew check");
            check(Math.Abs(mechanicalPT.CurrentHitPoints - 50f) < 1e-2f,
                $"ignored ApplyDamage tracks 0 HP and never over-refunds on disable or Renew ({mechanicalPT.CurrentHitPoints:F2} HP)");
        }

        private static TrainCar NewCarWithEngine(ReciprocatingSteamEngine engine)
        {
            var car = (TrainCar)FormatterServices.GetUninitializedObject(typeof(TrainCar));
            AccessTools.Field(typeof(UnityEngine.Object), "m_CachedPtr").SetValue(car, new IntPtr(1));
            car.couplers = new Coupler[2];
            var simController = (SimController)FormatterServices.GetUninitializedObject(typeof(SimController));
            AccessTools.Field(typeof(UnityEngine.Object), "m_CachedPtr").SetValue(simController, new IntPtr(1));
            var flow = (SimulationFlow)FormatterServices.GetUninitializedObject(typeof(SimulationFlow));
            AccessTools.Field(typeof(SimulationFlow), "OrderedSimComps")
                .SetValue(flow, (IReadOnlyList<SimComponent>)new SimComponent[] { engine });
            AccessTools.Field(typeof(SimController), "simFlow").SetValue(simController, flow);
            AccessTools.Field(typeof(TrainCar), "_simController").SetValue(car, (simController, true));
            EngineCylinders.Of(engine);
            return car;
        }

        private static InjectorRig NewStubInjectorRig(TrainCar car)
        {
            var rig = (InjectorRig)FormatterServices.GetUninitializedObject(typeof(InjectorRig));
            AccessTools.Field(typeof(InjectorRig), "<Car>k__BackingField").SetValue(rig, car);
            AccessTools.Field(typeof(InjectorRig), "<Circuits>k__BackingField").SetValue(rig, new List<InjectorCircuit>());
            // The Water page asks whether a guest works the hand pump.
            AccessTools.Field(typeof(InjectorRig), "remotePumpers").SetValue(rig, new HashSet<byte>());
            return rig;
        }

        private static void CheckGuestHostAuthoritativeFlags(Action<bool, string> check)
        {
            var prevClientCheck = Net.ClientCheck;
            var spec = new SuperheaterSpec
            {
                Kind = "Type A", FullSuperheat = 136f, HalfSuperheat = 98f, StandingSuperheat = 400f, TimeConstant = 75f,
            };
            var hostEngine = NewEngine(out _, out var hostThrottle, out var hostHeat);
            var hostBoiler = NewColdBoiler(out _);
            var hostWater = BoilerWaterRig.Create(hostBoiler, hostEngine, BoilerWaterRig.Guard.None);
            var hostSh = EngineSuperheater.Attach(null, hostEngine, spec, hostHeat, 20e6f);

            var guestEngine = NewEngine(out _, out _, out var guestHeat);
            var guestCar = NewCarWithEngine(guestEngine);
            var guestBoiler = NewColdBoiler(out var guestBoilerHeat);
            var guestWater = BoilerWaterRig.Create(guestBoiler, null, BoilerWaterRig.Guard.None, guestCar);
            AccessTools.Field(typeof(BoilerWaterRig), "damage").SetValue(guestWater, NewDamageController(1f, out _));
            var guestSh = EngineSuperheater.Attach(guestCar, guestEngine, spec, guestHeat, 20e6f);
            var guestInjectorRig = NewStubInjectorRig(guestCar);

            var gameParams = new SimGameParams(false, false, false, 1f, 1f);
            var coalDef = new FireboxDefinition { ID = "firebox", maxCoalCapacity = 120f, burnTime = 180f, efficiencyAtMaxCombustion = 0.7f };
            var guestCoal = new RealisticFirebox(coalDef, guestCar);
            guestCoal.SetGameParams(gameParams);
            guestCoal.SetSaveStateData(new JObject { ["fireOn"] = true, ["coalMass"] = 40f });

            var oilDef = new FireboxDefinition { ID = "firebox", maxCoalCapacity = 200f, burnTime = 100f, efficiencyAtMaxCombustion = 0.6f, startupMaxPressure = 22f };
            var guestOil = new RealisticOilFirebox(oilDef, guestCar);
            guestOil.SetGameParams(gameParams);
            guestOil.Model.PrepareFire();
            guestOil.Model.Restore(true, OilFire.HotBrick, 60f, 0f);

            var waterRows = AccessTools.Method(typeof(FireOverlay), "WaterRows");
            var engineRows = AccessTools.Method(typeof(FireOverlay), "EngineRows");

            try
            {
                // ---- Direction A: Host ON (Standard), Guest OFF (Relaxed) ----
                Net.ClientCheck = () => false;
                Main.Settings = new Settings { BoilerThermalShock = true }; 
                hostWater.ThermalShock.Mirror(0.32f, 0.55f, true);
                hostWater.TubeLeakRate = 0.045f;
                hostThrottle.Value = 1f;
                hostEngine.intakeFlowReadOut.Value = 8f;
                hostSh.Step(hostEngine, 1f);
                hostThrottle.Value = 0f;
                hostEngine.intakeFlowReadOut.Value = 0f;
                for (var i = 0; i < 500; i++)
                    hostSh.Step(hostEngine, 1f);
                var packetHostOn = ClientMirror.Capture(null, null, hostWater, null, null, null, null, hostSh);

                // Guest runs Relaxed (both toggles OFF) with Net.IsClient == true.
                Main.Settings = new Settings();
                Main.Settings.ApplyRelaxed();
                Net.ClientCheck = () => true;
                ClientMirror.Apply(packetHostOn, null, null, guestWater, null, null, "guest", null, null, guestSh);

                // Local guest BeginTick and EngineSuperheater.Step must NOT overwrite mirrored host state!
                var guestVessel = (WaterPressureVessel)AccessTools.Field(typeof(Boiler), "vessel").GetValue(guestBoiler);
                guestBoilerHeat.Value = 0f;
                guestVessel.waterTemp = 25f;
                guestWater.BeginTick(1f);
                guestSh.Step(guestEngine, 1f);

                var apiOn = Api.ReadParts(guestCoal, null, null, guestWater, guestCar);
                var apiBoilerOn = (Dictionary<string, object>)apiOn["boiler"];
                var apiEngineOn = (Dictionary<string, object>)apiOn["engine"];
                var waterCardOn = new FireOverlay.Card();
                waterRows.Invoke(null, new object[] { waterCardOn, guestInjectorRig });
                var tubesRowOn = waterCardOn.Rows.FirstOrDefault(r => r.Label == "Tubes, stays");
                var engineCardOn = new FireOverlay.Card();
                engineRows.Invoke(null, new object[] { engineCardOn, guestCar, guestCoal });
                var elemRowOn = engineCardOn.Rows.FirstOrDefault(r => r.Label == "Elements");
                var coalAdviceOn = FireOverlay.CoalAdvice(guestCoal, null, guestWater);
                var oilReadingOn = FireController.ReadOil(guestOil);

                check(guestWater.ThermalShockActive && guestSh.SuperheaterCareActive
                      && guestWater.ThermalShock.WarmingTooFast && Math.Abs(guestWater.ThermalShock.Wear - 0.32f) < 1e-4f
                      && Math.Abs(guestWater.TubeLeakRate - 0.045f) < 1e-4f && guestSh.Model.Overheating
                      && (bool)apiBoilerOn["warmingTooFast"] && (float)apiBoilerOn["thermalShockWearPct"] > 30f
                      && (float)apiBoilerOn["tubeLeakKgPerS"] > 0.04f
                      && (bool)apiEngineOn["superheaterOverheating"] && (float)apiEngineOn["superheaterHealthPct"] < 95f
                      && tubesRowOn.Value != null && tubesRowOn.Value.Contains("WARMING TOO FAST")
                      && elemRowOn.Value != null && elemRowOn.Value.Contains("HOT") && elemRowOn.Value.Contains("health")
                      && coalAdviceOn.Text.Contains("Ease the fire")
                      && oilReadingOn.BoilerWarmingTooFast && oilReadingOn.SuperheaterHot,
                    "host ON / guest Relaxed: guest Api, FireOverlay/tablet rows, and coal/oil advice follow host enable flags and survive guest ticks");

                // ---- Direction B: Host OFF (Relaxed), Guest ON (Standard) ----
                Net.ClientCheck = () => false;
                Main.Settings = new Settings();
                Main.Settings.ApplyRelaxed(); // Host has both care toggles OFF (even with saved wear!)
                hostWater.ThermalShock.SetEnabled(false);
                hostWater.TubeLeakRate = 0f;
                hostSh.Step(hostEngine, 1f);
                var packetHostOff = ClientMirror.Capture(null, null, hostWater, null, null, null, null, hostSh);

                // Guest runs Standard (both toggles ON) with Net.IsClient == true.
                Main.Settings = new Settings { BoilerThermalShock = true }; 
                Net.ClientCheck = () => true;
                ClientMirror.Apply(packetHostOff, null, null, guestWater, null, null, "guest", null, null, guestSh);

                // Guest local ticks with high heat must NOT re-enable or overwrite host-off state.
                guestBoilerHeat.Value = 25e6f;
                guestWater.BeginTick(1f);
                guestSh.Step(guestEngine, 1f);

                var apiOff = Api.ReadParts(guestCoal, null, null, guestWater, guestCar);
                var apiBoilerOff = (Dictionary<string, object>)apiOff["boiler"];
                var apiEngineOff = (Dictionary<string, object>)apiOff["engine"];
                var waterCardOff = new FireOverlay.Card();
                waterRows.Invoke(null, new object[] { waterCardOff, guestInjectorRig });
                var tubesRowOff = waterCardOff.Rows.FirstOrDefault(r => r.Label == "Tubes, stays");
                var engineCardOff = new FireOverlay.Card();
                engineRows.Invoke(null, new object[] { engineCardOff, guestCar, guestCoal });
                var elemRowOff = engineCardOff.Rows.FirstOrDefault(r => r.Label == "Elements");
                var coalAdviceOff = FireOverlay.CoalAdvice(guestCoal, null, guestWater);
                var oilReadingOff = FireController.ReadOil(guestOil);

                check(!guestWater.ThermalShockActive && !guestSh.SuperheaterCareActive
                      && !(bool)apiBoilerOff["warmingTooFast"] && (float)apiBoilerOff["thermalShockWearPct"] == 0f
                      && (float)apiBoilerOff["thermalShockLossPct"] == 0f && (float)apiBoilerOff["tubeLeakKgPerS"] == 0f
                      && !(bool)apiEngineOff["superheaterOverheating"] && (float)apiEngineOff["superheaterHealthPct"] == 100f
                      && tubesRowOff.Value == "off"
                      && elemRowOff.Value != null && elemRowOff.Value.EndsWith("· off")
                      && !coalAdviceOff.Text.Contains("Ease the fire") && !coalAdviceOff.Text.Contains("Superheater")
                      && !oilReadingOff.BoilerWarmingTooFast && !oilReadingOff.SuperheaterHot,
                    "host OFF / guest Standard: guest Api, FireOverlay/tablet rows, and coal/oil advice show off and ignore guest Standard toggles");
            }
            finally
            {
                Net.ClientCheck = prevClientCheck;
                var byCar = (Dictionary<TrainCar, BoilerWaterRig>)AccessTools.Field(typeof(BoilerWaterRig), "ByCar").GetValue(null);
                byCar?.Remove(guestCar);
            }
        }
    }
}
