using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

/// <summary>
/// Cold-boiler thermal shock: measures actual boiler temperature rise, gentle
/// warm-up stays sound (S282 and S060), forcing a cold boiler hard stresses
/// tubes and stays into slow leak wear, Steam Startup speedup is normalized so
/// 25x never unfairly punishes normal cold starts, working engines with sagged
/// pressure never trigger thermal shock or stall the fireman, direct steaming
/// is safe, repair clears wear, and Relaxed-off disables every effect and
/// fireman cold-start branch.
/// </summary>
public class BoilerThermalShockTests
{
    private const float Dt = 0.1f;
    // Representative S282 combined water + steel heat capacity (~65 MJ/K).
    private const float S282Capacity = 65e6f;
    // Representative S060 combined water + steel heat capacity (~19 MJ/K).
    private const float S060Capacity = 19e6f;

    private static BoilerThermalShockInputs Inputs(float temp, float startup = 1f,
        bool working = false, bool directSteaming = false, bool enabled = true) => new()
    {
        BoilerTemperature = temp,
        StartupMultiplier = startup,
        Working = working,
        DirectSteaming = directSteaming,
        Enabled = enabled,
    };

    [Fact]
    public void GentleColdStartRampAccumulatesZeroStressAndZeroWear()
    {
        var shock = new BoilerThermalShock();
        var temp = 25f;
        // 5 MW unboosted on S282 => ~0.077 K/s, safely under SafeHeatingRate (0.12 K/s).
        const float heat = 5e6f;
        while (temp < 190f)
        {
            shock.Step(Dt, Inputs(temp));
            Assert.False(shock.WarmingTooFast);
            temp += heat / S282Capacity * Dt;
        }
        Assert.Equal(0f, shock.Wear);
        Assert.Equal(0f, shock.Stress);
        Assert.Equal(0f, shock.EfficiencyLoss);
        Assert.Equal(0f, shock.LeakRate(15f));
    }

    [Fact]
    public void ForcedColdStartAccumulatesStressWarnsAndCausesTubeLeakWear()
    {
        var shock = new BoilerThermalShock();
        var temp = 25f;
        var warned = false;
        // 20 MW roaring fire on cold S282 => ~0.308 K/s, well above SafeHeatingRate.
        const float heat = 20e6f;
        while (temp < 190f)
        {
            shock.Step(Dt, Inputs(temp));
            warned |= shock.WarmingTooFast;
            temp += heat / S282Capacity * Dt;
        }
        Assert.True(warned, "forcing a cold boiler should warn the crew");
        Assert.InRange(shock.Wear, 0.12f, 0.38f);
        Assert.True(shock.EfficiencyLoss > 0.01f && shock.EfficiencyLoss <= BoilerThermalShock.MaxEfficiencyLoss);
        Assert.Equal(0f, shock.LeakRate(1f));
        Assert.True(shock.LeakRate(15f) > 0.01f);
        Assert.True(shock.LeakRate(15f) > shock.LeakRate(5f));
    }

    [Fact]
    public void S060ColdStartGentleIsSoundAndForcedAccumulatesWear()
    {
        // Gentle S060 cold start (~1.8 MW unboosted on 19 MJ/K => ~0.095 K/s < 0.12 K/s).
        var gentle = new BoilerThermalShock();
        var temp = 25f;
        while (temp < 190f)
        {
            gentle.Step(Dt, Inputs(temp));
            Assert.False(gentle.WarmingTooFast);
            temp += 1.8e6f / S060Capacity * Dt;
        }
        Assert.Equal(0f, gentle.Wear);
        Assert.Equal(0f, gentle.Stress);

        // Forced S060 cold start (4.5 MW roaring fire on 19 MJ/K => ~0.237 K/s > 0.12 K/s).
        float RunForced(float speedup, float stepDt)
        {
            var forced = new BoilerThermalShock();
            var t = 25f;
            var warned = false;
            var rate = 4.5e6f * speedup / S060Capacity;
            while (t < 190f)
            {
                forced.Step(stepDt, Inputs(t, startup: speedup));
                warned |= forced.WarmingTooFast;
                t += rate * stepDt;
            }
            Assert.True(warned, "forced S060 cold start should warn");
            return forced.Wear;
        }

        var wear1x = RunForced(1f, 0.25f);
        var wear25x = RunForced(25f, 0.01f);
        Assert.InRange(wear1x, 0.05f, 0.30f);
        Assert.InRange(wear25x, wear1x * 0.95f, wear1x * 1.05f);
    }

