using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using HarmonyLib;
using LocoSim.Definitions;
using LocoSim.Implementations;
using Newtonsoft.Json.Linq;
using RealisticFiring.Core;
using RealisticFiring.Patches;
using RealisticFiring.Sim;

namespace RealisticFiring.AdapterCheck
{
 internal static class RearrangedS282Checks
    {
        private static readonly AccessTools.FieldRef<Boiler, float> TotalVolume = AccessTools.FieldRefAccess<Boiler, float>("totalVolume");
        private static readonly AccessTools.FieldRef<Boiler, WaterPressureVessel> Vessel = AccessTools.FieldRefAccess<Boiler, WaterPressureVessel>("vessel");

        internal static void Run(SimGameParams gameParams, Action<bool, string> check)
        {
            var wasImpurities = Main.Settings.BoilerWaterImpurities;
            var wasQuality = Main.Settings.BoilerWaterQuality;
            var wasCylinders = Main.Settings.RealisticCylinders;
            try
            {
                Main.Settings.BoilerWaterImpurities = true;
                Main.Settings.BoilerWaterQuality = FeedWaterQuality.Bad;
                Main.Settings.RealisticCylinders = true;
                CheckBoosterOrder(check);
                CheckResize(gameParams, check);
                CheckCarSave(gameParams, check);
                CheckCylinders(check);
                CheckSteamPath(gameParams, check);
            }
            finally
            {
                Main.Settings.BoilerWaterImpurities = wasImpurities;
                Main.Settings.BoilerWaterQuality = wasQuality;
                Main.Settings.RealisticCylinders = wasCylinders;
            }
        }

        private static void CheckBoosterOrder(Action<bool, string> check)
        {
            var prefix = new HarmonyMethod(AccessTools.Method(typeof(BoosterPatch), "Prefix"));
            check(prefix.after != null && prefix.after.Contains("RearrangedS282") && prefix.priority == Priority.Low,
                "the booster's graph prefix runs after Rearranged S282's (its Harmony ID) and at low priority, so his graph is built before the booster joins it");
        }

        // Rearranged 3.0.1's BodyBoilerPhysics: the caches written, then a new
        // vessel of the new volume holding the old water's mass and enthalpy scaled by it.
        private static void Resize(Boiler boiler, float ratio)
        {
            var before = Vessel(boiler);
            var volume = TotalVolume(boiler) * ratio;
            var after = new WaterPressureVessel(volume, before.pressure, before.mass * ratio) { enthalpy = before.enthalpy * ratio };
            after.Update();
            TotalVolume(boiler) = volume;
            Vessel(boiler) = after;
        }

        private static float Ppm(BoilerWaterRig rig) => (rig.Water.Solids + rig.Water.SuspendedSludge + rig.Water.SettledSludge) / rig.MassNow * 1e6f;

        private static void CheckResize(SimGameParams gameParams, Action<bool, string> check)
        {
            var (boiler, rig, _, _, _, _, _) = GaugeGlassChokeChecks.BuildS282(gameParams);
            rig.Foul(3000f);
            rig.MeltPlug();
            rig.GlassChoke.Restore(0.4f, float.NaN, 30f);
            rig.Fouling.Restore(20f, 0.05f);
            var ppm = Ppm(rig);
            var loss = rig.FoulingLoss;
            var volume = TotalVolume(boiler);
            var mass = rig.MassNow;

            Resize(boiler, 1.2f);
            GaugeGlassChokeChecks.StepBoiler(boiler, rig, 0.1f);
            check(Math.Abs(TotalVolume(boiler) - volume * 1.2f) < 1f && Math.Abs(rig.MassNow - mass * 1.2f) < mass * 0.01f
                  && Math.Abs(Ppm(rig) - ppm) < ppm * 0.02f && rig.Plug.Melted && rig.GlassChoke.Choke > 0.39f
                  && Math.Abs(rig.FoulingLoss - loss) < loss * 0.02f,
                $"a boiler resized as Rearranged resizes it keeps its water: {ppm:F0} -> {Ppm(rig):F0} ppm, plug {(rig.Plug.Melted ? "still melted" : "renewed")}, choke {rig.GlassChoke.Choke:P0}, scale loss {loss:P1} -> {rig.FoulingLoss:P1}");

            // Back to the stock size, as his rollback or reverting to a stock arrangement does.
            Resize(boiler, 1f / 1.2f);
            GaugeGlassChokeChecks.StepBoiler(boiler, rig, 0.1f);
            check(Math.Abs(TotalVolume(boiler) - volume) < 1f && rig.Plug.Melted && Math.Abs(Ppm(rig) - ppm) < ppm * 0.03f,
                "resized back, the water is still the same water");

            // A game reset (here the mod's own renewal, a new vessel at the spawn water in the same volume) still washes out.
            rig.Renew("adapter check");
            GaugeGlassChokeChecks.StepBoiler(boiler, rig, 0.1f);
            check(!rig.Plug.Melted && rig.GlassChoke.Choke == 0f && Ppm(rig) < ppm * 0.5f,
                $"a boiler the game makes anew in the same volume is still washed out ({Ppm(rig):F0} ppm, a new plug)");
        }

