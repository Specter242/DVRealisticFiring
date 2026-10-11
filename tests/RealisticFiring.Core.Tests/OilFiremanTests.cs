using RealisticFiring.Core;
using Xunit.Abstractions;

namespace RealisticFiring.Core.Tests;

 public class OilFiremanTests
{
    private readonly ITestOutputHelper output;

    public OilFiremanTests(ITestOutputHelper output) => this.output = output;

    private sealed class OilFootplate
    {
        public const float Dt = 0.05f;
        private const float HeatPerBar = 2.5e8f;
        private const float Latent = 1.95e6f;
        private const float LossPerDegree = 1500f;
        // The 2901's draft as the mod fits it: natural 1.0 kg/s, 2 kg of air a
        // kg of exhaust steam, and a period blower of 0.85 kg/s at 5.
        private const float BlowerSteam = 0.85f;
        public const float FullWorkSteam = 12f;
        public const float GlassMass = 8000f;

        private sealed class Steam : ISteamProperties
        {
            public float SteamEnthalpy(float pressure) => 2675f + 9f * (pressure - 1f);
            public float WaterEnthalpy(float temperature) => 4.19f * temperature;
            public float WaterTemperature(float enthalpy) => enthalpy / 4.19f;
            public float SaturationTemperature(float pressure) => 100f + 7f * (pressure - 1f);
        }

        private static readonly Steam Water = new Steam();

        public OilFootplate(float pressure = 14.5f, float oilTemperature = 60f, bool lit = true)
        {
            Fire = new OilFire(new FireboxParameters(200f, 100f, 0.6f));
            if (lit)
                Fire.PrepareFire();
            Fire.Restore(lit, lit ? OilFire.HotBrick : OilFire.ColdBrick, oilTemperature, 0f);
            Pressure = pressure;
            Fireman.Mode = FiremanMode.Road;
        }

        public OilFire Fire { get; }
        public Injector Injector { get; } = new Injector(new InjectorSpec(14.2f, 15f));
        public AutoOilFireman Fireman { get; } = new AutoOilFireman();
        public float Pressure;
        public float SafetyValve = 15f;
        public float WaterMass = 0.55f * GlassMass;
        public float EngineSteam;
        public bool Standing;
        public float Sand = 20f;
        public FiremanActions Last;
        public float Time;
        public float BlackTime;
        public float ThrownSand;

        /// <summary>The damper where the player holds it, or null for the fireman's.</summary>
        public float? HeldDamper;

        public float Glass => WaterMass / GlassMass;
        public SteamDraft Draft => new SteamDraft(BlowerSteam, 1f, 2f, SafetyValve);
        public float BlowerFlow => Draft.BlowerSteam(Pressure, Last.Blower);
        public float Air => Draft.Air(EngineSteam, BlowerFlow, HeldDamper ?? Last.Damper);

        public void Run(float seconds, Action<OilFootplate>? each = null)
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
            var injectorState = Injector.State;
            Last = Fireman.Step(Dt, Fire, new FiremanReadings
            {
                Pressure = Pressure,
                SafetyValve = SafetyValve,
                Work = EngineSteam / FullWorkSteam,
                FullWorkSteam = FullWorkSteam,
                GlassMass = GlassMass,
                Throttle = EngineSteam > 0.05f ? 0.6f : 0f,
                Cutoff = Standing ? 0f : 0.5f,
                Standing = Standing,
                Glass = Glass,
                TenderWater = 40_000f,
                TenderCoal = 20_000f,
                Injector = injectorState,
                InjectorCrack = Injector.Spec.CrackOpening,
                InjectorStartPressure = Injector.Spec.StartPressure,
                InjectorFullFeed = Injector.FullDelivery(Pressure),
                InjectorLeast = Injector.Spec.LeastShare(Pressure),
                Sand = Sand,
            });
            if (Last.Sand > 0f)
            {
                Sand -= Last.Sand;
                ThrownSand += Last.Sand;
                Fire.ThrowSand(Last.Sand, Math.Min(1f, EngineSteam / FullWorkSteam));
            }

            var saturation = Water.SaturationTemperature(Pressure);
            Fire.Step(Dt, new OilFireInputs
            {
                FiringValve = Last.FiringValve,
                Atomizer = Last.Atomizer,
                Heater = Last.Heater,
                AirFlow = Air,
                Exhaust = Math.Min(1f, EngineSteam / FullWorkSteam),
                BoilerPressure = Pressure,
                BoilerTemperature = saturation,
                TenderFuel = 20_000f,
                StartupMultiplier = 1f,
            });
            if (Fire.StackSmoke >= AutoOilFireman.BlackSmoke)
                BlackTime += Dt;

