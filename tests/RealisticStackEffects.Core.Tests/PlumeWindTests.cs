using RealisticStackEffects.Core;

namespace RealisticStackEffects.Core.Tests;

 public class PlumeWindTests
{
    [Fact]
    public void AnOrdinaryDayBlowsALightBreezeWithCalmSpells()
    {
        var calm = PlumeWind.Speed(0f, 0f, spell: 0f, gust: 0.5f, scale: 1f);
        var breezy = PlumeWind.Speed(0f, 0f, spell: 1f, gust: 0.5f, scale: 1f);
        Assert.InRange(calm, 0.1f, 0.5f);
        Assert.Equal(PlumeWind.Breeze, breezy, 3);
    }

    [Fact]
    public void RainAndStormsBlowHarder()
    {
        var dry = PlumeWind.Speed(0f, 0f, 0.5f, 0.5f, 1f);
        var rain = PlumeWind.Speed(1f, 0f, 0.5f, 0.5f, 1f);
        var storm = PlumeWind.Speed(1f, 1f, 0.5f, 0.5f, 1f);
        Assert.True(rain > dry + 2f);
        Assert.True(storm > rain + 4f);
    }

    [Fact]
    public void GustsBlowAboveAndBelowAndTheSettingScalesIt()
    {
        Assert.True(PlumeWind.Speed(0f, 0f, 1f, 1f, 1f) > PlumeWind.Speed(0f, 0f, 1f, 0f, 1f));
        Assert.Equal(0f, PlumeWind.Speed(1f, 1f, 1f, 1f, 0f));
        Assert.Equal(2f * PlumeWind.Speed(0f, 0f, 1f, 0.5f, 1f), PlumeWind.Speed(0f, 0f, 1f, 0.5f, 2f), 3);
    }

    [Fact]
    public void TheWindBlowsTheWayTheCloudsDrift()
    {
        // The game's clouds drift by (sin, cos) of its wind direction.
        var north = PlumeWind.Toward(0f);
        var east = PlumeWind.Toward(90f);
        Assert.Equal(0f, north.X, 3);
        Assert.Equal(1f, north.Z, 3);
        Assert.Equal(1f, east.X, 3);
        Assert.Equal(0f, east.Z, 3);
    }
}
