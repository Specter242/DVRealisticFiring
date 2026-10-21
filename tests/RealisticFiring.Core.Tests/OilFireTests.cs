using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

 public class OilFireTests
{
    // A big oil engine's firebox: 200 kg over 100 s, 0.6 efficient at its most.
    private static readonly FireboxParameters Box = new FireboxParameters(200f, 100f, 0.6f);
    private const float Dt = 0.1f;
    private const float HalfOil = 1f;

    private static float Valve(float oil) => oil / (OilFire.MaxFeedFactor * Box.MaxCombustionRate);

    // Air for an oil rate at an excess-air ratio.
    private static float Air(float oil, float excess) => oil * FireModel.StoichiometricAirPerCoal * excess;

    // The atomizer opening that gives the oil this much of its need at an exhaust strength.
    private static float AtomizerFor(float oil, float share, float exhaust = 0.5f)
    {
        var need = oil * OilFire.AtomizingSteamPerKg * (0.6f + 0.4f * exhaust);
        return share * need / (OilFire.AtomizerCapacity * OilFire.AtomizingSteamPerKg * OilFire.MaxFeedFactor * Box.MaxCombustionRate);
    }

    private static OilFireInputs Inputs(float oil, float excess = OilFire.BestExcessAir, float atomizing = 1.2f, float exhaust = 0.5f) => new OilFireInputs
    {
        FiringValve = Valve(oil),
        Atomizer = AtomizerFor(oil, atomizing, exhaust),
        AirFlow = Air(oil, excess),
        Exhaust = exhaust,
        BoilerPressure = 15f,
        BoilerTemperature = 198f,
        TenderFuel = 1e6f,
        StartupMultiplier = 1f,
    };

    private static OilFire Burning()
    {
        var fire = new OilFire(Box);
        fire.PrepareFire();
        return fire;
    }

    private static void Run(OilFire fire, OilFireInputs inputs, float seconds)
    {
        for (var t = 0f; t < seconds; t += Dt)
            fire.Step(Dt, inputs);
    }

    private static double CurveHeat(float rate) => FireTestKit.CurveHeat(Box, rate);

    [Fact]
    public void AWellSetFireMakesItsCurvesSteamForItsOil()
    {
        var fire = Burning();
        Run(fire, Inputs(HalfOil), 60f);

        Assert.Equal(CurveHeat(HalfOil), fire.Heat, 0.03 * CurveHeat(HalfOil));
        Assert.True(fire.SootDensity < 0.05f, $"soot {fire.SootDensity:F2}");
        Assert.Equal(0f, fire.Drumming);
        Assert.Equal(OilFire.BestExcessAir, fire.ExcessAir, 0.05f);
    }

    [Fact]
    public void TheStackReadsTheAirBlackShortOfItClearAndWastefulWithTooMuch()
    {
        var best = Burning();
        var short_ = Burning();
        var clear = Burning();
        Run(best, Inputs(HalfOil), 30f);
        Run(short_, Inputs(HalfOil, excess: 0.7f), 30f);
        Run(clear, Inputs(HalfOil, excess: 3.5f), 30f);

        Assert.True(short_.SootDensity > 0.5f, $"short of air: soot {short_.SootDensity:F2}");
        Assert.True(short_.Heat < 0.8f * best.Heat);
        Assert.True(clear.SootDensity < 0.02f);
        Assert.True(clear.Heat < 0.85f * best.Heat, $"clear stack {clear.Heat / 1e6f:F1} MW against {best.Heat / 1e6f:F1} MW");
        Assert.True(clear.FireOn);
    }

