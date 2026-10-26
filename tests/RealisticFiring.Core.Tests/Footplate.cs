using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

/// <summary>
/// An S282-like engine for the fireman to work: the real fire, shovel, and
/// injector, a simple boiler whose pressure follows the heat it gains, a
/// draft from the exhaust and blower, and the water's solids. The engine's
/// steam is scripted.
/// </summary>
internal sealed class Footplate
{
    public const float Dt = 0.05f;

    // Heat to raise the boiler a bar, in J: about 10 t of water at 4 °C a bar.
    private readonly float HeatPerBar;
    private readonly bool saturationCurve;

    // Heat the boiler loses through its lagging, in W per °C over the air's 20.
    private const float LossPerDegree = 1000f;

    // Latent heat at working pressure, in J/kg.
    private const float Latent = 1.95e6f;

    // Draft: air through a still fire, and per kg of exhaust and blower steam, in kg/s.
    private readonly float PassiveAir;
    private const float Entrainment = 3.5f;

    private readonly float BlowerSteam;
    private const float BlowdownRate = 20f;
    private const float FeedPpm = 350f;

    // Water below the crown sheet, and between the crown sheet and the steam outlet, in kg.
    private const float CrownMass = 8000f;
    public const float GlassMass = 4000f;

    private sealed class Steam : ISteamProperties
    {
        public float SteamEnthalpy(float pressure) => 2675f + 9f * (pressure - 1f);
        public float WaterEnthalpy(float temperature) => 4.19f * temperature;
        public float WaterTemperature(float enthalpy) => enthalpy / 4.19f;
        public float SaturationTemperature(float pressure) => 100f + 7f * (pressure - 1f);
    }

    private static readonly Steam Water = new Steam();

    public static FireboxParameters S282 => new FireboxParameters(150f, 120f, 0.5f, 10f, 5f, 15f);

    public Footplate(FiremanMode mode = FiremanMode.Road, float coal = 40f, float pressure = 14.5f, float glass = 0.55f,
        float injectorDelivery = 4f, float stokerFeed = 0f, float pumpFeed = 0f, FootplateEngine? engine = null)
    {
        engine ??= FootplateEngine.S282Like;
        HeatPerBar = engine.HeatPerBar;
        saturationCurve = engine.SaturationCurve;
        PassiveAir = engine.PassiveAir;
        BlowerSteam = engine.BlowerSteam;
        SafetyValve = engine.SafetyValve;
        if (stokerFeed > 0f)
            Stoker = new Stoker(new StokerSpec(S282, stokerFeed) { MeanCoalBetweenJams = 0f }, S282, new Random(1));
        if (pumpFeed > 0f)
            Pump = new FeedPump(new FeedPumpSpec(FeedHeaterType.Open, pumpFeed));
        Injector = new Injector(new InjectorSpec(injectorDelivery, 15f));
        Fire = FireTestKit.BurningFire(engine.Firebox, coal, FireTestKit.CleanCoal);
        if (engine.Character != null)
            Fire.Character = engine.Character;
        Fire.GrateArea = engine.GrateArea;
        Shovel = new ShovelDelivery(Fire.Parameters);
        Fireman.Mode = mode;
        Pressure = pressure;
        WaterMass = glass * GlassMass;
    }

    public FireModel Fire { get; }
    public ShovelDelivery Shovel { get; }
    public Injector Injector { get; }

    /// <summary>A stoker, where fitted (the S282's retrofit feeds 1.56 kg/s).</summary>
    public Stoker? Stoker { get; }

    /// <summary>A feedwater heater and pump, where fitted.</summary>
    public FeedPump? Pump { get; }

    public StokerControls StokerControls;
    public float PumpValve;
    public FeedPumpFlow PumpFlow;
    public float Stoked;
    public AutoFireman Fireman { get; } = new AutoFireman();

    public float Pressure;
    public float SafetyValve;

    /// <summary>Where the boiler would burst; 0, not known.</summary>
    public float BurstPressure;
    public float WaterMass;
    public float TenderWater = 20_000f;
    public float TenderCoal = 10_000f;
    public float Solids = 500f;

    /// <summary>The steam the engine takes at full work, in kg/s (the S282's throttle).</summary>
    public const float FullWorkSteam = 6f;

