using RealisticStackEffects.Core;

namespace RealisticStackEffects.Core.Tests;

/// <summary>
/// Sanitization and application of optional chimney geometry hints
/// (<see cref="StackGeometryHint"/>): valid values pass through, out-of-range
/// positive values clamp to physical bounds, invalid/NaN/negative values are
/// dropped, and bore never exceeds outside radius.
/// </summary>
public class StackGeometryHintTests
{
    [Fact]
    public void ValidHintAppliesAllThreeDimensions()
    {
        var hint = new StackGeometryHint
        {
            LipHeight = 0.37f,
            BoreRadius = 0.22f,
            OutsideRadius = 0.29f,
        };
        float? lip = null;
        float bore = StackGeometryHint.DefaultBoreRadius;
        float outside = StackGeometryHint.DefaultOutsideRadius;

        hint.Apply(ref lip, ref bore, ref outside);

        Assert.Equal(0.37f, lip);
        Assert.Equal(0.22f, bore, 4);
        Assert.Equal(0.29f, outside, 4);
    }

    [Fact]
    public void ZeroLipHeightAtEmitterIsAccepted()
    {
        var clean = new StackGeometryHint { LipHeight = 0f }.Sanitize();
        Assert.True(clean.HasAny);
        Assert.Equal(0f, clean.LipHeight);
    }

    [Fact]
    public void InvalidNegativeZeroAndNonFiniteValuesAreDropped()
    {
        var invalid = new StackGeometryHint
        {
            LipHeight = -0.1f,
            BoreRadius = float.NaN,
            OutsideRadius = float.PositiveInfinity,
        };
        Assert.False(invalid.Sanitize().HasAny);

        var zeroRadii = new StackGeometryHint
        {
            LipHeight = float.NegativeInfinity,
            BoreRadius = 0f,
            OutsideRadius = -0.2f,
        };
        Assert.False(zeroRadii.Sanitize().HasAny);

        float? lip = 0.45f;
        float bore = 0.21f;
        float outside = 0.30f;
        invalid.Apply(ref lip, ref bore, ref outside);
        Assert.Equal(0.45f, lip);
        Assert.Equal(0.21f, bore, 4);
        Assert.Equal(0.30f, outside, 4);
    }

    [Fact]
    public void PartialHintSanitizesBadFieldsWhileKeepingGoodOnes()
    {
        var mixed = new StackGeometryHint
        {
            LipHeight = 0.42f,
            BoreRadius = -0.5f,
            OutsideRadius = float.NaN,
        };
        var clean = mixed.Sanitize();
        Assert.True(clean.HasAny);
        Assert.Equal(0.42f, clean.LipHeight);
        Assert.Null(clean.BoreRadius);
        Assert.Null(clean.OutsideRadius);

        float? lip = null;
        float bore = 0.24f;
        float outside = 0.31f;
        mixed.Apply(ref lip, ref bore, ref outside);
        Assert.Equal(0.42f, lip);
        Assert.Equal(0.24f, bore, 4);
        Assert.Equal(0.31f, outside, 4);
    }

    [Fact]
    public void PositiveOutOfRangeValuesClampToBounds()
    {
        var high = new StackGeometryHint
        {
            LipHeight = 9.5f,
            BoreRadius = 1.2f,
            OutsideRadius = 2.0f,
        }.Sanitize();
        Assert.Equal(StackGeometryHint.MaxLipHeight, high.LipHeight);
        Assert.Equal(StackGeometryHint.MaxOutsideRadius, high.BoreRadius);
        Assert.Equal(StackGeometryHint.MaxOutsideRadius, high.OutsideRadius);

        var low = new StackGeometryHint
        {
            LipHeight = 0.05f,
            BoreRadius = 0.01f,
            OutsideRadius = 0.02f,
        }.Sanitize();
        Assert.Equal(0.05f, low.LipHeight);
        Assert.Equal(StackGeometryHint.MinBoreRadius, low.BoreRadius);
        Assert.Equal(StackGeometryHint.MinOutsideRadius, low.OutsideRadius);
    }

    [Fact]
    public void BoreNeverExceedsOutsideRadiusWhenBothProvided()
    {
        var inverted = new StackGeometryHint
        {
            BoreRadius = 0.42f,
            OutsideRadius = 0.28f,
        }.Sanitize();
        Assert.Equal(0.28f, inverted.OutsideRadius);
        Assert.Equal(0.28f, inverted.BoreRadius);
    }

    [Fact]
    public void BoreOnlyExpandsOutsideRadiusIfLargerThanBaseline()
    {
        float? lip = 0.5f;
        float bore = StackGeometryHint.DefaultBoreRadius;
        float outside = StackGeometryHint.DefaultOutsideRadius;

        new StackGeometryHint { BoreRadius = 0.42f }.Apply(ref lip, ref bore, ref outside);

        Assert.Equal(0.5f, lip);
        Assert.Equal(0.42f, bore, 4);
        Assert.Equal(0.42f, outside, 4);
    }

    [Fact]
    public void OutsideOnlyClampsBaselineBoreIfNarrower()
    {
        float? lip = 0.5f;
        float bore = StackGeometryHint.DefaultBoreRadius;
        float outside = StackGeometryHint.DefaultOutsideRadius;

        new StackGeometryHint { OutsideRadius = 0.16f }.Apply(ref lip, ref bore, ref outside);

        Assert.Equal(0.5f, lip);
        Assert.Equal(0.16f, outside, 4);
        Assert.Equal(0.16f, bore, 4);
    }
}
