using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LocoSim.Definitions;
using LocoSim.Implementations;
using Newtonsoft.Json.Linq;
using RealisticFiring.Core;
using RealisticFiring.Sim;

namespace RealisticFiring.AdapterCheck
{
    /// <summary>
    /// End-to-end checks for the choking gauge glass &amp; try cocks loop on the
    /// stock Boiler + BoilerWaterRig + TryCockRig + ClientMirror + FireOverlay + Api.
    /// </summary>
    internal static class GaugeGlassChokeChecks
    {
        internal static void Run(SimGameParams gameParams, Action<bool, string> check)
        {
            var wasImpurities = Main.Settings.BoilerWaterImpurities;
            var wasQuality = Main.Settings.BoilerWaterQuality;
            try
            {
                Main.Settings.BoilerWaterImpurities = true;
                Main.Settings.BoilerWaterQuality = FeedWaterQuality.Bad;

                var (boiler, rig, cocks, ports, blowdown, steamDraw, heat) = BuildS282(gameParams);

                // Foul the boiler water and steam it under load so sediment and foam choke the lower glass passage
                // on the realistic shift/hour time scale (~35–40 min with active foaming).
                rig.Foul(4500f);
                heat.Value = 2e6f;
                steamDraw.Value = 4f;
                StepBoiler(boiler, rig, 2400f);
                var foamWhileWorking = rig.FoamNow;
                steamDraw.Value = 0f;
                heat.Value = 0f;
                check(rig.GlassChoke.IsStuck && rig.GlassChoke.Choke >= GaugeGlassChoke.StuckThreshold && foamWhileWorking > 0.1f,
                    $"dirty foaming boiler water chokes the S282 gauge glass (choke {rig.GlassChoke.Choke:P0}, foam {foamWhileWorking:P0})");

                // Blow down the boiler so true level drops below the middle try cock (0.761),
                // while the heavily choked glass remains falsely high.
                var glassBefore = rig.GlassLevel;
                blowdown.Value = 1f;
                for (var i = 0; i < 600 && rig.TrueSurfaceLevel > 0.70f; i++)
                    StepBoiler(boiler, rig, 0.1f);
                blowdown.Value = 0f;
                var glassWhileLow = rig.GlassLevel;
                var trueSurfaceLow = rig.TrueSurfaceLevel;

                // Open the top (0.900), middle (0.761), and bottom (0.621) try cocks:
                // middle try cock MUST blow dry steam at trueSurfaceLow (~0.70) even though glass reads > 0.78!
                ports[0].ExternalValueUpdate(1f);
                ports[1].ExternalValueUpdate(1f);
                ports[2].ExternalValueUpdate(1f);
                var topFlow = cocks.Flow(0);
                var midFlow = cocks.Flow(1);
                var botFlow = cocks.Flow(2);
                ports[0].ExternalValueUpdate(0f);
                ports[1].ExternalValueUpdate(0f);
                ports[2].ExternalValueUpdate(0f);
                check(trueSurfaceLow < 0.72f && glassWhileLow > trueSurfaceLow + 0.06f
                      && topFlow.Steam > 0f && topFlow.Water == 0f
                      && midFlow.Steam > 0f && midFlow.Water == 0f
                      && botFlow.Water > 0f,
                    $"stuck glass reads falsely high ({glassWhileLow:F3} vs true {trueSurfaceLow:F3}, started {glassBefore:F3}) while try cocks read true level (middle blows dry steam, bottom blows water)");

                // Save and restore preserves glass choke and stuck column; an older save without the key loads clear.
                var saved = boiler.GetSaveStateData();
                rig.Save(saved);
                var (reloaded, reloadedRig, _, _, _, _, _) = BuildS282(gameParams);
                reloaded.SetSaveStateData(saved);
                BoilerWaterRig.OnLoaded(reloaded, saved);
                check(Math.Abs(reloadedRig.GlassChoke.Choke - rig.GlassChoke.Choke) < 1e-4f
                      && reloadedRig.GlassChoke.HasLevel
                      && Math.Abs(reloadedRig.GlassChoke.ColumnLevel - rig.GlassChoke.ColumnLevel) < 1e-4f,
                    $"glass choke ({reloadedRig.GlassChoke.Choke:P0}) and stuck column ({reloadedRig.GlassChoke.ColumnLevel:F3}) round-trip in boiler save");

                var legacy = new JObject(saved);
                legacy.Remove("rfGlassChoke");
                legacy.Remove("rfGlassColumn");
                legacy.Remove("rfGlassBlowAge");
                var (legacyBoiler, legacyRig, _, _, _, _, _) = BuildS282(gameParams);
                legacyBoiler.SetSaveStateData(legacy);
                BoilerWaterRig.OnLoaded(legacyBoiler, legacy);
                check(legacyRig.GlassChoke.Choke == 0f && !legacyRig.GlassChoke.HasLevel,
                    "an older save without rfGlassChoke loads with a clear gauge glass");

                // Multiplayer mirror carries impurities-active state, true surface, foam, and choke state to a guest
                // so guest try cocks, advice, and Api always agree with the host — even if the guest's local
                // BoilerWaterImpurities setting disagrees with the host!
                var (guestBoiler, guestRig, guestCocks, guestPorts, _, _, _) = BuildS282(gameParams);
                var packet = ClientMirror.Capture(null, null, rig, null, null);
                Main.Settings.BoilerWaterImpurities = false;
                ClientMirror.Apply(packet, null, null, guestRig, null, null, "glass guest");
                guestPorts[1].ExternalValueUpdate(1f);
                var guestMid = guestCocks.Flow(1);
                guestPorts[1].ExternalValueUpdate(0f);
                var fire = new RealisticFirebox(new FireboxDefinition { ID = "firebox" }, null);
                fire.SetGameParams(gameParams);
                fire.SetSaveStateData(new JObject { ["fireOn"] = true, ["coalMass"] = 40f });
                var guestAdvice = FireOverlay.CoalAdvice(fire, null, guestRig);
                var guestApi = Api.ReadParts(fire, null, null, guestRig, null)["boiler"] as Dictionary<string, object>;
                check(guestRig.ImpuritiesActive && guestRig.HasTrueSurface && guestBoiler.pressureReadOut.Value > 5f
                      && Math.Abs(guestRig.TrueSurfaceLevel - rig.TrueSurfaceLevel) < 1e-4f
                      && Math.Abs(guestRig.GlassChoke.Choke - rig.GlassChoke.Choke) < 1e-4f
                      && guestMid.Steam > 0f && guestMid.Water == 0f
                      && guestAdvice.Level == AdviceLevel.Act && guestAdvice.Text.Contains("try cocks")
                      && guestApi != null && guestApi.ContainsKey("glassChokePct"),
                    "ClientMirror syncs host impurities-active, true surface, and glass choke even when guest BoilerWaterImpurities is off");

                // Reverse mismatch: host has BoilerWaterImpurities off, guest has it on -> guest mirrors off (0 foam/choke, no stuck advice).
                var (hostOffBoiler, hostOffRig, _, _, _, _, _) = BuildS282(gameParams);
                StepBoiler(hostOffBoiler, hostOffRig, 1f);
                var offPacket = ClientMirror.Capture(null, null, hostOffRig, null, null);
                Main.Settings.BoilerWaterImpurities = true;
                ClientMirror.Apply(offPacket, null, null, guestRig, null, null, "glass guest host-off");
                var guestOffAdvice = FireOverlay.CoalAdvice(fire, null, guestRig);
                var guestOffApi = Api.ReadParts(fire, null, null, guestRig, null)["boiler"] as Dictionary<string, object>;
                check(!guestRig.ImpuritiesActive && guestRig.FoamNow == 0f && guestRig.GlassChoke.Choke == 0f
                      && !guestOffAdvice.Text.Contains("try cocks")
                      && guestOffApi != null && !guestOffApi.ContainsKey("glassChokePct"),
                    "when host BoilerWaterImpurities is off, a guest with impurities on mirrors impurities off (zero foam/choke, no stuck warning)");

                // FireOverlay advice & Api reflect the stuck glass cleanly on host.
                var advice = FireOverlay.CoalAdvice(fire, null, rig);
                var api = Api.ReadParts(fire, null, null, rig, null);
                var apiBoiler = api["boiler"] as Dictionary<string, object>;
                check(advice.Level == AdviceLevel.Act && advice.Text.Contains("try cocks")
                      && apiBoiler != null && apiBoiler.ContainsKey("glassChokePct")
                      && Math.Abs((float)apiBoiler["glassChokePct"] - rig.GlassChoke.Choke * 100f) < 1e-2f,
                    "FireOverlay advice warns of a stuck glass and Api reports glassChokePct");

                // Blowing through with the glass bottom cock clears choking and draws down visually in ShownLevel,
                // WITHOUT leaking the brief blow-through dip into boiler.waterLevelReadOut (so external mods like
                // DVAIEngineer never misread a blow-through as "no water in sight").
                ports[TryCockRig.GlassBottom].ExternalValueUpdate(1f);
                StepBoiler(boiler, rig, 2.5f);
                var portDuringBlow = boiler.waterLevelReadOut.Value;
                var shownDuringBlow = rig.GlassChoke.ShownLevel;
                var chokeAfterBlow = rig.GlassChoke.Choke;
                ports[TryCockRig.GlassBottom].ExternalValueUpdate(0f);
                StepBoiler(boiler, rig, 3.0f);
                check(chokeAfterBlow == 0f && rig.GlassChoke.Choke < 0.02f
                      && shownDuringBlow < rig.TrueSurfaceLevel - 0.04f
                      && portDuringBlow >= rig.TrueSurfaceLevel - 0.01f
                      && Math.Abs(rig.GlassLevel - rig.TrueSurfaceLevel) < 0.008f
                      && Math.Abs(rig.GlassChoke.ShownLevel - rig.TrueSurfaceLevel) < 0.008f,
                    $"blowing through clears choke ({chokeAfterBlow:P0}), draws down visually ({shownDuringBlow:F3}) without leaking dip to waterLevelReadOut ({portDuringBlow:F3} vs true {rig.TrueSurfaceLevel:F3}), and rebounds cleanly");

                // Washout clears any choking immediately.
                reloadedRig.Washout();
                check(reloadedRig.GlassChoke.Choke == 0f && !reloadedRig.GlassChoke.HasLevel,
                    "boiler washout clears gauge glass choking");

                // Relaxed / BoilerWaterImpurities off never chokes.
                Main.Settings.BoilerWaterImpurities = false;
                var (relaxedBoiler, relaxedRig, _, _, _, relaxedSteam, relaxedHeat) = BuildS282(gameParams);
                relaxedRig.Foul(4500f);
                relaxedHeat.Value = 2e6f;
                relaxedSteam.Value = 4f;
                StepBoiler(relaxedBoiler, relaxedRig, 600f);
                check(relaxedRig.GlassChoke.Choke == 0f && Math.Abs(relaxedRig.GlassLevel - relaxedRig.TrueSurfaceLevel) < 1e-4f,
                    "with BoilerWaterImpurities off (Relaxed), gauge glass choking is disabled");
            }
            finally
            {
                Main.Settings.BoilerWaterImpurities = wasImpurities;
                Main.Settings.BoilerWaterQuality = wasQuality;
            }
        }

