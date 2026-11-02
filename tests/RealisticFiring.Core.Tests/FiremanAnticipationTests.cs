using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

public class FiremanAnticipationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AHeavierShovelDoesNotMultiplyTheSustainedHandFiringRate(bool hurried)
    {
        float Fired(float shovel)
        {
            var fire = FireTestKit.BurningFire(Footplate.S282, 20f);
            var man = new AutoFireman { Mode = FiremanMode.Road };
            var r = Reading(AutoFireman.PressureFor(FiremanMode.Road, 16f), false);
            r.RoadKnown = false; r.Work = hurried ? .8f : .5f; r.ShovelMass = shovel;
            r.TenderCoal = 0f;
            for (var i = 0; i < 1200; i++) man.Step(.1f, fire, r);
            r.TenderCoal = 10000f;
            fire.Restore(true, man.CoalTarget - (hurried ? 7.5f : 4f) * shovel);
            var fired = 0f;
            for (var i = 0; i < 6000; i++) fired += man.Step(.1f, fire, r).Shovelful;
            return fired;
        }
        var normal = Fired(3f);
        Assert.True(normal > 0f);
        Assert.InRange(Fired(8f), .85f * normal, normal + 16f);
    }

    [Fact]
    public void FillingTheDeficitEndsARoundWithoutSkippingItsRest()
    {
        var fire = FireTestKit.BurningFire(Footplate.S282, 20f);
        var man = new AutoFireman { Mode = FiremanMode.Road };
        var r = Reading(AutoFireman.PressureFor(FiremanMode.Road, 16f), false);
        r.Work = r.WorkSoon = .5f; r.TenderCoal = 0f;
        for (var i = 0; i < 1200; i++) man.Step(.1f, fire, r);
        r.TenderCoal = 10000f;
        fire.Restore(true, man.CoalTarget - 3f);
        FiremanActions a = default;
        for (var i = 0; i < 100 && a.Shovelful == 0f; i++) a = man.Step(.1f, fire, r);
        Assert.True(a.Shovelful > 0f);
        fire.AddCoal(a.Shovelful, a.Aim);
        man.Step(.1f, fire, r); // The fulfilled target ends this round early.
        fire.Restore(true, man.CoalTarget - 3f);
        for (var i = 0; i < 100; i++) Assert.Equal(0f, man.Step(.1f, fire, r).Shovelful);
    }

    [Fact]
    public void ABriefBurstOfWorkDoesNotImmediatelyBuildAHeavyFireOrWaterReserve()
    {
        var fire = FireTestKit.BurningFire(Footplate.S282, 40f);
        var man = new AutoFireman { Mode = FiremanMode.Road };
        var r = Reading(AutoFireman.PressureFor(FiremanMode.Road, 16f), false);
        r.Work = r.WorkSoon = r.WorkAhead = .3f;
        for (var i = 0; i < 1200; i++) man.Step(.1f, fire, r);
        var baseline = man.PlannedSteam;
        r.Work = r.WorkSoon = r.WorkAhead = .95f;
        for (var i = 0; i < 30; i++) man.Step(.1f, fire, r);
        Assert.True(man.PlannedSteam < baseline + .35f * (5.7f - baseline),
            $"a three-second burst raised the plan from {baseline:F2} to {man.PlannedSteam:F2}");
        Assert.Equal(AutoFireman.GlassGoal, man.GlassTarget);
        for (var i = 0; i < 900; i++) man.Step(.1f, fire, r);
        Assert.InRange(man.PlannedSteam, 5.6f, 5.8f);
        Assert.Equal(AutoFireman.GlassBeforeWork, man.GlassTarget);
        // A real unloading or the engineer's easing call must still reduce fuel promptly.
        r.Work = r.WorkSoon = r.WorkAhead = .3f;
        for (var i = 0; i < 100; i++) man.Step(.1f, fire, r);
        Assert.InRange(man.PlannedSteam, 1.7f, 2.1f);
        man.Call = FiremanCall.EasingOff;
        man.Step(.1f, fire, r);
        Assert.Equal(0f, man.PlannedSteam);
    }

    [Fact]
    public void FillingAheadDoesNotDemandExtraFireAtTheSafetyValveOrWithPressureTooLow()
    {
        float Plan(bool forecast, float pressure)
        {
            var fire = FireTestKit.BurningFire(Footplate.S282, 40f);
            var man = new AutoFireman { Mode = FiremanMode.Road };
            var r = Reading(pressure, forecast);
            r.Glass = .4f; r.GlassMass = 4000f;
            r.Injector = InjectorState.Working; r.InjectorLever = .8f;
            r.InjectorFullFeed = 5f;
            for (var i = 0; i < 1800; i++) man.Step(.1f, fire, r);
            return man.PlannedSteam;
        }
        Assert.Equal(Plan(false, 15.95f), Plan(true, 15.95f), 3);
        // Above the crown-sheet floor, an optional fill is curtailed by low pressure.
        // It must not still demand the heat for the uncurtailed water reserve.
        Assert.Equal(Plan(false, 13f), Plan(true, 13f) - 4.8f, 2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HandFiringHasARestBetweenRoundsEvenWhenBehind(bool hurried)
    {
        var fire = FireTestKit.BurningFire(Footplate.S282, 20f);
        var man = new AutoFireman { Mode = FiremanMode.Road };
        var r = Reading(AutoFireman.PressureFor(FiremanMode.Road, 16f), false);
        r.RoadKnown = false; r.Work = hurried ? .8f : .5f;
        for (var i = 0; i < 1200; i++) man.Step(.1f, fire, r);
        // Hold the deficit constant to exercise the maximum scheduler rate,
        // without conflating the pause with combustion, smoke, or reaching its target.
        fire.Restore(true, man.CoalTarget - (hurried ? 30f : 12f));
        for (var i = 0; i < 600; i++) man.Step(.1f, fire, r);
        var throws = new List<float>();
        for (var i = 0; i < 1200; i++)
            if (man.Step(.1f, fire, r).Shovelful > 0f) throws.Add(i * .1f);
        Assert.True(throws.Count > 4);
        var longestRest = throws.Zip(throws.Skip(1), (a, b) => b - a).Max();
        Assert.True(longestRest >= (hurried ? 6f : 12f), $"longest gap {longestRest:F1} s");
        Assert.True(throws.Count <= (hurried ? 46 : 24), $"{throws.Count} throws in two minutes");
    }

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
