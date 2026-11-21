using System;
using System.Collections.Generic;
using System.Linq;
using RealisticFiring.Core;
using RealisticFiring.Shed;
using RealisticFiring.Sim;
using RealisticFiring.Visuals;
using UnityEngine;

namespace RealisticFiring.AdapterCheck
{
    /// <summary>
    /// Verifies the unified <see cref="LocomotiveProfiles"/> registry:
    /// complete coverage of supported locomotives and tenders, shared profile
    /// instances for multi-livery aliases, exclusion of stock-only types, and
    /// consistency across every migrated simulation, shed, and cab consumer.
    /// </summary>
    internal static class ProfileRegistryChecks
    {
        private static readonly string[] ExpectedCoal =
        {
            "LocoS282A", "LocoS060", "Jk3A", "S464-730", "S484-840A", "FEF-3",
            "em_lms_8f_loc_black", "em_lms_8f_loc_grey",
            "em_fowler_4f_44422", "em_fowler_4f_4027",
            "S2102-870", "S2102-720",
            "S260N-580A", "S040N-270", "S0440NG-890", "S440NCF-400A",
        };

        private static readonly string[] ExpectedNonCoal =
        {
            "A.T.&S.F. 2901-A", "GWI40xx", "GWI_Manor",
        };

        private static readonly string[] ExcludedTypes = { "S040F", "S484-841A" };

