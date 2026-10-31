using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

public class ThrottleCharacteristicTests
{
    private static SteamPath Path(RegulatorLocation location) => new(new SteamPathSpec(location, 750, 250, 6));

    [Theory]
    [InlineData(1500, 15)] [InlineData(1500, 10)]
    [InlineData(1400, 20)] [InlineData(1200, 10)]
    public void A_charged_front_header_preserves_the_chest_only_pressure_equality_gate(double volume, double capacity)
    {
        var header = new SteamPath(new SteamPathSpec(RegulatorLocation.AfterSuperheater, volume * .75, volume, capacity));
        var chest = new SteamPath(new SteamPathSpec(RegulatorLocation.AfterSuperheater, 0, volume, capacity));
        header.Reset(header.Spec.TotalVolume / SteamPath.VolumePerKg(14), 350);
        chest.Reset(volume / SteamPath.VolumePerKg(14), 350);
        for (var i = 0; i < 20; i++)
        {
            header.Take(.1); chest.Take(.1);
            header.Step(14, 190, 350, 1, 0, .02);
            chest.Step(14, 190, 350, 1, 0, .02);
            Assert.Equal(chest.ChestPressure, header.ChestPressure, 8);
            Assert.InRange(Math.Abs(header.ChestPressure - 14), 0, .001);
        }
    }

    [Fact]
    public void An_undercharged_front_header_does_not_supply_a_higher_pressure_chest()
    {
        var path = new SteamPath(new SteamPathSpec(RegulatorLocation.AfterSuperheater, 750, 1000, 6));
        var pipe = 750 / SteamPath.VolumePerKg(2);
        var chest = 1000 / SteamPath.VolumePerKg(10);
        Assert.True(path.Restore(new SteamPathState { PipeMass = pipe, ChestMass = chest,
            PipeHeat = pipe * SteamPath.SpecificHeat(300), ChestHeat = chest * SteamPath.SpecificHeat(300) }));
        var flow = path.Step(14, 190, 350, 1, 0, .001);
        Assert.True(path.PipePressure < path.ChestPressure);
        Assert.True(flow.RegulatorMass <= 0);
        Assert.True(path.State.ChestMass <= chest);
        Assert.Equal(pipe + chest + flow.InletMass, path.Mass, 9);
    }

    [Theory]
    [InlineData(20)] [InlineData(60)] [InlineData(144)]
    public void Front_end_refill_pays_full_tick_losses_before_applying_the_native_linear_valve_cap(int hz)
    {
        var path = new SteamPath(new SteamPathSpec(RegulatorLocation.AfterSuperheater, 750, 1000, 6));
        path.Reset(path.Spec.TotalVolume / SteamPath.VolumePerKg(14), 350);
        var mass = path.Mass;
        var header = path.State.PipeMass;
        var flow = path.Step(14, 190, 350, .2, 1.5, 1.0 / hz);
        Assert.Equal(1.5 / hz, flow.DeliveredMass, 9);
        Assert.Equal(1.2 / hz, flow.RegulatorMass, 9);
        Assert.Equal(1.2 / hz, flow.InletMass, 9);
        Assert.Equal(header, path.State.PipeMass, 9);
        Assert.Equal(mass + flow.InletMass - flow.DeliveredMass, path.Mass, 9);
    }

    [Fact]
    public void Charging_a_front_header_and_feeding_its_chest_share_one_finite_boiler_supply_budget()
    {
        var path = new SteamPath(new SteamPathSpec(RegulatorLocation.AfterSuperheater, 750, 1000, 6));
        var mass = 750 / SteamPath.VolumePerKg(14) - .59;
        Assert.True(path.Restore(new SteamPathState { PipeMass = mass, PipeHeat = mass * SteamPath.SpecificHeat(350) }));
        var flow = path.Step(14, 190, 350, 1, 0, .01);
        Assert.InRange(flow.InletMass, .58, .6000000001);
        Assert.True(flow.RegulatorMass > 0);
        Assert.Equal(mass + flow.InletMass, path.Mass, 9);
    }

    [Fact]
    public void A_front_end_throttle_keeps_the_header_charged_while_the_chest_is_empty()
    {
        var front = Path(RegulatorLocation.AfterSuperheater);
        var dome = Path(RegulatorLocation.BeforeSuperheater);
        var flow = front.Step(14, 190, 350, 0, 0, 5);
        dome.Step(14, 190, 350, 0, 0, 5);
        Assert.InRange(front.PipePressure, 13.99, 14.01);
        Assert.Equal(0, front.State.ChestMass);
        Assert.Equal(0, dome.Mass);
        Assert.Equal(front.Mass, flow.InletMass, 9);
        Assert.Equal(0, flow.RegulatorMass);
    }

