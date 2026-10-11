using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

 public class AshpanTests
{
    private static readonly FireboxParameters S282 = new FireboxParameters(150f, 120f, 0.5f);
    private const float S282Grate = 2.12f;
    private const float Dt = 0.1f;

    private static FireModel Working(float kgPerHour, out FireInputs inputs, out float target)
    {
        var rate = kgPerHour / 3600f;
        target = rate * S282.BurnTime;
        var fire = new FireModel(S282) { GrateArea = S282Grate };
        fire.Restore(fireOn: true, coalMass: target);
        inputs = FireTestKit.Inputs(rate * FireModel.AirPerCoal);
        return fire;
    }

    private static void Run(FireModel fire, FireInputs inputs, float target, float seconds, float shakeEvery)
    {
        var shake = shakeEvery;
        for (var t = 0f; t < seconds; t += Dt)
        {
            while (fire.CoalMass + 1.5f <= target)
                fire.AddCoal(3f, fire.NeediestZone());
            if (shakeEvery > 0f && t >= shake)
            {
                fire.ShakeGrate();
                shake = t + shakeEvery;
            }
            fire.Step(Dt, inputs);
        }
    }

    [Fact]
    public void HardWorkFillsThePanAboutHalfInTwoHours()
    {
        var fire = Working(1175f, out var inputs, out var target);

        Run(fire, inputs, target, 2f * 3600f, shakeEvery: 900f);

        Assert.InRange(fire.AshpanFill, 0.38f, 0.62f);
    }

    [Fact]
    public void AFullPanChokesTheAirAndHeatsTheAshToClinker()
    {
        var empty = Working(1175f, out var inputs, out var target);
        var full = Working(1175f, out _, out _);
        full.RestoreAshpan(full.AshpanCapacity);

        Run(empty, inputs, target, 1800f, shakeEvery: 900f);
        Run(full, inputs, target, 1800f, shakeEvery: 900f);

        Assert.True(full.PrimaryAirFlow < 0.75f * empty.PrimaryAirFlow, $"air {full.PrimaryAirFlow:F2} full, {empty.PrimaryAirFlow:F2} empty");
        Assert.True(full.ClinkerMass > 1.2f * empty.ClinkerMass, $"clinker {full.ClinkerMass:F1} kg full, {empty.ClinkerMass:F1} kg empty");
    }

    [Fact]
    public void AFullPanTakesNoMoreAshSoShakingLeavesItOnTheGrate()
    {
        var fire = FireTestKit.BurningFire(S282, 40f);
        foreach (var zone in fire.Zones)
            zone.Ash = 1f;
        fire.RestoreAshpan(fire.AshpanCapacity);

        fire.ShakeGrate();

        Assert.Equal(9f, fire.AshMass, 3);
    }

    [Fact]
    public void ShakenAshGoesIntoThePan()
    {
        var fire = FireTestKit.BurningFire(S282, 40f);
        foreach (var zone in fire.Zones)
            zone.Ash = 1f;

        fire.ShakeGrate();

        Assert.True(fire.Ashpan > 5f, $"ashpan {fire.Ashpan:F2} kg");
        Assert.Equal(9f, fire.AshMass + fire.Ashpan - fire.CoalDroppedThroughGrate * FireTuning.Default.AshFraction, 2);
    }

    [Fact]
    public void TheHopperDoorsEmptyThePan()
    {
        var fire = FireTestKit.BurningFire(S282, 40f);
        fire.RestoreAshpan(150f);

        for (var t = 0f; t < 15f; t += Dt)
            fire.OpenAshpan(1f, Dt);

        Assert.True(fire.Ashpan < 1f, $"{fire.Ashpan:F2} kg left");
        Assert.True(fire.AshpanEmptied > 149f);
    }

    [Fact]
    public void DumpedAndDroppedFireEndsUpInThePan()
    {
        var fire = FireTestKit.BurningFire(S282, 40f);
        foreach (var zone in fire.Zones)
            zone.Clinker = 1f;

        for (var t = 0f; t < 5f; t += Dt)
            fire.OpenDropGrate(1f, Dt);
        var afterDrop = fire.Ashpan;
        for (var t = 0f; t < 30f; t += Dt)
            fire.Dump(1f, Dt);

        Assert.True(afterDrop > 2.5f, $"ashpan {afterDrop:F2} kg after the drop grate");
        Assert.True(fire.Ashpan > afterDrop + 5f, $"ashpan {fire.Ashpan:F2} kg after the dump");
    }

    [Fact]
    public void AFreshFireStartsWithAnEmptyPan()
    {
        var fire = FireTestKit.BurningFire(S282, 40f);
        fire.RestoreAshpan(100f);

        fire.Restore(fireOn: true, coalMass: 60f);

        Assert.Equal(0f, fire.Ashpan);
    }
}
