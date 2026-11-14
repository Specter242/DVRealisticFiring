using System.Collections.Generic;
using System.Linq;
using Xunit;
namespace RealisticFiring.Core.Tests;

public class SupplyPlanTests
{
    private static SupplyPlan.Candidate Shed(string area, string name, float x, float z = 0f) => new SupplyPlan.Candidate(area, name, true, x, z);
    private static SupplyPlan.Candidate Spawn(string area, string name, float x, float z = 0f) => new SupplyPlan.Candidate(area, name, false, x, z);

    [Fact] public void AShedComesBeforeTheSpawnTracksInItsStalls()
    {
        // Harbor: the roundhouse's repair stall, and two steam spawn stalls beside it.
        var chosen = SupplyPlan.Choose(new[]
        {
            Spawn("HB", "spawn [Y]_[HB]_[T1-02-P]", 10f),
            Spawn("HB", "spawn [Y]_[HB]_[T1-05-P]", 25f),
            Shed("HB", "shed T_HB_1", 0f),
        });
        Assert.Equal(new[] { "shed T_HB_1" }, chosen.Select(c => c.Name));
    }

    [Fact] public void AFarSpawnTrackIsASecondPlaceInItsYard()
    {
        // Machine Factory: the roundhouse, its stalls, and the yard's A-01-P well away.
        var chosen = SupplyPlan.Choose(new[]
        {
            Shed("MF", "shed T_MF_1", 0f),
            Spawn("MF", "spawn [Y]_[MF]_[T1-02-P]", 12f),
            Spawn("MF", "spawn [Y]_[MF]_[A-01-P]", 400f),
        });
        Assert.Equal(new[] { "shed T_MF_1", "spawn [Y]_[MF]_[A-01-P]" }, chosen.Select(c => c.Name));
    }

    [Fact] public void NoYardGetsMoreThanTwo()
    {
        var chosen = SupplyPlan.Choose(new[]
        {
            Shed("SM", "shed T_SM_1", 0f),
            Shed("SM", "shop SM-A-02-P at 26.3", 300f),
            Spawn("SM", "spawn [Y]_[SM]_[A-09-P]", 800f),
        });
        Assert.Equal(2, chosen.Count);
        Assert.All(chosen, c => Assert.True(c.Shed));
    }

    [Fact] public void EachYardWithOnlyASpawnTrackGetsOne()
    {
        var chosen = SupplyPlan.Choose(new[]
        {
            Spawn("CME", "spawn [Y]_[CME]_[A-01-P]", 0f),
            Spawn("FRC", "spawn [Y]_[FRC]_[A-01-P]", 5000f),
            Spawn("FRS", "spawn [Y]_[FRS]_[A-01-P]", 9000f),
        });
        Assert.Equal(3, chosen.Count);
    }

    [Fact] public void PlacesCloseTogetherAcrossYardsAreOnePlace()
    {
        var chosen = SupplyPlan.Choose(new[]
        {
            Spawn("A", "spawn a", 0f),
            Spawn("B", "spawn b", 60f),
        });
        Assert.Single(chosen);
    }

    [Fact] public void TheChoiceDoesNotDependOnTheOrderFound()
    {
        var places = new[]
        {
            Spawn("MF", "spawn [Y]_[MF]_[A-01-P]", 400f),
            Spawn("MF", "spawn [Y]_[MF]_[T1-04-P]", 30f),
            Shed("MF", "shed T_MF_1", 0f),
            Spawn("IMW", "spawn [Y]_[IMW]_[T1-01-P]", 3000f),
            Shed("IMW", "shed T_IMW_1", 3010f),
        };
        var once = SupplyPlan.Choose(places).Select(c => c.Name).ToList();
        var again = SupplyPlan.Choose(places.Reverse()).Select(c => c.Name).ToList();
        Assert.Equal(once, again);
        Assert.Equal(new[] { "shed T_IMW_1", "shed T_MF_1", "spawn [Y]_[MF]_[A-01-P]" }, once);
    }

    [Fact] public void ACandidateFoundTwiceIsTakenOnce()
    {
        var chosen = SupplyPlan.Choose(new[] { Spawn("FM", "spawn x", 0f), Spawn("FM", "spawn x", 500f) });
        Assert.Single(chosen);
    }

    [Fact] public void ABrokenPositionIsPassedBy()
    {
        var chosen = SupplyPlan.Choose(new[] { Spawn("FM", "spawn nan", float.NaN), Spawn("FM", "spawn ok", 0f) });
        Assert.Equal(new[] { "spawn ok" }, chosen.Select(c => c.Name));
        Assert.Empty(SupplyPlan.Choose(null));
    }

    [Fact] public void SpotsTryThePreferredSideFirstAndStayClearOfTheirOwnTrack()
    {
        var spots = SupplyPlan.Spots(-1).ToList();
        Assert.Equal(0f, spots[0].Along);
        Assert.Equal(-1, spots[0].Side);
        Assert.Contains(spots, s => s.Along == 0f && s.Side == 1);
        Assert.All(spots, s => Assert.True(SupplyPlan.ClearOfOwnTrack(s.Aside), s.ToString()));
        Assert.Equal(spots.Count, spots.Select(s => (s.Along, s.Side, s.Aside)).Distinct().Count());
    }

