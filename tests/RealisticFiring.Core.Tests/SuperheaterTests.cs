using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

/// <summary>
/// The superheater's elements: superheat against the firing rate and the
/// steam drawn, their lag, the soak standing, and wet steam.
/// </summary>
public class SuperheaterTests
{
    private static SuperheaterSpec Spec() => new SuperheaterSpec
    {
        Kind = "Type A", FullSuperheat = 150f, HalfSuperheat = 110f, StandingSuperheat = 400f, TimeConstant = 60f,
    };

    private static Superheater Settled(float fire, float flow)
    {
        var superheater = new Superheater(Spec());
        superheater.Step(fire, flow, 1f, 0.02f);
        return superheater;
    }

    private static void Run(Superheater superheater, float fire, float flow, float seconds, float quality = 1f)
    {
        for (var t = 0f; t < seconds; t += 0.1f)
            superheater.Step(fire, flow, quality, 0.1f);
    }

    [Fact]
    public void WorkingSteadilyItGivesTheSpecsFullAndHalfSuperheat()
    {
        Assert.Equal(150f, Settled(1f, 1f).Superheat, 1);
        Assert.Equal(110f, Settled(0.5f, 0.5f).Superheat, 1);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(60)]
    [InlineData(144)]
    public void PulsedSteamWithTheSameAverageFlowCoolsLikeSteadySteam(int hz)
    {
        var steady = Settled(1f, 1f);
        var pulsed = Settled(1f, 1f);
        var dt = 1f / hz;
        for (var tick = 0; tick < 600 * hz; tick++)
        {
            steady.Step(1f, 1f, 1f, dt);
            pulsed.Step(1f, tick % 4 == 0 ? 4f : 0f, 1f, dt);
        }
        Assert.InRange(pulsed.Elements - steady.Elements, -1f, 1f);
    }

    [Fact]
    public void SteamFlowAboveTheRatedRateContinuesToCoolTheElements()
    {
        var heater = new Superheater(Spec());
        Assert.True(heater.Steady(1f, 4f) < heater.Steady(1f, 2f));
        Assert.True(heater.Steady(1f, 2f) < heater.Steady(1f, 1f));
    }

    [Fact]
    public void SuperheatRisesWithTheRate()
    {
        var rates = new[] { 0.1f, 0.25f, 0.5f, 0.75f, 1f };
        var superheats = rates.Select(r => Settled(r, r).Superheat).ToArray();

        for (var i = 1; i < superheats.Length; i++)
            Assert.True(superheats[i] > superheats[i - 1], string.Join(", ", superheats));
        Assert.InRange(superheats[0], 30f, 90f);
    }

    [Fact]
    public void AFireForcedPastTheSteamDrawnSuperheatsMore()
    {
        Assert.True(Settled(1f, 0.5f).Superheat > Settled(1f, 1f).Superheat + 30f);
        Assert.True(Settled(0.5f, 1f).Superheat < Settled(0.5f, 0.5f).Superheat);
    }

    [Fact]
    public void TheElementsFollowAChangeOverAboutTheTimeConstant()
    {
        var superheater = Settled(0.5f, 0.5f);
        Run(superheater, 1f, 1f, 60f);

        var share = (superheater.Superheat - 110f) / (150f - 110f);
        Assert.InRange(share, 0.55f, 0.7f);
        Run(superheater, 1f, 1f, 300f);
        Assert.Equal(150f, superheater.Superheat, 0);
    }

    [Fact]
    public void StandingWithTheThrottleShutTheElementsSoakTowardTheGas()
    {
        var superheater = Settled(1f, 1f);
        Run(superheater, 1f, 0f, 1200f);
        Assert.InRange(superheater.Elements, 390f, 400f);

        // The first steam through comes hot, then settles.
        Run(superheater, 1f, 1f, 10f);
        Assert.True(superheater.Superheat > 250f, $"{superheater.Superheat}");
        Run(superheater, 1f, 1f, 600f);
        Assert.Equal(150f, superheater.Superheat, 0);
    }

    [Fact]
    public void WithTheFireDeadTheElementsCoolToTheBoilersSteam()
    {
        var superheater = Settled(1f, 1f);
        Run(superheater, 0f, 0f, 3600f);

        Assert.True(superheater.Elements < 15f, $"{superheater.Elements}");
        Assert.Equal(0f, Settled(0f, 0f).Superheat, 3);
    }

    [Fact]
    public void WetSteamSpendsTheSuperheatDryingOut()
    {
        var superheater = Settled(1f, 1f);
        superheater.Step(1f, 1f, 0.95f, 0.02f);
        Assert.InRange(superheater.Superheat, 105f, 115f);

        // Priming: water through the header, no superheat left.
        superheater.Step(1f, 1f, 0.7f, 0.02f);
        Assert.Equal(0f, superheater.Superheat);
    }

    [Fact]
    public void ASaturatedEngineHasNone()
    {
        var superheater = new Superheater(SuperheaterSpec.None);
        superheater.Step(1f, 1f, 1f, 1f);

        Assert.Equal(0f, superheater.Superheat);
        Assert.Equal(0f, superheater.Steady(1f, 0f));
    }

    [Fact]
    public void ASpecThatCannotBalanceIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new Superheater(new SuperheaterSpec { FullSuperheat = 150f, HalfSuperheat = 110f, StandingSuperheat = 140f }));
        Assert.Throws<ArgumentException>(() => new Superheater(new SuperheaterSpec { FullSuperheat = 150f, HalfSuperheat = 160f, StandingSuperheat = 400f }));
    }
}