        internal static void Run(Action<bool, string> check)
        {
            var expectedAll = new HashSet<string>(ExpectedCoal.Concat(ExpectedNonCoal));
            check(expectedAll.SetEquals(LocomotiveProfiles.ListedLiveries) && LocomotiveProfiles.ListedLiveries.Count == expectedAll.Count,
                $"LocomotiveProfiles lists all {expectedAll.Count} supported locomotive liveries");
            check(new HashSet<string>(ExpectedCoal).SetEquals(LocomotiveProfiles.CoalLiveries) && FireboxRegistry.SupportedLiveries.Count == ExpectedCoal.Length
                  && FireboxRegistry.SupportedLiveries.All(LocomotiveProfiles.CoalLiveries.Contains),
                $"FireboxRegistry.SupportedLiveries matches all {ExpectedCoal.Length} coal liveries from LocomotiveProfiles");
            check(LocomotiveProfiles.All.All(p => !string.IsNullOrEmpty(p.Id) && p.Liveries != null && p.Liveries.Length > 0)
                  && LocomotiveProfiles.All.SelectMany(p => p.Liveries).Count() == LocomotiveProfiles.ListedLiveries.Count,
                "every LocomotiveProfile has a non-empty Id and unique livery list");

            // Multi-livery aliases resolve to the exact same profile instance.
            check(ReferenceEquals(LocomotiveProfiles.For("em_lms_8f_loc_black"), LocomotiveProfiles.For("em_lms_8f_loc_grey"))
                  && ReferenceEquals(LocomotiveProfiles.For("em_fowler_4f_44422"), LocomotiveProfiles.For("em_fowler_4f_4027"))
                  && ReferenceEquals(LocomotiveProfiles.For("GWI40xx"), LocomotiveProfiles.For("GWI_Manor")),
                "multi-livery aliases (8F, 4F, GWI) share identical LocomotiveProfile instances");
            check(LocomotiveProfiles.TryGetTender("em_lms_stanier_tender_1_black", out var blackTender)
                  && LocomotiveProfiles.TryGetTender("em_lms_stanier_tender_1_grey", out var greyTender)
                  && ReferenceEquals(blackTender, greyTender) && blackTender.HasWaterValves
                  && blackTender.WaterValveHandles.Length == 2 && blackTender.WaterValveTravel > 0f,
                "Stanier tender liveries share one TenderProfile with two water valve handles");

            // Excluded types stay completely out of the registry and LeftStock.
            foreach (var excluded in ExcludedTypes)
            {
                check(!LocomotiveProfiles.Contains(excluded) && !FireboxRegistry.SupportedLiveries.Contains(excluded)
                      && !LeftStock.Listed(excluded) && LeftStock.Is(excluded, excluded, unlistedToo: true),
                    $"excluded type '{excluded}' is absent from LocomotiveProfiles and stays stock even with unlistedToo");
            }
            check(expectedAll.All(LeftStock.Listed) && !LeftStock.Listed("UnlistedLoco"),
                "LeftStock.Listed matches LocomotiveProfiles.Contains");

            // Consumers read cleanly from LocomotiveProfiles without drift.
            check(BoilerFit.TryBurnTime("S464-730", 100f, 0.7f, oil: false, out var j3Burn) && j3Burn > 0f
                  && BoilerFit.TryBurnTime("A.T.&S.F. 2901-A", 100f, 0.7f, oil: true, out var sfBurn) && sfBurn > 0f
                  && !BoilerFit.TryBurnTime("LocoS282A", 100f, 0.7f, oil: false, out _),
                "BoilerFit resolves prototype evaporation from LocomotiveProfiles");

            check(BlowerFit.TryGetDraft("FEF-3", out var fefDraft) && fefDraft.MaxBlower == 0.85f && fefDraft.Natural == 0.9f
                  && !BlowerFit.TryGetDraft("GWI40xx", out _),
                "BlowerFit resolves blower/natural draft specs from LocomotiveProfiles");

            check(SafetyValveFit.TryGetFit("em_lms_8f_loc_black", out var v8f) && v8f.Lift == 16.5f && v8f.Capacity == 4f
                  && SafetyValveFit.TryGetFit("S2102-870", out var vQj) && vQj.Lift == 17.3f && vQj.Capacity == 7.5f
                  && !SafetyValveFit.TryGetFit("GWI40xx", out _),
                "SafetyValveFit resolves lifts and capacities from LocomotiveProfiles");

            check(EngineSteamPath.TryFit("LocoS282A", 300f, 6f, out var s282Path)
                  && s282Path.Regulator == RegulatorLocation.BeforeSuperheater && s282Path.PipeVolume == 300
                  && EngineSteamPath.TryFit("S464-730", 500f, 10f, out var j3Path)
                  && j3Path.Regulator == RegulatorLocation.AfterSuperheater && j3Path.PipeVolume == 600
                  && EngineSteamPath.SaturatedPath("LocoS060") && EngineSteamPath.SaturatedPath("S440NCF-400A")
                  && !EngineSteamPath.SaturatedPath("LocoS282A"),
                "EngineSteamPath reads superheated and saturated steam paths from LocomotiveProfiles");

            check(FireboxCharacters.ForLivery("LocoS282A").ArchLength == 1f
                  && FireboxCharacters.ForLivery("LocoS282A").Jets != null
                  && FireboxCharacters.ForLivery("S2102-870").PrototypeGrateArea == 6.8f
                  && FireboxCharacters.ForLivery("S464-730").StokerLumpsLeft == 0.1f,
                "FireboxCharacters reads per-locomotive fireboxes from LocomotiveProfiles");

            check(DriftingFittings.Upgradable("LocoS282A") && DriftingFittings.Upgradable("LocoS060")
                  && !DriftingFittings.Upgradable("S464-730")
                  && DriftingFittings.MayHave("A.T.&S.F. 2901-A") && DriftingFittings.MayHave("em_lms_8f_loc_black")
                  && !DriftingFittings.MayHave("Jk3A") && DriftingFittings.CanHaveValve("LocoS282A")
                  && !DriftingFittings.CanHaveValve("A.T.&S.F. 2901-A"),
                "DriftingFittings reads upgradable and prototype fittings from LocomotiveProfiles");

            check(AutoFireDoorFit.Prototype.Count == 4
                  && AutoFireDoorFit.Prototype.Contains("S464-730")
                  && AutoFireDoorFit.Prototype.Contains("FEF-3")
                  && AutoFireDoorFit.Prototype.Contains("S484-840A")
                  && AutoFireDoorFit.Prototype.Contains("S2102-870"),
                "AutoFireDoorFit.Prototype derives all 4 prototype auto-door liveries from LocomotiveProfiles");

            check(InjectorRig.HasTwoValveGraph("Jk3A") && InjectorRig.HasTwoValveGraph("em_lms_8f_loc_black")
                  && InjectorRig.HasTwoValveGraph("em_fowler_4f_44422") && !InjectorRig.HasTwoValveGraph("LocoS282A")
                  && InjectorValves.OwnLeverIsSteam("em_lms_8f_loc_grey") && InjectorValves.OwnLeverIsSteam("em_fowler_4f_4027")
                  && !InjectorValves.OwnLeverIsSteam("LocoS282A")
                  && InjectorValves.Lifting("LocoS282A") && InjectorValves.Lifting("Jk3A")
                  && !InjectorValves.Lifting("LocoS060") && !InjectorValves.Lifting("S464-730"),
                "InjectorRig and InjectorValves read two-valve graphs, lever roles, and lifting flags from LocomotiveProfiles");

            check(TryCockRig.Fits("LocoS282A") && TryCockRig.Fits("LocoS060") && !TryCockRig.Fits("Jk3A")
                  && FiringFrame.Reversed("S440NCF-400A") && !FiringFrame.Reversed("LocoS282A"),
                "TryCockRig and FiringFrame read from LocomotiveProfiles");

            check(ExpectedCoal.All(l => FireholeOpening.TryHole(l, out _))
                  && ExpectedCoal.All(l => AshpanLever.TrySeat(l, out _))
                  && ShedConnections.HasBlowerConnection("LocoS282A")
                  && !ShedConnections.HasBlowerConnection("em_lms_8f_loc_black")
                  && !ShedConnections.HasBlowerConnection("S040N-270")
                  && !ShedConnections.HasBlowerConnection("S440NCF-400A")
                  && ShedConnections.HasBlowerConnection("UnlistedLoco")
                  && !ShedConnections.HasWaterConnection("S040N-270")
                  && ShedConnections.HasWaterConnection("em_lms_8f_loc_black")
                  && ShedConnections.HasWaterConnection("UnlistedLoco"),
                "FireholeOpening, AshpanLever, and ShedConnections read seats and exclusions from LocomotiveProfiles");

            check(DropGrateCab.SeatFor("S440NCF-400A") == new Vector3(0f, 0f, 0.3f)
                  && FeedPumpCab.SpotFor("LocoS282A").ValveInCar == new Vector3(-0.46f, 2.9f, 0.06f)
                  && InjectorSteamCab.OnSide("S440NCF-400A") && !InjectorSteamCab.OnSide("LocoS282A")
                  && CabWaterRegulators.CutFromMesh("em_lms_8f_loc_black")
                  && CabWaterRegulators.InPlaceFor("em_fowler_4f_44422") != null
                  && StokerCab.PlacementFor("S464-730").Turn == 8f
                  && StokerCab.LeverMoveFor("LocoS282A") == new Vector3(0f, 0f, -0.3f)
                  && QjStokerCab.StokerSeatFor(QjStokerCab.Livery) == new Vector3(0.12f, 0.17f, 0.06f)
                  && PlainFireboxWalls.Tones.ContainsKey("S2102-720") && PlainFireboxWalls.Tones.Count == 1,
                "cab visual placements and PlainFireboxWalls.Tones read consistently from LocomotiveProfiles");

            check(LocomotiveProfiles.For("LocoS282A").Cab.JetsValveSeat.HasValue
                  && LocomotiveProfiles.For("LocoS060").Cab.JetsValveOwnHandle
                  && LocomotiveProfiles.For("LocoS282A").Cab.DriftingValveSeat.HasValue
                  && LocomotiveProfiles.For("LocoS060").Cab.DriftingValveOwnHandle
                  && LocomotiveProfiles.For("LocoS282A").Cab.BoosterCarSeat == new Vector3(1.45f, 1.60f, -0.24f)
                  && LocomotiveProfiles.For("S464-730").Cab.BoosterPlacement == new Vector3(-26f, 15f, 0f)
                  && LocomotiveProfiles.For("LocoS282A").Cab.BrickArch.HasValue
                  && LocomotiveProfiles.For("LocoS060").Cab.BrickArch.HasValue
                  && LocomotiveProfiles.For("LocoS060").Cab.FireboxLightGain == 1.6f
                  && LocomotiveProfiles.For("A.T.&S.F. 2901-A").Cab.OilBurnerLooseWheels != null
                  && LocomotiveProfiles.For("A.T.&S.F. 2901-A").Cab.OilBurnerLooseWheels.Count == 3,
                "Jets, DriftingValve, Booster, BrickArch, FireboxLightGain, and OilBurner cab metadata live in LocomotiveProfiles");
        }
    }
}
