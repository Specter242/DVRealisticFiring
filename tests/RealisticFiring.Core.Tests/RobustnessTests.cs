using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

/// <summary>
/// What the models must survive (audit 2026-11-09): a save and reload, a
/// bad number from the game, water in the intake at any tick size, a tick
/// of no time, and any handling of the fire without losing or making coal.
/// </summary>
public class RobustnessTests
{
    private static readonly FireboxParameters OilBox = new FireboxParameters(200f, 100f, 0.6f);
    private static readonly SteamDraft Draft = new SteamDraft(0.85f, 1f, 2f, 15f);

    private static OilFireInputs OilInputs(float oil, float exhaust = 0.5f)
    {
        var need = oil * OilFire.AtomizingSteamPerKg * (0.6f + 0.4f * exhaust);
        var capacity = OilFire.AtomizerCapacity * OilFire.AtomizingSteamPerKg * OilFire.MaxFeedFactor * OilBox.MaxCombustionRate;
        return new OilFireInputs
        {
            FiringValve = oil / (OilFire.MaxFeedFactor * OilBox.MaxCombustionRate),
            Atomizer = 1.2f * need / capacity,
            AirFlow = oil * FireModel.StoichiometricAirPerCoal * OilFire.BestExcessAir,
            Exhaust = exhaust,
            BoilerPressure = 15f,
            BoilerTemperature = 198f,
            TenderFuel = 1e6f,
            StartupMultiplier = 1f,
        };
    }

    // A fire lit some minutes ago, its brick still far from relighting
    // heat, saved and loaded with its valve where it was: it went out on the
    // first tick, and oil ran into the dark box.
    [Theory]
    [InlineData(150f, 0.1f)]
    [InlineData(150f, 0.5f)]
    [InlineData(600f, 0.1f)]
    [InlineData(600f, 0.5f)]
    public void ALitOilFireOnCoolBrickStaysLitAcrossAReload(float brick, float share)
    {
        var inputs = OilInputs(share * OilBox.MaxCombustionRate);
        var live = new OilFire(OilBox);
        live.Restore(false, brick, OilFire.WarmOil, 0f);
        live.Ignite();
        for (var i = 0; i < 100; i++)
            live.Step(0.02f, inputs);
        Assert.True(live.FireOn, "lit before the save");

        var loaded = new OilFire(OilBox);
        loaded.Restore(live.FireOn, live.BrickTemperature, live.OilTemperature, live.FlueSoot, live.PooledOil, live.Vapor);
        var worstFlareback = 0f;
        for (var i = 0; i < 3000; i++)
        {
            loaded.Step(0.02f, inputs);
            worstFlareback = Math.Max(worstFlareback, loaded.Flareback);
        }

        Assert.True(loaded.FireOn, "lit a minute after the load");
        Assert.Equal(0f, worstFlareback);
        Assert.True(loaded.Vapor + loaded.PooledOil < OilFire.SafeToLight, $"{loaded.Vapor:F2} kg of vapor, {loaded.PooledOil:F2} kg pooled");
        Assert.True(loaded.FeedRate > 0.9f * share * OilBox.MaxCombustionRate, $"feeding {loaded.FeedRate:F2} kg/s");
    }

    [Fact]
    public void AnOilFireSavedOutComesBackOutWithNoFeedSettledForIt()
    {
        var fire = new OilFire(OilBox);
        fire.Restore(false, OilFire.ColdBrick, OilFire.WarmOil, 0f);
        fire.Step(0.02f, OilInputs(0.5f * OilBox.MaxCombustionRate));
        Assert.False(fire.FireOn);
        Assert.True(fire.FeedRate < 0.1f * OilBox.MaxCombustionRate, "the feed still opens over its second and a half");
    }

    [Fact]
    public void AHeaterLeftOpenStopsAtItsCeilingAndStillWarmsColdOilAsFast()
    {
        var fire = new OilFire(OilBox);
        fire.Restore(true, OilFire.HotBrick, OilFire.AmbientTemperature, 0f);
        var inputs = OilInputs(0.5f * OilBox.MaxCombustionRate);
        inputs.Heater = 1f;
        var warmAt = float.NaN;
        for (var t = 0f; t < 4f * 3600f; t += 0.5f)
        {
            fire.Step(0.5f, inputs);
            if (float.IsNaN(warmAt) && fire.OilTemperature >= OilFire.WarmOil)
                warmAt = t;
        }

        Assert.InRange(warmAt, 420f, 600f);
        Assert.True(fire.OilTemperature <= OilFire.HeaterCeiling, $"{fire.OilTemperature:F0} °C after four hours");
        Assert.True(fire.OilTemperature > OilFire.HotOil, "still overheated: the heater wants shutting");
    }

