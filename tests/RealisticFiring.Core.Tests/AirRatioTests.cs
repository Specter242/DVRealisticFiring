using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

 public class AirRatioTests
{
    private static readonly FireboxParameters S282 = new FireboxParameters(150f, 120f, 0.5f);

    [Theory]
    [InlineData(0.15f)]
    [InlineData(0.4f)]
    [InlineData(0.75f)]
    public void ARightlyFedBedReadsNearTheDesignAirAtAnyRate(float load)
    {
        var ratio = new TendedFire(S282, load).Fire.AirRatio;
        Assert.InRange(ratio, 1.0f, 1.3f);
    }

    [Theory]
    [InlineData(0.15f)]
    [InlineData(0.4f)]
    public void AnOverstokedBedIsChokedAndSmokes(float load)
    {
        var fire = new TendedFire(S282, load, coalFactor: 1.5f).Fire;
        Assert.True(fire.AirRatio < 0.9f, $"ratio {fire.AirRatio:F2}");
        Assert.True(fire.SmokeDensity > 0.4f, $"smoke {fire.SmokeDensity:P0}");
    }

    [Theory]
    [InlineData(0.4f)]
    [InlineData(0.75f)]
    public void AnUnderfedBedLetsTheAirThrough(float load)
    {
        var ratio = new TendedFire(S282, load, coalFactor: 0.5f).Fire.AirRatio;
        Assert.True(ratio > 1.6f, $"ratio {ratio:F2}");
    }
}
