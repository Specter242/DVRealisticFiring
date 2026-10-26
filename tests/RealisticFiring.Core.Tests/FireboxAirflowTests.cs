using System;
using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

 public class FireboxAirflowTests
{
    private const float Dt = 1f / 30f;

    private static FireboxAirflow Run(Func<int, float> burn, float seconds, float speed = 1.2f)
    {
        var flow = new FireboxAirflow(1.03f, 1.46f);
        var burns = new float[flow.Across];
        var heats = new float[flow.Across];
        var surface = new float[flow.Across];
        for (var i = 0; i < flow.Across; i++)
        {
            burns[i] = burn(i);
            heats[i] = burns[i];
            surface[i] = 0.3f;
        }
        for (var t = 0f; t < seconds; t += Dt)
            flow.Step(Dt, burns, heats, surface, speed);
        return flow;
    }

    private static float Mean(FireboxAirflow flow, Func<int, int, float> value, int i0, int i1, int j0, int j1)
    {
        var sum = 0f;
        var n = 0;
        for (var j = j0; j < j1; j++)
            for (var i = i0; i < i1; i++, n++)
                sum += value(i, j);
        return sum / n;
    }

    [Fact]
    public void AnEvenFireRisesAndTurnsInToTheTubesAtTheTop()
    {
        var flow = Run(_ => 1f, 6f);
        var a = flow.Across;
        var top = flow.Up - 3;
        var middleUp = Mean(flow, flow.VelocityUp, a / 3, 2 * a / 3, flow.Up / 2, flow.Up / 2 + 3);
        var leftTopAcross = Mean(flow, flow.VelocityAcross, 1, a / 4, top, flow.Up);
        var rightTopAcross = Mean(flow, flow.VelocityAcross, 3 * a / 4, a - 1, top, flow.Up);

        Assert.True(middleUp > 0.5f, $"up {middleUp:F2} m/s half way up");
        Assert.True(leftTopAcross > 0.05f, $"left at the top {leftTopAcross:F2} m/s across");
        Assert.True(rightTopAcross < -0.05f, $"right at the top {rightTopAcross:F2} m/s across");
    }

    [Fact]
    public void AFireHotterOnTheLeftRollsItsGasOverToTheRight()
    {
        var flow = Run(i => i < 8 ? 1f : 0.2f, 6f);
        var a = flow.Across;
        var leftUp = Mean(flow, flow.VelocityUp, 1, a / 3, flow.Up / 3, 2 * flow.Up / 3);
        var rightUp = Mean(flow, flow.VelocityUp, 2 * a / 3, a - 1, flow.Up / 3, 2 * flow.Up / 3);
        var highAcross = Mean(flow, flow.VelocityAcross, a / 4, 3 * a / 4, 2 * flow.Up / 3, flow.Up);

        Assert.True(leftUp > rightUp + 0.3f, $"up {leftUp:F2} m/s on the left, {rightUp:F2} on the right");
        Assert.True(highAcross > 0f, $"high up {highAcross:F2} m/s across, toward the right");
    }

    [Fact]
    public void ItsEddiesStartJustAboveTheCoal()
    {
        var flow = new FireboxAirflow(1.03f, 1.46f)
        {
            Buoyancy = 0f,
            Confinement = 0f,
            Kicks = 0f,
            EddyRate = 1000f,
        };
        var none = new float[flow.Across];
        var surface = new float[flow.Across];
        for (var i = 0; i < flow.Across; i++)
            surface[i] = 0.3f;
        for (var step = 0; step < 20; step++)
            flow.Step(0.001f, none, none, surface, 0f);

        var hx = flow.Width / flow.Across;
        var hy = flow.Height / flow.Up;
        var turning = 0f;
        var height = 0f;
        for (var j = 1; j < flow.Up - 1; j++)
        {
            for (var i = 1; i < flow.Across - 1; i++)
            {
                var w = Math.Abs((flow.VelocityUp(i + 1, j) - flow.VelocityUp(i - 1, j)) / (2f * hx)
                    - (flow.VelocityAcross(i, j + 1) - flow.VelocityAcross(i, j - 1)) / (2f * hy));
                turning += w;
                height += w * (j + 0.5f) * hy;
            }
        }
        var above = height / turning - 0.3f;
        Assert.True(above < 0.35f, $"the gas turns {above:F2} m above the coal on average");
    }

