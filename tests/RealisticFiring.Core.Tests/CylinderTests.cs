using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

/// <summary>
/// The cylinders' valve events and indicator diagram, on the S282's
/// figures: 0.61 by 0.711 m cylinders (208 L), a 1,000 L steam chest, and
/// 6 kg/s through the throttle. Pressures are absolute bar.
/// </summary>
public class CylinderTests
{
    private const float Bore = 0.61f;
    private const float Stroke = 0.711f;
    private const float CylinderVolume = 207.8f;
    private const float ChestVolume = 1000f;
    private const float MaxFlow = 6f;

    // The game's own steam table: specific volume in L/kg from absolute bar, and back.
    private static float SteamVolume(float pressure) => 1720f * (float)Math.Pow(pressure, -0.951);
    private static float SteamPressure(float volume) => (float)Math.Pow(volume / 1720f, -1.0515248);

    private static readonly ValveGear Gear = new ValveGear();

    // The stock engine's mean pressure ratio, without its condensation step.
    private static float StockMeanRatio(float cutoff) => cutoff * (1f + (float)Math.Log(1f / cutoff));

    [Theory]
    [InlineData(0.25f)]
    [InlineData(0.5f)]
    [InlineData(0.75f)]
    public void TheValveClosesAtTheCutoffAsked(float cutoff)
    {
        var events = Gear.Events(cutoff);

        Assert.Equal(0f, events.Opening(events.CutoffAngle), 3);
        Assert.True(events.Opening(events.CutoffAngle - 0.05f) > 0f);
        Assert.Equal(cutoff, (1f - (float)Math.Cos(events.CutoffAngle)) / 2f, 3);
    }

    [Fact]
    public void ItMatchesTheZeunerCutoffOfThePennsyI1s()
    {
        // Lap 2 in., lead 1/4 in., 6 in. of travel in full gear: 49.65 % (Wikipedia, "Piston valve").
        var i1s = new ValveGear { Lap = 2f, Lead = 0.25f };

        Assert.Equal(3f, i1s.Events(0.4965f).HalfTravel, 2);
    }

    [Fact]
    public void ShorterCutoffsReleaseEarlierAndCompressMore()
    {
        var quarter = Gear.Events(0.25f);
        var half = Gear.Events(0.5f);
        var full = Gear.Events(0.9f);

        Assert.InRange(quarter.Release, 0.66f, 0.72f);
        Assert.InRange(quarter.Compression, 0.28f, 0.34f);
        Assert.InRange(half.Release, 0.8f, 0.85f);
        Assert.InRange(full.Release, 0.95f, 1f);
        Assert.True(full.Compression < 0.05f);
    }

    [Fact]
    public void TheLeadOpensThePortBeforeDeadCenterInEveryNotch()
    {
        foreach (var cutoff in new[] { 0.15f, 0.5f, 0.9f })
        {
            var events = Gear.Events(cutoff);
            Assert.True(events.AdmissionAngle < 0f);
            Assert.Equal(Gear.Lead / Gear.PortWidth, events.Opening(0f), 3);
        }
    }

    [Theory]
    [InlineData(0.15f)]
    [InlineData(0.25f)]
    [InlineData(0.5f)]
    [InlineData(0.9f)]
    public void TheMeanPressureIsTheDiagramsArea(float cutoff)
    {
        var cycle = new CylinderCycle(Gear.Events(cutoff), 0.1f, 1.135f, 14f, 1.3f);
        const int steps = 20000;
        var area = 0.0;
        for (var i = 0; i < steps; i++)
        {
            var s = (i + 0.5f) / steps;
            area += (cycle.Working(s) - cycle.Returning(s)) / steps;
        }

        Assert.Equal(area, cycle.MeanEffectivePressure, 2);
    }

    [Fact]
    public void FullGearGivesNearlyTheStockPressureAndShorterCutoffsLess()
    {
        var full = new CylinderCycle(Gear.Events(0.9f), 0.1f, 1.135f, 14f, 1f).MeanEffectivePressure;
        var stockFull = 14f * StockMeanRatio(0.9f) - 1f;
        var half = new CylinderCycle(Gear.Events(0.5f), 0.1f, 1.135f, 14f, 1f).MeanEffectivePressure;
        var quarter = new CylinderCycle(Gear.Events(0.25f), 0.1f, 1.135f, 14f, 1f).MeanEffectivePressure;

        Assert.InRange(full / stockFull, 0.93f, 1f);
        Assert.True(full > half && half > quarter && quarter > 0f);
    }

