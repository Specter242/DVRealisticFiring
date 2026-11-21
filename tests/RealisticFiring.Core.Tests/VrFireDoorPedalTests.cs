using Xunit;

namespace RealisticFiring.Core.Tests;

public class VrFireDoorPedalTests
{
    [Fact]
    public void CleanTriggerHoldEngagesAfterHoldDelayAndStaysHeld()
    {
        var pedal = new VrFireDoorPedal();
        var s0 = pedal.Step(triggerDown: true, useModified: false, stickDeflected: false, pointingAtUi: false, 10.00f);
        Assert.False(s0.Pressed);
        Assert.False(s0.Held);

        var s1 = pedal.Step(triggerDown: true, useModified: false, stickDeflected: false, pointingAtUi: false, 10.08f);
        Assert.False(s1.Pressed);
        Assert.False(s1.Held);

        var s2 = pedal.Step(triggerDown: true, useModified: false, stickDeflected: false, pointingAtUi: false, 10.00f + VrFireDoorPedal.HoldDelay);
        Assert.True(s2.Pressed);
        Assert.True(s2.Held);
        Assert.True(pedal.Active);

        var s3 = pedal.Step(triggerDown: true, useModified: false, stickDeflected: false, pointingAtUi: false, 12.00f);
        Assert.False(s3.Pressed);
        Assert.True(s3.Held);

        var s4 = pedal.Step(triggerDown: false, useModified: false, stickDeflected: false, pointingAtUi: false, 12.05f);
        Assert.False(s4.Pressed);
        Assert.False(s4.Held);
        Assert.False(pedal.Active);
    }

    [Fact]
    public void ScoopSizeScrollSuppressesDoorUntilTriggerIsReleased()
    {
        var pedal = new VrFireDoorPedal();
        // Player squeezes trigger, then flicks thumbstick before HoldDelay to adjust scoop size.
        Assert.False(pedal.Step(true, false, false, false, 10.00f).Held);
        Assert.False(pedal.Step(true, true, true, false, 10.06f).Held);
        // Thumbstick returns to neutral while trigger is still held: door must stay suppressed.
        Assert.False(pedal.Step(true, false, false, false, 10.30f).Held);
        Assert.False(pedal.Step(true, false, false, false, 11.00f).Held);
        // After releasing and pressing cleanly again, the door engages normally.
        Assert.False(pedal.Step(false, false, false, false, 11.05f).Held);
        Assert.False(pedal.Step(true, false, false, false, 11.10f).Held);
        var engage = pedal.Step(true, false, false, false, 11.10f + VrFireDoorPedal.HoldDelay);
        Assert.True(engage.Pressed);
        Assert.True(engage.Held);
    }

    [Fact]
    public void DeflectedStickAtTriggerPressSuppressesImmediately()
    {
        var pedal = new VrFireDoorPedal();
        Assert.False(pedal.Step(true, false, stickDeflected: true, false, 10.00f).Held);
        Assert.False(pedal.Step(true, false, stickDeflected: false, false, 10.50f).Held);
    }

    [Fact]
    public void StickNoiseOrDeflectionWhileDoorIsLatchedOpenKeepsDoorHeld()
    {
        var pedal = new VrFireDoorPedal();
        pedal.Step(true, false, false, false, 10.00f);
        Assert.True(pedal.Step(true, false, false, false, 10.20f).Pressed);
        Assert.True(pedal.Active);

        // Once latched open past HoldDelay, stick noise or turning stick deflection does not snap-cancel the door.
        var bump = pedal.Step(true, useModified: true, stickDeflected: true, false, 10.30f);
        Assert.False(bump.Pressed);
        Assert.True(bump.Held);
        Assert.True(pedal.Active);

        var stillHeld = pedal.Step(true, false, false, false, 10.60f);
        Assert.False(stillHeld.Pressed);
        Assert.True(stillHeld.Held);

        // Releasing the trigger closes the door cleanly.
        var released = pedal.Step(false, false, false, false, 10.70f);
        Assert.False(released.Held);
        Assert.False(pedal.Active);
    }