    [Fact]
    public void WorkingS282WithSaggedPressureTakesZeroShockNeverWarnsAndFiremanKeepsShoveling()
    {
        // Probe: S282 working hard on a 14-20 MW fire with pressure sagged below
        // 5-6 bar gauge (< 165 C). Both while pressure sags/holds AND while it
        // recovers slightly under heavy steam draw, thermal shock must stay 0,
        // advice must never tell the crew to ease the fire, and the auto fireman
        // must shovel coal normally.
        var shock = new BoilerThermalShock();
        var fire = FireTestKit.BurningFire(Footplate.S282, 35f);
        var injectors = new[] { new InjectorReading(InjectorState.Working, 0.6f, true) };

        // Phase 1: pressure sagging from 6.0 bar abs (~159 C) to 4.5 bar abs (~148 C) over 180 s.
        var temp = 159f;
        for (var t = 0f; t < 180f; t += Dt)
        {
            shock.Step(Dt, Inputs(temp, working: true));
            Assert.False(shock.WarmingTooFast);
            Assert.Equal(0f, shock.Stress);
            Assert.Equal(0f, shock.Wear);
            temp = Math.Max(148f, temp - 0.06f * Dt);
        }

        // Phase 2: pressure slowly recovering 4.5 -> 6.0 bar abs while still working hard.
        for (var t = 0f; t < 180f; t += Dt)
        {
            shock.Step(Dt, Inputs(temp, working: true));
            Assert.False(shock.WarmingTooFast);
            Assert.Equal(0f, shock.Stress);
            Assert.Equal(0f, shock.Wear);
            temp = Math.Min(160f, temp + 0.25f * Dt);
        }

        // Even if not flagged working, a steady or falling temperature never shocks.
        var stagnant = new BoilerThermalShock();
        for (var t = 0f; t < 180f; t += Dt)
        {
            stagnant.Step(Dt, Inputs(155f - 0.01f * t, working: false));
            Assert.False(stagnant.WarmingTooFast);
            Assert.Equal(0f, stagnant.Wear);
        }

        var advice = FiringAdvice.ForCoal(fire, 0f, 0.6f, injectors, boilerWarmingTooFast: shock.WarmingTooFast);
        Assert.DoesNotContain("Ease the fire", advice.Text);

        // Auto fireman on a working S282 with pressure sagged to 5.5 bar abs (155 C)
        // must keep shoveling hard over 120 s (matching non-thermal-shock baseline).
        float RunWorkingSagged(bool thermalShockEnabled)
        {
            var plate = new Footplate(FiremanMode.Road, coal: 40f, pressure: 5.5f, glass: 0.6f)
            {
                EngineSteam = 5.0f,
                Standing = false,
            };
            var man = plate.Fireman;
            var model = new BoilerThermalShock();
            var coalFired = 0f;
            for (var t = 0f; t < 120f; t += Footplate.Dt)
            {
                var sat = 100f + 7f * (plate.Pressure - 1f);
                model.Step(Footplate.Dt, Inputs(sat, working: true, enabled: thermalShockEnabled));
                Assert.False(model.WarmingTooFast);
                var r = plate.Readings;
                r.BoilerTemperature = sat;
                r.ThermalShockEnabled = thermalShockEnabled;
                r.WarmingTooFast = model.WarmingTooFast;
                var actions = man.Step(Footplate.Dt, plate.Fire, r);
                if (actions.Shovelful > 0f)
                {
                    coalFired += plate.Shovel.Deliver(plate.Fire, actions.Shovelful, actions.Aim);
                    plate.TenderCoal -= actions.Shovelful;
                }
                plate.Fire.Step(Footplate.Dt, new FireInputs
                {
                    AirFlow = plate.Air,
                    DoorOpening = actions.Door,
                    StartupMultiplier = 1f,
                    BoilerTemperature = sat,
                    Speed = 15f,
                    BoilerPressure = plate.Pressure,
                });
            }
            return coalFired;
        }

        var withShock = RunWorkingSagged(true);
        var withoutShock = RunWorkingSagged(false);
        Assert.True(withShock > 80f, $"fireman should keep shoveling hard on sagged working engine ({withShock:F1} kg)");
        Assert.InRange(withShock, withoutShock - 1f, withoutShock + 1f);
    }