    [Fact]
    public void ANewEddyBuildsItsBendGradually()
    {
        var flow = new FireboxAirflow(1.03f, 1.46f)
        {
            Buoyancy = 0f,
            Confinement = 0f,
            Kicks = 0f,
            EddyRate = 1f,
            EddySpin = 2.4f,
        };
        var none = new float[flow.Across];
        var surface = new float[flow.Across];
        Array.Fill(surface, 0.3f);
        float Bend()
        {
            var bend = 0f;
            for (var j = 0; j < flow.Up; j++)
                for (var i = 0; i < flow.Across; i++)
                    bend = Math.Max(bend, Math.Abs(flow.CameFrom(i, j) - (i + 0.5f) * flow.Width / flow.Across));
            return bend;
        }
        for (var step = 0; step < 40 && flow.EddiesStarted == 0; step++)
            flow.Step(Dt, none, none, surface, 0f);
        Assert.Equal(1, flow.EddiesStarted);
        var first = Bend();
        for (var step = 0; step < 12; step++)
            flow.Step(Dt, none, none, surface, 0f);
        var formed = Bend();
        Assert.True(formed > 0.05f, $"the formed eddy bends the flame {formed:F3} m");
        Assert.True(first < 0.25f * formed, $"first frame's bend {first:F3} m against {formed:F3} m formed");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TexturePhasesEaseOutOfTheirRestarts(bool secondPhase)
    {
        var flow = new FireboxAirflow(1f, 1f, across: 4) { Period = 1f, EddyRate = 0f };
        var none = new float[flow.Across];
        if (secondPhase)
            for (var step = 0; step < 500; step++)
                flow.Step(0.001f, none, none, none, 0f);
        flow.Step(0.001f, none, none, none, 0f);
        var weight = secondPhase ? flow.WeightB : flow.WeightA;
        // No finite slope at the reset: a 1 ms timing difference must not
        // reveal a freshly reset texture by a visible share of its contrast.
        Assert.InRange(weight, 0f, 0.0001f);
        Assert.InRange(flow.WeightA + flow.WeightB, 0.99999f, 1.00001f);
    }

    [Fact]
    public void SmokeFillsAStillBoxAndStreamsOutOfAWorkingOne()
    {
        float UpperSmoke(float speed, float burning)
        {
            var flow = new FireboxAirflow(1.03f, 1.46f);
            var burns = new float[flow.Across];
            var heats = new float[flow.Across];
            var surface = new float[flow.Across];
            var smoke = new float[flow.Across];
            for (var i = 0; i < flow.Across; i++)
            {
                burns[i] = heats[i] = burning;
                surface[i] = 0.3f;
                smoke[i] = 1f;
            }
            for (var t = 0f; t < 8f; t += Dt)
                flow.Step(Dt, burns, heats, surface, speed, smoke);
            for (var j = 0; j < flow.Up; j++)
                for (var i = 0; i < flow.Across; i++)
                    Assert.True(float.IsFinite(flow.Smoke(i, j)) && flow.Smoke(i, j) >= 0f, $"smoke at {i},{j}");
            return Mean(flow, flow.Smoke, 0, flow.Across, flow.Up / 2, flow.Up);
        }

        var standing = UpperSmoke(0.05f, 0.15f);
        var working = UpperSmoke(1.2f, 1f);
        Assert.True(standing > 0.3f, $"standing, the upper box holds {standing:F2} of the coal's smoke");
        // Thinner working: diluted in faster gas (partly: a shovelful's smoke
        // mixes into it only partly) and carried out to the tubes.
        Assert.True(working < 0.8f * standing, $"working, the upper box holds {working:F2} against {standing:F2} standing");
    }

    [Fact]
    public void ItsCoordinatesRideTheGasAndStayFinite()
    {
        var flow = Run(_ => 1f, 30f, speed: 2.5f);
        for (var j = 0; j < flow.Up; j++)
        {
            for (var i = 0; i < flow.Across; i++)
            {
                Assert.True(float.IsFinite(flow.VelocityAcross(i, j)) && float.IsFinite(flow.VelocityUp(i, j)), $"velocity at {i},{j}");
                Assert.True(float.IsFinite(flow.PhaseAUp(i, j)) && float.IsFinite(flow.PhaseBUp(i, j)), $"coordinates at {i},{j}");
                Assert.InRange(flow.CameFrom(i, j), 0f, flow.Width);
            }
        }
        Assert.InRange(flow.WeightA + flow.WeightB, 0.999f, 1.001f);
        // Each phase restarts once a period: 30 s of 1.4 s periods.
        Assert.InRange(flow.RestartsA, 20, 22);
        Assert.InRange(flow.RestartsB, 20, 22);

        // Rising gas came from lower down: its coordinates lie under it.
        var i0 = flow.Across / 2;
        var j0 = flow.Up / 2;
        var y = (j0 + 0.5f) * flow.Height / flow.Up;
        Assert.True(flow.PhaseAUp(i0, j0) < y + 0.01f && flow.PhaseBUp(i0, j0) < y + 0.01f,
            $"coordinates {flow.PhaseAUp(i0, j0):F2} and {flow.PhaseBUp(i0, j0):F2} m against {y:F2}");
    }
}