    [Fact]
    public void BackPressureCostsMeanPressure()
    {
        var events = Gear.Events(0.5f);
        var free = new CylinderCycle(events, 0.1f, 1.135f, 14f, 1f).MeanEffectivePressure;
        var choked = new CylinderCycle(events, 0.1f, 1.135f, 14f, 1.7f).MeanEffectivePressure;

        Assert.InRange(free - choked, 0.6f, 0.9f);
    }

    [Fact]
    public void TheClearanceKeepsMorePressureAtRelease()
    {
        var quarter = new CylinderCycle(Gear.Events(0.25f), 0.1f, 1.135f, 14f, 1f);
        var full = new CylinderCycle(Gear.Events(0.9f), 0.1f, 1.135f, 14f, 1f);

        Assert.InRange(quarter.ReleasePressure, 0.25f * 14f, 0.45f * 14f);
        Assert.InRange(full.ReleasePressure, 0.88f * 14f, 0.96f * 14f);
    }

    [Fact]
    public void TheCushionRisesTowardTheClearance()
    {
        var cycle = new CylinderCycle(Gear.Events(0.25f), 0.1f, 1.135f, 14f, 1.2f);

        Assert.Equal(1.2f, cycle.Returning(0.5f), 3);
        Assert.True(cycle.Returning(0f) > 5f);
        Assert.True(cycle.Returning(0f) < 14f);
    }

    [Fact]
    public void AShortCutoffIsOpenLongerThanItsShareOfTheStroke()
    {
        // The piston creeps near dead center, so a quarter of its travel takes
        // a third of the stroke's crank angle, and the lead adds more.
        var quarter = Gear.Events(0.25f).AdmissionTurns;
        var half = Gear.Events(0.5f).AdmissionTurns;

        Assert.True(quarter > 1.3f * 0.25f / 2f);
        Assert.InRange(half, 0.25f, 0.3f);
    }

    [Fact]
    public void TheValveBarelyOpensThePortAtAShortCutoff()
    {
        var short15 = Gear.Events(0.15f);
        var long75 = Gear.Events(0.75f);

        Assert.True(short15.PortWindow < 0.1f * short15.AdmissionTurns);
        Assert.True(long75.PortWindow > 0.4f * long75.AdmissionTurns);
    }

    // Superheated well past condensing on the walls, unless asked otherwise.
    private static SteamCylinders S282(float cutoff, float exhaustFlow = 0f, float superheat = 150f)
    {
        var cylinders = new SteamCylinders(new CylinderSpec(), Bore, Stroke, MaxFlow);
        cylinders.Set(cutoff, exhaustFlow, 14f, SteamCylinders.SaturationTemperature(14f) + superheat);
        return cylinders;
    }

    private static CylinderAdmission Admit(SteamCylinders cylinders, float turns) =>
        cylinders.Admit(14f, ChestVolume / SteamVolume(14f), ChestVolume, CylinderVolume, turns, SteamVolume, SteamPressure);

    // The stock admission: the cut-off's volume filled for the cut-off's share of half a turn at full flow.
    private static float StockAdmission(float cutoff, float turns)
    {
        var filled = CylinderVolume * cutoff;
        var shared = filled / (filled + ChestVolume) * (ChestVolume / SteamVolume(14f));
        var timed = cutoff / turns / 2f * MaxFlow;
        return Math.Min(14f, SteamPressure(filled / Math.Min(shared, timed)));
    }

    [Fact]
    public void SlowlyTheCylinderFillsAsTheStockOneDoesFromItsChest()
    {
        var admission = Admit(S282(0.5f), 0.5f);

        Assert.InRange(admission.Pressure, StockAdmission(0.5f, 0.5f) - 0.5f, 14f);
        Assert.True(admission.Cushion > 0f && admission.Cushion < 0.1f * admission.Mass);
    }

    [Fact]
    public void AtSpeedAShortCutoffHoldsMorePressureThanTheStockTiming()
    {
        var quarter = Admit(S282(0.25f), 3f).Pressure;

        Assert.True(quarter > StockAdmission(0.25f, 3f) + 1.5f, $"{quarter} against {StockAdmission(0.25f, 3f)}");
    }

    [Fact]
    public void AtSpeedNotchingUpRaisesTheAdmissionPressure()
    {
        var quarter = Admit(S282(0.25f), 3f).Pressure;
        var half = Admit(S282(0.5f), 3f).Pressure;

        Assert.True(quarter > half + 0.4f, $"{quarter} at 25 % against {half} at 50 %");
        Assert.True(half > StockAdmission(0.5f, 3f) + 2f, $"{half} at 50 % against the stock {StockAdmission(0.5f, 3f)}");
    }