    // The engineer's side.
    public float EngineSteam;

    /// <summary>How wrong the fireman's ear is: the work he hears over the work done.</summary>
    public float WorkHeardBias = 1f;

    /// <summary>The glass bobs this much either way (foam, the engine's rocking).</summary>
    public float GlassBob;

    /// <summary>The glass reads this much over the water's level from the engine's tilt now (+ front up), and the most and least on the road ahead.</summary>
    public float Tilt;
    public float TiltHigh;
    public float TiltLow;


    /// <summary>The hardest work ahead along the road, as a share of full work.</summary>
    public float WorkAhead;
    public float? SteamAhead;
    public bool Standing;
    public bool Braking;

    /// <summary>The game's Steam Startup speedup on the fire (1 none).</summary>
    public float StartupMultiplier = 1f;

    /// <summary>
    /// The game's Steam Startup setting (1 at 100 %, 25 at 2,500 %); when set,
    /// <see cref="StartupMultiplier"/> follows the pressure and standing each
    /// tick as the game computes it: up to 25 over the setting at 1 bar,
    /// none at the firebox's start-up pressure or moving.
    /// </summary>
    public float? SteamStartup;

    /// <summary>The firebox's start-up pressure, absolute bar.</summary>
    public float StartupMaxPressure = 13f;

    /// <summary>Standing with the reverser set and the brakes off.</summary>
    public bool StartComing;

    /// <summary>The engineer's throttle; by default open while the engine draws steam.</summary>
    public float? Throttle;

    // The controls, as the fireman last set them.
    public float Door;
    public float Blower;
    public float Blowdown;
    public float Damper = 1f;
    public float InjectorLever;

    /// <summary>With its own steam valve (the lever then its water valve), where that sits; null for the one lever.</summary>
    public float? InjectorSteam;

    /// <summary>Water run out of the overflow, in kg.</summary>
    public float Overflowed;

    /// <summary>The water as it stands level, in glass shares; the glass reads <see cref="GlassShown"/>.</summary>
    public float Glass => WaterMass / GlassMass;
    public float GlassShown => Glass + Tilt;
    // The draft as the mod fits it: the blower's steam entrains more air a kg than the exhaust's.
    public SteamDraft Draft => new SteamDraft(BlowerSteam, PassiveAir, Entrainment, SafetyValve);
    public float BlowerFlow => Draft.BlowerSteam(Pressure, Blower);
    public float Air => Draft.Air(EngineSteam + AirPumpSteam, BlowerFlow, Damper);

    /// <summary>Steam an air pump takes, kg/s, its exhaust up the stack with the engine's.</summary>
    public float AirPumpSteam;
    public float Time { get; private set; }

    // What happened.
    public float CoalFired;
    public float SteamMade;
    public float BlownOff;
    public readonly List<float> Throws = new List<float>();
    public readonly List<ShakeStroke> Shakes = new List<ShakeStroke>();
    public readonly List<FiremanActions> Log = new List<FiremanActions>();
    public bool InjectorBroke;
    public FiremanActions Last;

    public FiremanReadings Readings => new FiremanReadings
    {
        Pressure = Pressure,
        SafetyValve = SafetyValve,
        BurstPressure = BurstPressure,
        AuxiliarySteam = AirPumpSteam + BlowerFlow,
        Standing = Standing,
        Braking = Braking,
        Throttle = Throttle ?? (EngineSteam > 0.05f ? 0.6f : 0f),
        Cutoff = StartComing || !Standing && EngineSteam > 0.05f ? 0.6f : 0f,
        RoadKnown = SteamAhead.HasValue,
        WorkSoon = (SteamAhead ?? 0f) / FullWorkSteam,
        Glass = GlassShown + GlassBob * MathF.Sin(2f * MathF.PI * 1.3f * Time),
        GlassTilt = Tilt,
        GlassTiltHigh = TiltHigh,
        GlassTiltLow = TiltLow,
        Work = WorkHeardBias * EngineSteam / FullWorkSteam,
        WorkAhead = WorkAhead,
        FullWorkSteam = FullWorkSteam,
        GlassMass = GlassMass,
        InjectorLeast = Injector.Spec.LeastShare(Pressure),
        TenderWater = TenderWater,
        TenderCoal = TenderCoal,
        Injector = Injector.State,
        InjectorLever = InjectorLever,
        InjectorSteamValve = InjectorSteam.HasValue,
        InjectorSteam = InjectorSteam ?? 0f,
        InjectorCrack = Injector.Spec.CrackOpening,
        InjectorStartPressure = Injector.Spec.StartPressure,
        InjectorFullFeed = Injector.FullDelivery(Pressure),
        HasStoker = Stoker != null,
        StokerJammed = Stoker != null && Stoker.Jammed,
        StokerFullFeed = Stoker != null ? Stoker.Spec.MaxFeedRate : 0f,
        HasPump = Pump != null,
        Pump = PumpFlow.State,
        PumpFullFeed = Pump != null ? Pump.Spec.Capacity : 0f,
        Solids = Solids,
        ShovelMass = 3f,
        StartupMultiplier = StartupMultiplier,
    };

