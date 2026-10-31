using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

public class SteamPathTests
{
    [Theory]
    [InlineData(RegulatorLocation.BeforeSuperheater)]
    [InlineData(RegulatorLocation.AfterSuperheater)]
    public void A_shut_circuit_bleeds_down_instead_of_retaining_starting_work_forever(RegulatorLocation location)
    {
        var path = new SteamPath(new SteamPathSpec(location, 300, 1000, 6));
        path.Reset(path.Spec.TotalVolume / SteamPath.VolumePerKg(14), 330);
        var header = path.State.PipeMass;
        var loss = path.Bleed(1800, 190, SteamPath.LeakSeconds(path.Spec.RegulatedVolume, .61, 300));
        Assert.Equal(1, path.ChestPressure, 8);
        Assert.True(loss.LeakedMass > 0);
        if (location == RegulatorLocation.AfterSuperheater) Assert.Equal(header, path.State.PipeMass);
        else Assert.Equal(1, path.PipePressure, 8);
        // At one bar the cycle has no positive work, at any crank position.
        var cylinders = new SteamCylinders(new CylinderSpec(), .61f, .711f, 6);
        cylinders.Set(.9f, 0, 14, 330);
        Assert.True(cylinders.Cycle((float)path.ChestPressure).MeanEffectivePressure <= 0);
        var cycle = cylinders.Cycle((float)path.ChestPressure);
        for (var i = 0; i <= 100; i++)
            Assert.True(cycle.Working(i / 100f) - cycle.Returning(1 - i / 100f) <= 0);
    }

    [Fact]
    public void Cooling_condenses_vapor_and_accounts_for_the_removed_mass_and_heat()
    {
        var path = Dome(); path.Reset(800 / SteamPath.VolumePerKg(14), 190);
        var mass = path.Mass; var heat = path.Heat;
        var loss = path.Bleed(600, 25, 1e9);
        Assert.True(loss.CondensedMass > 0);
        Assert.Equal(1, path.ChestPressure, 8);
        Assert.Equal(mass, path.Mass + loss.LeakedMass + loss.CondensedMass, 9);
        Assert.Equal(heat, path.Heat + loss.LeakedHeat + loss.CondensedHeat + loss.CoolingHeat, 3);
    }

    [Theory]
    [InlineData(144)] [InlineData(30)] [InlineData(4)]
    public void Hot_closed_circuit_leakage_is_independent_of_frame_rate(int hz)
    {
        var path = Dome(); path.Reset(800 / SteamPath.VolumePerKg(14), 300);
        for (var i = 0; i < 60 * hz; i++) path.Bleed(1.0 / hz, 300, 60);
        var expected = SteamPath.RemainingMass(14, 800, 60, 60);
        Assert.Equal(expected, path.Mass, 8);
    }

    [Fact]
    public void Short_closure_keeps_residual_pull_but_an_eight_hour_cooldown_does_not()
    {
        var path = Dome(); path.Reset(800 / SteamPath.VolumePerKg(14), 300);
        var tau = SteamPath.LeakSeconds(800, .61, 300);
        path.Bleed(2, 190, tau);
        Assert.InRange(path.ChestPressure, 10, 14);
        path.Bleed(8 * 3600, 25, tau);
        Assert.Equal(1, path.ChestPressure, 8);
        Assert.True(path.ChestTemperature < 26);
    }

    private static SteamPath Dome() => new(new SteamPathSpec(RegulatorLocation.BeforeSuperheater, 600, 200, 5));
    private static SteamPath Front() => new(new SteamPathSpec(RegulatorLocation.AfterSuperheater, 0, 1500, 10));

