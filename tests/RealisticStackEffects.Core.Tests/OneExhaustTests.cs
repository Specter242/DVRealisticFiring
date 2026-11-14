using RealisticStackEffects.Core;

namespace RealisticStackEffects.Core.Tests;

 public class OneExhaustTests
{
    private static readonly PlumeTuning Tuning = new PlumeTuning();

    // A light train starting on a smoky fire in fog: little steam through
    // the stack, so what leaves between beats is the fire's smoke and its
    // own water, which shows no white.
    private static PlumeInputs LightStart => new PlumeInputs
    {
        ExhaustPressure = 2f,
        TotalFlowNormalized = 0.03f,
        Soot = 0.3f,
        FireOn = true,
        FireboxGas = 20f,
        PuffHaze = 0.5f,
        SteamWhiteness = 1.2f,
        BlowerWhiteness = 1.2f,
        WaterWhiteness = 0f,
    };

    private static float Brightness(PlumeColor c) => (c.R + c.G + c.B) / 3f;

    private static float Depth(PlumeColor c) => -(float)Math.Log(1f - c.A);

    [Fact]
    public void EachOnItsOwnTheBeatsAreLighterThanTheSmokeBetweenThem()
    {
        var puff = PlumeModel.ForBeat(Tuning, LightStart);
        var haze = PlumeModel.Between(Tuning, LightStart);
        Assert.True(Brightness(puff.Color) > Brightness(haze.Color) + 0.05f, "the fault this guards against");
        Assert.True(haze.SmokeShare > puff.SmokeShare + 0.1f);
    }

    [Fact]
    public void WorkingTheBeatsAndTheSmokeBetweenThemAreOneColor()
    {
        var puff = PlumeModel.ForBeat(Tuning, LightStart);
        var haze = PlumeModel.Between(Tuning, LightStart);
        var share = PlumeModel.AsOne(ref puff, ref haze, 2.5f);
        Assert.InRange(share, 0.3f, 0.9f);
        Assert.Equal(puff.Color.R, haze.Color.R, 5);
        Assert.Equal(puff.Color.A, haze.Color.A, 5);
        Assert.Equal(puff.SmokeShare, haze.SmokeShare, 5);
        Assert.Equal(puff.SmokeColor.R, haze.SmokeColor.R, 5);
    }

    [Fact]
    public void TheMixtureCarriesTheSameSmokeAndSteam()
    {
        var puff = PlumeModel.ForBeat(Tuning, LightStart);
        var haze = PlumeModel.Between(Tuning, LightStart);
        const float beats = 2.5f;
        var puffs = beats * puff.Particles;
        var smokeBefore = puffs * Depth(puff.Color) * puff.SmokeShare + haze.Rate * Depth(haze.Color) * haze.SmokeShare;
        var steamBefore = puffs * Depth(puff.Color) * (1f - puff.SmokeShare) + haze.Rate * Depth(haze.Color) * (1f - haze.SmokeShare);
        PlumeModel.AsOne(ref puff, ref haze, beats);
        var each = puffs + haze.Rate;
        Assert.Equal(smokeBefore, each * Depth(puff.Color) * puff.SmokeShare, 2);
        Assert.Equal(steamBefore, each * Depth(puff.Color) * (1f - puff.SmokeShare), 2);
    }

    [Fact]
    public void TheMixtureLiesBetweenTheTwo()
    {
        var puff = PlumeModel.ForBeat(Tuning, LightStart);
        var haze = PlumeModel.Between(Tuning, LightStart);
        var (light, dark) = (Brightness(puff.Color), Brightness(haze.Color));
        var (least, most) = (puff.SmokeShare, haze.SmokeShare);
        PlumeModel.AsOne(ref puff, ref haze, 2.5f);
        Assert.InRange(Brightness(puff.Color), dark, light);
        Assert.InRange(puff.SmokeShare, least, most);
    }

    [Fact]
    public void StandingOrDriftingEachKeepsItsOwn()
    {
        var puff = PlumeModel.ForBeat(Tuning, LightStart);
        var haze = PlumeModel.Between(Tuning, LightStart);
        var (puffWas, hazeWas) = (puff, haze);
        Assert.Equal(0f, PlumeModel.AsOne(ref puff, ref haze, 0f));
        Assert.Equal(puffWas.Color.A, puff.Color.A);
        Assert.Equal(hazeWas.Color.A, haze.Color.A);
        Assert.Equal(hazeWas.SmokeShare, haze.SmokeShare);
    }

    [Fact]
    public void FastTheMixtureIsNearlyTheBeats()
    {
        var puff = PlumeModel.ForBeat(Tuning, LightStart);
        var haze = PlumeModel.Between(Tuning, LightStart);
        var own = puff;
        Assert.True(PlumeModel.AsOne(ref puff, ref haze, 40f) > 0.9f);
        Assert.Equal(own.SmokeShare, puff.SmokeShare, 1);
        Assert.Equal(Brightness(own.Color), Brightness(puff.Color), 1);
    }
}
