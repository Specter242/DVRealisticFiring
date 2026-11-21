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

    [Fact]
    public void WindShearEqualsSurfaceAtTheLipAndIncreasesMonotonicallyWithHeight()
    {
        const float surface = 2.5f;
        Assert.Equal(surface, PlumeWind.AtHeight(surface, 0f), 4);
        Assert.Equal(surface, PlumeWind.AtHeight(surface, -2f), 4);
        Assert.Equal(0f, PlumeWind.AtHeight(0f, 100f), 4);

        var heights = new[] { 0f, 2f, 5f, 10f, 25f, 50f, 100f, 250f };
        var previous = PlumeWind.AtHeight(surface, heights[0]);
        for (var i = 1; i < heights.Length; i++)
        {
            var current = PlumeWind.AtHeight(surface, heights[i]);
            Assert.True(current > previous, $"height {heights[i]} m ({current:F3}) should exceed {heights[i - 1]} m ({previous:F3})");
            previous = current;
        }

        // Gentle near the stack so near-stack tuning holds, clearly stronger aloft.
        Assert.InRange(PlumeWind.ShearFactor(5f), 1.02f, 1.12f);
        Assert.True(PlumeWind.ShearFactor(100f) > 1.3f);
    }

    [Fact]
    public void WindShearIsCappedAndSafeForNonFiniteInputs()
    {
        // Capped at finite MaxShearFactor at extreme and infinite heights.
        Assert.Equal(PlumeWind.MaxShearFactor, PlumeWind.ShearFactor(10000f), 4);
        Assert.Equal(PlumeWind.MaxShearFactor, PlumeWind.ShearFactor(float.PositiveInfinity), 4);
        Assert.Equal(1f, PlumeWind.ShearFactor(float.NegativeInfinity), 4);
        Assert.Equal(1f, PlumeWind.ShearFactor(float.NaN), 4);

        // AtHeight stays finite and never produces NaN (e.g. 0 * Infinity).
        Assert.Equal(0f, PlumeWind.AtHeight(0f, float.PositiveInfinity));
        Assert.Equal(0f, PlumeWind.AtHeight(0f, float.NaN));
        Assert.Equal(0f, PlumeWind.AtHeight(-3f, 50f));
        Assert.Equal(0f, PlumeWind.AtHeight(float.NaN, 50f));
        Assert.Equal(0f, PlumeWind.AtHeight(float.PositiveInfinity, 50f));
        Assert.Equal(0f, PlumeWind.AtHeight(float.NegativeInfinity, 50f));

        Assert.Equal(2.5f * PlumeWind.MaxShearFactor, PlumeWind.AtHeight(2.5f, float.PositiveInfinity), 4);
        Assert.Equal(2.5f, PlumeWind.AtHeight(2.5f, float.NegativeInfinity), 4);
        Assert.Equal(2.5f, PlumeWind.AtHeight(2.5f, float.NaN), 4);
    }

    [Fact]
    public void DiurnalInversionFormsOnCalmDawnsAndNights()
    {
        var dawnStrength = PlumeWind.InversionStrength(hour: 5.5f, windSpeed: 0.3f, rain: 0f);
        var dawnCeiling = PlumeWind.InversionCeiling(hour: 5.5f, windSpeed: 0.3f, rain: 0f);
        Assert.Equal(1f, dawnStrength, 3);
        Assert.InRange(dawnCeiling, PlumeWind.InversionLowCeiling, PlumeWind.InversionHighCeiling);
        Assert.Equal(PlumeWind.InversionLowCeiling, dawnCeiling, 3);

        // Morning burn-off weakens and lifts the ceiling before it vanishes.
        var morningStrength = PlumeWind.InversionStrength(hour: 7.5f, windSpeed: 0.3f, rain: 0f);
        var morningCeiling = PlumeWind.InversionCeiling(hour: 7.5f, windSpeed: 0.3f, rain: 0f);
        Assert.InRange(morningStrength, 0.2f, 0.9f);
        Assert.True(morningCeiling > dawnCeiling && morningCeiling < PlumeWind.InversionHighCeiling);

        // Late evening rebuilds a gentle layer and wraps continuously across midnight.
        var evening = PlumeWind.InversionStrength(hour: 21.5f, windSpeed: 0.4f, rain: 0f);
        var beforeMidnight = PlumeWind.InversionStrength(hour: 23.95f, windSpeed: 0.3f, rain: 0f);
        var afterMidnight = PlumeWind.InversionStrength(hour: 0.05f, windSpeed: 0.3f, rain: 0f);
        Assert.True(evening > 0.2f);
        Assert.InRange(MathF.Abs(beforeMidnight - afterMidnight), 0f, 0.02f);
    }

    [Fact]
    public void DiurnalInversionVanishesAtMiddayInHighWindOrInRain()
    {
        // Midday and afternoon convective mixing: no cap even in dead calm.
        foreach (var hour in new[] { 9f, 12f, 15f, 18.5f })
        {
            Assert.Equal(0f, PlumeWind.InversionStrength(hour, windSpeed: 0f, rain: 0f));
            Assert.True(float.IsPositiveInfinity(PlumeWind.InversionCeiling(hour, windSpeed: 0f, rain: 0f)));
        }

        // Strong wind mixes out even a dawn inversion.
        Assert.Equal(0f, PlumeWind.InversionStrength(hour: 5f, windSpeed: 4f, rain: 0f));
        Assert.True(float.IsPositiveInfinity(PlumeWind.InversionCeiling(hour: 5f, windSpeed: 4f, rain: 0f)));

        // Rain eliminates radiative inversion even at calm dawn.
        Assert.Equal(0f, PlumeWind.InversionStrength(hour: 5f, windSpeed: 0.2f, rain: 0.5f));
        Assert.True(float.IsPositiveInfinity(PlumeWind.InversionCeiling(hour: 5f, windSpeed: 0.2f, rain: 0.5f)));
    }
}
