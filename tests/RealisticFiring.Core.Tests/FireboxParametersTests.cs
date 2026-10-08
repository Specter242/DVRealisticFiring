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
