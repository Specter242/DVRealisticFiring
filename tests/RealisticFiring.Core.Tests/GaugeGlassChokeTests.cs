using System;
using System.Linq;
using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

/// <summary>
/// Choking gauge glass &amp; try cocks loop: deterministic choking from dirty
/// water impurities, sluggish/damped and stuck falsely-high readings,
/// blow-through drawdown and clear-vs-choked rebound, washout clearing, save
/// round-trip, Relaxed/impurities-off no-op, try cocks reading true level,
/// fireman's-words advice, and automatic fireman handling.
/// </summary>
public class GaugeGlassChokeTests
{
    private const float Bottom = 0.63f;
    private const float Top = 0.96f;
    private const float Working = 14f;
    private const float Dt = 0.1f;

    private static GaugeGlassChokeInputs Steaming(float surface, float ppm = 3500f, float sludgePpm = 1050f,
        float foam = 0.5f, float work = 0.8f, float pressure = Working, bool impurities = true,
        float bottomCock = 0f, float topCock = 0f, bool boiling = true) => new GaugeGlassChokeInputs
        {
            SurfaceLevel = surface,
            GlassBottom = Bottom,
            GlassTop = Top,
            ImpuritiesEnabled = impurities,
            FoamingConcentration = ppm,
            SuspendedSludgePpm = sludgePpm,
            Foam = foam,
            Work = work,
            Pressure = pressure,
            Boiling = boiling,
            BottomCock = bottomCock,
            TopCock = topCock,
        };

    private static void Run(GaugeGlassChoke glass, float seconds, Func<float, GaugeGlassChokeInputs> inputs)
    {
        for (var t = 0f; t < seconds; t += Dt)
            glass.Step(Dt, inputs(t));
    }

    [Fact]
    public void CleanWaterDoesNotChokeTheGlass()
    {
        var glass = new GaugeGlassChoke();
        // Even Bad feed fresh (800 ppm) is below ChokeOnsetPpm (1000 ppm).
        Run(glass, 1800f, _ => Steaming(0.80f, ppm: 800f, sludgePpm: 240f, foam: 0f));
        Assert.Equal(0f, glass.Choke);
        Assert.False(glass.IsSluggish);
        Assert.Equal(0.80f, glass.ShownLevel, 4);
    }

    [Fact]
    public void RelaxedOrImpuritiesOffNeverChokesAndStillAllowsBlowThrough()
    {
        var glass = new GaugeGlassChoke();
        Run(glass, 1200f, _ => Steaming(0.80f, ppm: 4500f, sludgePpm: 1350f, foam: 1f, impurities: false));
        Assert.Equal(0f, glass.Choke);
        Assert.Equal(0.80f, glass.ShownLevel, 4);
        Assert.Equal(GlassCheck.Clear, glass.Status(false, 4500f));

        // Blow-through drawdown still works when impurities are off.
        Run(glass, 0.8f, _ => Steaming(0.80f, impurities: false, bottomCock: 1f));
        Assert.True(glass.ShownLevel < 0.68f, $"expected drawdown, got {glass.ShownLevel:F3}");
        Assert.True(glass.Blowing);

        // Shuts cock -> crisp rebound back to true level.
        Run(glass, 3f, _ => Steaming(0.80f, impurities: false, bottomCock: 0f));
        Assert.False(glass.Blowing);
        Assert.Equal(0.80f, glass.ShownLevel, 3);
    }

