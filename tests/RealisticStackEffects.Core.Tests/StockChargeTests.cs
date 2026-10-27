using RealisticStackEffects.Core;

namespace RealisticStackEffects.Core.Tests;

/// <summary>
/// A shovelful on the game's own firebox smokes out: thick gas from coal
/// landing on a hot fire, reaching the stack a moment later and clearing
/// in seconds, sooner on a forced fire.
/// </summary>
public class StockChargeTests
{
    private const float Dt = 0.02f;

    private static void Run(StockCharge charge, float seconds, float forcing = 0.5f, bool fireOn = true, float temperature = 1100f)
    {
        for (var t = 0f; t < seconds; t += Dt)
            charge.Step(Dt, fireOn, temperature, forcing);
    }

    // A hot, working fire with a throw landing on it.
    private static StockCharge Thrown(float kg, float forcing = 0.5f, float temperature = 1100f)
    {
        var charge = new StockCharge();
        Run(charge, 5f, forcing, temperature: temperature);
        charge.Throw(kg);
        charge.Step(Dt, true, temperature, forcing);
        return charge;
    }

    [Fact]
    public void AShovelfulSmokesOutAndTheStackSeesItAMomentLater()
    {
        var charge = Thrown(3f);
        Assert.True(charge.SmokeOutRate > 20f, $"a 3 kg shovelful smokes {charge.SmokeOutRate:F1} puffs a second");
        Assert.True(charge.StackRate < 0.1f * charge.SmokeOutRate, "the stack has none of it yet");
        Run(charge, 1.5f);
        Assert.True(charge.StackRate > 8f, $"through the tubes after 1.5 s: {charge.StackRate:F1}");
        Run(charge, 20f);
        Assert.True(charge.SmokeOutRate < 0.5f && charge.StackRate < 1f, "gone in seconds");
    }

    [Fact]
    public void AForcedFireClearsItsChargeSooner()
    {
        var dull = Thrown(3f, forcing: 0f);
        var forced = Thrown(3f, forcing: 1f);
        Run(dull, 4f, forcing: 0f);
        Run(forced, 4f, forcing: 1f);
        Assert.True(forced.Charge < 0.6f * dull.Charge, $"forced {forced.Charge:F2} kg left, dull {dull.Charge:F2}");
    }

    [Fact]
    public void NothingSmokesOutWithoutAThrow()
    {
        // A stoker's feed and the fire's burning move the coal, not the charge.
        var charge = new StockCharge();
        Run(charge, 30f);
        Assert.Equal(0f, charge.Charge);
        Assert.Equal(0f, charge.StackRate);
    }

    [Fact]
    public void CoalThrownOnADeadFireSmokesNothingWhenLit()
    {
        var charge = new StockCharge();
        charge.Throw(36f);
        Run(charge, 1f, fireOn: false);
        Run(charge, 2f);
        Assert.Equal(0f, charge.Charge);
    }

    [Fact]
    public void AThrowInALongFrameLandsWhole()
    {
        var charge = new StockCharge();
        Run(charge, 5f);
        charge.Throw(3f);
        charge.Step(0.25f, true, 1100f, 0.5f);
        Assert.True(charge.Charge > 2.8f, $"{charge.Charge:F2} kg charged by a throw in a 250 ms frame");
    }

    [Fact]
    public void AStockThrowSmokesNoThickerThanAFewShovelfulsButLonger()
    {
        var real = Thrown(3f);
        var stock = Thrown(36f);
        Assert.Equal(StockCharge.SmokeOutPerKg * StockCharge.MostSmoking, stock.SmokeOutRate, 1);
        Run(real, 8f);
        Run(stock, 8f);
        Assert.True(stock.SmokeOutRate > 3f * real.SmokeOutRate, $"after 8 s the 36 kg throw still smokes {stock.SmokeOutRate:F1}, the shovelful {real.SmokeOutRate:F1}");
    }

    [Fact]
    public void ACoolFireSmokesLess()
    {
        var hot = Thrown(3f);
        var cool = Thrown(3f, temperature: 300f);
        Assert.True(cool.SmokeOutRate < 0.7f * hot.SmokeOutRate);
    }

    [Fact]
    public void ANonsenseThrowIsIgnored()
    {
        var charge = new StockCharge();
        charge.Throw(-3f);
        charge.Throw(float.NaN);
        charge.Throw(float.PositiveInfinity);
        Run(charge, 1f);
        Assert.Equal(0f, charge.Charge);
    }
}
