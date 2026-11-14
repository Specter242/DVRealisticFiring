using RealisticStackEffects.Core;

namespace RealisticStackEffects.Core.Tests;

 public class ExhaustBlastTests
{
    private static List<(float At, float Flow)> Run(BeatRelease beat, float seconds, float turnsASecond, float step = 1f / 240f)
    {
        var left = new List<(float, float)>();
        for (var t = 0f; t < seconds; t += step)
        {
            ExhaustBlast.Step(ref beat, step, turnsASecond * step);
            while (ExhaustBlast.Next(ref beat, out var flow, out var due))
                left.Add((beat.Age - due, flow));
        }
        return left;
    }

    [Theory]
    [InlineData(2f, 0.41f)]
    [InlineData(4f, 0.66f)]
    [InlineData(12f, 0.85f)]
    public void AHarderReleaseBlowsMoreOfItsSteamDownAtOnce(float pressure, float share)
    {
        Assert.Equal(share, ExhaustBlast.BlowdownShare(pressure), 2);
    }

    [Fact]
    public void ABeatBarelyOverTheBackPressureStillBlowsALittle()
    {
        Assert.Equal(0.1f, ExhaustBlast.BlowdownShare(1f), 3);
        Assert.Equal(0.1f, ExhaustBlast.BlowdownShare(0.5f), 3);
    }

    [Fact]
    public void EveryParticleLeavesOnceByTheEndOfTheReturnStroke()
    {
        var beat = ExhaustBlast.Begin(20, 6f);
        Assert.Equal(20, beat.Blown + beat.Displaced);

        // One turn a second: the return stroke is half a second.
        var left = Run(beat, 0.6f, 1f);

        Assert.Equal(20, left.Count);
    }

    [Fact]
    public void TheBlowdownIsOverInAFewHundredthsOfASecond()
    {
        var beat = ExhaustBlast.Begin(20, 12f);

        var left = Run(beat, 0.25f, 0f);

        // The crank stopped: only the blowdown's particles, half of them
        // within 0.7 of its time constant.
        Assert.Equal(beat.Blown, left.Count);
        Assert.True(left[beat.Blown / 2 - 1].At < 0.7f * ExhaustBlast.BlowdownTime + 0.01f);
        Assert.True(left[^1].At < 0.25f);
    }

    [Fact]
    public void TheFlowFallsThroughTheBlowdown()
    {
        var beat = ExhaustBlast.Begin(12, 12f);

        var left = Run(beat, 0.25f, 0f);

        Assert.True(left[0].Flow > 0.9f);
        for (var i = 1; i < left.Count; i++)
            Assert.True(left[i].Flow < left[i - 1].Flow);
        Assert.True(left[^1].Flow < 0.1f);
    }

    [Fact]
    public void TheReturnStrokeGoesByTheCrankNotTheClock()
    {
        var slow = Run(ExhaustBlast.Begin(20, 2f), 3f, 0.25f);
        var fast = Run(ExhaustBlast.Begin(20, 2f), 3f, 4f);

        // Half a turn takes 2 s at a quarter turn a second, an eighth of a second at four.
        Assert.Equal(20, slow.Count);
        Assert.Equal(20, fast.Count);
        Assert.InRange(slow[^1].At, 1.5f, 2.05f);
        Assert.InRange(fast[^1].At, 0.08f, 0.2f);
    }

    [Fact]
    public void AStoppedCrankKeepsTheCylindersSteamOnlySoLong()
    {
        var beat = ExhaustBlast.Begin(20, 2f);

        Run(beat, 0.5f, 0f);
        Assert.False(beat.Done);
        for (var i = 0; i < 40; i++)
            ExhaustBlast.Step(ref beat, 0.1f, 0f);

        Assert.True(beat.Done);
        Assert.False(ExhaustBlast.Next(ref beat, out _, out _));
    }

    [Fact]
    public void SlowHardBeatsAreBurstsAndFastOnesRunTogether()
    {
        // The longest the stack waits for a particle, against the beats' spacing.
        static float LongestGap(float beatsASecond, float pressure)
        {
            var times = new List<float>();
            var period = 1f / beatsASecond;
            for (var b = 0; b < 12; b++)
                foreach (var (at, _) in Run(ExhaustBlast.Begin(12, pressure), 6f * period + 0.5f, beatsASecond / 4f))
                    times.Add(b * period + at);
            times.Sort();
            var longest = 0f;
            // Between the beats under way, not the first's start or the last's tail.
            for (var i = 1; i < times.Count; i++)
                if (times[i] > 4f * period && times[i] < 8f * period)
                    longest = Math.Max(longest, times[i] - times[i - 1]);
            return longest;
        }

        // Starting: a quarter second between chuffs, the stack never bare for half of it.
        Assert.True(LongestGap(4f, 12f) < 0.125f);
        // At speed, 22 beats a second: an even stream.
        Assert.True(LongestGap(22f, 3f) < 0.01f);
    }

    [Fact]
    public void AParticleLeavesAsFastAsTheStacksFlow()
    {
        Assert.Equal(18f, ExhaustBlast.Speed(1f, 18f, 3f), 3);
        Assert.Equal(3f, ExhaustBlast.Speed(0f, 18f, 3f), 3);
        Assert.Equal(10.5f, ExhaustBlast.Speed(0.5f, 18f, 3f), 3);
        // A beat weaker than the least leaves at its own speed.
        Assert.Equal(3f, ExhaustBlast.Speed(1f, 2f, 3f), 3);
    }

    [Fact]
    public void ABeatCrossedEarlierInTheFrameHasGoneOnSince()
    {
        var beat = ExhaustBlast.Begin(10, 6f);

        ExhaustBlast.Step(ref beat, 0.02f, 0.01f);
        var first = ExhaustBlast.Next(ref beat, out _, out var due);

        Assert.True(first);
        Assert.InRange(due, 0.015f, 0.02f);
    }
}