    [Fact]
    public void DirtyAndFoamingWaterChokesDeterministicallyAndColdBoilerDoesNot()
    {
        var dirty = new GaugeGlassChoke();
        var foaming = new GaugeGlassChoke();
        var cold = new GaugeGlassChoke();

        Run(dirty, 1200f, _ => Steaming(0.80f, ppm: 2500f, sludgePpm: 750f, foam: 0f, work: 0.8f));
        Run(foaming, 1200f, _ => Steaming(0.80f, ppm: 3800f, sludgePpm: 1140f, foam: 0.7f, work: 0.8f));
        Run(cold, 1200f, _ => Steaming(0.80f, ppm: 4500f, sludgePpm: 1350f, foam: 1f, pressure: 1f, boiling: false));

        Assert.True(dirty.Choke > 0.05f && dirty.Choke < 0.30f, $"expected shift-paced dirty choke, got {dirty.Choke:F3}");
        Assert.True(foaming.Choke > dirty.Choke * 2f, $"foaming {foaming.Choke:F3} vs dirty {dirty.Choke:F3}");
        Assert.Equal(0f, cold.Choke);

        // Shift/hour pacing: ~60–90 min to full choke in dirty working water, ~30–40 min with active foaming.
        var dirtyFull = new GaugeGlassChoke();
        var dirtySeconds = 0f;
        while (dirtyFull.Choke < 1f && dirtySeconds < 7200f)
        {
            dirtyFull.Step(1f, Steaming(0.80f, ppm: 3800f, sludgePpm: 900f, foam: 0f, work: 0.8f));
            dirtySeconds += 1f;
        }
        Assert.InRange(dirtySeconds, 3600f, 5400f);

        var foamingFull = new GaugeGlassChoke();
        var foamingSeconds = 0f;
        while (foamingFull.Choke < 1f && foamingSeconds < 7200f)
        {
            foamingFull.Step(1f, Steaming(0.80f, ppm: 4000f, sludgePpm: 1000f, foam: 0.9f, work: 0.9f));
            foamingSeconds += 1f;
        }
        Assert.InRange(foamingSeconds, 1800f, 2500f);

        // Identical runs produce identical choke (never random).
        var replay = new GaugeGlassChoke();
        Run(replay, 1200f, _ => Steaming(0.80f, ppm: 3800f, sludgePpm: 1140f, foam: 0.7f, work: 0.8f));
        Assert.Equal(foaming.Choke, replay.Choke, 6);
    }

    [Fact]
    public void PartialChokeMakesTheGlassSluggishAndDampsSwellAndSurge()
    {
        var clear = new GaugeGlassChoke();
        var sluggish = new GaugeGlassChoke();
        clear.Restore(0f, 0.80f, 0f);
        sluggish.Restore(0.45f, 0.80f, 200f);

        // Oscillating surface (swell / foam surge) is damped by a sluggish glass.
        float clearMin = 1f, clearMax = 0f, slugMin = 1f, slugMax = 0f;
        Run(clear, 10f, t =>
        {
            var s = 0.80f + 0.03f * (float)Math.Sin(t * 2.0 * Math.PI * 0.7);
            var inp = Steaming(s, ppm: 900f, sludgePpm: 0f, foam: 0f);
            return inp;
        });
        for (var t = 0f; t < 10f; t += Dt)
        {
            var s = 0.80f + 0.03f * (float)Math.Sin(t * 2.0 * Math.PI * 0.7);
            clear.Step(Dt, Steaming(s, ppm: 900f, sludgePpm: 0f, foam: 0f));
            sluggish.Step(Dt, Steaming(s, ppm: 900f, sludgePpm: 0f, foam: 0f));
            clearMin = Math.Min(clearMin, clear.ShownLevel);
            clearMax = Math.Max(clearMax, clear.ShownLevel);
            slugMin = Math.Min(slugMin, sluggish.ShownLevel);
            slugMax = Math.Max(slugMax, sluggish.ShownLevel);
        }
        Assert.True((slugMax - slugMin) < 0.25f * (clearMax - clearMin),
            $"sluggish swing {slugMax - slugMin:F4} should be much smaller than clear {clearMax - clearMin:F4}");

        // Step drop lags noticeably behind the true level.
        Run(sluggish, 10f, _ => Steaming(0.70f, ppm: 900f, sludgePpm: 0f, foam: 0f));
        Assert.True(sluggish.ShownLevel > 0.75f, $"expected sluggish glass to lag above 0.75, got {sluggish.ShownLevel:F3}");
    }

