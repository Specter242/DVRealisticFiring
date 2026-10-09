using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

 public class ClinkerTests
{
    // The S282: 150 kg, 120 s, its grate 1.05 by 2.02 m as the mod measures it.
    private static readonly FireboxParameters S282 = new FireboxParameters(150f, 120f, 0.5f);
    private const float S282Grate = 2.12f;
    private static readonly float ReferenceZoneMass = S282.MaxCoalCapacity / GrateLayout.ZoneCount * FireTuning.Default.ReferenceDepthFraction;
    private const float Dt = 0.1f;

    // Fires to hold the coal a steady rate needs, shaking on an interval (0
    // never); returns the hours
    // until the worst zone's clinker chokes it, or null within the limit.
    private static float? HoursToChoke(float kgPerHour, float shakeEvery, float hours, FireTuning tuning = null)
    {
        var fire = Fire(kgPerHour, tuning, out var inputs, out var target);
        var shake = shakeEvery;
        for (var t = 0f; t <= hours * 3600f; t += Dt)
        {
            while (fire.CoalMass + 1.5f <= target)
                fire.AddCoal(3f, fire.NeediestZone());
            if (shakeEvery > 0f && t >= shake)
            {
                fire.ShakeGrate();
                shake = t + shakeEvery;
            }
            fire.Step(Dt, inputs);
            if (fire.Zones.Max(z => z.Clinker) >= FireTuning.Default.ClinkerChokeShare * ReferenceZoneMass)
                return t / 3600f;
        }
        return null;
    }

    private static FireModel Fire(float kgPerHour, FireTuning tuning, out FireInputs inputs, out float target)
    {
        var rate = kgPerHour / 3600f;
        target = rate * S282.BurnTime;
        var fire = new FireModel(S282, tuning) { GrateArea = S282Grate };
        fire.Restore(fireOn: true, coalMass: target);
        inputs = FireTestKit.Inputs(rate * FireModel.AirPerCoal);
        return fire;
    }

    [Fact]
    public void HardWorkShakenEveryQuarterHourClinkersInAboutTwoHours()
    {
        var hours = HoursToChoke(1175f, shakeEvery: 900f, hours: 4f);

        Assert.NotNull(hours);
        Assert.InRange(hours.Value, 1.5f, 2.6f);
    }

    [Fact]
    public void NeverShakingLetsAThickAshBedClinkerTheFireInAboutAnHour()
    {
        var neglected = HoursToChoke(1175f, shakeEvery: 0f, hours: 3f);
        var shaken = HoursToChoke(1175f, shakeEvery: 900f, hours: 4f);

        Assert.NotNull(neglected);
        Assert.InRange(neglected.Value, 0.7f, 1.4f);
        Assert.True(neglected < 0.7f * shaken, $"neglected {neglected:F1} h, shaken {shaken:F1} h");
    }

    [Fact]
    public void ALightFireHardlyClinkers()
    {
        Assert.Null(HoursToChoke(500f, shakeEvery: 900f, hours: 4f));
    }

    [Fact]
    public void StirringAZoneWithTheIronsClinkersIt()
    {
        // "Raising some of the ash into the hot zone of the fire, where it
        // melts" (ICS 1920 p.10): a zone raked every two minutes clinkers more.
        float CenterClinker(bool raked)
        {
            var fire = Fire(1175f, null, out var inputs, out var target);
            float shake = 900f, rake = 120f;
            for (var t = 0f; t <= 3600f; t += Dt)
            {
                while (fire.CoalMass + 1.5f <= target)
                    fire.AddCoal(3f, fire.NeediestZone());
                if (t >= shake)
                {
                    fire.ShakeGrate();
                    shake = t + 900f;
                }
                if (raked && t >= rake)
                {
                    fire.Rake(GrateZone.Center);
                    rake = t + 120f;
                }
                fire.Step(Dt, inputs);
            }
            return fire.Zones[(int)GrateZone.Center].Clinker;
        }

        var left = CenterClinker(raked: false);
        var stirred = CenterClinker(raked: true);
        Assert.True(stirred > 1.3f * left, $"center clinker {stirred:F2} kg raked, {left:F2} kg left alone");
    }

    [Fact]
    public void CleanCoalNeverClinkers()
    {
        Assert.Null(HoursToChoke(1175f, shakeEvery: 0f, hours: 3f, tuning: FireTestKit.CleanCoal));
    }

    [Fact]
    public void AWiggleTakesLessAshAndFireThanAShakeAndNoClinker()
    {
        FireModel Dirty()
        {
            var fire = FireTestKit.BurningFire(S282, 40f);
            foreach (var zone in fire.Zones)
            {
                zone.Ash = 1f;
                zone.Clinker = 1f;
            }
            return fire;
        }
        var wiggled = Dirty();
        var shaken = Dirty();

        wiggled.ShakeGrate(ShakeStroke.Wiggle);
        shaken.ShakeGrate(ShakeStroke.Shake);

        Assert.True(wiggled.AshMass > shaken.AshMass + 1f, $"ash {wiggled.AshMass} wiggled, {shaken.AshMass} shaken");
        Assert.True(wiggled.AshMass < 9f);
        Assert.Equal(9f, wiggled.ClinkerMass, 3);
        Assert.True(shaken.ClinkerMass < 9f);
        Assert.True(wiggled.CoalDroppedThroughGrate < 0.5f * shaken.CoalDroppedThroughGrate);
    }

    [Fact]
    public void ShakingWhileWorkingHardLosesMoreFireAndThrowsCinders()
    {
        FireModel Drawn(float kgPerHour)
        {
            var fire = Fire(kgPerHour, null, out var inputs, out _);
            FireTestKit.Run(fire, inputs, 5f);
            return fire;
        }
        var working = Drawn(1300f);
        var easy = Drawn(200f);
        var workingBurning = working.Zones.Sum(z => z.Burning);
        var easyBurning = easy.Zones.Sum(z => z.Burning);

        working.ShakeGrate();
        easy.ShakeGrate();

        var workingLoss = (working.CoalDroppedThroughGrate + working.ShakeCinders) / workingBurning;
        var easyLoss = (easy.CoalDroppedThroughGrate + easy.ShakeCinders) / easyBurning;
        Assert.True(workingLoss > 2f * easyLoss, $"lost {workingLoss:P1} working, {easyLoss:P1} easy");
        Assert.True(working.ShakeCinders > 0f);
        Assert.True(easy.ShakeCinders < 0.1f * working.ShakeCinders);
    }
}
