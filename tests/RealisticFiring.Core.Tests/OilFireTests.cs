using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

 public class OilFireTests
{
    // An S484-841-sized firebox: 200 kg over 100 s, 0.6 efficient at its most.
    private static readonly FireboxParameters Gs4 = new FireboxParameters(200f, 100f, 0.6f);
    private const float Dt = 0.1f;
    private const float HalfOil = 1f;

    private static float Valve(float oil) => oil / (OilFire.MaxFeedFactor * Gs4.MaxCombustionRate);

    // Air for an oil rate at an excess-air ratio.
    private static float Air(float oil, float excess) => oil * FireModel.StoichiometricAirPerCoal * excess;

    // The atomizer opening that gives the oil this much of its need at an exhaust strength.
    private static float AtomizerFor(float oil, float share, float exhaust = 0.5f)
    {
        var need = oil * OilFire.AtomizingSteamPerKg * (0.6f + 0.4f * exhaust);
        return share * need / (OilFire.AtomizerCapacity * OilFire.AtomizingSteamPerKg * OilFire.MaxFeedFactor * Gs4.MaxCombustionRate);
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
        var fire = new OilFire(Gs4);
        fire.PrepareFire();
        return fire;
    }

    private static void Run(OilFire fire, OilFireInputs inputs, float seconds)
    {
        for (var t = 0f; t < seconds; t += Dt)
            fire.Step(Dt, inputs);
    }

    private static double StockHeat(float rate) => FireTestKit.StockHeat(Gs4, rate);

    [Fact]
    public void AWellSetFireMakesTheStockSteamForItsOil()
    {
        var fire = Burning();
        Run(fire, Inputs(HalfOil), 60f);

        Assert.Equal(StockHeat(HalfOil), fire.Heat, 0.03 * StockHeat(HalfOil));
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
            var fire = new OilFire(Gs4);
            fire.Restore(fireOn: true, brickTemperature: brick, oilTemperature: OilFire.WarmOil, flueSoot: 0f);
            return fire;
        }

        var cold = LightFire(OilFire.ColdBrick);
        var hot = LightFire(OilFire.HotBrick);
        var light = 0.08f * Gs4.MaxCombustionRate;
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
            var fire = new OilFire(Gs4);
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
        var right = new OilFire(Gs4);
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

        var wrong = new OilFire(Gs4);
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
        fire.ThrowSand(3f, drawing: 0.1f);
        Assert.True(fire.FlueSoot > 0.9f * gentle, "with little pull the sand falls in the pan");

        fire.ThrowSand(3f, drawing: 1f);
        fire.ThrowSand(3f, drawing: 1f);
        Assert.True(fire.FlueSoot < 0.05f, $"soot {fire.FlueSoot:F2} after sanding");
        Assert.True(fire.SandBurst > 0.5f);
    }

    [Fact]
    public void ColdOilLagsAndSmokesUntilTheHeaterWarmsIt()
    {
        var warm = Burning();
        var cold = Burning();
        cold.Restore(true, OilFire.HotBrick, OilFire.AmbientTemperature, 0f);
        Run(warm, Inputs(HalfOil), 20f);
        Run(cold, Inputs(HalfOil), 20f);

        Assert.True(cold.FeedRate < 0.5f * warm.FeedRate, $"cold oil feeds {cold.FeedRate:F2} kg/s against {warm.FeedRate:F2}");
        Assert.True(cold.Atomizing < 0.6f * warm.Atomizing, $"cold oil atomizing {cold.Atomizing:F2} against {warm.Atomizing:F2}");
        Assert.True(cold.SootDensity > warm.SootDensity + 0.2f, $"cold oil smokes {cold.SootDensity:F2}");

        Run(cold, Inputs(HalfOil) with { Heater = 1f }, 600f);
        Assert.True(cold.OilTemperature >= OilFire.WarmOil, $"oil at {cold.OilTemperature:F0} C after ten minutes' heating");
        Assert.True(cold.HeaterSteamUsed > 0f);
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
        var mirror = new OilFire(Gs4);

        mirror.Mirror(fire.Snapshot());

        Assert.Equal(fire.Snapshot(), mirror.Snapshot());
        Assert.True(mirror.FireOn && mirror.SootDensity > 0f);
    }
}