    [Fact]
    public void HeavyChokeSticksAndReadsFalselyHighWhileTrueWaterDropsAndTryCocksTellTheTruth()
    {
        var glass = new GaugeGlassChoke();
        glass.Restore(0.95f, 0.82f, 600f);
        Assert.True(glass.IsStuck);

        // True boiler water drops dangerously near the crown sheet (0.63).
        const float trueSurface = 0.64f;
        Run(glass, 120f, _ => Steaming(trueSurface, ppm: 3800f, sludgePpm: 1100f, foam: 0.4f));

        // The stuck glass stays falsely high (and condensation even creeps it up slightly).
        Assert.True(glass.ShownLevel >= 0.82f, $"stuck glass should stay falsely high (>= 0.82), got {glass.ShownLevel:F3}");

        // Meanwhile the try cocks tap the boiler directly at the true surface (0.64):
        // S282 top (0.900) and middle (0.761) blow dry steam; bottom (0.621) blows water!
        var topCock = TryCock.Flow(1f, trueSurface - 0.900f, 0.4f, Working, 195f);
        var midCock = TryCock.Flow(1f, trueSurface - 0.761f, 0.4f, Working, 195f);
        var botCock = TryCock.Flow(1f, trueSurface - 0.621f, 0.4f, Working, 195f);
        Assert.True(topCock.Steam > 0f && topCock.Water == 0f, "top try cock must blow dry steam");
        Assert.True(midCock.Steam > 0f && midCock.Water == 0f, "middle try cock must blow dry steam even though glass reads 0.82+");
        Assert.True(botCock.Water > 0f, "bottom try cock at 0.621 must still find water at 0.64");
    }

    [Fact]
    public void FullChokeStallsTrappedColumnEvenWhenTrueWaterRises()
    {
        // Stuck low at full choke (1.0): even when true water rises sharply, the blocked
        // lower passage does not track upward — only slow upper-connection steam condensation creeps it.
        var boiling = new GaugeGlassChoke();
        boiling.Restore(1.0f, 0.66f, 2000f);
        Run(boiling, 120f, _ => Steaming(0.90f, ppm: 3800f, sludgePpm: 1100f, foam: 0.4f));
        var expectedCreep = GaugeGlassChoke.CondenseRate * 120f;
        Assert.InRange(boiling.ColumnLevel, 0.66f + expectedCreep - 0.001f, 0.66f + expectedCreep + 0.001f);

        // When not boiling (no steam condensing), a fully choked column stays strictly frozen.
        var cold = new GaugeGlassChoke();
        cold.Restore(1.0f, 0.66f, 2000f);
        Run(cold, 120f, _ => Steaming(0.90f, ppm: 3800f, sludgePpm: 1100f, foam: 0.4f, boiling: false));
        Assert.Equal(0.66f, cold.ColumnLevel, 5);
    }

