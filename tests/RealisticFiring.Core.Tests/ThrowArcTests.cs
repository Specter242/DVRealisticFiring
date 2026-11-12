using System;
using Xunit;
namespace RealisticFiring.Core.Tests;

public class ThrowArcTests
{
    // Flies a point under gravity; returns where it is when it reaches the target's depth.
    private static CarPoint Fly(CarPoint from, CarPoint v, float targetZ, float g = -9.81f)
    {
        float x = from.X, y = from.Y, z = from.Z, vy = v.Y;
        const float dt = 0.0005f;
        for (var t = 0f; t < 3f && z < targetZ; t += dt) { x += v.X * dt; y += vy * dt; z += v.Z * dt; vy += g * dt; }
        return new CarPoint(x, y, z);
    }

    [Fact] public void ThrowFromTheFiringPositionReachesTheDoorWhereLookedAt()
    {
        // r20: released 0.19 m right of the door's line at 2.57 m, 0.75 m short of the plane.
        var from = new CarPoint(0.19f, 2.57f, -0.65f);
        var door = new CarPoint(0.0f, 1.93f, 0.10f);
        var v = ThrowArc.Launch(from, door, -9.81f);
        var at = Fly(from, v, door.Z);
        Assert.InRange(at.X, -0.01f, 0.01f);
        Assert.InRange(at.Y, 1.92f, 1.94f);
        var speed = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        Assert.InRange(speed, 3.5, 6.0);
    }

    [Fact] public void LookingAboveTheDoorStillMisses()
    {
        var from = new CarPoint(0.19f, 2.57f, -0.65f);
        var aboveDoor = new CarPoint(0.0f, 2.45f, 0.10f); // the backhead over a 1.70-2.17 m doorway
        var at = Fly(from, ThrowArc.Launch(from, aboveDoor, -9.81f), 0.10f);
        Assert.True(at.Y > 2.17f);
    }

    [Fact] public void FarTargetsAreThrownTowardNotReached()
    {
        var v = ThrowArc.Launch(new CarPoint(0, 0, 0), new CarPoint(0, 0, 10f), -9.81f);
        Assert.Equal(ThrowArc.Speed, v.Z, 3);
        Assert.Equal(0f, v.Y, 3);
        Assert.Equal(0f, ThrowArc.Launch(new CarPoint(1, 1, 1), new CarPoint(1, 1, 1), -9.81f).Z);
    }
}
