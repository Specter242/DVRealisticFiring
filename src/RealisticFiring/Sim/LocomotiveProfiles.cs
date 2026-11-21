using System.Collections.Generic;
using System.Linq;
using RealisticFiring.Core;
using RealisticFiring.Visuals;
using UnityEngine;

namespace RealisticFiring.Sim
{
    /// <summary>
    /// Single authoritative registry of every supported steam locomotive's
    /// simulation parameters, fittings, shed hose seats, and cab placements.
    /// Excluded locomotives (<see cref="LeftStock"/>: <c>S040F</c>, <c>S484-841A</c>)
    /// are never registered here.
    /// </summary>
    internal static class LocomotiveProfiles
    {
        private const float LbPerHour = 0.45359237f / 3600f;

        // The FEF-1's 91,000 cu in reservoir cast in its frame (RME Feb 1938 pp.43-47), L.
        private const float FefMainReservoir = 1491f;


        // Usual backhead injector steam lever turns.
        // Each backhead seat: move on backhead (x toward cab middle, y up, z out), m, and
        // turn (about backhead up), tilt (about across), twist (about face), degrees.
        private static readonly Vector3 StandardInjectorTurns = new Vector3(-180f, 0f, 2f);

        // QJ 1.0.2's existing hollow water fitting mouth (end-ring vertices,
        // not the mesh's overall center whose side branch sits 17 mm off-axis).
        internal static readonly Vector3 QjWaterMouth = new Vector3(-1.5431395f, 1.5876111f, 2.4728939f);

        // Standing with a bright fire and the throttle shut, the elements went
        // "above red heat" (Santa Fe, 1921). They take minutes to come up; their
        // steel's heat over the steam's at the full rate is about 75 s.
        internal static SuperheaterSpec Superheat(string kind, float full, float halfShare) =>
            new SuperheaterSpec { Kind = kind, FullSuperheat = full, HalfSuperheat = full * halfShare, StandingSuperheat = 400f, TimeConstant = 75f };

        // Emerald's engines' two-injector averaging graph: the LMS 8F 1.2.1 and
        // the LMS 4F 1.1.1 wire the same IDs, read from each bundle.
        private static readonly TwoValveInjectorGraph EmeraldTwoValveGraph = new TwoValveInjectorGraph
        {
            Add = "injectoradding", AddA = "injector1", AddB = "injector2", Multiplier = "injector_calc", MultiplierOut = "injector_calc.injectorhalf",
            Factor = "injector_multiplier", FactorPort = "injector_multiplier.multiplier", Right = "injector2", Left = "injector1",
        };

        private static readonly FireboxCharacters.Firebox BoringNarrowGaugeFirebox = new FireboxCharacters.Firebox(
            "a small, level narrow-gauge box between the frames: modest pull to the front; fresh rounds smoke readily",
            new GrateCharacter { BackRowDraft = 0.94f, FrontRowDraft = 1.08f, SideDraft = 1.03f, OverFireMixing = 0.85f });

