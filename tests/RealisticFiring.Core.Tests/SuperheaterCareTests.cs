using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

/// <summary>
/// Superheater element care: working steam keeps elements cool, normal standing
/// fires on big superheated engines (Hudson, 2901, FEF-3) stay safely below the
/// damage threshold with zero wear, shutting off on a white-hot forced fire
/// without steam flow cooks/scales them and degrades superheat (and cylinder
/// economy), drifting valve or cracked throttle or closing the damper protects
/// them, repair restores them, Relaxed-off disables every effect and fireman
/// branch while preserving pre-PR standing superheat, and advice/firemen handle
/// overheating cleanly.
/// </summary>
public class SuperheaterCareTests
{
    private const float Dt = 0.25f;
    private const float SatTemp14Bar = 195f;

    private static SuperheaterSpec TypeASpec() => new()
    {
        Kind = "Type A", FullSuperheat = 136f, HalfSuperheat = 98f, StandingSuperheat = 400f, TimeConstant = 75f,
    };

    private static SuperheaterSpec HudsonSpec() => new()
    {
        Kind = "Type E", FullSuperheat = 175f, HalfSuperheat = 175f * 0.82f, StandingSuperheat = 400f, TimeConstant = 75f,
    };

    private static SuperheaterSpec SantaFe2901Spec() => new()
    {
        Kind = "Type E", FullSuperheat = 180f, HalfSuperheat = 180f * 0.82f, StandingSuperheat = 400f, TimeConstant = 75f,
    };

    private static SuperheaterSpec Fef3Spec() => new()
    {
        Kind = "Type A", FullSuperheat = 158f, HalfSuperheat = 158f * 0.75f, StandingSuperheat = 400f, TimeConstant = 75f,
    };

    private static SuperheaterCareInputs Inputs(float fire, float flow, float throttle, float driftingValve = 0f,
        bool careEnabled = true, float satTemp = SatTemp14Bar, float startup = 1f) => new()
    {
        Fire = fire,
        Flow = flow,
        Quality = 1f,
        SaturationTemperature = satTemp,
        Throttle = throttle,
        DriftingValve = driftingValve,
        StartupMultiplier = startup,
        CareEnabled = careEnabled,
    };

    private static void Run(Superheater sh, SuperheaterCareInputs inputs, float seconds)
    {
        for (var t = 0f; t < seconds; t += Dt)
            sh.Step(Dt, inputs);
    }

    [Fact]
    public void WorkingWithSteamFlowKeepsElementsCoolAndSound()
    {
        var sh = new Superheater(TypeASpec());
        Run(sh, Inputs(fire: 1f, flow: 1f, throttle: 1f), 600f);
        Assert.InRange(sh.ElementTemperature, 320f, 345f);
        Assert.False(sh.Overheating);
        Assert.False(sh.ApproachingOverheat);
        Assert.Equal(0f, sh.Wear);
        Assert.Equal(1f, sh.Health);
        Assert.Equal(136f, sh.Superheat, 0);
    }

