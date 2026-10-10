using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

 public class FireboxSmokeTests
{
    private static readonly FireboxParameters S282 = new FireboxParameters(150f, 120f, 0.5f);
    private const float Dt = 0.1f;

    private static (FireModel Fire, FireboxSmoke Smoke, FireInputs Inputs) Idling()
    {
        var fire = FireTestKit.BurningFire(S282, 40f, FireTestKit.CleanCoal);
        var inputs = FireTestKit.Inputs(0.4f);
        var smoke = new FireboxSmoke();
        for (var t = 0f; t < 300f; t += Dt)
        {
            fire.Step(Dt, inputs);
            smoke.Step(Dt, fire);
        }
        return (fire, smoke, inputs);
    }

    private static void Run(FireModel fire, FireboxSmoke smoke, FireInputs inputs, float seconds)
    {
        for (var t = 0f; t < seconds; t += Dt)
        {
            fire.Step(Dt, inputs);
            smoke.Step(Dt, fire);
        }
    }

    [Fact]
    public void AShovelfulSmokesTheBoxOutUntilItCatches()
    {
        var (fire, smoke, inputs) = Idling();
        var before = smoke.TotalSmokeOutRate;

        fire.AddCoal(3f, GrateZone.Center);
        Run(fire, smoke, inputs, 0.5f);
        var landed = 0f;
        for (var i = 0; i < GrateLayout.ZoneCount; i++)
            landed += smoke.TakeLanded((GrateZone)i);
        var smokingOut = smoke.TotalSmokeOutRate;

        Assert.Equal(0f, before);
        Assert.True(landed > 2.5f, $"{landed:F2} kg landed, spills included");
        Assert.True(smokingOut > 15f, $"smoke-out {smokingOut:F1} puffs/s");
        Assert.Equal(0f, smoke.TakeLanded(GrateZone.Center));

        Run(fire, smoke, inputs, 30f);
        Assert.True(smoke.TotalSmokeOutRate < 0.05f * smokingOut, $"smoke-out {smoke.TotalSmokeOutRate:F2} puffs/s after 30 s");
    }

    [Fact]
    public void AStokersTrickleDoesNotSmokeOut()
    {
        var (fire, smoke, inputs) = Idling();
        for (var t = 0f; t < 20f; t += Dt)
        {
            fire.AddCoalEvenly(0.02f);
            fire.Step(Dt, inputs);
            smoke.Step(Dt, fire);
        }
        Assert.Equal(0f, smoke.TotalCharge);
    }

    [Fact]
    public void ADeadFireMakesNoSmoke()
    {
        var (fire, smoke, inputs) = Idling();
        fire.AddCoal(3f, GrateZone.Center);
        Run(fire, smoke, inputs, 0.5f);

        fire.Extinguish();
        Run(fire, smoke, inputs, 0.1f);

        Assert.Equal(0f, smoke.TotalGasRate + smoke.TotalSmokeOutRate + smoke.TotalCharge);
    }

    [Fact]
    public void TheStacksIdleColumnFillsAndDarkensWithTheFireboxSmoke()
    {
        var tuning = new PlumeTuning();
        var clean = new PlumeInputs { FireOn = true };
        var gassy = new PlumeInputs { FireOn = true, FireboxGas = 30f };
        var smokedOut = new PlumeInputs { FireOn = true, FireboxGas = 30f, FireboxSmokeOut = 25f };

        var cleanHaze = PlumeModel.Between(tuning, clean);
        var gassyHaze = PlumeModel.Between(tuning, gassy);
        var smokedHaze = PlumeModel.Between(tuning, smokedOut);

        Assert.True(gassyHaze.Rate > 2f * cleanHaze.Rate, $"{cleanHaze.Rate:F1} -> {gassyHaze.Rate:F1} puffs/s");
        Assert.True(smokedHaze.Rate > gassyHaze.Rate);
        Assert.True(Brightness(smokedHaze.Color) < Brightness(gassyHaze.Color) - 0.15f,
            $"brightness {Brightness(gassyHaze.Color):F2} gassy, {Brightness(smokedHaze.Color):F2} smoked out");
        Assert.True(PlumeModel.ForBeat(tuning, smokedOut with { ExhaustPressure = 4f }).SootParticles > 0,
            "a beat through a smoked-out box carries its soot");
    }

    private static float Brightness(PlumeColor c) => (c.R + c.G + c.B) / 3f;
}
