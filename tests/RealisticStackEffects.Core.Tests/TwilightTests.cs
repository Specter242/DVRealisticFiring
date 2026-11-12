using RealisticStackEffects.Core;
using Xunit;

namespace RealisticStackEffects.Core.Tests;

 public class TwilightTests
{
    [Fact]
    public void ByDayTheSunIsWholeOnTheSteam()
    {
        Assert.Equal(1f, Twilight.DirectLight(30f));
        Assert.Equal(1f, Twilight.DirectLight(Twilight.SunWholeBy));
    }

    [Fact]
    public void FromSunsetThroughDuskTheSetSunLightsNoSteam()
    {
        // The capture's dusk: the game's light at a quarter of the day's, the sun down.
        Assert.Equal(0f, Twilight.DirectLight(93f));
        Assert.Equal(0f, Twilight.DirectLight(95.9f));
        Assert.InRange(Twilight.DirectLight(88f), 0.3f, 0.7f);
    }

    [Fact]
    public void ItFadesWithoutAStepAndTheMoonsLightReturns()
    {
        for (var zenith = 80f; zenith < 110f; zenith += 0.25f)
            Assert.True(System.Math.Abs(Twilight.DirectLight(zenith + 0.25f) - Twilight.DirectLight(zenith)) < 0.07f, $"a step at {zenith}");
        Assert.Equal(1f, Twilight.DirectLight(Twilight.MoonBy));
        Assert.Equal(1f, Twilight.DirectLight(150f));
        Assert.Equal(1f, Twilight.DirectLight(float.NaN));
    }
}
