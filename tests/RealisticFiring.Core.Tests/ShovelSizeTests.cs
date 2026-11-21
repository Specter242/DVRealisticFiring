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
    public void TheAcceleratedStartupBoostsTheShovelfulUpToTwelveKilograms()
    {
        Assert.Equal(6f, ShovelSize.StartupLoad(6f, 1f));
        Assert.Equal(6f, ShovelSize.StartupLoad(6f, 0.5f));
        Assert.Equal(6f, ShovelSize.StartupLoad(6f, float.NaN));
        Assert.Equal(9f, ShovelSize.StartupLoad(6f, 1.5f));
        Assert.Equal(12f, ShovelSize.StartupLoad(6f, 25f));
        Assert.Equal(12f, ShovelSize.StartupLoad(3f, 25f));
        Assert.Equal(7.5f, ShovelSize.StartupLoad(3f, 2.5f));
        // Never less than the player's own, even over the cap.
        Assert.Equal(14f, ShovelSize.StartupLoad(14f, 25f));
        Assert.Equal(" (startup ×2)", ShovelSize.BoostNote(2f));
        Assert.Equal(" (startup ×2.5)", ShovelSize.BoostNote(2.5f));
        Assert.Equal("", ShovelSize.BoostNote(1f));
        // The pile gives the boosted shovelful times the coal multiplier, as any: the same cost per kilogram.
        Assert.Equal(48f, ShovelSize.PileCapacity(ShovelSize.StartupLoad(6f, 25f), 3, consumptionMultiplier: 4f), 4);
    }

    [Fact]
    public void ASmallFireboxTakesAStartupShovelfulNoThickerThanItsColdBedShouldBe()
    {
        // The S060's 60 kg box: twice the fireman's 8 % thin bed, 9.6 kg (game, job 3126: 12 kg kept the advice
        // calling the bed thick after nearly every throw). The S282's 150 kg box keeps 12 kg.
        Assert.Equal(9.6f, ShovelSize.StartupCap(60f), 3);
        Assert.Equal(12f, ShovelSize.StartupCap(150f));
        Assert.Equal(12f, ShovelSize.StartupCap(0f));
        Assert.Equal(9.6f, ShovelSize.StartupLoad(6f, 25f, 60f), 3);
        Assert.Equal(12f, ShovelSize.StartupLoad(6f, 25f, 150f));
        Assert.Equal(9f, ShovelSize.StartupLoad(6f, 1.5f, 60f));
        // Never less than the player's own.
        Assert.Equal(8f, ShovelSize.StartupLoad(8f, 25f, 40f));
        // One load at the cap is just the bed the advice calls thick, not over it.
        Assert.False(ShovelSize.StartupCap(60f) > FiringAdvice.ThickColdBed * AutoFireman.ColdDraftFire * 60f + 1e-4f);
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