        private static SimulationFlow FlowOf(params SimComponent[] components)
        {
            var flow = (SimulationFlow)FormatterServices.GetUninitializedObject(typeof(SimulationFlow));
            AccessTools.Field(typeof(SimulationFlow), "OrderedSimComps").SetValue(flow, (IReadOnlyList<SimComponent>)components);
            return flow;
        }

        private static void CheckCarSave(SimGameParams gameParams, Action<bool, string> check)
        {
            var (boiler, rig, _, _, _, _, _) = GaugeGlassChokeChecks.BuildS282(gameParams);
            rig.Foul(2500f);
            rig.MeltPlug();
            rig.GlassChoke.Restore(0.3f, float.NaN, 12f);
            var ppm = Ppm(rig);

            // The car's save: the stock boiler's own object untouched, ours beside it.
            var stock = boiler.GetSaveStateData();
            var car = new JObject { ["sim"] = new JObject { ["boiler"] = stock } };
            BoilerWaterRig.SaveCar(FlowOf(boiler), car);
            var entry = car[BoilerWaterRig.CarSaveKey]?["boiler"] as JObject;
            check(entry != null && entry["rfSolids"] != null && entry["rfPlugMelted"] != null && entry["rfGlassChoke"] != null
                  && !stock.Properties().Any(p => p.Name.StartsWith("rf", StringComparison.Ordinal)),
                $"the boiler water saves in the car's own save under '{BoilerWaterRig.CarSaveKey}', the stock boiler's save object left with only the game's fields ({string.Join(", ", stock.Properties().Select(p => p.Name))})");

            // Loaded as the game loads a car: the boilers first, then the car's entry.
            var (reloaded, reloadedRig, _, _, _, _, _) = GaugeGlassChokeChecks.BuildS282(gameParams);
            reloaded.SetSaveStateData(stock);
            BoilerWaterRig.OnLoaded(reloaded, stock);
            BoilerWaterRig.LoadCar(FlowOf(reloaded), car);
            check(Math.Abs(Ppm(reloadedRig) - ppm) < ppm * 0.02f && reloadedRig.Plug.Melted && Math.Abs(reloadedRig.GlassChoke.Choke - 0.3f) < 1e-4f,
                "the car's entry restores the water, the plug, and the glass after the boiler's own load");

            // A save by 1.26.0 or earlier: the same keys inside the boiler's object, no car entry.
            var legacy = boiler.GetSaveStateData();
            rig.Save(legacy);
            var (old, oldRig, _, _, _, _, _) = GaugeGlassChokeChecks.BuildS282(gameParams);
            old.SetSaveStateData(legacy);
            BoilerWaterRig.OnLoaded(old, legacy);
            BoilerWaterRig.LoadCar(FlowOf(old), new JObject { ["sim"] = new JObject { ["boiler"] = legacy } });
            check(Math.Abs(Ppm(oldRig) - ppm) < ppm * 0.02f && oldRig.Plug.Melted && Math.Abs(oldRig.GlassChoke.Choke - 0.3f) < 1e-4f,
                "an older save with the water in the boiler's own object still loads it");

            // A save from before the mod: fresh water.
            var (fresh, freshRig, _, _, _, _, _) = GaugeGlassChokeChecks.BuildS282(gameParams);
            var bare = boiler.GetSaveStateData();
            fresh.SetSaveStateData(bare);
            BoilerWaterRig.OnLoaded(fresh, bare);
            BoilerWaterRig.LoadCar(FlowOf(fresh), new JObject());
            check(!freshRig.Plug.Melted && freshRig.GlassChoke.Choke == 0f && Ppm(freshRig) < ppm * 0.5f,
                "a save with neither starts the water fresh");
        }

