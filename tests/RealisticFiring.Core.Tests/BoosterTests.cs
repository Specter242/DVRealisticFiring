using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

/// <summary>
/// A Franklin C-2 booster on a J-3a Hudson's trailer axle: 12,100 lb at 250
/// psi, cut in below 12 mph with the reverser in the forward corner, cocks
/// open for 20 s, falling off with speed, clashing if cut in too fast or
/// turning, priming if cut in cold. Pressures are absolute bar.
/// </summary>
public class BoosterTests
{
    private const float Dt = 0.05f;
    private const float Lbf = 4.44822f;
    private const float Mph = 0.44704f;
    private const float Chest250Psi = 1f + 250f / 14.5038f;

    // The game's own steam table: specific volume in L/kg from absolute bar.
    private static float SteamVolume(float pressure) => 1.72f * (float)Math.Pow(pressure, -0.951);

    private static Booster NewBooster() => new Booster(new BoosterSpec());

    private static BoosterInputs In(bool latch = true, bool idling = false, float chest = Chest250Psi, float cutoff = 0.9f, float speed = 0f, float adhesion = 0f) =>
        new BoosterInputs
        {
            Latch = latch,
            Idling = idling,
            ChestPressure = chest,
            BoilerPressure = 19f,
            Cutoff = cutoff,
            Speed = speed,
            AdhesionLimit = adhesion,
        };

    private static BoosterFlow Run(Booster booster, BoosterInputs inputs, float seconds)
    {
        var flow = default(BoosterFlow);
        for (var t = 0f; t < seconds; t += Dt)
            flow = booster.Step(Dt, inputs, SteamVolume);
        return flow;
    }

    // Idles it warm, then lets it stop.
    private static void Warm(Booster booster)
    {
        Run(booster, In(latch: false, idling: true), 180f);
        Run(booster, In(latch: false), 20f);
    }

    [Fact]
    public void CutOutItDoesNothing()
    {
        var flow = Run(NewBooster(), In(latch: false), 5f);

        Assert.Equal(BoosterState.CutOut, flow.State);
        Assert.Equal(0f, flow.Force);
        Assert.Equal(0f, flow.SteamUsed);
    }

    [Fact]
    public void ItPullsTwelveThousandPoundsAtTwoHundredFiftyPsiOnceTheCocksClose()
    {
        var booster = NewBooster();
        Warm(booster);

        var cocks = Run(booster, In(), 5f);
        Assert.Equal(BoosterState.CocksOpen, cocks.State);
        Assert.True(cocks.CocksOpen);
        Assert.InRange(cocks.InletPressure, 2f, 2.8f);
        Assert.InRange(cocks.Force, 1000f * Lbf, 2500f * Lbf);

        var working = Run(booster, In(), 20f);
        Assert.Equal(BoosterState.Working, working.State);
        Assert.InRange(working.Force, 12000f * Lbf, 12200f * Lbf);
    }

    [Fact]
    public void TheLatchWillNotStayUpWithTheReverserHookedUp()
    {
        var booster = NewBooster();
        Warm(booster);

        var flow = Run(booster, In(cutoff: 0.6f), 1f);

        Assert.True(flow.LatchDropped);
        Assert.Equal(BoosterState.CutOut, flow.State);
    }

    [Fact]
    public void HookingTheReverserBackCutsItOut()
    {
        var booster = NewBooster();
        Warm(booster);
        Run(booster, In(), 30f);

        var flow = booster.Step(Dt, In(cutoff: 0.5f, speed: 4f), SteamVolume);

        Assert.True(flow.LatchDropped);
        Assert.Equal(BoosterState.CutOut, flow.State);
        Assert.Equal(0f, flow.Force);
    }

    [Fact]
    public void CutInAboveTwelveMilesAnHourTheGearsClash()
    {
        var booster = NewBooster();
        Warm(booster);

        var flow = booster.Step(Dt, In(speed: 14f * Mph), SteamVolume);

        Assert.Equal(BoosterEvent.Clash, flow.Event);
        Assert.True(flow.LatchDropped);
        Assert.Equal(BoosterState.CutOut, flow.State);
    }

