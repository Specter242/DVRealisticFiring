using Xunit;
namespace RealisticFiring.Core.Tests;

public class AutoFireDoorTests
{
    private const float Frame = 0.02f;

    // Runs the door frame by frame, as the game would, writing back what the cycle asks.
    private sealed class Rig
    {
        public readonly AutoFireDoor Cycle = new AutoFireDoor();
        public float Door;
        public float Now = 10f;
        public float Widest;

        public void Run(float seconds, bool held = false)
        {
            var end = Now + seconds - Frame / 2f;
            while (Now < end)
            {
                Now += Frame;
                if (Cycle.Step(Door, held, Now) is float set)
                    Door = set;
                Widest = System.Math.Max(Widest, Door);
            }
        }

        public void Press() => Cycle.Press(Door, Now);
    }

    [Fact] public void HoldingOpensTheDoorFullyWithinItsStroke()
    {
        var rig = new Rig();
        rig.Press();
        rig.Run(AutoFireDoor.Stroke + 2f * Frame, held: true);
        Assert.Equal(1f, rig.Door);
        Assert.Equal(AutoFireDoorPhase.Open, rig.Cycle.Phase);
    }

    [Fact] public void TheDoorIsOpenEnoughToShovelBeforeTheSwingEnds()
    {
        var rig = new Rig();
        rig.Press();
        rig.Run(AutoFireDoor.Stroke * 0.7f, held: true);
        Assert.True(rig.Door >= AutoFireDoor.ShovelOpening);
    }

    [Fact] public void HeldTheDoorStaysOpenAsLongAsTheKey()
    {
        var rig = new Rig();
        rig.Press();
        rig.Run(60f, held: true);
        Assert.Equal(1f, rig.Door);
        Assert.Equal(AutoFireDoorPhase.Open, rig.Cycle.Phase);
    }

    [Fact] public void LettingGoShutsTheDoorAtOnce()
    {
        var rig = new Rig();
        rig.Press();
        rig.Run(2f, held: true);
        rig.Run(Frame);
        Assert.Equal(AutoFireDoorPhase.Closing, rig.Cycle.Phase);
        Assert.True(rig.Door < 1f);
        rig.Run(AutoFireDoor.Stroke + Frame);
        Assert.Equal(0f, rig.Door);
        Assert.False(rig.Cycle.Working);
    }

    [Fact] public void AQuickTapBarelyOpensTheDoorAndShutsIt()
    {
        var rig = new Rig();
        rig.Press();
        rig.Run(Frame, held: true);
        rig.Run(1f);
        Assert.True(rig.Widest <= 2f * Frame / AutoFireDoor.Stroke + 1e-4f);
        Assert.Equal(0f, rig.Door);
        Assert.False(rig.Cycle.Working);
    }

    [Fact] public void LetGoWhileStillOpeningItShutsFromWhereItGot()
    {
        var rig = new Rig();
        rig.Press();
        rig.Run(AutoFireDoor.Stroke / 2f, held: true);
        var reached = rig.Door;
        rig.Run(Frame);
        Assert.True(rig.Door < reached);
        rig.Run(AutoFireDoor.Stroke);
        Assert.Equal(0f, rig.Door);
    }

    [Fact] public void PressedAgainAsItShutsItSwingsBackOpen()
    {
        var rig = new Rig();
        rig.Press();
        rig.Run(1f, held: true);
        rig.Run(AutoFireDoor.Stroke / 2f);
        rig.Press();
        rig.Run(AutoFireDoor.Stroke, held: true);
        Assert.Equal(1f, rig.Door);
    }

    [Fact] public void ACrackedDoorGoesBackToItsCrack()
    {
        var rig = new Rig { Door = 0.2f };
        rig.Press();
        rig.Run(1f, held: true);
        rig.Run(AutoFireDoor.Stroke + Frame);
        Assert.Equal(0.2f, rig.Door);
        Assert.False(rig.Cycle.Working);
    }

    [Fact] public void ADoorLeftWideOpenGoesShut()
    {
        var rig = new Rig { Door = AutoFireDoor.ShovelOpening };
        rig.Press();
        rig.Run(1f, held: true);
        rig.Run(AutoFireDoor.Stroke + Frame);
        Assert.Equal(0f, rig.Door);
    }

