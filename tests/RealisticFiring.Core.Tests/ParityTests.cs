using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

/// <summary>
/// The stock locos must keep their performance: a well-managed fire has to
/// burn coal against the draft, and turn coal into heat, like the stock
/// Firebox does at full oxygen availability.
/// </summary>
public class ParityTests
{
    /// <summary>Stated parity tolerance, as a fraction of the stock value.</summary>
    private const double Tolerance = 0.05;

    [Theory]
    [InlineData(0.25f)]
    [InlineData(0.5f)]
    [InlineData(0.75f)]
    [InlineData(0.95f)]
    public void WellManagedSteadyFireMatchesStockHeatPerCoalAndCoalPerAir(float load)
    {
        var p = new FireboxParameters();
        var stockRate = load * p.MaxCombustionRate;
        var airFlow = stockRate * FireTestKit.StockAirPerCoal;
        // The stock fire burns rate = coalMass / burnTime, so this is the coal
        // a stock player keeps in the box to use exactly the available air.
        var targetCoal = stockRate * p.BurnTime;

        var fire = FireTestKit.BurningFire(p, targetCoal);
        var fireman = new Fireman();
        var inputs = FireTestKit.Inputs(airFlow);

        // Settle for 20 minutes, then measure for 20.
        Simulate(fire, fireman, inputs, targetCoal, 1200f, out _, out _, out _);
        Simulate(fire, fireman, inputs, targetCoal, 1200f, out var burned, out var heat, out var smoke);

        var burnRate = burned / 1200.0;
        var heatPerCoal = heat / burned;
        var stockHeatPerCoal = FireTestKit.StockHeat(p, burnRate) / burnRate;

        Assert.InRange(burnRate / stockRate, 1 - Tolerance, 1 + Tolerance);
        Assert.InRange(heatPerCoal / stockHeatPerCoal, 1 - Tolerance, 1 + Tolerance);
        Assert.True(smoke < 0.15, $"mean smoke {smoke:F3} should stay light");
    }

    private static void Simulate(FireModel fire, Fireman fireman, FireInputs inputs, float targetCoal, float seconds,
        out double burned, out double heatEnergy, out double meanSmoke)
    {
        burned = 0;
        heatEnergy = 0;
        double smoke = 0;
        var steps = 0;
        for (var t = 0f; t < seconds; t += FireTestKit.Dt)
        {
            fireman.Tend(fire, targetCoal, FireTestKit.Dt);
            burned += fire.Step(FireTestKit.Dt, inputs);
            heatEnergy += fire.Heat * FireTestKit.Dt;
            smoke += fire.SmokeDensity;
            steps++;
        }
        meanSmoke = smoke / steps;
    }
}
