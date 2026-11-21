using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

public class MakerFitTests
{
    private const string FullValidJson = """
    {
      "schemaVersion": 1,
      "liveries": ["CustomPacificA", "CustomPacificB"],
      "fuel": "coal",
      "summary": "Custom Pacific locomotive with brick arch and feedwater heater",
      "firebox": {
        "grateAreaM2": 6.2,
        "grateSlopeDeg": -6.0,
        "slopeOverPile": true,
        "backRowDraft": 1.15,
        "frontRowDraft": 0.85,
        "sideDraft": 1.05,
        "overFireMixing": 1.1,
        "ashFraction": 1.25,
        "clinkerFactor": 0.9,
        "reversedDoor": true,
        "overFireJets": {
          "count": 6,
          "tube": "large"
        }
      },
      "boiler": {
        "maxEvaporationLbPerHr": 48000,
        "safetyValves": {
          "liftPsiGauge": 200,
          "capacityKgPerS": 6.5
        },
        "lowWaterGuard": "fusiblePlug"
      },
      "draft": {
        "blowerMaxSteamKgPerS": 0.55,
        "naturalDraftKgPerS": 0.62,
        "airPumps": {
          "type": "crossCompound",
          "count": 2,
          "mainReservoirL": 1400
        }
      },
      "cylinders": {
        "stockSteamPerStroke": false,
        "superheated": true,
        "superheaterKind": "Type E",
        "maxSuperheatC": 170,
        "halfSuperheatShare": 0.8,
        "frontEndThrottle": true,
        "steamCircuitVolumeL": 620,
        "driftingFittings": {
          "snifting": true,
          "driftingValve": true,
          "byPass": false
        }
      },
      "injectors": {
        "type": "lifting",
        "maxFlowKgPerS": 7.5,
        "oneLeverForTwo": false,
        "crackOpening": 0.25,
        "steamValves": true,
        "ownLeverIsSteam": true,
        "waterRegulators": {
          "inPlace": {
            "left": [-1.04, 2.11, -3.69],
            "right": [1.04, 2.40, -3.69],
            "reachM": 0.115,
            "turnDeg": 150
          }
        },
        "twoValveGraph": {
          "add": "injAdd",
          "addA": "inj1",
          "addB": "inj2",
          "multiplier": "injCalc",
          "multiplierOut": "injCalc.half",
          "factor": "injFactor",
          "factorPort": "injFactor.val",
          "right": "inj2",
          "left": "inj1"
        },
        "tenderLiveries": ["CustomPacificTenderA"]
      },
      "stoker": {
        "fitted": true,
        "retrofit": true,
        "anchorPort": "blower.EXT_IN",
        "backLanes": 0.8,
        "lumpsLeft": 0.08,
        "supplyPipeAtM": -0.08
      },
      "feedPump": {
        "fitted": true,
        "type": "open",
        "name": "Worthington 4-SA",
        "capacityGph": 6600,
        "onInjectorLever": false
      },
      "booster": {
        "fitted": true,
        "wheelDiameterM": 1.1176,
        "trailerLoadKg": 21500,
        "drivingAxles": 3
      },
      "autoFireDoor": {
        "fitted": true
      },
      "cab": {
        "firehole": {
          "middleM": 0.01,
          "footM": 1.85,
          "widthM": 0.52,
          "heightM": 0.41,
          "planeM": -0.18
        },
        "injectorSteam": {
          "position": [0.12, -0.08, 0.04],
          "rotation": [-180, 0, 2],
          "sideOffset": [0.07, -0.01, 0.15]
        },
        "dropGrate": {
          "position": [0.0, 0.0, -0.3]
        },
        "ashpan": {
          "position": [-0.95, 1.12, 0.45],
          "noHanger": true
        },
        "stokerManifold": {
          "offsetCm": [18.0, -6.5, -4.0],
          "turnDeg": 5,
          "tiltDeg": -2
        },
        "stokerLever": {
          "position": [0.15, 0.0, -0.25]
        },
        "feedPump": {
          "valveOffset": [-0.14, -0.35],
          "gaugeOffset": [-0.14, -0.21],
          "wallGauge": [0.0, 0.25, 0.06]
        },
        "booster": {
          "position": [1.38, 1.65, -0.42]
        },
        "jets": {
          "position": [-0.12, 2.45, -0.14],
          "rotation": [0, 0, 0],
          "plateOffset": [0.09, 0.18, -0.04],
          "plateSpinDeg": 10,
          "plateTurnDeg": 180,
          "plateSquare": true,
          "ownHandle": true
        },
        "driftingValve": {
          "position": [0.68, 2.82, -0.10],
          "rotation": [0, -35, 0]
        },
        "shedBlower": {
          "position": [-0.62, 2.15, 9.85],
          "swingDeg": 5,
          "downDeg": 42,
          "spinDeg": 90,
          "outM": 0.03,
          "landmark": "low on the left side of the smokebox"
        },
        "shedWater": {
          "position": [-0.82, 1.38, 0.72],
          "landmark": "low on the left side of the firebox"
        }
      }
    }
    """;

