using System.Linq;
using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

 public class FiringAdviceTests
{
    private static readonly FireboxParameters S282 = new FireboxParameters(150f, 120f, 0.5f);
    private static readonly InjectorReading[] Feeding = { new InjectorReading(InjectorState.Working, 0.6f, true) };

    // A lit bed: each zone's burning coal, and clinker where given.
    private static FireModel Bed(float[] coal, float[]? clinker = null)
    {
        var fire = new FireModel(S282);
        var data = new float[GrateLayout.ZoneCount * 4];
        for (var i = 0; i < GrateLayout.ZoneCount; i++)
        {
            data[i * 4 + 1] = coal[i];
            data[i * 4 + 3] = clinker?[i] ?? 0f;
        }
        Assert.True(fire.RestoreZones(true, data));
        return fire;
    }

    private static float[] Even(float kg) => Enumerable.Repeat(kg, GrateLayout.ZoneCount).ToArray();

    [Fact]
    public void ABoilerDamagedPastItsSafetyValveIsWarnedOf()
    {
        // Whole, it bursts at three times its lift: nothing to say.
        Assert.Null(FiringAdvice.Bursting(14f, 15f, 45f));
        var far = FiringAdvice.Bursting(9f, 15f, 12f);
        var near = FiringAdvice.Bursting(11f, 15f, 12f);
        Assert.Equal(AdviceLevel.Watch, far!.Value.Level);
        Assert.Equal(AdviceLevel.Act, near!.Value.Level);
        Assert.Contains("burst at 11.0 bar", near.Value.Text);
    }

    [Fact]
    public void AnEvenBedWithTheWaterInHandIsSteady()
    {
        var advice = FiringAdvice.ForCoal(Bed(Even(10f)), 0.1f, 0.6f, Feeding);
        Assert.Equal(AdviceLevel.Ok, advice.Level);
    }

    [Fact]
    public void AThinZoneIsNamedWithItsCoal()
    {
        var coal = Even(10f);
        coal[(int)GrateZone.FrontLeft] = 1.8f;
        var advice = FiringAdvice.ForCoal(Bed(coal), 0.1f, 0.6f, Feeding);
        Assert.Equal(AdviceLevel.Watch, advice.Level);
        Assert.Equal("Fire the front left: it's burning thin (1.8 kg).", advice.Text);
    }

    [Fact]
    public void AnEmptyGrateIsNotThin()
    {
        Assert.Equal(-1, FiringAdvice.ThinZone(Bed(Even(0f)).Zones));
    }

    [Fact]
    public void ClinkerAndAFullAshpanAskForBothAsTheFiremanWould()
    {
        var fire = Bed(Even(10f));
        var clinker = Even(0f);
        clinker[(int)GrateZone.Center] = 0.3f * fire.ReferenceZoneMass;
        fire = Bed(Even(10f), clinker);
        fire.RestoreAshpan(0.6f * fire.AshpanCapacity);
        Assert.True(FiringAdvice.WantsCleaning(fire));
        var advice = FiringAdvice.ForCoal(fire, 0.1f, 0.6f, Feeding);
        Assert.Contains("clean the fire and empty the pan", advice.Text);
    }

    [Fact]
    public void AHotInjectorComesBeforeTheFireInItsOwnValvesWords()
    {
        var coal = Even(10f);
        coal[0] = 1f;
        var withValve = FiringAdvice.ForCoal(Bed(coal), 0.1f, 0.6f, new[] { new InjectorReading(InjectorState.Hot, 0.6f, true) });
        var lever = FiringAdvice.ForCoal(Bed(coal), 0.1f, 0.6f, new[] { new InjectorReading(InjectorState.Hot, 0.2f, false) });
        Assert.Equal(AdviceLevel.Act, withValve.Level);
        Assert.Contains("hold the steam at Prime", withValve.Text);
        Assert.Contains("hold it cracked", lever.Text);
    }