    [Fact]
    public void BlowingThroughClearsChokingAndReboundsSharplyWhenClearVsSluggishlyWhenChoked()
    {
        // 1. Compare rebound speed: clear glass vs choked glass after a drawdown.
        var clear = new GaugeGlassChoke();
        var choked = new GaugeGlassChoke();
        clear.Restore(0f, 0.85f, 0f);
        choked.Restore(0.9f, 0.85f, 500f);

        // Draw clear down at working pressure, and draw choked down without clearing (low pressure) to compare rebound.
        Run(clear, 0.8f, _ => Steaming(0.85f, ppm: 900f, sludgePpm: 0f, foam: 0f, bottomCock: 1f));
        Run(choked, 3.0f, _ => Steaming(0.85f, ppm: 900f, sludgePpm: 0f, foam: 0f, pressure: 1.01325f, bottomCock: 1f));
        Assert.True(choked.Choke > 0.85f, "unpressurized blow should not clear the plug");
        var clearStart = clear.ShownLevel;
        var chokedStart = choked.ShownLevel;

        // Release both cocks for 1 second: clear rebounds sharply, choked creeps slowly.
        Run(clear, 1.0f, _ => Steaming(0.85f, ppm: 900f, sludgePpm: 0f, foam: 0f, bottomCock: 0f));
        Run(choked, 1.0f, _ => Steaming(0.85f, ppm: 900f, sludgePpm: 0f, foam: 0f, bottomCock: 0f));
        var clearFraction = (clear.ShownLevel - clearStart) / (0.85f - clearStart);
        var chokedFraction = (choked.ShownLevel - chokedStart) / (0.85f - chokedStart);
        Assert.True(clearFraction > 0.80f && chokedFraction < 0.35f,
            $"clear rebounded {clearFraction:P0} in 1 s vs choked {chokedFraction:P0}");

        // 2. Full pressurized blow-through clears a stuck glass back to true low level.
        var stuck = new GaugeGlassChoke();
        stuck.Restore(1f, 0.85f, 800f);
        const float trueLow = 0.66f;
        Run(stuck, 2.5f, _ => Steaming(trueLow, ppm: 3800f, sludgePpm: 1100f, foam: 0.5f, bottomCock: 1f));
        Assert.Equal(0f, stuck.Choke);
        Assert.Equal(0f, stuck.TimeSinceBlowThrough);

        // Shut cock -> snaps sharply to trueLow (0.66), NOT the old false 0.85.
        Run(stuck, 3.0f, _ => Steaming(trueLow, ppm: 3800f, sludgePpm: 1100f, foam: 0.5f, bottomCock: 0f));
        Assert.False(stuck.Blowing);
        Assert.InRange(stuck.ShownLevel, trueLow - 0.005f, trueLow + 0.005f);
    }

    [Fact]
    public void BlowingClearsPromptlyWhenColumnIsRisingDuringRebound()
    {
        // Regression: blowing a clear glass while the boiler is being filled (ColumnLevel rising at 0.003/s
        // on 20 ms ticks) must still clear Blowing within a few seconds once cocks close.
        var glass = new GaugeGlassChoke();
        var level = 0.75f;
        const float tick = 0.02f;
        const float risePerSec = 0.003f;

        // Open bottom cock to draw down.
        for (var t = 0f; t < 0.8f; t += tick)
        {
            level += risePerSec * tick;
            glass.Step(tick, Steaming(level, ppm: 800f, sludgePpm: 0f, foam: 0f, bottomCock: 1f));
        }
        Assert.True(glass.Blowing);
        Assert.True(glass.ShownLevel < level - 0.05f, $"expected drawdown below {level:F3}, got {glass.ShownLevel:F3}");

        // Close cocks while boiler water continues rising at 0.003/s on 20 ms ticks.
        var elapsed = 0f;
        while (glass.Blowing && elapsed < 10f)
        {
            level += risePerSec * tick;
            glass.Step(tick, Steaming(level, ppm: 800f, sludgePpm: 0f, foam: 0f, bottomCock: 0f));
            elapsed += tick;
        }
        Assert.False(glass.Blowing, $"Blowing should clear within a few seconds while column rises, still active after {elapsed:F2} s");
        Assert.True(elapsed < 4f, $"expected clear glass Blowing to settle in < 4 s, took {elapsed:F2} s");
        Assert.Equal(level, glass.ShownLevel, 4);
    }