    [Fact] public void InsideAYardTheSpotsReachPastTheNeighbouringTracks()
    {
        // A spawn track with two neighbours each side, 4.5 m apart: the
        // first clear spot is past the outermost.
        var tracks = new[] { -9f, -4.5f, 4.5f, 9f }
            .Select(x => (IReadOnlyList<(float X, float Z)>)new List<(float X, float Z)> { (x, -100f), (x, 100f) }).ToList();
        var spots = SupplyPlan.Spots(1).ToList();
        var firstClear = spots.FindIndex(s => SupplyPlan.ClearOfTracks(s.Side * s.Aside, s.Along, tracks));
        Assert.True(firstClear >= 0);
        Assert.True(spots[firstClear].Aside >= 9f + SupplyPlan.TrackClearance + SupplyPlan.HalfAlong);
        Assert.Equal(0f, spots[firstClear].Along);
        // Every near spot comes before any far one.
        var lastNear = spots.FindLastIndex(s => s.Aside < 5f);
        var firstFar = spots.FindIndex(s => s.Aside >= 5f);
        Assert.True(lastNear < firstFar);
    }

    [Fact] public void AFootprintBetweenYardTracksIsNotClear()
    {
        // Tracks 4.5 m apart: the gap's middle is 2.25 m from each.
        var neighbour = new List<(float X, float Z)> { (4.5f, -50f), (4.5f, 50f) };
        Assert.False(SupplyPlan.ClearOfTracks(2.25f, 0f, new[] { neighbour }));
        // Past the yard's edge, with nothing beyond, it is.
        Assert.True(SupplyPlan.ClearOfTracks(-3.0f, 0f, new[] { neighbour }));
    }

    [Fact] public void AGenericSpawnTrackTakesItsYardFromItsName()
    {
        Assert.Equal("CMS", SupplyPlan.YardInTrackName("[Y]_[CMS]_[P1]"));
        Assert.Equal("CP", SupplyPlan.YardInTrackName("[Y]_[CP]_[P1]"));
        Assert.Equal("HB", SupplyPlan.YardInTrackName("[Y]_[HB]_[T1-02-P]"));
        Assert.Null(SupplyPlan.YardInTrackName("#Y-#S-180-#T"));
        Assert.Null(SupplyPlan.YardInTrackName("[Y]_[]_[P1]"));
        Assert.Null(SupplyPlan.YardInTrackName(null));
        // Four such tracks in four yards are four places, not two in one.
        var chosen = SupplyPlan.Choose(new[] { "CMS", "CP", "CS", "OR" }
            .Select((y, i) => Spawn(SupplyPlan.YardInTrackName($"[Y]_[{y}]_[P1]"), $"spawn [Y]_[{y}]_[P1]", i * 1000f)));
        Assert.Equal(4, chosen.Count);
    }

    [Fact] public void TheHostStocksAPlaceAtTheSpotAGuestFoundBesideIt()
    {
        // The far spot 12 m along and 15 m aside, the guest 40 m off.
        Assert.True(SupplyPlan.AcceptsGuestSpot(1000f, 2000f, 1012f, 2015f, 1050f, 2030f, 150f));
        // Beside a shed's stands, a little farther out.
        Assert.True(SupplyPlan.AcceptsGuestSpot(0f, 0f, 0f, 28f, 0f, 0f, 150f));
    }

    [Fact] public void AGuestSpotAwayFromThePlaceOrFromTheGuestIsRefused()
    {
        // Another yard's spot under this place's name.
        Assert.False(SupplyPlan.AcceptsGuestSpot(0f, 0f, 400f, 0f, 400f, 0f, 150f));
        // A guest asking from far beyond where they could see it.
        Assert.False(SupplyPlan.AcceptsGuestSpot(0f, 0f, 5f, 5f, 600f, 0f, 150f));
        // Within the range and its slack, still taken.
        Assert.True(SupplyPlan.AcceptsGuestSpot(0f, 0f, 5f, 0f, 5f + 150f + SupplyPlan.GuestRangeSlack - 1f, 0f, 150f));
    }

    [Fact] public void AGuestSpotThatIsNoNumberIsRefused()
    {
        Assert.False(SupplyPlan.AcceptsGuestSpot(0f, 0f, float.NaN, 0f, 0f, 0f, 150f));
        Assert.False(SupplyPlan.AcceptsGuestSpot(0f, 0f, 0f, float.PositiveInfinity, 0f, 0f, 150f));
        Assert.False(SupplyPlan.AcceptsGuestSpot(0f, 0f, 0f, 0f, float.NaN, 0f, 150f));
    }

    [Fact] public void DistanceToALineMeasuresToItsSegmentsNotOnlyItsPoints()
    {
        // Points 40 m apart; the footprint sits beside the segment's middle.
        var line = new List<(float X, float Z)> { (0f, -20f), (0f, 20f) };
        Assert.Equal(3f, SupplyPlan.DistanceToLine(3f, 0f, line), 3);
        Assert.Equal(5f, SupplyPlan.DistanceToLine(3f, 24f, line), 3);
        Assert.True(float.IsPositiveInfinity(SupplyPlan.DistanceToLine(0f, 0f, new List<(float X, float Z)>())));
    }
}
