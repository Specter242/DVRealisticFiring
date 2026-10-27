using RealisticStackEffects.Core;

namespace RealisticStackEffects.Core.Tests;

/// <summary>
/// The stock engine reports beats through a port that only fires on change,
/// so beats in the same frame merge. Counting crank-angle boundaries gives
/// every beat its own puff: 4 per revolution on a 2-cylinder engine.
/// </summary>
public class ExhaustBeatCounterTests
{
    [Fact]
    public void FirstReadingIsNotABeat()
    {
        var counter = new ExhaustBeatCounter(4);

        Assert.Equal(0, counter.Advance(0.3f));
    }

    [Fact]
    public void EveryBoundaryCrossedIsABeat()
    {
        var counter = new ExhaustBeatCounter(4);
        counter.Advance(0.0f);

        Assert.Equal(0, counter.Advance(0.2f));
        Assert.Equal(1, counter.Advance(0.3f));
        Assert.Equal(2, counter.Advance(0.8f));
    }

    [Fact]
    public void CountsAcrossTheWrap()
    {
        var counter = new ExhaustBeatCounter(4);
        counter.Advance(0.9f);

        Assert.Equal(1, counter.Advance(0.1f));
    }

    [Fact]
    public void RunningInReverseAlsoBeats()
    {
        var counter = new ExhaustBeatCounter(4);
        counter.Advance(0.3f);

        Assert.Equal(1, counter.Advance(0.2f));
        Assert.Equal(1, counter.Advance(0.9f));
    }

    [Fact]
    public void ThreeCylindersBeatSixTimesARevolution()
    {
        var counter = new ExhaustBeatCounter(6);
        var beats = 0;
        counter.Advance(0f);
        for (var i = 1; i <= 100; i++)
            beats += counter.Advance(i / 100f % 1f);

        Assert.Equal(6, beats);
    }

    [Fact]
    public void RejectsNoBeats()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExhaustBeatCounter(0));
    }
}