    /// <summary>Runs for <paramref name="seconds"/>, calling <paramref name="each"/> after every tick.</summary>
    public void Run(float seconds, Action<Footplate>? each = null)
    {
        for (var t = 0f; t < seconds - 1e-4f; t += Dt)
        {
            Tick();
            each?.Invoke(this);
        }
    }

    private void Tick()
    {
        Time += Dt;
        if (SteamStartup is float setting)
        {
            var t = Math.Clamp((StartupMaxPressure - Pressure) / (StartupMaxPressure - 1f), 0f, 1f) * (Standing ? 1f : 0f);
            StartupMultiplier = 1f + (25f / setting - 1f) * t;
        }
        var actions = Fireman.Step(Dt, Fire, Readings);
        Last = actions;
        if (Fireman.Mode != FiremanMode.Off)
        {
            if (actions.Shovelful > 0f)
            {
                CoalFired += Shovel.Deliver(Fire, actions.Shovelful, actions.Aim);
                TenderCoal -= actions.Shovelful;
                Throws.Add(Time);
            }
            if (actions.Shake.HasValue)
            {
                Fire.ShakeGrate(actions.Shake.Value);
                Shakes.Add(actions.Shake.Value);
            }
            Door = actions.Door;
            Blower = actions.Blower;
            Blowdown = actions.Blowdown;
            Damper = actions.Damper;
            InjectorLever = actions.Injector;
            if (InjectorSteam.HasValue)
                InjectorSteam = actions.InjectorSteam;
            PumpValve = actions.Pump;
            StokerControls = new StokerControls
            {
                Direction = actions.StokerDirection,
                Throttle = actions.StokerThrottle,
                MasterJet = actions.MasterJet,
                LeftBack = actions.BackJets,
                RightBack = actions.BackJets,
                LeftFront = actions.FrontJets,
                RightFront = actions.FrontJets,
                Fines = actions.FinesJet,
            };
        }

        var stokerSteam = 0f;
        if (Stoker != null)
        {
            var stoked = Stoker.Step(Dt, new StokerInputs { Controls = StokerControls, BoilerPressure = Pressure, TenderCoal = TenderCoal, AirFlow = Air }, Fire);
            TenderCoal -= stoked.Drawn;
            Stoked += stoked.Fed;
            stokerSteam = stoked.SteamUsed;
        }

        var saturation = Water.SaturationTemperature(Pressure);
        var burned = Fire.Step(Dt, new FireInputs
        {
            AirFlow = Air,
            DoorOpening = Door,
            StartupMultiplier = StartupMultiplier,
            BoilerTemperature = saturation,
            Speed = Standing ? 0f : 15f,
        });

        var flow = Injector.Step(Dt, new InjectorInputs
        {
            SteamValve = InjectorSteam ?? InjectorLever,
            WaterValve = InjectorSteam.HasValue ? InjectorLever : (float?)null,
            BoilerPressure = Pressure,
            TenderWater = TenderWater,
            BoilerSpace = 10_000f,
            TenderWaterTemperature = 15f,
        }, Water);
        InjectorBroke |= flow.State == InjectorState.Broken;
        Overflowed += flow.Overflow * Dt;
        TenderWater = Math.Max(0f, TenderWater - flow.TenderDraw * Dt);

        var pumped = 0f;
        var pumpWarmth = 0f;
        if (Pump != null)
        {
            PumpFlow = Pump.Step(Dt, new FeedPumpInputs
            {
                Valve = PumpValve,
                BoilerPressure = Pressure,
                BoilerSpace = 10_000f,
                TenderWater = TenderWater,
                TenderWaterTemperature = 15f,
                ExhaustFlow = EngineSteam,
                FullExhaustFlow = FullWorkSteam,
                ChestPressure = EngineSteam > 0.05f ? 0.8f * Pressure : 1f,
            }, Water);
            TenderWater = Math.Max(0f, TenderWater - PumpFlow.TenderDraw * Dt);
            pumped = PumpFlow.WaterIn;
            pumpWarmth = PumpFlow.WaterIn * 4190f * Math.Max(0f, saturation - PumpFlow.FeedTemperature);
            stokerSteam += PumpFlow.SteamUsed;
        }

        var steamOut = EngineSteam + AirPumpSteam + BlowerFlow + flow.SteamUsed + stokerSteam;
        var blown = Pressure > 1.5f ? Blowdown * BlowdownRate : 0f;
        var heat = Fire.Heat - LossPerDegree * (saturation - 20f) - steamOut * Latent - flow.WaterIn * 4190f * Math.Max(0f, saturation - flow.FeedTemperature)
                   - pumpWarmth;
        // A boiler raised from low pressure takes more heat a bar there, its
        // water's boiling point rising steeply (about 100 × p^0.25 °C).
        var perBar = saturationCurve ? HeatPerBar * MathF.Pow(13f / Math.Max(1f, Pressure), 0.75f) : HeatPerBar;
        Pressure += heat / perBar * Dt;
        if (Pressure > SafetyValve)
        {
            BlownOff += (Pressure - SafetyValve) * perBar / Latent;
            Pressure = SafetyValve;
        }
        SteamMade += steamOut * Dt;

        var total = CrownMass + WaterMass;
        Solids = (Solids * total + FeedPpm * (flow.WaterIn + pumped) * Dt - Solids * blown * Dt) / total;
        WaterMass += (flow.WaterIn + pumped - steamOut - blown) * Dt;
        Log.Add(actions);
    }