    [Fact]
    public void Closing_a_front_end_throttle_isolates_its_header_from_cylinder_demand()
    {
        var front = Path(RegulatorLocation.AfterSuperheater);
        front.Reset(1000 / SteamPath.VolumePerKg(14), 350);
        var header = front.State.PipeMass;
        var chest = front.State.ChestMass;
        var drawn = front.Step(14, 190, 350, 0, 10, 5);
        Assert.InRange(drawn.DeliveredMass, chest - 1e-9, chest + 1e-9);
        Assert.Equal(header, front.State.PipeMass, 9);
        Assert.Equal(0, front.State.ChestMass);
        Assert.Equal(0, drawn.InletMass, 9);
        Assert.Equal(0, drawn.RegulatorMass);
        var stroke = front.Take(10);
        Assert.Equal(0, stroke.DeliveredMass);
        Assert.Equal(header, front.Mass, 9);
    }

    [Fact]
    public void Regulator_position_changes_opening_and_run_down_with_the_same_volume_and_flow_budget()
    {
        (double rise, double decay) Measure(RegulatorLocation location)
        {
            var path = Path(location);
            path.Step(14, 190, 350, 0, 0, 5);
            var rise = double.NaN;
            for (var i = 0; i < 6000; i++)
            {
                path.Step(14, 190, 350, .25, .5, .01);
                if (double.IsNaN(rise) && path.ChestPressure >= 7.5) rise = (i + 1) * .01;
            }
            var decay = double.NaN;
            var threshold = 1 + .1 * (path.ChestPressure - 1);
            for (var i = 0; i < 6000; i++)
            {
                path.Step(14, 190, 350, 0, .5, .01);
                if (path.ChestPressure <= threshold) { decay = (i + 1) * .01; break; }
            }
            return (rise, decay);
        }
        var upstream = Measure(RegulatorLocation.BeforeSuperheater);
        var front = Measure(RegulatorLocation.AfterSuperheater);
        Assert.True(double.IsFinite(upstream.rise) && double.IsFinite(front.rise));
        Assert.True(upstream.rise > front.rise * 2, $"opening: upstream {upstream.rise}, front {front.rise}");
        Assert.True(upstream.decay > front.decay * 2, $"run-down: upstream {upstream.decay}, front {front.decay}");
    }

    [Theory]
    [InlineData(RegulatorLocation.BeforeSuperheater)]
    [InlineData(RegulatorLocation.AfterSuperheater)]
    public void The_separate_circuit_conserves_mass_and_carried_heat_through_pressure_changes(RegulatorLocation location)
    {
        var path = new SteamPath(new SteamPathSpec(location, 750, 1000, 6));
        var random = new Random(231);
        for (var i = 0; i < 2000; i++)
        {
            var mass = path.Mass; var heat = path.Heat;
            var opening = i % 5 == 0 ? 0 : random.NextDouble();
            var flow = path.Step(1 + 24 * random.NextDouble(), 190, 250 + 150 * random.NextDouble(),
                opening, 10 * random.NextDouble(), .001 + .4 * random.NextDouble());
            Assert.InRange(Math.Abs(path.Mass - mass - flow.InletMass + flow.DeliveredMass), 0, 1e-9);
            Assert.InRange(Math.Abs(path.Heat - heat - flow.InletHeat - flow.HeaterHeat + flow.DeliveredHeat), 0, 1e-5);
            if (opening == 0) Assert.Equal(0, flow.RegulatorMass);
        }
    }

    [Theory]
    [InlineData(4)] [InlineData(10)] [InlineData(60)] [InlineData(144)]
    public void Both_regulator_positions_preserve_mass_and_steady_delivery(int hz)
    {
        foreach (var location in new[] { RegulatorLocation.BeforeSuperheater, RegulatorLocation.AfterSuperheater })
        {
            var path = Path(location);
            var inlet = 0.0; var delivery = 0.0;
            for (var i = 0; i < hz * 30; i++)
            {
                var before = path.Mass;
                var flow = path.Step(14, 190, 350, .6, 3, 1.0 / hz);
                Assert.InRange(Math.Abs(path.Mass - before - flow.InletMass + flow.DeliveredMass), 0, 1e-9);
                if (i >= hz * 20) { inlet += flow.InletMass; delivery += flow.DeliveredMass; }
            }
            Assert.InRange(inlet / 10, 2.99, 3.01);
            Assert.InRange(delivery / 10, 2.99, 3.01);
        }
    }
}