    [Theory]
    [InlineData("Hudson", 175f, 0.82f, 213f)]
    [InlineData("2901", 180f, 0.82f, 216f)]
    [InlineData("FEF-3", 158f, 0.75f, 216.5f)]
    public void BigSuperheatedEnginesTakeZeroWearStandingAt40And50PercentFireAndWearAtWhiteHotFire(
        string name, float fullSuperheat, float halfShare, float satTemp)
    {
        var spec = new SuperheaterSpec
        {
            Kind = halfShare > 0.8f ? "Type E" : "Type A",
            FullSuperheat = fullSuperheat,
            HalfSuperheat = fullSuperheat * halfShare,
            StandingSuperheat = 400f,
            TimeConstant = 75f,
        };

        // 40% and 50% standing fire with throttle shut for 10 minutes: ZERO wear and well below 440 C.
        foreach (var standingFire in new[] { 0.40f, 0.50f })
        {
            var sh = new Superheater(spec);
            Run(sh, Inputs(fire: standingFire, flow: 0f, throttle: 0f, satTemp: satTemp), 600f);
            Assert.True(sh.ElementTemperature < 440f,
                $"{name} at {standingFire:P0} standing fire reached {sh.ElementTemperature:F1} C");
            Assert.False(sh.Overheating, $"{name} should not overheat at {standingFire:P0} standing fire");
            Assert.False(sh.ApproachingOverheat, $"{name} should not warn at {standingFire:P0} standing fire");
            Assert.Equal(0f, sh.Wear);
        }

        // 85% and 100% white-hot fire with throttle shut and damper open: DOES overheat and accumulate wear.
        foreach (var hotFire in new[] { 0.85f, 1.00f })
        {
            var hot = new Superheater(spec);
            Run(hot, Inputs(fire: 1f, flow: 1f, throttle: 1f, satTemp: satTemp), 10f);
            Run(hot, Inputs(fire: hotFire, flow: 0f, throttle: 0f, satTemp: satTemp), 600f);
            Assert.True(hot.ElementTemperature > Superheater.SafeElementTemperature,
                $"{name} at {hotFire:P0} white-hot shut-off fire should exceed safe limit ({hot.ElementTemperature:F1} C)");
            Assert.True(hot.Overheating);
            Assert.True(hot.Wear > 0.1f, $"{name} at {hotFire:P0} white-hot shut-off fire should accumulate wear ({hot.Wear:F3})");
        }

        // Prevented on a 100% fire by drifting valve, cracked throttle, or closing the damper (easing fire).
        var withDrifting = new Superheater(spec);
        Run(withDrifting, Inputs(fire: 1f, flow: 1f, throttle: 1f, satTemp: satTemp), 10f);
        Run(withDrifting, Inputs(fire: 1f, flow: 0f, throttle: 0f, driftingValve: 0.5f, satTemp: satTemp), 600f);
        Assert.False(withDrifting.Overheating);
        Assert.Equal(0f, withDrifting.Wear);

        var withCracked = new Superheater(spec);
        Run(withCracked, Inputs(fire: 1f, flow: 1f, throttle: 1f, satTemp: satTemp), 10f);
        Run(withCracked, Inputs(fire: 1f, flow: 0.02f, throttle: 0.05f, satTemp: satTemp), 600f);
        Assert.False(withCracked.Overheating);
        Assert.Equal(0f, withCracked.Wear);

        var withDamperClosed = new Superheater(spec);
        Run(withDamperClosed, Inputs(fire: 1f, flow: 1f, throttle: 1f, satTemp: satTemp), 10f);
        Run(withDamperClosed, Inputs(fire: 0.35f, flow: 0f, throttle: 0f, satTemp: satTemp), 600f);
        Assert.False(withDamperClosed.Overheating);
        Assert.Equal(0f, withDamperClosed.Wear);
    }

    [Fact]
    public void ShutOffOnAHotFireOverheatsCooksElementsAndDegradesSuperheatAndCylinderEconomy()
    {
        var sh = new Superheater(TypeASpec());
        Run(sh, Inputs(fire: 1f, flow: 1f, throttle: 1f), 10f);
        var cleanSuperheat = sh.Superheat;

        // Shut off completely on a roaring fire for 10 minutes.
        Run(sh, Inputs(fire: 1f, flow: 0f, throttle: 0f), 600f);
        Assert.True(sh.ElementTemperature > Superheater.SafeElementTemperature + 50f, $"temp {sh.ElementTemperature:F0} C");
        Assert.True(sh.Overheating);
        Assert.True(sh.Wear > 0.2f, $"wear {sh.Wear:F3}");
        Assert.True(sh.Health < 0.8f);

        // Return to steady full work: cooked elements deliver noticeably less superheat.
        Run(sh, Inputs(fire: 1f, flow: 1f, throttle: 1f), 600f);
        Assert.False(sh.Overheating);
        Assert.True(sh.Superheat < cleanSuperheat - 15f, $"degraded {sh.Superheat:F1} vs clean {cleanSuperheat:F1}");

        // Lower superheat increases cylinder steam consumption per stroke.
        var cleanCyl = new SteamCylinders(new CylinderSpec(), 0.61f, 0.711f, 6f);
        var wornCyl = new SteamCylinders(new CylinderSpec(), 0.61f, 0.711f, 6f);
        cleanCyl.Set(0.25f, 3f, 14f, SatTemp14Bar + cleanSuperheat);
        wornCyl.Set(0.25f, 3f, 14f, SatTemp14Bar + sh.Superheat);
        static float SteamVol(float p) => 1694f / Math.Max(0.1f, p);
        static float SteamPress(float v) => 1694f / Math.Max(1f, v);
        var cleanAdmit = cleanCyl.Admit(13f, 5f, 1000f, 208f, 2f, SteamVol, SteamPress);
        var wornAdmit = wornCyl.Admit(13f, 5f, 1000f, 208f, 2f, SteamVol, SteamPress);
        Assert.True(wornAdmit.Mass > cleanAdmit.Mass, $"worn steam {wornAdmit.Mass:F4} should exceed clean {cleanAdmit.Mass:F4}");
    }

