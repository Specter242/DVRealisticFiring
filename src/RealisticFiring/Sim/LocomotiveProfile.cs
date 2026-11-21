using System.Collections.Generic;
using RealisticFiring.Core;
using UnityEngine;

namespace RealisticFiring.Sim
{
    /// <summary>A blower and air pump fit for a locomotive.</summary>
    internal readonly struct BlowerSpec
    {
        public BlowerSpec(float maxBlower, float natural, AirPump pump, int pumps, float mainReservoir = 0f)
        {
            MaxBlower = maxBlower;
            Natural = natural;
            Pump = pump;
            Pumps = pumps;
            MainReservoir = mainReservoir;
        }

        public float MaxBlower { get; }
        public float Natural { get; }
        public AirPump Pump { get; }
        public int Pumps { get; }
        public float MainReservoir { get; }
    }

    /// <summary>A safety valve fit: lift in absolute bar and vent capacity in kg/s.</summary>
    internal readonly struct SafetyValveSpec
    {
        public SafetyValveSpec(float lift, float capacity)
        {
            Lift = lift;
            Capacity = capacity;
        }

        public float Lift { get; }
        public float Capacity { get; }
    }

    /// <summary>How an engine's throttle and superheater steam path are laid out.</summary>
    internal enum SteamPathMode
    {
        None,
        Saturated,
        BeforeSuperheater,
        AfterSuperheater,
    }

    /// <summary>Per-locomotive steam path mode and inferred added circuit volume, L.</summary>
    internal readonly struct SteamPathProfile
    {
        public SteamPathProfile(SteamPathMode mode, float pipeVolume = 0f)
        {
            Mode = mode;
            PipeVolume = pipeVolume;
        }

        public static readonly SteamPathProfile Saturated = new SteamPathProfile(SteamPathMode.Saturated);
        public static SteamPathProfile BeforeSuperheater(float pipeVolume) => new SteamPathProfile(SteamPathMode.BeforeSuperheater, pipeVolume);
        public static SteamPathProfile AfterSuperheater(float pipeVolume) => new SteamPathProfile(SteamPathMode.AfterSuperheater, pipeVolume);

        public SteamPathMode Mode { get; }
        public float PipeVolume { get; }
    }

    /// <summary>How an engine carries drifting fittings: none, career upgrades, or prototype equipment.</summary>
    internal readonly struct DriftingProfile
    {
        public static readonly DriftingProfile None = default;
        public static readonly DriftingProfile Upgradable = new DriftingProfile(upgradable: true, snifting: false, valve: false, byPass: false);

        public static DriftingProfile Prototype(bool snifting = false, bool valve = false, bool byPass = false) =>
            new DriftingProfile(upgradable: false, snifting: snifting, valve: valve, byPass: byPass);

        private DriftingProfile(bool upgradable, bool snifting, bool valve, bool byPass)
        {
            IsUpgradable = upgradable;
            PrototypeSnifting = snifting;
            PrototypeValve = valve;
            PrototypeByPass = byPass;
        }

        public bool IsUpgradable { get; }
        public bool PrototypeSnifting { get; }
        public bool PrototypeValve { get; }
        public bool PrototypeByPass { get; }
        public bool HasPrototype => PrototypeSnifting || PrototypeValve || PrototypeByPass;
    }

    /// <summary>How an engine carries an automatic fire door.</summary>
    internal enum AutoFireDoorMode
    {
        None,
        Upgradable,
        Prototype,
    }

    /// <summary>
    /// Cabs with two injector valves feeding the boiler's one injector port
    /// through a calculated average, as CCL cannot wire two: each graph
    /// verified exactly, so no arbitrary calculated or mechanical feed is
    /// taken for cab injectors, and no ports or control save data change.
    /// Right is the first injector, left the second; the adder's inputs
    /// are named as each graph wires them.
    /// </summary>
    internal sealed class TwoValveInjectorGraph
    {
        public string Add, AddA, AddB, Multiplier, MultiplierOut, Factor, FactorPort, Right, Left;
    }

