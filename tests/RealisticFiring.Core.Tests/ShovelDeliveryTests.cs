using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

/// <summary>
/// The game's shovel delivers up to 36 kg at once (measured on the S282).
/// Deliveries are split into pieces sized to the firebox so one throw does
/// not bury a single zone.
/// </summary>
public class ShovelDeliveryTests
{
    // Stock S282 and S060 definitions, from the in-game sim graph dump.
    private static readonly FireboxParameters S282 = new FireboxParameters(150f, 120f, 0.5f, 10f);
    private static readonly FireboxParameters S060 = new FireboxParameters(60f, 180f, 0.5f, 4f);

    [Theory]
    [InlineData(150f, 4.1667f)]
    [InlineData(60f, 1.6667f)]
    public void PieceIsAQuarterOfAZonesShareOfCapacity(float capacity, float expected)
    {
        var delivery = new ShovelDelivery(new FireboxParameters(capacity));

        Assert.Equal(expected, delivery.PieceMass, 3);
    }

    [Fact]
    public void AnUnaimedGameShovelfulCoversTheGrate()
    {
        var fire = new FireModel(S282);
        var delivery = new ShovelDelivery(S282);

        var accepted = delivery.Deliver(fire, 36f, aim: null);

        Assert.Equal(36f, accepted, 3);
        var fresh = fire.Zones.Select(z => z.Fresh).ToList();
        Assert.True(fresh.Max() < 1.5f * fresh.Min(), $"zones {string.Join(", ", fresh.Select(f => f.ToString("F1")))}");
    }

    [Fact]
    public void UnaimedSinglePiecesSpreadAcrossTheGrate()
    {
        var fire = new FireModel(S282);
        var delivery = new ShovelDelivery(S282);

        for (var i = 0; i < 9; i++)
            delivery.Deliver(fire, delivery.PieceMass, aim: null);

        var fresh = fire.Zones.Select(z => z.Fresh).ToList();
        Assert.True(fresh.Max() < 1.5f * fresh.Min(), "nine single pieces should make one even round");
    }

    [Fact]
    public void AimedDeliveryLandsAroundTheAimedZone()
    {
        var fire = new FireModel(S282);
        var delivery = new ShovelDelivery(S282);

        delivery.Deliver(fire, 36f, GrateZone.BackLeft);

        var aimed = fire.Zones[(int)GrateZone.BackLeft].Fresh;
        var far = fire.Zones[(int)GrateZone.FrontRight].Fresh;
        Assert.True(aimed > 0.4f * 36f, $"aimed zone got {aimed} kg");
        Assert.True(far < 0.5f, $"far corner got {far} kg");
    }

    [Fact]
    public void AnAimedRealShovelfulLandsWholeInTheAimedZone()
    {
        // The S060 splits unaimed coal into 1.7 kg pieces; a 3 kg aimed throw
        // must still land where it was aimed, apart from the usual spill.
        var fire = new FireModel(S060);
        var delivery = new ShovelDelivery(S060);

        delivery.Deliver(fire, 3f, GrateZone.FrontLeft);

        var nearby = new[] { GrateZone.FrontLeft }.Concat(GrateLayout.Neighbors(GrateZone.FrontLeft)).ToList();
        var aimed = fire.Zones[(int)GrateZone.FrontLeft].Fresh;
        var elsewhere = Enumerable.Range(0, GrateLayout.ZoneCount)
            .Where(i => !nearby.Contains((GrateZone)i))
            .Sum(i => fire.Zones[i].Fresh);
        Assert.True(aimed >= 0.75f * 3f, $"aimed zone got {aimed} kg");
        Assert.Equal(0f, elsewhere, 4);
    }

    [Fact]
    public void DeliveryStopsAtCapacity()
    {
        var fire = new FireModel(S060);
        var delivery = new ShovelDelivery(S060);

        Assert.Equal(36f, delivery.Deliver(fire, 36f, aim: null), 3);
        Assert.Equal(24f, delivery.Deliver(fire, 36f, aim: null), 3);
        Assert.Equal(60f, fire.CoalMass, 3);
    }

    [Theory]
    [InlineData(0.25f)]
    [InlineData(0.75f)]
    public void GameShovelfulsKeepStockParityOnTheS282(float load)
    {
        var rate = load * S282.MaxCombustionRate;
        var inputs = FireTestKit.Inputs(rate * FireTestKit.StockAirPerCoal);
        var target = rate * S282.BurnTime;
        var fire = FireTestKit.BurningFire(S282, target);
        var delivery = new ShovelDelivery(S282);

        double burned = 0, heat = 0;
        var sinceShake = 0f;
        for (var t = 0f; t < 2400f; t += FireTestKit.Dt)
        {
            // A player throws a full 36 kg shovelful whenever there is room
            // and the fire has burned down by that much.
            if (fire.CoalMass + 18f <= target)
                delivery.Deliver(fire, 36f, aim: null);
            if ((sinceShake += FireTestKit.Dt) >= 300f)
            {
                fire.ShakeGrate();
                sinceShake = 0f;
            }

            var consumed = fire.Step(FireTestKit.Dt, inputs);
            if (t >= 1200f)
            {
                burned += consumed;
                heat += fire.Heat * FireTestKit.Dt;
            }
        }

        var burnRate = burned / 1200.0;
        var stockHeatPerCoal = FireTestKit.StockHeat(S282, burnRate) / burnRate;
        Assert.InRange(heat / burned / stockHeatPerCoal, 0.95, 1.05);
    }
}
