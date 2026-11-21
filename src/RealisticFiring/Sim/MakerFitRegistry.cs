using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RealisticFiring.Core;
using RealisticFiring.Visuals;
using UnityEngine;

namespace RealisticFiring.Sim
{
    /// <summary>
    /// Discovers, validates, and serves third-party realistic-firing.json maker
    /// fits from installed mod folders (Mods/*/realistic-firing.json and
    /// Mods/*/realistic-firing/*.json) and RealisticFiring/Fits/*.json.
    /// Built-in profiles and LeftStock excluded types always take precedence.
    /// </summary>
    internal static class MakerFitRegistry
    {
        public const string FileName = "realistic-firing.json";
        public const string SubfolderName = "realistic-firing";
        public const string UserFitsFolderName = "Fits";
        internal const long MaxFileBytes = 512 * 1024;

        private static MakerFitCatalog catalog = CreateCatalog();
        private static readonly Dictionary<string, FireboxCharacters.Firebox> FireboxCache = new Dictionary<string, FireboxCharacters.Firebox>(StringComparer.Ordinal);
        private static IReadOnlyCollection<string> coalLiveriesCache;
        private static string sessionFingerprintCache;

        private static MakerFitCatalog CreateCatalog() =>
            new MakerFitCatalog(
                FireboxRegistry.BuiltInLiveries.Concat(LeftStock.BuiltInChecked),
                LeftStock.ExcludedTypes,
                TenderWaterValves.FittedLiveries);

        internal static MakerFitCatalog Catalog => catalog;

        internal static string SessionFingerprint() =>
            sessionFingerprintCache ?? (sessionFingerprintCache = catalog.ComputeFingerprint());

        internal static void Reset()
        {
            catalog = CreateCatalog();
            FireboxCache.Clear();
            coalLiveriesCache = null;
            sessionFingerprintCache = null;
        }

        /// <summary>
        /// Scans the sibling Mods/* folders and <paramref name="modPath"/>/Fits
        /// for maker-fit JSON files. Safe to call at startup; malformed files
        /// are logged and skipped without throwing.
        /// </summary>
        internal static int LoadFromModDirectory(string modPath, Action<string> log = null, Action<string> warn = null)
        {
            if (string.IsNullOrWhiteSpace(modPath))
            {
                Reset();
                return 0;
            }
            var trimmed = modPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var modsRoot = Path.GetDirectoryName(trimmed);
            var userFitsDir = Path.Combine(trimmed, UserFitsFolderName);
            return LoadFromDirectories(modsRoot, userFitsDir, log, warn);
        }

        internal static int LoadFromDirectories(string modsRoot, string userFitsDir, Action<string> log = null, Action<string> warn = null)
        {
            Reset();
            var files = DiscoverFiles(modsRoot, userFitsDir, warn);
            var loaded = 0;
            foreach (var item in files)
            {
                string json;
                try
                {
                    var info = new FileInfo(item.Path);
                    if (info.Length > MaxFileBytes)
                    {
                        warn?.Invoke($"Maker fit {item.Label}: skipped because file size ({info.Length} bytes) exceeds the {MaxFileBytes}-byte limit.");
                        continue;
                    }
                    json = File.ReadAllText(item.Path);
                }
                catch (Exception e)
                {
                    warn?.Invoke($"Maker fit {item.Label}: could not read file ({e.GetType().Name}: {e.Message})");
                    continue;
                }

                var result = MakerFitParser.Parse(json);
                if (!result.Ok)
                {
                    warn?.Invoke($"Maker fit {item.Label}: skipped due to {result.Errors.Count} validation error(s): {string.Join("; ", result.Errors)}");
                    continue;
                }

                var accepted = catalog.Register(item.Label, item.Tier, result.Profile, log, warn);
                if (accepted > 0)
                {
                    loaded += accepted;
                    log?.Invoke($"Maker fit {item.Label}: loaded {result.Profile.Fuel.ToString().ToLowerInvariant()} profile for {string.Join(", ", result.Profile.Liveries.Where(catalog.Contains))}");
                }
            }
            coalLiveriesCache = null;
            sessionFingerprintCache = null;
            return loaded;
        }

