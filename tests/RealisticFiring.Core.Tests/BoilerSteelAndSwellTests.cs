using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

/// <summary>
/// The boiler's steel holds heat beside the water and gives it back as the
/// pressure falls; the glass swells with the steam in the water, jumps as
/// the pressure falls, and drops as it rises or as cold feed goes in.
/// </summary>
public class BoilerSteelAndSwellTests
{
    private const float Dt = 0.02f;

    // An S282's boiler steaming at 14 bar: 2,000 kJ/kg to boil, 6 kg/s full steam.
    private static BoilerSwellInputs Steaming(float heat, float pressure = 14f, float fed = 0f, float subcooling = 0f) => new BoilerSwellInputs
    {
        Heat = heat,
        LatentHeat = 2.0e6f,
        FullSteam = 6f,
        Fed = fed,
        FeedSubcooling = subcooling,
        Pressure = pressure,
        Boiling = true,
    };

    [Fact]
    public void The_steel_holds_about_a_quarter_of_the_waters_heat()
    {
        var steel = new BoilerMetal(10000f, 100f);
        var water = 10000f * 4190f;
        Assert.InRange(steel.HeatCapacity / water, 0.25f, 0.32f);
    }

    [Fact]
    public void The_steel_follows_the_water_and_gives_its_heat_back()
    {
        var steel = new BoilerMetal(10000f, 190f);
        var water = 10000f * BoilerMetal.WaterHeat;
        var given = 0f;
        for (var t = 0f; t < 5f * BoilerMetal.FollowTime; t += Dt)
            given += steel.Exchange(Dt, 180f, water);
        Assert.InRange(steel.Temperature, 180f, 180.1f);
        Assert.Equal(steel.HeatCapacity * 10f, given, steel.HeatCapacity * 10f * 0.01f);
        var taken = steel.Exchange(1f, 200f, water);
        Assert.True(taken < 0f);
    }

    // A hot boiler blown dry and filled from the shed's hose: the first
    // kilograms beside the hot steel warm to it and no further (2026-10-26:
    // they swung thousands of degrees each step and burst the boiler).
    [Fact]
    public void A_little_water_beside_hot_steel_warms_to_it_and_no_further()
    {
        var steel = new BoilerMetal(12000f, 192f);
        var mass = 0f;
        var temperature = 0f;
        var lowest = float.MaxValue;
        var highest = float.MinValue;
        for (var t = 0f; t < 2f; t += Dt)
        {
            const float fed = 15f * 0.1f * Dt;
            temperature = (mass * temperature + fed * 82f) / (mass + fed);
            mass += fed;
            var capacity = mass * BoilerMetal.WaterHeat;
            temperature += steel.Exchange(Dt, temperature, capacity) / capacity;
            lowest = Math.Min(lowest, temperature);
            highest = Math.Max(highest, temperature);
        }
        Assert.InRange(lowest, 82f, 192.01f);
        Assert.InRange(highest, 82f, 192.01f);
        Assert.InRange(steel.Temperature, 191.9f, 192f);
    }

    [Fact]
    public void EmptySteelCoolsThroughTheBoilersExteriorWithoutOvershooting()
    {
        var oneStep = new BoilerMetal(12000f, 192f);
        var smallSteps = new BoilerMetal(12000f, 192f);
        oneStep.Cool(8f * 3600f, 25f, 1300f);
        for (var i = 0; i < 8 * 3600; i++)
            smallSteps.Cool(1f, 25f, 1300f);
        Assert.InRange(oneStep.Temperature, 25f, 45f);
        Assert.Equal(oneStep.Temperature, smallSteps.Temperature, 0.1f);
        oneStep.Cool(1e9f, 25f, 1300f);
        Assert.Equal(25f, oneStep.Temperature);
        var before = smallSteps.Temperature;
        smallSteps.Cool(1000f, 25f, 0f);
        Assert.Equal(before, smallSteps.Temperature);
    }

    [Fact]
    public void The_glass_stands_higher_the_harder_the_boiler_steams()
    {
        var idle = new BoilerSwell();
        var working = new BoilerSwell();
        for (var t = 0f; t < 20f; t += Dt)
        {
            idle.Step(Dt, Steaming(1e6f));
            working.Step(Dt, Steaming(12e6f));
        }
        Assert.True(working.Rise > idle.Rise + 0.03f, $"working {working.Rise:F3}, idling {idle.Rise:F3}");
        Assert.InRange(working.Rise, 0f, BoilerSwell.FullSwell + 1e-4f);
    }

    [Fact]
    public void Falling_pressure_swells_it_and_rising_pressure_shrinks_it()
    {
        float After(float barPerSecond)
        {
            var swell = new BoilerSwell();
            var pressure = 14f;
            for (var t = 0f; t < 10f; t += Dt)
            {
                swell.Step(Dt, Steaming(6e6f, pressure));
                if (t > 5f)
                    pressure += barPerSecond * Dt;
            }
            return swell.Rise;
        }
        var steady = After(0f);
        Assert.True(After(-0.1f) > steady + 0.01f);
        Assert.True(After(0.1f) < steady - 0.01f);
    }

    [Fact]
    public void Cold_feed_shrinks_it_and_cold_water_holds_no_steam()
    {
        var dry = new BoilerSwell();
        var fed = new BoilerSwell();
        for (var t = 0f; t < 20f; t += Dt)
        {
            dry.Step(Dt, Steaming(6e6f));
            fed.Step(Dt, Steaming(6e6f, fed: 15f * Dt, subcooling: 4190f * 130f));
        }
        Assert.True(fed.Rise < dry.Rise - 0.01f);

        var cold = new BoilerSwell();
        for (var t = 0f; t < 20f; t += Dt)
            cold.Step(Dt, Steaming(12e6f) with { Boiling = false });
        Assert.Equal(0f, cold.Rise);
    }
}