        private static (Boiler Boiler, BoilerWaterRig Water, TryCockRig Cocks, Port[] Ports, Port Blowdown, Port SteamDraw, Port Heat) BuildS282(SimGameParams gameParams)
        {
            var boilerDef = new BoilerDefinition
            {
                ID = "boiler", diameter = 1.7f, length = 8.8f, capacityMultiplier = 0.85f, maxInjectorRate = 20f,
                waterConsumptionMultiplier = 4f, safetyValveOpeningPressure = 15f, safetyValveClosingPressure = 14.8f,
                spawnPressure = 14f, spawnWaterLevel = 12800f, crownSheetNormalizedWaterLevel = 0.6f,
                steamOutletNormalizedWaterLevel = 0.98f, maxBlowdownRate = 100f,
            };
            var boiler = (Boiler)boilerDef.InstantiateImplementation();
            boiler.SetGameParams(gameParams);
            var engine = (ReciprocatingSteamEngine)new ReciprocatingSteamEngineDefinition
            {
                ID = "engine", throttleMaxFlow = 6f,
            }.InstantiateImplementation();
            engine.maxFlowReadOut.Value = 6f;

            Port Connect(string id, float value = 0f)
            {
                var port = new Port(id, new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"), value);
                boiler.GetAllPortReferences().Single(r => r.id == "boiler." + id).SetPortReference(port);
                return port;
            }
            Connect("INJECTOR");
            var blowdown = Connect("BLOWDOWN");
            Connect("WATER", 50000f);
            Connect("WATER_CONSUMPTION");
            var heat = Connect("HEAT");
            Connect("FIREBOX_TEMPERATURE", 200f);
            var steamDraw = Connect("STEAM_CONSUMPTION");

            var waterRig = BoilerWaterRig.Create(boiler, engine, BoilerWaterRig.Guard.FusiblePlug);
            var ports = TryCockRig.Valves.Select(v =>
                new Port(v.PortId, new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"), 0f)).ToArray();
            var tryCockRig = TryCockRig.Create(waterRig, boiler, ports, "LocoS282A");
            StepBoiler(boiler, waterRig, 0.1f);
            return (boiler, waterRig, tryCockRig, ports, blowdown, steamDraw, heat);
        }

        private static void StepBoiler(Boiler boiler, BoilerWaterRig rig, float seconds)
        {
            const float dt = 0.1f;
            var simulateBlowdown = AccessTools.Method(typeof(Boiler), "SimulateBlowdown");
            var simulateEvaporation = AccessTools.Method(typeof(Boiler), "SimulateEvaporation");
            var simulateWaterLevel = AccessTools.Method(typeof(Boiler), "SimulateWaterLevel");
            for (var t = 0f; t < seconds; t += dt)
            {
                rig.BeginTick(dt);
                var massBefore = rig.MassNow;
                simulateBlowdown.Invoke(boiler, new object[] { dt });
                rig.AfterBlowdown(massBefore);
                simulateEvaporation.Invoke(boiler, new object[] { dt });
                simulateWaterLevel.Invoke(boiler, new object[] { dt });
                rig.AfterWaterLevel(dt);
                rig.SimulateCrownSheet(dt);
            }
        }
    }
}