        private static void CheckCylinders(Action<bool, string> check)
        {
            var engine = new ReciprocatingSteamEngine(new ReciprocatingSteamEngineDefinition
                { ID = "steamEngine", cylinderBore = 0.6f, pistonStroke = 0.7f, throttleMaxFlow = 5f, steamChestVolume = 800f });
            var cylinders = EngineCylinders.Of(engine);
            var first = cylinders.Model;
            first.WallCondensation = false;
            check(ReferenceEquals(first, cylinders.Model), "the cylinders' model stands while the engine is unchanged");

            // Rearranged's CylinderCalibration.SetSizing: the bore and, for equalised engines, the throttle's flow.
            engine.cylinderBore = 0.66f;
            AccessTools.Field(typeof(ReciprocatingSteamEngine), "throttleMaxFlow").SetValue(engine, 4f);
            var second = cylinders.Model;
            check(!ReferenceEquals(first, second) && second.PortFlowPerBar > first.PortFlowPerBar * 1.2f && !second.WallCondensation
                  && ReferenceEquals(cylinders, EngineCylinders.Of(engine)),
                $"a bore Rearranged changes rebuilds the cylinders (port {first.PortFlowPerBar:F2} -> {second.PortFlowPerBar:F2} kg/s per bar), keeping their setting");
        }

        private static void CheckSteamPath(SimGameParams gameParams, Action<bool, string> check)
        {
            var engine = new ReciprocatingSteamEngine(new ReciprocatingSteamEngineDefinition
                { ID = "steamEngine", cylinderBore = 0.6f, pistonStroke = 0.7f, throttleMaxFlow = 5f, steamChestVolume = 800f });
            engine.SetGameParams(gameParams);
            engine.intakeTemperature.SetPortReference(new Port("temperature", new PortDefinition(PortType.READONLY_OUT, PortValueType.TEMPERATURE, "OUT"), 300));
            engine.intakePressure.SetPortReference(new Port("pressure", new PortDefinition(PortType.READONLY_OUT, PortValueType.PRESSURE, "OUT"), 14));
            engine.throttleControl.SetPortReference(new Port("throttle", new PortDefinition(PortType.READONLY_OUT, PortValueType.CONTROL, "OUT"), 0));
            var path = EngineSteamPath.Attach(engine, new SteamPathSpec(RegulatorLocation.BeforeSuperheater, 300, 800, 5));
            AccessTools.Field(typeof(ReciprocatingSteamEngine), "steamChestSteamMass").SetValue(engine, 4f);
            EngineSteamPath.BeginTick(engine, 0.02f);
            EngineSteamPath.BeginTick(engine, 0.02f);
            check(EngineSteamPath.TryGet(engine, out var same) && ReferenceEquals(same, path), "the throttle path stands while the engine is unchanged");

            AccessTools.Field(typeof(ReciprocatingSteamEngine), "steamChestVolume").SetValue(engine, 700f);
            AccessTools.Field(typeof(ReciprocatingSteamEngine), "throttleMaxFlow").SetValue(engine, 4.5f);
            EngineSteamPath.BeginTick(engine, 0.02f);
            check(EngineSteamPath.TryGet(engine, out var refit) && !ReferenceEquals(refit, path)
                  && refit.Model.Spec.ChestVolume == 700 && refit.Model.Spec.MaxFlow == 4.5f && refit.Model.Spec.PipeVolume == 300
                  && refit.Model.Spec.Regulator == RegulatorLocation.BeforeSuperheater && refit.Active,
                $"a chest and throttle Rearranged resizes refit the throttle path to them ({refit?.Model.Spec.ChestVolume:F0} L chest, {refit?.Model.Spec.MaxFlow:F2} kg/s), still working");
        }
    }
}
