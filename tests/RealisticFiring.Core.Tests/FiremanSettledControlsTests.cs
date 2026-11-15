using RealisticFiring.Core;
using Xunit.Abstractions;

namespace RealisticFiring.Core.Tests;

 public class FiremanSettledControlsTests
{
    private readonly ITestOutputHelper output;
    public FiremanSettledControlsTests(ITestOutputHelper output) => this.output = output;

    // Where a control is moved, as a hand would see it: a change of more
    // than a lever's slack, and whether it went back within a few seconds.
    private sealed class Moves
    {
        private const float Slack = 0.02f;
        private const float Quick = 3f;
        private float held = float.NaN;
        private int direction;
        private float movedAt = float.NegativeInfinity;

        public int Count { get; private set; }
        public int QuickReversals { get; private set; }

        public void See(float value, float time)
        {
            if (float.IsNaN(held))
            {
                held = value;
                return;
            }
            if (Math.Abs(value - held) <= Slack)
                return;
            var next = Math.Sign(value - held);
            if (direction != 0 && next != direction && time - movedAt < Quick)
                QuickReversals++;
            direction = next;
            movedAt = time;
            held = value;
            Count++;
        }

        public override string ToString() => $"{Count} moves, {QuickReversals} back within 3 s";
    }

    [Fact]
    public void StoppedAfterLightWorkHeSetsTheDamperAndDoorAndLeavesThem()
    {
        // Before: the stack on the black-smoke mark held the damper flicking
        // between 0.13 and 0.4, 1,342 moves in the 20 minutes, 1,245 of them
        // back within 3 s; the door went shut and cracked by turns.
        var engine = new Footplate { EngineSteam = .2f * Footplate.FullWorkSteam };
        engine.Run(600f);
        engine.EngineSteam = 0f;
        engine.Standing = engine.Braking = true;
        var damper = new Moves();
        var door = new Moves();
        var blown = engine.BlownOff;
        engine.Run(1200f, e =>
        {
            damper.See(e.Last.Damper, e.Time);
            if (e.Fireman.RoundLeft == 0 && e.Last.Door < 1f)
                door.See(e.Last.Door, e.Time);
        });

        output.WriteLine($"damper {damper}; door at rest {door}; blown off {engine.BlownOff - blown:F1} kg, pressure {engine.Pressure:F2}");
        Assert.Equal(0, damper.QuickReversals);
        Assert.True(damper.Count <= 60, $"damper moved {damper.Count} times");
        Assert.Equal(0, door.QuickReversals);
        // No more off the safety valve than when the damper fluttered (2 kg).
        Assert.True(engine.BlownOff - blown <= 2f, $"blew off {engine.BlownOff - blown:F1} kg");
        Assert.True(engine.Fire.FireOn);
    }

    [Fact]
    public void CruisingAtTheLightWorkMarkTheDamperStaysPut()
    {
        // Before: work heard either side of 40 % had the damper wide and at
        // his check by turns, 451 moves in the 20 minutes, 358 back within 3 s.
        var engine = new Footplate { EngineSteam = .4f * Footplate.FullWorkSteam };
        var random = new Random(3);
        var damper = new Moves();
        engine.Run(1200f, e =>
        {
            e.EngineSteam = (.4f + .06f * (float)(random.NextDouble() - .5)) * Footplate.FullWorkSteam;
            damper.See(e.Last.Damper, e.Time);
        });

        output.WriteLine($"damper {damper}, pressure {engine.Pressure:F2}");
        Assert.Equal(0, damper.QuickReversals);
        Assert.True(damper.Count <= 30, $"damper moved {damper.Count} times");
    }
}
