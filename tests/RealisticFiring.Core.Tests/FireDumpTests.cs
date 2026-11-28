using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

/// <summary>
/// The fire-dump lever as the host's sim acts on it (Torun, 2026-11-22: in
/// multiplayer the lever "gets stuck at an extreme position, and all the coal
/// gets dumped into the pan"). Rocking still shakes the grate; a hold still
/// dumps at the stock rate; but one hold, or one stuck reading, takes at most a
/// third of the fire, and dumping it all takes several holds back through center.
/// </summary>
public class FireDumpTests
{
    private const float Dt = 0.05f;

    // The S282's stock firebox: 150 kg, 10 kg/s dump.
    private static readonly FireboxParameters S282 = new FireboxParameters(150f, 120f, 0.5f, coalDumpRate: 10f);

    // The S060's: 60 kg, 4 kg/s.
    private static readonly FireboxParameters S060 = new FireboxParameters(60f, 180f, 0.5f, coalDumpRate: 4f);

    private sealed class Run
    {
        public readonly FireModel Fire;
        public readonly FireDump Dump = new FireDump();
        public int Shakes;
        public int Wiggles;
        public float Dumped;

        public Run(FireboxParameters p, float coal) => Fire = FireTestKit.BurningFire(p, coal, FireTestKit.CleanCoal);

        public Run Hold(float value, float seconds)
        {
            for (var t = 0f; t < seconds; t += Dt)
            {
                var tick = Dump.Step(Fire, value, Dt);
                Shakes += tick.Shakes;
                Wiggles += tick.Wiggles;
                Dumped += tick.Dumped;
            }
            return this;
        }
    }

    [Fact]
    public void RockingShakesTheGrateAndDumpsNothing()
    {
        var run = new Run(S282, 75f);
        for (var i = 0; i < 20; i++)
            run.Hold(i % 2 == 0 ? 0f : 1f, 0.6f).Hold(0.5f, 0.3f);
        for (var i = 0; i < 10; i++)
            run.Hold(0.6f, 0.4f).Hold(0.5f, 0.4f);

        Assert.Equal(20, run.Shakes);
        Assert.Equal(10, run.Wiggles);
        Assert.Equal(0f, run.Dumped);
        Assert.Equal(75f, run.Fire.CoalMass, 3);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    public void AHoldDumpsAtTheStockRateUpToAThirdOfTheFire(float side)
    {
        var run = new Run(S282, 75f).Hold(side, 1.5f);
        Assert.InRange(run.Dumped, 0.2f, 6f); // dumping has begun, a second over

        run.Hold(side, 10f);
        Assert.Equal(25f, run.Dumped, 1);
        Assert.Equal(50f, run.Fire.CoalMass, 1);
        Assert.Equal(0, run.Shakes + run.Wiggles);
    }

    [Theory]
    [InlineData(75f, 50f)]
    [InlineData(150f, 100f)]
    [InlineData(30f, 15f)] // a low fire: the hold's floor, a tenth of the box
    public void ALeverStuckOverForMinutesTakesOneHoldsShare(float coal, float left)
    {
        var run = new Run(S282, coal).Hold(0f, 300f);

        Assert.Equal(left, run.Fire.CoalMass, 1);
        Assert.Equal(0f, run.Dump.Step(run.Fire, 0f, Dt).Flow);
    }

    [Fact]
    public void AStuckLeverDoesNotDumpCoalFiredAfterItsShare()
    {
        var run = new Run(S060, 30f).Hold(1f, 20f);
        var before = run.Fire.CoalMass;
        run.Fire.AddCoal(10f, GrateZone.Center);
        run.Hold(1f, 20f);

        Assert.Equal(before + 10f, run.Fire.CoalMass, 2);
    }

    [Fact]
    public void AReadingThatFlickersOverAndBackNeverDumps()
    {
        // Shorter than the hold time over, then back to center, again and again.
        var run = new Run(S282, 75f);
        for (var i = 0; i < 60; i++)
            run.Hold(0f, 0.8f).Hold(0.5f, 0.2f);

        Assert.Equal(0f, run.Dumped);
    }

    [Fact]
    public void LeavingTheDumpPointOnTheSameSideDoesNotStartANewHold()
    {
        // Only back through center (or across) starts a new hold.
        var run = new Run(S282, 75f).Hold(0f, 10f);
        run.Hold(0.35f, 1f).Hold(0f, 10f);

        Assert.Equal(25f, run.Dumped, 1);
    }

    [Theory]
    [InlineData(75f, 5)]
    [InlineData(150f, 6)]
    [InlineData(10f, 1)]
    public void DeliberateHoldsBackThroughCenterDumpTheWholeFire(float coal, int holds)
    {
        var run = new Run(S282, coal);
        var taken = 0;
        while (run.Fire.CoalMass > 0f && taken < 20)
        {
            run.Hold(0f, 6f).Hold(0.5f, 0.5f);
            taken++;
        }

        Assert.Equal(0f, run.Fire.CoalMass, 3);
        Assert.Equal(holds, taken);
    }

    [Fact]
    public void WithTheCoalGoneAHoldKeepsClearingTheClinker()
    {
        // Dumping is how a clinkered fire is cleaned (design.md, "Grate shaking and the dump").
        var run = new Run(S282, 0f);
        foreach (var zone in run.Fire.Zones)
            zone.Clinker = 2f;
        var clinker = run.Fire.ClinkerMass;

        run.Hold(0f, 40f);

        Assert.True(run.Fire.ClinkerMass < 0.1f * clinker, $"{run.Fire.ClinkerMass:F2} of {clinker:F2} kg clinker left");
    }

    [Fact]
    public void TheFlowEasesOffAfterTheLeverComesBack()
    {
        var run = new Run(S282, 150f).Hold(0f, 3f);
        var flow = run.Dump.Step(run.Fire, 0.5f, Dt).Flow;
        Assert.InRange(flow, 0.5f, 1f);

        run.Hold(0.5f, 2f);
        Assert.Equal(0f, run.Dump.Step(run.Fire, 0.5f, Dt).Flow);
    }
}

public class DumpStrokeTests
{
    [Fact]
    public void NothingIsAllowedBeforeAHold()
    {
        var stroke = new DumpStroke();

        Assert.True(stroke.Spent);
        Assert.Equal(0f, stroke.Allow(1f, 50f, 10f, 0.05f));
    }