    [Fact]
    public void WorkingOilEngineWithSaggedPressureFiresAtFullRateIdenticalToThermalShockOff()
    {
        // Regression test matching the S282 probe: a working oil-fired engine whose
        // boiler pressure has sagged below 5-6 bar (< 165 C) must NEVER be capped at
        // the 28% cold-boiler WarmUpValve limit, and must fire identically whether
        // BoilerThermalShock is enabled or disabled.
        (float finalValve, float oilBurned) RunWorkingOilSagged(bool thermalShockEnabled)
        {
            var oilFire = new OilFire(new FireboxParameters(200f, 100f, 0.6f));
            oilFire.PrepareFire();
            oilFire.Restore(true, OilFire.HotBrick, 60f, 0f);
            var man = new AutoOilFireman { Mode = FiremanMode.Road };
            var model = new BoilerThermalShock();
            var oilBurned = 0f;
            var finalValve = 0f;
            const float pressure = 5.5f; // sagged well below working pressure (~155 C sat, < 165 C)
            const float satTemp = 155f;
            // Already working hard with hot fire and open atomizer when pressure sags below 165 C.
            oilFire.Step(Dt, new OilFireInputs
            {
                FiringValve = 0.8f,
                Atomizer = 0.9f,
                Heater = 0.05f,
                BoilerPressure = pressure,
                BoilerTemperature = satTemp,
                AirFlow = 45f,
                TenderFuel = 10000f,
                StartupMultiplier = 1f,
            });
            for (var t = 0f; t < 120f; t += Dt)
            {
                model.Step(Dt, Inputs(satTemp, working: true, enabled: thermalShockEnabled));
                Assert.False(model.WarmingTooFast);
                Assert.Equal(0f, model.Wear);
                var r = new FiremanReadings
                {
                    Pressure = pressure,
                    SafetyValve = 20f,
                    Glass = 0.6f,
                    Work = 0.8f,
                    FullWorkSteam = 10f,
                    Standing = false,
                    Throttle = 0.8f,
                    TenderWater = 20000f,
                    TenderCoal = 10000f,
                    BoilerTemperature = satTemp,
                    ThermalShockEnabled = thermalShockEnabled,
                    WarmingTooFast = model.WarmingTooFast,
                };
                var actions = man.Step(Dt, oilFire, r);
                finalValve = actions.FiringValve;
                oilFire.Step(Dt, new OilFireInputs
                {
                    FiringValve = actions.FiringValve,
                    Atomizer = actions.Atomizer,
                    Heater = actions.Heater,
                    BoilerPressure = pressure,
                    BoilerTemperature = satTemp,
                    AirFlow = 45f * actions.Damper,
                    TenderFuel = 10000f,
                    StartupMultiplier = 1f,
                });
                oilBurned += oilFire.BurnRate * Dt;
            }
            return (finalValve, oilBurned);
        }

        var withShock = RunWorkingOilSagged(true);
        var withoutShock = RunWorkingOilSagged(false);
        Assert.True(withShock.finalValve > 0.7f,
            $"working oil fireman below 165 C must not be capped at 28% valve ({withShock.finalValve:P0})");
        Assert.Equal(withoutShock.finalValve, withShock.finalValve, 4);
        Assert.Equal(withoutShock.oilBurned, withShock.oilBurned, 4);
    }