        /// <summary>Every supported locomotive profile (one per distinct locomotive; multi-livery locos share one entry).</summary>
        internal static readonly IReadOnlyList<LocomotiveProfile> All = new[]
        {
            // Stock S282.
            // No prototype (Altfuture: "all ... locomotives in DV are fictive");
            // taken as a light Mikado's wide firebox over the trailing truck,
            // with a short combustion chamber and a brick arch (USRA Light
            // Mikado, RME Aug. 1918). Its grate slopes 6.9° to the front, as
            // measured from its coal pile, so the fire creeps forward off the
            // back corners at speed. The typical draft is a Mikado's measured
            // one: a Frisco 2-8-2 with a sealed arch pulled hardest at the back
            // (Acker, RME Apr. 1930).
            new LocomotiveProfile
            {
                Id = "LocoS282A",
                Liveries = new[] { "LocoS282A" },
                CoalFirebox = true,
                // Its drawn arch covers about the front half of the grate (BrickArch): a full arch, as the tested engines'.
                // A road engine's fire (about 150 lb/sq ft/h at its most): BE's 3-1/4-in tubes with 0.150-in nozzles,
                // on 24-in centers along its 1.9 m of grate, staggered: six (council round 1).
                Firebox = new FireboxCharacters.Firebox(
                    "a light Mikado's wide box: short chamber and arch; draws hardest at the back, and the fire creeps forward off the back corners",
                    new GrateCharacter { OverFireMixing = 0.95f })
                {
                    ArchLength = 1f,
                    Jets = new OverFireJets(6, JetTube.Large),
                },
                // USRA light Mikado blower (1 1/4-in. fitting, RME Aug 1918 p.438; B&O Mikado 0.31 kg/s at 85 psi, RME July 1925 p.460; inferred).
                Blower = new BlowerSpec(0.5f, 0.6f, AirPump.NineAndHalf, 1),
                // Maker's 203 psi lift kept (within ICC's 6 psi of USRA light Mikado's 200 psi).
                SafetyValve = new SafetyValveSpec(15.0f, 6f),
                LowWaterGuard = BoilerWaterRig.Guard.FusiblePlug,
                // From the stock cab (read from the game's resources, 2026-11-14):
                // three cocks on a diagonal left of its glass (0.63 to 0.96 over 2.948 to 3.126 m),
                // at 3.094, 3.018, and 2.944 m (the bottom one at the glass's foot),
                // plus the glass's own top and bottom cocks at 3.217 and 2.857 m.
                TryCocks = new TryCockRig.Fit(
                    new[] { "s282_valve_s_03", "s282_valve_s_02", "s282_valve_s_01", "s282_valve_w_01", "s282_valve_w_02" },
                    new[] { 0.900f, 0.761f, 0.621f, 0.96f, 0.63f }),
                // USRA light Mikado: Type A, 882 sq ft on 3,783 (RME Aug 1918).
                Superheater = Superheat("Type A", 136f, 0.72f),
                SteamPath = SteamPathProfile.BeforeSuperheater(300f),
                Drifting = DriftingProfile.Upgradable,
                AutoFireDoor = AutoFireDoorMode.Upgradable,
                Booster = new BoosterFit.Fit(1.0922f, 20412f, 4, upgrade: true),
                Feedwater = new FeedwaterFit.Fit(FeedHeaterType.Open, "Worthington SA", 6.94f, onInjectorLever: false, upgrade: true),
                Injector = new InjectorProfile
                {
                    PeriodDelivery = 6f,
                    OneLeverForTwo = true,
                    HasSteamValves = true,
                    Lifting = true,
                },
                Firehole = new FireholeSeat(0.010f, 1.720f, 0.450f, 0.456f, 0.054f),
                Shed = new ShedProfile
                {
                    BlowerSeat = new ShedSeat(new Vector3(-0.48f, 1.93f, 10.32f), 0f, 44f, 90f, 0.03f),
                    WaterSeat = new ShedSeat(new Vector3(-0.22f, 1.15f, 2.28f)),
                    BlowerHint = "low on the left side of the smokebox",
                    WaterHint = "low on the left behind the last driving wheel, just ahead of the cab",
                },
                Cab = new CabProfile
                {
                    AshpanPull = new Vector3(-0.61f, 0.99f, 1.66f), 
                    FeedPumpSpot = new FeedPumpCab.Spot(Vector2.zero, new Vector2(0f, 0.12f), new Vector3(-0.46f, 2.9f, 0.06f)),
                    InjectorSteamSeat = (new Vector3(0.11f, 0.03f, 0.14f), StandardInjectorTurns), 
                    InjectorSteamFixedSeat = new Vector3(0.26f, 0.13f, 0.21f),
                    StokerManifold = new StokerCab.Placement(new Vector3(88f, 25f, 9f), -9f, 0f),
                    StokerLeverMove = new Vector3(0f, 0f, -0.3f), 
                    StokerAnchorPort = "blower.EXT_IN",
                    BoosterCarSeat = new Vector3(1.45f, 1.60f, -0.24f),
                    JetsValveSeat = new CabValve.Seat(new Vector3(-0.11f, 2.41f, 0.11f), Vector3.zero, new Vector3(0.10f, 0.21f, -0.045f), 0f, 180f, plateSquare: true, inCar: true),
                    DriftingValveSeat = new CabValve.Seat(new Vector3(0.69f, 2.84f, 0.18f), new Vector3(0f, -40f, 0f), new Vector3(0f, 0f, 0f), 0f, 0f, plateSquare: false, inCar: true),
                    // Steam chests behind front coupler and above rail (inferred from stock model).
                    DriftSoundChest = new Vector2(2.8f, 1.7f),
                    // Light Mikado box with short combustion chamber and Security arch on arch tubes (RME Aug. 1918).
                    // Stock box (mesh 's282_firebox'): door sheet z 0.21, grate sloping down 7° to z 2.0-2.2, throat
                    // sheet rising to y 2.10 at z 2.74, chamber floor on to tube plate z 3.14 (tubes from y 2.19 up),
                    // crown y 2.95 over door and 3.09 from z 2.74 on, 1.03 m between side sheets. Arch springs from
                    // throat sheet under chamber and covers front half of grate, lip 0.9 m over grate and 0.65 m under
                    // crown; two tubes and three rows of bricks span the box, as the footage's.
                    BrickArch = new BrickArchShape(2.59f, 1.9f, 1.25f, 15f, 2),
                },
            },

            // Stock S060.
            // A small switcher's narrow, level grate between the frames (the USATC S100 the community likens it to: 19 sq ft, hand fired).
            // No combustion chamber; a short arch: it draws nearly evenly, end to end, a little harder at the back, and smokes readily after firing (inferred).
            new LocomotiveProfile
            {
                Id = "LocoS060",
                Liveries = new[] { "LocoS060" },
                CoalFirebox = true,
                // Short arch (0.68 m of a 1.86 m grate, 37 %): reaches about three-quarters as far as a full arch's half.
                // Switcher: BE's 2-1/4-in tubes with 1/8-in nozzles, four on 24-in centers along its 1.65 m of grate.
                Firebox = new FireboxCharacters.Firebox(
                    "a switcher's small, narrow box: draws nearly evenly, smokes readily",
                    new GrateCharacter { BackRowDraft = 1.04f, FrontRowDraft = 0.96f, SideDraft = 1.04f, OverFireMixing = 0.8f })
                {
                    ArchLength = 0.73f,
                    Jets = new OverFireJets(4, JetTube.Small),
                },
                Blower = new BlowerSpec(0.4f, 0.3f, AirPump.NineAndHalf, 1),
                // Maker's 210 psi lift kept (USATC S100's).
                SafetyValve = new SafetyValveSpec(15.5f, 3.5f),
                LowWaterGuard = BoilerWaterRig.Guard.FusiblePlug,
                // Left of its glass (0.68 to 0.84 over 2.590 to 2.768 m), at 2.733, 2.679, and 2.626 m,
                // plus the glass's own top and bottom cocks at 2.860 and 2.499 m.
                TryCocks = new TryCockRig.Fit(
                    new[] { "s060_valve_05", "s060_valve_06", "s060_valve_07", "s060_valve_03", "s060_valve_04" },
                    new[] { 0.808f, 0.760f, 0.712f, 0.84f, 0.68f }),
                TakesStockSteam = true,
                SuperheaterExempt = true,
                SteamPath = SteamPathProfile.Saturated,
                Drifting = DriftingProfile.Upgradable,
                AutoFireDoor = AutoFireDoorMode.Upgradable,
                Injector = new InjectorProfile
                {
                    // A small tank engine's: its 16.5 in. cylinders take a No. 7 by Baldwin's rule, "next size
                    // larger with specially large boilers" (Kneass p.129). The stock fire, 5.3 MW, boils about
                    // 2.1 kg/s flat out, and the throttle and air pump take 2.2; a Sellers No. 8½, 2,587 gal/h
                    // at 200 psi (Swingle p.348), feeds that with room to spare down to 13 bar. Was a No. 7½,
                    // 2.1 kg/s, which fell behind a hot fire below full pressure (WoodenPlankGames, 2026-10-25:
                    // the glass down 15 % in five minutes at 13 bar), and before that the USRA 0-6-0's No. 11,
                    // whose least was more than this boiler boils. The game's is 5.
                    PeriodDelivery = 2.7f,
                    OneLeverForTwo = true,
                    HasSteamValves = true,
                },
                Firehole = new FireholeSeat(0.009f, 1.752f, 0.450f, 0.456f, -2.002f),
                Shed = new ShedProfile
                {
                    BlowerRightSide = true,
                    BlowerSeat = new ShedSeat(new Vector3(0.52f, 1.97f, 3.60f), 0f, 34f, 0f, 0f),
                    WaterSeat = new ShedSeat(new Vector3(-0.65f, 1.47f, -1.00f)),
                    BlowerHint = "low on the right side of the smokebox",
                    WaterHint = "on the left ahead of the cab, below the firebox",
                },
                Cab = new CabProfile
                {
                    AshpanPull = new Vector3(-0.73f, 1.11f, -2.26f), 
                    InjectorSteamSeat = (new Vector3(-0.09f, -0.37f, 0.02f), StandardInjectorTurns), 
                    JetsValveSeat = new CabValve.Seat(new Vector3(-0.204f, 3.205f, -1.973f), Vector3.zero, inCar: true),
                    JetsValveOwnHandle = true,
                    DriftingValveSeat = new CabValve.Seat(new Vector3(0.310f, 3.206f, -1.990f), Vector3.zero, Vector3.zero, 0f, 0f, plateSquare: false, inCar: true),
                    DriftingValveOwnHandle = true,
                    DriftSoundChest = new Vector2(1.5f, 1.4f),
                    // Small switcher's narrow box with a short arch and no arch tubes (USATC S100's bricks spring
                    // from studs on the side sheets). Stock box (mesh 's060_firebox'): door sheet z -1.82, throat
                    // sheet rising from y 1.88 at z -0.17 to y 2.10 at tube plate z 0.04, crown level at y 2.78,
                    // 0.90 m between side sheets. Arch springs from throat and covers front two fifths of box,
                    // lip 0.55 m under crown.
                    BrickArch = new BrickArchShape(-0.12f, 1.93f, -0.8f, 15f, 0),
                    FireboxLightGain = 1.6f,
                },
            },

            // Lilla and Cruzer's hand-fired JK3 (Nexus 1334, 1.0.1), read from its release bundle 2026-10-10. Jk3B is its tender.
            // Fictional 1897-1912 shunter; narrow, long, level grate (0.897 by 1.593 m). With no arch asserted,
            // a box burns faster at the front (Acker, RME Apr. 1940): a modest pull toward the tubes and limited
            // over-fire mixing give it a hand-fired small-box character. These are inferred firing traits, not
            // documented fittings or a historical prototype.
            new LocomotiveProfile
            {
                Id = "Jk3A",
                Liveries = new[] { "Jk3A" },
                CoalFirebox = true,
                Firebox = new FireboxCharacters.Firebox(
                    "a small, long, level box: modest pull to the front; fresh rounds smoke readily",
                    new GrateCharacter { BackRowDraft = 0.94f, FrontRowDraft = 1.08f, SideDraft = 1.03f, OverFireMixing = 0.85f }),
                Blower = new BlowerSpec(0.2f, 0.15f, AirPump.NineAndHalf, 1),
                SafetyValve = new SafetyValveSpec(15.0f, 2.5f),
                LowWaterGuard = BoilerWaterRig.Guard.None,
                TakesStockSteam = true,
                // 1897–1912 shunters were saturated.
                Superheater = SuperheaterSpec.None,
                SteamPath = SteamPathProfile.Saturated,
                Injector = new InjectorProfile
                {
                    // Two lifting injectors, 2.5 kg/s each (8 to 9 mm): each feeds its small boiler (about 2.1 kg/s)
                    // alone, as German practice sized each feed (Röll, 1912, vol. 9 p.98); lifting costs nothing in
                    // delivery (Kneass p.112). Inferred. The game's is 8 together.
                    PeriodDelivery = 5f,
                    HasSteamValves = true,
                    Lifting = true,
                    // JK3 1.0.1 two-valve averaging graph.
                    TwoValveGraph = new TwoValveInjectorGraph
                    {
                        Add = "injectorAdd", AddA = "injector", AddB = "injector2", Multiplier = "injectorNormalized", MultiplierOut = "injectorNormalized.INJECTOR_NORMALIZED",
                        Factor = "injectorDivisor", FactorPort = "injectorDivisor.DIVISOR", Right = "injector", Left = "injector2",
                    },
                },
                Firehole = new FireholeSeat(0.000f, 1.448f, 0.450f, 0.365f, -2.351f),
                Shed = new ShedProfile
                {
                    BlowerSeat = new ShedSeat(new Vector3(-0.90f, 1.35f, 3.25f), 0f, -13f, 0f, 0f),
                    WaterSeat = new ShedSeat(new Vector3(-0.54f, 1.31f, -1.35f)),
                    BlowerHint = "low on the left side of the smokebox, tilted slightly upward",
                    WaterHint = "on the left ahead of the cab, below the firebox",
                },
                Cab = new CabProfile
                {
                    AshpanPull = new Vector3(-0.86f, 1.01f, -0.87f), 
                    AshpanNoHanger = true,
                    InjectorSteamSeat = (new Vector3(-0.06f, -0.13f, 0.02f), StandardInjectorTurns), // both levers, 2026-10-11
                },
            },

            // LittleNiko36's NYC Dreyfuss Hudson (Nexus 1688; measured 2026-10-08).
            // NYC J-3a (RME May 1938): a 43-in combustion chamber, four arch tubes, four combustion tubes
            // through each side sheet; mud ring 16 in higher at the back, about 7°. Standard HT stoker:
            // the screw sends fines right and lumps left, and the right back corner is "often likely to be
            // short of coal" (1944 New York firing course pp. 45-48). Bituminous of varying quality
            // ("Indiana dirt", Hal Lewis, 1991): a little more ash and clinker.
            new LocomotiveProfile
            {
                Id = "S464-730",
                Liveries = new[] { "S464-730" },
                CoalFirebox = true,
                // NYC J-3a, 85,000 lb/h with the standard front end, Selkirk standing tests (Railway Age, 18 Jan 1941, p.181).
                PrototypeEvaporation = 85000f * LbPerHour,
                Firebox = new FireboxCharacters.Firebox(
                    "the J-3a's box: 43-in chamber, arch tubes; slopes about 7°; the stoker runs the right back corner short",
                    new GrateCharacter { ClinkerFactor = 1.1f, AshFactor = 1.1f })
                {
                    PrototypeSlope = -7f,
                    StokerLumpsLeft = 0.1f,
                },
                // Two 8 1/2-in. cross-compound air pumps on the J-3a ("the air compressors" on its pilot, Railway Age 2 Apr 1938 p.599), inferred.
                Blower = new BlowerSpec(0.8f, 0.75f, AirPump.CrossCompound, 2),
                // Maker's 280 psi lift kept (within ICC's 6 psi of J-3a's 275 psi as built).
                SafetyValve = new SafetyValveSpec(20.3f, 9.5f),
                // NYC did not use fusible plugs and fitted low-water alarms.
                LowWaterGuard = BoilerWaterRig.Guard.LowWaterAlarm,
                // NYC J-3a: Type E, 1,745 sq ft on 4,187 (RME May 1938).
                Superheater = Superheat("Type E", 175f, 0.82f),
                SteamPath = SteamPathProfile.AfterSuperheater(600f),
                // NYC J-3a: "Franklin", one forum member's recollection of the J-1s and J-3s (The Home Machinist, 2009);
                // likely, as RME lists such doors on big American engines of its day, though its article on the class
                // (May 1938) doesn't name the door.
                AutoFireDoor = AutoFireDoorMode.Prototype,
                // NYC J-3a: a Franklin C-2 on the 51-in rear trailer wheels, whose axle carries 55,500 lb; three driving axles (official NYC diagram).
                Booster = new BoosterFit.Fit(1.2954f, 25174f, 3),
                // NYC J-3a: "Elesco type K40 feedwater heater" (RME May 1938 p.167), its pump on the left, the injector
                // on the right. No K40 rating was found; a K-60-A was 12,000 gal/h (RME July 1943 p.299), so K40 is taken as ~8,000 (inferred).
                Feedwater = new FeedwaterFit.Fit(FeedHeaterType.Closed, "Elesco K40", 8.4f, onInjectorLever: true),
                Injector = new InjectorProfile
                {
                    // NYC J-3a: no injector given (RME May 1938); the NYC fitted Nathan non-lifters beside its
                    // feedwater heaters (L-3, RME Jan 1941), about 10,000 gal/h (inferred). The game's is 36.
                    PeriodDelivery = 10.5f,
                    HasSteamValves = true,
                },
                Firehole = new FireholeSeat(0.000f, 2.344f, 0.630f, 0.365f, 0.109f),
                Shed = new ShedProfile
                {
                    BlowerSeat = new ShedSeat(new Vector3(-0.74f, 2.33f, 12.47f), 0f, 43f, 0f, 0f),
                    WaterSeat = new ShedSeat(new Vector3(-0.93f, 1.49f, 0.72f)),
                    BlowerHint = "low on the left side of the smokebox",
                    WaterHint = "on the left below the cab, at the rear of the boiler",
                },
                Cab = new CabProfile
                {
                    AshpanPull = new Vector3(-1.07f, 1.18f, 1.79f), 
                    FeedPumpSpot = new FeedPumpCab.Spot(Vector2.zero, new Vector2(0.076f, 0.24f)),
                    InjectorSteamSeat = (new Vector3(0f, -0.22f, 0f), StandardInjectorTurns), // 2026-10-11
                    StokerManifold = new StokerCab.Placement(new Vector3(19.2f, -8.1f, -11.5f), 8f, -1f), 
                    StokerLeverMove = new Vector3(0.45f, 0f, -0.15f), 
                    StokerSupplyPipeAt = -0.08f,
                    BoosterPlacement = new Vector3(-26f, 15f, 0f),
                },
            },

            // Cypress and LittleNiko36's UP FEF-3 No. 844 (Nexus 1590, 1.0.58), read from its bundle and seated in game 2026-10-09.
            // UP FEF class (RME Feb. 1938, Nov. 1939; UP's FEF-3 diagram): a 90-in combustion chamber, arch tubes and
            // circulators, twelve air tubes through each side sheet, and a generous way over the arch (125 % of the
            // flue gas area, heavy cinder cutting), which eases the front's longer path, so it draws more evenly than
            // a typical sealed arch (inferred: a gap at the tube plate evened it fully, Acker 1930); mud ring 10 in
            // higher at the back, about 4°. Standard BK stoker, whose back corners ran short (Lewis). Rock Springs and
            // Superior coal: "produces no clinker", 3 to 5 % ash (USGS Bulletin 341; USBM Bulletin 209).
            new LocomotiveProfile
            {
                Id = "FEF-3",
                Liveries = new[] { "FEF-3" },
                CoalFirebox = true,
                // UP FEF-1, 68,700 lb/h by Cole's ratios (Brandt, RME Mar 1940 p.104), times the tested engines'
                // mean margin over Cole's, 1.22 (the J-3a 1.40, the Santa Fe 3765s 1.03): no FEF was tested.
                PrototypeEvaporation = 83600f * LbPerHour,
                Firebox = new FireboxCharacters.Firebox(
                    "the FEF-3's box: 90-in chamber, circulators, air tubes; slopes about 4°; Rock Springs coal; the BK stoker starves the back corners",
                    new GrateCharacter { BackRowDraft = 1.08f, FrontRowDraft = 0.92f, OverFireMixing = 1.25f, ClinkerFactor = 0.7f, AshFactor = 0.6f })
                {
                    PrototypeSlope = -4f,
                    StokerBackLanes = 0.7f,
                },
                // Two 8 1/2-in. cross-compounds (FEF-2's, RME Nov 1939 pp.508-509; FEF-3 "virtually identical",
                // Kratville, Steam Glory, 2004) and the FEF-1's 91,000 cu in reservoir cast in its frame (RME Feb 1938 pp.43-47).
                Blower = new BlowerSpec(0.85f, 0.9f, AirPump.CrossCompound, 2, FefMainReservoir),
                SafetyValve = new SafetyValveSpec(21.7f, 10.5f),
                LowWaterGuard = BoilerWaterRig.Guard.FusiblePlug,
                // UP FEF-3: a large Type A, 1,400 sq ft on 4,224 (UP diagram book).
                Superheater = Superheat("Type A", 158f, 0.75f),
                SteamPath = SteamPathProfile.AfterSuperheater(500f),
                // UP FEF class: "a Franklin firedoor" on the FEF-1 (RME Feb. 1938 p.47), and "Fire door, Butterfly"
                // from Franklin on the road's 4-6-6-4s (RME Jan. 1937); the FEF-3 taken to follow them. No. 844 went
                // to oil in 1945; the mod fires it as built, on coal.
                AutoFireDoor = AutoFireDoorMode.Prototype,
                // UP 844 as preserved: a Worthington 5-SA ("of 9,000 gallons capacity", RME July 1937 p.309)
                // from 1953, replacing its exhaust steam injector.
                Feedwater = new FeedwaterFit.Fit(FeedHeaterType.Open, "Worthington 5-SA", 9.46f, onInjectorLever: false),
                Injector = new InjectorProfile
                {
                    // UP FEF: "one Nathan non-lifting injector ... 10,000 gal. per hr." (FEF-1, RME Feb 1938 p.47);
                    // the FEF-3 was built like the FEF-1 and FEF-2. The game's is 60 (844).
                    PeriodDelivery = 10.5f,
                    HasSteamValves = true,
                },
                Firehole = new FireholeSeat(0.000f, 2.332f, 0.585f, 0.365f, -8.305f),
                Shed = new ShedProfile
                {
                    BlowerSeat = new ShedSeat(new Vector3(-0.76f, 2.36f, 7.77f), 0f, 54f, 0f, 0f),
                    WaterSeat = new ShedSeat(new Vector3(-1.42f, 1.77f, -7.27f)),
                    BlowerHint = "low on the left side of the smokebox",
                    WaterHint = "on the left outside the firebox, below the cab",
                },
                Cab = new CabProfile
                {
                    AshpanPull = new Vector3(-1.35f, 1.33f, -7.26f), 
                    FeedPumpSpot = new FeedPumpCab.Spot(new Vector2(-0.11f, -0.40f), new Vector2(-0.11f, -0.28f)),
                    InjectorSteamSeat = (new Vector3(-0.14f, -0.48f, -0.03f), StandardInjectorTurns), // 2026-10-11
                    // The 844's stoker valve sits low beside its blower valve; the manifold stands 80 cm above it.
                    StokerManifold = new StokerCab.Placement(new Vector3(-34f, 80f, 8f), 0f, 0f),
                    StokerValveCopyPath = "Controllers/Left/automaticStoker",
                },
            },

            // LittleNiko36's 4-8-4 (Nexus 1494), a UP FEF-3 in a made-up road's livery ("Black Arrow", per its bundle).
            // Same box; made different (invented): that road burns a typical bituminous, with typical ash and clinker,
            // and fitted the MB stoker (as UP did on some FEF-2s, by Kratville), which reaches the back corners.
            new LocomotiveProfile
            {
                Id = "S484-840A",
                Liveries = new[] { "S484-840A" },
                CoalFirebox = true,
                // UP FEF-1 Cole's ratios × 1.22 margin (Brandt, RME Mar 1940 p.104), as FEF-3.
                PrototypeEvaporation = 83600f * LbPerHour,
                Firebox = new FireboxCharacters.Firebox(
                    "an FEF-3's box on another road: typical bituminous, an MB stoker that reaches the back corners",
                    new GrateCharacter { BackRowDraft = 1.08f, FrontRowDraft = 0.92f, OverFireMixing = 1.25f })
                {
                    PrototypeSlope = -4f,
                },
                // Same UP 4-8-4 twin cross-compound pumps & 91,000 cu in reservoir as FEF-3 (RME Nov 1939; Kratville 2004; RME Feb 1938).
                Blower = new BlowerSpec(0.85f, 0.9f, AirPump.CrossCompound, 2, FefMainReservoir),
                SafetyValve = new SafetyValveSpec(21.7f, 10.5f),
                LowWaterGuard = BoilerWaterRig.Guard.FusiblePlug,
                Superheater = Superheat("Type A", 158f, 0.75f),
                SteamPath = SteamPathProfile.AfterSuperheater(500f),
                // LittleNiko36's "Black Arrow", an FEF-3 too (Franklin firedoor, RME Feb. 1938 p.47 / Jan. 1937).
                AutoFireDoor = AutoFireDoorMode.Prototype,
                // An FEF-3 on a made-up road ("Black Arrow"): a Worthington SA (9,000 gal/h, RME Sept 1934 p.316), as the 844's.
                Feedwater = new FeedwaterFit.Fit(FeedHeaterType.Open, "Worthington SA", 9.46f, onInjectorLever: false),
                Injector = new InjectorProfile
                {
                    // UP FEF 10,000 gal/h Nathan non-lifting injector (FEF-1, RME Feb 1938 p.47). The game's is 50.
                    PeriodDelivery = 10.5f,
                    HasSteamValves = true,
                },
                Firehole = new FireholeSeat(0.000f, 2.422f, 0.585f, 0.365f, -8.274f),
                Shed = new ShedProfile
                {
                    BlowerSeat = new ShedSeat(new Vector3(-0.79f, 2.45f, 7.57f), 0f, 54f, 0f, 0f),
                    WaterSeat = new ShedSeat(new Vector3(-0.72f, 1.41f, -7.23f)),
                    BlowerHint = "low on the left side of the smokebox",
                    WaterHint = "on the left below the cab, at the rear of the boiler",
                },
                Cab = new CabProfile
                {
                    AshpanPull = new Vector3(-1.08f, 1.27f, -6.83f), 
                    FeedPumpSpot = new FeedPumpCab.Spot(Vector2.zero, new Vector2(0f, 0.12f), new Vector3(0.75f, 3.47f, -8.36f)),
                    InjectorSteamSeat = (new Vector3(-0.25f, -0.19f, -0.01f), StandardInjectorTurns), // 2026-10-11
                    // The S484-840 has one stoker valve, on the right beside the injector.
                    StokerManifold = new StokerCab.Placement(new Vector3(-1f, -20f, 0f), -6f, 0f),
                },
                // Stack piece ('smoke box.002') is unreadable in game; rises 0.37 m above both smoke emitters.
                StackGeometry = new RealisticStackEffects.Core.StackGeometryHint { LipHeight = 0.37f },
            },

            // Emerald's LMS Stanier 8F (Nexus 1360, 1.2.1), read from its bundle 2026-10-14;
            // the WD Photographic scheme is a paint, not a livery. Both black and grey share this profile.
            new LocomotiveProfile
            {
                Id = "em_lms_8f_loc_black",
                Liveries = new[] { "em_lms_8f_loc_black", "em_lms_8f_loc_grey" },
                CoalFirebox = true,
                // LMS Class 5 at the front-end limit, 23,050 lb/h (Cox, British Railways Standard Steam Locomotives, 1966):
                // the 8F "was never subjected to similar constant speed tests, but the corresponding Class 5 4-6-0 was most
                // thoroughly tested", its firebox identical.
                PrototypeEvaporation = 23050f * LbPerHour,
                // LMS Stanier 8F: a narrow Belpaire box between the frames, 28.65 sq ft, with a brick arch and no
                // combustion chamber; its grate slopes at the front and runs level at the back (the Black Five's,
                // about 55:45, Rowledge & Reed 1981 pp.53-54), hand-fired, fixed bars. The firehole's deflector plate
                // turns secondary air down toward the arch (BR handbook 1957, pp.32, 46); fire thin and often, a thick
                // fire under the door is a fault (pp.27-33). Its arch sealed to the tube plate, the back draws hardest,
                // as British rules fire it ("keep the fire thickest along the back of the box, that is, under the door",
                // L&YR Questions for Enginemen, c.1910); how much harder, and the mixing, are inferred, and the slope
                // averaged over the grate. The mod's grate and pile slope about 12° down to the front their whole length
                // (2026-10-14); the prototype's sloped at the front alone, so its average stands over the pile's.
                Firebox = new FireboxCharacters.Firebox(
                    "a narrow Belpaire box: sloping front, level back; brick arch and a deflector plate",
                    new GrateCharacter { BackRowDraft = 1.08f, FrontRowDraft = 0.92f, SideDraft = 1.03f, OverFireMixing = 0.95f })
                {
                    PrototypeSlope = -5f,
                    PrototypeSlopeOverPile = true,
                },
                // LMS 8F: a 28.65 sq ft grate; its blower between the S060's and the S282's (inferred), natural draft
                // by grate area. Its pump stands in for the vacuum ejector, whose exhaust also goes up the chimney.
                Blower = new BlowerSpec(0.4f, 0.26f, AirPump.NineAndHalf, 1),
                // The 8F's 225 psi, which its maker set as 15.5 bar, gauge.
                SafetyValve = new SafetyValveSpec(16.5f, 4f),
                // British practice: lead plugs in the firebox crown.
                LowWaterGuard = BoilerWaterRig.Guard.FusiblePlug,
                // LMS 8F: 21 elements, 215 sq ft on 1,650 (domed boilers; lner.info, Haresnape 1970): a small Type A.
                // Jubilees' 14 elements rarely gave past 500-510 °F at 225 psi (Rowledge & Reed 1981, p.30); 100 °C
                // at the full rate is inferred.
                Superheater = Superheat("Type A", 100f, 0.72f),
                SteamPath = SteamPathProfile.BeforeSuperheater(120f),
                // LMS 8F snifting valves by the cylinders, likely (BR later took many off; when, unknown).
                Drifting = DriftingProfile.Prototype(snifting: true),
                Injector = new InjectorProfile
                {
                    // LMS 8F: the WD engines carried two live-steam injectors (Railway Gazette, 19 Jan 1940, p.83),
                    // Gresham & Craven on the LMS, under the cab footsteps (non-lifting); their size is not given.
                    // About 3.5 kg/s each, above the boiler's most (inferred). The game's is 20 together.
                    // Two-valve graph (LMS 8F 1.2.1): fireman's (right) 'injector2', driver's (left) 'injector1'.
                    PeriodDelivery = 7f,
                    HasSteamValves = true,
                    OwnLeverIsSteam = true,
                    TwoValveGraph = EmeraldTwoValveGraph,
                },
                // Fitted on the black livery; the grey is the same cab.
                Firehole = new FireholeSeat(0.000f, 2.018f, 0.540f, 0.410f, -4.379f),
                Shed = new ShedProfile
                {
                    HasBlowerConnection = false,
                    WaterSeat = new ShedSeat(new Vector3(-0.66f, 1.55f, -2.15f)),
                    WaterHint = "low on the left side of the firebox, below the cab",
                },
                Cab = new CabProfile
                {
                    AshpanPull = new Vector3(-1.00f, 1.42f, -2.39f), 
                    InjectorWaterSeats = (new Vector2(0.55f, 0.95f), new Vector2(0.4f, 1.3f)),
                    // Regulators cut from CAB.113: handwheel nearly a whole turn (354°), handle as far as cab side lets it (150°).
                    WaterRegulatorMesh = ("CAB.113", 354f, 150f),
                },
            },

            // Emerald's LMS Fowler 4F 0-6-0 (Nexus 1457, 1.1.1), read from its bundle 2026-11-15:
            // its BR livery (No. 44422) and its LMS one (No. 4027). em_lms_4f_tender_br and _lms are its tenders.
            new LocomotiveProfile
            {
                Id = "em_fowler_4f_44422",
                Liveries = new[] { "em_fowler_4f_44422", "em_fowler_4f_4027" },
                CoalFirebox = true,
                PrototypeEvaporation = 12000f * LbPerHour,
                Firebox = new FireboxCharacters.Firebox(
                    "a narrow Belpaire box between the frames: brick arch, hand fired",
                    new GrateCharacter { BackRowDraft = 1.08f, FrontRowDraft = 0.92f, SideDraft = 1.03f, OverFireMixing = 0.9f })
                {
                    PrototypeGrateArea = 21.1f * 0.09290304f,
                },
                // LMS Fowler 4F: a 21.1 sq ft grate (loco-info.com); its blower the S060's and the 8F's, its grate
                // between theirs (inferred), natural draft by grate area. Vacuum braked (Wikipedia): its pump stands
                // in for the ejector, as the 8F's does.
                Blower = new BlowerSpec(0.4f, 0.19f, AirPump.NineAndHalf, 1),
                // The 4F's 175 psi (Wikipedia) is 13.1 bar absolute; its maker's 13.3 (178 psi), within 6 psi, is kept.
                // Ross pops from No. 4027 (Brassmasters, 2004); their size not found. Sized for its standing fire, about
                // 3.5 MW on its as-built boiler (BoilerFit), where the maker's 5 kg/s reseats 19 psi under its lift.
                SafetyValve = new SafetyValveSpec(13.3f, 2.5f),
                LowWaterGuard = BoilerWaterRig.Guard.FusiblePlug,
                // LMS Fowler 4F: a Schmidt (the Midland 3835 class's, Wikipedia), 246 to 252 sq ft on 1,158 (Wikipedia):
                // by its surface over the evaporative, between the 8F's 100 °C on 215/1,650 and the S282's 136 on
                // 882/3,783, about 130 °C at the full rate (inferred).
                Superheater = Superheat("Schmidt", 130f, 0.72f),
                SteamPath = SteamPathProfile.BeforeSuperheater(130f),
                // LMS 4F snifting valves at the smokebox's foot, likely (BR later took many off; when, unknown).
                Drifting = DriftingProfile.Prototype(snifting: true),
                Injector = new InjectorProfile
                {
                    // LMS Fowler 4F: two Gresham & Craven live-steam injectors, about No. 9-10 (Derby drawings 9756,
                    // "No 10 injector - Gresham & Craven", Jan 1917, beside the Class 4 freight's "Pipe & rod
                    // arrangement"; LMS DD 3307, "Injector No. 9 cones for standard 0-6-0 No. 4 class freight engine"),
                    // non-lifting below the footplate as the 8F's (Midland practice, drawing 8103, "Arrangement of
                    // injectors under footplate", 1913; cab's water valves on rods down through its floor);
                    // "frequently temperamental starters" with "little range of delivery" (Powell, Living with L.M.S.
                    // Locomotives, 1977, ch. 4; steam valves on backplate too). Their delivery not found: about
                    // 2.7 kg/s each, a Sellers No. 8½'s 2,587 gal/h at 200 psi (Swingle p.348), above the boiler's
                    // most as the 8F's are (inferred). The game's is 7 together.
                    // Two-valve graph (LMS 4F 1.1.1): left-hand drive cab (reverser on left), as the LMS engines
                    // were from No. 4207 (Brassmasters, MR/LMS/BR 4F prototype notes, 2004): fireman's (right)
                    // 'injector2', driver's (left) 'injector1', as on the 8F.
                    PeriodDelivery = 5.4f,
                    HasSteamValves = true,
                    OwnLeverIsSteam = true,
                    TwoValveGraph = EmeraldTwoValveGraph,
                },
                Firehole = new FireholeSeat(0.000f, 1.946f, 0.423f, 0.432f, -2.773f),
                Shed = new ShedProfile
                {
                    // No British house blower connection, by the same rule as the 8F.
                    HasBlowerConnection = false,
                    WaterSeat = new ShedSeat(new Vector3(-0.8f, 1.41f, -1.52f)), 
                },
                Cab = new CabProfile
                {
                    AshpanPull = new Vector3(-0.92f, 0.79f, -3.45f), 
                    DropGrateOffset = new Vector3(0f, 0f, -0.3f), 
                    WaterRegulatorInPlace = new CabWaterRegulators.Levers
                    {
                        Left = new Vector3(-1.0425f, 2.115f, -3.6905f),
                        Right = new Vector3(1.0425f, 2.405f, -3.6905f),
                        Reach = 0.115f,
                        Turn = 150f,
                    },
                },
            },

            // LittleNiko36's S2102-870 (Nexus 1779, 1.0.0), a China Railways QJ 2-10-2 with CCL's steam stoker,
            // read from its bundle 2026-10-19. S2102-870B is its tender.
            new LocomotiveProfile
            {
                Id = QjStokerCab.Livery,
                Liveries = new[] { QjStokerCab.Livery },
                CoalFirebox = true,
                // China Railways QJ, "up to 95 kg steam per square meter and hour" on 255.3 m² (Hans Schaefer's QJ pages; Datong's figures).
                PrototypeEvaporation = 95f * 255.3f / 3600f,
                Firebox = new FireboxCharacters.Firebox(
                    "the QJ's box: 1,200 mm chamber, arch tubes; inclined; ashy Chinese coal; the C-3 stoker leaves the back corners short",
                    new GrateCharacter { ClinkerFactor = 1.2f, AshFactor = 1.5f })
                {
                    PrototypeGrateArea = 6.8f,
                    PrototypeSlope = -5f,
                    StokerBackLanes = 0.6f,
                },
                // China Railways QJ: a 6.8 m² (73.2 sq ft) grate; its blower between the S282's and the Hudson's by
                // grate area, natural draft by grate area (both inferred). One steam cross-compound pump low on the
                // right of the smokebox, its exhaust into the blast pipe from No. 6243 (the LV's МТЗ pump, 3,100 L/min,
                // close to the 8½-in.'s); the maker's 1,000 L reservoir, the LV's two 500 L.
                Blower = new BlowerSpec(0.6f, 0.66f, AirPump.CrossCompound, 1),
                SafetyValve = new SafetyValveSpec(17.3f, 7.5f),
                // China Railways QJ: fusible plugs in the crown sheet (three, by an ex-crewman; the LV it came from had two).
                LowWaterGuard = BoilerWaterRig.Guard.FusiblePlug,
                // China Railways QJ: Schmidt Type A, 69 elements, 144.1 m² on 255.3; steam "close to 400 °C" at its
                // design rate (the works book), about 195 °C over saturation at 15 kgf/cm².
                Superheater = Superheat("Type A", 195f, 0.75f),
                SteamPath = SteamPathProfile.BeforeSuperheater(500f),
                Drifting = DriftingProfile.Prototype(byPass: true),
                // China Railways QJ: a retired Chinese engine crewman's account of big five-driver engines gives a
                // floor pedal, an air cylinder above two doors that part sideways, and the doors falling shut on
                // letting go (Zhihu); inferred, as the account names no class.
                AutoFireDoor = AutoFireDoorMode.Prototype,
                // China Railways QJ: an open heater on part of the exhaust, its mixer at the top front of the boiler
                // (a Worthington SA copy, by one account), and a piston hot-water pump under the smokebox that the
                // fireman works by its own valve; it ran at about 400 L/min in service (Schaefer). Its capacity not
                // found: the SA's 9,000 gal/h, above that and the boiler's tested 8 kg/s (inferred).
                Feedwater = new FeedwaterFit.Fit(FeedHeaterType.Open, "open heater (Worthington SA type)", 9.46f, onInjectorLever: false),
                Injector = new InjectorProfile
                {
                    // China Railways QJ: the cab's one lever is the driver's, on the left, the large FX16.5 non-lifting
                    // injector under his seat (the fireman's small X10 lifter is not in the model). Its delivery not
                    // found: by its 16.5 against the JK3's 8.5 mm, 2.5 kg/s scaled by the area, about 9.4 kg/s (inferred).
                    PeriodDelivery = 9.4f,
                    HasSteamValves = true,
                },
                Firehole = new FireholeSeat(0.000f, 2.407f, 0.650f, 0.350f, -0.358f),
                Shed = new ShedProfile
                {
                    BlowerSeat = new ShedSeat(new Vector3(-1.25f, 2.11f, 11.69f), 0f, -15f, 0f, 0f), 
                    WaterSeat = new ShedSeat(QjWaterMouth), 
                    BlowerHint = "low on the left side of the smokebox, tilted upward",
                    WaterHint = "on the left at the plain gray spigot ahead of the cab",
                },
                Cab = new CabProfile
                {
                    AshpanPull = new Vector3(-1.4f, 1.53f, -0.11f), 
                    DropGrateOffset = new Vector3(0.95f, -0.17f, -0.3f), 
                    FeedPumpSpot = new FeedPumpCab.Spot(new Vector2(0.26f, -0.28f), new Vector2(0.26f, -0.14f)),
                    InjectorSteamSeat = (new Vector3(-0.2f, 0f, 0f), StandardInjectorTurns), 
                    StokerManifold = new StokerCab.Placement(new Vector3(0f, 10f, -4f), 0f, 0f),
                    StokerLeverMove = new Vector3(-0.38f, 0f, 0f), 
                    QjStokerSeat = new Vector3(0.12f, 0.17f, 0.06f),
                },
            },

            // E_noob's DB BR95 (Nexus 1792, 1.0.0), a Prussian T 20 2-10-2 tank engine, hand fired from its bunker,
            // read from its bundle 2026-10-24.
            new LocomotiveProfile
            {
                Id = "S2102-720",
                Liveries = new[] { "S2102-720" },
                CoalFirebox = true,
                // Prussian T 20 No. 77 001's best on the EZA's 1923 runs, 75 kg of water per m² of heating surface and
                // hour on 200 m², a light train up 1:10; the heavy trains 44 to 53 (Nordmann, Organ 1924, p.100).
                PrototypeEvaporation = 75f * 200f / 3600f,
                Firebox = new FireboxCharacters.Firebox(
                    "the T 20's box: a copper Belpaire over the frames, hand fired; coal of varying quality",
                    new GrateCharacter { ClinkerFactor = 1.1f, AshFactor = 1.2f }),
                // Prussian T 20: a 4.36 m² (46.9 sq ft) grate; its blower between the 8F's and the S282's by grate area,
                // natural draft by grate area (both inferred). "Two two-stage air pumps" front right on the smokebox,
                // for the rack lines' air (Hanomag-Lehrblatt 081, 1924); their capacity not found, each taken as a
                // 9½-in. (inferred); the maker's reservoir.
                Blower = new BlowerSpec(0.45f, 0.42f, AirPump.NineAndHalf, 2),
                // The T 20's 14 at gauge (kgf/cm²) is 14.7 bar absolute; its maker's 15.0, within 6 psi, is kept.
                // Sized for its standing fire, 8.4 MW on its prototype's boiler, where the maker's 15 kg/s popped.
                SafetyValve = new SafetyValveSpec(15.0f, 5.5f),
                // Prussian T 20: none named in the 1924 descriptions; German rules had one or more fusible plugs in the crown sheet.
                LowWaterGuard = BoilerWaterRig.Guard.FusiblePlug,
                // Prussian T 20: 34 flues, 62.5 m² on 198.8; the superheater's steam about 300 to 350 °C on the EZA's
                // 1923 runs (Nordmann, Organ 1924), about 150 °C over saturation at 14 at. A Schmidt, as the Prussian
                // engines' (inferred).
                Superheater = Superheat("Schmidt", 150f, 0.72f),
                // The DR class 95's automatic pressure equalizers (Druckausgleich), verified; their type unknown.
                Drifting = DriftingProfile.Prototype(byPass: true),
                // Prussian T 20 as built: "links Kolbenspeisewasserpumpe Knorr, Abdampfvorwärmer Knorr"
                // (Hanomag-Lehrblatt 131, 1924), a surface heater on the exhaust and a piston pump on the smokebox, set
                // from the cab by its own handwheel and gauge (Lehrblatt 081). Its capacity not found: the injector's
                // 250 L/min, as German practice sized each feed for the whole boiler (inferred).
                Feedwater = new FeedwaterFit.Fit(FeedHeaterType.Closed, "Knorr surface heater and pump", 4.17f, onInjectorLever: false),
                Injector = new InjectorProfile
                {
                    // Prussian T 20: "Rechts Dampfstrahlpumpe 250 l/Min." (Hanomag-Lehrblatt 131, 1924), worked by a
                    // pull rod (Lehrblatt 081); the cab's one lever. The game's is 20.
                    PeriodDelivery = 4.17f,
                    HasSteamValves = true,
                },
                Firehole = new FireholeSeat(0.000f, 2.312f, 0.763f, 0.517f, -3.550f),
                Shed = new ShedProfile
                {
                    BlowerSeat = new ShedSeat(new Vector3(-1f, 2.21f, 4.85f)), 
                    WaterSeat = new ShedSeat(new Vector3(-1.28f, 1.24f, -3.36f), 0f, 90f, 0f, 0f), 
                },
                Cab = new CabProfile
                {
                    AshpanPull = new Vector3(-1.46f, 1.05f, -3.9f), 
                    DropGrateOffset = new Vector3(0f, 0f, -0.3f), 
                    FeedPumpSpot = new FeedPumpCab.Spot(Vector2.zero, new Vector2(0f, 0.12f), new Vector3(-0.8f, 3.11f, -3.53f)),
                    InjectorSteamSeat = (new Vector3(0.09f, -0.17f, 0f), StandardInjectorTurns), 
                    PlainFireboxWallTone = new Color(0.30f, 0.24f, 0.20f),
                },
            },


            // My Name Is BorING's 1067 mm narrow-gauge 2-6-0 (Nexus 1349, 1.0.5; S260N-580B is its tender).
            new LocomotiveProfile
            {
                Id = "S260N-580A",
                Liveries = new[] { "S260N-580A" },
                CoalFirebox = true,
                Firebox = BoringNarrowGaugeFirebox,
                Blower = new BlowerSpec(0.06f, 0.04f, AirPump.NineAndHalf, 1),
                // Maker's 12.0 bar lift (159 psi; SAR X class worked at 130), sized for 0.82 MW standing fire on the
                // maker's grate (FireModel, tended box, door shut, blower wide open; BoringNarrowGaugeChecks), where
                // the maker's 2 kg/s popped.
                SafetyValve = new SafetyValveSpec(12.0f, 1.0f),
                LowWaterGuard = BoilerWaterRig.Guard.FusiblePlug,
                TakesStockSteam = true,
                Superheater = SuperheaterSpec.None,
                SteamPath = SteamPathProfile.Saturated,
                Injector = new InjectorProfile
                {
                    // Feeds the 1.08 kg/s flat-out fire with room to spare (inferred). The game's is 5.
                    PeriodDelivery = 1.5f,
                    HasSteamValves = true,
                    Lifting = true,
                },
                Firehole = new FireholeSeat(-0.006f, 1.307f, 0.335f, 0.355f, -0.449f),
                Shed = new ShedProfile
                {
                    BlowerSeat = new ShedSeat(new Vector3(-0.23f, 1f, 4.25f)), 
                    WaterSeat = new ShedSeat(new Vector3(-0.41f, 0.93f, -0.35f)), 
                },
                Cab = new CabProfile
                {
                    AshpanPull = new Vector3(-0.79f, 0.8f, -0.03f), 
                    DropGrateOffset = new Vector3(0f, 0f, -0.3f), 
                    InjectorSteamSeat = (new Vector3(0f, 0f, 0f), StandardInjectorTurns), 
                },
            },

            // My Name Is BorING's 1067 mm narrow-gauge 0-4-0 saddle tank (Nexus 1366, 1.0.1; auxiliary water tender is not fired).
            new LocomotiveProfile
            {
                Id = "S040N-270",
                Liveries = new[] { "S040N-270" },
                CoalFirebox = true,
                Firebox = BoringNarrowGaugeFirebox,
                Blower = new BlowerSpec(0.04f, 0.03f, AirPump.NineAndHalf, 1),
                // Maker's 10.0 bar lift (130 psi; Porter's 7 x 12 worked at 140 to 170), sized for 0.53 MW standing
                // fire on the maker's grate (FireModel; BoringNarrowGaugeChecks), where the maker's 2 kg/s popped.
                SafetyValve = new SafetyValveSpec(10.0f, 0.5f),
                LowWaterGuard = BoilerWaterRig.Guard.FusiblePlug,
                TakesStockSteam = true,
                Superheater = SuperheaterSpec.None,
                SteamPath = SteamPathProfile.Saturated,
                Injector = new InjectorProfile
                {
                    // Feeds the 0.41 kg/s flat-out fire with room to spare (inferred). The game's is 4.
                    PeriodDelivery = 0.6f,
                    HasSteamValves = true,
                    Lifting = true,
                },
                Firehole = new FireholeSeat(-0.002f, 0.788f, 0.260f, 0.235f, -1.316f),
                Shed = new ShedProfile
                {
                    HasBlowerConnection = false,
                    HasWaterConnection = false,
                },
                Cab = new CabProfile
                {
                    AshpanPull = new Vector3(-0.63f, 0.53f, -1.26f), 
                    DropGrateOffset = new Vector3(0.72f, 0f, -0.3f), 
                    InjectorSteamSeat = (new Vector3(0f, 0f, 0f), StandardInjectorTurns), 
                },
            },

            // My Name Is BorING's 1067 mm narrow-gauge geared 0-4-4-0 (Nexus 1387, 1.0.1).
            new LocomotiveProfile
            {
                Id = "S0440NG-890",
                Liveries = new[] { "S0440NG-890" },
                CoalFirebox = true,
                Firebox = BoringNarrowGaugeFirebox,
                Blower = new BlowerSpec(0.11f, 0.07f, AirPump.NineAndHalf, 1),
                // Maker's 13.5 bar lift (181 psi; the Heisler worked at 160), sized for 1.49 MW standing fire on the
                // maker's grate (FireModel; BoringNarrowGaugeChecks), where the maker's 2 kg/s popped.
                SafetyValve = new SafetyValveSpec(13.5f, 1.0f),
                LowWaterGuard = BoilerWaterRig.Guard.FusiblePlug,
                TakesStockSteam = true,
                Superheater = SuperheaterSpec.None,
                SteamPath = SteamPathProfile.Saturated,
                Injector = new InjectorProfile
                {
                    // Feeds the 1.44 kg/s flat-out fire with room to spare (inferred). The game's is 12.
                    PeriodDelivery = 2f,
                    HasSteamValves = true,
                    Lifting = true,
                },
                Firehole = new FireholeSeat(0.004f, 1.404f, 0.450f, 0.428f, -1.144f),
                Shed = new ShedProfile
                {
                    BlowerSeat = new ShedSeat(new Vector3(-0.53f, 1.46f, 3.3f), 0f, -8f, 0f, 0f), 
                    WaterSeat = new ShedSeat(new Vector3(-1.24f, 1.05f, -0.45f), 0f, 90f, 0f, 0f), 
                },
                Cab = new CabProfile
                {
                    AshpanPull = new Vector3(-1.09f, 0.84f, -0.9f), 
                    DropGrateOffset = new Vector3(0.88f, 0f, -0.05f), 
                    InjectorSteamSeat = (new Vector3(0f, 0f, 0f), StandardInjectorTurns), 
                },
            },

            // My Name Is BorING's 1067 mm narrow-gauge cab-forward 4-4-0 (Nexus 1417, 1.0.0; S440NCF-400B is its water tender).
            // Engines fired from the front: its door at the forward end of its box, the fireman facing the rear,
            // in the firing frame turned half about (FiringFrame).
            new LocomotiveProfile
            {
                Id = "S440NCF-400A",
                Liveries = new[] { "S440NCF-400A" },
                CoalFirebox = true,
                ReversedFiringFrame = true,
                Firebox = BoringNarrowGaugeFirebox,
                Blower = new BlowerSpec(0.11f, 0.07f, AirPump.NineAndHalf, 1),
                // Maker's 11.5 bar lift (152 psi; NPC No. 21 worked at 140, the FS 670 at 15 kgf/cm²),
                // sized for its standing fire, about 1.4 MW on his grate of 0.77 m².
                SafetyValve = new SafetyValveSpec(11.5f, 1.0f),
                LowWaterGuard = BoilerWaterRig.Guard.FusiblePlug,
                TakesStockSteam = true,
                Superheater = SuperheaterSpec.None,
                SteamPath = SteamPathProfile.Saturated,
                Injector = new InjectorProfile
                {
                    // Fire makes 1.29 kg/s flat out on its maker's grate at his throttle's most (FireModel); the FS 670
                    // had at least two injectors, fed by hoses from its tank wagon, their make not found (inferred). The game's is 6.
                    PeriodDelivery = 1.8f,
                    HasSteamValves = true,
                    Lifting = true,
                },
                Firehole = new FireholeSeat(0.001f, 1.183f, 0.450f, 0.324f, -1.408f),
                Shed = new ShedProfile
                {
                    HasBlowerConnection = false,
                    WaterSeat = new ShedSeat(new Vector3(-0.61f, 1.04f, 0.09f)), 
                },
                Cab = new CabProfile
                {
                    AshpanPull = new Vector3(-0.92f, 0.86f, 0.4f), 
                    DropGrateOffset = new Vector3(0f, 0f, 0.3f), 
                    InjectorSteamSeat = (new Vector3(0.02f, -0.01f, 0.11f), new Vector3(-360f, 0f, 2f)),
                    InjectorSteamSideSeat = new Vector3(0.07f, -0.01f, 0.15f),
                },
            },

            // Santa Fe 2901 oil burner (listed/checked without coal firebox; fired by RealisticOilFirebox).
            new LocomotiveProfile
            {
                Id = "A.T.&S.F. 2901-A",
                Liveries = new[] { "A.T.&S.F. 2901-A" },
                CoalFirebox = false,
                // Santa Fe 3766, 82,000 lb/h (AT&SF Test Report 87312, in Stagner, Trains, Feb 1987); no 2900 was tested.
                PrototypeEvaporation = 82000f * LbPerHour,
                // Two 8 1/2-in. cross-compounds on the Santa Fe 4-8-4s (the 1941 order's, RME Sept 1942 p.371), inferred.
                Blower = new BlowerSpec(0.85f, 1.0f, AirPump.CrossCompound, 2),
                // Maker's 297 psi lift kept (within ICC's 6 psi of 300 psi).
                SafetyValve = new SafetyValveSpec(21.5f, 8f),
                LowWaterGuard = BoilerWaterRig.Guard.None,
                // AT&SF 2900: 220 Type E flues, 2,366 sq ft on 5,312 (Locobase).
                Superheater = Superheat("Type E", 180f, 0.82f),
                SteamPath = SteamPathProfile.AfterSuperheater(800f),
                // The Santa Fe 2900 class's Wagner by-pass valves (called drifting valves, but admitting no steam), verified for the class.
                Drifting = DriftingProfile.Prototype(byPass: true),
                // Santa Fe 2900 class: "a Worthington Type 6-SA feed water heater" (No. 2921; 6-SA of 12,000 gal/h,
                // Railway Age, 2 June 1945 p.974). Cab has an injector handwheel each side; as on the NYC, the
                // fireman's works the pump and the engineman's the injector.
                Feedwater = new FeedwaterFit.Fit(FeedHeaterType.Open, "Worthington 6-SA", 12.6f, onInjectorLever: true),
                Injector = new InjectorProfile
                {
                    // Santa Fe 2900 class: no injector found. Its boiler boils about 12 kg/s in the game; a 12,000 gal/h
                    // injector, as the N&W J paired a 12,000 gal/h Nathan with its Worthington 6-SA (Railway Age,
                    // 2 June 1945 p.974), rather than the road's bigger 2-10-4s' 13,500 (RME Feb 1939 p.51).
                    // Inferred, low confidence. The game's is 35.
                    PeriodDelivery = 12.6f,
                    HasSteamValves = true,
                },
                Shed = new ShedProfile
                {
                    BlowerSeat = new ShedSeat(new Vector3(-0.64f, 2.31f, 8.08f), 0f, 79f, 0f, 0f),
                    WaterSeat = new ShedSeat(new Vector3(-1.22f, 2.03f, -7.98f)),
                    BlowerHint = "low on the left side of the smokebox",
                    WaterHint = "on the left below the cab, at the rear of the boiler",
                },
                Cab = new CabProfile
                {
                    InjectorDummyWheel = ("Injector 2", 0.1f),
                    OilBurnerValveSeat = new Vector3(-0.36f, 0.03f, 0f),
                    OilBurnerLooseWheels = new Dictionary<string, string>
                    {
                        [OilBurnerFit.Atomizer.Id] = "HW.014",
                        [OilBurnerFit.Heater.Id] = "HW.017",
                        [OilBurnerFit.ShopSteam.Id] = "HW.015",
                    },
                },
            },

            // GWI 2.0.0 engines (listed/checked without coal firebox): limited five-notch injector lever reaching
            // 0, .25, .5, .75 and 1. Its first opening must be able to prime (CrackOpening = 0.25); keep every
            // other fit's threshold and the maker's rate/pressure.
            new LocomotiveProfile
            {
                Id = "GWI40xx",
                Liveries = new[] { "GWI40xx", "GWI_Manor" },
                CoalFirebox = false,
                Injector = new InjectorProfile
                {
                    CrackOpening = 0.25f,
                },
            },
        };

