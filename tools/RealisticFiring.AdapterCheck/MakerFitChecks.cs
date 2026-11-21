using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using DV.ThingTypes;
using HarmonyLib;
using LocoSim.Implementations;
using RealisticFiring.Core;
using RealisticFiring.Shed;
using RealisticFiring.Sim;
using RealisticFiring.Visuals;
using UnityEngine;

namespace RealisticFiring.AdapterCheck
{
    /// <summary>
    /// Verifies MakerFitRegistry discovery, precedence, subsystem wiring, and
    /// SeatingBench JSON export round-tripping against the live adapter types.
    /// </summary>
    internal static class MakerFitChecks
    {
        public static void Run(Action<bool, string> check)
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "rf-maker-fit-" + Guid.NewGuid().ToString("N"));
            var modsRoot = Path.Combine(tempRoot, "Mods");
            var modA = Path.Combine(modsRoot, "AlphaSteam");
            var modASub = Path.Combine(modA, "realistic-firing");
            var modB = Path.Combine(modsRoot, "BrokenMod");
            var modOversized = Path.Combine(modsRoot, "OversizedMod");
            var rfDir = Path.Combine(modsRoot, "RealisticFiring");
            var fitsDir = Path.Combine(rfDir, "Fits");

            try
            {
                Directory.CreateDirectory(modASub);
                Directory.CreateDirectory(modB);
                Directory.CreateDirectory(modOversized);
                Directory.CreateDirectory(fitsDir);

                MakerFitRegistry.Reset();
                var fpBefore = MakerFitRegistry.SessionFingerprint();
                check(fpBefore == "none",
                    "MakerFitRegistry.SessionFingerprint() reports 'none' when no maker fits are loaded");

                // 1) Mod-shipped single file for custom coal locomotive + attempts to override stock/excluded.
                File.WriteAllText(Path.Combine(modA, "realistic-firing.json"), @"{
  ""schemaVersion"": 1,
  ""liveries"": [""CustomTest462"", ""LocoS282A"", ""S040F"", ""S484-841A""],
  ""fuel"": ""coal"",
  ""summary"": ""Custom 4-6-2 test profile"",
  ""firebox"": {
    ""grateAreaM2"": 5.4,
    ""grateSlopeDeg"": -6.5,
    ""slopeOverPile"": true,
    ""backRowDraft"": 1.15,
    ""frontRowDraft"": 0.85,
    ""sideDraft"": 1.04,
    ""overFireMixing"": 1.1,
    ""ashFraction"": 1.3,
    ""clinkerFactor"": 0.8,
    ""reversedDoor"": true,
    ""overFireJets"": { ""count"": 6, ""tube"": ""large"" }
  },
  ""boiler"": {
    ""maxEvaporationLbPerHr"": 45000,
    ""safetyValves"": { ""liftPsiGauge"": 200, ""capacityKgPerS"": 6.0 },
    ""lowWaterGuard"": ""lowWaterAlarm""
  },
  ""draft"": {
    ""blowerMaxSteamKgPerS"": 0.55,
    ""naturalDraftKgPerS"": 0.58,
    ""airPumps"": { ""type"": ""crossCompound"", ""count"": 2, ""mainReservoirL"": 1350 }
  },
  ""cylinders"": {
    ""stockSteamPerStroke"": true,
    ""superheated"": true,
    ""superheaterKind"": ""Type E"",
    ""maxSuperheatC"": 165,
    ""halfSuperheatShare"": 0.78,
    ""frontEndThrottle"": true,
    ""steamCircuitVolumeL"": 550,
    ""driftingFittings"": { ""snifting"": true, ""driftingValve"": true, ""byPass"": true }
  },
  ""injectors"": {
    ""type"": ""lifting"",
    ""maxFlowKgPerS"": 8.2,
    ""steamValves"": true,
    ""ownLeverIsSteam"": true,
    ""waterRegulators"": { ""mesh"": ""CAB.TEST"", ""rightTurnDeg"": 300, ""leftTurnDeg"": 140 },
    ""twoValveGraph"": {
      ""add"": ""injAdd"", ""addA"": ""inj1"", ""addB"": ""inj2"",
      ""multiplier"": ""injMul"", ""multiplierOut"": ""injMul.out"",
      ""factor"": ""injFac"", ""factorPort"": ""injFac.val"",
      ""right"": ""inj2"", ""left"": ""inj1""
    },
    ""tenderLiveries"": [""OldModTenderA""]
  },
  ""stoker"": { ""fitted"": true, ""retrofit"": true, ""backLanes"": 0.75, ""lumpsLeft"": 0.05 },
  ""feedPump"": { ""fitted"": true, ""type"": ""closed"", ""name"": ""Elesco K40"", ""capacityKgPerS"": 8.4, ""onInjectorLever"": false },
  ""booster"": { ""fitted"": true, ""wheelDiameterM"": 1.2, ""trailerLoadKg"": 22000, ""drivingAxles"": 3 },
  ""autoFireDoor"": true,
  ""cab"": {
    ""firehole"": { ""middleM"": 0.02, ""footM"": 1.80, ""widthM"": 0.50, ""heightM"": 0.40, ""planeM"": -0.25 },
    ""injectorSteam"": { ""position"": [0.15, -0.10, 0.05], ""rotation"": [-180, 0, 2], ""sideOffset"": [0.06, -0.02, 0.14] },
    ""dropGrate"": { ""position"": [0.42, 0.0, -0.25] },
    ""ashpan"": { ""position"": [-0.92, 1.05, 0.35], ""noHanger"": true },
    ""stokerManifold"": { ""offsetCm"": [15.0, -5.0, -3.0], ""turnDeg"": 4, ""tiltDeg"": -1 },
    ""stokerLever"": { ""position"": [0.22, 0.0, -0.20] },
    ""feedPump"": { ""valveInCar"": [-0.65, 3.05, -2.10], ""gaugeOffset"": [0.0, 0.14], ""wallGauge"": [0.1, 0.3, 0.05] },
    ""booster"": { ""position"": [1.40, 1.55, -0.35] },
    ""jets"": { ""position"": [-0.15, 2.40, 0.08], ""rotation"": [0, 5, 0], ""plateOffset"": [0.08, 0.15, -0.03], ""plateTurnDeg"": 180, ""plateSquare"": true, ""ownHandle"": true },
    ""driftingValve"": { ""position"": [0.65, 2.80, 0.12], ""rotation"": [0, -30, 0] },
    ""shedBlower"": { ""position"": [-0.65, 2.10, 9.50], ""downDeg"": 40, ""landmark"": ""low on the left smokebox side"" },
    ""shedWater"": { ""enabled"": false }
  },
  ""stack"": { ""lipHeightM"": 0.44, ""boreRadiusM"": 0.22, ""outsideRadiusM"": 0.30 }
}");

                // 2) Subfolder file for a custom oil locomotive.
                File.WriteAllText(Path.Combine(modASub, "oil-burner.json"), @"{
  ""schemaVersion"": 1,
  ""liveries"": [""CustomOil484""],
  ""fuel"": ""oil"",
  ""cylinders"": { ""superheated"": false }
}");

                // 2b) Subfolder file attempting to enable fake second liveries / reskins of excluded car types.
                File.WriteAllText(Path.Combine(modASub, "excluded-reskins.json"), @"{
  ""schemaVersion"": 1,
  ""liveries"": [""GS4_Reskin"", ""C16_Reskin""],
  ""fuel"": ""coal""
}");

                // 3) Invalid file in another mod must be skipped cleanly with warnings.
                File.WriteAllText(Path.Combine(modB, "realistic-firing.json"), @"{ ""schemaVersion"": 99, ""liveries"": [""BadLoco""] }");

                // 3b) Oversized file (> 512 KB) must be skipped before reading with a clear warning.
                var padding = new string(' ', (int)MakerFitRegistry.MaxFileBytes + 1024);
                File.WriteAllText(Path.Combine(modOversized, "realistic-firing.json"),
                    "{ \"schemaVersion\": 1, \"liveries\": [\"OversizedLoco\"]" + padding + "}");

                // 4) User Fits override for CustomTest462 updates summary, drop grate position, and tender livery,
                //    while still unable to override LocoS282A.
                File.WriteAllText(Path.Combine(fitsDir, "override.json"), @"{
  ""schemaVersion"": 1,
  ""liveries"": [""CustomTest462"", ""LocoS282A""],
  ""summary"": ""User-tuned 4-6-2"",
  ""firebox"": {
    ""grateAreaM2"": 5.4,
    ""grateSlopeDeg"": -6.5,
    ""slopeOverPile"": true,
    ""backRowDraft"": 1.15,
    ""frontRowDraft"": 0.85,
    ""sideDraft"": 1.04,
    ""overFireMixing"": 1.1,
    ""ashFraction"": 1.3,
    ""clinkerFactor"": 0.8,
    ""reversedDoor"": true,
    ""overFireJets"": { ""count"": 6, ""tube"": ""large"" }
  },
  ""boiler"": {
    ""maxEvaporationLbPerHr"": 45000,
    ""safetyValves"": { ""liftPsiGauge"": 200, ""capacityKgPerS"": 6.0 },
    ""lowWaterGuard"": ""lowWaterAlarm""
  },
  ""draft"": {
    ""blowerMaxSteamKgPerS"": 0.55,
    ""naturalDraftKgPerS"": 0.58,
    ""airPumps"": { ""type"": ""crossCompound"", ""count"": 2, ""mainReservoirL"": 1350 }
  },
  ""cylinders"": {
    ""stockSteamPerStroke"": true,
    ""superheated"": true,
    ""superheaterKind"": ""Type E"",
    ""maxSuperheatC"": 165,
    ""halfSuperheatShare"": 0.78,
    ""frontEndThrottle"": true,
    ""steamCircuitVolumeL"": 550,
    ""driftingFittings"": { ""snifting"": true, ""driftingValve"": true, ""byPass"": true }
  },
  ""injectors"": {
    ""type"": ""lifting"",
    ""maxFlowKgPerS"": 8.2,
    ""steamValves"": true,
    ""ownLeverIsSteam"": true,
    ""waterRegulators"": { ""mesh"": ""CAB.TEST"", ""rightTurnDeg"": 300, ""leftTurnDeg"": 140 },
    ""twoValveGraph"": {
      ""add"": ""injAdd"", ""addA"": ""inj1"", ""addB"": ""inj2"",
      ""multiplier"": ""injMul"", ""multiplierOut"": ""injMul.out"",
      ""factor"": ""injFac"", ""factorPort"": ""injFac.val"",
      ""right"": ""inj2"", ""left"": ""inj1""
    },
    ""tenderLiveries"": [""CustomTest462Tender"", ""em_lms_stanier_tender_1_black""]
  },
  ""stoker"": { ""fitted"": true, ""retrofit"": true, ""backLanes"": 0.75, ""lumpsLeft"": 0.05 },
  ""feedPump"": { ""fitted"": true, ""type"": ""closed"", ""name"": ""Elesco K40"", ""capacityKgPerS"": 8.4, ""onInjectorLever"": false },
  ""booster"": { ""fitted"": true, ""wheelDiameterM"": 1.2, ""trailerLoadKg"": 22000, ""drivingAxles"": 3 },
  ""autoFireDoor"": true,
  ""cab"": {
    ""firehole"": { ""middleM"": 0.02, ""footM"": 1.80, ""widthM"": 0.50, ""heightM"": 0.40, ""planeM"": -0.25 },
    ""injectorSteam"": { ""position"": [0.15, -0.10, 0.05], ""rotation"": [-180, 0, 2], ""sideOffset"": [0.06, -0.02, 0.14] },
    ""dropGrate"": { ""position"": [0.55, 0.0, -0.25] },
    ""ashpan"": { ""position"": [-0.92, 1.05, 0.35], ""noHanger"": true },
    ""stokerManifold"": { ""offsetCm"": [15.0, -5.0, -3.0], ""turnDeg"": 4, ""tiltDeg"": -1 },
    ""stokerLever"": { ""position"": [0.22, 0.0, -0.20] },
    ""feedPump"": { ""valveInCar"": [-0.65, 3.05, -2.10], ""gaugeOffset"": [0.0, 0.14], ""wallGauge"": [0.1, 0.3, 0.05] },
    ""booster"": { ""position"": [1.40, 1.55, -0.35] },
    ""jets"": { ""position"": [-0.15, 2.40, 0.08], ""rotation"": [0, 5, 0], ""plateOffset"": [0.08, 0.15, -0.03], ""plateTurnDeg"": 180, ""plateSquare"": true, ""ownHandle"": true },
    ""driftingValve"": { ""position"": [0.65, 2.80, 0.12], ""rotation"": [0, -30, 0] },
    ""shedBlower"": { ""position"": [-0.65, 2.10, 9.50], ""downDeg"": 40, ""landmark"": ""low on the left smokebox side"" },
    ""shedWater"": { ""enabled"": false }
  },
  ""stack"": { ""lipHeightM"": 0.44, ""boreRadiusM"": 0.22, ""outsideRadiusM"": 0.30 }
}");

                var logs = new List<string>();
                var warns = new List<string>();
                var loaded = MakerFitRegistry.LoadFromModDirectory(rfDir, logs.Add, warns.Add);

                check(loaded == 5, $"maker-fit discovery loads mod, subfolder, and user override fits ({loaded} registrations)");
                check(warns.Any(w => w.Contains("BrokenMod") && w.Contains("unsupported schemaVersion 99")),
                    "malformed or unsupported maker-fit files log actionable warnings without throwing");
                check(warns.Any(w => w.Contains("OversizedMod") && w.Contains("limit")) && !MakerFitRegistry.HasLivery("OversizedLoco"),
                    "oversized maker-fit files above the 512 KB cap are skipped with a warning");
                check(!MakerFitRegistry.HasLivery("BadLoco") && !MakerFitRegistry.HasLivery("S040F")
                      && !MakerFitRegistry.HasLivery("S484-841A") && !MakerFitRegistry.HasLivery("LocoS282A"),
                    "excluded types and built-in liveries are never registered or overridden by maker fits");
                check(LeftStock.Is("S040F", "S040F", unlistedToo: true) && LeftStock.Is("S484-841A", "S484-841A", unlistedToo: true),
                    "LeftStock excluded types stay excluded even after maker-fit discovery");

                // Fake second-livery reskins of excluded car types (S040F, S484-841A) must stay rejected
                // even when their livery ID appears in a coal maker-fit JSON.
                var gs4Reskin = FakeCar("GS4_Reskin", "S484-841A");
                var c16Reskin = FakeCar("C16_Reskin", "S040F");
                var customCoalCar = FakeCar("CustomTest462", "CustomTest462Type");
                check(LeftStock.IsExcluded(gs4Reskin) && LeftStock.IsExcluded(c16Reskin)
                      && LeftStock.Is(gs4Reskin) && LeftStock.Is(c16Reskin)
                      && !LeftStock.Listed(gs4Reskin) && !LeftStock.Listed(c16Reskin)
                      && !FireboxRegistry.IsSupported(gs4Reskin) && !FireboxRegistry.IsSupported(c16Reskin)
                      && !FireboxRegistry.IsSupported("GS4_Reskin", "S484-841A") && !FireboxRegistry.IsSupported("C16_Reskin", "S040F")
                      && !MakerFitRegistry.IsCoalCar(gs4Reskin) && !MakerFitRegistry.IsCoalCar(c16Reskin)
                      && !MakerFitRegistry.HasCar(gs4Reskin) && !MakerFitRegistry.HasCar(c16Reskin)
                      && !MakerFitRegistry.TryGetProfile(gs4Reskin, out _) && !MakerFitRegistry.TryGetProfile("C16_Reskin", "S040F", out _),
                    "second liveries / reskins of excluded car types (S040F, S484-841A) are rejected by FireboxRegistry and MakerFitRegistry");
                check(!LeftStock.IsExcluded(customCoalCar) && FireboxRegistry.IsSupported(customCoalCar)
                      && MakerFitRegistry.IsCoalCar(customCoalCar) && MakerFitRegistry.TryGetProfile(customCoalCar, out _),
                    "custom coal maker-fit car whose parentType is not excluded is supported");

                // Session fingerprint reflects loaded maker fits.
                var fpAfter = MakerFitRegistry.SessionFingerprint();
                check(fpAfter != fpBefore && fpAfter.StartsWith("4:"),
                    "MakerFitRegistry.SessionFingerprint() changes when maker fits are loaded");

                // Listing & fuel routing
                check(LeftStock.Listed("CustomTest462") && FireboxRegistry.SupportedLiveries.Contains("CustomTest462"),
                    "coal maker-fit livery is listed in LeftStock and FireboxRegistry.SupportedLiveries");
                check(LeftStock.Listed("CustomOil484") && !FireboxRegistry.SupportedLiveries.Contains("CustomOil484"),
                    "oil maker-fit livery is listed in LeftStock without joining coal FireboxRegistry");

                // Firebox character & user override precedence
                var fb = FireboxCharacters.ForLivery("CustomTest462");
                check(fb.Summary == "User-tuned 4-6-2" && fb.PrototypeGrateArea == 5.4f && fb.PrototypeSlope == -6.5f
                      && fb.PrototypeSlopeOverPile && fb.Jets != null && fb.JetsAsBuilt && fb.StokerBackLanes == 0.75f,
                    "FireboxCharacters wires user-overridden maker-fit firebox, jets, and stoker traits");
                check(FiringFrame.Reversed("CustomTest462"), "FiringFrame honors maker-fit reversedDoor");

                // Boiler, draft, safety valves, water guard
                check(BoilerFit.TryBurnTime("CustomTest462", 120f, 0.8f, false, out var burnTime) && burnTime > 0f,
                    "BoilerFit sizes burn time from maker-fit maxEvaporationLbPerHr");
                check(SafetyValveFit.TryGetFit("CustomTest462", out var sv) && Math.Abs(sv.Capacity - 6f) < 0.01f && sv.Lift > 14f,
                    "SafetyValveFit wires maker-fit safety valve lift and capacity");
                check(BlowerFit.TryGetDraft("CustomTest462", out var draft) && Math.Abs(draft.MaxBlower - 0.55f) < 0.001f && Math.Abs(draft.Natural - 0.58f) < 0.001f,
                    "BlowerFit wires maker-fit blower and natural draft");
                check(BoilerWaterRig.GuardFor("CustomTest462") == BoilerWaterRig.Guard.LowWaterAlarm,
                    "BoilerWaterRig wires maker-fit lowWaterGuard");

                // Cylinders, superheater, steam path, drifting
                check(EngineCylinders.UsesStockSteam("CustomTest462"), "EngineCylinders honors maker-fit stockSteamPerStroke");
                var sh = EngineSuperheater.SpecFor("CustomTest462");
                check(!sh.Saturated && sh.Kind == "Type E" && Math.Abs(sh.FullSuperheat - 165f) < 0.01f,
                    "EngineSuperheater wires maker-fit superheater spec");
                check(EngineSuperheater.SpecFor("CustomOil484").Saturated && EngineSteamPath.SaturatedPath("CustomOil484"),
                    "saturated maker-fit wires saturated superheater and native steam path");
                check(EngineSteamPath.TryFit("CustomTest462", 1000f, 6f, out var pathSpec)
                      && pathSpec.Regulator == RegulatorLocation.AfterSuperheater && Math.Abs(pathSpec.PipeVolume - 550) < 0.1,
                    "EngineSteamPath wires maker-fit frontEndThrottle and steamCircuitVolumeL");
                check(DriftingFittings.MayHave("CustomTest462") && DriftingFittings.CanHaveValve("CustomTest462"),
                    "DriftingFittings wires maker-fit drifting equipment");

                // Injectors, feed pump, booster, water regulators
                var excludedTenderCar = FakeCar("em_lms_stanier_tender_1_black", "S484-841A");
                var dummyFlow = (SimulationFlow)FormatterServices.GetUninitializedObject(typeof(SimulationFlow));
                check(InjectorValves.Fits("CustomTest462") && InjectorValves.Lifting("CustomTest462")
                      && InjectorValves.OwnLeverIsSteam("CustomTest462") && InjectorValves.Count("CustomTest462") == 2
                      && !InjectorValves.HasTenderValves("CustomTest462Tender")
                      && !InjectorValves.HasTenderValves("OldModTenderA")
                      && warns.Any(w => w.Contains("CustomTest462Tender") && w.Contains("handle fit"))
                      && MakerFitRegistry.HasTenderWaterValves("em_lms_stanier_tender_1_black")
                      && InjectorValves.HasTenderValves("em_lms_stanier_tender_1_black")
                      && TenderWaterValves.HasFit("em_lms_stanier_tender_1_black")
                      && TenderWaterValves.Attach(excludedTenderCar, dummyFlow) == null
                      && InjectorValves.Fits("CustomOil484"),
                    "InjectorValves, TenderWaterValves, and InjectorRig reject unfitted custom tenders, accept fitted tenders, guard excluded cars, and default steam valves");
                check(CabWaterRegulators.CutFromMesh("CustomTest462"), "CabWaterRegulators wires maker-fit mesh water regulators");
                check(FeedwaterFit.TryGet("CustomTest462", out var pumpFit) && pumpFit.Type == FeedHeaterType.Closed && Math.Abs(pumpFit.Capacity - 8.4f) < 0.01f,
                    "FeedwaterFit wires maker-fit feed pump");
                check(BoosterFit.TryGet("CustomTest462", out var boosterFit) && boosterFit.DrivingAxles == 3 && Math.Abs(boosterFit.WheelDiameter - 1.2f) < 0.001f,
                    "BoosterFit wires maker-fit booster");

                // Cab & shed placements
                check(FireholeOpening.TryHole("CustomTest462", out var hole) && Math.Abs(hole.Width - 0.50f) < 0.001f && Math.Abs(hole.Plane - (-0.25f)) < 0.001f,
                    "FireholeOpening wires maker-fit firehole rectangle and plane");
                var injSeat = InjectorSteamCab.SeatFor("CustomTest462");
                check(Math.Abs(injSeat.Move.x - 0.15f) < 0.001f && InjectorSteamCab.OnSide("CustomTest462"),
                    "InjectorSteamCab wires maker-fit seat and sideOffset");
                check(Math.Abs(DropGrateCab.SeatFor("CustomTest462").x - 0.55f) < 0.001f,
                    "DropGrateCab honors user-tier maker-fit override seat");
                check(AshpanLever.TrySeat("CustomTest462", out var ashSeat) && Math.Abs(ashSeat.x - (-0.92f)) < 0.001f,
                    "AshpanLever wires maker-fit seat");
                check(Math.Abs(StokerCab.PlacementFor("CustomTest462").OffsetCm.x - 15f) < 0.01f
                      && Math.Abs(StokerCab.LeverMoveFor("CustomTest462").x - 0.22f) < 0.001f,
                    "StokerCab wires maker-fit manifold and lever placements");
                check(FeedPumpCab.SpotFor("CustomTest462").ValveInCar.HasValue
                      && Math.Abs(FeedPumpCab.LeverGaugeSeatFor("CustomTest462").y - 0.3f) < 0.001f,
                    "FeedPumpCab wires maker-fit spot and wall gauge seat");
                check(BoosterCab.CarSeatFor("CustomTest462") is Vector3 bSeat && Math.Abs(bSeat.x - 1.40f) < 0.001f,
                    "BoosterCab wires maker-fit car seat");
                check(Math.Abs(JetsCab.SeatFor("CustomTest462").Move.x - (-0.15f)) < 0.001f
                      && Math.Abs(DriftingValveCab.Valve.SeatFor("CustomTest462").Move.x - 0.65f) < 0.001f,
                    "JetsCab and DriftingValveCab wire maker-fit CabValve seats");
                check(ShedConnections.HasBlowerConnection("CustomTest462") && !ShedConnections.HasWaterConnection("CustomTest462")
                      && ShedConnections.Describe("CustomTest462", false, Vector3.zero, 10f) == "low on the left smokebox side",
                    "ShedConnections wires maker-fit enable flags and landmark");
                check(MakerFitRegistry.TryGetStackGeometry("CustomTest462", out var stackGeom)
                      && Math.Abs(stackGeom.LipHeight.GetValueOrDefault() - 0.44f) < 0.001f
                      && Math.Abs(stackGeom.BoreRadius.GetValueOrDefault() - 0.22f) < 0.001f
                      && Math.Abs(stackGeom.OutsideRadius.GetValueOrDefault() - 0.30f) < 0.001f,
                    "MakerFitRegistry wires optional stack geometry hints");

                // Built-in S282 remains untouched
                check(FireboxCharacters.ForLivery("LocoS282A").Summary != "User-tuned 4-6-2",
                    "built-in LocoS282A firebox character stays authoritative");

#if !PUBLIC_RELEASE
                // SeatingBench JSON export round-trip check
                var exported = SeatingBench.BuildExportJson("ExportedTest280", oil: false, new Dictionary<string, float[]>
                {
                    ["FireholeOpening.Holes"] = new[] { 0.01f, 1.72f, 0.45f, 0.456f, 0.054f },
                    ["InjectorSteamCab.Seats"] = new[] { 0.11f, 0.03f, 0.14f, -180f, 0f, 2f },
                    ["DropGrateCab.Placements"] = new[] { 0f, 0f, -0.3f },
                    ["AshpanLever.Placements"] = new[] { -0.61f, 0.99f, 1.66f },
                    ["StokerCab.Placements"] = new[] { 19.2f, -8.1f, -11.5f, 8f, -1f },
                    ["StokerCab.LeverMoves"] = new[] { 0f, 0f, -0.3f },
                    ["FeedPumpCab.Spots"] = new[] { -0.46f, 2.9f, 0.06f, 0f, 0.12f },
                    ["BoosterCab.CarSeats"] = new[] { 1.45f, 1.60f, -0.24f },
                    ["JetsCab.Seats"] = new[] { -0.11f, 2.41f, 0.11f, 0f, 0f, 0f, 0.10f, 0.21f, -0.045f, 0f, 180f },
                    ["DriftingValveCab.Seats"] = new[] { 0.69f, 2.84f, 0.18f, 0f, -40f, 0f, 0f, 0f, 0f, 0f, 0f },
                    ["ShedConnections.Seated"] = new[] { -0.48f, 1.93f, 10.32f, 0f, 44f, 90f, 0.03f },
                    ["ShedConnections.WaterSeated"] = new[] { -0.22f, 1.15f, 2.28f, 0f, 0f, 0f, 0f },
                }, jetsPlateSquare: true, driftingPlateSquare: false);
                var reparsed = MakerFitParser.Parse(exported);
                check(reparsed.Ok && reparsed.Profile != null
                      && reparsed.Profile.Liveries.SequenceEqual(new[] { "ExportedTest280" })
                      && reparsed.Profile.Cab?.Firehole != null
                      && reparsed.Profile.Cab?.InjectorSteam != null
                      && reparsed.Profile.Cab?.DropGrate != null
                      && reparsed.Profile.Cab?.Ashpan != null
                      && reparsed.Profile.Cab?.StokerManifold != null
                      && reparsed.Profile.Cab?.FeedPump?.ValveInCar != null
                      && reparsed.Profile.Cab?.Booster?.Position != null
                      && reparsed.Profile.Cab?.Jets?.PlateSquare == true
                      && reparsed.Profile.Cab?.DriftingValve != null
                      && reparsed.Profile.Cab?.ShedBlower?.DownDeg == 44f
                      && reparsed.Profile.Cab?.ShedWater?.Position != null,
                    "SeatingBench JSON export produces valid schemaVersion 1 JSON containing all seated parts");
#endif
            }
            finally
            {
                MakerFitRegistry.Reset();
                if (Directory.Exists(tempRoot))
                {
                    try { Directory.Delete(tempRoot, recursive: true); } catch { }
                }
            }

