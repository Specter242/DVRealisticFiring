using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

/// <summary>
/// The standard shovel carries 3 tender chunks of 48 kg; the S282's consumption
/// multiplier of 4 makes that 36 kg in the firebox (measured in game). The mod
/// sizes it as a real shovelful, in the coal pile's units.
/// </summary>
public class ShovelSizeTests
{
    [Fact]
    public void StandardShovelCarriesTheShovelfulMass()
    {
        Assert.Equal(3, ShovelSize.StandardShovelChunks);
        Assert.Equal(12f, ShovelSize.PileCapacity(3f, shovelChunks: 3, consumptionMultiplier: 4f), 4);
    }

    [Fact]
    public void OtherShovelsKeepTheirSizeRatio()
    {
        Assert.Equal(4f, ShovelSize.PileCapacity(3f, shovelChunks: 4, consumptionMultiplier: 1f), 4);
        Assert.Equal(2f, ShovelSize.PileCapacity(3f, shovelChunks: 2, consumptionMultiplier: 1f), 4);
    }

    [Fact]
    public void CapacityIsInPileUnits()
    {
        // FireboxSimController.TransferCoal divides by the consumption multiplier.
        Assert.Equal(6f, ShovelSize.PileCapacity(3f, ShovelSize.StandardShovelChunks, consumptionMultiplier: 2f), 4);
    }

    [Theory]
    [InlineData(0, 1f)]
    [InlineData(3, 0f)]
    [InlineData(3, -1f)]
    public void InvalidShovelOrMultiplierFallsBackToStandard(int chunks, float multiplier)
    {
        Assert.Equal(3f, ShovelSize.PileCapacity(3f, chunks, multiplier), 4);
    }
}
