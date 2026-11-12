using RealisticStackEffects.Core;

namespace RealisticStackEffects.Core.Tests;

public class PlumeTurnsTests
{
    private static readonly float[] FrameTimes = { 0.002f, 0.0069f, 0.011f, 0.0143f, 0.0167f, 0.02f, 0.025f, 0.033f, 0.05f, 0.1f };
    private static readonly float[] Ages = { 0.5f, 1.5f, 3f, 6f, 12f, 60f, 240f };

    [Fact]
    public void EveryParticleHasATurnEachCycleAndIsCountedInExactlyOneOfThem()
    {
        var seeds = new Random(7);
        foreach (var dt in FrameTimes)
        {
            var turns = PlumeTurns.Plan(PlumeTurns.NewestEvery, dt);
            for (var n = 0; n < 200; n++)
            {
                var seed = (uint)seeds.Next(int.MinValue, int.MaxValue);
                foreach (var age in Ages)
                {
                    var due = Enumerable.Range(0, turns.Passes).Count(turn => turns.Due(seed, age, turn));
                    Assert.Equal(turns.Passes / turns.Apart(age), due);
                    Assert.True(due >= 1);
                    var counted = Enumerable.Range(0, turns.Passes).Where(turn => turns.Counted(seed, turn)).ToList();
                    Assert.Single(counted);
                    Assert.True(turns.Due(seed, age, counted[0]));
                }
            }
        }
    }

    [Fact]
    public void ATiersTurnsStayAboutAsFarApartInTimeAtAnyFrameRate()
    {
        foreach (var dt in FrameTimes)
        {
            var turns = PlumeTurns.Plan(PlumeTurns.NewestEvery, dt);
            for (var tier = 0; tier <= PlumeTurns.OldestTier; tier++)
            {
                var age = tier == 0 ? 0.5f : PlumeTurns.YoungestTier * (1 << (tier - 1)) * 1.5f;
                Assert.Equal(tier, PlumeTurns.TierOf(age));
                var wanted = PlumeTurns.NewestEvery * (1 << tier);
                var apart = turns.Apart(age) * dt;
                // Within a power of two's rounding, or every pass when a pass is longer than that.
                Assert.True(apart <= Math.Max(wanted * 1.5f, dt * 1.001f), $"tier {tier} at {dt * 1000:F1} ms: {apart * 1000:F0} ms apart");
                Assert.True(apart >= wanted * 0.7f, $"tier {tier} at {dt * 1000:F1} ms: {apart * 1000:F0} ms apart");
            }
        }
    }

    [Fact]
    public void BelowSixtyFramesASecondOldParticlesNoLongerTakeATurnEveryFrame()
    {
        // 40 frames a second: the frame-time stride was 1 there.
        var turns = PlumeTurns.Plan(PlumeTurns.NewestEvery, 0.025f);
        Assert.Equal(1, turns.Apart(0.5f));
        Assert.Equal(2, turns.Apart(1.5f));
        Assert.Equal(16, turns.Apart(30f));
        Assert.Equal(16, turns.Passes);
        // A particle's share of full turns over a plume of even ages to 30 s: about an eighth.
        var share = Enumerable.Range(0, 3000).Average(i => 1.0 / turns.Apart(i * 0.01f));
        Assert.InRange(share, 0.10, 0.15);
    }

    [Fact]
    public void APlumeWorkedLessOftenSpacesItsTurnsByItsOwnPeriod()
    {
        // Out of view within a kilometre at 40 frames a second: a pass every 4th frame, 100 ms.
        var every = PlumeTurns.FramesApart(false, 500f);
        Assert.Equal(4, every);
        var turns = PlumeTurns.Plan(PlumeTurns.NewestEvery * every, 0.025f * every);
        Assert.Equal(1, turns.Apart(0.5f));
        Assert.Equal(16, turns.Apart(30f));
    }

    [Theory]
    [InlineData(true, 10f, 1)]
    [InlineData(true, 299f, 1)]
    [InlineData(true, 300f, 2)]
    [InlineData(true, 5000f, 4)]
    [InlineData(false, 10f, 4)]
    [InlineData(false, 999f, 4)]
    [InlineData(false, 1000f, 8)]
    public void APlumeInViewCloseByIsCarriedEveryFrameAndOthersLessOften(bool inView, float away, int frames) =>
        Assert.Equal(frames, PlumeTurns.FramesApart(inView, away));
}
