using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

/// <summary>
/// A real grate does not burn evenly. The front, near the tube plate and the
/// throat under the brick arch, and the sides, where air slips up between the
/// grate and the firebox walls, draw more air than the back under the door,
/// so an evenly fired grate burns through at the front corners first; firing
/// those heavier sends air back toward the door. On a sloping grate a moving
/// loco shakes coal down toward the front.
/// </summary>
public class GrateCharacterTests
{
    private static readonly FireboxParameters Stock = new FireboxParameters();
    private static readonly float HalfLoadAir = 0.5f * Stock.MaxCombustionRate * FireTestKit.StockAirPerCoal;

    private static float AirShare(FireModel fire, GrateZone zone) => fire.Zones[(int)zone].AirFlow / fire.PrimaryAirFlow;

    [Fact]
    public void TheFrontAndSidesDrawMoreAirThanTheBackOnAnEvenBed()
    {
        var fire = FireTestKit.BurningFire(Stock, 60f);
        fire.Step(FireTestKit.Dt, FireTestKit.Inputs(HalfLoadAir));

        Assert.True(AirShare(fire, GrateZone.FrontLeft) > AirShare(fire, GrateZone.Center));
        Assert.True(AirShare(fire, GrateZone.Front) > AirShare(fire, GrateZone.UnderDoor));
        Assert.True(AirShare(fire, GrateZone.MidLeft) > AirShare(fire, GrateZone.Center));
        Assert.True(AirShare(fire, GrateZone.FrontRight) > AirShare(fire, GrateZone.BackRight));
    }

    [Fact]
    public void DraftWeightsKeepTheGratesTotalOnAnEvenBed()
    {
        var fire = new FireModel(Stock);
        var mean = Enumerable.Range(0, GrateLayout.ZoneCount).Average(i => fire.DraftWeight((GrateZone)i));

        Assert.Equal(1.0, mean, 3);
    }

    [Fact]
    public void AnEvenlyFiredGrateBurnsThroughAtTheFrontCornersFirst()
    {
        var fire = FireTestKit.BurningFire(Stock, 40f);
        FireTestKit.Run(fire, FireTestKit.Inputs(HalfLoadAir), 60f);

        var frontCorner = fire.Zones[(int)GrateZone.FrontLeft].Coal;
        var underDoor = fire.Zones[(int)GrateZone.UnderDoor].Coal;
        Assert.True(frontCorner < 0.9f * underDoor, $"front corner {frontCorner:F2} kg, under the door {underDoor:F2} kg");
    }

    [Fact]
    public void FiringTheFrontHeavierSendsAirToTheBack()
    {
        var even = FireTestKit.BurningFire(Stock, 60f);
        var frontHeavy = FireTestKit.BurningFire(Stock, 60f);
        foreach (var zone in new[] { GrateZone.FrontLeft, GrateZone.Front, GrateZone.FrontRight })
            frontHeavy.Zones[(int)zone].Burning += 4f;
        even.Step(FireTestKit.Dt, FireTestKit.Inputs(HalfLoadAir));
        frontHeavy.Step(FireTestKit.Dt, FireTestKit.Inputs(HalfLoadAir));

        Assert.True(AirShare(frontHeavy, GrateZone.UnderDoor) > AirShare(even, GrateZone.UnderDoor));
    }

    [Fact]
    public void UnaimedCoalGoesWhereTheFireNeedsItMost()
    {
        var fire = FireTestKit.BurningFire(Stock, 40f);
        fire.Zones[(int)GrateZone.FrontRight].Burning = 0.5f;

        Assert.Equal(GrateZone.FrontRight, fire.NeediestZone());
    }

    [Fact]
    public void AnEvenlyNeedyGrateIsFedMostAtItsHighDraftZones()
    {
        // Fed only where it needs it, an empty grate builds up the high-draft
        // front corners more than the low-draft back center.
        var fire = new FireModel(Stock);
        for (var i = 0; i < 60; i++)
            fire.AddCoal(1f, fire.NeediestZone());

        Assert.True(fire.Zones[(int)GrateZone.FrontLeft].Coal > fire.Zones[(int)GrateZone.UnderDoor].Coal);
    }

    [Fact]
    public void ASlopingGrateShakesCoalForwardOnlyWhenMoving()
    {
        var standing = FireTestKit.BurningFire(Stock, 60f);
        var moving = FireTestKit.BurningFire(Stock, 60f);
        foreach (var fire in new[] { standing, moving })
            fire.GrateSlope = -7f;
        var still = FireTestKit.Inputs(0f);
        var running = FireTestKit.Inputs(0f);
        running.Speed = 20f;

        FireTestKit.Run(standing, still, 300f);
        FireTestKit.Run(moving, running, 300f);

        float Row(FireModel fire, int row) => Enumerable.Range(0, GrateLayout.Columns).Sum(c => fire.Zones[(int)GrateLayout.At(row, c)].Coal);
        Assert.Equal(Row(standing, 0), Row(standing, 2), 2);
        Assert.True(Row(moving, 2) > 1.05f * Row(moving, 0), $"front {Row(moving, 2):F2} kg, back {Row(moving, 0):F2} kg");
        Assert.Equal(standing.CoalMass, moving.CoalMass, 2);
    }
}