            var flow = Injector.Step(Dt, new InjectorInputs
            {
                SteamValve = Last.Injector,
                BoilerPressure = Pressure,
                TenderWater = 40_000f,
                BoilerSpace = 20_000f,
                TenderWaterTemperature = 15f,
            }, Water);

            var steamOut = EngineSteam + BlowerFlow + Fire.AtomizerSteam + Fire.HeaterSteamUsed + flow.SteamUsed;
            var heat = Fire.Heat - LossPerDegree * (saturation - 20f) - steamOut * Latent
                       - flow.WaterIn * 4190f * Math.Max(0f, saturation - flow.FeedTemperature);
            Pressure = Math.Min(SafetyValve, Pressure + heat / HeatPerBar * Dt);
            WaterMass += (flow.WaterIn - steamOut) * Dt;
        }
    }

    [Fact]
    public void OnTheRoadHeHoldsThePressureWithAHazeAtTheStack()
    {
        var engine = new OilFootplate();
        engine.EngineSteam = 6f;
        engine.Run(300f);
        engine.BlackTime = 0f;
        float low = float.MaxValue, lowGlass = 1f, atomLow = float.MaxValue, atomHigh = 0f, oilLow = float.MaxValue, oilHigh = 0f;
        engine.EngineSteam = 9f;
        engine.Run(600f, e =>
        {
            low = Math.Min(low, e.Pressure);
            lowGlass = Math.Min(lowGlass, e.Glass);
            if (e.Time > 330f)
            {
                atomLow = Math.Min(atomLow, e.Fire.Atomizing);
                atomHigh = Math.Max(atomHigh, e.Fire.Atomizing);
            }
            oilLow = Math.Min(oilLow, e.Fire.OilTemperature);
            oilHigh = Math.Max(oilHigh, e.Fire.OilTemperature);
        });
        output.WriteLine($"pressure low {low:F2}, glass low {lowGlass:F2}, black {engine.BlackTime:F0} s, atomizing {atomLow:F2}-{atomHigh:F2}, " +
                         $"oil {oilLow:F0}-{oilHigh:F0} C, excess air {engine.Fire.ExcessAir:F2}, valve {engine.Last.FiringValve:F2}, " +
                         $"atomizer {engine.Last.Atomizer:F2}, damper {engine.Last.Damper:F2}, trim {engine.Fireman.Trim:F2}");
        var target = AutoOilFireman.PressureFor(FiremanMode.Road, engine.SafetyValve);
        Assert.True(engine.Fire.FireOn);
        Assert.True(low >= target - 1.5f, $"pressure fell to {low:F2}");
        Assert.True(engine.BlackTime < 60f, $"black for {engine.BlackTime:F0} s");
        Assert.InRange(atomLow, 0.9f, 2.5f);
        Assert.InRange(atomHigh, 0.9f, 2.5f);
        Assert.InRange(oilLow, OilFire.WarmOil, OilFire.HotOil);
        Assert.InRange(oilHigh, OilFire.WarmOil, OilFire.HotOil);
    }

    [Fact]
    public void StandingHeKeepsALightFireLitAndClean()
    {
        var engine = new OilFootplate();
        engine.Standing = true;
        engine.Run(600f);
        output.WriteLine($"standing: fire {engine.Fire.FireOn}, valve {engine.Last.FiringValve:F2}, blower {engine.Last.Blower:F2}, " +
                         $"damper {engine.Last.Damper:F2}, excess air {engine.Fire.ExcessAir:F2}, pressure {engine.Pressure:F2}, black {engine.BlackTime:F0} s");
        Assert.True(engine.Fire.FireOn);
        Assert.True(engine.BlackTime < 30f);
        Assert.True(engine.Last.FiringValve >= engine.Fireman.LeastFire);
    }

    [Fact]
    public void PullingAwayShortOfSteamHeDoesNotSmokeBlackForLong()
    {
        // The 2901 in game (2026-10-10): standing a bar and a half short, he
        // opened the oil for the pressure as the engine started, and half of
        // it went unburned, black, for most of a minute.
        var engine = new OilFootplate(pressure: 19f);
        engine.SafetyValve = 21.5f;
        engine.Standing = true;
        engine.Run(120f);
        engine.BlackTime = 0f;
        engine.Standing = false;
        var drumming = 0f;
        engine.Run(30f, e => e.EngineSteam = Math.Min(9f, e.EngineSteam + 0.5f * OilFootplate.Dt));
        engine.Run(240f, e => drumming = Math.Max(drumming, e.Fire.Drumming));
        output.WriteLine($"black {engine.BlackTime:F0} s, drumming up to {drumming:P0}, pressure {engine.Pressure:F2}, valve {engine.Last.FiringValve:F2}, " +
                         $"damper {engine.Last.Damper:F2}, blower {engine.Last.Blower:F2}, stack {engine.Fire.StackSmoke:F2}");
        Assert.True(engine.BlackTime < 15f, $"black for {engine.BlackTime:F0} s");
        Assert.True(drumming < 0.2f, $"drummed {drumming:P0}");
    }

    [Fact]
    public void ShortOfAirHeCutsTheOilBackUntilTheStackClears()
    {
        // The player holds the damper nearly shut: more air cannot come, so
        // a black stack is too much oil, and he takes it off.
        var engine = new OilFootplate();
        engine.EngineSteam = 9f;
        engine.Run(120f);
        engine.HeldDamper = 0.4f;
        engine.BlackTime = 0f;
        engine.Run(300f);
        output.WriteLine($"black {engine.BlackTime:F0} s of 300, valve {engine.Last.FiringValve:F2}, stack {engine.Fire.StackSmoke:F2}, " +
                         $"excess air {engine.Fire.ExcessAir:F2}, pressure {engine.Pressure:F2}");
        Assert.True(engine.BlackTime < 30f, $"black for {engine.BlackTime:F0} s");
        Assert.True(engine.Fire.StackSmoke < AutoOilFireman.BlackSmoke);
    }

    [Fact]
    public void StandingOnALightFireTheAtomizerDoesNotDrum()
    {
        var engine = new OilFootplate();
        engine.Standing = true;
        var drumming = 0f;
        engine.Run(300f, e => drumming = Math.Max(drumming, e.Time > 60f ? e.Fire.Drumming : 0f));
        output.WriteLine($"valve {engine.Last.FiringValve:F2}, atomizer {engine.Last.Atomizer:F3} ({engine.Fire.Atomizing:F1}x), drumming up to {drumming:P0}");
        Assert.True(drumming < 0.05f, $"drummed {drumming:P0}");
    }

    [Fact]
    public void WithTheFireOutHeRunsNoOilAndClearsTheBox()
    {
        var engine = new OilFootplate(lit: false);
        engine.Run(5f);
        Assert.False(engine.Fire.FireOn);
        Assert.Equal(0f, engine.Last.FiringValve);
        Assert.True(engine.Last.Blower > 0f);
        Assert.True(engine.Last.Notices.HasFlag(FiremanNotice.FireOut));
        Assert.Equal(0f, engine.Fire.PooledOil);
    }

    [Fact]
    public void ColdOilHeWarmsWithTheHeaterAndThenEases()
    {
        var engine = new OilFootplate(oilTemperature: 25f);
        engine.EngineSteam = 4f;
        engine.Run(1f);
        Assert.True(engine.Last.Heater > 0.9f);
        Assert.True(engine.Last.Notices.HasFlag(FiremanNotice.ColdOil));
        engine.Run(900f);
        output.WriteLine($"oil {engine.Fire.OilTemperature:F0} C, heater {engine.Last.Heater:F2}");
        Assert.InRange(engine.Fire.OilTemperature, OilFire.WarmOil, OilFire.HotOil);
        Assert.True(engine.Last.Heater < 0.5f);
    }

    [Fact]
    public void SootedFluesAreSandedWhileWorkingHardAndNotTooOften()
    {
        var engine = new OilFootplate();
        var sooted = engine.Fire.Snapshot();
        sooted.FlueSoot = 0.6f;
        engine.Fire.Mirror(sooted);
        engine.EngineSteam = 9f;
        engine.Run(30f);
        var first = engine.ThrownSand;
        engine.Run(120f);
        output.WriteLine($"sand thrown {first:F1} kg at first, {engine.ThrownSand:F1} kg after two more minutes; soot {engine.Fire.FlueSoot:F2}");
        Assert.Equal(AutoOilFireman.SandThrow, first, 3);
        Assert.Equal(first, engine.ThrownSand, 3);
    }
}
