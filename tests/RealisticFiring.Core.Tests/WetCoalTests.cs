using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

 public class WetCoalTests
{
    private static readonly FireboxParameters S282 = new FireboxParameters(150f, 120f, 0.5f);
    private static readonly FireboxParameters Hudson = new FireboxParameters(180f, 90f, 0.6f);

    [Fact]
    public void RainSoaksTheCoalAndItDriesBackAfter()
    {
        var coal = new CoalMoisture();
        Assert.Equal(CoalMoisture.Delivered, coal.Moisture, 4);

        // A quarter of an hour of hard rain: most of the way to soaked.
        for (var t = 0f; t < 900f; t += 1f)
            coal.Step(1f, 1f);
        Assert.InRange(coal.Moisture, 0.07f, CoalMoisture.Soaked);
        for (var t = 0f; t < 3600f; t += 1f)
            coal.Step(1f, 1f);
        Assert.Equal(CoalMoisture.Soaked, coal.Moisture, 3);

        // The weather dries: the coal drains back slower than it soaked.
        for (var t = 0f; t < 900f; t += 1f)
            coal.Step(1f, 0f);
        Assert.True(coal.Moisture > 0.07f, $"dried to {coal.Moisture:P1} in a quarter hour");
        for (var t = 0f; t < 6f * 3600f; t += 10f)
            coal.Step(10f, 0f);
        Assert.Equal(CoalMoisture.Delivered, coal.Moisture, 3);
    }

    [Fact]
    public void CoalLoadedAtTheStageMixesWithWhatIsLeft()
    {
        var coal = new CoalMoisture();
        coal.Restore(CoalMoisture.Soaked);
        // A quarter of the tender left, soaked; filled up with dry coal.
        coal.Load(2000f, 6000f);
        Assert.Equal((CoalMoisture.Soaked * 2000f + CoalMoisture.Delivered * 6000f) / 8000f, coal.Moisture, 4);
    }

    // The fire's heat summed over a while, with each shovelful as wet as <paramref name="moisture"/>.
    private static (float Heat, float Coal) Fire(float moisture, float seconds = 600f)
    {
        var fire = FireTestKit.BurningFire(S282, 60f, FireTestKit.CleanCoal);
        fire.FeedMoisture = moisture;
        var inputs = FireTestKit.Inputs(0.5f * S282.MaxCombustionRate * FireTestKit.StockAirPerCoal);
        var heat = 0f;
        var coal = 0f;
        for (var t = 0f; t < seconds; t += FireTestKit.Dt)
        {
            if (fire.CoalMass < 60f)
                coal += fire.AddCoalEvenly(60f - fire.CoalMass);
            fire.Step(FireTestKit.Dt, inputs);
            heat += fire.Heat * FireTestKit.Dt;
        }
        return (heat, coal);
    }

    [Fact]
    public void CoalAsDeliveredFiresAsTheStockCoal()
    {
        var dry = Fire(0f);
        var delivered = Fire(CoalMoisture.Delivered);
        Assert.Equal(dry.Heat, delivered.Heat, 0);
    }

    [Fact]
    public void RainWaterTakesItsHeatUpTheStack()
    {
        // Soaked coal: about 0.08 kg of rain water on each kg fired, at 2.79 MJ a kg.
        var dry = Fire(CoalMoisture.Delivered);
        var wet = Fire(CoalMoisture.Soaked);
        var water = CoalMoisture.WaterPerKg(CoalMoisture.Soaked) - CoalMoisture.WaterPerKg(CoalMoisture.Delivered);
        var expected = water * FireModel.DryingEnergy;
        var perKgDry = dry.Heat / dry.Coal;
        var perKgWet = wet.Heat / wet.Coal;
        // The drying, and about as much again: the slower-catching coal keeps
        // more of the bed green and burns less of the same draft, the air
        // left over cooling the box.
        Assert.InRange(perKgDry - perKgWet, 0.8f * expected, 2.5f * expected);
        // The drying about 1 per cent of the heat for 10 per cent of moisture (Kreisinger, 1915).
        Assert.InRange(expected / perKgDry, 0.005f, 0.015f);
        Assert.InRange((perKgDry - perKgWet) / perKgDry, 0.005f, 0.03f);
    }

    [Fact]
    public void WetCoalCatchesSlower()
    {
        float FreshAfter(float moisture)
        {
            var fire = FireTestKit.BurningFire(S282, 40f, FireTestKit.CleanCoal);
            fire.FeedMoisture = moisture;
            fire.AddCoal(12f, GrateZone.Center);
            FireTestKit.Run(fire, FireTestKit.Inputs(0.5f * S282.MaxCombustionRate * FireTestKit.StockAirPerCoal), 20f);
            return fire.FreshMass;
        }

        var dry = FreshAfter(CoalMoisture.Delivered);
        var wet = FreshAfter(CoalMoisture.Soaked);
        Assert.True(wet > dry * 1.1f, $"wet coal left {wet:F2} kg fresh against dry {dry:F2}");
    }

    [Fact]
    public void WaterGoesWithTheFreshCoalDumped()
    {
        var fire = FireTestKit.BurningFire(S282, 0f, FireTestKit.CleanCoal);
        fire.Extinguish();
        fire.FeedMoisture = CoalMoisture.Soaked;
        fire.AddCoalEvenly(40f);
        var water = fire.FreshWater;
        Assert.True(water > 0f);
        fire.RemoveCoal(20f);
        fire.Step(FireTestKit.Dt, FireTestKit.Inputs(1f));
        Assert.Equal(water / 2f, fire.FreshWater, 2);
    }

    private static float[] Landing(float moisture, out float lost)
    {
        var lumps = new float[GrateLayout.ZoneCount];
        var fines = new float[GrateLayout.ZoneCount];
        var stoker = new Stoker(new StokerSpec(Hudson, 2.5f) { MeanCoalBetweenJams = 0f }, Hudson, new Random(1));
        lost = stoker.Distribute(StokerControls.Working(0.5f), 15f, 0.8f * Hudson.MaxCombustionRate * FireModel.AirPerCoal, lumps, fines, moisture);
        return lumps.Zip(fines, (l, f) => l + f).ToArray();
    }

    [Fact]
    public void DampFinesStayOnTheFire()
    {
        Landing(CoalMoisture.Delivered, out var dryLost);
        Landing(0.075f, out var dampLost);
        Assert.True(dampLost < dryLost * 0.9f, $"damp coal lost {dampLost:P1}, dry {dryLost:P1}");
    }

    [Fact]
    public void SoakedCoalSticksOnTheTableAndFallsShort()
    {
        var dry = Landing(CoalMoisture.Delivered, out _);
        var wet = Landing(CoalMoisture.Soaked, out _);
        // Row 0 is the back of the grate, under the door.
        float Back(float[] shares) => Enumerable.Range(0, GrateLayout.Columns).Sum(c => shares[(int)GrateLayout.At(0, c)]);
        float Front(float[] shares) => Enumerable.Range(0, GrateLayout.Columns).Sum(c => shares[(int)GrateLayout.At(GrateLayout.Rows - 1, c)]);
        Assert.True(Front(wet) < Front(dry), $"front {Front(wet):P1} wet, {Front(dry):P1} dry");
        Assert.True(Back(wet) > Back(dry), $"back {Back(wet):P1} wet, {Back(dry):P1} dry");
    }
}