            check(MakerFitRegistry.SessionFingerprint() == "none",
                "MakerFitRegistry.SessionFingerprint() returns to 'none' after MakerFitRegistry.Reset()");
        }

        /// <summary>
        /// Called inside the Multiplayer check block (where MultiplayerAPI is
        /// resolved) to confirm Integration.Shape() changes when a maker fit loads.
        /// </summary>
        public static void RunMultiplayerShape(Action<bool, string> check)
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "rf-maker-fit-mp-" + Guid.NewGuid().ToString("N"));
            var fitsDir = Path.Combine(tempRoot, "Mods", "RealisticFiring", "Fits");
            try
            {
                MakerFitRegistry.Reset();
                var shapeBefore = Multiplayer.Integration.Shape();
                check(shapeBefore.Contains("maker fits none"),
                    "Multiplayer Shape() includes 'maker fits none' when no maker fits are loaded");

                Directory.CreateDirectory(fitsDir);
                File.WriteAllText(Path.Combine(fitsDir, "mp-test.json"), @"{
  ""schemaVersion"": 1,
  ""liveries"": [""CustomMpTest280""],
  ""fuel"": ""coal"",
  ""firebox"": { ""grateAreaM2"": 4.5 }
}");
                MakerFitRegistry.LoadFromModDirectory(Path.Combine(tempRoot, "Mods", "RealisticFiring"));
                var fp = MakerFitRegistry.SessionFingerprint();
                var shapeAfter = Multiplayer.Integration.Shape();
                check(shapeAfter != shapeBefore && fp.StartsWith("1:") && shapeAfter.Contains($"maker fits {fp}"),
                    "Multiplayer Shape() changes when a maker fit is loaded and embeds its deterministic digest");
            }
            finally
            {
                MakerFitRegistry.Reset();
                if (Directory.Exists(tempRoot))
                {
                    try { Directory.Delete(tempRoot, recursive: true); } catch { }
                }
            }

            check(Multiplayer.Integration.Shape().Contains("maker fits none"),
                "Multiplayer Shape() returns to 'maker fits none' after resetting maker fits");
        }

        private static TrainCar FakeCar(string liveryId, string carTypeId)
        {
            var cachedPtr = AccessTools.Field(typeof(UnityEngine.Object), "m_CachedPtr");
            var carType = (TrainCarType_v2)FormatterServices.GetUninitializedObject(typeof(TrainCarType_v2));
            cachedPtr.SetValue(carType, new IntPtr(1));
            carType.id = carTypeId;

            var livery = (TrainCarLivery)FormatterServices.GetUninitializedObject(typeof(TrainCarLivery));
            cachedPtr.SetValue(livery, new IntPtr(1));
            livery.id = liveryId;
            livery.parentType = carType;

            var car = (TrainCar)FormatterServices.GetUninitializedObject(typeof(TrainCar));
            cachedPtr.SetValue(car, new IntPtr(1));
            car.carLivery = livery;
            return car;
        }
    }
}