    [Fact]
    public void WashoutAndSaveRestoreRoundTripCleanly()
    {
        var glass = new GaugeGlassChoke();
        glass.Restore(0.82f, 0.84f, 412f);
        Assert.Equal(0.82f, glass.Choke, 4);
        Assert.Equal(0.84f, glass.ColumnLevel, 4);
        Assert.Equal(412f, glass.TimeSinceBlowThrough, 2);

        // Washout clears everything and re-snaps to the new surface on next tick.
        glass.Reset();
        Assert.Equal(0f, glass.Choke);
        Assert.Equal(0f, glass.TimeSinceBlowThrough);
        Assert.False(glass.HasLevel);
        glass.Step(Dt, Steaming(0.75f, ppm: 350f, sludgePpm: 105f, foam: 0f));
        Assert.Equal(0.75f, glass.ShownLevel, 4);

        // Bad/NaN values in Restore are sanitized safely.
        glass.Restore(float.NaN, float.PositiveInfinity, -10f);
        Assert.Equal(0f, glass.Choke);
        Assert.False(glass.HasLevel);
        Assert.Equal(0f, glass.TimeSinceBlowThrough);
    }

    [Fact]
    public void AdviceWarnsInFiremansWordsWhenGlassIsDueSluggishOrStuck()
    {
        var fire = new FireModel(new FireboxParameters(150f, 120f, 0.5f));
        var data = new float[GrateLayout.ZoneCount * 4];
        for (var i = 0; i < GrateLayout.ZoneCount; i++)
            data[i * 4 + 1] = 10f;
        fire.RestoreZones(true, data);
        var feeding = new[] { new InjectorReading(InjectorState.Working, 0.6f, true) };

        var stuck = FiringAdvice.ForCoal(fire, 0.1f, 0.7f, feeding, glassCheck: GlassCheck.Stuck);
        Assert.Equal(AdviceLevel.Act, stuck.Level);
        Assert.Contains("try cocks", stuck.Text);
        Assert.Contains("blow the glass through", stuck.Text);

        // Stuck-low glass (below LowGlass): must warn not to trust the glass and to check try cocks,
        // NOT tell the player to add more water (the real level may already have recovered).
        var stuckLowCoal = FiringAdvice.ForCoal(fire, 0.1f, FiringAdvice.LowGlass - 0.15f, feeding, glassCheck: GlassCheck.Stuck);
        Assert.Equal(AdviceLevel.Act, stuckLowCoal.Level);
        Assert.Contains("try cocks", stuckLowCoal.Text);
        Assert.Contains("blow the glass through", stuckLowCoal.Text);
        Assert.DoesNotContain("injector on", stuckLowCoal.Text);

        var stuckLowOilLit = FiringAdvice.ForOil(new OilReading
        {
            FireOn = true,
            BoilerPressure = 14f,
            GlassLevel = FiringAdvice.LowGlass - 0.15f,
            GlassCheck = GlassCheck.Stuck,
            StackSmoke = 0.15f,
            Injectors = feeding,
        });
        Assert.Equal(AdviceLevel.Act, stuckLowOilLit.Level);
        Assert.Contains("try cocks", stuckLowOilLit.Text);
        Assert.DoesNotContain("injector on", stuckLowOilLit.Text);

        var stuckLowOilCold = FiringAdvice.ForOil(new OilReading
        {
            FireOn = false,
            BoilerPressure = 14f,
            GlassLevel = FiringAdvice.LowGlass - 0.15f,
            GlassCheck = GlassCheck.Stuck,
            Injectors = feeding,
        });
        Assert.Equal(AdviceLevel.Act, stuckLowOilCold.Level);
        Assert.Contains("try cocks", stuckLowOilCold.Text);
        Assert.DoesNotContain("fill her before you light", stuckLowOilCold.Text);

        var sluggish = FiringAdvice.ForCoal(fire, 0.1f, 0.7f, feeding, glassCheck: GlassCheck.Sluggish);
        Assert.Equal(AdviceLevel.Watch, sluggish.Level);
        Assert.Contains("sluggishly", sluggish.Text);
        Assert.Contains("try cocks", sluggish.Text);

        var due = FiringAdvice.ForCoal(fire, 0.1f, 0.7f, feeding, glassCheck: GlassCheck.Due);
        Assert.Equal(AdviceLevel.Watch, due.Level);
        Assert.Contains("bottom cock", due.Text);

        var clear = FiringAdvice.ForCoal(fire, 0.1f, 0.7f, feeding, glassCheck: GlassCheck.Clear);
        Assert.Equal(AdviceLevel.Ok, clear.Level);

        // GaugeGlassChoke.Status transitions cleanly, and gates blow-through advice on steam pressure.
        var choke = new GaugeGlassChoke();
        Assert.Equal(GlassCheck.Clear, choke.Status(true, 350f));
        choke.Restore(0.1f, 0.8f, GaugeGlassChoke.BlowReminderTime + 10f);
        Assert.Equal(GlassCheck.Due, choke.Status(true, 1500f));
        Assert.Equal(GlassCheck.Clear, choke.Status(true, 1500f, 1.01325f));
        Assert.Equal(GlassCheck.Clear, choke.Status(false, 1500f));
        choke.Restore(GaugeGlassChoke.SluggishThreshold, 0.8f, 10f);
        Assert.Equal(GlassCheck.Sluggish, choke.Status(true, 1500f));
        Assert.Equal(GlassCheck.Clear, choke.Status(true, 1500f, 1.01325f));
        choke.Restore(GaugeGlassChoke.StuckThreshold, 0.8f, 10f);
        Assert.Equal(GlassCheck.Stuck, choke.Status(true, 1500f));
        Assert.Equal(GlassCheck.Clear, choke.Status(true, 1500f, 1.01325f));
        Assert.Equal(GlassCheck.Stuck, choke.Status(true, 1500f, GaugeGlassChoke.BlowMinPressure));
    }