    /// <summary>Per-locomotive injector sizing and valve configuration.</summary>
    internal sealed class InjectorProfile
    {
        /// <summary>Prototype injector delivery at rated pressure, kg/s (US gal/h × 3.785 / 3600).</summary>
        public float? PeriodDelivery { get; set; }

        /// <summary>Whether one cab lever stands for the prototype's two injectors.</summary>
        public bool OneLeverForTwo { get; set; }

        /// <summary>Custom crack opening threshold where a notched cab lever needs it (GWI 2.0.0).</summary>
        public float? CrackOpening { get; set; }

        /// <summary>Verified two-valve averaging graph, where the cab works two injectors through one boiler port.</summary>
        public TwoValveInjectorGraph TwoValveGraph { get; set; }

        /// <summary>Whether this locomotive is fitted with separate injector steam/water valves.</summary>
        public bool HasSteamValves { get; set; }

        /// <summary>Whether the locomotive's injector lifts water rather than being non-lifting.</summary>
        public bool Lifting { get; set; }

        /// <summary>Whether the cab's own injector levers are the steam valves and the added controls the water regulators.</summary>
        public bool OwnLeverIsSteam { get; set; }
    }

    /// <summary>Firehole opening geometry in the firing frame, m.</summary>
    internal readonly struct FireholeSeat
    {
        public FireholeSeat(float middle, float foot, float width, float height, float plane)
        {
            Middle = middle;
            Foot = foot;
            Width = width;
            Height = height;
            Plane = plane;
        }

        public float Middle { get; }
        public float Foot { get; }
        public float Width { get; }
        public float Height { get; }
        public float Plane { get; }

        public (float Middle, float Foot, float Width, float Height, float Plane) AsTuple() => (Middle, Foot, Width, Height, Plane);
    }

    /// <summary>A shed hose connection seat in the car frame.</summary>
    internal readonly struct ShedSeat
    {
        public ShedSeat(Vector3 at, float swing = 0f, float down = 0f, float spin = 0f, float @out = 0f)
        {
            At = at;
            Swing = swing;
            Down = down;
            Spin = spin;
            Out = @out;
        }

        public Vector3 At { get; }
        public float Swing { get; }
        public float Down { get; }
        public float Spin { get; }
        public float Out { get; }

        public (Vector3 At, float Swing, float Down, float Spin, float Out) AsTuple() => (At, Swing, Down, Spin, Out);
    }

    /// <summary>Shed blower and water hose connections and hints for a locomotive.</summary>
    internal sealed class ShedProfile
    {
        public bool HasBlowerConnection { get; set; } = true;
        public bool HasWaterConnection { get; set; } = true;
        public bool BlowerRightSide { get; set; }
        public ShedSeat? BlowerSeat { get; set; }
        public ShedSeat? WaterSeat { get; set; }
        public string BlowerHint { get; set; }
        public string WaterHint { get; set; }
    }

    /// <summary>A brick arch's shape in the car frame, m and degrees.</summary>
    internal readonly struct BrickArchShape
    {
        public BrickArchShape(float footAlong, float footUp, float lipAlong, float slope, int tubes)
        {
            FootAlong = footAlong;
            FootUp = footUp;
            LipAlong = lipAlong;
            Slope = slope;
            Tubes = tubes;
        }

        public float FootAlong { get; }
        public float FootUp { get; }
        public float LipAlong { get; }
        public float Slope { get; }
        public int Tubes { get; }
    }