    [Fact]
    public void TheAtomizerMustKeepUpWithTheOilAndTheExhaust()
    {
        var best = Burning();
        var weak = Burning();
        var strong = Burning();
        Run(best, Inputs(HalfOil), 30f);
        Run(weak, Inputs(HalfOil, atomizing: 0.4f), 30f);
        Run(strong, Inputs(HalfOil, atomizing: 5f), 30f);

        Assert.True(weak.SootDensity > 0.4f && weak.PooledOil > 0f, $"weak atomizer: soot {weak.SootDensity:F2}, pooled {weak.PooledOil:F2} kg");
        Assert.True(strong.Drumming > 0.5f, $"drumming {strong.Drumming:F2}");
        // Its waste is mostly the boiler's steam; the firebox cools a little.
        Assert.True(strong.AtomizerSteam > 3.5f * best.AtomizerSteam);
        Assert.True(strong.Heat < best.Heat);

        // The same atomizer that suits a standing fire is short when the exhaust works hard.
        var working = Burning();
        Run(working, Inputs(HalfOil, atomizing: 1f, exhaust: 0f) with { Exhaust = 1f }, 30f);
        Assert.True(working.Atomizing < 0.8f, $"atomizing {working.Atomizing:F2} working hard");
    }

    [Fact]
    public void ColdBrickHoldsOnlyALightFireAndAStrongPullBlowsItOut()
    {
        static OilFire LightFire(float brick)
        {
            var fire = new OilFire(Box);
            fire.Restore(fireOn: true, brickTemperature: brick, oilTemperature: OilFire.WarmOil, flueSoot: 0f);
            return fire;
        }

        var cold = LightFire(OilFire.ColdBrick);
        var hot = LightFire(OilFire.HotBrick);
        var light = 0.08f * Box.MaxCombustionRate;
        foreach (var fire in new[] { cold, hot })
        {
            Run(fire, Inputs(light), 5f);
            // The throttle opened on a light fire: the exhaust pulls hard.
            Run(fire, Inputs(light) with { AirFlow = Air(light, 8f), Exhaust = 1f }, 5f);
        }

        Assert.False(cold.FireOn, "a light fire on cold brick blows out");
        Assert.True(hot.FireOn, "hot brick holds it");
    }

    [Fact]
    public void OilRunInWithTheFireOutFlaresBackWhenLitUnlessTheBlowerClearsItFirst()
    {
        static OilFire OutOnWarmBrick()
        {
            var fire = new OilFire(Box);
            fire.Restore(fireOn: false, brickTemperature: 500f, oilTemperature: OilFire.WarmOil, flueSoot: 0f);
            // The fire went out and the oil was left on, with little draft.
            Run(fire, Inputs(0.3f) with { AirFlow = 0.05f }, 15f);
            return fire;
        }

        var careless = OutOnWarmBrick();
        Assert.True(careless.Vapor > OilFire.FlarebackFuel, $"vapor {careless.Vapor:F2} kg");
        careless.Ignite();
        careless.Step(Dt, Inputs(0.3f));
        Assert.True(careless.Flareback > 0f, "lit with vapor in the box, it flares back");

        var careful = OutOnWarmBrick();
        // Oil shut, blower on to clear the box, then waste, then oil.
        Run(careful, Inputs(0.3f) with { FiringValve = 0f, AirFlow = 3f }, 20f);
        careful.Ignite();
        var flared = 0f;
        for (var t = 0f; t < 3f; t += Dt)
        {
            careful.Step(Dt, Inputs(0.3f));
            flared = Math.Max(flared, careful.Flareback);
        }
        Assert.Equal(0f, flared);
        Assert.True(careful.FireOn);
    }

    [Fact]
    public void LitInOrderItLightsCleanlyButOilBeforeTheAtomizerExplodes()
    {
        var right = new OilFire(Box);
        right.Restore(false, OilFire.ColdBrick, OilFire.WarmOil, 0f);
        right.Ignite();
        var flared = 0f;
        for (var t = 0f; t < 5f; t += Dt)
        {
            right.Step(Dt, Inputs(0.2f));
            flared = Math.Max(flared, right.Flareback);
        }
        Assert.True(right.FireOn);
        Assert.Equal(0f, flared);

        var wrong = new OilFire(Box);
        wrong.Restore(false, OilFire.ColdBrick, OilFire.WarmOil, 0f);
        wrong.Ignite();
        flared = 0f;
        for (var t = 0f; t < 10f; t += Dt)
        {
            wrong.Step(Dt, Inputs(0.4f) with { Atomizer = 0f });
            flared = Math.Max(flared, wrong.Flareback);
        }
        Assert.True(flared > 0f, "oil on the waste without the atomizer pools and goes up");
    }

