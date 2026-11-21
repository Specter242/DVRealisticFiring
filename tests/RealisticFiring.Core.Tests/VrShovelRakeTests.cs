using Xunit;

namespace RealisticFiring.Core.Tests;

public class VrShovelRakeTests
{
    [Fact]
    public void ThrustingAnEmptyShovelIntoAnOpenFireboxRakesOnce()
    {
        var rake = new VrShovelRake();
        Assert.False(rake.Step(grabbed: true, loaded: false, inOpenFirebox: false, GrateZone.Center, 0f, 1.8f, -0.3f, 10f));
        Assert.True(rake.Step(grabbed: true, loaded: false, inOpenFirebox: true, GrateZone.Center, 0f, 1.8f, 0.2f, 10.1f));
        // Holding still inside the firehole does not re-trigger, even past cooldown.
        Assert.False(rake.Step(grabbed: true, loaded: false, inOpenFirebox: true, GrateZone.Center, 0f, 1.8f, 0.2f, 10.5f));
        Assert.False(rake.Step(grabbed: true, loaded: false, inOpenFirebox: true, GrateZone.Center, 0f, 1.8f, 0.2f, 15.0f));
    }

    [Fact]
    public void SweepingTheBladeInsideTheFireboxRakesAgainAfterCooldown()
    {
        var rake = new VrShovelRake();
        Assert.True(rake.Step(grabbed: true, loaded: false, inOpenFirebox: true, GrateZone.Center, 0f, 1.8f, 0.2f, 10f));
        // Sweep before cooldown expires is ignored.
        Assert.False(rake.Step(grabbed: true, loaded: false, inOpenFirebox: true, GrateZone.Center, 0.25f, 1.8f, 0.2f, 10f + VrShovelRake.Cooldown * 0.5f));
        // Small jitter below SweepDistance after cooldown does not trigger.
        Assert.False(rake.Step(grabbed: true, loaded: false, inOpenFirebox: true, GrateZone.Center, 0.05f, 1.8f, 0.2f, 10f + VrShovelRake.Cooldown + 0.1f));
        // Meaningful sweep past SweepDistance after cooldown rakes again.
        Assert.True(rake.Step(grabbed: true, loaded: false, inOpenFirebox: true, GrateZone.Center, VrShovelRake.SweepDistance + 0.02f, 1.8f, 0.2f, 10f + VrShovelRake.Cooldown + 0.2f));
    }

    [Fact]
    public void MovingToAnotherZoneInsideTheFireboxRequiresMinimumSweepDistance()
    {
        var rake = new VrShovelRake();
        Assert.True(rake.Step(grabbed: true, loaded: false, inOpenFirebox: true, GrateZone.MidLeft, -0.2f, 1.8f, 0.2f, 10f));
        // Small movement across a zone boundary does not rake, even after cooldown.
        Assert.False(rake.Step(grabbed: true, loaded: false, inOpenFirebox: true, GrateZone.MidRight, -0.19f, 1.8f, 0.2f, 10.2f));
        Assert.False(rake.Step(grabbed: true, loaded: false, inOpenFirebox: true, GrateZone.MidRight, -0.19f, 1.8f, 0.2f, 10f + VrShovelRake.Cooldown + 0.05f));
        // Sweeping at least SweepDistance into the other zone after cooldown rakes.
        Assert.True(rake.Step(grabbed: true, loaded: false, inOpenFirebox: true, GrateZone.MidRight, -0.2f + VrShovelRake.SweepDistance + 0.02f, 1.8f, 0.2f, 10f + VrShovelRake.Cooldown + 0.1f));
    }

    [Fact]
    public void JitteringOneMillimetreAcrossZoneBoundaryAt90FpsForFiveSecondsNeverRepeatRakes()
    {
        var rake = new VrShovelRake();
        // Initial thrust into the firebox rakes once.
        Assert.True(rake.Step(grabbed: true, loaded: false, inOpenFirebox: true, GrateZone.Center, 0f, 1.8f, 0.2f, 10f));

        var repeatRakes = 0;
        const int frames = 90 * 5;
        for (var i = 1; i <= frames; i++)
        {
            var flip = (i & 1) == 1;
            var zone = flip ? GrateZone.MidLeft : GrateZone.Center;
            var x = flip ? -0.001f : 0f;
            var now = 10f + i / 90f;
            if (rake.Step(grabbed: true, loaded: false, inOpenFirebox: true, zone, x, 1.8f, 0.2f, now))
                repeatRakes++;
        }

        Assert.Equal(0, repeatRakes);
    }