    /// <summary>Throws grouped into rounds: throws closer than the fireman's gap between rounds.</summary>
    public List<int> Rounds()
    {
        var rounds = new List<int>();
        for (var i = 0; i < Throws.Count; i++)
        {
            if (i == 0 || Throws[i] - Throws[i - 1] > AutoFireman.HurriedRoundGap)
                rounds.Add(0);
            rounds[^1]++;
        }
        return rounds;
    }
}

/// <summary>An engine's figures for the <see cref="Footplate"/>: its firebox, grate, draft, and boiler.</summary>
internal sealed record FootplateEngine(FireboxParameters Firebox, float BlowerSteam, float PassiveAir, float HeatPerBar, float SafetyValve)
{
    public GrateCharacter? Character { get; init; }

    /// <summary>The grate's area, m²; 0 leaves it unmeasured.</summary>
    public float GrateArea { get; init; }

    /// <summary>Whether <see cref="HeatPerBar"/> is the figure at 13 bar, rising toward low pressure as water's boiling point does.</summary>
    public bool SaturationCurve { get; init; }

    /// <summary>The test footplate's S282-like engine.</summary>
    public static FootplateEngine S282Like => new(Footplate.S282, 0.5f, 0.5f, 1.6e8f, 15f);

    /// <summary>
    /// Lilla and Cruzer's JK3: its stock 70 kg, 200 s firebox (start-up
    /// pressure 13 bar), its fitted blower and natural draft (BlowerFit,
    /// 0.2 and 0.15 kg/s), its measured grate and firebox character, and its
    /// 15 bar safety valve; a small shunter's boiler, about 4.5 t of water
    /// and its steel (inferred), 8.9e7 J a bar at 13 bar, 6e8 at 1.
    /// </summary>
    public static FootplateEngine Jk3 => new(new FireboxParameters(70f, 200f, 0.5f, 4f, 5f, 15f), 0.2f, 0.15f, 8.9e7f, 15f)
    {
        SaturationCurve = true,
        Character = new GrateCharacter { BackRowDraft = 0.94f, FrontRowDraft = 1.08f, SideDraft = 1.03f, OverFireMixing = 0.85f },
        GrateArea = 1.430f,
    };
}
