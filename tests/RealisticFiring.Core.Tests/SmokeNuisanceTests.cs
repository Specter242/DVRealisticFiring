using RealisticFiring.Core;
using Xunit.Abstractions;

namespace RealisticFiring.Core.Tests;

public class SmokeNuisanceTests
{
    private readonly ITestOutputHelper output;
    public SmokeNuisanceTests(ITestOutputHelper output) => this.output = output;

    public static IEnumerable<object[]> StandingCases()
    {
        foreach (var setup in new[] { (Jk3: false, Stoker: 0f), (Jk3: false, Stoker: 1.56f), (Jk3: true, Stoker: 0f) })
            foreach (var mode in new[] { FiremanMode.KeepAlive, FiremanMode.Shunting, FiremanMode.Road })
                foreach (var work in new[] { .2f, .85f })
                    yield return new object[] { setup.Jk3, setup.Stoker, mode, work };
    }

    [Theory]
    [MemberData(nameof(StandingCases))]
    public void AutomaticFiremanStandingAfterWorkDoesNotEarnFees(bool jk3, float stoker, FiremanMode mode, float work)
    {
        var engine = new Footplate(mode, coal: jk3 ? 20 : 40, stokerFeed: stoker,
            engine: jk3 ? FootplateEngine.Jk3 : FootplateEngine.S282Like);
        // The small JK3 cannot sustain the S282 harness's 6 kg/s. Keep
        // this stop check above atmospheric pressure, as in the actual game.
        engine.EngineSteam = (jk3 ? 1.5f : Footplate.FullWorkSteam) * work;
        engine.Run(600);
        Assert.True(engine.Pressure >= 1, $"Invalid pre-stop boiler pressure {engine.Pressure}");
        engine.EngineSteam = 0;
        engine.Standing = true;
        var beforeBlown = engine.BlownOff;
        var model = new SmokeNuisance();
        double fee = 0;
        var warnings = 0;
        engine.Run(3600, e =>
        {
            var flow = model.Step(Footplate.Dt, SmokeDistrict.HarborTown,
                SmokeNuisance.CoalSmoke(e.Fire), e.Fire.FireOn && e.Fire.ConsumptionRate > 0, false);
            fee += flow.Fee;
            if (flow.Warning) warnings++;
        });
        output.WriteLine($"jk3={jk3} stoker={stoker} mode={mode} after={work}: warnings={warnings} fee={fee} " +
            $"blown off while standing={engine.BlownOff - beforeBlown:F3} kg pressure={engine.Pressure:F3}");
        Assert.InRange(warnings, 0, 1);
        Assert.Equal(0, fee);
    }

    [Theory]
    [InlineData(false, 0f)]
    [InlineData(false, 1.56f)]
    [InlineData(true, 0f)]
    public void AutomaticFiremanAtChangingLoadsDoesNotIncurFees(bool jk3, float stoker)
    {
        var engine = new Footplate(coal: jk3 ? 20 : 40, stokerFeed: stoker,
            engine: jk3 ? FootplateEngine.Jk3 : FootplateEngine.S282Like);
        var model = new SmokeNuisance();
        double total = 0;
        foreach (var work in new[] { .2f, .5f, .85f, .25f })
        {
            engine.EngineSteam = Footplate.FullWorkSteam * work;
            engine.Run(600, e =>
            {
                total += model.Step(Footplate.Dt, SmokeDistrict.CitySouth,
                    SmokeNuisance.CoalSmoke(e.Fire), e.Fire.FireOn, false).Fee;
            });
        }
        Assert.Equal(0, total);
    }

    [Theory]
    [InlineData(.25f)]
    [InlineData(.5f)]
    [InlineData(.8f)]
    public void FrequentSmallHandFiringRemainsFree(float load)
    {
        var fire = new TendedFire(Footplate.S282, load);
        var model = new SmokeNuisance();
        double total = 0;
        for (var i = 0; i < 1800; ++i)
        {
            fire.Run(1);
            total += model.Step(1, SmokeDistrict.HarborTown, SmokeNuisance.CoalSmoke(fire.Fire), fire.Fire.FireOn, false).Fee;
        }
        Assert.Equal(0, total);
    }

