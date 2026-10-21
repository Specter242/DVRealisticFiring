using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

public class FireboxParametersTests
{
    [Fact]
    public void StockDefaultsDeriveStockMaximums()
    {
        var parameters = new FireboxParameters();

        // Firebox constructor: maxCombustionRate = capacity / burnTime,
        // maxHeatOutput = maxCombustionRate * 32e6 * efficiencyAtMaxCombustion.
        Assert.Equal(80.0 / 120.0, parameters.MaxCombustionRate, 6);
        Assert.Equal(80.0 / 120.0 * 32_000_000.0 * 0.5, parameters.MaxHeatOutput, 0);
    }

    [Fact]
    public void ABurnTimeSizesTheFireToItsBoiler()
    {
        // The Hudson's box, 180 kg at 0.6, to the J-3a's 10.7 kg/s at 2.6 MJ a kg.
        var burnTime = FireboxParameters.BurnTimeFor(180f, 0.6f, 27.85e6f);
        var sized = new FireboxParameters(180f, burnTime, 0.6f);

        Assert.Equal(124.1f, burnTime, 1);
        Assert.InRange(sized.MaxHeatOutput, 27.84e6f, 27.86e6f);
        Assert.Equal(180f, sized.MaxCoalCapacity);
        // An oil burner forced to 1.15 times its rate gives the heat at that.
        var oil = new FireboxParameters(200f, FireboxParameters.BurnTimeFor(200f, 0.5f, 26.86e6f, 1.15f), 0.5f);
        Assert.InRange(1.15f * oil.MaxHeatOutput, 26.85e6f, 26.87e6f);
        Assert.Throws<ArgumentOutOfRangeException>(() => FireboxParameters.BurnTimeFor(180f, 0.6f, 0f));
    }

    [Fact]
    public void ABoilerTakesUpLessOfItsCoalTheHarderItIsFired()
    {
        // The S282's box, 0.5 at its most: four fifths fired lightly, falling
        // in a straight line with the rate, as the tested boilers' did.
        var p = new FireboxParameters(150f, 120f, 0.5f);
        Assert.Equal(0.8f, p.EfficiencyAt(0f), 3);
        Assert.Equal(0.65f, p.EfficiencyAt(0.5f * p.MaxCombustionRate), 3);
        Assert.Equal(0.5f, p.EfficiencyAt(p.MaxCombustionRate), 3);
        // An oil burner forced past the rate gives what it does at it.
        Assert.Equal(0.5f, p.EfficiencyAt(1.15f * p.MaxCombustionRate), 3);
        // A maker's efficiency over the light-firing one holds at every rate.
        var rich = new FireboxParameters(80f, 120f, 0.9f);
        Assert.Equal(0.9f, rich.EfficiencyAt(0f), 3);
    }

    [Theory]
    [InlineData(0f, 120f, 0.5f)]
    [InlineData(80f, 0f, 0.5f)]
    [InlineData(80f, 120f, 0f)]
    [InlineData(80f, 120f, 1.1f)]
    [InlineData(float.NaN, 120f, 0.5f)]
    public void RejectsNonPhysicalValues(float capacity, float burnTime, float efficiency)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FireboxParameters(capacity, burnTime, efficiency));
    }
}
