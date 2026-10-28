using RealisticStackEffects.Core;

namespace RealisticStackEffects.Core.Tests;

 public class PlumeRiseTests
{
    [Theory]
    [InlineData(30f)]
    [InlineData(60f)]
    [InlineData(100f)]
    public void MovingTrainFinishesItsPuffsWithinTenMetres(float kilometresPerHour)
    {
        var speed = kilometresPerHour / 3.6f;
        var scale = PlumeRise.PuffAgeScale(speed, 8f, 0.45f, 10f);
        Assert.InRange(speed * 8f * 0.45f * scale, 0f, 10.001f);
        Assert.InRange(scale, 0f, 1f);
    }

    [Fact]
    public void StationaryAndWalkingSpeedKeepTheAcceptedPuffTiming()
    {
        Assert.Equal(1f, PlumeRise.PuffAgeScale(0f, 8f, 0.45f, 10f));
        Assert.Equal(1f, PlumeRise.PuffAgeScale(1f, 8f, 0.45f, 10f));
        Assert.Equal(1f, PlumeRise.PuffAgeScale(20f, 8f, 0.45f, 0f));
    }

    [Fact]
    public void HigherSpeedShortensTheHandoffWithoutExtendingTheLightBeat()
    {
        var slow = PlumeRise.PuffAgeScale(10f, 8f, 0.45f, 10f);
        var fast = PlumeRise.PuffAgeScale(20f, 8f, 0.45f, 10f);
        Assert.True(fast < slow);
        Assert.InRange(20f * 8f * 0.3f * fast, 0f, 10f);
    }

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
    public void ShowingTheStillAirRiseSlowerLeavesTheBentPlume()
    {
        var flux = Flux(Working);
        Assert.Equal(0.7f * PlumeRise.RiseSpeed(flux, 10f, 0f), PlumeRise.RiseSpeed(flux, 10f, 0f, 0.7f), 4);
        // At speed the bent-over plume is the slower, its trajectory the air's and its rise's.
        Assert.Equal(PlumeRise.RiseSpeed(flux, 10f, 20f), PlumeRise.RiseSpeed(flux, 10f, 20f, 0.7f), 4);
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
        Assert.Equal(0.3f, PlumeRise.Wake(0f), 3);
        Assert.True(PlumeRise.Wake(3f) < 0.05f, $"{PlumeRise.Wake(3f):F2} of the train's speed 3 m up");
        Assert.True(PlumeRise.Wake(20f) < 0.001f);
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
        var mild = SteamCloud.For(new PlumeAir(10f, 0.8f), SteamCloud.ReferenceFlueGas);
        var raw = SteamCloud.For(new PlumeAir(1f, 0.95f), SteamCloud.ReferenceFlueGas);
        // A minute on, 300 m up: the smoke still shows, the steam is gone.
        Assert.True(PlumeRise.Opacity(black, mild, 60f, 300f, 5f) > 0.08f);
        Assert.True(PlumeRise.Opacity(steam, mild, 60f, 300f, 5f) < PlumeRise.Invisible);
        // At the lip the smoke shows as it left; the steam's white forms a
        // little way up, and shows as it left once formed.
        Assert.Equal(0.8f, PlumeRise.Opacity(black, mild, 0f, 0f, 5f), 2);
        Assert.True(PlumeRise.Opacity(steam, mild, 0f, 0f, 5f) < 0.1f);
        Assert.InRange(PlumeRise.Opacity(steam, mild, 1f, 2f, 5f), 0.55f, 0.7f);
        // Cold, damp air keeps the steam longer.
        Assert.True(PlumeRise.Opacity(steam, raw, 10f, 20f, 5f) > PlumeRise.Opacity(steam, mild, 10f, 20f, 5f) + 0.1f);
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
    public void ATrailStaysSpreadWhenTheTrainStops()
    {
        // loady740 (Nexus, 2026-10-27): a line of puffs hung where the train had run.
        var running = 0f;
        for (var turn = 0; turn < 1200; turn++)
            running = PlumeRise.SpreadOn(running, 0.025f, 20f);
        Assert.Equal(PlumeRise.SpreadWidth(30f, 20f), running, 1);
        var stopped = PlumeRise.SpreadOn(running, 10f, 0f);
        Assert.True(stopped > running);
        float Shown(float spread) => PlumeRise.OpacityOf(PlumeRise.Parcel(0.8f, 1f).SmokeDepth, 5f, spread);
        Assert.True(Shown(stopped) < Shown(running));
        // Worked out from its age at the air's speed now, the stopped trail drew back in over three times as thick.
        Assert.True(Shown(PlumeRise.SpreadWidth(40f, 0f)) > 3f * Shown(stopped));
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
