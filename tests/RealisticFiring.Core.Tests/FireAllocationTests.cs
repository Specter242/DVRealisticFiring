using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

/// <summary>
/// The fire's step makes no garbage: it runs every physics step on every
/// fired engine, and each allocation there is a collection sooner (the
/// performance review, 2026-10-25: 360 bytes a step with fresh coal in all
/// nine zones, from enumerating each zone's neighbors).
/// </summary>
public class FireAllocationTests
{
    private static readonly FireboxParameters S282 = new FireboxParameters(150f, 120f, 0.5f);

    [Fact]
    public void AStepWithFreshCoalEverywhereAllocatesNothing()
    {
        var fire = FireTestKit.BurningFire(S282, 60f);
        fire.AddCoalEvenly(30f);
        var inputs = FireTestKit.Inputs(airFlow: 3f, door: 0f);
        // Warmed up first: the first steps load and fill what they keep.
        FireTestKit.Run(fire, inputs, 1f);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 200; i++)
            fire.Step(FireTestKit.Dt, inputs);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated == 0, $"200 steps allocated {allocated} bytes");
    }
}