    [Fact]
    public void SteamStartupSpeedupNormalizationProtectsNormalStartsAndMatchesForcedWear()
    {
        // Normal gentle fire (5 MW unboosted) at 25x speedup = 125 MW boosted.
        var gentle25x = new BoilerThermalShock();
        var temp = 25f;
        while (temp < 190f)
        {
            gentle25x.Step(0.02f, Inputs(temp, startup: 25f));
            Assert.False(gentle25x.WarmingTooFast);
            temp += 5e6f * 25f / S282Capacity * 0.02f;
        }
        Assert.Equal(0f, gentle25x.Wear);

        // Forced roaring fire (20 MW unboosted) should produce matching wear at 1x and 25x.
        float RunForced(float speedup, float stepDt)
        {
            var model = new BoilerThermalShock();
            var t = 25f;
            var rate = 20e6f * speedup / S282Capacity;
            while (t < 190f)
            {
                model.Step(stepDt, Inputs(t, startup: speedup));
                t += rate * stepDt;
            }
            return model.Wear;
        }

        var wear1x = RunForced(1f, 0.25f);
        var wear25x = RunForced(25f, 0.01f);
        Assert.True(wear1x > 0.12f);
        Assert.InRange(wear25x, wear1x * 0.95f, wear1x * 1.05f);
    }

    [Fact]
    public void DirectSteamingAtShedStandsIsSafeEvenAtRapidTemperatureRise()
    {
        var shock = new BoilerThermalShock();
        var temp = 30f;
        for (var t = 0f; t < 200f; t += Dt)
        {
            shock.Step(Dt, Inputs(temp, directSteaming: true));
            temp += 0.4f * Dt;
        }
        Assert.False(shock.WarmingTooFast);
        Assert.Equal(0f, shock.Stress);
        Assert.Equal(0f, shock.Wear);
    }

    [Fact]
    public void WarmedBoilerIsImmuneToColdStartThermalShock()
    {
        var shock = new BoilerThermalShock();
        var temp = BoilerThermalShock.WarmTemperature + 5f;
        for (var t = 0f; t < 300f; t += Dt)
        {
            shock.Step(Dt, Inputs(temp));
            temp += 0.4f * Dt;
        }
        Assert.False(shock.WarmingTooFast);
        Assert.Equal(0f, shock.Wear);
    }

    [Fact]
    public void RelaxedOffSettingIsANoOpAndDisablesSavedWearEffects()
    {
        var shock = new BoilerThermalShock();
        shock.Restore(0.45f);
        Assert.True(shock.EfficiencyLoss > 0f);
        Assert.True(shock.LeakRate(15f) > 0f);

        var temp = 30f;
        for (var t = 0f; t < 300f; t += Dt)
        {
            shock.Step(Dt, Inputs(temp, enabled: false));
            temp += 0.4f * Dt;
        }
        Assert.False(shock.WarmingTooFast);
        Assert.Equal(0f, shock.Stress);
        Assert.Equal(0.45f, shock.Wear, 4);
        Assert.Equal(0f, shock.EfficiencyLoss);
        Assert.Equal(0f, shock.LeakRate(15f));
    }

    [Fact]
    public void RepairRestoreAndMirrorHandleWearCleanly()
    {
        var shock = new BoilerThermalShock();
        shock.Restore(0.4f);
        Assert.Equal(0.4f, shock.Wear, 4);
        shock.Repair(0.5f);
        Assert.Equal(0.2f, shock.Wear, 4);
        shock.Repair(1f);
        Assert.Equal(0f, shock.Wear);

        shock.Restore(float.NaN);
        Assert.Equal(0f, shock.Wear);
        shock.Restore(2.5f);
        Assert.Equal(1f, shock.Wear);

        shock.Mirror(0.35f, 0.6f, true);
        Assert.Equal(0.35f, shock.Wear, 4);
        Assert.Equal(0.6f, shock.Stress, 4);
        Assert.True(shock.WarmingTooFast);
    }

