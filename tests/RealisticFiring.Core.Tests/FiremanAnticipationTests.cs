using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

public class FiremanAnticipationTests
{
    private static FiremanReadings Reading(float pressure, bool forecast, bool stoker = false) => new FiremanReadings
    {
        Pressure = pressure, SafetyValve = 16f, Glass = .55f, TenderWater = 20000f, TenderCoal = 10000f,
        Work = .2f, FullWorkSteam = 6f, Throttle = .2f, Cutoff = .25f,
        RoadKnown = true, WorkSoon = forecast ? 1f : .2f, WorkAhead = forecast ? 1f : .2f,
        ShovelMass = 3f, HasStoker = stoker, StokerFullFeed = 1.56f,
    };

    [Theory]
    [InlineData(15.95f, false)]
    [InlineData(16.1f, false)]
    [InlineData(15.95f, true)]
    public void AClimbCannotDemandExtraFuelAtTheSafetyValve(float pressure, bool stoker)
    {
        (float Target, float Steam, float Feed, float Thrown) Run(bool forecast)
        {
            var fire = FireTestKit.BurningFire(Footplate.S282, 40f, FireTestKit.CleanCoal);
            var man = new AutoFireman { Mode = FiremanMode.Road };
            var thrown = 0f;
            var action = default(FiremanActions);
            for (var t = 0f; t < 120f; t += .1f)
            {
                action = man.Step(.1f, fire, Reading(pressure, forecast, stoker));
                thrown += action.Shovelful;
                if (action.Shovelful > 0f) fire.AddCoal(action.Shovelful, action.Aim);
            }
            return (man.CoalTarget, man.PlannedSteam, action.StokerThrottle, thrown);
        }

        var present = Run(false);
        var ahead = Run(true);
        Assert.Equal(present.Target, ahead.Target, 4);
        Assert.Equal(present.Steam, ahead.Steam, 4);
        Assert.Equal(present.Feed, ahead.Feed, 4);
        Assert.Equal(0f, ahead.Thrown);
    }

    [Fact]
    public void HeStopsAPendingRoundWhenThePressureRises()
    {
        var fire = FireTestKit.BurningFire(Footplate.S282, 20f, FireTestKit.CleanCoal);
        var man = new AutoFireman { Mode = FiremanMode.Road };
        man.Step(.05f, fire, Reading(13f, true));
        Assert.True(man.RoundLeft > 0);
        for (var t = 0f; t < 10f; t += .05f)
            Assert.Equal(0f, man.Step(.05f, fire, Reading(15.95f, true)).Shovelful);
        Assert.Equal(0, man.RoundLeft);
    }

    [Fact]
    public void ExtraDemandEasesSmoothlyAndReturnsWhenThereIsRoom()
    {
        float Plan(float pressure, bool forecast)
        {
            var fire = FireTestKit.BurningFire(Footplate.S282, 40f, FireTestKit.CleanCoal);
            var man = new AutoFireman { Mode = FiremanMode.Road };
            for (var t = 0; t < 60; t++) man.Step(1f, fire, Reading(pressure, forecast));
            return man.PlannedSteam;
        }
        var low = Plan(14f, true);
        var middle = Plan(15.6f, true);
        var high = Plan(15.85f, true);
        Assert.True(low > middle && middle > high);
        Assert.Equal(Plan(15.85f, false), high, 4);
        Assert.InRange(low, 5.9f, 6.1f);
    }

    [Fact]
    public void TheOilFireAlsoStopsBuildingForAClimbAtTheSafetyValve()
    {
        float Valve(bool forecast)
        {
            var fire = new OilFire(new FireboxParameters(200f, 100f, .6f));
            fire.PrepareFire();
            fire.Step(.1f, new OilFireInputs { Atomizer = 1f, BoilerPressure = 16f,
                BoilerTemperature = 200f, TenderFuel = 10000f, FiringValve = .2f, AirFlow = 20f });
            var man = new AutoOilFireman { Mode = FiremanMode.Road };
            for (var i = 0; i < 60; i++) man.Step(1f, fire, Reading(15.95f, forecast));
            Assert.True(man.Valve > 0f);
            return man.Valve;
        }
        Assert.Equal(Valve(false), Valve(true), 4);
    }
}
