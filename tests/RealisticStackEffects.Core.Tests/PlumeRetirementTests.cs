using RealisticStackEffects.Core;

namespace RealisticStackEffects.Core.Tests;

public class PlumeRetirementTests
{
    [Fact]
    public void CrossingInvisibleStartsACompleteFadeInsteadOfRemovingTheParcel()
    {
        var life = new PlumeRetirement();
        Assert.False(PlumeRetirement.ShouldRetire(3f, .4f, .02f, .01f, true, true));
        Assert.Equal(1f, life.Fade(3f, false, .75f));
        Assert.True(PlumeRetirement.ShouldRetire(3.1f, .4f, .009f, .01f, true, true));
        Assert.Equal(1f, life.Fade(3.1f, true, .75f));
        Assert.InRange(life.Fade(3.475f, true, .75f), .249f, .251f);
        Assert.True(life.Fade(3.8f, true, .75f) > 0f);
        Assert.Equal(0f, life.Fade(3.86f, true, .75f));
        Assert.Equal(0f, life.Fade(4f, false, .75f));
    }

    [Fact]
    public void SteamStillFormingAndNewParticlesAreProtectedFromFaintnessRetirement()
    {
        Assert.False(PlumeRetirement.ShouldRetire(2f, .4f, 0f, .01f, false, true));
        Assert.False(PlumeRetirement.ShouldRetire(.1f, .4f, 0f, .01f, true, true));
        Assert.True(PlumeRetirement.ShouldRetire(2f, .4f, .5f, .01f, false, false));
        // A user faintness threshold below the physical cutoff still fades.
        Assert.True(PlumeRetirement.ShouldRetire(2f, .4f, .001f, 0f, true, true));
    }
}