    [Fact]
    public void JitteringHalfMillimetreAcrossFireboxLipAt90FpsForFiveSecondsNeverRepeatRakes()
    {
        var rake = new VrShovelRake();
        // Initial entry at the firehole lip rakes once.
        Assert.True(rake.Step(grabbed: true, loaded: false, inOpenFirebox: true, GrateZone.Center, 0f, 1.8f, 0.0005f, 10f));

        var repeatRakes = 0;
        const int frames = 90 * 5;
        for (var i = 1; i <= frames; i++)
        {
            var inside = (i & 1) == 0;
            var z = inside ? 0.0005f : 0f;
            var now = 10f + i / 90f;
            if (rake.Step(grabbed: true, loaded: false, inOpenFirebox: inside, GrateZone.Center, 0f, 1.8f, z, now))
                repeatRakes++;
        }

        Assert.Equal(0, repeatRakes);
    }

    [Fact]
    public void StayingOutsideForExitDebounceClearsInsideStateEvenWithoutFullSweepWithdrawal()
    {
        var rake = new VrShovelRake();
        Assert.True(rake.Step(grabbed: true, loaded: false, inOpenFirebox: true, GrateZone.Center, 0f, 1.8f, 0.01f, 10f));

        // Brief outside excursion shorter than ExitDebounce and below SweepDistance does not count as an exit.
        Assert.False(rake.Step(grabbed: true, loaded: false, inOpenFirebox: false, GrateZone.Center, 0f, 1.8f, 0f, 10.8f));
        Assert.False(rake.Step(grabbed: true, loaded: false, inOpenFirebox: false, GrateZone.Center, 0f, 1.8f, 0f, 10.9f));
        Assert.False(rake.Step(grabbed: true, loaded: false, inOpenFirebox: true, GrateZone.Center, 0f, 1.8f, 0.01f, 10.95f));

        // Continuous outside stay reaching ExitDebounce clears insideFirebox so re-entry rakes.
        Assert.False(rake.Step(grabbed: true, loaded: false, inOpenFirebox: false, GrateZone.Center, 0f, 1.8f, 0f, 11.0f));
        Assert.False(rake.Step(grabbed: true, loaded: false, inOpenFirebox: false, GrateZone.Center, 0f, 1.8f, 0f, 11.0f + VrShovelRake.ExitDebounce));
        Assert.True(rake.Step(grabbed: true, loaded: false, inOpenFirebox: true, GrateZone.Center, 0f, 1.8f, 0.01f, 11.0f + VrShovelRake.ExitDebounce + 0.02f));
    }

    [Fact]
    public void PullingOutAndThrustingBackInRakesAfterCooldown()
    {
        var rake = new VrShovelRake();
        Assert.True(rake.Step(grabbed: true, loaded: false, inOpenFirebox: true, GrateZone.Center, 0f, 1.8f, 0.2f, 10f));
        Assert.False(rake.Step(grabbed: true, loaded: false, inOpenFirebox: false, GrateZone.Center, 0f, 1.8f, -0.3f, 10.3f));
        // Re-entering before cooldown does not rake.
        Assert.False(rake.Step(grabbed: true, loaded: false, inOpenFirebox: true, GrateZone.Center, 0f, 1.8f, 0.2f, 10.5f));
        // Pulling out again and re-entering after cooldown rakes.
        Assert.False(rake.Step(grabbed: true, loaded: false, inOpenFirebox: false, GrateZone.Center, 0f, 1.8f, -0.3f, 10.8f));
        Assert.True(rake.Step(grabbed: true, loaded: false, inOpenFirebox: true, GrateZone.Center, 0f, 1.8f, 0.2f, 10f + VrShovelRake.Cooldown + 0.2f));
    }

    [Fact]
    public void UnloadingCoalNeverAccidentallyRakesWhileBladeRemainsInside()
    {
        var rake = new VrShovelRake();
        // Loaded shovel enters firebox and unloads coal.
        Assert.False(rake.Step(grabbed: true, loaded: true, inOpenFirebox: true, GrateZone.Center, 0f, 1.8f, 0.2f, 20f));
        Assert.False(rake.Step(grabbed: true, loaded: false, inOpenFirebox: true, GrateZone.Center, 0f, 1.8f, 0.2f, 20.02f));
        Assert.False(rake.CanHighlight(grabbed: true, loaded: false, inOpenFirebox: true, 20.02f));

        // Even sweeping widely and waiting past UnloadGrace while still inside must NOT rake until pulled out.
        Assert.False(rake.Step(grabbed: true, loaded: false, inOpenFirebox: true, GrateZone.Front, 0.4f, 1.8f, 0.5f, 25f));
        Assert.False(rake.CanHighlight(grabbed: true, loaded: false, inOpenFirebox: true, 25f));

        // Pulling out clears the exit lock, and re-entering after UnloadGrace rakes cleanly.
        Assert.False(rake.Step(grabbed: true, loaded: false, inOpenFirebox: false, GrateZone.Center, 0f, 1.8f, -0.3f, 25.2f));
        Assert.True(rake.CanHighlight(grabbed: true, loaded: false, inOpenFirebox: true, 25.3f));
        Assert.True(rake.Step(grabbed: true, loaded: false, inOpenFirebox: true, GrateZone.Front, 0f, 1.8f, 0.2f, 25.3f));
    }

