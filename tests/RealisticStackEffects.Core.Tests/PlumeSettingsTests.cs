using RealisticStackEffects.Core;

namespace RealisticStackEffects.Core.Tests;

/// <summary>
/// Player-facing chimney plume sliders (SteamVisibility and WindStrength):
/// safe bounds, NaN/infinity fallbacks, identity physics at 1.0, and scaling
/// of steam whiteness and wind drift.
/// </summary>
public class PlumeSettingsTests
{
    [Fact]
    public void SteamVisibilityClampsToValidRangeAndFallsBackOnInvalidValues()
    {
        Assert.Equal(1f, PlumeSettings.DefaultSteamVisibility);
        Assert.Equal(0.5f, PlumeSettings.MinSteamVisibility);
        Assert.Equal(2f, PlumeSettings.MaxSteamVisibility);

        Assert.Equal(0.5f, PlumeSettings.ClampSteamVisibility(0.5f));
        Assert.Equal(1f, PlumeSettings.ClampSteamVisibility(1f));
        Assert.Equal(1.4f, PlumeSettings.ClampSteamVisibility(1.4f));
        Assert.Equal(2f, PlumeSettings.ClampSteamVisibility(2f));

        Assert.Equal(0.5f, PlumeSettings.ClampSteamVisibility(0f));
        Assert.Equal(0.5f, PlumeSettings.ClampSteamVisibility(-3f));
        Assert.Equal(2f, PlumeSettings.ClampSteamVisibility(5f));
        Assert.Equal(1f, PlumeSettings.ClampSteamVisibility(float.NaN));
        Assert.Equal(1f, PlumeSettings.ClampSteamVisibility(float.PositiveInfinity));
        Assert.Equal(1f, PlumeSettings.ClampSteamVisibility(float.NegativeInfinity));
    }

    [Fact]
    public void WindStrengthClampsToValidRangeAndFallsBackOnInvalidValues()
    {
        Assert.Equal(1f, PlumeSettings.DefaultWindStrength);
        Assert.Equal(0f, PlumeSettings.MinWindStrength);
        Assert.Equal(2f, PlumeSettings.MaxWindStrength);

        Assert.Equal(0f, PlumeSettings.ClampWindStrength(0f));
        Assert.Equal(1f, PlumeSettings.ClampWindStrength(1f));
        Assert.Equal(1.5f, PlumeSettings.ClampWindStrength(1.5f));
        Assert.Equal(2f, PlumeSettings.ClampWindStrength(2f));

        Assert.Equal(0f, PlumeSettings.ClampWindStrength(-1f));
        Assert.Equal(2f, PlumeSettings.ClampWindStrength(10f));
        Assert.Equal(1f, PlumeSettings.ClampWindStrength(float.NaN));
        Assert.Equal(1f, PlumeSettings.ClampWindStrength(float.PositiveInfinity));
        Assert.Equal(1f, PlumeSettings.ClampWindStrength(float.NegativeInfinity));
    }

    [Fact]
    public void SteamWhitenessScalingPreservesPhysicsAtOneAndScalesCleanly()
    {
        Assert.Equal(0.35f, PlumeSettings.ScaleSteamWhiteness(0.35f, 1f), 5);
        Assert.Equal(0.175f, PlumeSettings.ScaleSteamWhiteness(0.35f, 0.5f), 5);
        Assert.Equal(0.7f, PlumeSettings.ScaleSteamWhiteness(0.35f, 2f), 5);

        // Out-of-range and non-finite visibility values clamp cleanly.
        Assert.Equal(0.175f, PlumeSettings.ScaleSteamWhiteness(0.35f, -2f), 5);
        Assert.Equal(0.7f, PlumeSettings.ScaleSteamWhiteness(0.35f, 9f), 5);
        Assert.Equal(0.35f, PlumeSettings.ScaleSteamWhiteness(0.35f, float.NaN), 5);

        // Zero, negative, or non-finite base whiteness never produces steam.
        Assert.Equal(0f, PlumeSettings.ScaleSteamWhiteness(0f, 2f));
        Assert.Equal(0f, PlumeSettings.ScaleSteamWhiteness(-0.5f, 2f));
        Assert.Equal(0f, PlumeSettings.ScaleSteamWhiteness(float.NaN, 2f));
        Assert.Equal(0f, PlumeSettings.ScaleSteamWhiteness(float.PositiveInfinity, 2f));
    }

    [Fact]
    public void SteamVisibilityRaisesAndLowersPuffAndBlowerOpacityWithoutBreakingDefault()
    {
        var tuning = new PlumeTuning();
        var afternoon = PlumeClimate.Air(SmokeSeason.Summer, 13f, 0f, 0f);
        SteamCloud.ForBeats(afternoon, SteamCloud.ReferenceFlueGas, 0f, out var baseWhite);
        var streamWhite = SteamCloud.For(PlumeAir.Reference, tuning.BlowerFlueGasMixing * 3f).Whiteness;

        PlumeInputs Inputs(float visibility) => new PlumeInputs
        {
            ExhaustPressure = 5f,
            TotalFlowNormalized = 0.6f,
            FireOn = true,
            HeatShare = 0.5f,
            SteamWhiteness = PlumeSettings.ScaleSteamWhiteness(baseWhite, visibility),
            BlowerWhiteness = PlumeSettings.ScaleSteamWhiteness(streamWhite, visibility),
            WaterWhiteness = PlumeSettings.ScaleSteamWhiteness(0.4f, visibility),
        };

        var subtle = PlumeModel.ForBeat(tuning, Inputs(0.5f));
        var normal = PlumeModel.ForBeat(tuning, Inputs(1f));
        var fuller = PlumeModel.ForBeat(tuning, Inputs(2f));

        Assert.True(subtle.Color.A < normal.Color.A);
        Assert.True(fuller.Color.A > normal.Color.A);
        Assert.InRange(fuller.Color.A, 0f, 1f);

        var hazeSubtle = PlumeModel.Between(tuning, Inputs(0.5f));
        var hazeNormal = PlumeModel.Between(tuning, Inputs(1f));
        var hazeFuller = PlumeModel.Between(tuning, Inputs(2f));

        Assert.True(hazeSubtle.Color.A < hazeNormal.Color.A);
        Assert.True(hazeFuller.Color.A > hazeNormal.Color.A);
    }

    [Fact]
    public void ClampedWindStrengthScalesPlumeWindFromStillAirToDouble()
    {
        var still = PlumeWind.Speed(0.5f, 0.2f, 0.8f, 0.6f, PlumeSettings.ClampWindStrength(0f));
        var normal = PlumeWind.Speed(0.5f, 0.2f, 0.8f, 0.6f, PlumeSettings.ClampWindStrength(1f));
        var strong = PlumeWind.Speed(0.5f, 0.2f, 0.8f, 0.6f, PlumeSettings.ClampWindStrength(2f));
        var invalid = PlumeWind.Speed(0.5f, 0.2f, 0.8f, 0.6f, PlumeSettings.ClampWindStrength(float.NaN));

        Assert.Equal(0f, still);
        Assert.True(normal > 0f);
        Assert.Equal(2f * normal, strong, 3);
        Assert.Equal(normal, invalid, 5);
    }
}