    [Fact]
    public void ParsesFullValidProfileAndConvertsUnits()
    {
        var parsed = MakerFitParser.Parse(FullValidJson);
        Assert.True(parsed.Ok, string.Join("\n", parsed.Errors));
        var p = parsed.Profile!;
        Assert.Equal(1, p.SchemaVersion);
        Assert.Equal(new[] { "CustomPacificA", "CustomPacificB" }, p.Liveries);
        Assert.Equal(MakerFitFuel.Coal, p.Fuel);
        Assert.Equal(6.2f, p.Firebox!.GrateAreaM2!.Value, 3);
        Assert.Equal(-6.0f, p.Firebox.GrateSlopeDeg!.Value, 3);
        Assert.True(p.Firebox.SlopeOverPile);
        Assert.True(p.Firebox.ReversedDoor);
        Assert.Equal(6, p.Firebox.OverFireJets!.Count);
        Assert.Equal(JetTube.Large, p.Firebox.OverFireJets.Tube);

        var grate = p.Firebox.ToGrateCharacter();
        Assert.Equal(1.15f, grate.BackRowDraft, 3);
        Assert.Equal(0.85f, grate.FrontRowDraft, 3);
        Assert.Equal(1.25f, grate.AshFactor, 3);
        Assert.Equal(0.9f, grate.ClinkerFactor, 3);

        // 48,000 lb/hr -> kg/s
        Assert.Equal(48000f * MakerFitProfile.LbPerHourToKgPerS, p.Boiler!.MaxEvaporationKgPerS!.Value, 4);
        // 200 psi gauge -> absolute bar
        Assert.Equal(200f / MakerFitProfile.PsiPerBar + MakerFitProfile.AtmosphericBar, p.Boiler.SafetyValves!.LiftBarAbs, 3);
        Assert.Equal(6.5f, p.Boiler.SafetyValves.CapacityKgPerS, 3);
        Assert.Equal(MakerFitLowWaterGuard.FusiblePlug, p.Boiler.LowWaterGuard);

        Assert.Equal(0.55f, p.Draft!.BlowerMaxSteamKgPerS!.Value, 3);
        Assert.Equal(0.62f, p.Draft.NaturalDraftKgPerS!.Value, 3);
        Assert.Equal(MakerFitAirPumpType.CrossCompound, p.Draft.AirPumps!.Type);
        Assert.Equal(2, p.Draft.AirPumps.Count);
        Assert.Equal(1400f, p.Draft.AirPumps.MainReservoirL, 2);

        Assert.True(p.Cylinders!.Superheated);
        Assert.Equal("Type E", p.Cylinders.SuperheaterKind);
        Assert.Equal(170f, p.Cylinders.MaxSuperheatC!.Value, 2);
        Assert.Equal(0.8f, p.Cylinders.HalfSuperheatShare!.Value, 3);
        Assert.True(p.Cylinders.FrontEndThrottle);
        Assert.Equal(620f, p.Cylinders.SteamCircuitVolumeL!.Value, 2);
        Assert.True(p.Cylinders.DriftingFittings!.Snifting);
        Assert.True(p.Cylinders.DriftingFittings.DriftingValve);
        Assert.False(p.Cylinders.DriftingFittings.ByPass);

        Assert.Equal(MakerFitInjectorType.Lifting, p.Injectors!.Type);
        Assert.Equal(7.5f, p.Injectors.MaxFlowKgPerS!.Value, 3);
        Assert.True(p.Injectors.OwnLeverIsSteam);
        Assert.Equal("injAdd", p.Injectors.TwoValveGraph!.Add);
        Assert.Equal(new[] { "CustomPacificTenderA" }, p.Injectors.TenderLiveries);

        Assert.True(p.Stoker!.Fitted);
        Assert.True(p.Stoker.Retrofit);
        Assert.Equal("blower.EXT_IN", p.Stoker.AnchorPort);
        Assert.Equal(0.8f, p.Stoker.BackLanes!.Value, 3);
        Assert.Equal(0.08f, p.Stoker.LumpsLeft!.Value, 3);

        // 6,600 US gal/hr -> kg/s
        Assert.Equal(6600f * MakerFitProfile.UsGallonsPerHourToKgPerS, p.FeedPump!.CapacityKgPerS, 4);
        Assert.Equal(FeedHeaterType.Open, p.FeedPump.Type);
        Assert.Equal("Worthington 4-SA", p.FeedPump.Name);

        Assert.True(p.Booster!.Fitted);
        Assert.Equal(1.1176f, p.Booster.WheelDiameterM, 4);
        Assert.Equal(21500f, p.Booster.TrailerLoadKg, 1);
        Assert.Equal(3, p.Booster.DrivingAxles);

        Assert.True(p.AutoFireDoor!.Fitted);
        Assert.Equal(0.52f, p.Cab!.Firehole!.WidthM, 3);
        Assert.True(p.Cab.Ashpan!.NoHanger);
        Assert.True(p.Cab.Jets!.OwnHandle);
        Assert.True(p.Cab.Jets.PlateSquare);
        Assert.Equal("low on the left side of the smokebox", p.Cab.ShedBlower!.Landmark);
    }

