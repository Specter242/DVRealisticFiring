using RealisticStackEffects.Core;
using Xunit;

namespace RealisticStackEffects.Core.Tests;

 public class VentSteamTests
{
    private static readonly PlumeAir Frosty = new PlumeAir(-5f, 0.9f);
    private static readonly PlumeAir Mild = new PlumeAir(10f, 0.8f);
    private static readonly PlumeAir HotDry = new PlumeAir(32f, 0.25f);

    private static SteamCloud Cloud(VentKind kind, PlumeAir air) => SteamCloud.For(air, 0f, VentSpec.For(kind).Wetness);

    [Fact]
    public void AJetLeavesAtItsSpeedAndSlowsAsOneOverItsDistance()
    {
        var spec = VentSpec.For(VentKind.SafetyValve);
        Assert.Equal(spec.Speed, VentSteam.SpeedAt(0f, spec.Speed, spec.Bore), 1);
        var near = VentSteam.Travel(0.5f, spec.Speed, spec.Bore);
        var far = VentSteam.Travel(2f, spec.Speed, spec.Bore);
        var start = VentSteam.JetStrength(spec.Speed, spec.Bore) / spec.Speed;
        // Speed × (distance + the start's) is the jet's strength all the way out.
        Assert.Equal(VentSteam.JetStrength(spec.Speed, spec.Bore), VentSteam.SpeedAt(0.5f, spec.Speed, spec.Bore) * (near + start), 1);
        Assert.Equal(VentSteam.JetStrength(spec.Speed, spec.Bore), VentSteam.SpeedAt(2f, spec.Speed, spec.Bore) * (far + start), 1);
    }

    [Fact]
    public void ASafetyValveShootsManyMetersAndADynamoAboutTwo()
    {
        var safety = VentSpec.For(VentKind.SafetyValve);
        var dynamo = VentSpec.For(VentKind.Dynamo);
        Assert.InRange(VentSteam.Travel(1f, safety.Speed, safety.Bore), 12f, 30f);
        Assert.InRange(VentSteam.Travel(1f, dynamo.Speed, dynamo.Bore), 1.5f, 4f);
    }

    [Fact]
    public void AParcelGrowsWithTheAirItTakesInAndKeepsItsMass()
    {
        var small = VentSteam.Size(0.001f, 10f);
        var big = VentSteam.Size(0.001f, 87f);
        // Eight times the mixture, twice the width.
        Assert.Equal(2f, big / small, 2);
    }

    [Fact]
    public void TheSameSteamLastsLongerTheColderAndDamperTheAir()
    {
        foreach (var kind in new[] { VentKind.Dynamo, VentKind.CylinderCock, VentKind.SafetyValve, VentKind.Whistle })
        {
            var spec = VentSpec.For(kind);
            var frosty = VentSteam.Lasting(spec, Cloud(kind, Frosty), 1f);
            var mild = VentSteam.Lasting(spec, Cloud(kind, Mild), 1f);
            var hot = VentSteam.Lasting(spec, Cloud(kind, HotDry), 1f);
            Assert.True(frosty > mild, $"{kind}: frosty {frosty} s, mild {mild} s");
            Assert.True(mild > hot, $"{kind}: mild {mild} s, hot {hot} s");
        }
    }

    [Fact]
    public void ADynamoShowsAShortStreamOnAMildDayAndALongOneInFrost()
    {
        var spec = VentSpec.For(VentKind.Dynamo);
        Assert.InRange(VentSteam.Lasting(spec, Cloud(VentKind.Dynamo, Mild), 1f), 0.4f, 6f);
        Assert.InRange(VentSteam.Lasting(spec, Cloud(VentKind.Dynamo, Frosty), 1f), 1.5f, 30f);
    }

    [Fact]
    public void AWeepingDrainShowsOnlyInColdDampAir()
    {
        var spec = VentSpec.For(VentKind.Weep);
        Assert.True(VentSteam.Lasting(spec, Cloud(VentKind.Weep, Frosty), 0.5f) > 3f);
        Assert.True(VentSteam.Lasting(spec, Cloud(VentKind.Weep, HotDry), 0.5f) < 1f);
    }

    [Fact]
    public void ASafetyValveIsSolidWhiteAndALeakAThinWisp()
    {
        float Thickest(VentKind kind)
        {
            var spec = VentSpec.For(kind);
            var cloud = Cloud(kind, Mild);
            var mass = spec.Flow / spec.Rate;
            var best = 0f;
            for (var age = 0.02f; age < 5f; age += 0.02f)
            {
                var dilution = VentSteam.JetDilution(VentSteam.Travel(age, spec.Speed, spec.Bore), spec.Bore);
                best = System.Math.Max(best, VentSteam.Opacity(mass, cloud.Liquid(dilution), VentSteam.Size(mass, dilution)));
            }
            return best;
        }
        Assert.True(Thickest(VentKind.SafetyValve) > 0.7f);
        Assert.InRange(Thickest(VentKind.Leak), 0.02f, 0.6f);
    }

    [Fact]
    public void AReliefsJetIsClearAtTheValveAndWhiteAMeterOrTwoOut()
    {
        var spec = VentSpec.For(VentKind.SafetyValve);
        Assert.Equal(0f, VentSteam.Formed(0.5f, spec.Bore, spec.Clear));
        Assert.InRange(VentSteam.Formed(1.2f, spec.Bore, spec.Clear), 0.3f, 0.9f);
        Assert.Equal(1f, VentSteam.Formed(2f, spec.Bore, spec.Clear));
        // Wet steam and spent exhaust show from the pipe.
        Assert.Equal(1f, VentSteam.Formed(0f, 0.022f, VentSpec.For(VentKind.CylinderCock).Clear));
        Assert.Equal(1f, VentSteam.Formed(0f, 0.032f, VentSpec.For(VentKind.Dynamo).Clear));
    }

    [Fact]
    public void AColdEnginesBoilerShowsNoPressure()
    {
        Assert.Equal(0f, VentSteam.Gauge(1.0f));
        Assert.Equal(0f, VentSteam.Gauge(VentSteam.Atmosphere));
        Assert.Equal(0f, VentSteam.Gauge(float.NaN));
        Assert.Equal(10f, VentSteam.Gauge(11.01325f), 3);
    }

    [Fact]
    public void WarmSteamRisesFasterThanSteamMixedCool()
    {
        var fresh = VentSteam.Rise(VentSteam.Excess(10f, 20f, 0.5f), 10f, 0.3f);
        var spent = VentSteam.Rise(VentSteam.Excess(10f, 2000f, 0f), 10f, 1.5f);
        Assert.True(fresh > spent);
        Assert.InRange(fresh, 0.2f, 2f);
    }

    [Fact]
    public void NothingIsNegativeOrNotANumberAtTheEdges()
    {
        Assert.Equal(0f, VentSteam.Travel(1f, 0f, 0.02f));
        Assert.Equal(0f, VentSteam.Opacity(0f, 1f, 1f));
        Assert.Equal(0f, VentSteam.Opacity(1f, 0f, 1f));
        Assert.False(float.IsNaN(VentSteam.Size(0f, 0f)));
        Assert.False(float.IsNaN(VentSteam.AmbientMixing(0f, 0f, 0.1f)));
        Assert.False(float.IsNaN(VentSteam.Rise(0f, -40f, 0f)));
    }
}