        /// <summary>Tenders with per-livery fitting metadata.</summary>
        internal static readonly IReadOnlyList<TenderProfile> Tenders = new[]
        {
            new TenderProfile
            {
                Liveries = new[] { "em_lms_stanier_tender_1_black", "em_lms_stanier_tender_1_grey" },
                HasWaterValves = true,
                WaterValveHandles = new[] { "Trimmer Left", "Trimmer Right" },
                WaterValveTravel = 0.13f,
            },
        };

        private static readonly Dictionary<string, LocomotiveProfile> ByLivery = BuildLiveryIndex();
        private static readonly Dictionary<string, TenderProfile> TendersByLivery = BuildTenderIndex();

        /// <summary>All supported coal-fired livery IDs.</summary>
        internal static readonly IReadOnlyCollection<string> CoalLiveries = new HashSet<string>(
            All.Where(p => p.CoalFirebox).SelectMany(p => p.Liveries));

        /// <summary>All listed locomotive livery IDs (coal-fired, oil-fired, and checked).</summary>
        internal static readonly IReadOnlyCollection<string> ListedLiveries = new HashSet<string>(ByLivery.Keys);

        private static Dictionary<string, LocomotiveProfile> BuildLiveryIndex()
        {
            var map = new Dictionary<string, LocomotiveProfile>();
            foreach (var profile in All)
            {
                foreach (var livery in profile.Liveries)
                    map.Add(livery, profile);
            }
            return map;
        }