    [Fact]
    public void ParsesMinimalPartialProfileWithDefaultsAndBom()
    {
        const string json = "\uFEFF{ \"schemaVersion\": 1, \"liveries\": [\"MinimalTank040\"], \"autoFireDoor\": true, \"boiler\": { \"lowWaterAlarm\": true } }";
        var parsed = MakerFitParser.Parse(json);
        Assert.True(parsed.Ok, string.Join("\n", parsed.Errors));
        Assert.Equal(MakerFitFuel.Coal, parsed.Profile!.Fuel);
        Assert.Null(parsed.Profile.Firebox);
        Assert.Equal(MakerFitLowWaterGuard.LowWaterAlarm, parsed.Profile.Boiler!.LowWaterGuard);
        Assert.True(parsed.Profile.AutoFireDoor!.Fitted);
    }

    [Fact]
    public void RoundTripsFormattedProfileLosslessly()
    {
        var first = MakerFitParser.Parse(FullValidJson);
        Assert.True(first.Ok, string.Join("\n", first.Errors));

        var formatted = MakerFitFormatter.Format(first.Profile!);
        var second = MakerFitParser.Parse(formatted);
        Assert.True(second.Ok, $"Formatted JSON failed to re-parse:\n{string.Join("\n", second.Errors)}\n\n{formatted}");

        var a = first.Profile!;
        var b = second.Profile!;
        Assert.Equal(a.SchemaVersion, b.SchemaVersion);
        Assert.Equal(a.Liveries, b.Liveries);
        Assert.Equal(a.Fuel, b.Fuel);
        Assert.Equal(a.Summary, b.Summary);
        Assert.Equal(a.Firebox!.GrateAreaM2!.Value, b.Firebox!.GrateAreaM2!.Value, 3);
        Assert.Equal(a.Boiler!.MaxEvaporationKgPerS!.Value, b.Boiler!.MaxEvaporationKgPerS!.Value, 2);
        Assert.Equal(a.Boiler.SafetyValves!.LiftBarAbs, b.Boiler.SafetyValves!.LiftBarAbs, 2);
        Assert.Equal(a.FeedPump!.CapacityKgPerS, b.FeedPump!.CapacityKgPerS, 2);
        Assert.Equal(a.Cab!.Firehole!.PlaneM, b.Cab!.Firehole!.PlaneM, 3);
        Assert.Equal(a.Cab.InjectorSteam!.Position, b.Cab.InjectorSteam!.Position);
        Assert.Equal(a.Cab.InjectorSteam.Rotation, b.Cab.InjectorSteam.Rotation);
        Assert.Equal(a.Cab.ShedBlower!.Position, b.Cab.ShedBlower!.Position);
        Assert.Equal(a.Cab.ShedBlower.DownDeg, b.Cab.ShedBlower.DownDeg, 2);
    }

    [Fact]
    public void FormatterNormalizesNegativeZeroAndEscapesStrings()
    {
        var profile = new MakerFitProfile
        {
            Liveries = new[] { "NegZeroLoco" },
            Summary = "Line \"quoted\" \\ path",
            Cab = new MakerFitCab
            {
                DropGrate = new MakerFitTransformSeat
                {
                    Position = new MakerFitVec3(-0f, -0.0001f, 0.25f),
                },
            },
        };
        var json = MakerFitFormatter.Format(profile);
        Assert.Contains("[0, 0, 0.25]", json);
        Assert.Contains("Line \\\"quoted\\\" \\\\ path", json);
        var reparsed = MakerFitParser.Parse(json);
        Assert.True(reparsed.Ok, string.Join("\n", reparsed.Errors));
        Assert.Equal("Line \"quoted\" \\ path", reparsed.Profile!.Summary);
    }

