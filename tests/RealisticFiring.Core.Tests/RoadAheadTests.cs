using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

 public class RoadAheadTests
{
    // A track of points every metre from its start, at heights given by distance from its start.
    private sealed class Track : IRoadTrack
    {
        private readonly float[] heights;

        public Track(string name, float length, Func<float, float> height)
        {
            Name = name;
            heights = new float[(int)length + 1];
            for (var i = 0; i < heights.Length; i++)
                heights[i] = height(i);
        }

        public string Name { get; }
        public int Count => heights.Length;
        public double Span(int index) => index;
        public float Height(int index) => heights[index];

        // What lies beyond its end (true) or its start, and whether the road enters it at its start.
        public (Track Track, bool AtStart)? AtEnd;
        public (Track Track, bool AtStart)? AtStart;
    }

    private static List<RoadPoint> Walk(Track from, double span, bool forward, float distance, float spacing = 10f) =>
        RoadAhead.Walk(from, span, forward, distance, spacing, t => t, (t, atEnd) => atEnd ? t.AtEnd : t.AtStart);

    [Fact]
    public void ItFollowsTheRouteWhicheverWayEachTrackRuns()
    {
        // A: 1,000 m level. B joins A's end at B's own end, so the road runs
        // B backwards, climbing 1 % the way the train goes. C joins B's start
        // at C's start: 500 m level, then the road ends.
        var a = new Track("A", 1000f, _ => 100f);
        var b = new Track("B", 500f, d => 105f - 0.01f * d);
        var c = new Track("C", 500f, _ => 105f);
        a.AtEnd = (b, false);
        b.AtStart = (c, true);

        var road = Walk(a, 800.0, forward: true, distance: 2000f);

        Assert.Equal(0f, road[0].Distance);
        Assert.Equal(100f, road[0].Height, 2);
        Assert.Equal(0f, RoadAhead.Grade(road, 0f, 200f), 4);
        Assert.Equal(0.01f, RoadAhead.Grade(road, 200f, 700f), 3);
        Assert.Equal(0f, RoadAhead.Grade(road, 700f, 1150f), 3);
        // It ends where the track does: 200 + 500 + 500 m.
        Assert.InRange(road[^1].Distance, 1190f, 1201f);
    }

    [Fact]
    public void BackwardsOnATrackItReadsTheGradeTheWayTheTrainGoes()
    {
        var hill = new Track("hill", 1000f, d => 0.02f * d);
        var road = Walk(hill, 900.0, forward: false, distance: 500f);
        Assert.Equal(-0.02f, RoadAhead.Grade(road, 0f, 400f), 4);
    }

    [Fact]
    public void ItStopsWhereItWasAsked()
    {
        var a = new Track("A", 5000f, _ => 0f);
        var road = Walk(a, 0.0, forward: true, distance: 1200f, spacing: 25f);
        Assert.InRange(road[^1].Distance, 1200f, 1226f);
        Assert.All(road.Zip(road.Skip(1)), pair => Assert.InRange(pair.Second.Distance - pair.First.Distance, 24f, 26f));
    }

    [Fact]
    public void TheSteepestStretchIsFoundWhereItStarts()
    {
        // Level, then a 300 m 1.5 % bank starting 600 m out, then level.
        var road = Enumerable.Range(0, 121)
            .Select(i => i * 10f)
            .Select(d => new RoadPoint(d, d < 600f ? 0f : d < 900f ? 0.015f * (d - 600f) : 4.5f))
            .ToList();
        Assert.Equal(0.015f, RoadAhead.Steepest(road, 0f, 1000f, 200f), 3);
        // Looking only at what starts within 300 m, the bank's foot is seen in part.
        var near = RoadAhead.Steepest(road, 0f, 300f, 200f);
        Assert.InRange(near, -0.001f, 0.001f);
        Assert.True(float.IsNaN(RoadAhead.Steepest(road, 2000f, 3000f, 200f)), "past the road's end nothing is known");
    }

    [Fact]
    public void AGradeAheadTakesMoreWorkForAHeavierTrainAndAFasterOne()
    {
        var reckoner = new RoadReckoner(fullWorkPower: 2e6f);
        var light = reckoner.WorkAt(0.3f, 300_000f, 15f, 0f, 0.01f);
        var heavy = reckoner.WorkAt(0.3f, 1_000_000f, 15f, 0f, 0.01f);
        var slow = reckoner.WorkAt(0.3f, 1_000_000f, 5f, 0f, 0.01f);
        Assert.True(heavy > light && light > 0.3f);
        Assert.True(heavy > slow);
        // Over the top the work falls, never below nothing.
        Assert.Equal(0f, reckoner.WorkAt(0.3f, 1_000_000f, 15f, 0.01f, -0.02f));
        // Not knowing the road, he expects what he hears.
        Assert.Equal(0.3f, reckoner.WorkAt(0.3f, 1_000_000f, 15f, float.NaN, 0.01f));
    }

    [Fact]
    public void HeLearnsWhatTheEngineTakesForItsPull()
    {
        // He starts thinking full work pulls 2 MW; the engine really works
        // at half for 0.6 MW of pull (full work pulls 1.2 MW).
        var reckoner = new RoadReckoner(fullWorkPower: 2e6f);
        var mass = 600_000f;
        var speed = 10f;
        var grade = 0.6e6f / (mass * RoadReckoner.Gravity * speed) - RoadReckoner.Rolling;
        for (var t = 0f; t < 600f; t += 0.5f)
            reckoner.Learn(0.5f, 0.5f, mass, speed, grade, steady: true);
        // Five time constants: within 1 % of the truth.
        Assert.InRange(reckoner.WorkPerWatt / (0.5f / 0.6e6f), 0.99f, 1.01f);

        // Unsteady running teaches him nothing.
        var other = new RoadReckoner(fullWorkPower: 2e6f);
        other.Learn(10f, 0.5f, mass, speed, grade, steady: false);
        Assert.Equal(1f / 2e6f, other.WorkPerWatt, 12);
    }
}
