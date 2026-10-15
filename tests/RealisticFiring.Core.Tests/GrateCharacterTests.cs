using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

/// <summary>
/// A real grate does not burn evenly. Under a brick arch sealed to the tube
/// plate the gases from the back take the shortest way to the tubes, so the
/// back under the door draws hardest and the front under the arch least
/// (Acker, RME Apr. 1930: the front pulled 70 % of the back), and the sides,
/// where air slips up beside the firebox walls, more than the middle. An
/// evenly fired grate burns through at the back corners first; firing them
/// heavier sends air forward. On a sloping grate a moving loco shakes coal
/// down toward the front.
/// </summary>
public class GrateCharacterTests
{
    private static readonly FireboxParameters Stock = new FireboxParameters();
    private static readonly float HalfLoadAir = 0.5f * Stock.MaxCombustionRate * FireTestKit.StockAirPerCoal;

    private static float AirShare(FireModel fire, GrateZone zone) => fire.Zones[(int)zone].AirFlow / fire.PrimaryAirFlow;

    [Fact]
    public void TheBackAndSidesDrawMoreAirThanTheFrontOnAnEvenBed()
    {
        var fire = FireTestKit.BurningFire(Stock, 60f);
        fire.Step(FireTestKit.Dt, FireTestKit.Inputs(HalfLoadAir));

        Assert.True(AirShare(fire, GrateZone.BackLeft) > AirShare(fire, GrateZone.Center));
        Assert.True(AirShare(fire, GrateZone.UnderDoor) > AirShare(fire, GrateZone.Front));
        Assert.True(AirShare(fire, GrateZone.MidLeft) > AirShare(fire, GrateZone.Center));
        Assert.True(AirShare(fire, GrateZone.BackRight) > AirShare(fire, GrateZone.FrontRight));
    }

    [Fact]
    public void DraftWeightsKeepTheGratesTotalOnAnEvenBed()
    {
        var fire = new FireModel(Stock);
        var mean = Enumerable.Range(0, GrateLayout.ZoneCount).Average(i => fire.DraftWeight((GrateZone)i));

        Assert.Equal(1.0, mean, 3);
    }

    [Fact]
    public void AnEvenlyFiredGrateBurnsThroughAtTheBackCornersFirst()
    {
        var fire = FireTestKit.BurningFire(Stock, 40f);
        FireTestKit.Run(fire, FireTestKit.Inputs(HalfLoadAir), 60f);

        var backCorner = fire.Zones[(int)GrateZone.BackLeft].Coal;
        var front = fire.Zones[(int)GrateZone.Front].Coal;
        Assert.True(backCorner < 0.9f * front, $"back corner {backCorner:F2} kg, front {front:F2} kg");
    }

    [Fact]
    public void FiringTheBackHeavierSendsAirToTheFront()
    {
        var even = FireTestKit.BurningFire(Stock, 60f);
        var backHeavy = FireTestKit.BurningFire(Stock, 60f);
        foreach (var zone in new[] { GrateZone.BackLeft, GrateZone.UnderDoor, GrateZone.BackRight })
            backHeavy.Zones[(int)zone].Burning += 4f;
        even.Step(FireTestKit.Dt, FireTestKit.Inputs(HalfLoadAir));
        backHeavy.Step(FireTestKit.Dt, FireTestKit.Inputs(HalfLoadAir));

        Assert.True(AirShare(backHeavy, GrateZone.Front) > AirShare(even, GrateZone.Front));
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
        // back corners more than the low-draft front center.
        var fire = new FireModel(Stock);
        for (var i = 0; i < 60; i++)
            fire.AddCoal(1f, fire.NeediestZone());

        Assert.True(fire.Zones[(int)GrateZone.BackLeft].Coal > fire.Zones[(int)GrateZone.Front].Coal);
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
