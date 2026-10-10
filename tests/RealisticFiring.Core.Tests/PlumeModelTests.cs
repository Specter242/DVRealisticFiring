using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

 public class PlumeModelTests
{
    private static readonly PlumeTuning Tuning = new PlumeTuning();

    private static PlumeInputs Working(float pressure, float smoke = 0f, float flow = 0.6f) => new PlumeInputs
    {
        ExhaustPressure = pressure,
        TotalFlowNormalized = flow,
        Soot = smoke,
        FireOn = true,
    };

    private static float Brightness(PlumeColor c) => (c.R + c.G + c.B) / 3f;

    [Fact]
    public void HardWorkThrowsPuffsHigh()
    {
        var drifting = PlumeModel.ForBeat(Tuning, Working(1f));
        var hard = PlumeModel.ForBeat(Tuning, Working(10f));

        Assert.InRange(drifting.Speed, 2f, 5f);
        Assert.True(hard.Speed >= 15f, $"hard puff at {hard.Speed} m/s");
        Assert.True(hard.Particles > drifting.Particles);
        Assert.True(hard.Size > drifting.Size);
    }

    [Fact]
    public void CleanFireShowsLightSteamAndSmokyFireShowsBlack()
    {
        var clean = PlumeModel.ForBeat(Tuning, Working(5f, smoke: 0f));
        var smoky = PlumeModel.ForBeat(Tuning, Working(5f, smoke: 1f));

        Assert.True(Brightness(clean.Color) > 0.8f);
        Assert.True(Brightness(smoky.Color) < 0.2f);
        Assert.True(smoky.Color.A > clean.Color.A);
        Assert.Equal(0, clean.SootParticles);
        Assert.True(smoky.SootParticles > 0);
    }

    [Fact]
    public void ColorDarkensSteadilyWithSmoke()
    {
        var previous = float.MaxValue;
        for (var smoke = 0f; smoke <= 1.001f; smoke += 0.1f)
        {
            var brightness = Brightness(PlumeModel.ForBeat(Tuning, Working(5f, smoke)).Color);
            Assert.True(brightness < previous, $"brightness {brightness} at smoke {smoke}");
            previous = brightness;
        }
    }

    [Fact]
    public void SparksAreRareAndOnlyUnderHeavyDraft()
    {
        Assert.Equal(0f, PlumeModel.ForBeat(Tuning, Working(4f)).Sparks);
        Assert.InRange(PlumeModel.ForBeat(Tuning, Working(10f, flow: 1f)).Sparks, 0.1f, 0.6f);
        Assert.True(PlumeModel.ForBeat(Tuning, Working(10f, flow: 0.3f)).Sparks < 0.25f);
        Assert.Equal(0f, PlumeModel.ForBeat(Tuning, Working(10f, flow: 1f) with { FireOn = false }).Sparks);
    }

    [Fact]
    public void TheHardestPuffStopsAtTheJetLength()
    {
        // Under linear drag k a puff launched at v travels v / k.
        var drag = PlumeModel.JetDrag(Tuning);
        var hardest = PlumeModel.ForBeat(Tuning, Working(10f));
        var light = PlumeModel.ForBeat(Tuning, Working(1f));

        Assert.Equal(Tuning.JetLength, hardest.Speed / drag, 3);
        Assert.True(light.Speed / drag < 0.5f * Tuning.JetLength);
    }

    [Fact]
    public void SpreadSmokeRisesAtTheTunedSpeed()
    {
        // Lift balances drag at the rise speed: dv/dt = lift - k v.
        Assert.Equal(Tuning.RiseSpeed, PlumeModel.Lift(Tuning) / PlumeModel.JetDrag(Tuning), 3);
    }

    [Fact]
    public void AStandingCleanFireSendsUpAFaintLazyColumn()
    {
        var haze = PlumeModel.Between(Tuning, Working(1f, smoke: 0f, flow: 0.01f));

        Assert.True(haze.Rate >= 2f, $"{haze.Rate} particles a second");
        Assert.InRange(haze.Speed, 1f, 2.5f);
        Assert.True(haze.Color.A < 0.35f);
    }

