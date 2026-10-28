using System;

namespace RealisticFiring.Core
{
    /// <summary>What the fireman is asked to do: a button each on his overlay.</summary>
    public enum FiremanMode
    {
        /// <summary>He does nothing and leaves every control where it is.</summary>
        Off,

        /// <summary>A thin fire, the water in the glass, about half pressure: for standing a long while.</summary>
        KeepAlive,

        /// <summary>A fire for stop-start work, the pressure kept clear of the safety valve.</summary>
        Shunting,

        /// <summary>The pressure just under the safety valve, fired ahead of the work.</summary>
        Road,
    }

    /// <summary>What the engineer tells the fireman, from the overlay's buttons.</summary>
    public enum FiremanCall
    {
        None,

        /// <summary>A pull is coming: build the fire and the water.</summary>
        HeavyAhead,

        /// <summary>Shutting off or stopping soon: let the fire burn down and fill the boiler.</summary>
        EasingOff,
    }

    /// <summary>What the fireman tells the player: things he cannot do himself.</summary>
    [Flags]
    public enum FiremanNotice
    {
        None = 0,

        /// <summary>Clinker is building: clean the fire at the next stop.</summary>
        CleanFire = 1,

        /// <summary>The ashpan is half full.</summary>
        EmptyAshpan = 2,

        /// <summary>The water is out of sight at the bottom of the glass.</summary>
        LowWater = 4,

        /// <summary>The tender has no water: he stops firing.</summary>
        TenderWaterOut = 8,

        /// <summary>The tender has no coal, or no oil.</summary>
        TenderCoalOut = 16,

        /// <summary>The fire is out: he cannot light it.</summary>
        FireOut = 32,

        /// <summary>The oil is too cold to run: he is heating it.</summary>
        ColdOil = 64,

        /// <summary>The flues are sooted and the sand box is empty.</summary>
        NoSand = 128,
    }

    /// <summary>What the fireman sees and hears each tick, besides the fire.</summary>
    public struct FiremanReadings
    {
        /// <summary>Boiler pressure, absolute bar.</summary>
        public float Pressure;

        /// <summary>Where the safety valve lifts, absolute bar.</summary>
        public float SafetyValve;

        /// <summary>
        /// Where the boiler would burst, absolute bar: under the safety valve
        /// when the body is badly damaged. 0 when it is not known, infinite
        /// when it cannot burst.
        /// </summary>
        public float BurstPressure;

        /// <summary>
        /// How hard the engine works, as he hears it in the exhaust's beats:
        /// 0 silent to about 1 at its rated steam. It is all he knows of the
        /// steam it draws; the gauge and the glass tell him how he is doing.
        /// </summary>
        public float Work;

        /// <summary>Whether he knows the road ahead (<see cref="WorkSoon"/> and <see cref="WorkAhead"/>).</summary>
        public bool RoadKnown;

        /// <summary>
        /// The work he expects in a minute or so, from the road ahead, as a
        /// share of full work: the hardest stretch the train will be on as
        /// fresh coal comes to burn. On the road he fires for it.
        /// </summary>
        public float WorkSoon;

        /// <summary>The engineer's throttle as he sees it, 0 shut to 1 wide open.</summary>
        public float Throttle;

        /// <summary>The reverser as he sees it, 0 in mid-gear to 1 in full gear either way.</summary>
        public float Cutoff;

        /// <summary>Whether the engineer has the brakes on.</summary>
        public bool Braking;

        /// <summary>
        /// The hardest work he expects over the next several minutes, from
        /// the road ahead, as a share of full work: long enough ahead to fill
        /// the boiler, which takes minutes where the fire takes one.
        /// </summary>
        public float WorkAhead;

        /// <summary>The steam the engine takes at full work, in kg/s, as he knows his engine.</summary>
        public float FullWorkSteam;

        /// <summary>Water from the bottom of the glass to its top, in kg, as he knows his boiler.</summary>
        public float GlassMass;

        /// <summary>Whether the train is stopped.</summary>
        public bool Standing;

        /// <summary>
        /// The game's Steam Startup speedup on the fire, 1 none: standing
        /// below its start-up pressure the fire burns up to 25 times as fast.
        /// 0 unknown, taken as 1.
        /// </summary>
        public float StartupMultiplier;

        /// <summary>
        /// Steam the auxiliaries take from the boiler, kg/s, as he hears them
        /// or set them: the air pump pounding, the dynamo, the blower as he has
        /// it open. Standing, they are the steam he fires for.
        /// </summary>
        public float AuxiliarySteam;

        /// <summary>The water in the glass, 0 at the crown sheet to 1 at the steam outlet.</summary>
        public float Glass;