    // Set on over oil already overheated, he read its sputtering smoke as
    // too much oil, cut the valve to its least, and the draft blew it out.
    [Theory]
    [InlineData(95f)]
    [InlineData(108f)]
    public void TheFiremanKeepsAnOverheatedOilFireAlightAndShutsTheHeater(float oilTemperature)
    {
        var fire = new OilFire(OilBox);
        fire.Restore(true, OilFire.HotBrick, oilTemperature, 0f);
        var fireman = new AutoOilFireman { Mode = FiremanMode.Road };
        const float pressure = 13.5f, engineSteam = 6f, fullWork = 12f;
        FiremanActions last = default;
        double heat = 0;
        var n = 0;
        for (var t = 0f; t < 900f; t += 0.05f)
        {
            last = fireman.Step(0.05f, fire, new FiremanReadings
            {
                Pressure = pressure, SafetyValve = 15f, Work = engineSteam / fullWork, FullWorkSteam = fullWork, GlassMass = 8000f,
                Throttle = 0.6f, Cutoff = 0.5f, Glass = 0.6f, TenderWater = 40000f, TenderCoal = 20000f,
                InjectorCrack = 0.2f, InjectorStartPressure = 3.5f, InjectorFullFeed = 10f, Sand = 20f,
            });
            fire.Step(0.05f, new OilFireInputs
            {
                FiringValve = last.FiringValve, Atomizer = last.Atomizer, Heater = last.Heater,
                AirFlow = Draft.Air(engineSteam, Draft.BlowerSteam(pressure, last.Blower), last.Damper),
                Exhaust = engineSteam / fullWork, BoilerPressure = pressure, BoilerTemperature = 190f, TenderFuel = 20000f, StartupMultiplier = 1f,
            });
            if (t > 300f)
            {
                heat += fire.Heat;
                n++;
            }
        }

        Assert.True(fire.FireOn, "the fire is still lit after a quarter of an hour");
        Assert.Equal(0f, last.Heater);
        Assert.True(last.Notices.HasFlag(FiremanNotice.HotOil));
        Assert.True(heat / n > 0.6f * engineSteam * AutoFiremanBase.SteamHeat, $"{heat / n / 1e6:F1} MW for the {engineSteam * AutoFiremanBase.SteamHeat / 1e6:F1} MW she takes");
    }

    // A melted plug's jet wets the fire by a share. Taken from the stored
    // rate every tick, a tenth of water left a fiftieth of the fire at 100
    // ticks a second.
    [Theory]
    [InlineData(0.1f)]
    [InlineData(0.3f)]
    [InlineData(0.45f)]
    public void WaterInTheIntakeCutsTheFireByItsShareAtAnyTickSize(float water)
    {
        var p = Footplate.S282;
        float Burned(float dt, float wet)
        {
            var fire = FireTestKit.BurningFire(p, 75f, FireTestKit.CleanCoal);
            var inputs = FireTestKit.Inputs(0.6f * p.MaxCombustionRate * FireModel.AirPerCoal);
            inputs.IntakeWater = wet;
            var burned = 0f;
            for (var t = 0f; t < 30f; t += dt)
                burned += fire.Step(dt, inputs);
            Assert.True(fire.FireOn);
            return burned;
        }

        var dry = Burned(0.02f, 0f);
        var fine = Burned(0.01f, water);
        var coarse = Burned(0.1f, water);
        Assert.InRange(fine / coarse, 0.95f, 1.05f);
        // Less than dry, and not by much more than its share twice over (the
        // air is wetted too).
        Assert.True(fine < dry, $"{fine:F2} kg wet against {dry:F2} dry");
        Assert.True(fine > (1f - water) * (1f - water) * 0.8f * dry, $"{fine:F2} kg wet against {dry:F2} dry");
    }

    [Fact]
    public void ABadNumberFromTheGameDoesNotStayInTheFire()
    {
        var p = Footplate.S282;
        var fire = FireTestKit.BurningFire(p, 75f, FireTestKit.CleanCoal);
        var bad = FireTestKit.Inputs(float.NaN);
        bad.BoilerTemperature = float.NaN;
        bad.Speed = float.PositiveInfinity;
        bad.IntakeWater = float.NaN;
        bad.StartupMultiplier = float.NaN;
        fire.Step(0.02f, bad);
        var good = FireTestKit.Inputs(0.6f * p.MaxCombustionRate * FireModel.AirPerCoal);
        for (var i = 0; i < 500; i++)
            fire.Step(0.02f, good);

        Assert.True(float.IsFinite(fire.CoalMass) && fire.CoalMass > 0f, $"coal {fire.CoalMass}");
        Assert.True(float.IsFinite(fire.Temperature), $"temperature {fire.Temperature}");
        Assert.True(float.IsFinite(fire.Heat) && fire.Heat > 0f, $"heat {fire.Heat}");
    }

