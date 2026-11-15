using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

 public class TryCockTests
{
    private const float Working = 14f;
    private const float Hot = 195f;

    [Fact]
    public void ShutPassesNothing()
    {
        var flow = TryCock.Flow(0f, 0.1f, 0f, Working, Hot);
        Assert.False(flow.Flowing);
    }

    [Fact]
    public void AboveTheWaterItBlowsDrySteam()
    {
        var flow = TryCock.Flow(1f, -0.05f, 0f, Working, Hot);
        Assert.Equal(0f, flow.Water, 4);
        Assert.InRange(flow.Steam, 0.02f, 0.06f);
    }

    [Fact]
    public void UnderTheWaterItBlowsWaterThatPartlyFlashes()
    {
        var flow = TryCock.Flow(1f, 0.05f, 0f, Working, Hot);
        Assert.Equal(0f, flow.Steam, 4);
        Assert.InRange(flow.Water, 0.3f, 0.7f);
        // About a sixth to a fifth of 14 bar water flashes in the air.
        Assert.InRange(flow.Flashed / flow.Water, 0.15f, 0.22f);
        Assert.True(flow.Spray > flow.Flashed);
    }

    [Fact]
    public void AtTheSurfaceItSpitsBoth()
    {
        var flow = TryCock.Flow(1f, 0f, 0f, Working, Hot);
        Assert.Equal(0.5f, flow.Wet, 3);
        Assert.True(flow.Water > 0f && flow.Steam > 0f);
    }

    [Fact]
    public void FoamWidensTheWetBandAndFroths()
    {
        // An inch and a half above the still water's surface: steam when it
        // is still, partly water when it foams.
        const float above = -0.015f;
        var still = TryCock.Flow(1f, above, 0f, Working, Hot);
        var foaming = TryCock.Flow(1f, above, 1f, Working, Hot);
        Assert.True(still.Wet < 0.01f);
        Assert.InRange(foaming.Wet, 0.2f, 0.5f);
        Assert.True(foaming.Froth > 0.2f);
        Assert.Equal(0f, still.Froth);
    }

    [Fact]
    public void CrackedOpenItPassesLess()
    {
        var full = TryCock.Flow(1f, -0.05f, 0f, Working, Hot);
        var cracked = TryCock.Flow(0.25f, -0.05f, 0f, Working, Hot);
        Assert.Equal(0.25f * full.Steam, cracked.Steam, 5);
    }

    [Fact]
    public void AColdBoilerOnlyDribbles()
    {
        var under = TryCock.Flow(1f, 0.05f, 0f, 1.01325f, 20f);
        var above = TryCock.Flow(1f, -0.05f, 0f, 1.01325f, 20f);
        Assert.InRange(under.Water, 0.005f, 0.05f);
        Assert.Equal(0f, under.Flashed);
        Assert.False(above.Flowing);
    }

    [Fact]
    public void BadInputsPassNothingStrange()
    {
        var flow = TryCock.Flow(float.NaN, float.NaN, float.NaN, float.NaN, float.NaN);
        Assert.False(flow.Flowing);
        flow = TryCock.Flow(1f, float.PositiveInfinity, -3f, float.NaN, float.NaN);
        Assert.True(float.IsFinite(flow.Water) && float.IsFinite(flow.Steam));
    }
}
