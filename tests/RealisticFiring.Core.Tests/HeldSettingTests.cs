using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

 public class HeldSettingTests
{
    private const float Dt = 0.05f;
    private const float Step = 0.1f, Notice = 2f, Dwell = 10f;

    private static HeldSetting Setting() => new HeldSetting(Step, Notice, Dwell);

    // Holds `wanted` for `seconds`; returns when it moved, or -1.
    private static float HoldFor(HeldSetting setting, float wanted, float seconds)
    {
        var start = setting.Value;
        for (var t = 0f; t < seconds - 1e-4f; t += Dt)
        {
            setting.Hold(wanted, Dt);
            if (setting.Value != start)
                return t + Dt;
        }
        return -1f;
    }

    [Fact] public void HisFirstWishIsWhereHeTakesItUp()
    {
        var damper = Setting();
        Assert.True(float.IsNaN(damper.Value));
        Assert.Equal(0.88f, damper.Hold(0.88f, Dt));
        // Taking it up is no move to wait after: wide open once noticed.
        var movedAt = HoldFor(damper, 1f, 5f);
        Assert.InRange(movedAt, Notice, Notice + 2f * Dt);
        Assert.Equal(1f, damper.Value);
    }

    [Fact] public void AWishFlickingEitherSideOfAMarkLeavesItWhereItIs()
    {
        // The damper at a stop on the black-smoke mark: 0.13 and its smoke
        // floor 0.4 by turns, several times a second.
        var damper = Setting();
        damper.Hold(0.13f, Dt);
        for (var i = 0; i < 1200; i++)
            damper.Hold(i % 6 < 3 ? 0.4f : 0.13f, Dt);
        Assert.Equal(0.13f, damper.Value);
    }

    [Fact] public void ASteadyChangeIsMadeOnceNoticedAndNotAgainSoon()
    {
        var damper = Setting();
        damper.Hold(1f, Dt);
        Assert.InRange(HoldFor(damper, 0.5f, 5f), Notice, Notice + 2f * Dt);
        // Wanted further shut at once after: he leaves it his while.
        var movedAt = HoldFor(damper, 0.2f, 20f);
        Assert.InRange(movedAt, Dwell - 2f * Dt, Dwell + 2f * Dt);
        Assert.Equal(0.2f, damper.Value);
    }

    [Fact] public void ALessThanAStepWaitsButShutAndWideOpenAreReached()
    {
        var damper = Setting();
        damper.Hold(0.5f, Dt);
        Assert.Equal(-1f, HoldFor(damper, 0.55f, 60f));
        var door = Setting();
        door.Hold(0.05f, Dt);
        Assert.True(HoldFor(door, 0f, 5f) > 0f);
        Assert.Equal(0f, door.Value);
        damper.Hold(0.95f, Dt, now: true);
        Assert.True(HoldFor(damper, 1f, 15f) > 0f);
        Assert.Equal(1f, damper.Value);
    }

    [Fact] public void AMoveHeMustMakeGoesAtOnceAndIsLeftAfter()
    {
        var damper = Setting();
        damper.Hold(0.1f, Dt);
        Assert.Equal(0.4f, damper.Hold(0.4f, Dt, now: true));
        Assert.InRange(HoldFor(damper, 0f, 20f), Dwell - 2f * Dt, Dwell + 2f * Dt);
    }

    [Fact] public void AThrowPausesHisWishRatherThanForgettingIt()
    {
        // Between throws the door's rest is wanted shut from a crack: the
        // throws' open door, 0.8 s in each 2.5, does not start his notice over.
        var door = Setting();
        door.Hold(0.15f, Dt);
        var t = 0f;
        while (door.Value != 0f && t < 10f)
        {
            if (t % 2.5f < 0.8f)
                door.Idle(Dt);
            else
                door.Hold(0f, Dt);
            t += Dt;
        }
        Assert.Equal(0f, door.Value);
        Assert.True(t < 4f, $"shut after {t:F1} s");
    }

    [Fact] public void ResetForgetsItAndNoWishLeavesIt()
    {
        var door = Setting();
        door.Hold(0.15f, Dt);
        Assert.Equal(0.15f, door.Hold(float.NaN, Dt));
        door.Reset();
        Assert.True(float.IsNaN(door.Value));
        Assert.Equal(0.5f, door.Hold(0.5f, Dt));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HeldSetting(0f, Notice, Dwell));
    }
}
