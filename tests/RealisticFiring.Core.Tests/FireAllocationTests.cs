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
        // The measured loop then runs once unmeasured. A first pass can
        // allocate once for reasons that are not the step's own (the runtime
        // recompiling a hot method partway through the loop), which failed
        // this test now and then; a step that makes garbage makes it on
        // every pass.
        long Measure()
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 200; i++)
                fire.Step(FireTestKit.Dt, inputs);
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Measure();
        var allocated = Measure();
        Assert.True(allocated == 0, $"200 steps allocated {allocated} bytes");
        // The measured steps were still the costly case: fresh coal in every zone.
        Assert.All(fire.Zones, zone => Assert.True(zone.Fresh > 0f));
    }
}