    [Theory]
    [InlineData("{ \"schemaVersion\": 2, \"liveries\": [\"A\"] }", "unsupported schemaVersion 2")]
    [InlineData("{ \"liveries\": [\"A\"] }", "schemaVersion")]
    [InlineData("{ \"schemaVersion\": 1, \"liveries\": [] }", "at least one entry")]
    [InlineData("{ \"schemaVersion\": 1, \"liveries\": [\"A\", \"A\"] }", "duplicate entry")]
    [InlineData("{ \"schemaVersion\": 1, \"liveries\": [\"A\"], \"fuel\": \"nuclear\" }", "coal")]
    [InlineData("{ \"schemaVersion\": 1, \"liveries\": [\"A\"], \"unknownField\": 1 }", "unknown property")]
    [InlineData("{ \"schemaVersion\": 1, \"liveries\": [\"A\"], \"firebox\": { \"grateAreaM2\": 0.001 } }", "out of range")]
    [InlineData("{ \"schemaVersion\": 1, \"liveries\": [\"A\"], \"firebox\": { \"ashFraction\": 1.0, \"ashFactor\": 1.0 } }", "not both")]
    [InlineData("{ \"schemaVersion\": 1, \"liveries\": [\"A\"], \"boiler\": { \"maxEvaporationKgPerS\": 5, \"maxEvaporationLbPerHr\": 40000 } }", "not both")]
    [InlineData("{ \"schemaVersion\": 1, \"liveries\": [\"A\"], \"boiler\": { \"fusiblePlug\": true, \"lowWaterAlarm\": true } }", "cannot both be true")]
    [InlineData("{ \"schemaVersion\": 1, \"liveries\": [\"A\"], \"boiler\": { \"safetyValves\": { \"capacityKgPerS\": 5 } } }", "liftBarAbs")]
    [InlineData("{ \"schemaVersion\": 1, \"liveries\": [\"A\"], \"draft\": { \"blowerMaxSteamKgPerS\": 0.5 } }", "must be provided together")]
    [InlineData("{ \"schemaVersion\": 1, \"liveries\": [\"A\"], \"cylinders\": { \"superheated\": false, \"maxSuperheatC\": 150 } }", "superheated")]
    [InlineData("{ \"schemaVersion\": 1, \"liveries\": [\"A\"], \"cylinders\": { \"superheated\": false, \"frontEndThrottle\": true } }", "superheated")]
    [InlineData("{ \"schemaVersion\": 1, \"liveries\": [\"A\"], \"boiler\": { \"safetyValves\": { \"liftPsiGauge\": 25, \"capacityKgPerS\": 5 } } }", "out of range")]
    [InlineData("{ \"schemaVersion\": 1, \"liveries\": [\"A\"], \"boiler\": { \"safetyValves\": { \"liftPsiGauge\": 570, \"capacityKgPerS\": 5 } } }", "out of range")]
    [InlineData("{ \"schemaVersion\": 1, \"liveries\": [\"A\"], \"boiler\": { \"maxEvaporationLbPerHr\": 320000 } }", "out of range")]
    [InlineData("{ \"schemaVersion\": 1, \"liveries\": [\"A\"], \"boiler\": { \"maxEvaporationLbPerHr\": 390 } }", "out of range")]
    [InlineData("{ \"schemaVersion\": 1, \"liveries\": [\"A\"], \"feedPump\": { \"fitted\": true, \"type\": \"open\", \"capacityGph\": 90 } }", "out of range")]
    [InlineData("{ \"schemaVersion\": 1, \"liveries\": [\"A\"], \"feedPump\": { \"fitted\": true, \"type\": \"open\", \"capacityGph\": 39000 } }", "out of range")]
    [InlineData("{ \"schemaVersion\": 1, \"liveries\": [\"A\"], \"injectors\": { \"count\": 2 } }", "unknown property")]
    [InlineData("{ \"schemaVersion\": 1, \"liveries\": [\"A\"], \"cab\": { \"injectorSteam\": { \"position\": [0, 0, 0], \"scale\": 1.0 } } }", "unknown property")]
    [InlineData("{ \"schemaVersion\": 1, \"liveries\": [\"A\"], \"cab\": { \"injectorSteam\": { \"position\": [0, 0, 0], \"parent\": \"backhead\" } } }", "unknown property")]
    [InlineData("{ \"schemaVersion\": 1, \"liveries\": [\"A\"], \"cab\": { \"dropGrate\": { \"position\": [0, 0, 0], \"rotation\": [0, 0, 0] } } }", "unknown property")]
    [InlineData("{ \"schemaVersion\": 1, \"liveries\": [\"A\"], \"cab\": { \"ashpan\": { \"position\": [0, 0, 0], \"rotation\": [0, 0, 0] } } }", "unknown property")]
    [InlineData("{ \"schemaVersion\": 1, \"liveries\": [\"A\"], \"cab\": { \"stokerLever\": { \"position\": [0, 0, 0], \"rotation\": [0, 0, 0] } } }", "unknown property")]
    [InlineData("{ \"schemaVersion\": 1, \"liveries\": [\"A\"], \"cab\": { \"booster\": { \"position\": [0, 0, 0], \"rotation\": [0, 0, 0] } } }", "unknown property")]
    [InlineData("{ \"schemaVersion\": 1, \"liveries\": [\"A\"], \"cab\": { \"shedBlower\": { \"position\": [0, 0, 0], \"parent\": \"smokebox\" } } }", "unknown property")]
    [InlineData("{ \"schemaVersion\": 1, \"liveries\": [\"A\"], \"fuel\": \"oil\", \"stoker\": { \"fitted\": true, \"retrofit\": true } }", "oil-fired")]
    [InlineData("{ \"schemaVersion\": 1, \"liveries\": [\"A\"], \"feedPump\": { \"fitted\": true, \"type\": \"open\" } }", "capacityKgPerS")]
    [InlineData("{ \"schemaVersion\": 1, \"liveries\": [\"A\"], \"booster\": { \"fitted\": true, \"wheelDiameterM\": 1.1 } }", "trailerLoadKg")]
    [InlineData("{ \"schemaVersion\": 1, \"liveries\": [\"A\"], \"cab\": { \"firehole\": { \"middleM\": 0, \"footM\": 1, \"widthM\": 0.01, \"heightM\": 0.4, \"planeM\": 0 } } }", "out of range")]
    [InlineData("{ \"schemaVersion\": 1, \"liveries\": [\"A\"], \"cab\": { \"shedBlower\": { \"enabled\": false, \"position\": [-0.5, 2, 5] } } }", "enabled")]
    [InlineData("{ \"schemaVersion\": 1, \"liveries\": [\"A\"], \"liveries\": [\"B\"] }", "duplicate property")]
    [InlineData("not json at all", "JSON syntax error")]
    public void RejectsInvalidDocumentsWithActionableErrors(string json, string expectedErrorFragment)
    {
        var parsed = MakerFitParser.Parse(json);
        Assert.False(parsed.Ok);
        Assert.Null(parsed.Profile);
        Assert.Contains(parsed.Errors, e => e.Contains(expectedErrorFragment, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CatalogEnforcesExcludedBuiltInAndTierPrecedence()
    {
        var catalog = new MakerFitCatalog(
            builtInLiveries: new[] { "LocoS282A", "A.T.&S.F. 2901-A" },
            excludedTypes: new[] { "S040F", "S484-841A" });
        var warnings = new List<string>();
        var logs = new List<string>();

        var mixed = MakerFitParser.Parse("""
        {
          "schemaVersion": 1,
          "liveries": ["S040F", "S484-841A", "LocoS282A", "A.T.&S.F. 2901-A", "CustomModLoco"],
          "summary": "from mod"
        }
        """).Profile!;

        var accepted = catalog.Register("MyMod/realistic-firing.json", MakerFitSourceTier.Mod, mixed, logs.Add, warnings.Add);
        Assert.Equal(1, accepted);
        Assert.False(catalog.Contains("S040F"));
        Assert.False(catalog.Contains("S484-841A"));
        Assert.False(catalog.Contains("LocoS282A"));
        Assert.False(catalog.Contains("A.T.&S.F. 2901-A"));
        Assert.True(catalog.Contains("CustomModLoco"));
        Assert.Equal(4, warnings.Count);

        // Duplicate mod-tier fit is rejected with warning; first wins.
        var dupMod = MakerFitParser.Parse("""{ "schemaVersion": 1, "liveries": ["CustomModLoco"], "summary": "second mod" }""").Profile!;
        Assert.Equal(0, catalog.Register("OtherMod/realistic-firing.json", MakerFitSourceTier.Mod, dupMod, logs.Add, warnings.Add));
        Assert.True(catalog.TryGet("CustomModLoco", out var active));
        Assert.Equal("from mod", active!.Summary);

        // User-tier fit overrides mod-tier fit for custom liveries, but still cannot override built-in/excluded.
        var userOverride = MakerFitParser.Parse("""{ "schemaVersion": 1, "liveries": ["CustomModLoco", "LocoS282A"], "summary": "from user" }""").Profile!;
        Assert.Equal(1, catalog.Register("Fits/custom.json", MakerFitSourceTier.User, userOverride, logs.Add, warnings.Add));
        Assert.True(catalog.TryGet("CustomModLoco", out active));
        Assert.Equal("from user", active!.Summary);
        Assert.False(catalog.Contains("LocoS282A"));
    }

    [Fact]
    public void CatalogDerivesTenderValvesFromWinningEntriesOnly()
    {
        var fittedTenders = new[] { "TenderA", "TenderB" };
        var catalog = new MakerFitCatalog(Array.Empty<string>(), Array.Empty<string>(), fittedTenders);

        var modFit1 = MakerFitParser.Parse("""
        {
          "schemaVersion": 1,
          "liveries": ["Loco1"],
          "injectors": { "tenderLiveries": ["TenderA"] }
        }
        """).Profile!;
        Assert.Equal(1, catalog.Register("Mod1/realistic-firing.json", MakerFitSourceTier.Mod, modFit1));
        Assert.True(catalog.HasTenderWaterValves("TenderA"));

        // User override switches Loco1 from TenderA to TenderB: TenderA must be dropped.
        var userFit1 = MakerFitParser.Parse("""
        {
          "schemaVersion": 1,
          "liveries": ["Loco1"],
          "injectors": { "tenderLiveries": ["TenderB"] }
        }
        """).Profile!;
        Assert.Equal(1, catalog.Register("Fits/loco1.json", MakerFitSourceTier.User, userFit1));
        Assert.False(catalog.HasTenderWaterValves("TenderA"));
        Assert.True(catalog.HasTenderWaterValves("TenderB"));

        // When another winning entry still names TenderA, overriding Loco2 must retain TenderA
        // only while a winning entry references it.
        var catalogShared = new MakerFitCatalog(Array.Empty<string>(), Array.Empty<string>(), fittedTenders);
        var modSharedA = MakerFitParser.Parse("""
        {
          "schemaVersion": 1,
          "liveries": ["Loco1", "Loco2"],
          "injectors": { "tenderLiveries": ["TenderA"] }
        }
        """).Profile!;
        Assert.Equal(2, catalogShared.Register("Mod/realistic-firing.json", MakerFitSourceTier.Mod, modSharedA));
        Assert.True(catalogShared.HasTenderWaterValves("TenderA"));

        // Override only Loco1 to TenderB: Loco2 still wins with TenderA, so both TenderA and TenderB stay active.
        Assert.Equal(1, catalogShared.Register("Fits/loco1.json", MakerFitSourceTier.User, userFit1));
        Assert.True(catalogShared.HasTenderWaterValves("TenderA"));
        Assert.True(catalogShared.HasTenderWaterValves("TenderB"));

        // Override Loco2 with no tenderLiveries: TenderA is now dropped while TenderB stays active.
        var userFit2NoTender = MakerFitParser.Parse("""
        {
          "schemaVersion": 1,
          "liveries": ["Loco2"]
        }
        """).Profile!;
        Assert.Equal(1, catalogShared.Register("Fits/loco2.json", MakerFitSourceTier.User, userFit2NoTender));
        Assert.False(catalogShared.HasTenderWaterValves("TenderA"));
        Assert.True(catalogShared.HasTenderWaterValves("TenderB"));
    }

    [Fact]
    public void CatalogRejectsUnfittedTenderLiveriesWithDiagnostic()
    {
        var warnings = new List<string>();
        var catalog = new MakerFitCatalog(
            builtInLiveries: new[] { "LocoS282A" },
            excludedTypes: new[] { "S040F" },
            fittedTenderValveLiveries: new[] { "FittedStanierTender" });

        var profile = MakerFitParser.Parse("""
        {
          "schemaVersion": 1,
          "liveries": ["CustomLocoWithTender"],
          "injectors": { "tenderLiveries": ["FittedStanierTender", "CustomUnfittedTender", "S040F"] }
        }
        """).Profile!;

        var accepted = catalog.Register("MyMod/realistic-firing.json", MakerFitSourceTier.Mod, profile, warn: warnings.Add);
        Assert.Equal(1, accepted);
        Assert.True(catalog.HasTenderWaterValves("FittedStanierTender"));
        Assert.False(catalog.HasTenderWaterValves("CustomUnfittedTender"));
        Assert.False(catalog.HasTenderWaterValves("S040F"));
        Assert.Contains(warnings, w => w.Contains("CustomUnfittedTender") && w.Contains("handle fit", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warnings, w => w.Contains("S040F") && w.Contains("left stock", StringComparison.OrdinalIgnoreCase));

        // Fail-closed: catalog without fitted tender liveries rejects custom tenders.
        var emptyFitWarnings = new List<string>();
        var strictCatalog = new MakerFitCatalog(Array.Empty<string>(), Array.Empty<string>());
        strictCatalog.Register("MyMod/realistic-firing.json", MakerFitSourceTier.Mod, profile, warn: emptyFitWarnings.Add);
        Assert.False(strictCatalog.HasTenderWaterValves("FittedStanierTender"));
        Assert.Contains(emptyFitWarnings, w => w.Contains("FittedStanierTender") && w.Contains("handle fit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ConvertedUnitRangesAcceptJustAboveSiFloor()
    {
        // 30 psi gauge = 30 / 14.5038 + 1.01325 = 3.0817 bar abs (>= 3.0 bar abs floor)
        var parsed = MakerFitParser.Parse("""
        {
          "schemaVersion": 1,
          "liveries": ["LowPressureLoco"],
          "boiler": {
            "maxEvaporationLbPerHr": 400,
            "safetyValves": { "liftPsiGauge": 30, "capacityKgPerS": 1.0 }
          },
          "feedPump": { "fitted": true, "type": "open", "capacityGph": 100 }
        }
        """);
        Assert.True(parsed.Ok, string.Join("\n", parsed.Errors));
        Assert.True(parsed.Profile!.Boiler!.SafetyValves!.LiftBarAbs >= 3f);
    }

    [Fact]
    public void CatalogFingerprintIsDeterministicAndOrderIndependent()
    {
        var empty = new MakerFitCatalog(Array.Empty<string>(), Array.Empty<string>());
        Assert.Equal("none", empty.ComputeFingerprint());

        var fitA = MakerFitParser.Parse("""
        {
          "schemaVersion": 1,
          "liveries": ["LocoA"],
          "boiler": { "safetyValves": { "liftPsiGauge": 200, "capacityKgPerS": 6.5 } },
          "injectors": { "tenderLiveries": ["Tender2", "Tender1"] }
        }
        """).Profile!;
        var fitB = MakerFitParser.Parse("""
        {
          "schemaVersion": 1,
          "liveries": ["LocoB"],
          "fuel": "oil"
        }
        """).Profile!;

        var cat1 = new MakerFitCatalog(Array.Empty<string>(), Array.Empty<string>());
        cat1.Register("A/realistic-firing.json", MakerFitSourceTier.Mod, fitA);
        cat1.Register("B/realistic-firing.json", MakerFitSourceTier.Mod, fitB);

        var cat2 = new MakerFitCatalog(Array.Empty<string>(), Array.Empty<string>());
        cat2.Register("B/realistic-firing.json", MakerFitSourceTier.Mod, fitB);
        cat2.Register("A/realistic-firing.json", MakerFitSourceTier.Mod, fitA);

        var fp1 = cat1.ComputeFingerprint();
        var fp2 = cat2.ComputeFingerprint();
        Assert.StartsWith("2:", fp1);
        Assert.Equal(fp1, fp2);

        // User override changes the fingerprint.
        var overrideA = MakerFitParser.Parse("""
        {
          "schemaVersion": 1,
          "liveries": ["LocoA"],
          "boiler": { "safetyValves": { "liftPsiGauge": 210, "capacityKgPerS": 6.5 } },
          "injectors": { "tenderLiveries": ["Tender1", "Tender2"] }
        }
        """).Profile!;
        cat1.Register("Fits/locoA.json", MakerFitSourceTier.User, overrideA);
        Assert.NotEqual(fp1, cat1.ComputeFingerprint());
    }

    [Fact]
    public void CatalogFingerprintPreservesRoundTripFloatPrecision()
    {
        var lift1 = MakerFitParser.Parse("""
        {
          "schemaVersion": 1,
          "liveries": ["PrecisionLoco"],
          "boiler": { "safetyValves": { "liftBarAbs": 12.001, "capacityKgPerS": 5.0 } }
        }
        """).Profile!;
        var lift2 = MakerFitParser.Parse("""
        {
          "schemaVersion": 1,
          "liveries": ["PrecisionLoco"],
          "boiler": { "safetyValves": { "liftBarAbs": 12.004, "capacityKgPerS": 5.0 } }
        }
        """).Profile!;

        var cat1 = new MakerFitCatalog(Array.Empty<string>(), Array.Empty<string>());
        var cat2 = new MakerFitCatalog(Array.Empty<string>(), Array.Empty<string>());
        cat1.Register("A.json", MakerFitSourceTier.Mod, lift1);
        cat2.Register("B.json", MakerFitSourceTier.Mod, lift2);
        Assert.NotEqual(cat1.ComputeFingerprint(), cat2.ComputeFingerprint());

        // Also check fields formatted as integers in pretty-print (e.g. rotation degrees).
        var rot1 = MakerFitParser.Parse("""
        {
          "schemaVersion": 1,
          "liveries": ["PrecisionLoco"],
          "cab": { "injectorSteam": { "position": [0.1, 0.2, 0.3], "rotation": [-180, 0.2, 2] } }
        }
        """).Profile!;
        var rot2 = MakerFitParser.Parse("""
        {
          "schemaVersion": 1,
          "liveries": ["PrecisionLoco"],
          "cab": { "injectorSteam": { "position": [0.1, 0.2, 0.3], "rotation": [-180, 0.4, 2] } }
        }
        """).Profile!;

        var catRot1 = new MakerFitCatalog(Array.Empty<string>(), Array.Empty<string>());
        var catRot2 = new MakerFitCatalog(Array.Empty<string>(), Array.Empty<string>());
        catRot1.Register("A.json", MakerFitSourceTier.Mod, rot1);
        catRot2.Register("B.json", MakerFitSourceTier.Mod, rot2);
        Assert.NotEqual(catRot1.ComputeFingerprint(), catRot2.ComputeFingerprint());
    }

    [Fact]
    public void CatalogFingerprintMatchesForMultiLiveryAndSplitProfiles()
    {
        const string body = """
          "fuel": "coal",
          "summary": "Shared multi-livery class",
          "boiler": { "safetyValves": { "liftBarAbs": 14.803, "capacityKgPerS": 6.25 } }
        """;

        var combined = MakerFitParser.Parse($$"""
        {
          "schemaVersion": 1,
          "liveries": ["ClassC", "ClassA", "ClassB"],
          {{body}}
        }
        """).Profile!;

        var catCombined = new MakerFitCatalog(Array.Empty<string>(), Array.Empty<string>());
        catCombined.Register("Pack/realistic-firing.json", MakerFitSourceTier.Mod, combined);

        var catSplit = new MakerFitCatalog(Array.Empty<string>(), Array.Empty<string>());
        foreach (var livery in new[] { "ClassB", "ClassC", "ClassA" })
        {
            var single = MakerFitParser.Parse($$"""
            {
              "schemaVersion": 1,
              "liveries": ["{{livery}}"],
              {{body}}
            }
            """).Profile!;
            catSplit.Register($"Pack/{livery}.json", MakerFitSourceTier.Mod, single);
        }

        var fpCombined = catCombined.ComputeFingerprint();
        var fpSplit = catSplit.ComputeFingerprint();
        Assert.StartsWith("3:", fpCombined);
        Assert.Equal(fpCombined, fpSplit);

        // Many liveries sharing one profile instance produce a deterministic digest.
        var manyLiveries = string.Join(", ", Enumerable.Range(0, 50).Select(i => $"\"FleetLoco_{i:D2}\""));
        var fleet = MakerFitParser.Parse($$"""
        {
          "schemaVersion": 1,
          "liveries": [{{manyLiveries}}],
          {{body}}
        }
        """).Profile!;
        var catFleet1 = new MakerFitCatalog(Array.Empty<string>(), Array.Empty<string>());
        var catFleet2 = new MakerFitCatalog(Array.Empty<string>(), Array.Empty<string>());
        catFleet1.Register("Fleet.json", MakerFitSourceTier.Mod, fleet);
        catFleet2.Register("Fleet.json", MakerFitSourceTier.Mod, fleet);
        var fpFleet = catFleet1.ComputeFingerprint();
        Assert.StartsWith("50:", fpFleet);
        Assert.Equal(fpFleet, catFleet2.ComputeFingerprint());
    }

    [Fact]
    public void StackGeometryHintParsesValidatesAndRoundTrips()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "liveries": ["CustomStackLoco"],
          "stack": {
            "lipHeightM": 0.44,
            "boreRadiusM": 0.21,
            "outsideRadiusM": 0.28
          }
        }
        """;

        var parsed = MakerFitParser.Parse(json);
        Assert.True(parsed.Ok, string.Join("; ", parsed.Errors));
        Assert.NotNull(parsed.Profile?.Stack);
        Assert.Equal(0.44f, parsed.Profile!.Stack!.LipHeightM);
        Assert.Equal(0.21f, parsed.Profile.Stack.BoreRadiusM);
        Assert.Equal(0.28f, parsed.Profile.Stack.OutsideRadiusM);

        var formatted = MakerFitFormatter.Format(parsed.Profile);
        var reparsed = MakerFitParser.Parse(formatted);
        Assert.True(reparsed.Ok, string.Join("; ", reparsed.Errors));
        Assert.Equal(0.44f, reparsed.Profile?.Stack?.LipHeightM);
        Assert.Equal(0.21f, reparsed.Profile?.Stack?.BoreRadiusM);
        Assert.Equal(0.28f, reparsed.Profile?.Stack?.OutsideRadiusM);

        var badOrder = MakerFitParser.Parse("""
        {
          "schemaVersion": 1,
          "liveries": ["BadStackLoco"],
          "stack": { "boreRadiusM": 0.35, "outsideRadiusM": 0.25 }
        }
        """);
        Assert.False(badOrder.Ok);
        Assert.Contains(badOrder.Errors, e => e.Contains("boreRadiusM") && e.Contains("outsideRadiusM"));

        var negative = MakerFitParser.Parse("""
        {
          "schemaVersion": 1,
          "liveries": ["BadStackLoco"],
          "stack": { "lipHeightM": -0.2 }
        }
        """);
        Assert.False(negative.Ok);
    }
}