    [Fact]
    public void HotBrickRelightsOilByItself()
    {
        var fire = Burning();
        Run(fire, Inputs(HalfOil), 60f);
        fire.Extinguish();

        fire.Step(Dt, Inputs(HalfOil));

        Assert.True(fire.FireOn);
        Assert.True(fire.BrickTemperature > OilFire.BrickRelights);
    }

    [Fact]
    public void SmokeSootsTheFluesAndSandScoursThemUnderAHardPull()
    {
        var fire = Burning();
        var clean = Burning();
        Run(clean, Inputs(HalfOil), 30f);
        var cleanHeat = clean.Heat;

        Run(fire, Inputs(HalfOil, excess: 0.6f), 600f);
        Run(fire, Inputs(HalfOil), 30f);
        Assert.True(fire.FlueSoot > 0.4f, $"soot {fire.FlueSoot:F2} after ten minutes smoking");
        Assert.True(fire.Heat < 0.92f * cleanHeat);

        var gentle = fire.FlueSoot;
        // A quart at a time (K02 p. 46).
        fire.ThrowSand(1.5f, drawing: 0.2f);
        Assert.True(fire.FlueSoot > 0.9f * gentle, "with little pull the sand falls in the pan");

        fire.ThrowSand(1.5f, drawing: 1f);
        Assert.True(fire.FlueSoot < 0.05f, $"soot {fire.FlueSoot:F2} after a quart of sand under a hard pull");
        Assert.True(fire.SandBurst > 0.5f);
    }

    [Fact]
    public void CoolOilLagsAndSmokesUntilTheHeaterWarmsIt()
    {
        var warm = Burning();
        var cool = Burning();
        cool.Restore(true, OilFire.HotBrick, 30f, 0f);
        Run(warm, Inputs(HalfOil), 20f);
        Run(cool, Inputs(HalfOil), 20f);

        Assert.True(cool.FeedRate < 0.5f * warm.FeedRate, $"cool oil feeds {cool.FeedRate:F2} kg/s against {warm.FeedRate:F2}");
        Assert.True(cool.Atomizing < 0.6f * warm.Atomizing, $"cool oil atomizing {cool.Atomizing:F2} against {warm.Atomizing:F2}");
        Assert.True(cool.SootDensity > warm.SootDensity + 0.2f, $"cool oil smokes {cool.SootDensity:F2}");

        Run(cool, Inputs(HalfOil) with { Heater = 1f }, 600f);
        Assert.True(cool.OilTemperature >= OilFire.WarmOil, $"oil at {cool.OilTemperature:F0} C after ten minutes' heating");
        Assert.True(cool.HeaterSteamUsed > 0f);
    }

    [Fact]
    public void ColdHeavyOilDoesNotRunThroughAnOpenValve()
    {
        var fire = new OilFire(Box);
        fire.Restore(false, OilFire.ColdBrick, OilFire.AmbientTemperature, 0f);
        Run(fire, Inputs(0f) with { FiringValve = 1f, Atomizer = 0f, BoilerPressure = 1f }, 60f);

        Assert.Equal(0f, fire.FeedRate);
        Assert.Equal(0f, fire.PooledOil);
    }

    [Fact]
    public void WithoutBoilerPressureNeitherTheHeaterNorTheAtomizerWorks()
    {
        var fire = new OilFire(Box);
        fire.Restore(false, OilFire.ColdBrick, OilFire.AmbientTemperature, 0f);
        Run(fire, Inputs(0f) with { Heater = 1f, Atomizer = 1f, BoilerPressure = 1f }, 600f);

        Assert.Equal(0f, fire.HeaterSteamUsed);
        Assert.Equal(0f, fire.AtomizerSteam);
        Assert.Equal(OilFire.AmbientTemperature, fire.OilTemperature, 1);
    }