    [Fact]
    public void ColdUnlitEngineWithSavedStuckGlassShowsLightingAdviceUntilPressureRises()
    {
        var coldCoal = new FireModel(new FireboxParameters(150f, 120f, 0.5f));
        var noInjectors = Array.Empty<InjectorReading>();
        var savedStuck = new GaugeGlassChoke();
        savedStuck.Restore(0.92f, 0.80f, 600f);
        Assert.True(savedStuck.IsStuck);

        // Cold coal engine (1.0 bar abs, below BlowMinPressure): neither Status nor ForCoal traps
        // the player with unactionable "blow the glass through" advice; lighting advice runs normally.
        Assert.Equal(GlassCheck.Clear, savedStuck.Status(true, 3500f, 1.0f));
        var coalColdDirect = FiringAdvice.ForCoal(coldCoal, 0f, 0.7f, noInjectors,
            glassCheck: GlassCheck.Stuck, boilerPressure: 1.0f);
        Assert.Contains("wood and a rag", coalColdDirect.Text);
        Assert.DoesNotContain("blow the glass through", coalColdDirect.Text);

        var coalColdLighter = FiringAdvice.ForCoal(coldCoal, 0f, 0.7f, noInjectors,
            lighterLightsCoal: true, glassCheck: GlassCheck.Stuck, boilerPressure: 1.0f);
        Assert.Contains("shovel in some coal and light it", coalColdLighter.Text);
        Assert.DoesNotContain("blow the glass through", coalColdLighter.Text);

        // Cold oil engine (1.0 bar abs, below BlowMinPressure): lighting steps still show even with saved Stuck glass.
        var oilCold = FiringAdvice.ForOil(new OilReading
        {
            FireOn = false,
            BoilerPressure = 1.0f,
            GlassLevel = 0.7f,
            GlassCheck = GlassCheck.Stuck,
            Injectors = noInjectors,
        });
        Assert.Equal(1, oilCold.LightingStep);
        Assert.Contains("no steam for the burner", oilCold.Text);
        Assert.DoesNotContain("blow the glass through", oilCold.Text);

        // Once boiler pressure reaches BlowMinPressure, Stuck advice takes precedence even below LowGlass.
        Assert.Equal(GlassCheck.Stuck, savedStuck.Status(true, 3500f, GaugeGlassChoke.BlowMinPressure));
        var coalSteamedLow = FiringAdvice.ForCoal(coldCoal, 0f, FiringAdvice.LowGlass - 0.15f, noInjectors,
            glassCheck: GlassCheck.Stuck, boilerPressure: GaugeGlassChoke.BlowMinPressure);
        Assert.Equal(AdviceLevel.Act, coalSteamedLow.Level);
        Assert.Contains("try cocks", coalSteamedLow.Text);
        Assert.Contains("blow the glass through", coalSteamedLow.Text);
        Assert.DoesNotContain("injector on", coalSteamedLow.Text);

        var oilSteamedLow = FiringAdvice.ForOil(new OilReading
        {
            FireOn = false,
            BoilerPressure = GaugeGlassChoke.BlowMinPressure,
            GlassLevel = FiringAdvice.LowGlass - 0.15f,
            GlassCheck = GlassCheck.Stuck,
            Injectors = noInjectors,
        });
        Assert.Equal(AdviceLevel.Act, oilSteamedLow.Level);
        Assert.Contains("try cocks", oilSteamedLow.Text);
        Assert.Contains("blow the glass through", oilSteamedLow.Text);
        Assert.DoesNotContain("fill her before you light", oilSteamedLow.Text);
    }

