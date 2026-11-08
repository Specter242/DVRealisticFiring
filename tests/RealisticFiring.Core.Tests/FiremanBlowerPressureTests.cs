using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

public class FiremanBlowerPressureTests
{
    [Theory]
    [InlineData(0f)]
    [InlineData(1.56f)]
    public void AfterLightWorkHeStandsWithoutRepeatedSafetyLifts(float stokerFeed)
    {
        var engine = new Footplate(stokerFeed: stokerFeed)
        {
            EngineSteam = .2f * Footplate.FullWorkSteam,
        };
        engine.Run(600f);
        engine.EngineSteam = 0f;
        engine.Standing = engine.Braking = true;
        var before = engine.BlownOff;
        var wasLifting = false;
        var lifts = 0;
        engine.Run(1200f, e =>
        {
            var lifting = e.BlownOff > before;
            if (lifting && !wasLifting)
                lifts++;
            before = e.BlownOff;
            wasLifting = lifting;
        });

        Assert.True(lifts <= 1, $"the safety lifted {lifts} times during the stop");
        Assert.True(engine.Fire.FireOn);
        Assert.True(engine.Pressure >= engine.Fireman.PressureTarget - .5f);
    }

    [Theory]
    [InlineData(FiremanMode.KeepAlive, 14.75f)]
    [InlineData(FiremanMode.Shunting, 14.9f)]
    [InlineData(FiremanMode.Road, 14.75f)]
    [InlineData(FiremanMode.Road, 15.1f)]
    public void NearTheSafetyValveSmokeCannotOverrideTheQuietBlower(FiremanMode mode, float pressure)
    {
        var fire = SmokyFire();
        var man = new AutoFireman { Mode = mode };
        var a = man.Step(.1f, fire, Reading(pressure));

        Assert.True(man.WaitingOnSmoke);
        Assert.InRange(a.Blower, 0f, .1f);
        // Keep secondary air available rather than forcing the bed harder.
        Assert.True(a.Door > AutoFireman.DoorCrack);
        Assert.Equal(0f, a.Shovelful);
    }

    [Fact]
    public void AwayFromTheSafetyValveHeStillDrawsASmokyFire()
    {
        var man = new AutoFireman { Mode = FiremanMode.Road };
        var a = man.Step(.1f, SmokyFire(), Reading(14.2f));
        Assert.True(man.WaitingOnSmoke);
        Assert.True(a.Blower >= .3f);
        Assert.True(a.Door > AutoFireman.DoorCrack);
    }

    [Fact]
    public void ABoilerShortOfPressureStillGetsRaisingDraft()
    {
        var man = new AutoFireman { Mode = FiremanMode.Road };
        var a = man.Step(.1f, SmokyFire(), Reading(12f));
        Assert.Equal(1f, a.Blower);
    }

    private static FireModel SmokyFire()
    {
        var fire = FireTestKit.BurningFire(Footplate.S282, 40f);
        fire.AddCoal(10f, GrateZone.UnderDoor);
        FireTestKit.Run(fire, FireTestKit.Inputs(.2f), 30f);
        Assert.True(fire.SootDensity > AutoFireman.BlackSmoke);
        return fire;
    }

    private static FiremanReadings Reading(float pressure) => new()
    {
        Pressure = pressure, SafetyValve = 15f, Glass = .85f,
        Standing = true, Braking = true, TenderWater = 20000f,
        TenderCoal = 10000f, ShovelMass = 3f, FullWorkSteam = 6f,
    };
}
