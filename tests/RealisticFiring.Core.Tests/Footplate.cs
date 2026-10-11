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
    private const float HeatPerBar = 1.6e8f;

    // Heat the boiler loses through its lagging, in W per °C over the air's 20.
    private const float LossPerDegree = 1000f;

    // Latent heat at working pressure, in J/kg.
    private const float Latent = 1.95e6f;

    // Draft: air through a still fire, and per kg of exhaust and blower steam, in kg/s.
    private const float PassiveAir = 0.5f;
    private const float Entrainment = 3.5f;

    private const float BlowerSteam = 0.5f;
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
        float injectorDelivery = 4f, float stokerFeed = 0f, float pumpFeed = 0f)
    {
        if (stokerFeed > 0f)
            Stoker = new Stoker(new StokerSpec(S282, stokerFeed) { MeanCoalBetweenJams = 0f }, S282, new Random(1));
        if (pumpFeed > 0f)
            Pump = new FeedPump(new FeedPumpSpec(FeedHeaterType.Open, pumpFeed));
        Injector = new Injector(new InjectorSpec(injectorDelivery, 15f));
        Fire = FireTestKit.BurningFire(S282, coal, FireTestKit.CleanCoal);
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
    public float SafetyValve = 15f;
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


    /// <summary>The hardest work ahead along the road, as a share of full work.</summary>
    public float WorkAhead;
    public float? SteamAhead;
    public bool Standing;
    public bool Braking;

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

    public float Glass => WaterMass / GlassMass;
    public float Air => (PassiveAir + Entrainment * (EngineSteam + Blower * BlowerSteam)) * (0.1f + 0.9f * Damper);
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
        Standing = Standing,
        Braking = Braking,
        Throttle = Throttle ?? (EngineSteam > 0.05f ? 0.6f : 0f),
        Cutoff = StartComing || !Standing && EngineSteam > 0.05f ? 0.6f : 0f,
        RoadKnown = SteamAhead.HasValue,
        WorkSoon = (SteamAhead ?? 0f) / FullWorkSteam,
        Glass = Glass + GlassBob * MathF.Sin(2f * MathF.PI * 1.3f * Time),
        Work = WorkHeardBias * EngineSteam / FullWorkSteam,
        WorkAhead = WorkAhead,
        FullWorkSteam = FullWorkSteam,
        GlassMass = GlassMass,
        InjectorLeast = Injector.Spec.LeastShare(Pressure),
        TenderWater = TenderWater,
        TenderCoal = TenderCoal,
        Injector = Injector.State,
        InjectorLever = InjectorLever,
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
            StartupMultiplier = 1f,
            BoilerTemperature = saturation,
            Speed = Standing ? 0f : 15f,
        });

        var flow = Injector.Step(Dt, new InjectorInputs
        {
            SteamValve = InjectorLever,
            BoilerPressure = Pressure,
            TenderWater = TenderWater,
            BoilerSpace = 10_000f,
            TenderWaterTemperature = 15f,
        }, Water);
        InjectorBroke |= flow.State == InjectorState.Broken;
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

        var steamOut = EngineSteam + Blower * BlowerSteam + flow.SteamUsed + stokerSteam;
        var blown = Pressure > 1.5f ? Blowdown * BlowdownRate : 0f;
        var heat = Fire.Heat - LossPerDegree * (saturation - 20f) - steamOut * Latent - flow.WaterIn * 4190f * Math.Max(0f, saturation - flow.FeedTemperature)
                   - pumpWarmth;
        Pressure += heat / HeatPerBar * Dt;
        if (Pressure > SafetyValve)
        {
            BlownOff += (Pressure - SafetyValve) * HeatPerBar / Latent;
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