    [Theory]
    [InlineData(OilFire.BestExcessAir, 0)]
    [InlineData(.2f, 500)]
    public void OilHazeIsFreeButAnAirStarvedBlackFireCanReachTheCap(float excess, double expected)
    {
        var fire = new OilFire(new FireboxParameters(200, 100, .6f));
        fire.PrepareFire();
        var model = new SmokeNuisance();
        double total = 0;
        var inputs = new OilFireInputs { FiringValve = .5f / OilFire.MaxFeedFactor, Atomizer = .48f,
            AirFlow = FireModel.StoichiometricAirPerCoal * excess, Exhaust = .5f,
            BoilerPressure = 15, BoilerTemperature = 198, TenderFuel = 1e6f, StartupMultiplier = 1 };
        for (var i = 0; i < 9000; ++i)
        {
            fire.Step(.1f, inputs);
            total += model.Step(.1f, SmokeDistrict.CityWest, SmokeNuisance.OilSmoke(fire), fire.FireOn, false).Fee;
        }
        Assert.Equal(expected, total);
    }

    [Fact]
    public void HeavySmokeStepAllocatesNothing()
    {
        var model = new SmokeNuisance();
        for (var i = 0; i < 100; ++i) Heavy(model, .02);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; ++i) Heavy(model, .02);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }

    private static SmokeNuisanceFlow Heavy(SmokeNuisance model, double seconds,
        SmokeDistrict district = SmokeDistrict.HarborTown) => model.Step(seconds, district, 1f, true, false);

    [Theory]
    [InlineData(2f, 500)]
    [InlineData(4f, 500)]
    [InlineData(6f, 500)]
    [InlineData(8f, 500)]
    [InlineData(12f, 0)]
    [InlineData(16f, 0)]
    public void OverfiredCoalChargesOnlyTheSmokyDraftCases(float air, double expectedFee)
    {
        var fire = new FireModel(Footplate.S282) { GrateArea = 2.12f,
            Character = new GrateCharacter { OverFireMixing = .8f } };
        fire.Restore(true, 20);
        fire.AddCoal(120, fire.NeediestZone());
        var model = new SmokeNuisance();
        var peak = 0f;
        double total = 0;
        for (var i = 0; i < 18000; ++i)
        {
            if (fire.CoalMass < 140) fire.AddCoal(140 - fire.CoalMass, fire.NeediestZone());
            fire.Step(.1f, FireTestKit.Inputs(air));
            peak = Math.Max(peak, fire.SootDensity);
            total += model.Step(.1, SmokeDistrict.CityWest,
                SmokeNuisance.CoalSmoke(fire), fire.FireOn, false).Fee;
        }
        output.WriteLine($"air {air}: peak soot {peak:F3}, charged {total:F0}");
        Assert.Equal(expectedFee, total);
    }

    [Fact]
    public void WarnsAfterOneMinuteButAllowsThreeMinutesBeforeCharging()
    {
        var model = new SmokeNuisance();
        Assert.False(Heavy(model, 59).Warning);
        var warning = Heavy(model, 1);
        Assert.True(warning.Warning);
        Assert.Equal(0, warning.Fee);
        Assert.False(Heavy(model, 120).Warning);
        Assert.Equal(0, model.State.Charged);
        var fee = Heavy(model, 60);
        Assert.True(fee.FirstCharge);
        Assert.Equal(100, fee.Fee, 6);
        Assert.False(Heavy(model, 60).FirstCharge);
    }

    [Theory]
    [InlineData(.0f, true, false)]
    [InlineData(.2f, true, false)]
    [InlineData(.64f, true, false)]
    [InlineData(1f, false, false)]
    [InlineData(1f, true, true)]
    public void ClearSteamHazeDeadFiresAndExemptLightupsAreFree(float soot, bool burning, bool exempt)
    {
        var model = new SmokeNuisance();
        var flow = model.Step(3600, SmokeDistrict.CityWest, soot, burning, exempt);
        Assert.Equal(0, flow.Fee);
        Assert.False(flow.Warning);
        Assert.Equal(0, model.State.SmokeSeconds);
    }

    [Fact]
    public void CleanRunningRestoresGraceWithoutResettingTheVisitCap()
    {
        var model = new SmokeNuisance();
        Heavy(model, 240);
        model.Step(120, SmokeDistrict.HarborTown, 0, true, false);
        Assert.Equal(0, model.State.SmokeSeconds);
        Assert.Equal(100, model.State.Charged, 6);
        Assert.Equal(0, Heavy(model, 180).Fee);
        Assert.Equal(400, Heavy(model, 3600).Fee, 6);
        Assert.Equal(0, Heavy(model, 3600).Fee);
    }

    [Fact]
    public void RepeatedShortShovelfulSmokeAndPullingAwayStayFree()
    {
        var model = new SmokeNuisance();
        for (var i = 0; i < 30; ++i)
        {
            Assert.Equal(0, Heavy(model, 30).Fee);
            model.Step(20, SmokeDistrict.HarborTown, .3f, true, false);
        }
        Assert.Equal(0, Heavy(model, 90).Fee);
        Assert.Equal(0, model.State.Charged);
    }

    [Fact]
    public void RuralAndUnknownDistrictsAreFree()
    {
        var model = new SmokeNuisance();
        Assert.Equal(0, Heavy(model, 3600, SmokeDistrict.None).Fee);
        Assert.Equal(0, Heavy(model, 3600, (SmokeDistrict)100).Fee);
        Assert.Equal(0, model.State.Charged);
    }

    [Fact]
    public void ABriefBoundaryCrossingOrDebtPaymentCannotResetTheCap()
    {
        var model = new SmokeNuisance();
        Assert.Equal(500, Heavy(model, 1000).Fee, 6);
        Heavy(model, 5, SmokeDistrict.None);
        Assert.Equal(0, Heavy(model, 1000).Fee);
        Heavy(model, 600, SmokeDistrict.None);
        Assert.Equal(100, Heavy(model, 240).Fee, 6);
    }

    [Fact]
    public void ASeparateTownStartsANewVisit()
    {
        var model = new SmokeNuisance();
        Heavy(model, 1000);
        Assert.Equal(0, Heavy(model, 180, SmokeDistrict.CitySouth).Fee);
        Assert.Equal(100, Heavy(model, 60, SmokeDistrict.CitySouth).Fee, 6);
    }

    [Fact]
    public void SavedGraceAndCapSurviveReload()
    {
        var first = new SmokeNuisance();
        Heavy(first, 210);
        var reloaded = new SmokeNuisance();
        reloaded.Restore(first.State);
        Assert.Equal(first.State, reloaded.State);
        Assert.False(Heavy(reloaded, 30).Warning);
        Assert.Equal(100, reloaded.State.Charged, 6);
        Assert.Equal(400, Heavy(reloaded, 1000).Fee, 6);
        reloaded.Restore(default);
        Assert.True(Heavy(reloaded, 60).Warning);
    }

    [Theory]
    [InlineData(1.0 / 144)]
    [InlineData(1.0 / 60)]
    [InlineData(.1)]
    [InlineData(1)]
    [InlineData(240)]
    public void GraceCrossingAndRateAreIndependentOfFrameRate(double dt)
    {
        var model = new SmokeNuisance();
        double total = 0;
        for (double t = 0; t < 240;)
        {
            var step = Math.Min(dt, 240 - t);
            total += Heavy(model, step).Fee;
            t += step;
        }
        Assert.Equal(100, total, 5);
    }

    [Fact]
    public void InvalidInputsCannotChargeOrPoisonState()
    {
        var model = new SmokeNuisance();
        Heavy(model, 59);
        foreach (var dt in new[] { double.NaN, double.PositiveInfinity, -1, 0 })
            Assert.Equal(0, Heavy(model, dt).Fee);
        Assert.Equal(59, model.State.SmokeSeconds);
        model.Step(60, SmokeDistrict.HarborTown, float.NaN, true, false);
        Assert.Equal(0, model.State.SmokeSeconds);
        model.Restore(new SmokeNuisanceState { District = SmokeDistrict.CityWest, Charged = double.NaN });
        Assert.Equal(default, model.State);
    }
}