    [Fact]
    public void PullingOutImmediatelyAfterUnloadStillRespectsUnloadGrace()
    {
        var rake = new VrShovelRake();
        Assert.False(rake.Step(grabbed: true, loaded: true, inOpenFirebox: true, GrateZone.Center, 0f, 1.8f, 0.2f, 20f));
        Assert.False(rake.Step(grabbed: true, loaded: false, inOpenFirebox: true, GrateZone.Center, 0f, 1.8f, 0.2f, 20.02f));
        Assert.False(rake.Step(grabbed: true, loaded: false, inOpenFirebox: false, GrateZone.Center, 0f, 1.8f, -0.3f, 20.2f));
        // Quick bounce back into the firebox within UnloadGrace is ignored.
        Assert.False(rake.Step(grabbed: true, loaded: false, inOpenFirebox: true, GrateZone.Center, 0f, 1.8f, 0.2f, 20.5f));
        // Pulling out and entering after UnloadGrace rakes.
        Assert.False(rake.Step(grabbed: true, loaded: false, inOpenFirebox: false, GrateZone.Center, 0f, 1.8f, -0.3f, 21.1f));
        Assert.True(rake.Step(grabbed: true, loaded: false, inOpenFirebox: true, GrateZone.Center, 0f, 1.8f, 0.2f, 21.2f));
    }

    [Fact]
    public void ReinsertingDuringPostUnloadGraceOnFirstShovelfulAllowsSweepingOnceGraceExpires()
    {
        var rake = new VrShovelRake();
        // First shovelful: unload inside firebox, withdraw blade, and reinsert before UnloadGrace expires.
        Assert.False(rake.Step(grabbed: true, loaded: true, inOpenFirebox: true, GrateZone.Center, 0f, 1.8f, 0.2f, 20f));
        Assert.False(rake.Step(grabbed: true, loaded: false, inOpenFirebox: true, GrateZone.Center, 0f, 1.8f, 0.2f, 20.02f));
        Assert.False(rake.Step(grabbed: true, loaded: false, inOpenFirebox: false, GrateZone.Center, 0f, 1.8f, -0.3f, 20.2f));
        Assert.False(rake.Step(grabbed: true, loaded: false, inOpenFirebox: true, GrateZone.Center, 0f, 1.8f, 0.2f, 20.5f));

        // Staying still inside once UnloadGrace expires does not spuriously rake.
        Assert.False(rake.Step(grabbed: true, loaded: false, inOpenFirebox: true, GrateZone.Center, 0f, 1.8f, 0.2f, 20.02f + VrShovelRake.UnloadGrace + 0.05f));

        // Sweeping at least SweepDistance inside after grace expires rakes without needing a second withdrawal.
        Assert.True(rake.Step(grabbed: true, loaded: false, inOpenFirebox: true, GrateZone.Center, VrShovelRake.SweepDistance + 0.02f, 1.8f, 0.2f, 20.02f + VrShovelRake.UnloadGrace + 0.1f));
    }

    [Fact]
    public void BriefLipJitterAfterUnloadDoesNotClearWithdrawRequirement()
    {
        var rake = new VrShovelRake();
        Assert.False(rake.Step(grabbed: true, loaded: true, inOpenFirebox: true, GrateZone.Center, 0f, 1.8f, 0.0005f, 20f));
        Assert.False(rake.Step(grabbed: true, loaded: false, inOpenFirebox: true, GrateZone.Center, 0f, 1.8f, 0.0005f, 20.02f));

        // Single-frame 0.5 mm lip jitter outside does not satisfy the post-unload exit requirement.
        Assert.False(rake.Step(grabbed: true, loaded: false, inOpenFirebox: false, GrateZone.Center, 0f, 1.8f, 0f, 20.03f));
        Assert.False(rake.Step(grabbed: true, loaded: false, inOpenFirebox: true, GrateZone.Center, 0f, 1.8f, 0.0005f, 20.04f));

        // Even past UnloadGrace with a wide sweep, mustExitBeforeRake still blocks until genuinely out.
        Assert.False(rake.Step(grabbed: true, loaded: false, inOpenFirebox: true, GrateZone.Front, 0.3f, 1.8f, 0.2f, 22f));
        Assert.False(rake.CanHighlight(grabbed: true, loaded: false, inOpenFirebox: true, 22f));

        // Staying outside continuously for ExitDebounce satisfies the withdraw requirement.
        Assert.False(rake.Step(grabbed: true, loaded: false, inOpenFirebox: false, GrateZone.Center, 0f, 1.8f, 0f, 22.1f));
        Assert.False(rake.Step(grabbed: true, loaded: false, inOpenFirebox: false, GrateZone.Center, 0f, 1.8f, 0f, 22.1f + VrShovelRake.ExitDebounce));
        Assert.True(rake.CanHighlight(grabbed: true, loaded: false, inOpenFirebox: true, 22.1f + VrShovelRake.ExitDebounce + 0.02f));
        Assert.True(rake.Step(grabbed: true, loaded: false, inOpenFirebox: true, GrateZone.Front, 0f, 1.8f, 0.0005f, 22.1f + VrShovelRake.ExitDebounce + 0.02f));
    }

