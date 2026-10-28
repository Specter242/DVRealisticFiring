using RealisticStackEffects.Core;

namespace RealisticStackEffects.Core.Tests;

/// <summary>
/// The smoke quality: Medium draws the plume at half resolution, Low with
/// half its particles besides; without half resolution, half and a quarter
/// of them. A share is thrown exactly on average over many puffs, but never
/// a beat without its puff. Medium and Low let the plume's faint far end go.
/// </summary>
public class SmokeQualityTests
{
    [Fact]
    public void HighThrowsEveryParticle()
    {
        var due = 0f;
        Assert.False(SmokeDetail.HalfResolution(SmokeQuality.High));
        Assert.Equal(1f, SmokeDetail.ParticleShare(SmokeQuality.High, halfResolution: false));
        Assert.Equal(12, SmokeDetail.Scaled(12, SmokeDetail.ParticleShare(SmokeQuality.High, false), ref due, least: 1));
        Assert.Equal(0f, due);
    }

    [Fact]
    public void HalfResolutionKeepsMoreParticles()
    {
        Assert.True(SmokeDetail.HalfResolution(SmokeQuality.Medium));
        Assert.True(SmokeDetail.HalfResolution(SmokeQuality.Low));
        Assert.Equal(1f, SmokeDetail.ParticleShare(SmokeQuality.Medium, halfResolution: true));
        Assert.Equal(0.5f, SmokeDetail.ParticleShare(SmokeQuality.Low, halfResolution: true));
        Assert.Equal(0.5f, SmokeDetail.ParticleShare(SmokeQuality.Medium, halfResolution: false));
        Assert.Equal(0.25f, SmokeDetail.ParticleShare(SmokeQuality.Low, halfResolution: false));
    }

    [Theory]
    [InlineData(SmokeQuality.Medium, 7)]
    [InlineData(SmokeQuality.Low, 7)]
    [InlineData(SmokeQuality.Low, 30)]
    public void LowerQualitiesThrowTheirShareOnAverage(SmokeQuality quality, int wanted)
    {
        var share = SmokeDetail.ParticleShare(quality, halfResolution: false);
        var due = 0f;
        var thrown = 0;
        for (var beat = 0; beat < 400; beat++)
            thrown += SmokeDetail.Scaled(wanted, share, ref due);
        Assert.InRange(thrown, wanted * share * 400 - 1, wanted * share * 400 + 1);
    }

    [Fact]
    public void LowerQualitiesLetTheFaintEndOfTheTrailGo()
    {
        var light = PlumeRise.Parcel(0.3f, 1f);
        float Lasts(SmokeQuality quality)
        {
            for (var seconds = 0f; seconds < 240f; seconds += 0.5f)
                if (PlumeRise.OpacityOf(light.SmokeDepth, 10f, PlumeRise.SpreadWidth(seconds, 14f)) < SmokeDetail.Faint(quality))
                    return seconds;
            return 240f;
        }
        Assert.Equal(PlumeRise.Invisible, SmokeDetail.Faint(SmokeQuality.High));
        Assert.True(Lasts(SmokeQuality.Medium) < Lasts(SmokeQuality.High) / 2f);
        Assert.True(Lasts(SmokeQuality.Low) < Lasts(SmokeQuality.Medium));
        // Smoke that plainly shows stays.
        Assert.True(PlumeRise.OpacityOf(light.SmokeDepth, 0f) > 5f * SmokeDetail.Faint(SmokeQuality.Low));
    }

    [Fact]
    public void EveryBeatKeepsItsPuffsCore()
    {
        var due = 0f;
        for (var beat = 0; beat < 20; beat++)
        {
            var thrown = SmokeDetail.Scaled(2, SmokeDetail.ParticleShare(SmokeQuality.Low, false), ref due, least: 1);
            Assert.InRange(thrown, 1, 2);
        }
        // Nothing wanted, nothing thrown.
        Assert.Equal(0, SmokeDetail.Scaled(0, 0.25f, ref due, least: 1));
    }
}
