using RealisticFiring.Core;
using Xunit.Abstractions;

namespace RealisticFiring.Core.Tests;

 public class OverFireJetTests
{
    private readonly ITestOutputHelper output;
    public OverFireJetTests(ITestOutputHelper output) => this.output = output;

    private static readonly FireboxParameters S282 = new FireboxParameters(150f, 120f, 0.5f, 10f, 5f, 15f);
    private static OverFireJets Six => new OverFireJets(6, JetTube.Large);
    private const float Boiler = 14.8f; // 200 psi gauge, absolute bar

    [Fact]
    public void AJetBlowsTheMeasuredSteamAndAir()
    {
        const float Lb = 0.45359237f / 3600f;
        var (steam, air) = OverFireJets.PerJet(JetTube.Large, 200f / 14.5038f);
        Assert.Equal(194f * Lb, steam, 4);
        Assert.Equal(2175f * Lb, air, 4);
        (steam, air) = OverFireJets.PerJet(JetTube.Small, 150f / 14.5038f);
        Assert.Equal(103f * Lb, steam, 4);
        Assert.Equal(1045f * Lb, air, 4);

        var (six, sixAir) = Six.Flow(1f, 200f / 14.5038f);
        Assert.Equal(6f * 194f * Lb, six, 4);
        Assert.Equal(6f * 2175f * Lb, sixAir, 4);
    }

    [Fact]
    public void AColdBoilerOrAShutValveBlowsNothing()
    {
        Assert.Equal((0f, 0f), Six.Flow(1f, 0f));
        Assert.Equal((0f, 0f), Six.Flow(0f, 13f));
        var fire = FireTestKit.BurningFire(S282, 60f);
        fire.Jets = Six;
        var inputs = FireTestKit.Inputs(5f);
        inputs.JetOpening = 1f;
        inputs.BoilerPressure = 1f;
        FireTestKit.Run(fire, inputs, 2f);
        Assert.Equal(0f, fire.JetAirFlow);
        Assert.Equal(0f, fire.JetSteamFlow);
    }

    [Fact]
    public void TheJetsTakeDraftFromTheGrateAndBlowBackWithNoneToSpare()
    {
        (float Primary, float Blowback) Air(float draft, float open)
        {
            var fire = FireTestKit.BurningFire(S282, 60f, FireTestKit.CleanCoal);
            fire.Jets = Six;
            var inputs = FireTestKit.Inputs(draft);
            inputs.JetOpening = open;
            inputs.BoilerPressure = Boiler;
            FireTestKit.Run(fire, inputs, 2f);
            return (fire.PrimaryAirFlow, fire.Blowback);
        }

        var working = Air(8f, 0f);
        var withJets = Air(8f, 1f);
        Assert.True(withJets.Primary < working.Primary && withJets.Primary > 0.7f * working.Primary,
            $"grate air {working.Primary:F2} kg/s, {withJets.Primary:F2} with the jets");
        Assert.Equal(0f, withJets.Blowback);

        // Standing with the blower shut: the natural draft's air alone.
        var still = Air(0.6f, 1f);
        Assert.Equal(0f, still.Primary);
        Assert.True(still.Blowback > 0.5f, $"blowback {still.Blowback:F2} kg/s");
    }

    private (float Soot, float Heat, float Penetration, float SteamShare) Round(float load, float round, float open, float? draft = null)
    {
        var tended = new TendedFire(S282, load, tuning: FireTestKit.CleanCoal);
        tended.Fire.Character = new GrateCharacter { OverFireMixing = 0.95f };
        tended.Fire.Jets = Six;
        tended.Inputs.BoilerPressure = Boiler;
        tended.Inputs.JetOpening = open;
        if (draft.HasValue)
            tended.Inputs.AirFlow = draft.Value;
        tended.Run(30f);
        tended.Fire.AddCoalEvenly(round * S282.MaxCoalCapacity);
        float soot = 0f, heat = 0f, penetration = 0f, steam = 0f;
        const int Seconds = 120;
        for (var i = 0; i < Seconds; i++)
        {
            tended.Run(1f);
            soot += tended.Fire.SootDensity;
            heat += tended.Fire.Heat;
            penetration += tended.Fire.JetPenetration;
            steam += tended.Fire.JetSteamFlow;
        }
        var made = heat / 2.3e6f;
        return (soot, heat / Seconds, penetration / Seconds, made > 0f ? steam / made : 0f);
    }