        internal static List<(string Path, string Label, MakerFitSourceTier Tier)> DiscoverFiles(string modsRoot, string userFitsDir, Action<string> warn = null)
        {
            var discovered = new List<(string Path, string Label, MakerFitSourceTier Tier)>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddFile(string filePath, string label, MakerFitSourceTier tier)
            {
                try
                {
                    var full = Path.GetFullPath(filePath);
                    if (File.Exists(full) && seenPaths.Add(full))
                        discovered.Add((full, label, tier));
                }
                catch (Exception e)
                {
                    warn?.Invoke($"Maker fit discovery skipped '{filePath}': {e.Message}");
                }
            }

            if (!string.IsNullOrWhiteSpace(modsRoot) && Directory.Exists(modsRoot))
            {
                string[] modDirs;
                try
                {
                    modDirs = Directory.GetDirectories(modsRoot);
                    Array.Sort(modDirs, StringComparer.OrdinalIgnoreCase);
                }
                catch (Exception e)
                {
                    warn?.Invoke($"Maker fit discovery could not scan '{modsRoot}': {e.Message}");
                    modDirs = Array.Empty<string>();
                }

                foreach (var modDir in modDirs)
                {
                    var modName = Path.GetFileName(modDir);
                    var single = Path.Combine(modDir, FileName);
                    if (File.Exists(single))
                        AddFile(single, $"{modName}/{FileName}", MakerFitSourceTier.Mod);

                    var subDir = Path.Combine(modDir, SubfolderName);
                    if (!Directory.Exists(subDir))
                        continue;
                    try
                    {
                        var subFiles = Directory.GetFiles(subDir, "*.json");
                        Array.Sort(subFiles, StringComparer.OrdinalIgnoreCase);
                        foreach (var file in subFiles)
                            AddFile(file, $"{modName}/{SubfolderName}/{Path.GetFileName(file)}", MakerFitSourceTier.Mod);
                    }
                    catch (Exception e)
                    {
                        warn?.Invoke($"Maker fit discovery could not scan '{subDir}': {e.Message}");
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(userFitsDir) && Directory.Exists(userFitsDir))
            {
                try
                {
                    var userFiles = Directory.GetFiles(userFitsDir, "*.json");
                    Array.Sort(userFiles, StringComparer.OrdinalIgnoreCase);
                    foreach (var file in userFiles)
                        AddFile(file, $"{UserFitsFolderName}/{Path.GetFileName(file)}", MakerFitSourceTier.User);
                }
                catch (Exception e)
                {
                    warn?.Invoke($"Maker fit discovery could not scan '{userFitsDir}': {e.Message}");
                }
            }

            return discovered;
        }

        internal static bool HasLivery(string livery, string carType = null) =>
            !LeftStock.IsExcluded(livery, carType) && catalog.Contains(livery);

        internal static bool HasCar(TrainCar car) =>
            car != null && car.carLivery != null && !LeftStock.IsExcluded(car) && catalog.Contains(car.carLivery.id);

        internal static bool IsCoalLivery(string livery, string carType = null) =>
            !LeftStock.IsExcluded(livery, carType) && catalog.TryGet(livery, out var profile) && profile.Fuel == MakerFitFuel.Coal;

        internal static bool IsCoalCar(TrainCar car) =>
            car != null && car.carLivery != null && !LeftStock.IsExcluded(car)
            && catalog.TryGet(car.carLivery.id, out var profile) && profile.Fuel == MakerFitFuel.Coal;

        internal static IReadOnlyCollection<string> AllCoalLiveries(HashSet<string> builtIn)
        {
            if (catalog.Count == 0)
                return builtIn;
            if (coalLiveriesCache == null)
            {
                var set = new HashSet<string>(builtIn, StringComparer.Ordinal);
                foreach (var livery in catalog.CoalLiveries)
                    set.Add(livery);
                coalLiveriesCache = set;
            }
            return coalLiveriesCache;
        }

        internal static bool TryGetProfile(string livery, out MakerFitProfile profile) =>
            TryGetProfile(livery, null, out profile);

        internal static bool TryGetProfile(string livery, string carType, out MakerFitProfile profile)
        {
            profile = null;
            return !LeftStock.IsExcluded(livery, carType) && catalog.TryGet(livery, out profile);
        }

        internal static bool TryGetProfile(TrainCar car, out MakerFitProfile profile)
        {
            profile = null;
            return car != null && car.carLivery != null && !LeftStock.IsExcluded(car)
                && catalog.TryGet(car.carLivery.id, out profile);
        }

        internal static bool TryGetFirebox(string livery, out FireboxCharacters.Firebox firebox)
        {
            firebox = null;
            if (livery == null)
                return false;
            if (FireboxCache.TryGetValue(livery, out firebox))
                return firebox != null;
            if (!catalog.TryGet(livery, out var profile))
                return false;
            if (profile.Firebox == null && string.IsNullOrWhiteSpace(profile.Summary) && profile.Stoker == null)
                return false;

            var fb = profile.Firebox;
            var summary = !string.IsNullOrWhiteSpace(profile.Summary) ? profile.Summary : $"maker-fit firebox for {livery}";
            var grate = fb != null ? fb.ToGrateCharacter() : GrateCharacter.Typical;
            firebox = new FireboxCharacters.Firebox(summary, grate)
            {
                PrototypeGrateArea = fb?.GrateAreaM2,
                PrototypeSlope = fb?.GrateSlopeDeg,
                PrototypeSlopeOverPile = fb != null && fb.SlopeOverPile,
                StokerBackLanes = profile.Stoker?.BackLanes ?? 1f,
                StokerLumpsLeft = profile.Stoker?.LumpsLeft ?? 0f,
                Jets = fb?.OverFireJets != null ? new OverFireJets(fb.OverFireJets.Count, fb.OverFireJets.Tube) : null,
                JetsAsBuilt = fb?.OverFireJets != null,
            };
            FireboxCache[livery] = firebox;
            return true;
        }

        internal static bool IsReversedDoor(string livery) =>
            catalog.TryGet(livery, out var profile) && profile.Firebox != null && profile.Firebox.ReversedDoor;

        internal static bool TryGetMaxEvaporation(string livery, out float kgPerS)
        {
            kgPerS = 0f;
            if (!catalog.TryGet(livery, out var profile) || profile.Boiler?.MaxEvaporationKgPerS == null)
                return false;
            kgPerS = profile.Boiler.MaxEvaporationKgPerS.Value;
            return true;
        }

        internal static bool TryGetSafetyValveFit(string livery, out (float Lift, float Capacity) fit)
        {
            fit = default;
            if (!catalog.TryGet(livery, out var profile) || profile.Boiler?.SafetyValves == null)
                return false;
            fit = (profile.Boiler.SafetyValves.LiftBarAbs, profile.Boiler.SafetyValves.CapacityKgPerS);
            return true;
        }

        internal static bool TryGetLowWaterGuard(string livery, out BoilerWaterRig.Guard guard)
        {
            guard = BoilerWaterRig.Guard.None;
            if (!catalog.TryGet(livery, out var profile) || profile.Boiler?.LowWaterGuard == null)
                return false;
            switch (profile.Boiler.LowWaterGuard.Value)
            {
                case MakerFitLowWaterGuard.FusiblePlug:
                    guard = BoilerWaterRig.Guard.FusiblePlug;
                    break;
                case MakerFitLowWaterGuard.LowWaterAlarm:
                    guard = BoilerWaterRig.Guard.LowWaterAlarm;
                    break;
                default:
                    guard = BoilerWaterRig.Guard.None;
                    break;
            }
            return true;
        }

        internal static bool TryGetBlowerFit(string livery, out (float MaxBlower, float Natural, AirPump Pump, int Pumps, float MainReservoir) fit)
        {
            fit = default;
            if (!catalog.TryGet(livery, out var profile) || profile.Draft?.BlowerMaxSteamKgPerS == null || profile.Draft?.NaturalDraftKgPerS == null)
                return false;
            var pumps = profile.Draft.AirPumps;
            var pump = pumps != null && pumps.Type == MakerFitAirPumpType.CrossCompound ? AirPump.CrossCompound : AirPump.NineAndHalf;
            var count = pumps != null ? pumps.Count : 1;
            var res = pumps != null ? pumps.MainReservoirL : 0f;
            fit = (profile.Draft.BlowerMaxSteamKgPerS.Value, profile.Draft.NaturalDraftKgPerS.Value, pump, count, res);
            return true;
        }

        internal static bool UsesStockSteamPerStroke(string livery) =>
            catalog.TryGet(livery, out var profile) && profile.Cylinders?.StockSteamPerStroke == true;

        internal static bool IsSuperheaterExempt(string livery) =>
            catalog.TryGet(livery, out var profile) && profile.Cylinders?.ExemptFromSuperheater == true;

        internal static bool TryGetSuperheater(string livery, out SuperheaterSpec spec)
        {
            spec = null;
            if (!catalog.TryGet(livery, out var profile) || profile.Cylinders == null)
                return false;
            var c = profile.Cylinders;
            if (c.Superheated == false)
            {
                spec = SuperheaterSpec.None;
                return true;
            }
            if (c.Superheated == true || c.MaxSuperheatC.HasValue)
            {
                var full = c.MaxSuperheatC ?? 136f;
                var halfShare = c.HalfSuperheatShare ?? 0.72f;
                var kind = string.IsNullOrWhiteSpace(c.SuperheaterKind) ? "Type A" : c.SuperheaterKind;
                spec = new SuperheaterSpec
                {
                    Kind = kind,
                    FullSuperheat = full,
                    HalfSuperheat = full * halfShare,
                    StandingSuperheat = 400f,
                    TimeConstant = 75f,
                };
                return true;
            }
            return false;
        }

        internal static bool IsSaturatedPath(string livery) =>
            catalog.TryGet(livery, out var profile) && profile.Cylinders?.Superheated == false;

        internal static bool TryGetSteamPath(string livery, out bool frontEnd, out float pipeVolumeL)
        {
            frontEnd = false;
            pipeVolumeL = 0f;
            if (!catalog.TryGet(livery, out var profile) || profile.Cylinders == null)
                return false;
            var c = profile.Cylinders;
            if (c.Superheated == false || (!c.FrontEndThrottle.HasValue && !c.SteamCircuitVolumeL.HasValue))
                return false;
            frontEnd = c.FrontEndThrottle ?? false;
            pipeVolumeL = c.SteamCircuitVolumeL ?? (frontEnd ? 500f : 300f);
            return true;
        }

        internal static bool TryGetDriftingFittings(string livery, out DriftingProfile profileOut)
        {
            profileOut = DriftingProfile.None;
            if (!catalog.TryGet(livery, out var profile) || profile.Cylinders?.DriftingFittings == null)
                return false;
            var d = profile.Cylinders.DriftingFittings;
            profileOut = DriftingProfile.Prototype(d.Snifting, d.DriftingValve, d.ByPass);
            return true;
        }

        internal static bool HasInjectorSteamValves(string livery) =>
            catalog.TryGet(livery, out var profile) && (profile.Injectors?.SteamValves ?? true);

        internal static bool IsLiftingInjector(string livery) =>
            catalog.TryGet(livery, out var profile) && profile.Injectors?.Type == MakerFitInjectorType.Lifting;

        internal static bool OwnLeverIsSteam(string livery) =>
            catalog.TryGet(livery, out var profile) && profile.Injectors?.OwnLeverIsSteam == true;

        internal static bool HasTenderWaterValves(string tenderLivery) => catalog.HasTenderWaterValves(tenderLivery);

        internal static bool TryGetInjectorMaxFlow(string livery, out float maxFlowKgPerS)
        {
            maxFlowKgPerS = 0f;
            if (!catalog.TryGet(livery, out var profile) || profile.Injectors?.MaxFlowKgPerS == null)
                return false;
            maxFlowKgPerS = profile.Injectors.MaxFlowKgPerS.Value;
            return true;
        }

        internal static bool IsOneLeverForTwo(string livery) =>
            catalog.TryGet(livery, out var profile) && profile.Injectors?.OneLeverForTwo == true;

        internal static bool TryGetInjectorCrackOpening(string livery, out float crackOpening)
        {
            crackOpening = 0f;
            if (!catalog.TryGet(livery, out var profile) || profile.Injectors?.CrackOpening == null)
                return false;
            crackOpening = profile.Injectors.CrackOpening.Value;
            return true;
        }

        internal static bool TryGetTwoValveGraph(string livery, out MakerFitTwoValveGraph graph)
        {
            graph = null;
            if (!catalog.TryGet(livery, out var profile) || profile.Injectors?.TwoValveGraph == null)
                return false;
            graph = profile.Injectors.TwoValveGraph;
            return true;
        }

        internal static bool TryGetCutWaterRegulators(string livery, out (string Mesh, float RightTurn, float LeftTurn) fit)
        {
            fit = default;
            if (!catalog.TryGet(livery, out var profile))
                return false;
            var wr = profile.Injectors?.WaterRegulators;
            if (wr == null || string.IsNullOrWhiteSpace(wr.Mesh) || !wr.RightTurnDeg.HasValue || !wr.LeftTurnDeg.HasValue)
                return false;
            fit = (wr.Mesh, wr.RightTurnDeg.Value, wr.LeftTurnDeg.Value);
            return true;
        }

        internal static bool TryGetInPlaceWaterRegulators(string livery, out CabWaterRegulators.Levers levers)
        {
            levers = null;
            if (!catalog.TryGet(livery, out var profile))
                return false;
            var ip = profile.Injectors?.WaterRegulators?.InPlace;
            if (ip == null)
                return false;
            levers = new CabWaterRegulators.Levers
            {
                Left = ToVector3(ip.Left),
                Right = ToVector3(ip.Right),
                Reach = ip.ReachM,
                Turn = ip.TurnDeg,
            };
            return true;
        }

        internal static bool ShouldRetrofitStoker(string livery) =>
            catalog.TryGet(livery, out var profile) && profile.Stoker != null && profile.Stoker.Fitted && profile.Stoker.Retrofit;

        internal static bool TryGetStokerAnchorPort(string livery, out string port)
        {
            port = null;
            if (!catalog.TryGet(livery, out var profile) || string.IsNullOrWhiteSpace(profile.Stoker?.AnchorPort))
                return false;
            port = profile.Stoker.AnchorPort;
            return true;
        }

        internal static bool TryGetStokerSupplyPipeAt(string livery, out float at)
        {
            at = 0f;
            if (!catalog.TryGet(livery, out var profile) || profile.Stoker?.SupplyPipeAtM == null)
                return false;
            at = profile.Stoker.SupplyPipeAtM.Value;
            return true;
        }

        internal static bool TryGetFeedPump(string livery, out FeedwaterFit.Fit fit)
        {
            fit = null;
            if (!catalog.TryGet(livery, out var profile) || profile.FeedPump == null || !profile.FeedPump.Fitted)
                return false;
            fit = new FeedwaterFit.Fit(profile.FeedPump.Type, profile.FeedPump.Name, profile.FeedPump.CapacityKgPerS, profile.FeedPump.OnInjectorLever);
            return true;
        }

        internal static bool TryGetBooster(string livery, out BoosterFit.Fit fit)
        {
            fit = null;
            if (!catalog.TryGet(livery, out var profile) || profile.Booster == null || !profile.Booster.Fitted)
                return false;
            fit = new BoosterFit.Fit(profile.Booster.WheelDiameterM, profile.Booster.TrailerLoadKg, profile.Booster.DrivingAxles);
            return true;
        }

        internal static bool HasAutoFireDoor(string livery) =>
            catalog.TryGet(livery, out var profile) && profile.AutoFireDoor != null && profile.AutoFireDoor.Fitted;

        internal static bool TryGetFirehole(string livery, out (float Middle, float Foot, float Width, float Height, float Plane) hole)
        {
            hole = default;
            if (!catalog.TryGet(livery, out var profile) || profile.Cab?.Firehole == null)
                return false;
            var h = profile.Cab.Firehole;
            hole = (h.MiddleM, h.FootM, h.WidthM, h.HeightM, h.PlaneM);
            return true;
        }

        internal static bool TryGetInjectorSteamSeat(string livery, out (Vector3 Move, Vector3 Turns) seat)
        {
            seat = default;
            if (!catalog.TryGet(livery, out var profile) || profile.Cab?.InjectorSteam == null)
                return false;
            var s = profile.Cab.InjectorSteam;
            seat = (ToVector3(s.Position), ToVector3(s.Rotation));
            return true;
        }

        internal static bool TryGetInjectorSteamFixedOffset(string livery, out Vector3 offset)
        {
            offset = default;
            if (!catalog.TryGet(livery, out var profile) || profile.Cab?.InjectorSteam?.FixedOffset == null)
                return false;
            offset = ToVector3(profile.Cab.InjectorSteam.FixedOffset.Value);
            return true;
        }

        internal static bool TryGetInjectorSteamSideOffset(string livery, out Vector3 offset)
        {
            offset = default;
            if (!catalog.TryGet(livery, out var profile) || profile.Cab?.InjectorSteam?.SideOffset == null)
                return false;
            offset = ToVector3(profile.Cab.InjectorSteam.SideOffset.Value);
            return true;
        }

        internal static bool TryGetDropGrate(string livery, out Vector3 seat)
        {
            seat = default;
            if (!catalog.TryGet(livery, out var profile) || profile.Cab?.DropGrate == null)
                return false;
            seat = ToVector3(profile.Cab.DropGrate.Position);
            return true;
        }

        internal static bool TryGetAshpan(string livery, out Vector3 seat)
        {
            seat = default;
            if (!catalog.TryGet(livery, out var profile) || profile.Cab?.Ashpan == null)
                return false;
            seat = ToVector3(profile.Cab.Ashpan.Position);
            return true;
        }

        internal static bool AshpanNoHanger(string livery) =>
            catalog.TryGet(livery, out var profile) && profile.Cab?.Ashpan != null && profile.Cab.Ashpan.NoHanger;

        internal static bool TryGetStokerManifold(string livery, out StokerCab.Placement placement)
        {
            placement = default;
            if (!catalog.TryGet(livery, out var profile) || profile.Cab?.StokerManifold == null)
                return false;
            var m = profile.Cab.StokerManifold;
            placement = new StokerCab.Placement(ToVector3(m.OffsetCm), m.TurnDeg, m.TiltDeg);
            return true;
        }

        internal static bool TryGetStokerLever(string livery, out Vector3 move)
        {
            move = default;
            if (!catalog.TryGet(livery, out var profile) || profile.Cab?.StokerLever == null)
                return false;
            move = ToVector3(profile.Cab.StokerLever.Position);
            return true;
        }

        internal static bool TryGetFeedPumpSpot(string livery, out FeedPumpCab.Spot spot)
        {
            spot = default;
            if (!catalog.TryGet(livery, out var profile) || profile.Cab?.FeedPump == null)
                return false;
            var p = profile.Cab.FeedPump;
            if (!p.ValveInCar.HasValue && !p.ValveOffset.HasValue && !p.GaugeOffset.HasValue)
                return false;
            var valve = p.ValveOffset.HasValue ? ToVector2(p.ValveOffset.Value) : Vector2.zero;
            var gauge = p.GaugeOffset.HasValue ? ToVector2(p.GaugeOffset.Value) : new Vector2(0f, 0.12f);
            Vector3? inCar = p.ValveInCar.HasValue ? ToVector3(p.ValveInCar.Value) : (Vector3?)null;
            spot = new FeedPumpCab.Spot(valve, gauge, inCar);
            return true;
        }

        internal static bool TryGetFeedPumpWallGauge(string livery, out Vector3 seat)
        {
            seat = default;
            if (!catalog.TryGet(livery, out var profile) || profile.Cab?.FeedPump?.WallGauge == null)
                return false;
            seat = ToVector3(profile.Cab.FeedPump.WallGauge.Value);
            return true;
        }

        internal static bool TryGetBoosterCarSeat(string livery, out Vector3 seat)
        {
            seat = default;
            if (!catalog.TryGet(livery, out var profile) || profile.Cab?.Booster?.Position == null)
                return false;
            seat = ToVector3(profile.Cab.Booster.Position.Value);
            return true;
        }

        internal static bool TryGetBoosterReverserOffset(string livery, out Vector3 offsetCm)
        {
            offsetCm = default;
            if (!catalog.TryGet(livery, out var profile) || profile.Cab?.Booster?.ReverserOffsetCm == null)
                return false;
            offsetCm = ToVector3(profile.Cab.Booster.ReverserOffsetCm.Value);
            return true;
        }

        internal static CabValve.Seat? TryGetJetsSeat(string livery) =>
            catalog.TryGet(livery, out var profile) && profile.Cab?.Jets != null ? ToCabValveSeat(profile.Cab.Jets) : (CabValve.Seat?)null;

        internal static bool JetsOwnHandle(string livery) =>
            catalog.TryGet(livery, out var profile) && profile.Cab?.Jets != null && profile.Cab.Jets.OwnHandle;

        internal static CabValve.Seat? TryGetDriftingValveSeat(string livery) =>
            catalog.TryGet(livery, out var profile) && profile.Cab?.DriftingValve != null ? ToCabValveSeat(profile.Cab.DriftingValve) : (CabValve.Seat?)null;

        internal static bool DriftingValveOwnHandle(string livery) =>
            catalog.TryGet(livery, out var profile) && profile.Cab?.DriftingValve != null && profile.Cab.DriftingValve.OwnHandle;

        internal static bool HasShedBlower(string livery) =>
            !(catalog.TryGet(livery, out var profile) && profile.Cab?.ShedBlower != null && !profile.Cab.ShedBlower.Enabled);

        internal static bool HasShedWater(string livery) =>
            !(catalog.TryGet(livery, out var profile) && profile.Cab?.ShedWater != null && !profile.Cab.ShedWater.Enabled);

        internal static bool TryGetShedSeat(string livery, bool water, out (Vector3 At, float Swing, float Down, float Spin, float Out) seat)
        {
            seat = default;
            if (!catalog.TryGet(livery, out var profile))
                return false;
            var s = water ? profile.Cab?.ShedWater : profile.Cab?.ShedBlower;
            if (s == null || !s.Enabled || !s.Position.HasValue)
                return false;
            seat = (ToVector3(s.Position.Value), s.SwingDeg, s.DownDeg, s.SpinDeg, s.OutM);
            return true;
        }

        internal static bool TryGetShedLandmark(string livery, bool water, out string landmark)
        {
            landmark = null;
            if (!catalog.TryGet(livery, out var profile))
                return false;
            var s = water ? profile.Cab?.ShedWater : profile.Cab?.ShedBlower;
            if (s == null || !s.Enabled || string.IsNullOrWhiteSpace(s.Landmark))
                return false;
            landmark = s.Landmark;
            return true;
        }

        internal static bool TryGetStackGeometry(string livery, out RealisticStackEffects.Core.StackGeometryHint hint)
        {
            hint = default;
            if (!catalog.TryGet(livery, out var profile) || profile.Stack == null || !profile.Stack.HasAny)
                return false;
            hint = new RealisticStackEffects.Core.StackGeometryHint
            {
                LipHeight = profile.Stack.LipHeightM,
                BoreRadius = profile.Stack.BoreRadiusM,
                OutsideRadius = profile.Stack.OutsideRadiusM,
            }.Sanitize();
            return hint.HasAny;
        }

        private static CabValve.Seat ToCabValveSeat(MakerFitCabValveSeat s) =>
            new CabValve.Seat(ToVector3(s.Position), ToVector3(s.Rotation), ToVector3(s.PlateOffset),
                s.PlateSpinDeg, s.PlateTurnDeg, s.PlateSquare, s.InCar);

        private static Vector2 ToVector2(MakerFitVec2 v) => new Vector2(v.X, v.Y);
        private static Vector3 ToVector3(MakerFitVec3 v) => new Vector3(v.X, v.Y, v.Z);
    }
}