    [Fact] public void ASecondPressKeepsWhereTheDoorRested()
    {
        var rig = new Rig { Door = 0.2f };
        rig.Press();
        rig.Run(AutoFireDoor.Stroke + 2f * Frame, held: true);
        rig.Press();
        Assert.Equal(0.2f, rig.Cycle.Rest);
    }

    [Fact] public void ANotchTheDoorSettlesOnWhileHeldLeavesItOpen()
    {
        var rig = new Rig();
        rig.Press();
        rig.Run(AutoFireDoor.Stroke + 2f * Frame, held: true);
        rig.Door = 0.9f;
        rig.Run(5f, held: true);
        Assert.Equal(0.9f, rig.Door);
        Assert.Equal(AutoFireDoorPhase.Open, rig.Cycle.Phase);
    }

    [Fact] public void AHandOnTheDoorWhileHeldDoesNotStopItShutting()
    {
        var rig = new Rig();
        rig.Press();
        rig.Run(1f, held: true);
        rig.Door = 0.5f;
        rig.Run(1f, held: true);
        rig.Run(AutoFireDoor.Stroke + Frame);
        Assert.Equal(0f, rig.Door);
        Assert.False(rig.Cycle.Working);
    }

    [Theory]
    [InlineData(AutoFireDoorPhase.Idle, AutoFireDoorPhase.Opening, AutoFireDoorAir.Admitted)]
    [InlineData(AutoFireDoorPhase.Idle, AutoFireDoorPhase.Open, AutoFireDoorAir.Admitted)]
    [InlineData(AutoFireDoorPhase.Closing, AutoFireDoorPhase.Opening, AutoFireDoorAir.Admitted)]
    [InlineData(AutoFireDoorPhase.Open, AutoFireDoorPhase.Closing, AutoFireDoorAir.Exhausted)]
    [InlineData(AutoFireDoorPhase.Opening, AutoFireDoorPhase.Closing, AutoFireDoorAir.Exhausted)]
    [InlineData(AutoFireDoorPhase.Opening, AutoFireDoorPhase.Open, AutoFireDoorAir.None)]
    [InlineData(AutoFireDoorPhase.Open, AutoFireDoorPhase.Opening, AutoFireDoorAir.None)]
    [InlineData(AutoFireDoorPhase.Open, AutoFireDoorPhase.Idle, AutoFireDoorAir.Exhausted)]
    [InlineData(AutoFireDoorPhase.Opening, AutoFireDoorPhase.Idle, AutoFireDoorAir.Exhausted)]
    [InlineData(AutoFireDoorPhase.Closing, AutoFireDoorPhase.Idle, AutoFireDoorAir.None)]
    [InlineData(AutoFireDoorPhase.Idle, AutoFireDoorPhase.Idle, AutoFireDoorAir.None)]
    public void TheAirIsHeardWhenTheDoorStartsEachWay(AutoFireDoorPhase before, AutoFireDoorPhase after, AutoFireDoorAir air) =>
        Assert.Equal(air, AutoFireDoor.AirFor(before, after));

    [Theory]
    [InlineData(2f)]
    [InlineData(Frame)]
    public void AHoldOrATapIsOneBreathInAndOneOut(float heldFor)
    {
        var rig = new Rig();
        var heard = new System.Collections.Generic.List<AutoFireDoorAir>();
        var last = rig.Cycle.Phase;
        void Listen()
        {
            var air = AutoFireDoor.AirFor(last, rig.Cycle.Phase);
            last = rig.Cycle.Phase;
            if (air != AutoFireDoorAir.None)
                heard.Add(air);
        }
        rig.Press();
        Listen();
        for (var t = 0f; t < heldFor + AutoFireDoor.Stroke + 0.5f; t += Frame)
        {
            rig.Run(Frame, held: t < heldFor);
            Listen();
        }
        Assert.Equal(new[] { AutoFireDoorAir.Admitted, AutoFireDoorAir.Exhausted }, heard);
        Assert.Equal(0f, rig.Door);
    }

    [Fact] public void AnIdleDoorIsLeftAlone()
    {
        var cycle = new AutoFireDoor();
        Assert.Null(cycle.Step(0.3f, held: true, 1f));
        Assert.False(cycle.Working);
    }
}