    [Fact]
    public void TheBlastPipesBackPressureGrowsWithTheExhaust()
    {
        Assert.Equal(1f, S282(0.5f, 0f).BackPressure, 3);
        Assert.Equal(1.7f, S282(0.5f, MaxFlow).BackPressure, 3);
        Assert.InRange(S282(0.5f, MaxFlow / 2f).BackPressure, 1.15f, 1.2f);
    }

    [Fact]
    public void SuperheatStiffensTheExpansion()
    {
        var cylinders = new SteamCylinders(new CylinderSpec(), Bore, Stroke, MaxFlow);
        cylinders.Set(0.5f, 0f, 14f, SteamCylinders.SaturationTemperature(14f));
        Assert.Equal(1.135f, cylinders.Exponent, 3);
        cylinders.Set(0.5f, 0f, 14f, SteamCylinders.SaturationTemperature(14f) + 150f);
        Assert.Equal(1.3f, cylinders.Exponent, 3);
    }

    // Goss's Purdue engine, 17 by 24 in., on 10 bar at a quarter cut-off.
    private static SteamCylinders Goss(float superheat, float cutoff = 0.25f)
    {
        var cylinders = new SteamCylinders(new CylinderSpec(), 0.432f, 0.61f, MaxFlow);
        cylinders.Set(cutoff, 0f, 10f, SteamCylinders.SaturationTemperature(10f) + superheat);
        return cylinders;
    }

    [Fact]
    public void SaturatedSteamCondensesOnTheWallsTillTheSuperheatStopsIt()
    {
        // A quarter of the steam admitted: a third more than works.
        Assert.Equal(1f / 3f, Goss(0f).Condensation(2f, 10f), 3);
        Assert.Equal(0.13f / 0.87f, Goss(50f).Condensation(2f, 10f), 3);
        Assert.Equal(0f, Goss(105f).Condensation(2f, 10f));
    }

    [Fact]
    public void CylindersLeftAsTheyWereDoNotCondense()
    {
        var cylinders = Goss(0f);
        cylinders.WallCondensation = false;

        Assert.Equal(0f, cylinders.Condensation(2f, 10f));
    }

    [Fact]
    public void ShortCutoffsSlowStrokesLowPressureAndSmallCylindersCondenseMore()
    {
        var quarter = Goss(0f).Condensation(2f, 10f);
        Assert.True(Goss(0f, 0.15f).Condensation(2f, 10f) > quarter);
        Assert.True(Goss(0f, 0.5f).Condensation(2f, 10f) < quarter);
        Assert.True(Goss(0f).Condensation(0.5f, 10f) > quarter);
        Assert.True(Goss(0f).Condensation(4f, 10f) < quarter);
        Assert.True(Goss(0f).Condensation(2f, 5f) > quarter);
        Assert.True(S282(0.25f, superheat: 0f).Condensation(2f, 10f) < quarter);
    }

    // Steam an indicated horsepower-hour, lb, at two turns a second.
    private static float WaterRate(SteamCylinders cylinders)
    {
        var admission = Admit(cylinders, 2f);
        var work = cylinders.Cycle(admission.Pressure).MeanEffectivePressure * 1e5f * CylinderVolume / 1000f;
        return admission.Mass / work * 3.6e6f * 2.2046f * 0.7457f;
    }

    [Fact]
    public void SaturatedSteamCostsAFifthMoreForTheSameWork()
    {
        var dry = WaterRate(S282(0.25f));
        var wet = WaterRate(S282(0.25f, superheat: 0f));

        // Goss's superheated engine 21.6 lb at 200 psi, the saturated 25.5;
        // PRR's H8sb against the H8b, 26 to 35 % less steam.
        Assert.InRange(dry, 18f, 22f);
        Assert.InRange(wet / dry, 1.12f, 1.4f);
    }

    [Fact]
    public void TheCondensedSteamIsCountedInWhatTheChestGives()
    {
        var wet = Admit(S282(0.25f, superheat: 0f), 2f);
        var dry = Admit(S282(0.25f), 2f);

        var condensation = S282(0.25f, superheat: 0f).Condensation(2f, 14f);
        Assert.Equal(wet.Mass * condensation / (1f + condensation), wet.Condensed, 3);
        Assert.Equal(0f, dry.Condensed);
        Assert.True(wet.Mass > 1.15f * dry.Mass, $"{wet.Mass} against {dry.Mass}");
    }

    [Theory]
    [InlineData(1f, 99.6f)]
    [InlineData(7f, 165f)]
    [InlineData(14f, 195f)]
    [InlineData(20f, 212.4f)]
    public void SaturationTemperatureIsCloseEnough(float pressure, float celsius)
    {
        Assert.InRange(SteamCylinders.SaturationTemperature(pressure), celsius - 3f, celsius + 3f);
    }
}
