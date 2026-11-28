using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

 public class RearrangedS282Tests
{
    // The stock S282's graph, in part, and Rearranged 3.0.1's (ArticulatedSimulation.Prepare).
    private static readonly string[] Stock = { "throttle", "steamConsumptionCalculator", "boiler", "steamEngine", "exhaust", "traction" };
    private static readonly string[] Rearranged =
    {
        "throttle", "rearrangedEngineIntake", "steamConsumptionCalculator", "boiler", "steamEngine",
        "rearrangedRearSteamEngine", "rearrangedMiddleSteamEngine", "rearrangedEngineTorque", "rearrangedEngineExhaust",
        "exhaust", "traction",
    };

    [Fact]
    public void Booster_goes_right_after_the_stock_engine()
    {
        var at = BoosterWiring.InsertAt(Stock, "steamEngine.TORQUE_OUT", "steamEngine");
        Assert.Equal(Array.IndexOf(Stock, "steamEngine") + 1, at);
    }

    [Fact]
    public void Booster_goes_after_Rearranged_torque_sum_so_it_reads_every_engine_this_tick()
    {
        var at = BoosterWiring.InsertAt(Rearranged, "rearrangedEngineTorque.OUT", "steamEngine");
        Assert.Equal(Array.IndexOf(Rearranged, "rearrangedEngineTorque") + 1, at);
        Assert.True(at > Array.IndexOf(Rearranged, "rearrangedMiddleSteamEngine"));
        Assert.True(at <= Array.IndexOf(Rearranged, "traction"));
    }

    [Fact]
    public void Booster_falls_back_to_the_engine_when_the_source_is_unknown()
    {
        Assert.Equal(4, BoosterWiring.InsertAt(Stock, "elsewhere.TORQUE_OUT", "steamEngine"));
        Assert.Equal(4, BoosterWiring.InsertAt(Stock, null, "steamEngine"));
        Assert.Equal(-1, BoosterWiring.InsertAt(Stock, "elsewhere.OUT", "noEngine"));
        Assert.Null(BoosterWiring.ComponentOf("noDot"));
        Assert.Equal("a", BoosterWiring.ComponentOf("a.B.C"));
    }

    [Fact]
    public void Trailer_axle_adds_one_on_the_stock_count_and_takes_it_off_again()
    {
        var axle = new TrailerAxle();
        Assert.Null(axle.Next(4f, inGear: false));
        Assert.Equal(5f, axle.Next(4f, inGear: true));
        Assert.Null(axle.Next(5f, inGear: true));
        Assert.Equal(4f, axle.Own(5f));
        Assert.Equal(4f, axle.Next(5f, inGear: false));
        Assert.False(axle.Added);
    }

    [Fact]
    public void Trailer_axle_adds_to_another_mods_count_not_an_absolute_number()
    {
        // Rearranged's 4-12-2: six powered axles.
        var axle = new TrailerAxle();
        Assert.Null(axle.Next(6f, inGear: false));
        Assert.Equal(7f, axle.Next(6f, inGear: true));
        Assert.Equal(6f, axle.Next(7f, inGear: false));
    }

    [Fact]
    public void A_count_another_mod_wrote_while_in_gear_stands_as_theirs()
    {
        var axle = new TrailerAxle();
        Assert.Equal(5f, axle.Next(4f, inGear: true));
        // Rearranged applies a 4-12-2 while the booster is in: six of its own.
        Assert.Equal(6f, axle.Own(6f));
        Assert.Equal(7f, axle.Next(6f, inGear: true));
        // And restores the stock S282 (its own 4) while still in gear.
        Assert.Equal(4f, axle.Own(4f));
        Assert.Null(axle.Next(4f, inGear: false));
    }

    [Fact]
    public void The_configured_count_tells_theirs_from_ours_when_they_write_our_number()
    {
        var axle = new TrailerAxle();
        Assert.Equal(5f, axle.Next(4f, inGear: true, configured: 4f));
        Assert.Equal(4f, axle.Own(5f, configured: 4f));
        Assert.True(axle.Added);
        // Rearranged applies a 2-10-2 while the booster is in: five of its own, the very number we wrote.
        Assert.Equal(5f, axle.Own(5f, configured: 5f));
        Assert.False(axle.Added);
        Assert.Null(axle.Next(5f, inGear: false, configured: 5f));
        // Read from the port alone, the same write would look like ours.
        var blind = new TrailerAxle();
        blind.Next(4f, inGear: true);
        Assert.Equal(4f, blind.Own(5f));
    }

    [Fact]
    public void Trailer_axle_never_goes_below_none()
    {
        var axle = new TrailerAxle();
        Assert.Equal(1f, axle.Next(0f, inGear: true));
        Assert.Equal(0f, axle.Next(1f, inGear: false));
    }

    // The S282's boiler: 17,000 L holding 12,000 kg at 180 °C.
    private const float Volume = 17000f, Mass = 12000f, Hot = 180f;

    [Fact]
    public void Rearranged_resize_carries_the_water()
    {
        // ResizeNormalized: the new volume, the old water's mass scaled by it, its pressure and temperature.
        foreach (var ratio in new[] { 0.7f, 1.15f, 1.6f })
            Assert.True(BoilerResize.CarriesWater(Mass, Volume, Hot, Mass * ratio, Volume * ratio, Hot));
        Assert.Equal(1.15f, BoilerResize.Carried(Mass, Mass * 1.15f), 4);
    }

    [Fact]
    public void A_game_reset_of_the_same_boiler_is_still_a_washout()
    {
        // Spawn, a burst boiler's repair, the start-up's water: the same volume, new water.
        Assert.False(BoilerResize.CarriesWater(Mass, Volume, Hot, Mass, Volume, Hot));
        Assert.False(BoilerResize.CarriesWater(0f, Volume, Hot, Mass, Volume, 194f));
    }

    [Fact]
    public void A_resize_with_different_water_is_not_the_same_water()
    {
        Assert.False(BoilerResize.CarriesWater(Mass, Volume, Hot, Mass * 1.3f, Volume * 1.15f, Hot));
        Assert.False(BoilerResize.CarriesWater(Mass, Volume, Hot, Mass * 1.15f, Volume * 1.15f, Hot - 10f));
        Assert.False(BoilerResize.CarriesWater(Mass, Volume, Hot, 0f, Volume * 1.15f, Hot));
        Assert.False(BoilerResize.CarriesWater(Mass, Volume, float.NaN, Mass * 1.15f, Volume * 1.15f, Hot));
        Assert.False(BoilerResize.CarriesWater(Mass, 0f, Hot, Mass, Volume, Hot));
    }

    [Fact]
    public void An_empty_boiler_resized_is_still_empty()
    {
        Assert.True(BoilerResize.CarriesWater(0f, Volume, 25f, 0f, Volume * 1.2f, 25f));
        Assert.Equal(1f, BoilerResize.Carried(0f, 0f));
    }
}