    [Fact]
    public void PauseWhileTriggerStaysHeldSuppressesDoorAfterUnpause()
    {
        var pedal = new VrFireDoorPedal();
        // Case 1: pause while door is already open, trigger stays held across unpause.
        pedal.Step(true, false, false, false, 10.00f);
        Assert.True(pedal.Step(true, false, false, false, 10.20f).Pressed);
        Assert.True(pedal.Active);

        pedal.SuppressUntilRelease();
        Assert.False(pedal.Active);

        // Unpausing with the trigger still held must NOT reopen the door after HoldDelay.
        Assert.False(pedal.Step(true, false, false, false, 10.25f).Held);
        Assert.False(pedal.Step(true, false, false, false, 10.25f + VrFireDoorPedal.HoldDelay + 0.5f).Held);
        Assert.False(pedal.Active);

        // Releasing and pressing cleanly again opens the door normally.
        Assert.False(pedal.Step(false, false, false, false, 11.00f).Held);
        Assert.False(pedal.Step(true, false, false, false, 11.10f).Held);
        var reopened = pedal.Step(true, false, false, false, 11.10f + VrFireDoorPedal.HoldDelay);
        Assert.True(reopened.Pressed);
        Assert.True(reopened.Held);

        // Case 2: pause while trigger was held before HoldDelay (e.g. clicking resume in pause menu).
        pedal.Reset();
        pedal.Step(true, false, false, false, 20.00f);
        pedal.SuppressUntilRelease();
        Assert.False(pedal.Step(true, false, false, false, 20.05f).Held);
        Assert.False(pedal.Step(true, false, false, false, 21.00f).Held);
    }

    [Fact]
    public void PointingAtUiSuppressesTheDoor()
    {
        var pedal = new VrFireDoorPedal();
        Assert.False(pedal.Step(true, false, false, pointingAtUi: true, 10.00f).Held);
        Assert.False(pedal.Step(true, false, false, pointingAtUi: false, 10.30f).Held);

        // Pointing at UI while already open also drops the hold until trigger release.
        pedal.Step(false, false, false, false, 10.40f);
        pedal.Step(true, false, false, false, 10.50f);
        Assert.True(pedal.Step(true, false, false, false, 10.50f + VrFireDoorPedal.HoldDelay).Pressed);
        var ui = pedal.Step(true, false, false, pointingAtUi: true, 10.80f);
        Assert.False(ui.Held);
        Assert.False(pedal.Active);
        Assert.False(pedal.Step(true, false, false, pointingAtUi: false, 11.20f).Held);
    }

    [Fact]
    public void PedalDrivesAutoFireDoorCycleIncludingCrackedDoorRestore()
    {
        var pedal = new VrFireDoorPedal();
        var door = new AutoFireDoor();
        var opening = 0.2f; // cracked door

        void Frame(bool trigger, bool scroll, float now)
        {
            var sig = pedal.Step(trigger, scroll, scroll, false, now);
            if (sig.Pressed)
                door.Press(opening, now);
            if (door.Step(opening, sig.Held, now) is float set)
                opening = set;
        }

        // Hold trigger cleanly for 0.8 s -> door opens fully.
        for (var i = 0; i <= 40; i++)
            Frame(trigger: true, scroll: false, 10f + i * 0.02f);
        Assert.Equal(1f, opening);
        Assert.Equal(AutoFireDoorPhase.Open, door.Phase);

        // Release trigger -> door returns to its 0.2 crack.
        for (var i = 1; i <= 30; i++)
            Frame(trigger: false, scroll: false, 10.8f + i * 0.02f);
        Assert.Equal(0.2f, opening, 4);
        Assert.False(door.Working);
    }
}