    /// <summary>Cab and exterior visual placements for a locomotive.</summary>
    internal sealed class CabProfile
    {
        public Vector3? AshpanPull { get; set; }
        public bool AshpanNoHanger { get; set; }
        public Vector3? DropGrateOffset { get; set; }
        public Visuals.FeedPumpCab.Spot? FeedPumpSpot { get; set; }
        public (Vector3 Move, Vector3 Turns)? InjectorSteamSeat { get; set; }
        public Vector3? InjectorSteamFixedSeat { get; set; }
        public Vector3? InjectorSteamSideSeat { get; set; }
        public (string Name, float PlateAft)? InjectorDummyWheel { get; set; }
        public (Vector2 Right, Vector2 Left)? InjectorWaterSeats { get; set; }
        public (string Mesh, float RightTurn, float LeftTurn)? WaterRegulatorMesh { get; set; }
        public Visuals.CabWaterRegulators.Levers WaterRegulatorInPlace { get; set; }
        public Visuals.StokerCab.Placement? StokerManifold { get; set; }
        public Vector3? StokerLeverMove { get; set; }
        public string StokerAnchorPort { get; set; }
        public string StokerValveCopyPath { get; set; }
        public float? StokerSupplyPipeAt { get; set; }
        public Vector3? QjStokerSeat { get; set; }
        public Vector3? BoosterPlacement { get; set; }
        public Vector3? BoosterCarSeat { get; set; }
        public Visuals.CabValve.Seat? JetsValveSeat { get; set; }
        public bool JetsValveOwnHandle { get; set; }
        public Visuals.CabValve.Seat? DriftingValveSeat { get; set; }
        public bool DriftingValveOwnHandle { get; set; }
        public Vector2? DriftSoundChest { get; set; }
        public BrickArchShape? BrickArch { get; set; }
        public Color? PlainFireboxWallTone { get; set; }
        public float FireboxLightGain { get; set; } = 1f;
        public Vector3? OilBurnerValveSeat { get; set; }
        public IReadOnlyDictionary<string, string> OilBurnerLooseWheels { get; set; }
    }

    /// <summary>
    /// Authoritative simulation, fitting, shed, and cab specification for one
    /// supported steam locomotive (possibly shared across multiple livery IDs).
    /// </summary>
    internal sealed class LocomotiveProfile
    {
        public string Id { get; set; }
        public string[] Liveries { get; set; }

        /// <summary>Whether the mod replaces this locomotive's coal firebox.</summary>
        public bool CoalFirebox { get; set; }

        /// <summary>Whether this locomotive is fired from the front (its firing frame turned half about).</summary>
        public bool ReversedFiringFrame { get; set; }

        /// <summary>Prototype maximum actual evaporation, kg/s, when the boiler is sized to its prototype.</summary>
        public float? PrototypeEvaporation { get; set; }

        public FireboxCharacters.Firebox Firebox { get; set; }
        public BlowerSpec? Blower { get; set; }
        public SafetyValveSpec? SafetyValve { get; set; }
        public BoilerWaterRig.Guard LowWaterGuard { get; set; }
        public TryCockRig.Fit? TryCocks { get; set; }

        /// <summary>Whether the cylinders take the stock engine's steam a stroke (boiler balanced for stock steam).</summary>
        public bool TakesStockSteam { get; set; }

        /// <summary>Whether the engine keeps its steam as it was: no superheater and no wet walls.</summary>
        public bool SuperheaterExempt { get; set; }

        /// <summary>Prototype superheater spec; null uses the default Type A.</summary>
        public SuperheaterSpec Superheater { get; set; }

        public SteamPathProfile SteamPath { get; set; }
        public DriftingProfile Drifting { get; set; }
        public AutoFireDoorMode AutoFireDoor { get; set; }
        public BoosterFit.Fit Booster { get; set; }
        public FeedwaterFit.Fit Feedwater { get; set; }

        public InjectorProfile Injector { get; set; } = new InjectorProfile();
        public FireholeSeat? Firehole { get; set; }
        public ShedProfile Shed { get; set; } = new ShedProfile();
        public CabProfile Cab { get; set; } = new CabProfile();

        /// <summary>Optional chimney geometry hint (lip height, bore, outside radius) forwarded to Realistic Stack Effects API v3.</summary>
        public RealisticStackEffects.Core.StackGeometryHint? StackGeometry { get; set; }
    }

    /// <summary>Per-tender metadata for tenders with working water valve handles.</summary>
    internal sealed class TenderProfile
    {
        public string[] Liveries { get; set; }
        public bool HasWaterValves { get; set; }
        public string[] WaterValveHandles { get; set; }
        public float WaterValveTravel { get; set; }
    }
}
