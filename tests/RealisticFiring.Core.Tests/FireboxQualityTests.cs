using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

public class FireboxQualityTests
{
    [Theory]
    [InlineData(FireboxQuality.High, 1, 0f)]
    [InlineData(FireboxQuality.Medium, 2, 0.05f)]
    [InlineData(FireboxQuality.Low, 4, 0.1f)]
    [InlineData((FireboxQuality)999, 1, 0f)]
    public void QualityAlsoBoundsCoalGeometryAndSurfaceUploads(FireboxQuality quality, int stride, float interval)
    {
        var detail = FireboxDetail.For(quality);
        Assert.Equal(stride, detail.LumpStride);
        Assert.Equal(interval, detail.UploadInterval);
    }

    [Theory]
    [InlineData(false, false, 128f, 0.02f)]
    [InlineData(false, true, 64f, 0.02f)]
    [InlineData(true, false, 24f, 0.06f)]
    [InlineData(true, true, 24f, 0.06f)]
    public void HighPreservesExistingBudgets(bool closed, bool inside, float steps, float spacing)
    {
        var sampling = FireboxSampling.For(FireboxQuality.High, closed, inside);
        Assert.Equal(steps, sampling.MaxSteps);
        Assert.Equal(spacing, sampling.StepLength);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LowerDetailReducesBothLongAndShortRaySampling(bool inside)
    {
        var high = FireboxSampling.For(FireboxQuality.High, false, inside);
        var medium = FireboxSampling.For(FireboxQuality.Medium, false, inside);
        var low = FireboxSampling.For(FireboxQuality.Low, false, inside);
        Assert.Equal(high.MaxSteps / 2f, medium.MaxSteps);
        Assert.Equal(high.MaxSteps / 4f, low.MaxSteps);
        Assert.Equal(high.StepLength * 2f, medium.StepLength);
        Assert.Equal(high.StepLength * 4f, low.StepLength);
        Assert.Equal(high.MaxSteps * high.StepLength, medium.MaxSteps * medium.StepLength);
        Assert.Equal(high.MaxSteps * high.StepLength, low.MaxSteps * low.StepLength);
    }

    // Audit 2026-11-09: the budget took no account of resolution or VR.
    [Theory]
    [InlineData(0.42f, 128f)]   // 1920 x 1080
    [InlineData(1f, 128f)]      // the reference screen
    [InlineData(1.67f, 76.6f)]  // 3840 x 2160
    [InlineData(1.8f, 71.1f)]   // a headset's two eyes
    [InlineData(5f, 64f)]       // never under half
    [InlineData(float.NaN, 128f)]
    [InlineData(float.PositiveInfinity, 128f)]
    public void MorePixelsThanTheReferenceScreenThinTheSamplesAndKeepTheReach(float load, float steps)
    {
        var plain = FireboxSampling.For(FireboxQuality.High, false, false);
        var sampling = FireboxSampling.For(FireboxQuality.High, false, false, pixelLoad: load);
        Assert.Equal(steps, sampling.MaxSteps, 0.1f);
        Assert.Equal(plain.MaxSteps * plain.StepLength, sampling.MaxSteps * sampling.StepLength, 0.001f);
        Assert.Equal(24f, FireboxSampling.For(FireboxQuality.High, true, false, pixelLoad: load).MaxSteps);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AtHalfResolutionEachQualityTakesItsSamplesTwiceAsCloseWithinTheShadersLimit(bool inside)
    {
        var high = FireboxSampling.For(FireboxQuality.High, false, inside, halfResolution: true);
        var medium = FireboxSampling.For(FireboxQuality.Medium, false, inside, halfResolution: true);
        var low = FireboxSampling.For(FireboxQuality.Low, false, inside, halfResolution: true);
        var plain = FireboxSampling.For(FireboxQuality.High, false, inside);
        var plainMedium = FireboxSampling.For(FireboxQuality.Medium, false, inside);

        Assert.Equal(plain.StepLength / 2f, high.StepLength);
        Assert.Equal(System.Math.Min(128f, plain.MaxSteps * 2f), high.MaxSteps);
        Assert.Equal(plain.MaxSteps, medium.MaxSteps);
        Assert.Equal(plain.StepLength, medium.StepLength);
        Assert.Equal(plainMedium.MaxSteps, low.MaxSteps);
        Assert.Equal(plainMedium.StepLength, low.StepLength);
        Assert.All(new[] { high, medium, low }, s => Assert.InRange(s.MaxSteps, 4f, 128f));
    }

    [Fact]
    public void ShutDoorRetainsItsFireAtEveryQuality()
    {
        foreach (var quality in Enum.GetValues<FireboxQuality>())
        {
            var sampling = FireboxSampling.For(quality, true, false);
            Assert.Equal(24f, sampling.MaxSteps);
            Assert.Equal(0.06f, sampling.StepLength);
        }
    }

    [Fact]
    public void UnknownQualityFallsBackToHigh()
    {
        var expected = FireboxSampling.For(FireboxQuality.High, false, false);
        var actual = FireboxSampling.For((FireboxQuality)999, false, false);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(-20f)]
    [InlineData(8f)]
    [InlineData(64f)]
    [InlineData(300f)]
    public void DevelopmentTuningStaysWithinShaderLimits(float tunedSteps)
    {
        foreach (var quality in Enum.GetValues<FireboxQuality>())
            foreach (var inside in new[] { false, true })
            {
                var sampling = FireboxSampling.For(quality, false, inside, tunedSteps, 0.04f);
                Assert.InRange(sampling.MaxSteps, 4f, 128f);
            }
        var tuned = FireboxSampling.For(FireboxQuality.High, false, true, 64f, 0.04f);
        Assert.Equal(32f, tuned.MaxSteps);
        Assert.Equal(0.04f, tuned.StepLength);
        var closed = FireboxSampling.For(FireboxQuality.Low, true, false, tunedSteps, 0.1f);
        Assert.Equal(0.1f, closed.StepLength);
    }
}