    [Fact]
    public void DriftingValveAndCrackedThrottleAndDampedFireAllPreventOverheating()
    {
        var withDriftingValve = new Superheater(TypeASpec());
        Run(withDriftingValve, Inputs(fire: 1f, flow: 1f, throttle: 1f), 10f);
        Run(withDriftingValve, Inputs(fire: 1f, flow: 0f, throttle: 0f, driftingValve: 0.5f), 600f);
        Assert.False(withDriftingValve.Overheating);
        Assert.True(withDriftingValve.ElementTemperature < Superheater.SafeElementTemperature);
        Assert.Equal(0f, withDriftingValve.Wear);

        var withCrackedThrottle = new Superheater(TypeASpec());
        Run(withCrackedThrottle, Inputs(fire: 1f, flow: 1f, throttle: 1f), 10f);
        Run(withCrackedThrottle, Inputs(fire: 1f, flow: 0.02f, throttle: 0.05f), 600f);
        Assert.False(withCrackedThrottle.Overheating);
        Assert.True(withCrackedThrottle.ElementTemperature < Superheater.SafeElementTemperature);
        Assert.Equal(0f, withCrackedThrottle.Wear);

        var withDampedFire = new Superheater(TypeASpec());
        Run(withDampedFire, Inputs(fire: 1f, flow: 1f, throttle: 1f), 10f);
        Run(withDampedFire, Inputs(fire: 0.15f, flow: 0f, throttle: 0f), 600f);
        Assert.False(withDampedFire.Overheating);
        Assert.True(withDampedFire.ElementTemperature < Superheater.SafeElementTemperature);
        Assert.Equal(0f, withDampedFire.Wear);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.15f)]
    [InlineData(0.35f)]
    public void EasingDampingOrDroppingTheFireCoolsCookingElementsInAboutAMinute(float fire)
    {
        // Game, job 3116: from 565 C the elements stayed over 480 C for 209 to 367 s, wearing 0.09 to 0.17 more.
        var sh = new Superheater(TypeASpec());
        Run(sh, Inputs(fire: 1f, flow: 1f, throttle: 1f), 10f);
        Run(sh, Inputs(fire: 1f, flow: 0f, throttle: 0f), 600f);
        Assert.True(sh.Overheating);
        var wear = sh.Wear;
        var above = 0f;
        for (var t = 0f; t < 300f; t += Dt)
        {
            sh.Step(Dt, Inputs(fire: fire, flow: 0f, throttle: 0f));
            if (sh.ElementTemperature >= Superheater.SafeElementTemperature)
                above += Dt;
        }
        // Within the smoothed fire's lag and the floor: 42 to 58 s here, against 230 to 300 s with no floor.
        Assert.True(above < 75f, $"over {Superheater.SafeElementTemperature} C for {above:F0} s");
        Assert.True(sh.Wear - wear < 0.045f, $"wore {sh.Wear - wear:F3} more");
        Assert.False(sh.Overheating);
    }

    [Fact]
    public void AFireDarkAFewSecondsInEachHalfMinuteHeatsTheElementsAsASteadyOneDoes()
    {
        // Game, job 3123: a bed re-laid every 30 s, out about 5 s each time, passed 480 C at 541 s against 326 when the
        // cooling floor followed the fire of the moment; it follows the fire over FireSmoothTime.
        float Heat(bool flicker, out float passedAt)
        {
            var sh = new Superheater(TypeASpec());
            Run(sh, Inputs(fire: 1f, flow: 1f, throttle: 1f), 10f);
            passedAt = float.NaN;
            for (var t = 0f; t < 650f; t += Dt)
            {
                var dark = flicker && t % 30f < 5f;
                sh.Step(Dt, Inputs(fire: dark ? 0f : 1f, flow: 0f, throttle: 0f));
                if (float.IsNaN(passedAt) && sh.ElementTemperature >= Superheater.SafeElementTemperature)
                    passedAt = t;
            }
            return sh.ElementTemperature;
        }
        var steady = Heat(false, out var steadyAt);
        var flickering = Heat(true, out var flickerAt);
        // The dark seconds are a sixth of the fire: some delay, the same as with no floor at all (232 s against 183).
        Assert.True(flickerAt < steadyAt * 1.35f, $"flickering passed {Superheater.SafeElementTemperature} C at {flickerAt:F0} s, steady at {steadyAt:F0} s");
        Assert.True(flickering > steady - 45f, $"flickering {flickering:F0} C, steady {steady:F0} C at 650 s");
    }

    [Fact]
    public void RepairedElementsComeInCoolAndDoNotWearAtOnce()
    {
        var sh = new Superheater(TypeASpec());
        Run(sh, Inputs(fire: 1f, flow: 1f, throttle: 1f), 10f);
        Run(sh, Inputs(fire: 1f, flow: 0f, throttle: 0f), 600f);
        Assert.True(sh.Wear > 0.2f);
        sh.Repair(1f);
        Run(sh, Inputs(fire: 1f, flow: 0f, throttle: 0f), 10f);
        Assert.Equal(0f, sh.Wear);
        Assert.True(sh.ElementTemperature < Superheater.SafeElementTemperature, $"{sh.ElementTemperature:F0} C");
    }

    [Fact]
    public void CareOffDisablesSavedWearAndPreservesPrePrStandingSuperheat()
    {
        var baseline = new Superheater(HudsonSpec());
        var withCareOff = new Superheater(HudsonSpec());
        withCareOff.Restore(0.65f);

        // Simulate cold-start / low-pressure standing fire with startup speedup (boosted raw fire = 1.5).
        for (var t = 0f; t < 120f; t += Dt)
        {
            baseline.Step(1.5f, 0f, 1f, Dt);
            withCareOff.Step(Dt, Inputs(fire: 1.5f, flow: 0f, throttle: 0f, careEnabled: false, satTemp: 150f, startup: 10f));
        }

        Assert.Equal(1f, withCareOff.SuperheatEfficiency);
        Assert.Equal(baseline.Elements, withCareOff.Elements, 3);
        Assert.Equal(baseline.Superheat, withCareOff.Superheat, 3);
        Assert.False(withCareOff.Overheating);
        Assert.False(withCareOff.ApproachingOverheat);
        Assert.False(withCareOff.WantsDamperCheck);
        Assert.Equal(0.65f, withCareOff.Wear, 4);

        var saturated = new Superheater(SuperheaterSpec.None);
        Run(saturated, Inputs(fire: 1f, flow: 0f, throttle: 0f, careEnabled: true), 600f);
        Assert.False(saturated.Overheating);
        Assert.Equal(0f, saturated.ElementTemperature);
        Assert.Equal(0f, saturated.Wear);
    }

    [Fact]
    public void StartupMultiplierProtectsColdStartElementsWithoutAlteringStandingSuperheat()
    {
        // A 40% fire boosted 10x by Steam Startup (raw fire = 4.0) should not cook
        // elements on a cold boiler, while leaving steam superheat unchanged.
        var sh = new Superheater(SantaFe2901Spec());
        var baseline = new Superheater(SantaFe2901Spec());
        for (var t = 0f; t < 120f; t += Dt)
        {
            baseline.Step(4.0f, 0f, 1f, Dt);
            sh.Step(Dt, Inputs(fire: 4.0f, flow: 0f, throttle: 0f, careEnabled: true, satTemp: 140f, startup: 10f));
        }
        Assert.Equal(0f, sh.Wear);
        Assert.False(sh.Overheating);
        Assert.Equal(baseline.Elements, sh.Elements, 3);
        Assert.Equal(baseline.Superheat, sh.Superheat, 3);
    }

    [Fact]
    public void RepairRestoreAndMirrorWorkCleanly()
    {
        var sh = new Superheater(TypeASpec());
        sh.Restore(0.6f);
        Assert.Equal(0.6f, sh.Wear, 4);
        Assert.Equal(0.4f, sh.Health, 4);
        sh.Repair(0.5f);
        Assert.Equal(0.3f, sh.Wear, 4);
        sh.Repair(1f);
        Assert.Equal(0f, sh.Wear);
        Run(sh, Inputs(fire: 1f, flow: 1f, throttle: 1f), 10f);
        Assert.Equal(136f, sh.Superheat, 0);

        sh.Mirror(250f, 180f, 510f, 0.4f, overheating: true, approaching: true);
        Assert.Equal(250f, sh.Elements, 3);
        Assert.Equal(180f, sh.Superheat, 3);
        Assert.Equal(510f, sh.ElementTemperature, 3);
        Assert.Equal(0.4f, sh.Wear, 3);
        Assert.True(sh.Overheating);
        Assert.True(sh.ApproachingOverheat);
    }

    [Fact]
    public void FiringAdviceWarnsForBoilerThermalShockAndHotSuperheater()
    {
        var fire = FireTestKit.BurningFire(Footplate.S282, 40f);
        var injectors = new[] { new InjectorReading(InjectorState.Working, 0.6f, true) };

        var shockAdvice = FiringAdvice.ForCoal(fire, 0f, 0.6f, injectors, boilerWarmingTooFast: true);
        Assert.Equal(AdviceLevel.Act, shockAdvice.Level);
        Assert.Contains("boiler warming too fast", shockAdvice.Text);

        var shValveAdvice = FiringAdvice.ForCoal(fire, 0f, 0.6f, injectors, driftingValve: true, superheaterHot: true);
        Assert.Equal(AdviceLevel.Act, shValveAdvice.Level);
        Assert.Contains("Superheater hot", shValveAdvice.Text);
        Assert.Contains("drifting valve", shValveAdvice.Text);

        var shDamperAdvice = FiringAdvice.ForCoal(fire, 0f, 0.6f, injectors, driftingValve: false, superheaterHot: true);
        Assert.Equal(AdviceLevel.Act, shDamperAdvice.Level);
        Assert.Contains("Superheater hot", shDamperAdvice.Text);
        Assert.Contains("crack throttle", shDamperAdvice.Text);

        var oilShock = FiringAdvice.ForOil(new OilReading
        {
            FireOn = true, BoilerPressure = 4f, GlassLevel = 0.6f, StackSmoke = 0.15f,
            Injectors = injectors, BoilerWarmingTooFast = true,
        });
        Assert.Equal(AdviceLevel.Act, oilShock.Level);
        Assert.Contains("boiler warming too fast", oilShock.Text);

        var oilSh = FiringAdvice.ForOil(new OilReading
        {
            FireOn = true, BoilerPressure = 15f, GlassLevel = 0.6f, StackSmoke = 0.15f,
            Injectors = injectors, SuperheaterHot = true,
        });
        Assert.Equal(AdviceLevel.Act, oilSh.Level);
        Assert.Contains("Superheater hot", oilSh.Text);
    }

    [Fact]
    public void AutomaticFiremenCloseDamperAndEaseFireWhenEnabledAndIgnoreWhenCareOff()
    {
        var coalFire = FireTestKit.BurningFire(Footplate.S282, 50f);
        var coalMan = new AutoFireman { Mode = FiremanMode.Road };
        var shutOffHot = new FiremanReadings
        {
            Pressure = 12f, SafetyValve = 15f, Glass = 0.6f, Standing = true, Throttle = 0f,
            TenderWater = 10000f, TenderCoal = 5000f, ShovelMass = 3f, FullWorkSteam = 6f,
            SuperheaterCareEnabled = true, SuperheaterHot = true,
        };
        var actions = coalMan.Step(0.1f, coalFire, shutOffHot);
        Assert.Equal(0f, actions.Damper);
        Assert.True(actions.Blower <= 0.15f);
        Assert.Equal(0f, actions.Shovelful);

        // With SuperheaterCareEnabled = false, fireman retains normal raising draft.
        var coalManOff = new AutoFireman { Mode = FiremanMode.Road };
        var shutOffCareDisabled = shutOffHot;
        shutOffCareDisabled.SuperheaterCareEnabled = false;
        var offActions = coalManOff.Step(0.1f, coalFire, shutOffCareDisabled);
        Assert.Equal(1f, offActions.Damper, 3);
        Assert.Equal(1f, offActions.Blower, 3);

        var oilFire = new OilFire(new FireboxParameters(200f, 100f, 0.6f));
        oilFire.PrepareFire();
        oilFire.Restore(true, OilFire.HotBrick, 60f, 0f);
        oilFire.Step(0.1f, new OilFireInputs
        {
            FiringValve = 0.25f, Atomizer = 0.5f, BoilerPressure = 12f, BoilerTemperature = 188f,
            AirFlow = 4f, TenderFuel = 5000f, StartupMultiplier = 1f,
        });
        Assert.True(oilFire.FireOn);
        var oilMan = new AutoOilFireman { Mode = FiremanMode.Road };
        var oilActions = oilMan.Step(0.1f, oilFire, shutOffHot);
        Assert.Equal(0f, oilActions.Blower);
        Assert.True(oilActions.Damper <= 0.25f);
        Assert.Equal(oilMan.LeastFire, oilActions.FiringValve, 3);

        var oilManOff = new AutoOilFireman { Mode = FiremanMode.Road };
        var oilOffActions = oilManOff.Step(0.1f, oilFire, shutOffCareDisabled);
        Assert.True(oilOffActions.Blower >= 0.55f);
        Assert.True(oilOffActions.FiringValve > oilManOff.LeastFire);
    }
}
