using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

 public class PlumeRiseTests
{
    // A hard-working engine (its fire in W, its exhaust steam in kg/s) and
    // an idling one, its blower on.
    private static readonly (float Fire, float Steam) Working = (30e6f, 10f);
    private static readonly (float Fire, float Steam) Idling = (2e6f, 1f);

    private static float Flux((float Fire, float Steam) engine) => PlumeRise.StackFlux(engine.Fire, engine.Steam);

    // Climbs a parcel for so many seconds in still air or past moving air.
    private static float HeightAfter(float seconds, (float Fire, float Steam) engine, float airSpeed = 0f)
    {
        var flux = Flux(engine);
        var height = 0f;
        for (var t = 0f; t < seconds; t += 0.05f)
            height += PlumeRise.RiseSpeed(flux, height, airSpeed) * 0.05f;
        return height;
    }

    [Fact]
    public void AWorkingEnginesPlumeTowersHundredsOfFeetInStillAir()
    {
        // 100 ft is 30 m: within half a minute, and on past 100 m in a minute.
        Assert.True(HeightAfter(30f, Working) > 100f, $"{HeightAfter(30f, Working):F0} m after 30 s");
        Assert.True(HeightAfter(60f, Working) > 150f, $"{HeightAfter(60f, Working):F0} m after a minute");
    }

    [Fact]
    public void ThePlumeNeverStopsRisingButSlowsWithHeight()
    {
        var flux = Flux(Working);
        var low = PlumeRise.RiseSpeed(flux, 10f, 0f);
        var high = PlumeRise.RiseSpeed(flux, 300f, 0f);
        Assert.True(high < low);
        Assert.True(high > 1f, $"{high:F2} m/s at 300 m");
        Assert.True(PlumeRise.RiseSpeed(flux, 3000f, 0f) > 0.5f);
    }

    [Fact]
    public void ABiggerFireRisesFaster()
    {
        Assert.True(HeightAfter(30f, Working) > HeightAfter(30f, Idling));
        Assert.Equal(0f, PlumeRise.RiseSpeed(PlumeRise.StackFlux(0f, 0f), 10f, 0f));
        // Steam alone, the fire out, still rises.
        Assert.True(PlumeRise.RiseSpeed(PlumeRise.StackFlux(0f, 1f), 10f, 0f) > 0.5f);
    }

    [Fact]
    public void AtSpeedThePlumeLiesBack()
    {
        // At 20 m/s it rises a fraction as high in the same time; slow, nearly as in still air.
        var still = HeightAfter(30f, Working);
        Assert.True(HeightAfter(30f, Working, 20f) < 0.5f * still);
        Assert.True(HeightAfter(30f, Working, 1f) > HeightAfter(30f, Working, 20f));
    }

    [Fact]
    public void ALightBreezeDoesNotBendAStrongPlume()
    {
        // Standing in a 1.1 m/s breeze the plume was bent over from the stack.
        Assert.True(HeightAfter(30f, Idling, 1f) > 0.95f * HeightAfter(30f, Idling));
        Assert.True(HeightAfter(30f, Idling, 3f) < HeightAfter(30f, Idling, 1f));
    }

    [Fact]
    public void TheTrainsWakeCarriesThePlumesBaseOnly()
    {
        Assert.Equal(0.5f, PlumeRise.Wake(0f), 3);
        Assert.True(PlumeRise.Wake(20f) < 0.05f);
    }

    [Fact]
    public void CondensingExhaustSteamLiftsThePlume()
    {
        // A working engine's steam is buoyant beyond being light and hot: its latent heat warms the cloud.
        Assert.True(PlumeRise.StackFlux(0f, 10f) > PlumeRise.StackFlux(30e6f, 0f));
    }

    [Fact]
    public void ThePlumeWidensWithHeight()
    {
        Assert.True(PlumeRise.Width(100f) > 20f);
        Assert.True(PlumeRise.Width(0f) < 1f);
    }

    [Fact]
    public void BlackSmokeStaysVisibleHighUpWhileSteamEvaporates()
    {
        var black = PlumeRise.Parcel(0.8f, 1f);
        var steam = PlumeRise.Parcel(0.7f, 0f);
        // A minute on, 300 m up: the smoke still shows, the steam is gone.
        Assert.True(PlumeRise.Opacity(black, 60f, 300f, 4f) > 0.08f);
        Assert.True(PlumeRise.Opacity(steam, 60f, 300f, 4f) < PlumeRise.Invisible);
        // Near the stack both show as they left.
        Assert.Equal(0.8f, PlumeRise.Opacity(black, 0f, 0f, 4f), 2);
        Assert.Equal(0.7f, PlumeRise.Opacity(steam, 0f, 0f, 4f), 2);
        // Cold, damp air keeps the steam longer.
        Assert.True(PlumeRise.Opacity(steam, 10f, 20f, 20f) > PlumeRise.Opacity(steam, 10f, 20f, 4f));
    }

    [Fact]
    public void AtSpeedATrailOfBlackSmokeThinsOutWithinAMileAndAHalf()
    {
        var black = PlumeRise.Parcel(0.8f, 1f);
        float At(float seconds) => PlumeRise.OpacityOf(black.SmokeDepth, 15f, PlumeRise.SpreadWidth(seconds, 20f));
        Assert.True(At(10f) > 0.15f, $"{At(10f):F2} at 200 m");
        Assert.True(At(40f) < 0.06f, $"{At(40f):F2} at 800 m");
        // Standing in still air the towering plume barely spreads by age.
        Assert.True(PlumeRise.SpreadWidth(60f, 0f) < PlumeRise.Width(200f));
    }

    [Fact]
    public void AnAgingPlumeKeepsWidening()
    {
        Assert.True(PlumeRise.PlumeWidth(100f, PlumeRise.SpreadWidth(60f, 5f)) > PlumeRise.PlumeWidth(100f, PlumeRise.SpreadWidth(20f, 5f)));
        Assert.True(PlumeRise.PlumeWidth(100f, 0f) > 30f);
    }

    [Fact]
    public void FewerParticlesCarryTheWiderPlumeAbove()
    {
        Assert.Equal(1f, PlumeRise.Survival(0f));
        Assert.True(PlumeRise.Survival(100f) < 0.1f);
        Assert.True(PlumeRise.Survival(300f) > 0f);
    }
}