    [Fact]
    public void TheStackShowsAHazeWhenRightClearWithTooMuchAirAndBlackWhenStarved()
    {
        float Stack(float excess, float atomizing = 1.2f)
        {
            var fire = Burning();
            Run(fire, Inputs(HalfOil, excess, atomizing), 20f);
            return fire.StackSmoke;
        }

        Assert.InRange(Stack(OilFire.BestExcessAir), 0.12f, 0.3f);
        Assert.InRange(Stack(2.5f), 0f, 0.05f);
        Assert.True(Stack(0.7f) > 0.6f, $"short of air {Stack(0.7f):F2}");
        Assert.True(Stack(OilFire.BestExcessAir, atomizing: 0.4f) > 0.4f, $"starved of steam {Stack(OilFire.BestExcessAir, 0.4f):F2}");
        // A little too much oil for the air browns the haze; it is not yet black.
        Assert.InRange(Stack(0.9f), 0.3f, 0.6f);
    }

    [Fact]
    public void TheStackDarkensOverSecondsAsTheSmokeWorksThroughTheTubes()
    {
        var fire = Burning();
        Run(fire, Inputs(HalfOil), 20f);
        Run(fire, Inputs(HalfOil, excess: 0.6f), 1f);
        var soon = fire.StackSmoke;
        Run(fire, Inputs(HalfOil, excess: 0.6f), 10f);

        Assert.True(soon < 0.6f && fire.StackSmoke > 0.85f, $"{soon:F2} after a second, {fire.StackSmoke:F2} after ten");
    }

    [Fact]
    public void AShedsSteamLineLightsADeadEngine()
    {
        var dead = new OilFire(Box);
        Run(dead, Inputs(HalfOil) with { BoilerPressure = 1f }, 1f);
        dead.Ignite();
        Run(dead, Inputs(HalfOil) with { BoilerPressure = 1f }, 5f);
        Assert.False(dead.FireOn, "with no steam the atomizer cannot work, so the oil will not light");

        var lined = new OilFire(Box);
        lined.Ignite();
        Run(lined, Inputs(HalfOil) with { BoilerPressure = 1f, SteamLinePressure = 7.9f }, 20f);
        Assert.True(lined.FireOn && lined.OnSteamLine && lined.AtomizerSteam > 0f, $"atomizing {lined.Atomizing:F2}");

        Run(lined, Inputs(HalfOil) with { BoilerPressure = 15f, SteamLinePressure = 7.9f }, 1f);
        Assert.False(lined.OnSteamLine, "with more steam of its own the engine takes the boiler's");
    }

    [Fact]
    public void TheHeaterAndAtomizerPassMoreSteamAsThePressureRises()
    {
        float Heated(float pressure)
        {
            var fire = new OilFire(Box);
            fire.Restore(false, OilFire.ColdBrick, OilFire.AmbientTemperature, 0f);
            Run(fire, Inputs(0f) with { Heater = 1f, Atomizer = 1f, BoilerPressure = pressure }, 300f);
            return fire.OilTemperature;
        }
        var low = new OilFire(Box);
        var high = new OilFire(Box);
        Run(low, Inputs(0f) with { Atomizer = 1f, BoilerPressure = 3f }, 1f);
        Run(high, Inputs(0f) with { Atomizer = 1f, BoilerPressure = 15f }, 1f);

        Assert.True(Heated(3f) > OilFire.AmbientTemperature + 1f && Heated(3f) < Heated(15f) - 3f, $"{Heated(3f):F1} C at 3 bar, {Heated(15f):F1} at 15");
        Assert.True(low.AtomizerSteam > 0f && low.AtomizerSteam < 0.5f * high.AtomizerSteam);
    }

    [Fact]
    public void TheFireAnswersTheValveWithinSeconds()
    {
        var fire = Burning();
        Run(fire, Inputs(0.4f), 30f);
        var t = 0f;
        while (fire.FeedRate < 0.9f * 1.2f && t < 30f)
        {
            fire.Step(Dt, Inputs(1.2f));
            t += Dt;
        }
        Assert.InRange(t, 0.5f, 6f);
    }