    [Fact]
    public void AHoldMayDumpAThirdOfTheFireOrATenthOfTheBox()
    {
        var stroke = new DumpStroke();

        stroke.Begin(90f, 150f);
        Assert.Equal(30f, stroke.Left, 3);
        stroke.Begin(60f, 150f);
        Assert.Equal(20f, stroke.Left, 3);
        stroke.Begin(12f, 150f);
        Assert.Equal(15f, stroke.Left, 3);
    }

    [Fact]
    public void TheLastTickIsCutToWhatIsLeft()
    {
        var stroke = new DumpStroke();
        stroke.Begin(0f, 10f); // 1 kg

        Assert.Equal(1f, stroke.Allow(1f, 5f, 10f, 0.05f)); // 0.5 kg
        stroke.Took(0.8f);
        Assert.Equal(0.4f, stroke.Allow(1f, 5f, 10f, 0.05f), 4); // 0.2 kg of 0.5
        stroke.Took(0.2f);
        Assert.True(stroke.Spent);
        Assert.Equal(0f, stroke.Allow(1f, 5f, 10f, 0.05f));
    }

    [Fact]
    public void AnEmptyGrateHasNothingToGuard()
    {
        var stroke = new DumpStroke();

        Assert.Equal(0.7f, stroke.Allow(0.7f, 0f, 10f, 0.05f));
    }

    [Theory]
    [InlineData(float.NaN, 150f, 15f)]
    [InlineData(float.PositiveInfinity, 150f, 15f)]
    [InlineData(-5f, 150f, 15f)]
    [InlineData(60f, float.NaN, 20f)]
    public void ABadReadingGivesASafeAllowance(float coal, float capacity, float left)
    {
        var stroke = new DumpStroke();
        stroke.Begin(coal, capacity);

        Assert.Equal(left, stroke.Left, 3);
    }
}