    [Fact]
    public void RaisedWhileTheIdlingEngineStillTurnsTheGearsClash()
    {
        var booster = NewBooster();
        Run(booster, In(latch: false, idling: true), 180f);

        var clash = booster.Step(Dt, In(), SteamVolume);
        Assert.Equal(BoosterEvent.Clash, clash.Event);

        Run(booster, In(latch: false), 20f);
        Assert.Equal(BoosterState.CocksOpen, booster.Step(Dt, In(), SteamVolume).State);
    }

    [Fact]
    public void ItsPullFallsOffWithSpeed()
    {
        var booster = NewBooster();
        Warm(booster);
        var start = Run(booster, In(), 25f).Force;

        var at10 = Run(booster, In(speed: 10f * Mph), 1f);
        var at20 = Run(booster, In(speed: 20f * Mph), 1f);

        Assert.True(at10.Force < start && at20.Force < at10.Force);
        // On the C-2's 51-in wheels 20 mph is about 340 crank rpm: some 70 % left.
        Assert.InRange(at20.Force / start, 0.6f, 0.8f);
        Assert.InRange(at20.CrankRpm, 320f, 360f);
    }

    [Fact]
    public void ItRunsOnTheMainEnginesSteamChestSoTheThrottleStarvesIt()
    {
        var booster = NewBooster();
        Warm(booster);
        Run(booster, In(), 25f);

        var shut = Run(booster, In(chest: 1f, speed: 3f), 1f);

        Assert.Equal(BoosterState.Working, shut.State);
        Assert.Equal(0f, shut.Force);
    }

    [Fact]
    public void AboveTwentyOneMilesAnHourItIsOverspeed()
    {
        var booster = NewBooster();
        Warm(booster);
        Run(booster, In(), 25f);

        Assert.False(Run(booster, In(speed: 18f * Mph), 1f).Overspeed);
        Assert.True(Run(booster, In(speed: 23f * Mph), 1f).Overspeed);
    }

    [Fact]
    public void BeyondTheTrailerAxlesAdhesionItsWheelsSlip()
    {
        var booster = NewBooster();
        Warm(booster);

        var flow = Run(booster, In(adhesion: 30000f), 25f);

        Assert.True(flow.Slipping);
        Assert.True(flow.Force < 30000f);
    }

    [Fact]
    public void CutInColdItPrimesUntilTheWaterIsOut()
    {
        var cold = NewBooster();
        var first = cold.Step(Dt, In(), SteamVolume);
        Assert.Equal(BoosterEvent.Priming, first.Event);
        Assert.True(Run(cold, In(), cold.Spec.PrimingTime - 1f).Priming);
        Assert.False(Run(cold, In(), 2f).Priming);

        var warm = NewBooster();
        Warm(warm);
        Assert.Equal(BoosterEvent.None, warm.Step(Dt, In(), SteamVolume).Event);
        Assert.False(Run(warm, In(), 25f).Priming);
    }

    [Fact]
    public void BackingUpInGearItIsDraggedNotDriven()
    {
        var booster = NewBooster();
        Warm(booster);
        Run(booster, In(), 25f);

        var flow = Run(booster, In(speed: -1f), 1f);

        Assert.True(flow.Backing);
        Assert.Equal(0f, flow.Force);
    }

    [Fact]
    public void AtFullOutputItUsesTheSteamTheTestsMeasured()
    {
        var booster = NewBooster();
        Warm(booster);
        Run(booster, In(), 25f);

        // About 12 mph: the dynamometer tests put a C-type at 9,000–13,000 lb/h at full output.
        var flow = Run(booster, In(speed: 12f * Mph), 1f);
        var lbPerHour = flow.SteamUsed * 3600f / 0.4536f;

        Assert.InRange(lbPerHour, 8000f, 14000f);
        Assert.Equal(flow.CrankRpm / 60f * 4f, flow.BeatRate, 3);
    }
}