    [Fact]
    public void A_volume_budget_requires_a_valid_regulator_and_finite_positive_spaces()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SteamPathSpec((RegulatorLocation)99, 600, 200, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SteamPathSpec(RegulatorLocation.BeforeSuperheater, -1, 200, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SteamPathSpec(RegulatorLocation.BeforeSuperheater, 600, 0, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SteamPathSpec(RegulatorLocation.BeforeSuperheater, 600, 200, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SteamPathSpec(RegulatorLocation.BeforeSuperheater, double.MaxValue, double.MaxValue, 5));
    }

    [Fact]
    public void Adopting_the_stock_chest_preserves_total_mass_and_pressure()
    {
        var path = Dome();
        var mass = 800 / SteamPath.VolumePerKg(14);
        path.Reset(mass, 300);
        Assert.Equal(mass, path.Mass, 10);
        Assert.Equal(14, path.ChestPressure, 8);
        Assert.Equal(14, path.PipePressure, 8);
        Assert.Equal(mass * SteamPath.SpecificHeat(300), path.Heat, 5);
    }

    [Fact]
    public void A_closed_regulator_keeps_inventory_and_does_not_fill_a_dead_empty_path()
    {
        foreach (var path in new[] { Dome(), Front() })
        {
            var flow = path.Step(14, 200, 350, 0, 0, 20);
            Assert.Equal(0, flow.InletMass);
            Assert.Equal(0, path.Mass);
            path.Reset(4, 300);
            var heat = path.Heat;
            flow = path.Step(14, 200, 350, 0, 0, 20);
            Assert.Equal(4, path.Mass, 10);
            Assert.Equal(heat + flow.HeaterHeat, path.Heat, 5);
        }
    }

    [Fact]
    public void The_inlet_respects_capacity_and_opening_even_on_a_coarse_step()
    {
        foreach (var path in new[] { Dome(), Front() })
        {
            var flow = path.Step(14, 200, 350, 0.1, 0, 0.25);
            Assert.InRange(flow.InletMass, 0.001, path.Spec.MaxFlow * 0.1 * 0.25 + 1e-10);
            Assert.Equal(flow.InletMass, path.Mass, 10);
        }
    }

    [Fact]
    public void Closing_retains_pressure_and_delivers_stored_steam_without_boiler_draw()
    {
        foreach (var path in new[] { Dome(), Front() })
        {
            path.Reset(path.Spec.TotalVolume / SteamPath.VolumePerKg(14), 300);
            var mass = path.Mass;
            var flow = path.Step(14, 200, 350, 0, 1, 0.5);
            Assert.Equal(0, flow.InletMass);
            Assert.Equal(0.5, flow.DeliveredMass, 8);
            Assert.Equal(mass - 0.5, path.Mass, 8);
            Assert.InRange(path.ChestPressure, 1.01, 13.99);
        }
    }

    [Fact]
    public void Reopening_uses_the_remaining_inventory_instead_of_starting_empty()
    {
        var charged = Dome();
        charged.Reset(4, 300);
        charged.Step(14, 200, 350, 0, 1, 0.5);
        var remaining = charged.Mass;
        var flow = charged.Step(14, 200, 350, 0.2, 1, 0.1);
        var empty = Dome();
        empty.Step(14, 200, 350, 0.2, 1, 0.1);
        Assert.Equal(remaining + flow.InletMass - flow.DeliveredMass, charged.Mass, 9);
        Assert.True(charged.ChestPressure > empty.ChestPressure);
    }

    [Fact]
    public void Falling_boiler_pressure_returns_steam_only_through_an_open_regulator()
    {
        foreach (var path in new[] { Dome(), Front() })
        {
            path.Reset(path.Spec.TotalVolume / SteamPath.VolumePerKg(14), 300);
            var closed = path.Step(5, 150, 300, 0, 0, 1);
            Assert.Equal(0, closed.InletMass);
            var flow = path.Step(5, 150, 300, 1, 0, 1);
            Assert.True(flow.InletMass < 0);
            Assert.True(flow.InletHeat < 0);
        }
    }

    [Fact]
    public void Steam_and_advected_heat_balance_during_randomized_work_and_reversals()
    {
        var random = new Random(815);
        foreach (var path in new[] { Dome(), Front() })
        {
            path.Reset(3, 300);
            for (var i = 0; i < 2000; i++)
            {
                var mass = path.Mass; var heat = path.Heat;
                var flow = path.Step(1 + 20 * random.NextDouble(), 200, 250 + 200 * random.NextDouble(),
                    random.NextDouble(), 15 * random.NextDouble(), 0.002 + random.NextDouble() * 0.3);
                Assert.InRange(Math.Abs(path.Mass - mass - flow.InletMass + flow.DeliveredMass), 0, 1e-9);
                Assert.InRange(Math.Abs(path.Heat - heat - flow.InletHeat + flow.DeliveredHeat - flow.HeaterHeat), 0, 1e-5);
                Assert.True(path.State.PipeMass >= 0 && path.State.ChestMass >= 0);
                Assert.True(double.IsFinite(path.ChestPressure));
            }
        }
    }

    [Fact]
    public void Stored_state_resumes_the_same_open_close_transient()
    {
        var first = Dome(); first.Reset(4, 300);
        first.Step(14, 200, 350, 0.3, 2, 0.8);
        var second = Dome(); Assert.True(second.Restore(first.State));
        for (var i = 0; i < 100; i++)
        {
            var opening = i < 40 ? 0 : 0.6;
            var a = first.Step(14, 200, 350, opening, 3, 0.02);
            var b = second.Step(14, 200, 350, opening, 3, 0.02);
            Assert.Equal(a.InletMass, b.InletMass);
            Assert.Equal(first.State.ChestHeat, second.State.ChestHeat);
            Assert.Equal(first.ChestPressure, second.ChestPressure);
        }
        var saved = second.State;
        Assert.False(second.Restore(new SteamPathState { PipeMass = double.NaN }));
        Assert.False(second.Restore(new SteamPathState { ChestMass = 0, ChestHeat = 1 }));
        Assert.Equal(saved.ChestMass, second.State.ChestMass);
    }

    [Theory]
    [InlineData(144)] [InlineData(60)] [InlineData(10)] [InlineData(4)]
    public void Ordinary_flow_preserves_steady_demand_and_capacity(int hz)
    {
        foreach (var path in new[] { Dome(), Front() })
        {
            var intake = 0.0; var delivered = 0.0;
            for (var i = 0; i < 30 * hz; i++)
            {
                var flow = path.Step(14, 200, 350, 0.6, path.Spec.MaxFlow * 0.5, 1.0 / hz);
                if (i >= 20 * hz) { intake += flow.InletMass; delivered += flow.DeliveredMass; }
            }
            Assert.InRange(intake / 10 / path.Spec.MaxFlow, 0.495, 0.505);
            Assert.InRange(delivered / 10 / path.Spec.MaxFlow, 0.495, 0.505);
            Assert.InRange(path.ChestPressure, 13.5, 14.01);
        }
    }

    [Fact]
    public void A_stroke_is_paid_once_from_the_connected_inventory_with_its_heat()
    {
        foreach (var path in new[] { Dome(), Front() })
        {
            path.Reset(5, 300);
            var heat = path.Heat;
            var flow = path.Take(0.7);
            Assert.Equal(4.3, path.Mass, 10);
            Assert.Equal(0.7, flow.DeliveredMass, 10);
            Assert.Equal(heat - flow.DeliveredHeat, path.Heat, 5);
            flow = path.Take(50);
            Assert.Equal(4.3, flow.DeliveredMass, 10);
            Assert.Equal(0, path.Mass);
            Assert.Equal(0, path.Heat);
        }
    }

    [Fact]
    public void Steam_leaves_the_elements_at_the_existing_heaters_temperature()
    {
        var path = Dome();
        for (var i = 0; i < 1000; i++) path.Step(14, 190, 350, 0.8, 3, 0.02);
        Assert.InRange(path.ChestTemperature, 349.99, 350.01);
        Assert.False(path.Restore(new SteamPathState { ChestMass = 1e5 }));
    }

    [Fact]
    public void A_short_open_close_transient_is_consistent_across_frame_rates()
    {
        (double mass, double pressure) Run(int hz)
        {
            var path = Dome();
            for (var i = 0; i < 4 * hz; i++) path.Step(14, 200, 350, 0.3, 1, 1.0 / hz);
            for (var i = 0; i < hz; i++) path.Step(14, 200, 350, 0, 1, 1.0 / hz);
            return (path.Mass, path.ChestPressure);
        }
        var expected = Run(144);
        foreach (var hz in new[] { 60, 10, 4 })
        {
            var actual = Run(hz);
            Assert.InRange(Math.Abs(expected.mass - actual.mass), 0, 0.01);
            Assert.InRange(Math.Abs(expected.pressure - actual.pressure), 0, 0.02);
        }
    }

    [Fact]
    public void Invalid_steps_leave_the_inventory_untouched_and_running_steps_allocate_nothing()
    {
        var path = Dome(); path.Reset(4, 300);
        path.Step(14, 200, 350, 0.5, 2, -1);
        path.Step(double.NaN, 200, 350, 0.5, 2, 0.1);
        Assert.Equal(4, path.Mass, 10);
        for (var i = 0; i < 100; i++) path.Step(14, 200, 350, 0.5, 2, 0.02);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) path.Step(14, 200, 350, 0.5, 2, 0.02);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }
}
