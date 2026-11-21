using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace RealisticFiring.Core
{
    public enum MakerFitFuel
    {
        Coal,
        Oil,
    }

    public enum MakerFitLowWaterGuard
    {
        None,
        FusiblePlug,
        LowWaterAlarm,
    }

    public enum MakerFitAirPumpType
    {
        NineAndHalf,
        CrossCompound,
    }

    public enum MakerFitInjectorType
    {
        NonLifting,
        Lifting,
    }

    public enum MakerFitSourceTier
    {
        Mod = 0,
        User = 1,
    }

    public readonly struct MakerFitVec2 : IEquatable<MakerFitVec2>
    {
        public MakerFitVec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public float X { get; }
        public float Y { get; }

        public bool Equals(MakerFitVec2 other) => X.Equals(other.X) && Y.Equals(other.Y);
        public override bool Equals(object obj) => obj is MakerFitVec2 other && Equals(other);
        public override int GetHashCode() => (X.GetHashCode() * 397) ^ Y.GetHashCode();
        public override string ToString() => $"({X.ToString("0.###", CultureInfo.InvariantCulture)}, {Y.ToString("0.###", CultureInfo.InvariantCulture)})";
    }

    public readonly struct MakerFitVec3 : IEquatable<MakerFitVec3>
    {
        public MakerFitVec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float X { get; }
        public float Y { get; }
        public float Z { get; }

        public bool IsZero => X == 0f && Y == 0f && Z == 0f;

        public bool Equals(MakerFitVec3 other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        public override bool Equals(object obj) => obj is MakerFitVec3 other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = X.GetHashCode();
                hash = (hash * 397) ^ Y.GetHashCode();
                hash = (hash * 397) ^ Z.GetHashCode();
                return hash;
            }
        }
        public override string ToString() => $"({X.ToString("0.###", CultureInfo.InvariantCulture)}, {Y.ToString("0.###", CultureInfo.InvariantCulture)}, {Z.ToString("0.###", CultureInfo.InvariantCulture)})";
    }

    public sealed class MakerFitProfile
    {
        public const int CurrentSchemaVersion = 1;
        public const float LbPerHourToKgPerS = 0.45359237f / 3600f;
        public const float UsGallonsPerHourToKgPerS = 3.785411784f / 3600f;
        public const float PsiPerBar = 14.5038f;
        public const float AtmosphericBar = 1.01325f;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public IReadOnlyList<string> Liveries { get; set; } = Array.Empty<string>();
        public MakerFitFuel Fuel { get; set; } = MakerFitFuel.Coal;
        public string Summary { get; set; }
        public MakerFitFirebox Firebox { get; set; }
        public MakerFitBoiler Boiler { get; set; }
        public MakerFitDraft Draft { get; set; }
        public MakerFitCylinders Cylinders { get; set; }
        public MakerFitInjectors Injectors { get; set; }
        public MakerFitStoker Stoker { get; set; }
        public MakerFitFeedPump FeedPump { get; set; }
        public MakerFitBooster Booster { get; set; }
        public MakerFitAutoFireDoor AutoFireDoor { get; set; }
        public MakerFitCab Cab { get; set; }
        public MakerFitStack Stack { get; set; }
    }

    public sealed class MakerFitOverFireJets
    {
        public int Count { get; set; }
        public JetTube Tube { get; set; } = JetTube.Large;
    }

    public sealed class MakerFitFirebox
    {
        public float? GrateAreaM2 { get; set; }
        public float? GrateSlopeDeg { get; set; }
        public bool SlopeOverPile { get; set; }
        public float? BackRowDraft { get; set; }
        public float? FrontRowDraft { get; set; }
        public float? SideDraft { get; set; }
        public float? OverFireMixing { get; set; }
        public float? AshFraction { get; set; }
        public float? ClinkerFactor { get; set; }
        public bool ReversedDoor { get; set; }
        public MakerFitOverFireJets OverFireJets { get; set; }

        public GrateCharacter ToGrateCharacter() => new GrateCharacter
        {
            BackRowDraft = BackRowDraft ?? GrateCharacter.Typical.BackRowDraft,
            FrontRowDraft = FrontRowDraft ?? GrateCharacter.Typical.FrontRowDraft,
            SideDraft = SideDraft ?? GrateCharacter.Typical.SideDraft,
            OverFireMixing = OverFireMixing ?? GrateCharacter.Typical.OverFireMixing,
            AshFactor = AshFraction ?? GrateCharacter.Typical.AshFactor,
            ClinkerFactor = ClinkerFactor ?? GrateCharacter.Typical.ClinkerFactor,
        };
    }

    public sealed class MakerFitSafetyValves
    {
        public float LiftBarAbs { get; set; }
        public float CapacityKgPerS { get; set; }
    }

    public sealed class MakerFitBoiler
    {
        public float? MaxEvaporationKgPerS { get; set; }
        public MakerFitSafetyValves SafetyValves { get; set; }
        public MakerFitLowWaterGuard? LowWaterGuard { get; set; }
    }

    public sealed class MakerFitAirPumps
    {
        public MakerFitAirPumpType Type { get; set; } = MakerFitAirPumpType.NineAndHalf;
        public int Count { get; set; } = 1;
        public float MainReservoirL { get; set; }
    }

    public sealed class MakerFitDraft
    {
        public float? BlowerMaxSteamKgPerS { get; set; }
        public float? NaturalDraftKgPerS { get; set; }
        public MakerFitAirPumps AirPumps { get; set; }
    }

    public sealed class MakerFitDriftingFittings
    {
        public bool Snifting { get; set; }
        public bool DriftingValve { get; set; }
        public bool ByPass { get; set; }
    }

    public sealed class MakerFitCylinders
    {
        public bool? StockSteamPerStroke { get; set; }
        public bool? Superheated { get; set; }
        public string SuperheaterKind { get; set; }
        public float? MaxSuperheatC { get; set; }
        public float? HalfSuperheatShare { get; set; }
        public bool? ExemptFromSuperheater { get; set; }
        public bool? FrontEndThrottle { get; set; }
        public float? SteamCircuitVolumeL { get; set; }
        public MakerFitDriftingFittings DriftingFittings { get; set; }
    }

    public sealed class MakerFitInPlaceRegulators
    {
        public MakerFitVec3 Left { get; set; }
        public MakerFitVec3 Right { get; set; }
        public float ReachM { get; set; }
        public float TurnDeg { get; set; }
    }

    public sealed class MakerFitWaterRegulators
    {
        public string Mesh { get; set; }
        public float? RightTurnDeg { get; set; }
        public float? LeftTurnDeg { get; set; }
        public MakerFitInPlaceRegulators InPlace { get; set; }
    }

    public sealed class MakerFitTwoValveGraph
    {
        public string Add { get; set; }
        public string AddA { get; set; }
        public string AddB { get; set; }
        public string Multiplier { get; set; }
        public string MultiplierOut { get; set; }
        public string Factor { get; set; }
        public string FactorPort { get; set; }
        public string Right { get; set; }
        public string Left { get; set; }
    }

    public sealed class MakerFitInjectors
    {
        public MakerFitInjectorType? Type { get; set; }
        public float? MaxFlowKgPerS { get; set; }
        public bool? OneLeverForTwo { get; set; }
        public float? CrackOpening { get; set; }
        public bool? SteamValves { get; set; }
        public bool? OwnLeverIsSteam { get; set; }
        public MakerFitWaterRegulators WaterRegulators { get; set; }
        public MakerFitTwoValveGraph TwoValveGraph { get; set; }
        public IReadOnlyList<string> TenderLiveries { get; set; }
    }

    public sealed class MakerFitStoker
    {
        public bool Fitted { get; set; }
        public bool Retrofit { get; set; }
        public string AnchorPort { get; set; }
        public float? BackLanes { get; set; }
        public float? LumpsLeft { get; set; }
        public float? SupplyPipeAtM { get; set; }
    }

    public sealed class MakerFitFeedPump
    {
        public bool Fitted { get; set; } = true;
        public FeedHeaterType Type { get; set; } = FeedHeaterType.Open;
        public string Name { get; set; }
        public float CapacityKgPerS { get; set; }
        public bool OnInjectorLever { get; set; }
    }

    public sealed class MakerFitBooster
    {
        public bool Fitted { get; set; } = true;
        public float WheelDiameterM { get; set; }
        public float TrailerLoadKg { get; set; }
        public int DrivingAxles { get; set; }
    }

    public sealed class MakerFitAutoFireDoor
    {
        public bool Fitted { get; set; } = true;
    }

    public sealed class MakerFitFirehole
    {
        public float MiddleM { get; set; }
        public float FootM { get; set; }
        public float WidthM { get; set; }
        public float HeightM { get; set; }
        public float PlaneM { get; set; }
    }

    public sealed class MakerFitInjectorSteamSeat
    {
        public MakerFitVec3 Position { get; set; }
        public MakerFitVec3 Rotation { get; set; } = new MakerFitVec3(-180f, 0f, 2f);
        public MakerFitVec3? FixedOffset { get; set; }
        public MakerFitVec3? SideOffset { get; set; }
    }

    public sealed class MakerFitTransformSeat
    {
        public MakerFitVec3 Position { get; set; }
    }

    public sealed class MakerFitAshpanSeat
    {
        public MakerFitVec3 Position { get; set; }
        public bool NoHanger { get; set; }
    }

    public sealed class MakerFitStokerManifoldSeat
    {
        public MakerFitVec3 OffsetCm { get; set; }
        public float TurnDeg { get; set; }
        public float TiltDeg { get; set; }
    }

    public sealed class MakerFitFeedPumpSeat
    {
        public MakerFitVec3? ValveInCar { get; set; }
        public MakerFitVec2? ValveOffset { get; set; }
        public MakerFitVec2? GaugeOffset { get; set; }
        public MakerFitVec3? WallGauge { get; set; }
    }

    public sealed class MakerFitBoosterSeat
    {
        public MakerFitVec3? Position { get; set; }
        public MakerFitVec3? ReverserOffsetCm { get; set; }
    }

    public sealed class MakerFitCabValveSeat
    {
        public MakerFitVec3 Position { get; set; }
        public MakerFitVec3 Rotation { get; set; }
        public bool InCar { get; set; } = true;
        public MakerFitVec3 PlateOffset { get; set; }
        public float PlateSpinDeg { get; set; }
        public float PlateTurnDeg { get; set; }
        public bool PlateSquare { get; set; }
        public bool OwnHandle { get; set; }
    }

    public sealed class MakerFitShedSeat
    {
        public bool Enabled { get; set; } = true;
        public MakerFitVec3? Position { get; set; }
        public float SwingDeg { get; set; }
        public float DownDeg { get; set; }
        public float SpinDeg { get; set; }
        public float OutM { get; set; }
        public string Landmark { get; set; }
    }

    public sealed class MakerFitCab
    {
        public MakerFitFirehole Firehole { get; set; }
        public MakerFitInjectorSteamSeat InjectorSteam { get; set; }
        public MakerFitTransformSeat DropGrate { get; set; }
        public MakerFitAshpanSeat Ashpan { get; set; }
        public MakerFitStokerManifoldSeat StokerManifold { get; set; }
        public MakerFitTransformSeat StokerLever { get; set; }
        public MakerFitFeedPumpSeat FeedPump { get; set; }
        public MakerFitBoosterSeat Booster { get; set; }
        public MakerFitCabValveSeat Jets { get; set; }
        public MakerFitCabValveSeat DriftingValve { get; set; }
        public MakerFitShedSeat ShedBlower { get; set; }
        public MakerFitShedSeat ShedWater { get; set; }

        public bool HasAny =>
            Firehole != null || InjectorSteam != null || DropGrate != null || Ashpan != null
            || StokerManifold != null || StokerLever != null || FeedPump != null || Booster != null
            || Jets != null || DriftingValve != null || ShedBlower != null || ShedWater != null;
    }

    public sealed class MakerFitStack
    {
        public float? LipHeightM { get; set; }
        public float? BoreRadiusM { get; set; }
        public float? OutsideRadiusM { get; set; }

        public bool HasAny => LipHeightM.HasValue || BoreRadiusM.HasValue || OutsideRadiusM.HasValue;
    }

    public sealed class MakerFitParseResult
    {
        public MakerFitProfile Profile { get; }
        public IReadOnlyList<string> Errors { get; }
        public bool Ok => Profile != null && Errors.Count == 0;

        public MakerFitParseResult(MakerFitProfile profile, IReadOnlyList<string> errors)
        {
            Profile = profile;
            Errors = errors ?? Array.Empty<string>();
        }
    }

    /// <summary>
    /// Parses and strictly validates declarative realistic-firing.json documents
    /// (schemaVersion 1) into <see cref="MakerFitProfile"/> instances without
    /// throwing on malformed or out-of-range input.
    /// </summary>
    public static class MakerFitParser
    {
        private static readonly HashSet<string> RootKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "schemaVersion", "liveries", "fuel", "summary",
            "firebox", "boiler", "draft", "cylinders", "injectors",
            "stoker", "feedPump", "booster", "autoFireDoor", "cab", "stack",
        };

        private static readonly HashSet<string> StackKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "lipHeightM", "boreRadiusM", "outsideRadiusM",
        };

        private static readonly HashSet<string> FireboxKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "grateAreaM2", "grateSlopeDeg", "slopeOverPile",
            "backRowDraft", "frontRowDraft", "sideDraft", "overFireMixing",
            "ashFraction", "ashFactor", "clinkerFactor", "reversedDoor", "overFireJets",
        };

        private static readonly HashSet<string> JetsKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "count", "tube",
        };

        private static readonly HashSet<string> BoilerKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "maxEvaporationKgPerS", "maxEvaporationLbPerHr", "safetyValves",
            "lowWaterGuard", "fusiblePlug", "lowWaterAlarm",
        };

        private static readonly HashSet<string> SafetyValvesKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "liftBarAbs", "liftPsiGauge", "capacityKgPerS",
        };

        private static readonly HashSet<string> DraftKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "blowerMaxSteamKgPerS", "naturalDraftKgPerS", "airPumps",
        };

        private static readonly HashSet<string> AirPumpsKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "type", "count", "mainReservoirL",
        };

        private static readonly HashSet<string> CylindersKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "stockSteamPerStroke", "superheated", "superheaterKind", "maxSuperheatC",
            "halfSuperheatShare", "exemptFromSuperheater", "frontEndThrottle",
            "steamCircuitVolumeL", "driftingFittings",
        };

        private static readonly HashSet<string> DriftingKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "snifting", "driftingValve", "byPass",
        };

        private static readonly HashSet<string> InjectorsKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "type", "maxFlowKgPerS", "oneLeverForTwo", "crackOpening",
            "steamValves", "ownLeverIsSteam", "waterRegulators", "twoValveGraph", "tenderLiveries",
        };

        private static readonly HashSet<string> WaterRegulatorsKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "mesh", "rightTurnDeg", "leftTurnDeg", "inPlace",
        };

        private static readonly HashSet<string> InPlaceKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "left", "right", "reachM", "turnDeg",
        };

        private static readonly HashSet<string> TwoValveGraphKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "add", "addA", "addB", "multiplier", "multiplierOut", "factor", "factorPort", "right", "left",
        };

        private static readonly HashSet<string> StokerKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "fitted", "retrofit", "anchorPort", "backLanes", "lumpsLeft", "supplyPipeAtM",
        };

        private static readonly HashSet<string> FeedPumpKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "fitted", "type", "name", "capacityKgPerS", "capacityGph", "onInjectorLever",
        };

        private static readonly HashSet<string> BoosterKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "fitted", "wheelDiameterM", "trailerLoadKg", "drivingAxles",
        };

        private static readonly HashSet<string> AutoFireDoorKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "fitted",
        };

        private static readonly HashSet<string> CabKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "firehole", "injectorSteam", "dropGrate", "ashpan", "stokerManifold",
            "stokerLever", "feedPump", "booster", "jets", "driftingValve",
            "shedBlower", "shedWater",
        };

        private static readonly HashSet<string> FireholeKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "middleM", "footM", "widthM", "heightM", "planeM",
        };

        private static readonly HashSet<string> InjectorSteamSeatKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "position", "rotation", "fixedOffset", "sideOffset",
        };

        private static readonly HashSet<string> TransformSeatKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "position",
        };

        private static readonly HashSet<string> AshpanSeatKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "position", "noHanger",
        };

        private static readonly HashSet<string> StokerManifoldSeatKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "offsetCm", "turnDeg", "tiltDeg",
        };

        private static readonly HashSet<string> FeedPumpSeatKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "valveInCar", "valveOffset", "gaugeOffset", "wallGauge",
        };

        private static readonly HashSet<string> BoosterSeatKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "position", "reverserOffsetCm",
        };

        private static readonly HashSet<string> CabValveSeatKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "position", "rotation", "inCar", "plateOffset", "plateSpinDeg",
            "plateTurnDeg", "plateSquare", "ownHandle",
        };

        private static readonly HashSet<string> ShedSeatKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "enabled", "position", "swingDeg", "downDeg", "spinDeg", "outM", "landmark",
        };

        public static MakerFitParseResult Parse(string json)
        {
            var errors = new List<string>();
            JsonValue root;
            try
            {
                root = MiniJsonParser.Parse(json);
            }
            catch (JsonParseException e)
            {
                errors.Add(e.Message);
                return new MakerFitParseResult(null, errors);
            }

            if (root.Kind != JsonKind.Object)
            {
                errors.Add("Root value must be a JSON object.");
                return new MakerFitParseResult(null, errors);
            }

            CheckUnknownKeys(root, "$", RootKeys, errors);

            var profile = new MakerFitProfile();
            var version = ReadRequiredInt(root, "schemaVersion", "$", 1, 1000, errors);
            if (version.HasValue)
            {
                if (version.Value != MakerFitProfile.CurrentSchemaVersion)
                    errors.Add($"$.schemaVersion: unsupported schemaVersion {version.Value} (this version of Realistic Firing supports schemaVersion {MakerFitProfile.CurrentSchemaVersion}).");
                profile.SchemaVersion = version.Value;
            }

            profile.Liveries = ReadRequiredStringArray(root, "liveries", "$", errors);

            var fuel = ReadOptionalString(root, "fuel", "$", errors);
            if (fuel != null)
            {
                if (fuel.Equals("coal", StringComparison.OrdinalIgnoreCase))
                    profile.Fuel = MakerFitFuel.Coal;
                else if (fuel.Equals("oil", StringComparison.OrdinalIgnoreCase))
                    profile.Fuel = MakerFitFuel.Oil;
                else
                    errors.Add($"$.fuel: expected \"coal\" or \"oil\", got \"{fuel}\".");
            }

            profile.Summary = ReadOptionalString(root, "summary", "$", errors, allowEmpty: false);

            if (TryGetObject(root, "firebox", "$", errors, out var fireboxObj))
                profile.Firebox = ParseFirebox(fireboxObj, errors);
            if (TryGetObject(root, "boiler", "$", errors, out var boilerObj))
                profile.Boiler = ParseBoiler(boilerObj, errors);
            if (TryGetObject(root, "draft", "$", errors, out var draftObj))
                profile.Draft = ParseDraft(draftObj, errors);
            if (TryGetObject(root, "cylinders", "$", errors, out var cylObj))
                profile.Cylinders = ParseCylinders(cylObj, errors);
            if (TryGetObject(root, "injectors", "$", errors, out var injObj))
                profile.Injectors = ParseInjectors(injObj, errors);
            if (TryGetObject(root, "stoker", "$", errors, out var stokerObj))
                profile.Stoker = ParseStoker(stokerObj, errors);
            if (TryGetObject(root, "feedPump", "$", errors, out var pumpObj))
                profile.FeedPump = ParseFeedPump(pumpObj, errors);
            if (TryGetObject(root, "booster", "$", errors, out var boosterObj))
                profile.Booster = ParseBooster(boosterObj, errors);
            if (root.ObjectProperties.TryGetValue("autoFireDoor", out var doorNode) && doorNode.Kind != JsonKind.Null)
                profile.AutoFireDoor = ParseAutoFireDoor(doorNode, errors);
            if (TryGetObject(root, "cab", "$", errors, out var cabObj))
                profile.Cab = ParseCab(cabObj, errors);
            if (TryGetObject(root, "stack", "$", errors, out var stackObj))
                profile.Stack = ParseStack(stackObj, errors);

            if (profile.Fuel == MakerFitFuel.Oil && profile.Stoker != null && profile.Stoker.Retrofit)
                errors.Add("$.stoker.retrofit: cannot retrofit a coal stoker on an oil-fired locomotive.");

            return new MakerFitParseResult(errors.Count == 0 ? profile : null, errors);
        }

        private static MakerFitStack ParseStack(JsonValue obj, List<string> errors)
        {
            const string path = "$.stack";
            CheckUnknownKeys(obj, path, StackKeys, errors);
            var lip = ReadOptionalFloat(obj, "lipHeightM", path, 0f, RealisticStackEffects.Core.StackGeometryHint.MaxLipHeight, errors);
            var bore = ReadOptionalFloat(obj, "boreRadiusM", path, RealisticStackEffects.Core.StackGeometryHint.MinBoreRadius, RealisticStackEffects.Core.StackGeometryHint.MaxOutsideRadius, errors);
            var outside = ReadOptionalFloat(obj, "outsideRadiusM", path, RealisticStackEffects.Core.StackGeometryHint.MinOutsideRadius, RealisticStackEffects.Core.StackGeometryHint.MaxOutsideRadius, errors);
            if (bore.HasValue && outside.HasValue && bore.Value > outside.Value)
                errors.Add($"{path}: \"boreRadiusM\" ({bore.Value}) cannot exceed \"outsideRadiusM\" ({outside.Value}).");
            var stack = new MakerFitStack
            {
                LipHeightM = lip,
                BoreRadiusM = bore,
                OutsideRadiusM = outside,
            };
            return stack.HasAny ? stack : null;
        }

        private static MakerFitFirebox ParseFirebox(JsonValue obj, List<string> errors)
        {
            const string path = "$.firebox";
            CheckUnknownKeys(obj, path, FireboxKeys, errors);
            var ashFraction = ReadOptionalFloat(obj, "ashFraction", path, 0.1f, 5f, errors);
            var ashFactor = ReadOptionalFloat(obj, "ashFactor", path, 0.1f, 5f, errors);
            if (ashFraction.HasValue && ashFactor.HasValue)
                errors.Add($"{path}: specify either \"ashFraction\" or \"ashFactor\", not both.");

            var fb = new MakerFitFirebox
            {
                GrateAreaM2 = ReadOptionalFloat(obj, "grateAreaM2", path, 0.05f, 25f, errors),
                GrateSlopeDeg = ReadOptionalFloat(obj, "grateSlopeDeg", path, -30f, 30f, errors),
                SlopeOverPile = ReadOptionalBool(obj, "slopeOverPile", path, errors) ?? false,
                BackRowDraft = ReadOptionalFloat(obj, "backRowDraft", path, 0.2f, 3f, errors),
                FrontRowDraft = ReadOptionalFloat(obj, "frontRowDraft", path, 0.2f, 3f, errors),
                SideDraft = ReadOptionalFloat(obj, "sideDraft", path, 0.2f, 3f, errors),
                OverFireMixing = ReadOptionalFloat(obj, "overFireMixing", path, 0.1f, 3f, errors),
                AshFraction = ashFraction ?? ashFactor,
                ClinkerFactor = ReadOptionalFloat(obj, "clinkerFactor", path, 0f, 5f, errors),
                ReversedDoor = ReadOptionalBool(obj, "reversedDoor", path, errors) ?? false,
            };

            if (TryGetObject(obj, "overFireJets", path, errors, out var jetsObj))
            {
                const string jetsPath = "$.firebox.overFireJets";
                CheckUnknownKeys(jetsObj, jetsPath, JetsKeys, errors);
                var count = ReadRequiredInt(jetsObj, "count", jetsPath, 1, 24, errors);
                var tubeStr = ReadOptionalString(jetsObj, "tube", jetsPath, errors);
                var tube = JetTube.Large;
                if (tubeStr != null)
                {
                    if (tubeStr.Equals("small", StringComparison.OrdinalIgnoreCase))
                        tube = JetTube.Small;
                    else if (tubeStr.Equals("large", StringComparison.OrdinalIgnoreCase))
                        tube = JetTube.Large;
                    else
                        errors.Add($"{jetsPath}.tube: expected \"small\" or \"large\", got \"{tubeStr}\".");
                }
                if (count.HasValue)
                    fb.OverFireJets = new MakerFitOverFireJets { Count = count.Value, Tube = tube };
            }

            return fb;
        }

        private const float MinEvaporationKgPerS = 0.05f;
        private const float MaxEvaporationKgPerS = 40f;
        private const float MinSafetyLiftBarAbs = 3f;
        private const float MaxSafetyLiftBarAbs = 40f;
        private const float MinFeedPumpCapacityKgPerS = 0.1f;
        private const float MaxFeedPumpCapacityKgPerS = 40f;

        private static MakerFitBoiler ParseBoiler(JsonValue obj, List<string> errors)
        {
            const string path = "$.boiler";
            CheckUnknownKeys(obj, path, BoilerKeys, errors);

            var hasEvapKg = HasNonNullProperty(obj, "maxEvaporationKgPerS");
            var hasEvapLb = HasNonNullProperty(obj, "maxEvaporationLbPerHr");
            var evapKg = ReadOptionalFloat(obj, "maxEvaporationKgPerS", path, MinEvaporationKgPerS, MaxEvaporationKgPerS, errors);
            var evapLbRaw = ReadOptionalFiniteFloat(obj, "maxEvaporationLbPerHr", path, errors);
            var evapFromLb = ValidateConvertedRange(
                evapLbRaw.HasValue ? evapLbRaw.Value * MakerFitProfile.LbPerHourToKgPerS : (float?)null,
                $"{path}.maxEvaporationLbPerHr", MinEvaporationKgPerS, MaxEvaporationKgPerS, "kg/s", errors);
            if (hasEvapKg && hasEvapLb)
                errors.Add($"{path}: specify either \"maxEvaporationKgPerS\" or \"maxEvaporationLbPerHr\", not both.");

            var boiler = new MakerFitBoiler
            {
                MaxEvaporationKgPerS = evapKg ?? evapFromLb,
            };

            if (TryGetObject(obj, "safetyValves", path, errors, out var svObj))
            {
                const string svPath = "$.boiler.safetyValves";
                CheckUnknownKeys(svObj, svPath, SafetyValvesKeys, errors);
                var hasLiftBar = HasNonNullProperty(svObj, "liftBarAbs");
                var hasLiftPsi = HasNonNullProperty(svObj, "liftPsiGauge");
                var liftBar = ReadOptionalFloat(svObj, "liftBarAbs", svPath, MinSafetyLiftBarAbs, MaxSafetyLiftBarAbs, errors);
                var liftPsiRaw = ReadOptionalFiniteFloat(svObj, "liftPsiGauge", svPath, errors);
                var liftFromPsi = ValidateConvertedRange(
                    liftPsiRaw.HasValue ? liftPsiRaw.Value / MakerFitProfile.PsiPerBar + MakerFitProfile.AtmosphericBar : (float?)null,
                    $"{svPath}.liftPsiGauge", MinSafetyLiftBarAbs, MaxSafetyLiftBarAbs, "bar abs", errors);
                if (hasLiftBar && hasLiftPsi)
                    errors.Add($"{svPath}: specify either \"liftBarAbs\" or \"liftPsiGauge\", not both.");
                if (!hasLiftBar && !hasLiftPsi)
                    errors.Add($"{svPath}: missing required \"liftBarAbs\" or \"liftPsiGauge\".");
                var cap = ReadRequiredFloat(svObj, "capacityKgPerS", svPath, 0.1f, 50f, errors);
                var lift = liftBar ?? liftFromPsi;
                if (lift.HasValue && cap.HasValue)
                    boiler.SafetyValves = new MakerFitSafetyValves { LiftBarAbs = lift.Value, CapacityKgPerS = cap.Value };
            }

            var guardStr = ReadOptionalString(obj, "lowWaterGuard", path, errors);
            var plug = ReadOptionalBool(obj, "fusiblePlug", path, errors);
            var alarm = ReadOptionalBool(obj, "lowWaterAlarm", path, errors);
            if (guardStr != null && (plug.HasValue || alarm.HasValue))
                errors.Add($"{path}: do not combine \"lowWaterGuard\" with \"fusiblePlug\" or \"lowWaterAlarm\".");
            if (plug == true && alarm == true)
                errors.Add($"{path}: \"fusiblePlug\" and \"lowWaterAlarm\" cannot both be true.");

            if (guardStr != null)
            {
                if (guardStr.Equals("none", StringComparison.OrdinalIgnoreCase))
                    boiler.LowWaterGuard = MakerFitLowWaterGuard.None;
                else if (guardStr.Equals("fusiblePlug", StringComparison.OrdinalIgnoreCase) || guardStr.Equals("fusible_plug", StringComparison.OrdinalIgnoreCase))
                    boiler.LowWaterGuard = MakerFitLowWaterGuard.FusiblePlug;
                else if (guardStr.Equals("lowWaterAlarm", StringComparison.OrdinalIgnoreCase) || guardStr.Equals("low_water_alarm", StringComparison.OrdinalIgnoreCase))
                    boiler.LowWaterGuard = MakerFitLowWaterGuard.LowWaterAlarm;
                else
                    errors.Add($"{path}.lowWaterGuard: expected \"none\", \"fusiblePlug\", or \"lowWaterAlarm\", got \"{guardStr}\".");
            }
            else if (plug.HasValue || alarm.HasValue)
            {
                boiler.LowWaterGuard = plug == true ? MakerFitLowWaterGuard.FusiblePlug
                    : alarm == true ? MakerFitLowWaterGuard.LowWaterAlarm
                    : MakerFitLowWaterGuard.None;
            }

            return boiler;
        }

        private static MakerFitDraft ParseDraft(JsonValue obj, List<string> errors)
        {
            const string path = "$.draft";
            CheckUnknownKeys(obj, path, DraftKeys, errors);

            var blower = ReadOptionalFloat(obj, "blowerMaxSteamKgPerS", path, 0.005f, 10f, errors);
            var natural = ReadOptionalFloat(obj, "naturalDraftKgPerS", path, 0.005f, 10f, errors);
            if (blower.HasValue != natural.HasValue)
                errors.Add($"{path}: \"blowerMaxSteamKgPerS\" and \"naturalDraftKgPerS\" must be provided together.");

            MakerFitAirPumps pumps = null;
            if (TryGetObject(obj, "airPumps", path, errors, out var pumpObj))
            {
                const string pumpPath = "$.draft.airPumps";
                CheckUnknownKeys(pumpObj, pumpPath, AirPumpsKeys, errors);
                var typeStr = ReadOptionalString(pumpObj, "type", pumpPath, errors);
                var type = MakerFitAirPumpType.NineAndHalf;
                if (typeStr != null)
                {
                    if (typeStr.Equals("nineAndHalf", StringComparison.OrdinalIgnoreCase) || typeStr.Equals("9.5in", StringComparison.OrdinalIgnoreCase))
                        type = MakerFitAirPumpType.NineAndHalf;
                    else if (typeStr.Equals("crossCompound", StringComparison.OrdinalIgnoreCase) || typeStr.Equals("8.5in", StringComparison.OrdinalIgnoreCase))
                        type = MakerFitAirPumpType.CrossCompound;
                    else
                        errors.Add($"{pumpPath}.type: expected \"nineAndHalf\" or \"crossCompound\", got \"{typeStr}\".");
                }
                var count = ReadOptionalInt(pumpObj, "count", pumpPath, 1, 4, errors) ?? 1;
                var res = ReadOptionalFloat(pumpObj, "mainReservoirL", pumpPath, 0f, 20000f, errors) ?? 0f;
                pumps = new MakerFitAirPumps { Type = type, Count = count, MainReservoirL = res };
            }

            if (pumps != null && !blower.HasValue)
                errors.Add($"{path}: \"airPumps\" requires \"blowerMaxSteamKgPerS\" and \"naturalDraftKgPerS\".");

            return new MakerFitDraft
            {
                BlowerMaxSteamKgPerS = blower,
                NaturalDraftKgPerS = natural,
                AirPumps = pumps,
            };
        }

        private static MakerFitCylinders ParseCylinders(JsonValue obj, List<string> errors)
        {
            const string path = "$.cylinders";
            CheckUnknownKeys(obj, path, CylindersKeys, errors);

            var cyl = new MakerFitCylinders
            {
                StockSteamPerStroke = ReadOptionalBool(obj, "stockSteamPerStroke", path, errors),
                Superheated = ReadOptionalBool(obj, "superheated", path, errors),
                SuperheaterKind = ReadOptionalString(obj, "superheaterKind", path, errors, allowEmpty: false),
                MaxSuperheatC = ReadOptionalFloat(obj, "maxSuperheatC", path, 10f, 350f, errors),
                HalfSuperheatShare = ReadOptionalFloat(obj, "halfSuperheatShare", path, 0.3f, 0.98f, errors),
                ExemptFromSuperheater = ReadOptionalBool(obj, "exemptFromSuperheater", path, errors),
                FrontEndThrottle = ReadOptionalBool(obj, "frontEndThrottle", path, errors),
                SteamCircuitVolumeL = ReadOptionalFloat(obj, "steamCircuitVolumeL", path, 10f, 5000f, errors),
            };

            if (cyl.Superheated == false)
            {
                if (cyl.MaxSuperheatC.HasValue || cyl.HalfSuperheatShare.HasValue || cyl.SuperheaterKind != null)
                    errors.Add($"{path}: superheater parameters cannot be set when \"superheated\" is false.");
                if (cyl.FrontEndThrottle.HasValue || cyl.SteamCircuitVolumeL.HasValue)
                    errors.Add($"{path}: \"frontEndThrottle\" and \"steamCircuitVolumeL\" apply only to superheated engines.");
            }
            if (cyl.ExemptFromSuperheater == true && cyl.Superheated == true)
                errors.Add($"{path}: \"exemptFromSuperheater\" and \"superheated\" cannot both be true.");

            if (TryGetObject(obj, "driftingFittings", path, errors, out var driftObj))
            {
                const string driftPath = "$.cylinders.driftingFittings";
                CheckUnknownKeys(driftObj, driftPath, DriftingKeys, errors);
                cyl.DriftingFittings = new MakerFitDriftingFittings
                {
                    Snifting = ReadOptionalBool(driftObj, "snifting", driftPath, errors) ?? false,
                    DriftingValve = ReadOptionalBool(driftObj, "driftingValve", driftPath, errors) ?? false,
                    ByPass = ReadOptionalBool(driftObj, "byPass", driftPath, errors) ?? false,
                };
            }

            return cyl;
        }

        private static MakerFitInjectors ParseInjectors(JsonValue obj, List<string> errors)
        {
            const string path = "$.injectors";
            CheckUnknownKeys(obj, path, InjectorsKeys, errors);

            var inj = new MakerFitInjectors
            {
                MaxFlowKgPerS = ReadOptionalFloat(obj, "maxFlowKgPerS", path, 0.05f, 50f, errors),
                OneLeverForTwo = ReadOptionalBool(obj, "oneLeverForTwo", path, errors),
                CrackOpening = ReadOptionalFloat(obj, "crackOpening", path, 0.05f, 0.5f, errors),
                SteamValves = ReadOptionalBool(obj, "steamValves", path, errors),
                OwnLeverIsSteam = ReadOptionalBool(obj, "ownLeverIsSteam", path, errors),
                TenderLiveries = ReadOptionalStringArray(obj, "tenderLiveries", path, errors),
            };

            var typeStr = ReadOptionalString(obj, "type", path, errors);
            if (typeStr != null)
            {
                if (typeStr.Equals("lifting", StringComparison.OrdinalIgnoreCase))
                    inj.Type = MakerFitInjectorType.Lifting;
                else if (typeStr.Equals("nonLifting", StringComparison.OrdinalIgnoreCase) || typeStr.Equals("non-lifting", StringComparison.OrdinalIgnoreCase))
                    inj.Type = MakerFitInjectorType.NonLifting;
                else
                    errors.Add($"{path}.type: expected \"lifting\" or \"nonLifting\", got \"{typeStr}\".");
            }

            if (TryGetObject(obj, "waterRegulators", path, errors, out var regObj))
            {
                const string regPath = "$.injectors.waterRegulators";
                CheckUnknownKeys(regObj, regPath, WaterRegulatorsKeys, errors);
                var mesh = ReadOptionalString(regObj, "mesh", regPath, errors, allowEmpty: false);
                var rightTurn = ReadOptionalFloat(regObj, "rightTurnDeg", regPath, 10f, 720f, errors);
                var leftTurn = ReadOptionalFloat(regObj, "leftTurnDeg", regPath, 10f, 720f, errors);
                MakerFitInPlaceRegulators inPlace = null;
                if (TryGetObject(regObj, "inPlace", regPath, errors, out var inPlaceObj))
                {
                    const string ipPath = "$.injectors.waterRegulators.inPlace";
                    CheckUnknownKeys(inPlaceObj, ipPath, InPlaceKeys, errors);
                    var left = ReadRequiredVec3(inPlaceObj, "left", ipPath, -50f, 50f, errors);
                    var right = ReadRequiredVec3(inPlaceObj, "right", ipPath, -50f, 50f, errors);
                    var reach = ReadRequiredFloat(inPlaceObj, "reachM", ipPath, 0.02f, 1f, errors);
                    var turn = ReadRequiredFloat(inPlaceObj, "turnDeg", ipPath, 10f, 360f, errors);
                    if (left.HasValue && right.HasValue && reach.HasValue && turn.HasValue)
                        inPlace = new MakerFitInPlaceRegulators { Left = left.Value, Right = right.Value, ReachM = reach.Value, TurnDeg = turn.Value };
                }

                var hasMesh = mesh != null || rightTurn.HasValue || leftTurn.HasValue;
                if (hasMesh && inPlace != null)
                    errors.Add($"{regPath}: specify either mesh cut-out (\"mesh\"/\"rightTurnDeg\"/\"leftTurnDeg\") or \"inPlace\", not both.");
                else if (hasMesh && (mesh == null || !rightTurn.HasValue || !leftTurn.HasValue))
                    errors.Add($"{regPath}: mesh water regulators require \"mesh\", \"rightTurnDeg\", and \"leftTurnDeg\".");
                else if (!hasMesh && inPlace == null)
                    errors.Add($"{regPath}: must specify either mesh cut-out or \"inPlace\".");

                inj.WaterRegulators = new MakerFitWaterRegulators
                {
                    Mesh = mesh,
                    RightTurnDeg = rightTurn,
                    LeftTurnDeg = leftTurn,
                    InPlace = inPlace,
                };
            }

            if (TryGetObject(obj, "twoValveGraph", path, errors, out var graphObj))
            {
                const string gPath = "$.injectors.twoValveGraph";
                CheckUnknownKeys(graphObj, gPath, TwoValveGraphKeys, errors);
                inj.TwoValveGraph = new MakerFitTwoValveGraph
                {
                    Add = ReadRequiredString(graphObj, "add", gPath, errors),
                    AddA = ReadRequiredString(graphObj, "addA", gPath, errors),
                    AddB = ReadRequiredString(graphObj, "addB", gPath, errors),
                    Multiplier = ReadRequiredString(graphObj, "multiplier", gPath, errors),
                    MultiplierOut = ReadRequiredString(graphObj, "multiplierOut", gPath, errors),
                    Factor = ReadRequiredString(graphObj, "factor", gPath, errors),
                    FactorPort = ReadRequiredString(graphObj, "factorPort", gPath, errors),
                    Right = ReadRequiredString(graphObj, "right", gPath, errors),
                    Left = ReadRequiredString(graphObj, "left", gPath, errors),
                };
            }

            return inj;
        }

        private static MakerFitStoker ParseStoker(JsonValue obj, List<string> errors)
        {
            const string path = "$.stoker";
            CheckUnknownKeys(obj, path, StokerKeys, errors);
            return new MakerFitStoker
            {
                Fitted = ReadOptionalBool(obj, "fitted", path, errors) ?? true,
                Retrofit = ReadOptionalBool(obj, "retrofit", path, errors) ?? false,
                AnchorPort = ReadOptionalString(obj, "anchorPort", path, errors, allowEmpty: false),
                BackLanes = ReadOptionalFloat(obj, "backLanes", path, 0.1f, 2f, errors),
                LumpsLeft = ReadOptionalFloat(obj, "lumpsLeft", path, -0.5f, 0.5f, errors),
                SupplyPipeAtM = ReadOptionalFloat(obj, "supplyPipeAtM", path, -1f, 1f, errors),
            };
        }

        private static MakerFitFeedPump ParseFeedPump(JsonValue obj, List<string> errors)
        {
            const string path = "$.feedPump";
            CheckUnknownKeys(obj, path, FeedPumpKeys, errors);
            var fitted = ReadOptionalBool(obj, "fitted", path, errors) ?? true;
            var typeStr = ReadOptionalString(obj, "type", path, errors);
            var type = FeedHeaterType.Open;
            if (typeStr != null)
            {
                if (typeStr.Equals("open", StringComparison.OrdinalIgnoreCase))
                    type = FeedHeaterType.Open;
                else if (typeStr.Equals("closed", StringComparison.OrdinalIgnoreCase))
                    type = FeedHeaterType.Closed;
                else
                    errors.Add($"{path}.type: expected \"open\" or \"closed\", got \"{typeStr}\".");
            }
            else if (fitted)
            {
                errors.Add($"{path}.type: required when \"fitted\" is true (\"open\" or \"closed\").");
            }

            var hasCapKg = HasNonNullProperty(obj, "capacityKgPerS");
            var hasCapGph = HasNonNullProperty(obj, "capacityGph");
            var capKg = ReadOptionalFloat(obj, "capacityKgPerS", path, MinFeedPumpCapacityKgPerS, MaxFeedPumpCapacityKgPerS, errors);
            var capGphRaw = ReadOptionalFiniteFloat(obj, "capacityGph", path, errors);
            var capFromGph = ValidateConvertedRange(
                capGphRaw.HasValue ? capGphRaw.Value * MakerFitProfile.UsGallonsPerHourToKgPerS : (float?)null,
                $"{path}.capacityGph", MinFeedPumpCapacityKgPerS, MaxFeedPumpCapacityKgPerS, "kg/s", errors);
            if (hasCapKg && hasCapGph)
                errors.Add($"{path}: specify either \"capacityKgPerS\" or \"capacityGph\", not both.");
            var capacity = capKg ?? capFromGph ?? 0f;
            if (fitted && !hasCapKg && !hasCapGph)
                errors.Add($"{path}: required \"capacityKgPerS\" or \"capacityGph\" when \"fitted\" is true.");

            return new MakerFitFeedPump
            {
                Fitted = fitted,
                Type = type,
                Name = ReadOptionalString(obj, "name", path, errors, allowEmpty: false) ?? "Feedwater heater and pump",
                CapacityKgPerS = capacity,
                OnInjectorLever = ReadOptionalBool(obj, "onInjectorLever", path, errors) ?? false,
            };
        }

        private static MakerFitBooster ParseBooster(JsonValue obj, List<string> errors)
        {
            const string path = "$.booster";
            CheckUnknownKeys(obj, path, BoosterKeys, errors);
            var fitted = ReadOptionalBool(obj, "fitted", path, errors) ?? true;
            var wheel = ReadOptionalFloat(obj, "wheelDiameterM", path, 0.4f, 2.5f, errors);
            var load = ReadOptionalFloat(obj, "trailerLoadKg", path, 1000f, 100000f, errors);
            var axles = ReadOptionalInt(obj, "drivingAxles", path, 1, 10, errors);
            if (fitted)
            {
                if (!wheel.HasValue) errors.Add($"{path}.wheelDiameterM: required when \"fitted\" is true.");
                if (!load.HasValue) errors.Add($"{path}.trailerLoadKg: required when \"fitted\" is true.");
                if (!axles.HasValue) errors.Add($"{path}.drivingAxles: required when \"fitted\" is true.");
            }
            return new MakerFitBooster
            {
                Fitted = fitted,
                WheelDiameterM = wheel ?? 0f,
                TrailerLoadKg = load ?? 0f,
                DrivingAxles = axles ?? 0,
            };
        }

        private static MakerFitAutoFireDoor ParseAutoFireDoor(JsonValue node, List<string> errors)
        {
            const string path = "$.autoFireDoor";
            if (node.Kind == JsonKind.Bool)
                return new MakerFitAutoFireDoor { Fitted = node.BoolValue };
            if (node.Kind != JsonKind.Object)
            {
                errors.Add($"{path}: expected boolean or object.");
                return null;
            }
            CheckUnknownKeys(node, path, AutoFireDoorKeys, errors);
            return new MakerFitAutoFireDoor
            {
                Fitted = ReadOptionalBool(node, "fitted", path, errors) ?? true,
            };
        }

        private static MakerFitCab ParseCab(JsonValue obj, List<string> errors)
        {
            const string path = "$.cab";
            CheckUnknownKeys(obj, path, CabKeys, errors);
            var cab = new MakerFitCab();

            if (TryGetObject(obj, "firehole", path, errors, out var holeObj))
            {
                const string p = "$.cab.firehole";
                CheckUnknownKeys(holeObj, p, FireholeKeys, errors);
                var middle = ReadRequiredFloat(holeObj, "middleM", p, -10f, 10f, errors);
                var foot = ReadRequiredFloat(holeObj, "footM", p, -5f, 10f, errors);
                var width = ReadRequiredFloat(holeObj, "widthM", p, 0.05f, 3f, errors);
                var height = ReadRequiredFloat(holeObj, "heightM", p, 0.05f, 3f, errors);
                var plane = ReadRequiredFloat(holeObj, "planeM", p, -50f, 50f, errors);
                if (middle.HasValue && foot.HasValue && width.HasValue && height.HasValue && plane.HasValue)
                    cab.Firehole = new MakerFitFirehole { MiddleM = middle.Value, FootM = foot.Value, WidthM = width.Value, HeightM = height.Value, PlaneM = plane.Value };
            }

            if (TryGetObject(obj, "injectorSteam", path, errors, out var injSteamObj))
            {
                const string p = "$.cab.injectorSteam";
                CheckUnknownKeys(injSteamObj, p, InjectorSteamSeatKeys, errors);
                var pos = ReadRequiredVec3(injSteamObj, "position", p, -50f, 50f, errors);
                var rot = ReadOptionalVec3(injSteamObj, "rotation", p, -720f, 720f, errors) ?? new MakerFitVec3(-180f, 0f, 2f);
                var fixedOff = ReadOptionalVec3(injSteamObj, "fixedOffset", p, -10f, 10f, errors);
                var sideOff = ReadOptionalVec3(injSteamObj, "sideOffset", p, -10f, 10f, errors);
                if (fixedOff.HasValue && sideOff.HasValue)
                    errors.Add($"{p}: specify either \"fixedOffset\" or \"sideOffset\", not both.");
                if (pos.HasValue)
                    cab.InjectorSteam = new MakerFitInjectorSteamSeat
                    {
                        Position = pos.Value,
                        Rotation = rot,
                        FixedOffset = fixedOff,
                        SideOffset = sideOff,
                    };
            }

            if (TryGetObject(obj, "dropGrate", path, errors, out var dropObj))
                cab.DropGrate = ParseTransformSeat(dropObj, "$.cab.dropGrate", errors);

            if (TryGetObject(obj, "ashpan", path, errors, out var ashObj))
            {
                const string p = "$.cab.ashpan";
                CheckUnknownKeys(ashObj, p, AshpanSeatKeys, errors);
                var pos = ReadRequiredVec3(ashObj, "position", p, -50f, 50f, errors);
                if (pos.HasValue)
                    cab.Ashpan = new MakerFitAshpanSeat
                    {
                        Position = pos.Value,
                        NoHanger = ReadOptionalBool(ashObj, "noHanger", p, errors) ?? false,
                    };
            }

            if (TryGetObject(obj, "stokerManifold", path, errors, out var manObj))
            {
                const string p = "$.cab.stokerManifold";
                CheckUnknownKeys(manObj, p, StokerManifoldSeatKeys, errors);
                var off = ReadRequiredVec3(manObj, "offsetCm", p, -500f, 500f, errors);
                var turn = ReadOptionalFloat(manObj, "turnDeg", p, -360f, 360f, errors) ?? 0f;
                var tilt = ReadOptionalFloat(manObj, "tiltDeg", p, -180f, 180f, errors) ?? 0f;
                if (off.HasValue)
                    cab.StokerManifold = new MakerFitStokerManifoldSeat
                    {
                        OffsetCm = off.Value,
                        TurnDeg = turn,
                        TiltDeg = tilt,
                    };
            }

            if (TryGetObject(obj, "stokerLever", path, errors, out var leverObj))
                cab.StokerLever = ParseTransformSeat(leverObj, "$.cab.stokerLever", errors);

            if (TryGetObject(obj, "feedPump", path, errors, out var pumpSeatObj))
            {
                const string p = "$.cab.feedPump";
                CheckUnknownKeys(pumpSeatObj, p, FeedPumpSeatKeys, errors);
                var inCar = ReadOptionalVec3(pumpSeatObj, "valveInCar", p, -50f, 50f, errors);
                var offset = ReadOptionalVec2(pumpSeatObj, "valveOffset", p, -10f, 10f, errors);
                var gauge = ReadOptionalVec2(pumpSeatObj, "gaugeOffset", p, -10f, 10f, errors);
                var wallGauge = ReadOptionalVec3(pumpSeatObj, "wallGauge", p, -10f, 10f, errors);
                if (inCar.HasValue && offset.HasValue)
                    errors.Add($"{p}: specify either \"valveInCar\" or \"valveOffset\", not both.");
                if (!inCar.HasValue && !offset.HasValue && !gauge.HasValue && !wallGauge.HasValue)
                    errors.Add($"{p}: must specify at least one of \"valveInCar\", \"valveOffset\", \"gaugeOffset\", or \"wallGauge\".");
                cab.FeedPump = new MakerFitFeedPumpSeat
                {
                    ValveInCar = inCar,
                    ValveOffset = offset,
                    GaugeOffset = gauge,
                    WallGauge = wallGauge,
                };
            }

            if (TryGetObject(obj, "booster", path, errors, out var boosterSeatObj))
            {
                const string p = "$.cab.booster";
                CheckUnknownKeys(boosterSeatObj, p, BoosterSeatKeys, errors);
                var pos = ReadOptionalVec3(boosterSeatObj, "position", p, -50f, 50f, errors);
                var revOff = ReadOptionalVec3(boosterSeatObj, "reverserOffsetCm", p, -500f, 500f, errors);
                if (pos.HasValue && revOff.HasValue)
                    errors.Add($"{p}: specify either \"position\" (car frame, m) or \"reverserOffsetCm\", not both.");
                if (!pos.HasValue && !revOff.HasValue)
                    errors.Add($"{p}: requires either \"position\" or \"reverserOffsetCm\".");
                cab.Booster = new MakerFitBoosterSeat
                {
                    Position = pos,
                    ReverserOffsetCm = revOff,
                };
            }

            if (TryGetObject(obj, "jets", path, errors, out var jetsSeatObj))
                cab.Jets = ParseCabValveSeat(jetsSeatObj, "$.cab.jets", errors);

            if (TryGetObject(obj, "driftingValve", path, errors, out var driftSeatObj))
                cab.DriftingValve = ParseCabValveSeat(driftSeatObj, "$.cab.driftingValve", errors);

            if (TryGetObject(obj, "shedBlower", path, errors, out var blowerSeatObj))
                cab.ShedBlower = ParseShedSeat(blowerSeatObj, "$.cab.shedBlower", errors);

            if (TryGetObject(obj, "shedWater", path, errors, out var waterSeatObj))
                cab.ShedWater = ParseShedSeat(waterSeatObj, "$.cab.shedWater", errors);

            return cab;
        }

        private static MakerFitTransformSeat ParseTransformSeat(JsonValue obj, string path, List<string> errors)
        {
            CheckUnknownKeys(obj, path, TransformSeatKeys, errors);
            var pos = ReadRequiredVec3(obj, "position", path, -50f, 50f, errors);
            if (!pos.HasValue)
                return null;
            return new MakerFitTransformSeat
            {
                Position = pos.Value,
            };
        }

        private static MakerFitCabValveSeat ParseCabValveSeat(JsonValue obj, string path, List<string> errors)
        {
            CheckUnknownKeys(obj, path, CabValveSeatKeys, errors);
            var pos = ReadRequiredVec3(obj, "position", path, -50f, 50f, errors);
            if (!pos.HasValue)
                return null;
            return new MakerFitCabValveSeat
            {
                Position = pos.Value,
                Rotation = ReadOptionalVec3(obj, "rotation", path, -720f, 720f, errors) ?? default,
                InCar = ReadOptionalBool(obj, "inCar", path, errors) ?? true,
                PlateOffset = ReadOptionalVec3(obj, "plateOffset", path, -5f, 5f, errors) ?? default,
                PlateSpinDeg = ReadOptionalFloat(obj, "plateSpinDeg", path, -360f, 360f, errors) ?? 0f,
                PlateTurnDeg = ReadOptionalFloat(obj, "plateTurnDeg", path, -360f, 360f, errors) ?? 0f,
                PlateSquare = ReadOptionalBool(obj, "plateSquare", path, errors) ?? false,
                OwnHandle = ReadOptionalBool(obj, "ownHandle", path, errors) ?? false,
            };
        }

        private static MakerFitShedSeat ParseShedSeat(JsonValue obj, string path, List<string> errors)
        {
            CheckUnknownKeys(obj, path, ShedSeatKeys, errors);
            var enabled = ReadOptionalBool(obj, "enabled", path, errors) ?? true;
            var pos = ReadOptionalVec3(obj, "position", path, -50f, 50f, errors);
            var swing = ReadOptionalFloat(obj, "swingDeg", path, -180f, 180f, errors) ?? 0f;
            var down = ReadOptionalFloat(obj, "downDeg", path, -180f, 180f, errors) ?? 0f;
            var spin = ReadOptionalFloat(obj, "spinDeg", path, -360f, 360f, errors) ?? 0f;
            var outM = ReadOptionalFloat(obj, "outM", path, -1f, 1f, errors) ?? 0f;
            var landmark = ReadOptionalString(obj, "landmark", path, errors, allowEmpty: false);
            if (!enabled && pos.HasValue)
                errors.Add($"{path}: \"position\" cannot be set when \"enabled\" is false.");
            return new MakerFitShedSeat
            {
                Enabled = enabled,
                Position = pos,
                SwingDeg = swing,
                DownDeg = down,
                SpinDeg = spin,
                OutM = outM,
                Landmark = landmark,
            };
        }

        private static void CheckUnknownKeys(JsonValue obj, string path, HashSet<string> allowed, List<string> errors)
        {
            foreach (var key in obj.ObjectProperties.Keys)
            {
                if (!allowed.Contains(key))
                    errors.Add($"{path}.{key}: unknown property.");
            }
        }

        private static bool TryGetObject(JsonValue parent, string key, string path, List<string> errors, out JsonValue obj)
        {
            obj = null;
            if (!parent.ObjectProperties.TryGetValue(key, out var node) || node.Kind == JsonKind.Null)
                return false;
            if (node.Kind != JsonKind.Object)
            {
                errors.Add($"{path}.{key}: expected JSON object.");
                return false;
            }
            obj = node;
            return true;
        }

        private static int? ReadRequiredInt(JsonValue obj, string key, string path, int min, int max, List<string> errors)
        {
            if (!obj.ObjectProperties.TryGetValue(key, out var node))
            {
                errors.Add($"{path}.{key}: missing required integer.");
                return null;
            }
            return ParseInt(node, $"{path}.{key}", min, max, errors);
        }

        private static int? ReadOptionalInt(JsonValue obj, string key, string path, int min, int max, List<string> errors)
        {
            if (!obj.ObjectProperties.TryGetValue(key, out var node) || node.Kind == JsonKind.Null)
                return null;
            return ParseInt(node, $"{path}.{key}", min, max, errors);
        }

        private static int? ParseInt(JsonValue node, string field, int min, int max, List<string> errors)
        {
            if (node.Kind != JsonKind.Number)
            {
                errors.Add($"{field}: expected integer.");
                return null;
            }
            var rounded = Math.Round(node.NumberValue);
            if (Math.Abs(node.NumberValue - rounded) > 1e-9 || rounded < int.MinValue || rounded > int.MaxValue)
            {
                errors.Add($"{field}: expected whole integer, got {node.NumberValue.ToString(CultureInfo.InvariantCulture)}.");
                return null;
            }
            var value = (int)rounded;
            if (value < min || value > max)
            {
                errors.Add($"{field}: value {value} is out of range [{min}, {max}].");
                return null;
            }
            return value;
        }

        private static float? ReadRequiredFloat(JsonValue obj, string key, string path, float min, float max, List<string> errors)
        {
            if (!obj.ObjectProperties.TryGetValue(key, out var node))
            {
                errors.Add($"{path}.{key}: missing required number.");
                return null;
            }
            return ParseFloat(node, $"{path}.{key}", min, max, errors);
        }

        private static float? ReadOptionalFloat(JsonValue obj, string key, string path, float min, float max, List<string> errors)
        {
            if (!obj.ObjectProperties.TryGetValue(key, out var node) || node.Kind == JsonKind.Null)
                return null;
            return ParseFloat(node, $"{path}.{key}", min, max, errors);
        }

        private static bool HasNonNullProperty(JsonValue obj, string key) =>
            obj.ObjectProperties.TryGetValue(key, out var node) && node.Kind != JsonKind.Null;

        private static float? ReadOptionalFiniteFloat(JsonValue obj, string key, string path, List<string> errors)
        {
            if (!obj.ObjectProperties.TryGetValue(key, out var node) || node.Kind == JsonKind.Null)
                return null;
            var field = $"{path}.{key}";
            if (node.Kind != JsonKind.Number)
            {
                errors.Add($"{field}: expected number.");
                return null;
            }
            var value = (float)node.NumberValue;
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                errors.Add($"{field}: number must be finite.");
                return null;
            }
            return value;
        }

        private static float? ValidateConvertedRange(float? converted, string field, float min, float max, string unit, List<string> errors)
        {
            if (!converted.HasValue)
                return null;
            var value = converted.Value;
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                errors.Add($"{field}: converted value must be finite.");
                return null;
            }
            if (value < min || value > max)
            {
                errors.Add($"{field}: converted value {value.ToString("0.####", CultureInfo.InvariantCulture)} {unit} is out of range [{min.ToString("0.####", CultureInfo.InvariantCulture)}, {max.ToString("0.####", CultureInfo.InvariantCulture)}].");
                return null;
            }
            return value;
        }

        private static float? ParseFloat(JsonValue node, string field, float min, float max, List<string> errors)
        {
            if (node.Kind != JsonKind.Number)
            {
                errors.Add($"{field}: expected number.");
                return null;
            }
            var value = (float)node.NumberValue;
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                errors.Add($"{field}: number must be finite.");
                return null;
            }
            if (value < min || value > max)
            {
                errors.Add($"{field}: value {value.ToString("0.####", CultureInfo.InvariantCulture)} is out of range [{min.ToString("0.####", CultureInfo.InvariantCulture)}, {max.ToString("0.####", CultureInfo.InvariantCulture)}].");
                return null;
            }
            return value;
        }

        private static bool? ReadOptionalBool(JsonValue obj, string key, string path, List<string> errors)
        {
            if (!obj.ObjectProperties.TryGetValue(key, out var node) || node.Kind == JsonKind.Null)
                return null;
            if (node.Kind != JsonKind.Bool)
            {
                errors.Add($"{path}.{key}: expected boolean.");
                return null;
            }
            return node.BoolValue;
        }

        private static string ReadRequiredString(JsonValue obj, string key, string path, List<string> errors)
        {
            if (!obj.ObjectProperties.TryGetValue(key, out var node))
            {
                errors.Add($"{path}.{key}: missing required string.");
                return null;
            }
            return ParseString(node, $"{path}.{key}", allowEmpty: false, errors);
        }

        private static string ReadOptionalString(JsonValue obj, string key, string path, List<string> errors, bool allowEmpty = true)
        {
            if (!obj.ObjectProperties.TryGetValue(key, out var node) || node.Kind == JsonKind.Null)
                return null;
            return ParseString(node, $"{path}.{key}", allowEmpty, errors);
        }

        private static string ParseString(JsonValue node, string field, bool allowEmpty, List<string> errors)
        {
            if (node.Kind != JsonKind.String)
            {
                errors.Add($"{field}: expected string.");
                return null;
            }
            var trimmed = node.StringValue.Trim();
            if (!allowEmpty && trimmed.Length == 0)
            {
                errors.Add($"{field}: must not be empty.");
                return null;
            }
            return trimmed;
        }

        private static IReadOnlyList<string> ReadRequiredStringArray(JsonValue obj, string key, string path, List<string> errors)
        {
            if (!obj.ObjectProperties.TryGetValue(key, out var node))
            {
                errors.Add($"{path}.{key}: missing required non-empty array of livery IDs.");
                return Array.Empty<string>();
            }
            return ParseStringArray(node, $"{path}.{key}", requiredNonEmpty: true, errors);
        }

        private static IReadOnlyList<string> ReadOptionalStringArray(JsonValue obj, string key, string path, List<string> errors)
        {
            if (!obj.ObjectProperties.TryGetValue(key, out var node) || node.Kind == JsonKind.Null)
                return null;
            return ParseStringArray(node, $"{path}.{key}", requiredNonEmpty: false, errors);
        }

        private static IReadOnlyList<string> ParseStringArray(JsonValue node, string field, bool requiredNonEmpty, List<string> errors)
        {
            if (node.Kind != JsonKind.Array)
            {
                errors.Add($"{field}: expected array of strings.");
                return Array.Empty<string>();
            }
            if (requiredNonEmpty && node.ArrayItems.Count == 0)
            {
                errors.Add($"{field}: must contain at least one entry.");
                return Array.Empty<string>();
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var list = new List<string>(node.ArrayItems.Count);
            for (var i = 0; i < node.ArrayItems.Count; i++)
            {
                var item = node.ArrayItems[i];
                if (item.Kind != JsonKind.String || string.IsNullOrWhiteSpace(item.StringValue))
                {
                    errors.Add($"{field}[{i}]: expected non-empty string.");
                    continue;
                }
                var value = item.StringValue.Trim();
                if (!seen.Add(value))
                {
                    errors.Add($"{field}[{i}]: duplicate entry \"{value}\".");
                    continue;
                }
                list.Add(value);
            }
            return list;
        }

        private static MakerFitVec2? ReadOptionalVec2(JsonValue obj, string key, string path, float min, float max, List<string> errors)
        {
            if (!obj.ObjectProperties.TryGetValue(key, out var node) || node.Kind == JsonKind.Null)
                return null;
            return ParseVec2(node, $"{path}.{key}", min, max, errors);
        }

        private static MakerFitVec2? ParseVec2(JsonValue node, string field, float min, float max, List<string> errors)
        {
            if (node.Kind != JsonKind.Array || node.ArrayItems.Count != 2)
            {
                errors.Add($"{field}: expected [x, y] array of 2 numbers.");
                return null;
            }
            var x = ParseFloat(node.ArrayItems[0], $"{field}[0]", min, max, errors);
            var y = ParseFloat(node.ArrayItems[1], $"{field}[1]", min, max, errors);
            return x.HasValue && y.HasValue ? new MakerFitVec2(x.Value, y.Value) : (MakerFitVec2?)null;
        }

        private static MakerFitVec3? ReadRequiredVec3(JsonValue obj, string key, string path, float min, float max, List<string> errors)
        {
            if (!obj.ObjectProperties.TryGetValue(key, out var node))
            {
                errors.Add($"{path}.{key}: missing required [x, y, z] vector.");
                return null;
            }
            return ParseVec3(node, $"{path}.{key}", min, max, errors);
        }

        private static MakerFitVec3? ReadOptionalVec3(JsonValue obj, string key, string path, float min, float max, List<string> errors)
        {
            if (!obj.ObjectProperties.TryGetValue(key, out var node) || node.Kind == JsonKind.Null)
                return null;
            return ParseVec3(node, $"{path}.{key}", min, max, errors);
        }

        private static MakerFitVec3? ParseVec3(JsonValue node, string field, float min, float max, List<string> errors)
        {
            if (node.Kind != JsonKind.Array || node.ArrayItems.Count != 3)
            {
                errors.Add($"{field}: expected [x, y, z] array of 3 numbers.");
                return null;
            }
            var x = ParseFloat(node.ArrayItems[0], $"{field}[0]", min, max, errors);
            var y = ParseFloat(node.ArrayItems[1], $"{field}[1]", min, max, errors);
            var z = ParseFloat(node.ArrayItems[2], $"{field}[2]", min, max, errors);
            return x.HasValue && y.HasValue && z.HasValue ? new MakerFitVec3(x.Value, y.Value, z.Value) : (MakerFitVec3?)null;
        }
    }

    /// <summary>
    /// Formats a <see cref="MakerFitProfile"/> as clean, canonical, ready-to-paste
    /// realistic-firing.json text that round-trips cleanly through <see cref="MakerFitParser"/>.
    /// </summary>
    public static class MakerFitFormatter
    {
        public static string Format(MakerFitProfile profile) => FormatCore(profile, null, sortTenders: false, exactNumbers: false);

        internal static string FormatCanonicalBody(MakerFitProfile profile) =>
            FormatCore(profile, Array.Empty<string>(), sortTenders: true, exactNumbers: true);

        private static string FormatCore(MakerFitProfile profile, IReadOnlyList<string> liveriesOverride, bool sortTenders, bool exactNumbers)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            var liveries = liveriesOverride ?? profile.Liveries;
            var w = new MiniJsonWriter(exactNumbers: exactNumbers);
            w.WriteStartObject();
            w.WriteProperty("schemaVersion", profile.SchemaVersion);

            w.WritePropertyName("liveries");
            w.WriteStartArray(inline: liveries != null && liveries.Count <= 3);
            if (liveries != null)
            {
                foreach (var livery in liveries)
                    w.WriteString(livery);
            }
            w.WriteEndArray();

            if (profile.Fuel != MakerFitFuel.Coal)
                w.WriteProperty("fuel", "oil");
            if (!string.IsNullOrWhiteSpace(profile.Summary))
                w.WriteProperty("summary", profile.Summary);

            WriteFirebox(w, profile.Firebox);
            WriteBoiler(w, profile.Boiler);
            WriteDraft(w, profile.Draft);
            WriteCylinders(w, profile.Cylinders);
            WriteInjectors(w, profile.Injectors, sortTenders);
            WriteStoker(w, profile.Stoker);
            WriteFeedPump(w, profile.FeedPump);
            WriteBooster(w, profile.Booster);
            if (profile.AutoFireDoor != null)
                w.WriteProperty("autoFireDoor", profile.AutoFireDoor.Fitted);
            WriteCab(w, profile.Cab);
            WriteStack(w, profile.Stack);

            w.WriteEndObject();
            return w.ToString();
        }

        private static void WriteFirebox(MiniJsonWriter w, MakerFitFirebox fb)
        {
            if (fb == null)
                return;
            w.WritePropertyName("firebox");
            w.WriteStartObject();
            if (fb.GrateAreaM2.HasValue) w.WriteProperty("grateAreaM2", fb.GrateAreaM2.Value, "0.###");
            if (fb.GrateSlopeDeg.HasValue) w.WriteProperty("grateSlopeDeg", fb.GrateSlopeDeg.Value, "0.##");
            if (fb.SlopeOverPile) w.WriteProperty("slopeOverPile", true);
            if (fb.BackRowDraft.HasValue) w.WriteProperty("backRowDraft", fb.BackRowDraft.Value, "0.###");
            if (fb.FrontRowDraft.HasValue) w.WriteProperty("frontRowDraft", fb.FrontRowDraft.Value, "0.###");
            if (fb.SideDraft.HasValue) w.WriteProperty("sideDraft", fb.SideDraft.Value, "0.###");
            if (fb.OverFireMixing.HasValue) w.WriteProperty("overFireMixing", fb.OverFireMixing.Value, "0.###");
            if (fb.AshFraction.HasValue) w.WriteProperty("ashFraction", fb.AshFraction.Value, "0.###");
            if (fb.ClinkerFactor.HasValue) w.WriteProperty("clinkerFactor", fb.ClinkerFactor.Value, "0.###");
            if (fb.ReversedDoor) w.WriteProperty("reversedDoor", true);
            if (fb.OverFireJets != null)
            {
                w.WritePropertyName("overFireJets");
                w.WriteStartObject();
                w.WriteProperty("count", fb.OverFireJets.Count);
                w.WriteProperty("tube", fb.OverFireJets.Tube == JetTube.Small ? "small" : "large");
                w.WriteEndObject();
            }
            w.WriteEndObject();
        }

        private static void WriteBoiler(MiniJsonWriter w, MakerFitBoiler b)
        {
            if (b == null)
                return;
            w.WritePropertyName("boiler");
            w.WriteStartObject();
            if (b.MaxEvaporationKgPerS.HasValue) w.WriteProperty("maxEvaporationKgPerS", b.MaxEvaporationKgPerS.Value, "0.###");
            if (b.SafetyValves != null)
            {
                w.WritePropertyName("safetyValves");
                w.WriteStartObject();
                w.WriteProperty("liftBarAbs", b.SafetyValves.LiftBarAbs, "0.##");
                w.WriteProperty("capacityKgPerS", b.SafetyValves.CapacityKgPerS, "0.##");
                w.WriteEndObject();
            }
            if (b.LowWaterGuard.HasValue)
            {
                var guard = b.LowWaterGuard.Value == MakerFitLowWaterGuard.FusiblePlug ? "fusiblePlug"
                    : b.LowWaterGuard.Value == MakerFitLowWaterGuard.LowWaterAlarm ? "lowWaterAlarm" : "none";
                w.WriteProperty("lowWaterGuard", guard);
            }
            w.WriteEndObject();
        }

        private static void WriteDraft(MiniJsonWriter w, MakerFitDraft d)
        {
            if (d == null)
                return;
            w.WritePropertyName("draft");
            w.WriteStartObject();
            if (d.BlowerMaxSteamKgPerS.HasValue) w.WriteProperty("blowerMaxSteamKgPerS", d.BlowerMaxSteamKgPerS.Value, "0.###");
            if (d.NaturalDraftKgPerS.HasValue) w.WriteProperty("naturalDraftKgPerS", d.NaturalDraftKgPerS.Value, "0.###");
            if (d.AirPumps != null)
            {
                w.WritePropertyName("airPumps");
                w.WriteStartObject();
                w.WriteProperty("type", d.AirPumps.Type == MakerFitAirPumpType.CrossCompound ? "crossCompound" : "nineAndHalf");
                w.WriteProperty("count", d.AirPumps.Count);
                if (d.AirPumps.MainReservoirL > 0f)
                    w.WriteProperty("mainReservoirL", d.AirPumps.MainReservoirL, "0.#");
                w.WriteEndObject();
            }
            w.WriteEndObject();
        }

        private static void WriteStack(MiniJsonWriter w, MakerFitStack s)
        {
            if (s == null || !s.HasAny)
                return;
            w.WritePropertyName("stack");
            w.WriteStartObject();
            if (s.LipHeightM.HasValue) w.WriteProperty("lipHeightM", s.LipHeightM.Value, "0.###");
            if (s.BoreRadiusM.HasValue) w.WriteProperty("boreRadiusM", s.BoreRadiusM.Value, "0.###");
            if (s.OutsideRadiusM.HasValue) w.WriteProperty("outsideRadiusM", s.OutsideRadiusM.Value, "0.###");
            w.WriteEndObject();
        }

        private static void WriteCylinders(MiniJsonWriter w, MakerFitCylinders c)
        {
            if (c == null)
                return;
            w.WritePropertyName("cylinders");
            w.WriteStartObject();
            if (c.StockSteamPerStroke.HasValue) w.WriteProperty("stockSteamPerStroke", c.StockSteamPerStroke.Value);
            if (c.Superheated.HasValue) w.WriteProperty("superheated", c.Superheated.Value);
            if (!string.IsNullOrWhiteSpace(c.SuperheaterKind)) w.WriteProperty("superheaterKind", c.SuperheaterKind);
            if (c.MaxSuperheatC.HasValue) w.WriteProperty("maxSuperheatC", c.MaxSuperheatC.Value, "0.#");
            if (c.HalfSuperheatShare.HasValue) w.WriteProperty("halfSuperheatShare", c.HalfSuperheatShare.Value, "0.###");
            if (c.ExemptFromSuperheater.HasValue) w.WriteProperty("exemptFromSuperheater", c.ExemptFromSuperheater.Value);
            if (c.FrontEndThrottle.HasValue) w.WriteProperty("frontEndThrottle", c.FrontEndThrottle.Value);
            if (c.SteamCircuitVolumeL.HasValue) w.WriteProperty("steamCircuitVolumeL", c.SteamCircuitVolumeL.Value, "0.#");
            if (c.DriftingFittings != null)
            {
                w.WritePropertyName("driftingFittings");
                w.WriteStartObject();
                w.WriteProperty("snifting", c.DriftingFittings.Snifting);
                w.WriteProperty("driftingValve", c.DriftingFittings.DriftingValve);
                w.WriteProperty("byPass", c.DriftingFittings.ByPass);
                w.WriteEndObject();
            }
            w.WriteEndObject();
        }

        private static void WriteInjectors(MiniJsonWriter w, MakerFitInjectors inj, bool sortTenders)
        {
            if (inj == null)
                return;
            w.WritePropertyName("injectors");
            w.WriteStartObject();
            if (inj.Type.HasValue) w.WriteProperty("type", inj.Type.Value == MakerFitInjectorType.Lifting ? "lifting" : "nonLifting");
            if (inj.MaxFlowKgPerS.HasValue) w.WriteProperty("maxFlowKgPerS", inj.MaxFlowKgPerS.Value, "0.###");
            if (inj.OneLeverForTwo.HasValue) w.WriteProperty("oneLeverForTwo", inj.OneLeverForTwo.Value);
            if (inj.CrackOpening.HasValue) w.WriteProperty("crackOpening", inj.CrackOpening.Value, "0.###");
            if (inj.SteamValves.HasValue) w.WriteProperty("steamValves", inj.SteamValves.Value);
            if (inj.OwnLeverIsSteam.HasValue) w.WriteProperty("ownLeverIsSteam", inj.OwnLeverIsSteam.Value);
            if (inj.WaterRegulators != null)
            {
                w.WritePropertyName("waterRegulators");
                w.WriteStartObject();
                if (inj.WaterRegulators.InPlace != null)
                {
                    w.WritePropertyName("inPlace");
                    w.WriteStartObject();
                    w.WriteVec3("left", inj.WaterRegulators.InPlace.Left, "0.####");
                    w.WriteVec3("right", inj.WaterRegulators.InPlace.Right, "0.####");
                    w.WriteProperty("reachM", inj.WaterRegulators.InPlace.ReachM, "0.###");
                    w.WriteProperty("turnDeg", inj.WaterRegulators.InPlace.TurnDeg, "0.#");
                    w.WriteEndObject();
                }
                else
                {
                    w.WriteProperty("mesh", inj.WaterRegulators.Mesh);
                    w.WriteProperty("rightTurnDeg", inj.WaterRegulators.RightTurnDeg ?? 0f, "0.#");
                    w.WriteProperty("leftTurnDeg", inj.WaterRegulators.LeftTurnDeg ?? 0f, "0.#");
                }
                w.WriteEndObject();
            }
            if (inj.TwoValveGraph != null)
            {
                w.WritePropertyName("twoValveGraph");
                w.WriteStartObject();
                w.WriteProperty("add", inj.TwoValveGraph.Add);
                w.WriteProperty("addA", inj.TwoValveGraph.AddA);
                w.WriteProperty("addB", inj.TwoValveGraph.AddB);
                w.WriteProperty("multiplier", inj.TwoValveGraph.Multiplier);
                w.WriteProperty("multiplierOut", inj.TwoValveGraph.MultiplierOut);
                w.WriteProperty("factor", inj.TwoValveGraph.Factor);
                w.WriteProperty("factorPort", inj.TwoValveGraph.FactorPort);
                w.WriteProperty("right", inj.TwoValveGraph.Right);
                w.WriteProperty("left", inj.TwoValveGraph.Left);
                w.WriteEndObject();
            }
            if (inj.TenderLiveries != null && inj.TenderLiveries.Count > 0)
            {
                w.WritePropertyName("tenderLiveries");
                w.WriteStartArray(inline: true);
                var tenders = sortTenders ? inj.TenderLiveries.OrderBy(t => t, StringComparer.Ordinal) : (IEnumerable<string>)inj.TenderLiveries;
                foreach (var tender in tenders)
                    w.WriteString(tender);
                w.WriteEndArray();
            }
            w.WriteEndObject();
        }

        private static void WriteStoker(MiniJsonWriter w, MakerFitStoker s)
        {
            if (s == null)
                return;
            w.WritePropertyName("stoker");
            w.WriteStartObject();
            w.WriteProperty("fitted", s.Fitted);
            if (s.Retrofit) w.WriteProperty("retrofit", true);
            if (!string.IsNullOrWhiteSpace(s.AnchorPort)) w.WriteProperty("anchorPort", s.AnchorPort);
            if (s.BackLanes.HasValue) w.WriteProperty("backLanes", s.BackLanes.Value, "0.###");
            if (s.LumpsLeft.HasValue) w.WriteProperty("lumpsLeft", s.LumpsLeft.Value, "0.###");
            if (s.SupplyPipeAtM.HasValue) w.WriteProperty("supplyPipeAtM", s.SupplyPipeAtM.Value, "0.###");
            w.WriteEndObject();
        }

        private static void WriteFeedPump(MiniJsonWriter w, MakerFitFeedPump p)
        {
            if (p == null)
                return;
            w.WritePropertyName("feedPump");
            w.WriteStartObject();
            w.WriteProperty("fitted", p.Fitted);
            if (p.Fitted)
            {
                w.WriteProperty("type", p.Type == FeedHeaterType.Closed ? "closed" : "open");
                if (!string.IsNullOrWhiteSpace(p.Name)) w.WriteProperty("name", p.Name);
                w.WriteProperty("capacityKgPerS", p.CapacityKgPerS, "0.###");
                if (p.OnInjectorLever) w.WriteProperty("onInjectorLever", true);
            }
            w.WriteEndObject();
        }

        private static void WriteBooster(MiniJsonWriter w, MakerFitBooster b)
        {
            if (b == null)
                return;
            w.WritePropertyName("booster");
            w.WriteStartObject();
            w.WriteProperty("fitted", b.Fitted);
            if (b.Fitted)
            {
                w.WriteProperty("wheelDiameterM", b.WheelDiameterM, "0.####");
                w.WriteProperty("trailerLoadKg", b.TrailerLoadKg, "0.#");
                w.WriteProperty("drivingAxles", b.DrivingAxles);
            }
            w.WriteEndObject();
        }

        private static void WriteCab(MiniJsonWriter w, MakerFitCab cab)
        {
            if (cab == null || !cab.HasAny)
                return;
            w.WritePropertyName("cab");
            w.WriteStartObject();

            if (cab.Firehole != null)
            {
                w.WritePropertyName("firehole");
                w.WriteStartObject();
                w.WriteProperty("middleM", cab.Firehole.MiddleM, "0.000");
                w.WriteProperty("footM", cab.Firehole.FootM, "0.000");
                w.WriteProperty("widthM", cab.Firehole.WidthM, "0.000");
                w.WriteProperty("heightM", cab.Firehole.HeightM, "0.000");
                w.WriteProperty("planeM", cab.Firehole.PlaneM, "0.000");
                w.WriteEndObject();
            }

            if (cab.InjectorSteam != null)
            {
                w.WritePropertyName("injectorSteam");
                w.WriteStartObject();
                w.WriteVec3("position", cab.InjectorSteam.Position, "0.##");
                w.WriteVec3("rotation", cab.InjectorSteam.Rotation, "0");
                if (cab.InjectorSteam.FixedOffset.HasValue) w.WriteVec3("fixedOffset", cab.InjectorSteam.FixedOffset.Value, "0.##");
                if (cab.InjectorSteam.SideOffset.HasValue) w.WriteVec3("sideOffset", cab.InjectorSteam.SideOffset.Value, "0.##");
                w.WriteEndObject();
            }

            WriteTransformSeat(w, "dropGrate", cab.DropGrate);

            if (cab.Ashpan != null)
            {
                w.WritePropertyName("ashpan");
                w.WriteStartObject();
                w.WriteVec3("position", cab.Ashpan.Position, "0.##");
                if (cab.Ashpan.NoHanger) w.WriteProperty("noHanger", true);
                w.WriteEndObject();
            }

            if (cab.StokerManifold != null)
            {
                w.WritePropertyName("stokerManifold");
                w.WriteStartObject();
                w.WriteVec3("offsetCm", cab.StokerManifold.OffsetCm, "0.#");
                w.WriteProperty("turnDeg", cab.StokerManifold.TurnDeg, "0");
                w.WriteProperty("tiltDeg", cab.StokerManifold.TiltDeg, "0");
                w.WriteEndObject();
            }

            WriteTransformSeat(w, "stokerLever", cab.StokerLever);

            if (cab.FeedPump != null)
            {
                w.WritePropertyName("feedPump");
                w.WriteStartObject();
                if (cab.FeedPump.ValveInCar.HasValue) w.WriteVec3("valveInCar", cab.FeedPump.ValveInCar.Value, "0.##");
                if (cab.FeedPump.ValveOffset.HasValue) w.WriteVec2("valveOffset", cab.FeedPump.ValveOffset.Value, "0.##");
                if (cab.FeedPump.GaugeOffset.HasValue) w.WriteVec2("gaugeOffset", cab.FeedPump.GaugeOffset.Value, "0.##");
                if (cab.FeedPump.WallGauge.HasValue) w.WriteVec3("wallGauge", cab.FeedPump.WallGauge.Value, "0.##");
                w.WriteEndObject();
            }

            if (cab.Booster != null)
            {
                w.WritePropertyName("booster");
                w.WriteStartObject();
                if (cab.Booster.Position.HasValue) w.WriteVec3("position", cab.Booster.Position.Value, "0.##");
                if (cab.Booster.ReverserOffsetCm.HasValue) w.WriteVec3("reverserOffsetCm", cab.Booster.ReverserOffsetCm.Value, "0.#");
                w.WriteEndObject();
            }

            WriteCabValveSeat(w, "jets", cab.Jets);
            WriteCabValveSeat(w, "driftingValve", cab.DriftingValve);
            WriteShedSeat(w, "shedBlower", cab.ShedBlower);
            WriteShedSeat(w, "shedWater", cab.ShedWater);

            w.WriteEndObject();
        }

        private static void WriteTransformSeat(MiniJsonWriter w, string name, MakerFitTransformSeat seat)
        {
            if (seat == null)
                return;
            w.WritePropertyName(name);
            w.WriteStartObject();
            w.WriteVec3("position", seat.Position, "0.##");
            w.WriteEndObject();
        }

        private static void WriteCabValveSeat(MiniJsonWriter w, string name, MakerFitCabValveSeat seat)
        {
            if (seat == null)
                return;
            w.WritePropertyName(name);
            w.WriteStartObject();
            w.WriteVec3("position", seat.Position, "0.##");
            w.WriteVec3("rotation", seat.Rotation, "0");
            if (!seat.InCar) w.WriteProperty("inCar", false);
            if (!seat.PlateOffset.IsZero) w.WriteVec3("plateOffset", seat.PlateOffset, "0.###");
            if (seat.PlateSpinDeg != 0f) w.WriteProperty("plateSpinDeg", seat.PlateSpinDeg, "0");
            if (seat.PlateTurnDeg != 0f) w.WriteProperty("plateTurnDeg", seat.PlateTurnDeg, "0");
            if (seat.PlateSquare) w.WriteProperty("plateSquare", true);
            if (seat.OwnHandle) w.WriteProperty("ownHandle", true);
            w.WriteEndObject();
        }

        private static void WriteShedSeat(MiniJsonWriter w, string name, MakerFitShedSeat seat)
        {
            if (seat == null)
                return;
            w.WritePropertyName(name);
            w.WriteStartObject();
            if (!seat.Enabled)
            {
                w.WriteProperty("enabled", false);
            }
            else
            {
                if (seat.Position.HasValue) w.WriteVec3("position", seat.Position.Value, "0.##");
                if (seat.SwingDeg != 0f) w.WriteProperty("swingDeg", seat.SwingDeg, "0");
                if (seat.DownDeg != 0f) w.WriteProperty("downDeg", seat.DownDeg, "0");
                if (seat.SpinDeg != 0f) w.WriteProperty("spinDeg", seat.SpinDeg, "0");
                if (seat.OutM != 0f) w.WriteProperty("outM", seat.OutM, "0.##");
                if (!string.IsNullOrWhiteSpace(seat.Landmark)) w.WriteProperty("landmark", seat.Landmark);
            }
            w.WriteEndObject();
        }
    }

    /// <summary>
    /// Holds loaded third-party maker-fit profiles and enforces precedence:
    /// excluded types are never registered, built-in locomotives remain
    /// authoritative, and user fits override mod-shipped fits for custom liveries.
    /// </summary>
    public sealed class MakerFitCatalog
    {
        public sealed class Entry
        {
            public Entry(string livery, MakerFitProfile profile, string source, MakerFitSourceTier tier)
            {
                Livery = livery;
                Profile = profile;
                Source = source;
                Tier = tier;
            }

            public string Livery { get; }
            public MakerFitProfile Profile { get; }
            public string Source { get; }
            public MakerFitSourceTier Tier { get; }
        }

        private readonly HashSet<string> builtIn;
        private readonly HashSet<string> excluded;
        private readonly HashSet<string> fittedTenderValves;
        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly HashSet<string> tenderValves = new HashSet<string>(StringComparer.Ordinal);

        public MakerFitCatalog(
            IEnumerable<string> builtInLiveries,
            IEnumerable<string> excludedTypes,
            IEnumerable<string> fittedTenderValveLiveries = null)
        {
            builtIn = new HashSet<string>(builtInLiveries ?? Array.Empty<string>(), StringComparer.Ordinal);
            excluded = new HashSet<string>(excludedTypes ?? Array.Empty<string>(), StringComparer.Ordinal);
            fittedTenderValves = new HashSet<string>(fittedTenderValveLiveries ?? Array.Empty<string>(), StringComparer.Ordinal);
        }

        public int Count => entries.Count;

        public IEnumerable<Entry> All => entries.Values;

        public IEnumerable<string> CoalLiveries => entries.Values.Where(e => e.Profile.Fuel == MakerFitFuel.Coal).Select(e => e.Livery);

        public bool Contains(string livery) => livery != null && entries.ContainsKey(livery);

        public bool TryGet(string livery, out MakerFitProfile profile)
        {
            profile = null;
            if (livery == null || !entries.TryGetValue(livery, out var entry))
                return false;
            profile = entry.Profile;
            return true;
        }

        public bool HasTenderWaterValves(string tenderLivery) => tenderLivery != null && tenderValves.Contains(tenderLivery);

        /// <summary>
        /// Computes a deterministic, order-independent digest of all winning
        /// registered maker fits (sorted by livery ID, hashing each distinct
        /// profile body once at full round-trip float precision). Returns
        /// <c>"none"</c> when no maker fits are loaded.
        /// </summary>
        public string ComputeFingerprint()
        {
            if (entries.Count == 0)
                return "none";

            var ordered = entries.Values.OrderBy(e => e.Livery, StringComparer.Ordinal).ToList();
            var profileDigests = new Dictionary<MakerFitProfile, string>();
            var sb = new StringBuilder(ordered.Count * 96);

            using (var sha = SHA256.Create())
            {
                foreach (var entry in ordered)
                {
                    if (!profileDigests.TryGetValue(entry.Profile, out var bodyDigest))
                    {
                        var bodyJson = MakerFitFormatter.FormatCanonicalBody(entry.Profile);
                        bodyDigest = ComputeHexSha256(sha, bodyJson);
                        profileDigests[entry.Profile] = bodyDigest;
                    }
                    sb.Append(entry.Livery);
                    sb.Append('\0');
                    sb.Append(bodyDigest);
                    sb.Append('\n');
                }

                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                var hex = new StringBuilder(16);
                for (var i = 0; i < 8; i++)
                    hex.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                return $"{ordered.Count}:{hex}";
            }
        }

        private static string ComputeHexSha256(SHA256 sha, string text)
        {
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
            var hex = new StringBuilder(hash.Length * 2);
            for (var i = 0; i < hash.Length; i++)
                hex.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
            return hex.ToString();
        }

        /// <summary>
        /// Registers <paramref name="profile"/> for each eligible livery it names.
        /// Returns the number of liveries accepted.
        /// </summary>
        public int Register(string source, MakerFitSourceTier tier, MakerFitProfile profile, Action<string> log = null, Action<string> warn = null)
        {
            if (profile == null || profile.Liveries == null)
                return 0;

            var accepted = 0;
            foreach (var livery in profile.Liveries)
            {
                if (excluded.Contains(livery))
                {
                    warn?.Invoke($"Maker fit {source}: skipped '{livery}' because that locomotive type is left stock by Realistic Firing.");
                    continue;
                }
                if (builtIn.Contains(livery))
                {
                    warn?.Invoke($"Maker fit {source}: skipped '{livery}' because Realistic Firing's built-in profile is authoritative.");
                    continue;
                }
                if (entries.TryGetValue(livery, out var existing))
                {
                    if (tier > existing.Tier)
                    {
                        entries[livery] = new Entry(livery, profile, source, tier);
                        log?.Invoke($"Maker fit {source}: user fit overrides '{livery}' (previously from {existing.Source}).");
                        accepted++;
                    }
                    else
                    {
                        warn?.Invoke($"Maker fit {source}: skipped duplicate '{livery}' (already registered from {existing.Source}).");
                    }
                    continue;
                }

                entries[livery] = new Entry(livery, profile, source, tier);
                accepted++;
            }

            if (accepted > 0)
            {
                if (profile.Injectors?.TenderLiveries != null)
                {
                    foreach (var tender in profile.Injectors.TenderLiveries)
                    {
                        if (excluded.Contains(tender))
                            warn?.Invoke($"Maker fit {source}: skipped tender water valves for '{tender}' because that type is left stock by Realistic Firing.");
                        else if (builtIn.Contains(tender))
                            warn?.Invoke($"Maker fit {source}: skipped tender water valves for '{tender}' because '{tender}' is a built-in locomotive livery.");
                        else if (!fittedTenderValves.Contains(tender))
                            warn?.Invoke($"Maker fit {source}: skipped tender water valves for '{tender}' because no tender cab handle fit exists for that livery.");
                    }
                }
                RebuildTenderValves();
            }

            return accepted;
        }

        private void RebuildTenderValves()
        {
            tenderValves.Clear();
            foreach (var entry in entries.Values)
            {
                var tenders = entry.Profile.Injectors?.TenderLiveries;
                if (tenders == null)
                    continue;
                foreach (var tender in tenders)
                {
                    if (!excluded.Contains(tender) && !builtIn.Contains(tender) && fittedTenderValves.Contains(tender))
                        tenderValves.Add(tender);
                }
            }
        }
    }
}
