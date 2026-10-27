using RealisticFiring.Core;
using RealisticStackEffects.Core;

namespace RealisticFiring.Core.Tests;

/// <summary>An oil fire's stack in the chimney plume: its haze, its black smoke, and a clear stack with too much air.</summary>
public class OilPlumeTests
{
    private static readonly PlumeTuning Tuning = new PlumeTuning();

    private static PlumeInputs Working(float pressure, float smoke = 0f, float flow = 0.6f) => new PlumeInputs
    {
        ExhaustPressure = pressure,
        TotalFlowNormalized = flow,
        Soot = smoke,
        FireOn = true,
    };

    private static float Brightness(PlumeColor c) => (c.R + c.G + c.B) / 3f;

    [Fact]
    public void AnOilStackReadsAsThePlumesHazeAndSoot()
    {
        // Too much air: a clear stack; a well-set fire: a clean fire's haze; black: full soot.
        Assert.Equal((0f, 1f), OilPlume.Stack(0f));
        Assert.Equal((0f, 0f), OilPlume.Stack(OilFire.HazeSmoke));
        Assert.Equal(PlumeModel.SootForBlack, OilPlume.Stack(1f).Soot, 3);
        Assert.True(OilPlume.Stack(0.5f).Soot > 0f && OilPlume.Stack(0.5f).Soot < 1f);
    }

    [Fact]
    public void AnOilFiresLightHazeTintsItsBeatsAndLingers()
    {
        // A light haze read the same as a clear stack: white steam, gone in seconds.
        var clear = PlumeModel.ForBeat(Tuning, OilPlume.Inputs(Tuning, 0f, 4f, 0.5f, true));
        var haze = PlumeModel.ForBeat(Tuning, OilPlume.Inputs(Tuning, OilFire.HazeSmoke, 4f, 0.5f, true));
        Assert.True(Brightness(haze.Color) < Brightness(clear.Color) - 0.1f);
        Assert.Equal(0f, clear.SmokeShare, 3);
        Assert.True(haze.SmokeShare > 0.2f);
        // A coal fire's beats stay white steam.
        Assert.Equal(0f, PlumeModel.ForBeat(Tuning, Working(4f)).SmokeShare, 3);
        // Too much air thins the steam: a clear stack is thinner than a light haze.
        Assert.True(clear.Color.A < 0.7f * haze.Color.A);
    }

    [Fact]
    public void AnOilFireBurningRightDrawsAWellFiredCoalFiresHaze()
    {
        // The 2901 burning right drew a solid gray column on a hot, dry day:
        // its beats carried twice a well-fired coal fire's haze.
        var oil = OilPlume.Inputs(Tuning, OilFire.HazeSmoke, 4f, 0.5f, true);
        var coal = Working(4f);
        coal.PuffHaze = PlumeModel.WellFiredHaze(Tuning, 0f);
        oil.SteamWhiteness = coal.SteamWhiteness = 0f;
        var oilBeat = PlumeModel.ForBeat(Tuning, oil);
        var coalBeat = PlumeModel.ForBeat(Tuning, coal);
        Assert.Equal(coalBeat.SmokeShare, oilBeat.SmokeShare, 3);
        Assert.Equal(coalBeat.Color.A, oilBeat.Color.A, 3);
    }
}
