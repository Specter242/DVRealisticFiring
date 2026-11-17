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

    [Theory]
    [InlineData(3f, 1, 3.5f)]
    [InlineData(3f, -1, 2.5f)]
    [InlineData(3.2f, 1, 3.5f)]
    [InlineData(3.2f, -1, 3f)]
    [InlineData(2.5f, 2, 3.5f)]
    [InlineData(8f, 1, 8f)]
    [InlineData(1f, -1, 1f)]
    [InlineData(7.8f, 1, 8f)]
    [InlineData(1.2f, -1, 1f)]
    public void The_wheel_steps_the_shovelful_half_a_kilogram_a_click_within_the_settings_range(float shovelful, int clicks, float after)
    {
        Assert.Equal(after, ShovelSize.Wheeled(shovelful, clicks, 1f, 8f), 4);
    }

    [Theory]
    [InlineData(0f, 3f, 3f)]
    [InlineData(2.5f, 3f, 2.5f)]
    [InlineData(5f, 3f, 3f)]
    [InlineData(0.5f, 3f, 3f)]
    [InlineData(float.NaN, 3f, 3f)]
    public void The_players_shovelful_is_the_wheels_pick_up_to_the_largest_or_a_full_one(float picked, float largest, float scoop)
    {
        Assert.Equal(scoop, ShovelSize.Scoop(picked, largest, 1f), 4);
    }

    [Theory]
    [InlineData(1f, 3f)]
    [InlineData(2.5f, 3f)]
    [InlineData(3f, 3f)]
    [InlineData(6f, 6f)]
    public void The_fireman_fires_the_largest_but_never_less_than_a_normal_shovelful(float largest, float his)
    {
        Assert.Equal(his, ShovelSize.Fireman(largest), 4);
    }
}
