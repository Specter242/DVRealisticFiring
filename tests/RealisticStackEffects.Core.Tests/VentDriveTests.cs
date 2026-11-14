using System.Collections.Generic;
using RealisticStackEffects.Core;
using Xunit;

namespace RealisticStackEffects.Core.Tests;

/// <summary>A stock source's steam sized by the game's own value (pnwr1854, Nexus, 2026-11-13).</summary>
public class VentDriveTests
{
    // As the S282's, S060's, and FEF-3's controllers set their blowdown: on and
    // off (0), start speed and start size scaled (102, 101).
    private static readonly int[] Blowdown = { 0, 102, 101 };

    [Fact] public void TheBlowdownFollowsItsFlowWhateverWasOpenedFirst()
    {
        // 10 % first, then 100 %, then 10 % again: each its own, not the
        // first opening shown full.
        var seen = new List<float>();
        foreach (var flow in new[] { 0.1f, 1f, 0.1f, 1f })
            seen.Add(VentDrive.Strength(flow, 1f, 0f, false, false));
        Assert.Equal(new[] { 0.1f, 1f, 0.1f, 1f }, seen);
        Assert.True(VentDrive.Strength(0.1f, 1f, 0f, false, false) < 0.35f, "a crack shows less than the old floor");
    }

    [Fact] public void TheUpdaterThatScalesTheEffectDrivesIt()
    {
        Assert.Equal(0, VentDrive.Driver(new[] { (IReadOnlyList<int>)Blowdown }));
        // The game's leaks: on and off by chest pressure and speed, scaled by
        // pressure's rate (5) on the first.
        Assert.Equal(0, VentDrive.Driver(new[] { (IReadOnlyList<int>)new[] { 0, 5 }, new[] { 0 } }));
        // Scaled by the second updater only.
        Assert.Equal(1, VentDrive.Driver(new[] { (IReadOnlyList<int>)new[] { 0 }, new[] { 102 } }));
    }

    [Fact] public void AnEffectOnlySwitchedOnAndOffHasNoDriver()
    {
        // The cylinder cocks' drips: on and off by the water in the cylinders
        // and the cocks, nothing scaled.
        Assert.Equal(-1, VentDrive.Driver(new[] { (IReadOnlyList<int>)new[] { 0 }, new[] { 0 } }));
        // Lifetime alone is not how much steam there is.
        Assert.Equal(-1, VentDrive.Driver(new[] { (IReadOnlyList<int>)new[] { 1, 100 } }));
        Assert.Equal(-1, VentDrive.Driver(new IReadOnlyList<int>[0]));
        Assert.Equal(-1, VentDrive.Driver(null!));
    }

    [Fact] public void ThePortIsTakenAsTheGameTakesIt()
    {
        Assert.Equal(0.5f, VentDrive.Strength(0.25f, 2f, 0f, false, false), 4);
        Assert.Equal(0.3f, VentDrive.Strength(-0.3f, 1f, 0f, true, false), 4);
        Assert.Equal(0.2f, VentDrive.Strength(-0.5f, 1f, 0.3f, false, true), 4);
        Assert.Equal(0.6f, VentDrive.Strength(0.5f, 1f, 0.1f, false, false), 4);
    }

    [Fact] public void AStrengthStaysWithinNothingAndFull()
    {
        Assert.Equal(1f, VentDrive.Strength(1.7f, 1f, 0f, false, false));
        Assert.Equal(0f, VentDrive.Strength(-0.2f, 1f, 0f, false, false));
        Assert.Equal(0f, VentDrive.Strength(float.NaN, 1f, 0f, false, false));
        Assert.Equal(0f, VentDrive.Strength(float.PositiveInfinity, 1f, 0f, false, false));
    }
}