    [Fact]
    public void CoalAndOilFiremenKeepColdWarmUpGentleAndBackOffWhenEnabledAndKeepOldBehaviorWhenOff()
    {
        var coalFire = FireTestKit.BurningFire(Footplate.S282, 20f);
        var coalMan = new AutoFireman { Mode = FiremanMode.Road };
        var coldReading = new FiremanReadings
        {
            Pressure = 2f, SafetyValve = 15f, Glass = 0.6f, Standing = true,
            TenderWater = 10000f, TenderCoal = 5000f, ShovelMass = 3f, FullWorkSteam = 6f,
            AuxiliarySteam = 0.8f, HasStoker = true, StokerFullFeed = 1.56f,
            BoilerTemperature = 60f, StartupMultiplier = 10f, ThermalShockEnabled = true,
        };
        var gentle = coalMan.Step(0.1f, coalFire, coldReading);
        Assert.True(gentle.Blower < 0.5f, $"cold warm-up should not blast full blower ({gentle.Blower:F2})");
        Assert.False(coalMan.Stoking);
        var gentleTarget = coalMan.CoalTarget;

        coldReading.WarmingTooFast = true;
        var backedOff = coalMan.Step(0.1f, coalFire, coldReading);
        Assert.True(backedOff.Blower <= 0.15f, $"warming too fast should drop blower ({backedOff.Blower:F2})");
        Assert.True(backedOff.Damper <= 0.3f, $"warming too fast should check damper ({backedOff.Damper:F2})");
        Assert.Equal(0f, backedOff.Shovelful);

        // With ThermalShockEnabled = false (Relaxed / toggle off), cold-boiler and
        // warming-too-fast branches are completely bypassed, keeping standing
        // auxiliary-steam allowance, full raising blower, and boosted stoking.
        var relaxedCoalMan = new AutoFireman { Mode = FiremanMode.Road };
        var offReading = coldReading;
        offReading.ThermalShockEnabled = false;
        var relaxedActions = relaxedCoalMan.Step(0.1f, coalFire, offReading);
        Assert.Equal(1f, relaxedActions.Blower, 3);
        Assert.Equal(1f, relaxedActions.Damper, 3);
        Assert.True(relaxedCoalMan.Stoking, "toggle-off should retain boosted standing stoker behavior");
        Assert.True(relaxedCoalMan.CoalTarget > gentleTarget + 5f,
            $"toggle-off should retain standing auxiliary-steam allowance ({relaxedCoalMan.CoalTarget:F1} vs {gentleTarget:F1})");

        // Oil fireman keeps firing valve gentle on a cold boiler and cuts it back when warming too fast,
        // and retains previous uncapped behavior when ThermalShockEnabled is false.
        var oilFire = new OilFire(new FireboxParameters(200f, 100f, 0.6f));
        oilFire.PrepareFire();
        oilFire.Restore(true, OilFire.HotBrick, 60f, 0f);
        oilFire.Step(0.1f, new OilFireInputs
        {
            FiringValve = 0.25f, Atomizer = 0.5f, BoilerPressure = 10f, BoilerTemperature = 180f,
            AirFlow = 4f, TenderFuel = 5000f, StartupMultiplier = 1f,
        });
        Assert.True(oilFire.FireOn);
        var oilMan = new AutoOilFireman { Mode = FiremanMode.Road };
        var oilCold = coldReading;
        oilCold.WarmingTooFast = false;
        var oilGentle = oilMan.Step(0.1f, oilFire, oilCold);
        Assert.InRange(oilGentle.FiringValve, 0.05f, 0.3f);
        Assert.True(oilGentle.Blower <= 0.3f, $"cold oil warm-up should cap blower ({oilGentle.Blower:F2})");

        oilCold.WarmingTooFast = true;
        var oilSlow = oilMan.Step(0.1f, oilFire, oilCold);
        Assert.InRange(oilSlow.FiringValve, 0.03f, 0.15f);
        Assert.True(oilSlow.FiringValve < oilGentle.FiringValve);

        var relaxedOilMan = new AutoOilFireman { Mode = FiremanMode.Road };
        var oilOff = oilCold;
        oilOff.ThermalShockEnabled = false;
        var oilRelaxed = relaxedOilMan.Step(0.1f, oilFire, oilOff);
        Assert.True(oilRelaxed.Blower >= 0.55f, $"toggle-off oil fireman should keep raising blower ({oilRelaxed.Blower:F2})");
        Assert.True(oilRelaxed.FiringValve > oilGentle.FiringValve,
            $"toggle-off oil fireman should not cap cold firing valve ({oilRelaxed.FiringValve:F2} vs {oilGentle.FiringValve:F2})");
    }
}
