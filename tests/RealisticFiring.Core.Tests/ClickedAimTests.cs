using Xunit;
namespace RealisticFiring.Core.Tests;

public class ClickedAimTests
{
    private const float Hold = 3f;
    private readonly object shovel = new object();
    private readonly object door = new object();

    [Fact] public void TheThrowTakesTheAimOfItsClick()
    {
        var aim = new ClickedAim<GrateZone>();
        aim.Click(shovel, door, GrateZone.BackLeft, DoorAimKind.Shovel, 10f);
        Assert.True(aim.TryTake(shovel, door, DoorAimKind.Shovel, 10.4f, Hold, out var zone));
        Assert.Equal(GrateZone.BackLeft, zone);
    }

    [Fact] public void AnAimIsTakenOnce()
    {
        var aim = new ClickedAim<GrateZone>();
        aim.Click(shovel, door, GrateZone.Front, DoorAimKind.Shovel, 10f);
        Assert.True(aim.TryTake(shovel, door, DoorAimKind.Shovel, 10.4f, Hold, out _));
        // A second thrust with no click of its own (another mod's use) falls back to the hover.
        Assert.False(aim.TryTake(shovel, door, DoorAimKind.Shovel, 10.5f, Hold, out _));
        Assert.False(aim.Held);
    }

    [Fact] public void EachClickReplacesTheLast()
    {
        var aim = new ClickedAim<GrateZone>();
        aim.Click(shovel, door, GrateZone.Front, DoorAimKind.Shovel, 10f);
        aim.Click(shovel, door, GrateZone.BackRight, DoorAimKind.Shovel, 10.2f);
        Assert.True(aim.TryTake(shovel, door, DoorAimKind.Shovel, 10.6f, Hold, out var zone));
        Assert.Equal(GrateZone.BackRight, zone);
    }

    [Fact] public void AThrowNeverTakesARakesClickNorTheOtherWayRound()
    {
        var aim = new ClickedAim<GrateZone>();
        aim.Click(shovel, door, GrateZone.MidLeft, DoorAimKind.Rake, 10f);
        Assert.False(aim.TryTake(shovel, door, DoorAimKind.Shovel, 10.4f, Hold, out _));
        aim.Click(shovel, door, GrateZone.MidLeft, DoorAimKind.Shovel, 11f);
        Assert.False(aim.TryTake(shovel, door, DoorAimKind.Rake, 11.4f, Hold, out _));
    }

    [Fact] public void ADifferentShovelOrTargetLeavesTheAimHeld()
    {
        var aim = new ClickedAim<GrateZone>();
        aim.Click(shovel, door, GrateZone.Center, DoorAimKind.Shovel, 10f);
        Assert.False(aim.TryTake(new object(), door, DoorAimKind.Shovel, 10.4f, Hold, out _));
        Assert.False(aim.TryTake(shovel, new object(), DoorAimKind.Shovel, 10.4f, Hold, out _));
        Assert.True(aim.Held);
        Assert.True(aim.TryTake(shovel, door, DoorAimKind.Shovel, 10.5f, Hold, out var zone));
        Assert.Equal(GrateZone.Center, zone);
    }

    [Fact] public void AStaleClickIsDropped()
    {
        var aim = new ClickedAim<GrateZone>();
        aim.Click(shovel, door, GrateZone.Front, DoorAimKind.Shovel, 10f);
        Assert.False(aim.TryTake(shovel, door, DoorAimKind.Shovel, 10f + Hold + 0.01f, Hold, out _));
        Assert.False(aim.Held);
        // At the hold it still counts; a clock that went back does not.
        aim.Click(shovel, door, GrateZone.Front, DoorAimKind.Shovel, 20f);
        Assert.True(aim.TryTake(shovel, door, DoorAimKind.Shovel, 20f + Hold, Hold, out _));
        aim.Click(shovel, door, GrateZone.Front, DoorAimKind.Shovel, 30f);
        Assert.False(aim.TryTake(shovel, door, DoorAimKind.Shovel, 29f, Hold, out _));
    }

    [Fact] public void NothingIsHeldWithoutAToolAndATarget()
    {
        var aim = new ClickedAim<GrateZone>();
        aim.Click(null, door, GrateZone.Front, DoorAimKind.Shovel, 10f);
        Assert.False(aim.Held);
        Assert.False(aim.TryTake(null, door, DoorAimKind.Shovel, 10.1f, Hold, out _));
    }
}