    [Fact]
    public void AutomaticFiremanBlowsThroughDirtyGlassAndNeverBaresCrownSheetOnStuckGlass()
    {
        var fire = new FireModel(new FireboxParameters(150f, 120f, 0.5f));
        fire.LayReadyFire(true);
        var man = new AutoFireman { Mode = FiremanMode.Road };

        FiremanReadings BaseReadings(float glass, float trueGlass, float choke, bool blowing, float sinceBlow) => new FiremanReadings
        {
            Pressure = 14f,
            SafetyValve = 15f,
            Work = 0.5f,
            FullWorkSteam = 5f,
            GlassMass = 4000f,
            Glass = glass,
            TenderWater = 20000f,
            TenderCoal = 5000f,
            Injector = InjectorState.Off,
            InjectorSteamValve = true,
            InjectorCrack = 0.2f,
            InjectorStartPressure = 3.5f,
            InjectorFullFeed = 6f,
            InjectorLeast = 0.2f,
            Solids = 2000f,
            FoamingSolids = 2600f,
            HasGlassCocks = true,
            GlassChoke = choke,
            GlassBlowing = blowing,
            TimeSinceGlassBlow = sinceBlow,
            HasTryCockGlass = true,
            TryCockGlass = trueGlass,
            ShovelMass = 3f,
        };

        // 1. Periodic blow-through in dirty water.
        var actions = man.Step(0.5f, fire, BaseReadings(0.55f, 0.55f, 0.10f, false, GaugeGlassChoke.BlowReminderTime));
        Assert.Equal(1f, actions.GlassBlowCock);
        Assert.True(man.BlowingGlass);

        // During intentional blow-through drawdown (glass drops to 0.02 while true level is 0.55),
        // the fireman does not trigger a false LowWater notice.
        var during = man.Step(0.5f, fire, BaseReadings(0.02f, 0.55f, 0.0f, true, 0f));
        Assert.False(during.Notices.HasFlag(FiremanNotice.LowWater));

        // 2. Stuck falsely-high glass (0.80) while true level at try cocks is dangerously low (0.08):
        // fireman reads try cocks, calls LowWater, puts the injector on, and blows through the glass.
        var man2 = new AutoFireman { Mode = FiremanMode.Road };
        var stuck = man2.Step(0.5f, fire, BaseReadings(0.80f, 0.08f, 0.90f, false, 600f));
        Assert.True(stuck.Notices.HasFlag(FiremanNotice.LowWater), "must detect true low water via try cocks when glass is stuck high");
        Assert.True(stuck.Injector > 0f, "must start injector immediately instead of trusting stuck 0.80 glass");
        Assert.Equal(1f, stuck.GlassBlowCock);
    }