    [Fact]
    public void NoWaterSaysWhichValveOrTheTender()
    {
        var shut = FiringAdvice.ForCoal(Bed(Even(10f)), 0f, 0.6f, new[] { new InjectorReading(InjectorState.NoWater, 0f, true) });
        var dry = FiringAdvice.ForCoal(Bed(Even(10f)), 0f, 0.6f, new[] { new InjectorReading(InjectorState.NoWater, 0.6f, true) });
        var tenderShut = FiringAdvice.ForCoal(Bed(Even(10f)), 0f, 0.6f, new[] { new InjectorReading(InjectorState.NoWater, 0.6f, true, tenderValveShut: true) });
        Assert.Contains("open the water valve", shut.Text);
        Assert.Contains("Tender's dry", dry.Text);
        Assert.Contains("open the tender water valve", tenderShut.Text);
    }

    [Fact]
    public void ALowGlassWantsTheInjector()
    {
        var advice = FiringAdvice.ForCoal(Bed(Even(10f)), 0.1f, 0.2f, new[] { new InjectorReading(InjectorState.Off, 0f, true) });
        Assert.Equal(AdviceLevel.Act, advice.Level);
        Assert.Contains("get the injector on", advice.Text);
    }

    [Fact]
    public void ADeadFireSaysSo()
    {
        var fire = new FireModel(S282);
        var advice = FiringAdvice.ForCoal(fire, 0f, 0.6f, Feeding);
        Assert.Contains("The fire's out", advice.Text);
    }

    private static OilReading Cold => new OilReading { BoilerPressure = 1f, OilTemperature = 20f, GlassLevel = 0.7f, ServiceTracks = "a service track" };

    [Fact]
    public void LightingAnOilFireGoesStepByStep()
    {
        var oil = Cold;
        Assert.Equal(1, FiringAdvice.ForOil(oil).LightingStep);
        oil.OnSteamLine = true;
        var warm = FiringAdvice.ForOil(oil);
        Assert.Equal(2, warm.LightingStep);
        Assert.StartsWith("Step 2 of 5, warm the oil", warm.Text);
        oil.OilTemperature = 50f;
        oil.Vapor = 0.2f;
        Assert.Equal(3, FiringAdvice.ForOil(oil).LightingStep);
        oil.Vapor = 0f;
        Assert.Equal(4, FiringAdvice.ForOil(oil).LightingStep);
        oil.BlowerOn = oil.AtomizerOpen = true;
        Assert.Equal(5, FiringAdvice.ForOil(oil).LightingStep);
        oil.Torch = 20f;
        Assert.StartsWith("Lit: crack the firing valve now", FiringAdvice.ForOil(oil).Text);

        var steps = FiringAdvice.Steps(oil);
        Assert.Equal(FiringAdvice.LightingSteps, steps.Count);
        Assert.True(steps.Take(4).All(s => s.Done));
        Assert.True(steps[4].Current);
    }

    [Fact]
    public void ADeadEngineOffTheServiceTrackIsSentToOne()
    {
        var oil = Cold;
        oil.ServiceTracks = "a loco parking track";
        Assert.Contains("get her onto a loco parking track", FiringAdvice.ForOil(oil).Text);
        oil.OnServiceTrack = true;
        Assert.Contains("open SHOP STEAM", FiringAdvice.ForOil(oil).Text);
        Assert.DoesNotContain("(or", FiringAdvice.ForOil(oil).Text);
        oil.SteamLineKey = "F11";
        Assert.Contains("open SHOP STEAM (or F11)", FiringAdvice.ForOil(oil).Text);
    }

    [Fact]
    public void ALitBurnerIsReadAtTheStack()
    {
        var oil = new OilReading { FireOn = true, BoilerPressure = 15f, GlassLevel = 0.6f, StackSmoke = 0.05f, Injectors = Feeding };
        Assert.Contains("too much air", FiringAdvice.ForOil(oil).Text);
        oil.StackSmoke = 0.7f;
        Assert.Equal(AdviceLevel.Act, FiringAdvice.ForOil(oil).Level);
        oil.StackSmoke = 0.15f;
        Assert.Equal(AdviceLevel.Ok, FiringAdvice.ForOil(oil).Level);
        oil.OnSteamLine = true;
        oil.SteamLineOpening = 1f;
        Assert.Contains("shut SHOP STEAM", FiringAdvice.ForOil(oil).Text);
    }
}