    [Fact]
    public void LipJitterOnUnloadFrameDoesNotClearWithdrawRequirement()
    {
        var rake = new VrShovelRake();
        // Loaded blade is inside the open firebox at the lip.
        Assert.False(rake.Step(grabbed: true, loaded: true, inOpenFirebox: true, GrateZone.Center, 0f, 1.8f, 0.0005f, 20f));

        // On the exact unload frame, blade jitters 0.5 mm outside the firehole collider.
        Assert.False(rake.Step(grabbed: true, loaded: false, inOpenFirebox: false, GrateZone.Center, 0f, 1.8f, 0f, 20.02f));

        // Returns inside on the next frame and stays inside past UnloadGrace.
        Assert.False(rake.Step(grabbed: true, loaded: false, inOpenFirebox: true, GrateZone.Center, 0f, 1.8f, 0.0005f, 20.03f));
        Assert.False(rake.Step(grabbed: true, loaded: false, inOpenFirebox: true, GrateZone.Center, 0f, 1.8f, 0.0005f, 20.02f + VrShovelRake.UnloadGrace + 0.1f));

        // Sweeping >= SweepDistance inside after grace expires still must NOT rake until genuinely withdrawn.
        Assert.False(rake.Step(grabbed: true, loaded: false, inOpenFirebox: true, GrateZone.Front, VrShovelRake.SweepDistance + 0.02f, 1.8f, 0.0005f, 20.02f + VrShovelRake.UnloadGrace + 0.2f));
        Assert.False(rake.CanHighlight(grabbed: true, loaded: false, inOpenFirebox: true, 20.02f + VrShovelRake.UnloadGrace + 0.2f));

        // Genuinely withdrawing outside (>= SweepDistance) clears the withdraw lock so re-entry rakes.
        Assert.False(rake.Step(grabbed: true, loaded: false, inOpenFirebox: false, GrateZone.Center, 0f, 1.8f, -0.3f, 20.02f + VrShovelRake.UnloadGrace + 0.3f));
        Assert.True(rake.CanHighlight(grabbed: true, loaded: false, inOpenFirebox: true, 20.02f + VrShovelRake.UnloadGrace + 0.4f));
        Assert.True(rake.Step(grabbed: true, loaded: false, inOpenFirebox: true, GrateZone.Front, 0f, 1.8f, 0.2f, 20.02f + VrShovelRake.UnloadGrace + 0.4f));
    }

    [Fact]
    public void UnheldOrLoadedShovelNeverRakes()
    {
        var rake = new VrShovelRake();
        Assert.False(rake.Step(grabbed: false, loaded: false, inOpenFirebox: true, GrateZone.Center, 0f, 1.8f, 0.2f, 10f));
        Assert.False(rake.Step(grabbed: true, loaded: true, inOpenFirebox: true, GrateZone.Center, 0f, 1.8f, 0.2f, 10.1f));
        Assert.False(rake.CanHighlight(grabbed: false, loaded: false, inOpenFirebox: true, 10.1f));
        Assert.False(rake.CanHighlight(grabbed: true, loaded: true, inOpenFirebox: true, 10.1f));
    }

    [Fact]
    public void ResetClearsLockoutAndInsideState()
    {
        var rake = new VrShovelRake();
        rake.Step(grabbed: true, loaded: true, inOpenFirebox: true, GrateZone.Center, 0f, 1.8f, 0.2f, 10f);
        rake.Step(grabbed: true, loaded: false, inOpenFirebox: true, GrateZone.Center, 0f, 1.8f, 0.2f, 10.02f);
        rake.Reset();
        Assert.True(rake.CanHighlight(grabbed: true, loaded: false, inOpenFirebox: true, 10.1f));
        Assert.True(rake.Step(grabbed: true, loaded: false, inOpenFirebox: true, GrateZone.Center, 0f, 1.8f, 0.2f, 10.1f));
    }
}