        private static Dictionary<string, TenderProfile> BuildTenderIndex()
        {
            var map = new Dictionary<string, TenderProfile>();
            foreach (var tender in Tenders)
            {
                foreach (var livery in tender.Liveries)
                    map.Add(livery, tender);
            }
            return map;
        }

        /// <summary>Whether <paramref name="livery"/> is registered in this mod.</summary>
        internal static bool Contains(string livery) => livery != null && ByLivery.ContainsKey(livery);

        /// <summary>Looks up the profile for <paramref name="livery"/>.</summary>
        internal static bool TryGet(string livery, out LocomotiveProfile profile)
        {
            profile = null;
            return livery != null && ByLivery.TryGetValue(livery, out profile);
        }

        /// <summary>Looks up the profile for <paramref name="car"/>.</summary>
        internal static bool TryGet(TrainCar car, out LocomotiveProfile profile) =>
            TryGet(car != null && car.carLivery != null ? car.carLivery.id : null, out profile);

        /// <summary>The profile for <paramref name="livery"/>, or null when unlisted.</summary>
        internal static LocomotiveProfile For(string livery) => TryGet(livery, out var profile) ? profile : null;

        /// <summary>The profile for <paramref name="car"/>, or null when unlisted.</summary>
        internal static LocomotiveProfile For(TrainCar car) => TryGet(car, out var profile) ? profile : null;

        /// <summary>Looks up tender metadata for <paramref name="livery"/>.</summary>
        internal static bool TryGetTender(string livery, out TenderProfile tender)
        {
            tender = null;
            return livery != null && TendersByLivery.TryGetValue(livery, out tender);
        }
    }
}
