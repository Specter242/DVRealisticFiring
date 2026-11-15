using RealisticFiring.Core;
using Xunit.Abstractions;

namespace RealisticFiring.Core.Tests;

/// <summary>
/// The setting that has the lighter light a cold coal bed as the stock game
/// does (kadengamesj, DV Discord, 2026-11-14), the wood and rag kept.
/// </summary>
public class LighterLightingTests
{
    private static readonly FireboxParameters S282 = StarterTimingReport.S282;
    private readonly ITestOutputHelper output;
    public LighterLightingTests(ITestOutputHelper output) => this.output = output;

    // A cold engine's box on its natural draft, the boiler at the air's warmth.
    private static FireInputs Cold()
    {
        var inputs = FireTestKit.Inputs(0.6f);
        inputs.BoilerTemperature = 20;
        return inputs;
    }

    // Coal shovelled onto a dead grate, across it as a fireman lays a fire.
    private static FireModel LaidCoal(float kg)
    {
        var fire = new FireModel(S282, FireTestKit.CleanCoal);
        foreach (var zone in new[] { GrateZone.UnderDoor, GrateZone.Center, GrateZone.Front })
            fire.AddCoal(kg / 3f, zone);
        return fire;
    }

    [Fact] public void OnlyWithTheSettingOnLaidDryCoalOnADeadGrateLights()
    {
        Assert.True(LighterLighting.LightsCoal(true, 9f, 0f, false));
        // Off, the default: the lighter lights only the rag.
        Assert.False(LighterLighting.LightsCoal(false, 9f, 0f, false));
        // Wood alone is lit by the rag, as before.
        Assert.False(LighterLighting.LightsCoal(true, 0f, 0f, false));
        // As wet as the stock firebox lights and no wetter.
        Assert.True(LighterLighting.LightsCoal(true, 9f, LighterLighting.WetGrate, false));
        Assert.False(LighterLighting.LightsCoal(true, 9f, 0.6f, false));
        Assert.False(LighterLighting.LightsCoal(true, 9f, float.NaN, false));
        // A burning fire is left as it is.
        Assert.False(LighterLighting.LightsCoal(true, 9f, 0f, true));
    }

    [Fact] public void AGuestPlaysByTheHostsSetting()
    {
        var rule = new SessionLighterRule();
        // Alone or hosting: the player's own.
        Assert.True(rule.LightsCoal(inSession: false, isClient: false, own: true));
        Assert.True(rule.LightsCoal(inSession: true, isClient: false, own: true));
        Assert.False(rule.LightsCoal(inSession: true, isClient: false, own: false));
        // A guest: the mod's default until the host's rule comes, then the host's.
        Assert.False(rule.LightsCoal(inSession: true, isClient: true, own: true));
        rule.Receive(true);
        Assert.True(rule.LightsCoal(inSession: true, isClient: true, own: false));
        rule.Receive(false);
        Assert.False(rule.LightsCoal(inSession: true, isClient: true, own: true));
        rule.Receive(true);
        rule.Reset();
        Assert.False(rule.Received);
        Assert.False(rule.LightsCoal(inSession: true, isClient: true, own: true));
    }

    [Fact] public void TheLighterLightsLaidCoalAndItBurnsOnItsOwn()
    {
        // The request lights the model as the stock firebox's sets its fire
        // on; the lighter's minute of flame catches the coal, which then
        // keeps itself going on a cold engine's natural draft.
        var fire = LaidCoal(9f);
        Assert.False(fire.FireOn);
        Assert.True(LighterLighting.LightsCoal(true, fire.CoalMass, 0f, fire.FireOn));
        fire.Ignite();
        FireTestKit.Run(fire, Cold(), 300f);
        output.WriteLine($"after 5 min: burning {fire.BurningMass:F2} kg of {fire.CoalMass:F2}, heat {fire.Heat / 1e3f:F0} kW");
        Assert.True(fire.FireOn);
        Assert.True(fire.BurningMass > 0.5f, $"burning {fire.BurningMass:F2} kg");
        Assert.True(fire.Heat > 0f);
    }

    [Fact] public void LaidCoalUnlitStaysOut()
    {
        var fire = LaidCoal(9f);
        FireTestKit.Run(fire, Cold(), 300f);
        Assert.False(fire.FireOn);
        Assert.Equal(0f, fire.BurningMass);
    }

    [Fact] public void CoalLitOverLaidWoodBurnsAndCatchesTheWood()
    {
        // Wood laid and coal over it, lit from the lighter: the coal burns,
        // and the wood catches from it.
        var fire = LaidCoal(9f);
        var wood = 4 * StarterFuel.PieceMass;
        fire.AddWood(wood, GrateZone.UnderDoor);
        Assert.True(LighterLighting.LightsCoal(true, fire.CoalMass, 0f, fire.FireOn));
        fire.Ignite();
        FireTestKit.Run(fire, Cold(), 300f);
        output.WriteLine($"after 5 min: coal burning {fire.BurningMass:F2} kg, wood {fire.StarterMass:F2} kg, alight {fire.StarterAlight}");
        Assert.True(fire.FireOn);
        Assert.True(fire.BurningMass > 0.5f, $"burning {fire.BurningMass:F2} kg");
        Assert.True(fire.StarterMass < 0.5f * wood, $"wood left {fire.StarterMass:F2} of {wood:F2} kg");
    }

    [Fact] public void TheAdviceNamesTheLighterOnlyWithTheSettingOn()
    {
        var injectors = new InjectorReading[0];
        var empty = new FireModel(S282);
        Assert.Contains("shovel in some coal and light it with the lighter", FiringAdvice.ForCoal(empty, 1, 1, injectors, lighterLightsCoal: true).Text);
        Assert.Contains("wood and a rag", FiringAdvice.ForCoal(empty, 1, 1, injectors).Text);

        var laid = LaidCoal(9f);
        Assert.Contains("Coal is laid: light it with the lighter", FiringAdvice.ForCoal(laid, 1, 1, injectors, lighterLightsCoal: true).Text);
        Assert.DoesNotContain("lighter", FiringAdvice.ForCoal(laid, 1, 1, injectors).Text);

        var wood = new FireModel(S282);
        wood.AddWood(4 * StarterFuel.PieceMass, GrateZone.UnderDoor);
        var withSetting = FiringAdvice.ForCoal(wood, 1, 1, injectors, lighterLightsCoal: true).Text;
        Assert.Contains("lay some coal over it", withSetting);
        Assert.Contains("oiled rag", withSetting);
        Assert.Contains("oil a rag", FiringAdvice.ForCoal(wood, 1, 1, injectors).Text);

        // A lit fire's advice is the same either way.
        var burning = FireTestKit.BurningFire(S282, 40f);
        Assert.Equal(FiringAdvice.ForCoal(burning, 0, 0.6f, injectors).Text,
            FiringAdvice.ForCoal(burning, 0, 0.6f, injectors, lighterLightsCoal: true).Text);
    }
}