        /// <summary>
        /// How much higher the glass reads than the water stands level, in
        /// glass shares, from the engine's tilt now: up with its front up
        /// (the water runs back to the glass at the backhead), down with it
        /// down. The game shifts the glass by the boiler's length over its
        /// diameter, halved, times the grade: about a fifth of the glass for
        /// 2 % on a long boiler.
        /// </summary>
        public float GlassTilt;

        /// <summary>
        /// The most and least <see cref="GlassTilt"/> on the road ahead within
        /// the water's lead (the steepest climb and descent the engine's front
        /// will meet), in glass shares; 0 where the road is not known.
        /// </summary>
        public float GlassTiltHigh;
        public float GlassTiltLow;

        /// <summary>Water in the tender, in kg.</summary>
        public float TenderWater;

        /// <summary>Fuel in the tender, coal or oil, in kg as it goes on the fire.</summary>
        public float TenderCoal;

        /// <summary>The injector's state and its lever's opening (its water valve, where it has a steam valve).</summary>
        public InjectorState Injector;
        public float InjectorLever;

        /// <summary>Whether the injector has its own three-position steam valve, and where that sits.</summary>
        public bool InjectorSteamValve;
        public float InjectorSteam;

        /// <summary>The lever opening that primes the injector (<see cref="InjectorSpec.CrackOpening"/>).</summary>
        public float InjectorCrack;

        /// <summary>The lowest pressure the injector picks up at, absolute bar.</summary>
        public float InjectorStartPressure;

        /// <summary>What the injector feeds wide open at this pressure, in kg/s.</summary>
        public float InjectorFullFeed;

        /// <summary>The least lever the injector keeps working at once it sings.</summary>
        public float InjectorLeast;

        /// <summary>Whether a feedwater pump is fitted, its state, and what it feeds wide open, in kg/s.</summary>
        public bool HasPump;
        public FeedPumpState Pump;
        public float PumpFullFeed;

        /// <summary>Whether a stoker is fitted, whether it is jammed (its engine stalls), and what it feeds flat out, in kg/s.</summary>
        public bool HasStoker;
        public bool StokerJammed;
        public float StokerFullFeed;
        /// <summary>Coal visibly collecting on the distributor; no knowledge of hidden moisture or packing.</summary>
        public bool StokerTableLoaded;
        /// <summary>The visible engine gauge, throttle, and running speed; the working gauge pressure is known for this stoker.</summary>
        public float StokerEnginePressure, StokerThrottle, StokerEngineSpeed, StokerWorkingPressure;
        public bool StokerRunningForward;

        /// <summary>Flue sand in the cab, in kg (oil burners).</summary>
        public float Sand;

        /// <summary>Solids in the boiler water, in ppm; 0 where nothing models them.</summary>
        public float Solids;

        /// <summary>Coal one shovelful puts on the fire, in kg.</summary>
        public float ShovelMass;
    }

    /// <summary>What the fireman does this tick.</summary>
    public struct FiremanActions
    {
        /// <summary>Coal to throw on the fire now, in kg; 0 for none.</summary>
        public float Shovelful;

        /// <summary>Where it goes.</summary>
        public GrateZone Aim;

        /// <summary>The firebox door, 0 shut to 1 open.</summary>
        public float Door;

        /// <summary>Rock the grate now.</summary>
        public ShakeStroke? Shake;

        /// <summary>The injector lever, 0 shut to 1 open (its water valve, where it has a steam valve).</summary>
        public float Injector;

        /// <summary>The injector's steam valve, where it has one: 0 shut, <see cref="InjectorSpec.SteamPrime"/>, or 1 wide open.</summary>
        public float InjectorSteam;

        /// <summary>The feed pump's operating valve, 0 shut to 1 open.</summary>
        public float Pump;

        /// <summary>The blower valve, 0 shut to 1 open.</summary>
        public float Blower;

        /// <summary>The blowdown valve, 0 shut to 1 open.</summary>
        public float Blowdown;

        /// <summary>The ashpan damper, 0 shut to 1 open.</summary>
        public float Damper;

        /// <summary>The stoker: its engine's throttle, its operating lever, and its jet valves, each 0 to 1.</summary>
        public float StokerThrottle;
        public StokerDirection StokerDirection;
        public float MasterJet;
        public float BackJets;
        public float FrontJets;
        public float FinesJet;
        public bool ClearStokerTable;

        /// <summary>The oil burner: its firing valve, atomizer, and oil heater, each 0 to 1, and flue sand to throw now, in kg.</summary>
        public float FiringValve;
        public float Atomizer;
        public float Heater;
        public float Sand;

        public FiremanNotice Notices;
    }
}