    [Fact]
    public void AMirroredFireShowsWhatTheOtherShows()
    {
        var fire = Burning();
        Run(fire, Inputs(HalfOil, excess: 0.8f), 30f);
        var mirror = new OilFire(Box);

        mirror.Mirror(fire.Snapshot());

        Assert.Equal(fire.Snapshot(), mirror.Snapshot());
        Assert.True(mirror.FireOn && mirror.SootDensity > 0f);
    }


    [Theory]
    [InlineData(1f)]
    [InlineData(25f)]
    public void AnEmptyTenderBurnsNothingWhateverTheFeedWasDoing(float speedup)
    {
        var fire = Burning();
        Run(fire, Inputs(1f), 60f);
        var empty = Inputs(1f);
        empty.TenderFuel = 0f;
        empty.StartupMultiplier = speedup;
        var drawn = fire.Step(Dt, empty);
        Assert.Equal(0f, drawn);
        Assert.Equal(0f, fire.Heat);
    }

    [Theory]
    [InlineData(1f, 0.02f)]
    [InlineData(25f, 0.02f)]
    [InlineData(25f, 0.1f)]
    public void ItNeverDrawsMoreThanTheTenderHolds(float speedup, float dt)
    {
        var fire = Burning();
        Run(fire, Inputs(1f), 60f);
        var low = Inputs(1f);
        low.TenderFuel = 0.01f;
        low.StartupMultiplier = speedup;
        var drawn = fire.Step(dt, low);
        Assert.InRange(drawn, 0f, 0.01f + 1e-6f);
        Assert.True(float.IsFinite(fire.Heat) && fire.Heat >= 0f);
    }

    [Fact]
    public void ADrownedFireStaysOutUntilTheWaterClears()
    {
        var fire = Burning();
        Run(fire, Inputs(1f), 60f);
        var flooded = Inputs(1f);
        flooded.IntakeWater = 1f;
        // The brick is hot enough to relight oil, and the oil still runs.
        Assert.True(fire.BrickTemperature > OilFire.BrickRelights);
        for (var i = 0; i < 50; i++)
        {
            fire.Step(Dt, flooded);
            Assert.False(fire.FireOn);
            Assert.Equal(0f, fire.Heat);
        }

        // Cleared, the hot brick lights the oil again.
        Run(fire, Inputs(1f), 2f);
        Assert.True(fire.FireOn);
    }

    [Fact]
    public void PooledOilDrainsFromAColdBoxUntilItLightsSafely()
    {
        var fire = new OilFire(Box);
        fire.Restore(false, OilFire.ColdBrick, OilFire.WarmOil, 0f, pooledOil: 1f);
        Run(fire, Inputs(0f), 300f);

        Assert.True(fire.PooledOil < OilFire.SafeToLight && fire.PooledOil > 0f, $"pooled {fire.PooledOil:F2} kg after 5 min");
        Assert.True(OilFire.SafeToLight < OilFire.FlarebackFuel);
    }

    [Fact]
    public void PooledOilAndVaporSurviveARestoreAndStillFlareBack()
    {
        var fire = new OilFire(Box);
        fire.Restore(false, OilFire.ColdBrick, OilFire.WarmOil, 0f, pooledOil: 3f, vapor: 1f);
        Assert.Equal(3f, fire.PooledOil);
        Assert.Equal(1f, fire.Vapor);

        // Lit with waste, what gathered goes up at once.
        fire.Ignite();
        fire.Step(Dt, Inputs(0.2f));
        Assert.True(fire.Flareback > 0f);
    }

    [Fact]
    public void ARestoreRejectsNonsense()
    {
        var fire = new OilFire(Box);
        fire.Restore(false, float.NaN, float.PositiveInfinity, 0f, pooledOil: -2f, vapor: float.NaN);
        Assert.Equal(0f, fire.PooledOil);
        Assert.Equal(0f, fire.Vapor);
        Assert.True(float.IsFinite(fire.BrickTemperature) && float.IsFinite(fire.OilTemperature));
    }
}
