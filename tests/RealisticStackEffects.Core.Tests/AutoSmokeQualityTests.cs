using RealisticStackEffects.Core;

namespace RealisticStackEffects.Core.Tests;

public class AutoSmokeQualityTests
{
    // Runs the governor for a while; the frame time is the game's own plus the smoke's at the governor's quality.
    private static (int Changes, SmokeQuality End) Run(AutoSmokeQuality auto, float seconds, Func<SmokeQuality, float> frame, bool smoke = true, float heldTo = 0f)
    {
        var changes = 0;
        for (var t = 0f; t < seconds;)
        {
            var dt = frame(auto.Quality);
            if (auto.Step(dt, smoke, heldTo))
                changes++;
            t += dt;
        }
        return (changes, auto.Quality);
    }

    private static float Smoke(SmokeQuality quality, float high) =>
        quality == SmokeQuality.High ? high : quality == SmokeQuality.Medium ? 0.6f * high : 0.3f * high;

    [Fact]
    public void AGameRunningWellStaysAtHigh()
    {
        var auto = new AutoSmokeQuality();
        var (changes, end) = Run(auto, 600f, q => 0.012f + Smoke(q, 0.003f));
        Assert.Equal(0, changes);
        Assert.Equal(SmokeQuality.High, end);
    }

    [Fact]
    public void SlowWithSmokeThatCostsStepsDownUntilItRunsWellAndSettles()
    {
        // 14 ms of game and 12 ms of smoke at High: 38 frames a second; Medium 47, Low 57.
        var auto = new AutoSmokeQuality();
        var (changes, end) = Run(auto, 60f, q => 0.014f + Smoke(q, 0.012f));
        Assert.Equal(SmokeQuality.Medium, end);
        Assert.Equal(1, changes);
        // Borderline for an hour: a step up brings the slowness back, and the waits between tries double.
        (changes, end) = Run(auto, 3600f, q => 0.016f + Smoke(q, 0.012f));
        Assert.InRange(changes, 0, 24);
        Assert.NotEqual(SmokeQuality.High, end);
    }

    [Fact]
    public void SlowForAnotherReasonGoesBackToHighAndLeavesTheSmokeAlone()
    {
        // 30 ms of game however the smoke is drawn.
        var auto = new AutoSmokeQuality();
        var (changes, end) = Run(auto, 60f, q => 0.030f + Smoke(q, 0.0005f));
        Assert.Equal(SmokeQuality.High, end);
        Assert.Equal(2, changes);
        Assert.Contains("did not help", auto.Why);
        // Left alone for two minutes, then one more try.
        (changes, end) = Run(auto, 50f, q => 0.030f + Smoke(q, 0.0005f));
        Assert.Equal(0, changes);
    }

    [Fact]
    public void WithNoSmokeInTheAirNothingChanges()
    {
        var auto = new AutoSmokeQuality();
        var (changes, _) = Run(auto, 120f, q => 0.040f, smoke: false);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void ItStepsBackUpOnceTheLoadHasGone()
    {
        var auto = new AutoSmokeQuality();
        Run(auto, 60f, q => 0.014f + Smoke(q, 0.020f));
        Assert.Equal(SmokeQuality.Low, auto.Quality);
        var (_, end) = Run(auto, 120f, q => 0.008f + Smoke(q, 0.002f));
        Assert.Equal(SmokeQuality.High, end);
    }

    [Fact]
    public void AGameHeldToThirtyFramesASecondIsNotSlowAtThirty()
    {
        var auto = new AutoSmokeQuality();
        var (changes, end) = Run(auto, 300f, q => 1f / 30f, heldTo: 30f);
        Assert.Equal(0, changes);
        Assert.Equal(SmokeQuality.High, end);
        // Nor does one held to sixty stay low for want of 65: it steps back up at sixty.
        Run(auto, 60f, q => 0.014f + Smoke(q, 0.020f), heldTo: 60f);
        Assert.NotEqual(SmokeQuality.High, auto.Quality);
        (_, end) = Run(auto, 200f, q => 1f / 60f, heldTo: 60f);
        Assert.Equal(SmokeQuality.High, end);
    }

    [Fact]
    public void HitchesAndLoadingScreensAreNotCounted()
    {
        var auto = new AutoSmokeQuality();
        for (var i = 0; i < 200; i++)
        {
            Assert.False(auto.Step(0.5f, true));
            Assert.False(auto.Step(0.012f, true));
        }
        Assert.Equal(SmokeQuality.High, auto.Quality);
    }

    [Theory]
    [InlineData(SmokeQuality.High, false, 1f)]
    [InlineData(SmokeQuality.Auto, false, 1f)]
    [InlineData(SmokeQuality.Medium, true, 1f)]
    [InlineData(SmokeQuality.Low, false, 0.25f)]
    public void AutoItselfIsDrawnAsHigh(SmokeQuality quality, bool halfResolution, float share)
    {
        Assert.Equal(share, SmokeDetail.ParticleShare(quality, halfResolution));
        Assert.Equal(quality == SmokeQuality.Medium || quality == SmokeQuality.Low, SmokeDetail.HalfResolution(quality));
    }
}