    [Fact]
    public void AutomaticFiremanReadsTryCocksWhilePlayerBlowsGlassAndWhenEnabledMidBlow()
    {
        var fire = new FireModel(new FireboxParameters(150f, 120f, 0.5f));
        fire.LayReadyFire(true);

        FiremanReadings Readings(float glass, float trueGlass, bool blowing, bool hasTryCocks = true) => new FiremanReadings
        {
            Pressure = 14f,
            SafetyValve = 15f,
            Work = 0.4f,
            FullWorkSteam = 5f,
            GlassMass = 4000f,
            Glass = glass,
            TenderWater = 20000f,
            TenderCoal = 5000f,
            Injector = InjectorState.Off,
            InjectorSteamValve = true,
            InjectorCrack = 0.2f,
            InjectorStartPressure = 3.5f,
            InjectorFullFeed = 6f,
            InjectorLeast = 0.2f,
            Solids = 1200f,
            FoamingSolids = 1400f,
            HasGlassCocks = true,
            GlassChoke = 0f,
            GlassBlowing = blowing,
            TimeSinceGlassBlow = 0f,
            HasTryCockGlass = hasTryCocks,
            TryCockGlass = trueGlass,
            ShovelMass = 3f,
        };

        // 1. Player cracks a glass cock while the fireman is active (holding it open 70 s):
        // fireman must track TryCockGlass live rather than freezing at his pre-blow level.
        var active = new AutoFireman { Mode = FiremanMode.Road };
        for (var i = 0; i < 20; i++)
            active.Step(0.5f, fire, Readings(0.60f, 0.60f, blowing: false));
        Assert.False(active.Feeding);

        FiremanActions last = default;
        for (var t = 0f; t < 70f; t += 0.5f)
        {
            var trueLevel = 0.60f - 0.52f * (t / 70f); // drops from 0.60 down to 0.08 while glass is blowing
            last = active.Step(0.5f, fire, Readings(0.02f, trueLevel, blowing: true));
        }
        Assert.True(active.Feeding, "fireman must start feeding while player holds glass cock open and water drops");
        Assert.True(last.Injector > 0f, "injector must be commanded open via live try-cock reading");
        Assert.True(last.Notices.HasFlag(FiremanNotice.LowWater), "must notice true low water during prolonged blow");

        // 2. Enabling the fireman mid-blow with drawn-down glass (0.02) and healthy try-cock level (0.65):
        // must initialize from TryCockGlass (not the drawn-down 0.02 glass) and NOT feed or alarm.
        var midBlowHealthy = new AutoFireman { Mode = FiremanMode.Road };
        var healthyStep = midBlowHealthy.Step(0.5f, fire, Readings(0.02f, 0.65f, blowing: true));
        Assert.False(midBlowHealthy.Feeding, "must not start feeding on a drawn-down glass when try cocks show 0.65");
        Assert.False(healthyStep.Notices.HasFlag(FiremanNotice.LowWater));

        // 3. Enabling the fireman mid-blow when true try-cock water is actually low (0.20):
        // must immediately read TryCockGlass and start feeding.
        var midBlowLow = new AutoFireman { Mode = FiremanMode.Road };
        var lowStep = midBlowLow.Step(0.5f, fire, Readings(0.02f, 0.20f, blowing: true));
        Assert.True(midBlowLow.Feeding, "must feed when enabled mid-blow with truly low try-cock water");
        Assert.True(lowStep.Injector > 0f);

        // 4. Enabling mid-blow without try-cock readings must not poison seenGlass with the drawn-down level.
        var midBlowNoTryCocks = new AutoFireman { Mode = FiremanMode.Road };
        var noTryStep = midBlowNoTryCocks.Step(0.5f, fire, Readings(0.02f, 0f, blowing: true, hasTryCocks: false));
        Assert.False(midBlowNoTryCocks.Feeding);
        Assert.False(noTryStep.Notices.HasFlag(FiremanNotice.LowWater));
    }
}