    [Fact]
    public void ADamagedSaveRestoresToSoundNumbers()
    {
        var p = Footplate.S282;
        var fire = new FireModel(p);
        fire.Restore(true, float.NaN);
        Assert.Equal(0f, fire.CoalMass);

        var plug = new FusiblePlug(100f);
        plug.Restore(false, float.NaN);
        Assert.False(plug.Step(1f, false, 300f, 190f));
        Assert.False(plug.Melted);
        Assert.False(plug.Step(1f, true, float.NaN, float.NaN));
        Assert.True(float.IsFinite(plug.CoreTemperature));

        var fouling = new BoilerFouling();
        fouling.Restore(float.NaN, float.NaN);
        Assert.Equal(0f, fouling.ScaleMass);
        Assert.Equal(0f, fouling.SootLoss);

        var water = new BoilerWater(FeedWaterQuality.Average, 8000f);
        water.Restore(float.NaN, float.NaN, float.NaN, float.NaN);
        water.Step(1f, new BoilerWaterInputs { WaterMass = float.NaN, Fed = float.NaN, BlownDown = float.NaN, Work = float.NaN });
        Assert.True(float.IsFinite(water.Solids) && float.IsFinite(water.Concentration), $"solids {water.Solids}, {water.Concentration} ppm");
    }

    [Fact]
    public void ATickOfNoTimeLeavesAJammedStokerJammed()
    {
        var p = Footplate.S282;
        var fire = FireTestKit.BurningFire(p, 75f, FireTestKit.CleanCoal);
        var stoker = new Stoker(new StokerSpec(p, 1.5f) { MeanCoalBetweenJams = 0f }, p, new Random(1));
        stoker.Jam();
        var inputs = new StokerInputs { Controls = StokerControls.Working(0.5f), BoilerPressure = 14f, TenderCoal = 1000f, AirFlow = 5f };
        Assert.True(stoker.Step(0.02f, inputs, fire).Jammed);
        Assert.True(stoker.Step(0f, inputs, fire).Jammed);
    }

    // Any handling, at any tick: the coal put on is the coal burned, taken
    // off, dropped, and left, and nothing goes negative.
    [Fact]
    public void CoalBalancesUnderRandomHandling()
    {
        var p = Footplate.S282;
        var random = new Random(7);
        for (var run = 0; run < 8; run++)
        {
            var fire = new FireModel(p, new FireTuning()) { GrateSlope = -10f, GrateArea = 6f };
            double added = fire.AddCoalEvenly(60f), burned = 0, removed = 0;
            fire.Ignite();
            for (var step = 0; step < 10000; step++)
            {
                var dt = (float)(0.005 + random.NextDouble() * 0.2);
                var pick = random.Next(400);
                if (pick < 8) added += fire.AddCoal((float)(random.NextDouble() * 8), (GrateZone)random.Next(GrateLayout.ZoneCount));
                else if (pick == 8) fire.ShakeGrate(random.Next(2) == 0 ? ShakeStroke.Shake : ShakeStroke.Wiggle);
                else if (pick == 9) fire.Rake((GrateZone)random.Next(GrateLayout.ZoneCount));
                else if (pick == 10) fire.Slice((GrateZone)random.Next(GrateLayout.ZoneCount));
                else if (pick == 11) fire.Push((GrateZone)random.Next(GrateLayout.ZoneCount), (GrateDirection)random.Next(4));
                else if (pick == 12) fire.OpenDropGrate((float)random.NextDouble(), dt);
                else if (pick == 13) fire.Dump((float)random.NextDouble() * 0.2f, dt);
                else if (pick == 14) fire.OpenAshpan((float)random.NextDouble(), dt);
                else if (pick == 15) removed += fire.RemoveCoal((float)random.NextDouble());
                else if (pick == 16) fire.Ignite();
                if (pick != 12) fire.OpenDropGrate(0f, dt);
                var inputs = FireTestKit.Inputs((float)(random.NextDouble() * 1.3) * p.MaxCombustionRate * FireModel.AirPerCoal, (float)random.NextDouble() * (pick < 100 ? 1f : 0f));
                inputs.Speed = (float)random.NextDouble() * 30f;
                inputs.StartupMultiplier = pick > 350 ? 10f : 1f;
                inputs.IntakeWater = pick == 17 ? 0.3f : 0f;
                burned += fire.Step(dt, inputs);
                foreach (var z in fire.Zones)
                    Assert.True(z.Fresh >= 0f && z.Burning >= 0f && z.Ash >= 0f && z.Clinker >= 0f, $"run {run}, step {step}: a zone below nothing");
                Assert.True(float.IsFinite(fire.Heat) && fire.Heat >= 0f && float.IsFinite(fire.Temperature) && fire.Ashpan >= 0f, $"run {run}, step {step}");
            }
            var left = burned + removed + fire.CoalDumped + fire.CoalDroppedThroughGrate + fire.ShakeCinders + fire.CoalMass;
            Assert.True(Math.Abs(added - left) / added < 1e-3, $"run {run}: {added:F2} kg on, {left:F2} kg accounted for");
        }
    }
}
