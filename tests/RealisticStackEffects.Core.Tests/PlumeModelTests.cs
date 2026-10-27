using RealisticStackEffects.Core;

namespace RealisticStackEffects.Core.Tests;

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
    public void TheBlowersStreamTakesItsOwnWhite()
    {
        // Standing on the blower in air that keeps the mixed stack clear but
        // lets the blower's jet show white: its stream is the jet's.
        var standing = Working(0f, smoke: 0f, flow: 0.1f) with { SteamWhiteness = 0.02f, BlowerWhiteness = 1f };
        var clear = standing with { BlowerWhiteness = 0.02f };
        Assert.True(PlumeModel.Between(Tuning, standing).Color.A > PlumeModel.Between(Tuning, clear).Color.A + 0.2f);
        // Unset, the blower's white is the exhaust's.
        Assert.Equal(0.02f, (Working(0f, flow: 0.1f) with { SteamWhiteness = 0.02f }).BlowerWhiteness);
    }

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
    public void ColorDarkensSteadilyWithSmokeUntilBlack()
    {
        var previous = float.MaxValue;
        for (var smoke = 0f; smoke <= 1.001f; smoke += 0.05f)
        {
            var brightness = Brightness(PlumeModel.ForBeat(Tuning, Working(5f, smoke)).Color);
            if (smoke <= PlumeModel.SootForBlack + 0.001f)
                Assert.True(brightness < previous, $"brightness {brightness} at smoke {smoke}");
            else
                Assert.True(brightness <= previous + 1e-4f, $"brightness {brightness} at smoke {smoke}, past black");
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
    public void ABeatLaunchesFromADriftToTheHardestSpeed()
    {
        // An oil burner's stack launches the game's beats by the same speeds.
        Assert.Equal(3f, PlumeModel.PuffSpeed(Tuning, 0f), 3);
        Assert.Equal(Tuning.LaunchSpeed, PlumeModel.PuffSpeed(Tuning, 1f), 3);
        Assert.True(PlumeModel.PuffSpeed(Tuning, 0.5f) > PlumeModel.PuffSpeed(Tuning, 0.25f));
        Assert.Equal(PlumeModel.PuffSpeed(Tuning, 1f), PlumeModel.ForBeat(Tuning, Working(10f)).Speed, 3);
    }

    [Fact]
    public void BetweenBeatsTheBlowerSpeedsTheColumn()
    {
        Assert.Equal(0f, PlumeModel.SteamThroughStack(0f));
        Assert.Equal(1f, PlumeModel.SteamThroughStack(0.5f));
        Assert.True(PlumeModel.HazeSpeed(1f) > PlumeModel.HazeSpeed(0f));
        var standing = PlumeModel.Between(Tuning, Working(0f, flow: 0f));
        Assert.Equal(PlumeModel.HazeSpeed(0f), standing.Speed, 3);
    }

    [Fact]
    public void AClearStackSendsUpNoHazeBetweenBeats()
    {
        var clear = Working(0f, flow: 0f);
        clear.ClearStack = 1f;
        Assert.Equal(0f, PlumeModel.Between(Tuning, clear).Color.A, 3);
        Assert.True(PlumeModel.Between(Tuning, Working(0f, flow: 0f)).Color.A > 0.1f);
    }

    [Fact]
    public void AStandingCleanFireSendsUpAFaintLazyColumn()
    {
        var haze = PlumeModel.Between(Tuning, Working(1f, smoke: 0f, flow: 0.01f));

        Assert.True(haze.Rate >= 2f, $"{haze.Rate} particles a second");
        Assert.InRange(haze.Speed, 1f, 2.5f);
        Assert.True(haze.Color.A < 0.25f, $"opacity {haze.Color.A}");
    }

    [Fact]
    public void SootGreysTheSteamItRidesIn()
    {
        // A puff and the stack's haze are white steam tinted by their smoke,
        // a little soot greying a lot of steam; a particle keeps that rule as
        // its steam evaporates, so it shows its puff's color as it leaves.
        foreach (var soot in new[] { 0.05f, 0.1f, 0.2f, 0.3f })
        {
            var puff = PlumeModel.ForBeat(Tuning, Working(5f, smoke: soot));
            var tint = PlumeModel.SmokeTint(puff.SmokeShare);
            var expected = PlumeModel.Steam.R + (puff.SmokeColor.R - PlumeModel.Steam.R) * tint;
            Assert.Equal(expected, puff.Color.R, 3);
            Assert.True(tint > puff.SmokeShare + 0.1f, $"soot {soot}: tinted {tint:F2} for a smoke share of {puff.SmokeShare:F2}");
        }
        // A fifth soot greys a working puff to the middle, where it was a light grey.
        Assert.InRange(Brightness(PlumeModel.ForBeat(Tuning, Working(5f, smoke: 0.2f)).Color), 0.4f, 0.6f);
    }

    [Fact]
    public void ACleanFireWithAirToSpareClearsItsStack()
    {
        // Standing on a thin, clean fire, half its air passing unburned: no haze, thinner steam.
        var clear = PlumeModel.CoalClearStack(0f, 0.5f);
        Assert.Equal(1f, clear, 3);
        var standing = Working(1f, smoke: 0f, flow: 0f) with { ClearStack = clear };
        Assert.True(PlumeModel.Between(Tuning, standing).Color.A < 0.02f);
        var drifting = Working(2f) with { ClearStack = clear };
        Assert.True(PlumeModel.ForBeat(Tuning, drifting).Color.A < 0.7f * PlumeModel.ForBeat(Tuning, Working(2f)).Color.A);
        // Working hard it burns nearly all its air: the stack keeps its steam.
        Assert.True(PlumeModel.CoalClearStack(0f, 0.02f) < 0.1f);
        // Any smoke to see and it is not clear, whatever the air.
        Assert.Equal(0f, PlumeModel.CoalClearStack(0.06f, 1f), 3);
    }

    [Fact]
    public void ACleanHotFireShimmersOverTheStack()
    {
        var clean = Working(0f, smoke: 0f, flow: 0.01f) with { HeatShare = 0.3f };
        Assert.True(PlumeModel.Shimmer(Tuning, clean) > 0.8f);
        // Smoke hides it, as do the blower's steam and a hard beat.
        Assert.True(PlumeModel.Shimmer(Tuning, clean with { Soot = 0.4f }) < 0.3f);
        Assert.True(PlumeModel.Shimmer(Tuning, clean with { TotalFlowNormalized = 0.1f }) < 0.4f);
        Assert.True(PlumeModel.Shimmer(Tuning, clean with { ExhaustPressure = 8f }) < 0.3f);
        // A banked fire hardly, a dead one not at all.
        Assert.True(PlumeModel.Shimmer(Tuning, clean with { HeatShare = 0.05f }) < 0.15f);
        Assert.Equal(0f, PlumeModel.Shimmer(Tuning, clean with { FireOn = false }));
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
    public void LightSootOnlyGreysThePuffAndHeavySootBlackensIt()
    {
        Assert.InRange(Brightness(PlumeModel.ForBeat(Tuning, Working(5f, smoke: 0.1f)).Color), 0.6f, 0.75f);
        Assert.True(Brightness(PlumeModel.ForBeat(Tuning, Working(5f, smoke: PlumeModel.SootForBlack)).Color) < 0.25f);
    }

    [Fact]
    public void ASootyPuffTurnsItsSmokesColorAsItsSteamGoes()
    {
        var puff = PlumeModel.ForBeat(Tuning, Working(5f, smoke: 0.3f));
        Assert.True(Brightness(puff.SmokeColor) < Brightness(puff.Color) - 0.2f);
        // A clean fire's haze between beats is the haze's own grey-brown.
        var haze = PlumeModel.Between(Tuning, Working(0f, flow: 0f));
        Assert.True(Brightness(haze.SmokeColor) > 0.6f);
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