    [Fact]
    public void AStandingFreshChargeSendsUpDarkSmoke()
    {
        var clean = PlumeModel.Between(Tuning, Working(1f, smoke: 0f, flow: 0.01f));
        var sooty = PlumeModel.Between(Tuning, Working(1f, smoke: 1f, flow: 0.01f));

        Assert.True(sooty.Color.A > 0.5f);
        Assert.True(Brightness(sooty.Color) < Brightness(clean.Color));
    }

    [Fact]
    public void TheBlowerMakesASteadyFasterWhiterStream()
    {
        var standing = PlumeModel.Between(Tuning, Working(1f, smoke: 0f, flow: 0.01f));
        var blower = PlumeModel.Between(Tuning, Working(1f, smoke: 0f, flow: 0.1f));

        Assert.True(blower.Rate >= 3f * standing.Rate, $"{blower.Rate} against {standing.Rate} particles a second");
        Assert.True(blower.Speed > 2f * standing.Speed);
        Assert.True(blower.Color.A > standing.Color.A);
        Assert.True(Brightness(blower.Color) > Brightness(standing.Color));
    }

    [Fact]
    public void ADeadFireWithNoSteamSendsNothingUp()
    {
        var cold = Working(1f, flow: 0f) with { FireOn = false };
        Assert.Equal(0f, PlumeModel.Between(Tuning, cold).Rate);

        var blowerOnly = Working(1f, flow: 0.1f) with { FireOn = false };
        Assert.True(PlumeModel.Between(Tuning, blowerOnly).Rate > 0f);
    }

    [Fact]
    public void LightSootOnlyGreysThePuff()
    {
        Assert.True(Brightness(PlumeModel.ForBeat(Tuning, Working(5f, smoke: 0.3f)).Color) > 0.6f);
    }

    [Fact]
    public void SteamPuffsAreClearlyVisible()
    {
        var clean = PlumeModel.ForBeat(Tuning, Working(3f));

        Assert.True(clean.Color.A >= 0.6f, $"alpha {clean.Color.A}");
        Assert.True(clean.Particles >= 5, $"{clean.Particles} particles");
    }

    [Fact]
    public void SloggingAtLongCutoffThrowsTheBiggestPuffs()
    {
        var running = PlumeModel.ForBeat(Tuning, Working(6.5f));
        var slogging = PlumeModel.ForBeat(Tuning, Working(12f));
        var quiet = PlumeModel.ForBeat(new PlumeTuning { HeavyVolume = 0f }, Working(12f));

        Assert.True(slogging.Particles >= 2 * running.Particles, $"{running.Particles} -> {slogging.Particles} particles");
        Assert.True(slogging.Size > 1.4f * running.Size, $"size {running.Size:F1} -> {slogging.Size:F1} m");
        Assert.True(slogging.LifetimeScale > 1.3f);
        Assert.Equal(running.Speed, slogging.Speed, 3);
        Assert.Equal(PlumeModel.ForBeat(Tuning, Working(6f)).Particles, quiet.Particles);
    }

    [Fact]
    public void TuningScalesThePuff()
    {
        var doubled = new PlumeTuning { PuffParticles = 2f };
        var faster = new PlumeTuning { LaunchSpeed = 10f, MaxBeatPressure = 4f };

        Assert.InRange(PlumeModel.ForBeat(doubled, Working(10f)).Particles,
            2 * PlumeModel.ForBeat(Tuning, Working(10f)).Particles - 1, 2 * PlumeModel.ForBeat(Tuning, Working(10f)).Particles + 1);
        Assert.Equal(10f, PlumeModel.ForBeat(faster, Working(4f)).Speed, 3);
    }

    [Fact]
    public void CindersArcUpAndFallBackLikeTheyHaveMass()
    {
        var hardest = PlumeModel.ForBeat(Tuning, Working(10f, flow: 1f));
        float height = 0f, peak = 0f;
        var velocity = PlumeModel.SparkLaunchSpeed(hardest);
        for (var t = 0f; t < 3f; t += 0.02f)
        {
            velocity = PlumeModel.SparkVelocityAfter(velocity, 0.02f);
            height += velocity * 0.02f;
            peak = Math.Max(peak, height);
        }

        Assert.InRange(peak, 6f, 20f);
        Assert.True(velocity < 0f, "a cinder is falling by the time it burns out");
    }
}
