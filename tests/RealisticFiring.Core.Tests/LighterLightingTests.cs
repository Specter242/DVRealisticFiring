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

    [Fact] public void TheRagAdviceSaysWhetherTheRagCanLightTheWood()
    {
        var injectors = new InjectorReading[0];
        string After(System.Action<FireModel> lay)
        {
            var fire = new FireModel(S282);
            lay(fire);
            FireTestKit.Run(fire, Cold(), 2f);
            return FiringAdvice.ForCoal(fire, 1, 1, injectors).Text;
        }

        Assert.Contains("heating the wood", After(f => { Wood(f, GrateZone.Center, 4); f.AddRag(0.05f, true, GrateZone.Center); }));
        Assert.Contains("heating the wood", After(f => { Wood(f, GrateZone.Center, 4); f.AddRag(0.05f, true, GrateZone.Front); }));
        Assert.Contains("heating the wood", After(f => { f.AddCoalEvenly(10f); Wood(f, GrateZone.Center, 4); f.AddRag(0.05f, true, GrateZone.Center); }));
        Assert.Contains("no wood", After(f => f.AddRag(0.05f, true, GrateZone.Front)));
        Assert.Contains("smothers it", After(f => { Wood(f, GrateZone.Center, 4); f.AddCoal(5f, GrateZone.Center); f.AddRag(0.05f, true, GrateZone.Center); }));
    }

    private static void Wood(FireModel f, GrateZone zone, int pieces)
    {
        for (var i = 0; i < pieces; i++)
            f.AddWood(StarterFuel.PieceMass, f.StarterZone(zone));
    }

    [Theory]
    [InlineData(GrateZone.Center, GrateZone.Front)]
    [InlineData(GrateZone.BackLeft, GrateZone.FrontRight)]
    [InlineData(GrateZone.UnderDoor, GrateZone.UnderDoor)]
    public void WoodOnCoalCatchesFromARagAnywhere(GrateZone woodAt, GrateZone ragAt)
    {
        var fire = new FireModel(S282);
        fire.AddCoalEvenly(10f);
        Wood(fire, woodAt, 4);
        fire.AddRag(0.05f, true, ragAt);
        var caught = -1f;
        for (var t = 0f; t < 600f && caught < 0f; t += 1f)
        {
            FireTestKit.Run(fire, Cold(), 1f);
            for (var z = 0; z < GrateLayout.ZoneCount; z++)
                if (fire.Starters[z].Involved > 0f && caught < 0f) caught = t + 1f;
        }
        output.WriteLine($"wood at {woodAt}, rag at {ragAt}: caught at {caught:F0} s");
        Assert.InRange(caught, 1f, 90f);
        FireTestKit.Run(fire, Cold(), 300f);
        output.WriteLine($"after 5 min more: coal burning {fire.BurningMass:F2} kg");
        Assert.True(fire.BurningMass > 0.5f, $"the coal caught from the wood ({fire.BurningMass:F2} kg burning)");
    }

    // A small engine's natural draft (BorING's, 0.03 to 0.04 kg/s) starts the wood: the wood fire draws the whole
    // grate's air, not one zone's share. In 1.24.2 the flames never took the pile at 0.04 kg/s or less.
    [Theory]
    [InlineData(0.03f, 4)]
    [InlineData(0.04f, 8)]
    public void ASmallEnginesNaturalDraftStartsTheWood(float air, int pieces)
    {
        var fire = new FireModel(S282);
        Wood(fire, GrateZone.UnderDoor, pieces);
        fire.AddRag(RagFuel.OilCapacity, true, GrateZone.UnderDoor);
        var inputs = FireTestKit.Inputs(air);
        inputs.BoilerTemperature = 20;
        var involved = -1f;
        for (var t = 0f; t < 180f; t += 1f)
        {
            FireTestKit.Run(fire, inputs, 1f);
            foreach (var starter in fire.Starters)
                if (involved < 0f && starter.Flaming > 0.9f) involved = t + 1f;
        }
        fire.AddCoal(3f, GrateZone.UnderDoor);
        FireTestKit.Run(fire, inputs, 120f);
        output.WriteLine($"{air:F2} kg/s, {pieces} pieces: flames over the pile at {involved:F0} s, coal burning {fire.BurningMass:F2} kg");
        Assert.InRange(involved, 1f, 150f);
        Assert.True(fire.BurningMass > 1f, $"burning {fire.BurningMass:F2} kg");
    }

    // A big charge fired on top of young wood still smothers it.
    [Fact] public void ABigChargeOnYoungWoodSmothersIt()
    {
        var fire = new FireModel(S282);
        Wood(fire, GrateZone.Center, 4);
        fire.AddRag(0.05f, true, GrateZone.Center);
        FireTestKit.Run(fire, Cold(), 30f);
        fire.AddCoal(20f, GrateZone.Center);
        FireTestKit.Run(fire, Cold(), 600f);
        output.WriteLine($"coal burning {fire.BurningMass:F2} kg, wood left {fire.StarterMass:F2} kg");
        Assert.True(fire.BurningMass < 0.5f, $"burning {fire.BurningMass:F2} kg");
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