    [Fact]
    public void RunningTheJetsBurnAboutHalfARoundsSmoke()
    {
        // MMA 1913: running smoke 20 to 7.5 per cent; Collins: 50 to 15 at 200 psi.
        var without = Round(0.5f, 0.25f, 0f);
        var with = Round(0.5f, 0.25f, 1f);
        output.WriteLine($"soot-seconds {without.Soot:F1} without, {with.Soot:F1} with; penetration {with.Penetration:F2}");
        Assert.True(without.Soot > 5f, $"the round should smoke: {without.Soot:F1}");
        Assert.InRange(with.Soot / without.Soot, 0.25f, 0.6f);
    }

    [Fact]
    public void StandingWithTheBlowerOnTheJetsClearARoundsSmoke()
    {
        // Standing, Ringelmann 4 to 0 in about 9 s (Steins, in Benton and Engdahl p.44).
        var without = Round(0.1f, 0.25f, 0f, draft: 3f);
        var with = Round(0.1f, 0.25f, 1f, draft: 3f);
        output.WriteLine($"soot-seconds {without.Soot:F1} without, {with.Soot:F1} with; penetration {with.Penetration:F2}; jets' steam {with.SteamShare:P1} of the steam made");
        Assert.True(without.Soot > 5f, $"the round should smoke: {without.Soot:F1}");
        Assert.True(with.Soot < 0.25f * without.Soot, $"soot-seconds {without.Soot:F1} without, {with.Soot:F1} with");
        Assert.True(with.Penetration > 0.95f);
    }

    [Fact]
    public void TheShareTheJetsBurnIsWhatTheyAdd()
    {
        // Standing after a round with the blower on: shut, they burn none of
        // the gas; open, a large share of it, and the burnout with them is the
        // burnout without plus that share.
        float Burnout(float open, out float jets)
        {
            var tended = new TendedFire(S282, 0.1f, tuning: FireTestKit.CleanCoal);
            tended.Fire.Character = new GrateCharacter { OverFireMixing = 0.95f };
            tended.Fire.Jets = Six;
            tended.Inputs.BoilerPressure = Boiler;
            tended.Inputs.JetOpening = open;
            tended.Inputs.AirFlow = 3f;
            tended.Run(30f);
            tended.Fire.AddCoalEvenly(0.25f * S282.MaxCoalCapacity);
            tended.Run(10f);
            jets = tended.Fire.JetBurnout;
            return tended.Fire.OverFireBurnout;
        }

        var shut = Burnout(0f, out var none);
        var open = Burnout(1f, out var share);
        output.WriteLine($"burnout {shut:P0} shut, {open:P0} open; the jets' share {share:P0}");
        Assert.Equal(0f, none);
        Assert.InRange(share, 0.2f, 1f);
        Assert.True(open > shut);
        Assert.InRange(share, 0f, open);
    }

    [Fact]
    public void WorkedHardTheyReachPartWayAndCostLittleSteam()
    {
        // Benton and Engdahl p.42: jets for 15 per cent over-fire air take about
        // 2.5 per cent of the boiler's output at full output, 9 at a quarter.
        var full = Round(1f, 0.1f, 1f);
        var fullWithout = Round(1f, 0.1f, 0f);
        var quarter = Round(0.25f, 0.1f, 1f);
        output.WriteLine($"full work: penetration {full.Penetration:F2}, steam {full.SteamShare:P1}, heat {full.Heat / 1e6f:F2} against {fullWithout.Heat / 1e6f:F2} MW; a quarter: steam {quarter.SteamShare:P1}");
        Assert.InRange(full.Penetration, 0.4f, 0.75f);
        Assert.InRange(full.SteamShare, 0.01f, 0.03f);
        Assert.InRange(quarter.SteamShare, 0.03f, 0.08f);
        Assert.InRange(full.Heat / fullWithout.Heat, 0.97f, 1.01f);
    }
}
