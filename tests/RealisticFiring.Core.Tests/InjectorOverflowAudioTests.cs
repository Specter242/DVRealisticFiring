using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

public class InjectorOverflowAudioTests
{
    private const float Water = 6f, Steam = Water / 13f;

    private static InjectorOverflowAudio Run(float water, float steam, float seconds = 4f, int fps = 60, float volume = 1f)
    {
        var sound = new InjectorOverflowAudio();
        for (var i = 0; i < (int)(seconds * fps); i++) sound.Step(1f / fps, water, steam, Water, Steam, volume);
        return sound;
    }

    [Fact]
    public void DryFeedHasAQuieterCeilingInsteadOfSaturatingTheSteamReleaseClip()
    {
        var sound = Run(0f, Steam);
        Assert.InRange(sound.Steam, 0.249f, 0.251f);
        Assert.Equal(0f, sound.Water);
    }

    [Fact]
    public void PrimeTracksTheSteamCapacityAndStaysBelowFeed()
    {
        var prime = Run(0f, Steam * .2f);
        var feed = Run(0f, Steam);
        Assert.InRange(prime.Steam, .049f, .051f);
        Assert.InRange(prime.Steam / feed.Steam, .199f, .201f);
    }

    [Fact]
    public void StartsQuietlyAndFadesAfterTheInjectorPicksUp()
    {
        var sound = new InjectorOverflowAudio();
        sound.Step(1f / 60f, Water, Steam, Water, Steam, 1f);
        Assert.InRange(sound.Steam, .001f, .1f);
        Assert.InRange(sound.Water, .001f, .1f);
        for (var i = 0; i < 240; i++) sound.Step(1f / 60f, Water, Steam, Water, Steam, 1f);
        var loud = sound.Steam;
        sound.Step(1f / 60f, 0f, 0f, Water, Steam, 1f);
        Assert.True(sound.Steam > 0f && sound.Steam < loud);
        for (var i = 0; i < 120; i++) sound.Step(1f / 60f, 0f, 0f, Water, Steam, 1f);
        Assert.Equal(0f, sound.Steam);
        Assert.Equal(0f, sound.Water);
    }

    [Fact]
    public void MasterSliderMutesImmediatelyAndStillScalesBothSounds()
    {
        var sound = Run(Water, Steam);
        sound.Step(0f, Water, Steam, Water, Steam, 0f);
        Assert.Equal(0f, sound.Steam);
        Assert.Equal(0f, sound.Water);
        var normal = Run(Water, Steam);
        var doubled = Run(Water, Steam, volume: 2f);
        Assert.InRange(doubled.Steam / normal.Steam, 1.999f, 2.001f);
        Assert.InRange(doubled.Water / normal.Water, 1.999f, 2.001f);
    }

    [Fact]
    public void AttackAndReleaseAreIndependentOfFrameRate()
    {
        var slow = Run(Water, Steam, seconds: .5f, fps: 10);
        var fast = Run(Water, Steam, seconds: .5f, fps: 120);
        Assert.InRange(Math.Abs(slow.Steam - fast.Steam), 0f, 1e-5f);
        for (var i = 0; i < 5; i++) slow.Step(.1f, 0f, 0f, Water, Steam, 1f);
        for (var i = 0; i < 60; i++) fast.Step(1f / 120f, 0f, 0f, Water, Steam, 1f);
        Assert.InRange(Math.Abs(slow.Steam - fast.Steam), 0f, 1e-5f);
    }

    [Theory]
    [InlineData(2.7f)] [InlineData(4.17f)] [InlineData(7f)]
    public void RatedSteamScalesAcrossDifferentInjectorSizes(float rated)
    {
        var sound = new InjectorOverflowAudio();
        for (var i = 0; i < 240; i++) sound.Step(1f / 60f, 0f, rated / 13f, rated, rated / 13f, 1f);
        Assert.InRange(sound.Steam, .249f, .251f);
    }

    [Fact]
    public void InvalidCapacityCannotMakeTheSoundNonfinite()
    {
        var sound = Run(Water, Steam);
        for (var i = 0; i < 240; i++) sound.Step(1f / 60f, float.NaN, float.NaN, 0f, float.NaN, 1f);
        Assert.Equal(0f, sound.Steam);
        Assert.Equal(0f, sound.Water);
    }
}
