using System;
using Xunit;
namespace RealisticFiring.Core.Tests;

public class FireholeGateTests
{
    // An S282-like firehole: 0.40 m wide, 0.30 m high, its plane at z 0,
    // and a box behind it to the grate's edges, the grate 0.45 m below the
    // hole's bottom, the crown 0.6 m over its top, 2.4 m deep.
    private static readonly FireholeGate Gate = new FireholeGate(-0.20f, 0.20f, 1.75f, 2.05f, 0f,
        -0.75f, 0.75f, 1.30f, 2.65f, 2.4f);

    private static CarPoint P(float x, float y, float z) => new CarPoint(x, y, z);

    [Fact] public void ThroughTheOpenHoleEnters()
    {
        Assert.Equal(FireholeEntry.Through, Gate.Classify(P(0, 1.9f, -0.04f), P(0.02f, 1.88f, 0.03f)));
        // A long frame's step still crosses within the hole.
        Assert.Equal(FireholeEntry.Through, Gate.Classify(P(0, 2.2f, -0.5f), P(0, 1.6f, 0.5f)));
    }

    [Fact] public void BackheadBelowBesideOrAboveTheHoleDoesNot()
    {
        // Through the plate below the door (the old broad box took this).
        Assert.Equal(FireholeEntry.Stray, Gate.Classify(P(0, 1.6f, -0.03f), P(0, 1.6f, 0.03f)));
        // Beside the door, inside the grate's width.
        Assert.Equal(FireholeEntry.Stray, Gate.Classify(P(0.45f, 1.9f, -0.03f), P(0.45f, 1.9f, 0.03f)));
        // Above the door, under the crown.
        Assert.Equal(FireholeEntry.Stray, Gate.Classify(P(0, 2.3f, -0.03f), P(0, 2.3f, 0.03f)));
        // A piece resting against the plate just below the door, jittering.
        Assert.Equal(FireholeEntry.None, Gate.Classify(P(0, 1.70f, -0.031f), P(0, 1.70f, -0.029f)));
    }

    [Fact] public void TopAndSideEntryIntoTheBoxIsStray()
    {
        // Dropped in from above the crown, and through a side sheet.
        Assert.Equal(FireholeEntry.Stray, Gate.Classify(P(0, 2.75f, 0.8f), P(0, 2.6f, 0.8f)));
        Assert.Equal(FireholeEntry.Stray, Gate.Classify(P(0.8f, 1.8f, 1f), P(0.7f, 1.8f, 1f)));
        // Moving about inside once there is nothing new.
        Assert.Equal(FireholeEntry.None, Gate.Classify(P(0, 1.8f, 0.4f), P(0, 1.5f, 0.5f)));
    }

    [Fact] public void MissesOutsideTheBoxAreNothing()
    {
        Assert.Equal(FireholeEntry.None, Gate.Classify(P(0, 1.9f, -0.6f), P(0, 1.8f, -0.4f)));
        // Thrown past the side of the boiler, out of the cab.
        Assert.Equal(FireholeEntry.None, Gate.Classify(P(0.9f, 1.9f, -0.03f), P(0.95f, 1.9f, 0.05f)));
    }

    [Fact] public void ReleaseAlreadyInsideCountsOnlyWithTheReachThroughTheHole()
    {
        var eye = P(0, 2.95f, -0.45f);
        // Hand reached in through the door, the piece let go just inside.
        Assert.Equal(FireholeEntry.ReleasedInside, Gate.ClassifyRelease(eye, P(0, 1.80f, 0.10f)));
        // The held piece clipped through the plate above the door: the reach
        // crosses the plane outside the hole.
        Assert.Equal(FireholeEntry.Stray, Gate.ClassifyRelease(eye, P(0, 2.4f, 0.1f)));
        // Let go in the cab: nothing to decide yet.
        Assert.Equal(FireholeEntry.None, Gate.ClassifyRelease(eye, P(0, 2.2f, -0.2f)));
    }

    // Car frame <-> world: a car moving along world z at its speed, yawed.
    private static (float x, float y, float z) ToWorld(CarPoint p, float carZ, float yaw)
    {
        var c = (float)Math.Cos(yaw); var s = (float)Math.Sin(yaw);
        return (c * p.X + s * p.Z, p.Y, -s * p.X + c * p.Z + carZ);
    }
    private static CarPoint ToCar((float x, float y, float z) w, float carZ, float yaw)
    {
        var c = (float)Math.Cos(yaw); var s = (float)Math.Sin(yaw);
        var dz = w.z - carZ;
        return P(c * w.x - s * dz, w.y, s * w.x + c * dz);
    }

    [Fact] public void MovingEngineFramesKeepRestingPiecesOutAndThrowsIn()
    {
        // The engine reversing at 25 m/s on a curve, one 0.02 s frame.
        const float dt = 0.02f, speed = -25f;
        var carBefore = 100f; var yawBefore = 0.10f;
        var carAfter = carBefore + speed * dt; var yawAfter = 0.1004f;
        // A piece lying still on the footplate by the door: each endpoint in
        // the frame of its own moment, it has not moved.
        var resting = P(0, 1.70f, -0.08f);
        var a = ToCar(ToWorld(resting, carBefore, yawBefore), carBefore, yawBefore);
        var b = ToCar(ToWorld(resting, carAfter, yawAfter), carAfter, yawAfter);
        Assert.Equal(FireholeEntry.None, Gate.Classify(a, b));
        Assert.True(Math.Abs(b.Z - a.Z) < 1e-3f);
        // A throw through the door in that frame enters.
        var flying = ToWorld(P(0, 1.95f, -0.03f), carBefore, yawBefore);
        var landed = ToWorld(P(0, 1.92f, 0.03f), carAfter, yawAfter);
        var to = ToCar(landed, carAfter, yawAfter);
        Assert.Equal(FireholeEntry.Through, Gate.Classify(ToCar(flying, carBefore, yawBefore), to));
        // Taking last frame's point in this frame instead puts it half a
        // metre inside already, and the throw would be missed.
        var naive = ToCar(flying, carAfter, yawAfter);
        Assert.True(naive.Z > 0.4f);
        Assert.Equal(FireholeEntry.None, Gate.Classify(naive, to));
    }

    private static GrateZone Land(CarPoint at, CarPoint v, CarPoint car) =>
        FireholeGate.LandingZone(at, v, car, -0.75f, 0.75f, 0f, 2.4f, 1.6f, -9.81f);

    [Fact] public void LandingFollowsTheFlightRelativeToTheEngine()
    {
        var at = P(0, 1.9f, 0.05f);
        var still = P(0, 0, 0);
        // A throw falling through the door comes down at the back, under it.
        Assert.Equal(GrateZone.UnderDoor, Land(at, P(0, -1, 1.5f), still));
        // The same throw on an engine running at 25 m/s: the piece's own
        // velocity includes the car's, which must not carry it to the front.
        Assert.Equal(GrateZone.UnderDoor, Land(at, P(0, -1, 26.5f), P(0, 0, 25f)));
        Assert.Equal(GrateZone.UnderDoor, Land(at, P(0, -1, -23.5f), P(0, 0, -25f)));
        // A flat, hard throw goes deeper, and sideways to its side.
        Assert.Equal(GrateZone.Center, Land(at, P(0, 0, 4f), still));
        Assert.Equal(GrateZone.BackRight, Land(at, P(1.8f, 0, 0.5f), still));
    }

    [Fact] public void ThrownRagLandsWhereTheThrownWoodDid()
    {
        // r21: wood and the rag crossed the S282's door at about 3.4 m/s
        // along and 1.7 m/s down; the rag was put in the centre zone, off the
        // pile, by the old 0.3 s carry. Both come down by the door.
        var grate = (minX: -0.5f, maxX: 0.5f, minZ: 0.2f, maxZ: 2.2f, top: 1.8f);
        var wood = FireholeGate.LandingZone(P(0.03f, 1.91f, 0.10f), P(-0.68f, -1.70f, 3.36f), P(0, 0, 0), grate.minX, grate.maxX, grate.minZ, grate.maxZ, grate.top, -9.81f);
        var rag = FireholeGate.LandingZone(P(0.04f, 1.95f, 0.06f), P(-0.61f, -1.73f, 3.34f), P(0, 0, 0), grate.minX, grate.maxX, grate.minZ, grate.maxZ, grate.top, -9.81f);
        Assert.Equal(GrateZone.UnderDoor, wood);
        Assert.Equal(wood, rag);
    }

    [Fact] public void SurveyedApertureExcludesTheFrameInsideTheRectangle()
    {
        // A 1 m by 0.5 m outline (the game's door target) over a doorway open
        // only in its middle half and upper three rows.
        var gate = new FireholeGate(-0.5f, 0.5f, 1.6f, 2.1f, 0f, -0.8f, 0.8f, 1.0f, 2.7f, 2.3f);
        var open = new bool[8 * 5];
        for (var r = 1; r < 5; r++) for (var c = 2; c < 6; c++) open[r * 8 + c] = true;
        gate.SetAperture(open, 8, 5);
        Assert.Equal(FireholeEntry.Through, gate.Classify(P(0, 1.9f, -0.05f), P(0, 1.9f, 0.05f)));
        // On the plate beside the doorway, inside the outline.
        Assert.Equal(FireholeEntry.Stray, gate.Classify(P(0.4f, 1.9f, -0.05f), P(0.4f, 1.9f, 0.05f)));
        // On the lip below it.
        Assert.Equal(FireholeEntry.Stray, gate.Classify(P(0, 1.65f, -0.05f), P(0, 1.65f, 0.05f)));
        Assert.Throws<ArgumentException>(() => gate.SetAperture(new bool[3], 8, 5));
    }

    [Fact] public void PartOpenDoorNarrowsTheDoorway()
    {
        var gate = new FireholeGate(-0.2f, 0.2f, 1.75f, 2.05f, 0f, -0.75f, 0.75f, 1.3f, 2.65f, 2.4f);
        gate.Opening = 0.3f; // 12 cm of the 40 cm open, about the middle
        Assert.Equal(FireholeEntry.Through, gate.Classify(P(0.03f, 1.9f, -0.05f), P(0.03f, 1.9f, 0.05f)));
        Assert.Equal(FireholeEntry.Stray, gate.Classify(P(0.12f, 1.9f, -0.05f), P(0.12f, 1.9f, 0.05f)));
        gate.Opening = 0f;
        Assert.Equal(FireholeEntry.Stray, gate.Classify(P(0, 1.9f, -0.05f), P(0, 1.9f, 0.05f)));
        Assert.Equal(FireholeEntry.Stray, gate.ClassifyRelease(P(0, 2.95f, -0.45f), P(0, 1.80f, 0.10f)));
        gate.Opening = float.NaN;
        Assert.Equal(0f, gate.Opening);
    }

    [Fact] public void DegenerateHoleIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new FireholeGate(0, 0, 1, 2, 0, -1, 1, 0, 3, 2));
    }
}

public class FittedFireholeGateTests
{
    // The S282's fitted hole (FireholeOpening): middle across 0.010 m, foot
    // 1.720 m, 0.450 by 0.456 m, in the plane z 0.054 m; its backhead's face
    // surveyed at z 0.10 m, 4.6 cm behind it. The box behind to the grate.
    private const float Middle = 0.010f, Foot = 1.720f, Width = 0.450f, Height = 0.456f, Plane = 0.054f, Depth = 0.15f;
    private static FireholeGate Gate() => FireholeGate.Fitted(Middle, Foot, Width, Height, Plane, Depth,
        -0.55f, 0.55f, 1.25f, 2.75f, 2.4f);
    private static CarPoint P(float x, float y, float z) => new CarPoint(x, y, z);

    [Fact] public void TheFittedHoleIsTheDoorway()
    {
        var gate = Gate();
        // Foot is the bottom edge, Foot + Height the top, Middle ± Width/2 across, Plane the plane.
        Assert.Equal(Foot, gate.HoleMinY, 4);
        Assert.Equal(Foot + Height, gate.HoleMaxY, 4);
        Assert.Equal(Middle - Width / 2f, gate.HoleMinX, 4);
        Assert.Equal(Middle + Width / 2f, gate.HoleMaxX, 4);
        Assert.Equal(Plane, gate.Plane, 4);
        Assert.False(gate.Surveyed);
    }

    [Fact] public void APieceCrossingTheFittedHoleEnters()
    {
        var gate = Gate();
        var mid = Foot + Height / 2f;
        Assert.Equal(FireholeEntry.Through, gate.Classify(P(Middle, mid, Plane - 0.04f), P(Middle, mid - 0.01f, Plane + 0.03f)));
        // Near each edge, still inside it.
        Assert.Equal(FireholeEntry.Through, gate.Classify(P(Middle - Width / 2f + 0.02f, Foot + 0.02f, Plane - 0.05f), P(Middle - Width / 2f + 0.02f, Foot + 0.02f, Plane + 0.05f)));
        Assert.Equal(FireholeEntry.Through, gate.Classify(P(Middle + Width / 2f - 0.02f, Foot + Height - 0.02f, Plane - 0.05f), P(Middle + Width / 2f - 0.02f, Foot + Height - 0.02f, Plane + 0.05f)));
    }

    [Fact] public void ThePlateAroundTheFittedHoleDoesNotTakeAPiece()
    {
        var gate = Gate();
        var mid = Foot + Height / 2f;
        // Thrown at the backhead just below, beside, and above the door: the
        // piece reaches the face 4.6 cm behind the fitted plane and bounces
        // back. Neither in the fire nor sent back out.
        foreach (var (x, y) in new[] { (Middle, Foot - 0.05f), (Middle + Width / 2f + 0.06f, mid), (Middle, Foot + Height + 0.05f) })
        {
            Assert.Equal(FireholeEntry.None, gate.Classify(P(x, y, Plane - 0.10f), P(x, y, Plane + 0.04f)));
            Assert.Equal(FireholeEntry.None, gate.Classify(P(x, y, Plane + 0.04f), P(x, y, Plane - 0.05f)));
        }
        // A piece lying on the cab floor against the backhead, jittering.
        Assert.Equal(FireholeEntry.None, gate.Classify(P(0.3f, 1.40f, Plane + 0.02f), P(0.3f, 1.40f, Plane + 0.03f)));
    }

    [Fact] public void APieceForcedThroughThePlateIsStray()
    {
        var gate = Gate();
        // Past the frame's depth beside the door, not through the hole: through a wall.
        Assert.Equal(FireholeEntry.Stray, gate.Classify(P(Middle + Width / 2f + 0.1f, Foot + 0.2f, Plane + 0.04f), P(Middle + Width / 2f + 0.1f, Foot + 0.2f, Plane + Depth + 0.05f)));
        Assert.Equal(FireholeEntry.Stray, gate.Classify(P(Middle, Foot - 0.2f, Plane - 0.05f), P(Middle, Foot - 0.2f, Plane + 0.5f)));
    }

    [Fact] public void AThrowThatMeetsTheMakersCollidersInTheDoorwayGoesIn()
    {
        // The FEF-3's FEF3_Cab.007 meets a split's end 5.5 cm before its
        // fitted plane, the 8F's Items 1 15 cm before: at 4.5 m/s, into the box.
        var gate = Gate();
        var mid = Foot + Height / 2f;
        Assert.True(gate.StrikesDoorway(P(Middle, mid, Plane - 0.055f), P(0f, -0.2f, 4.5f)));
        Assert.True(gate.StrikesDoorway(P(Middle + 0.05f, mid + 0.1f, Plane - 0.15f), P(0.3f, -1.0f, 3.8f)));
        // Thrown from the fireman's place, aslant, and dropping.
        Assert.True(gate.StrikesDoorway(P(Middle - 0.12f, Foot + 0.08f, Plane - 0.08f), P(-0.9f, -1.6f, 3.0f), -9.81f));
    }

    [Fact] public void ItIsThePiecesFlightNotWhereItsEndTouchesThatCounts()
    {
        var gate = Gate();
        var mid = Foot + Height / 2f;
        // Dropping steeply from the firing position: its middle 30 cm out and
        // 15 cm over the doorway's top, its end on the 8F's Items 1, on a
        // flight through the doorway's upper half.
        Assert.True(gate.StrikesDoorway(P(Middle + 0.04f, Foot + Height + 0.15f, Plane - 0.30f), P(-0.4f, -2.2f, 3.4f), -9.81f));
        // Its middle inside the outline, flying out past the doorway's side.
        Assert.False(gate.StrikesDoorway(P(Middle + Width / 2f - 0.06f, mid, Plane - 0.30f), P(1.6f, 0f, 3.0f)));
        // Its middle in front of the doorway, falling short below it.
        Assert.False(gate.StrikesDoorway(P(Middle, Foot + 0.08f, Plane - 0.30f), P(0f, -1.2f, 1.0f), -9.81f));
    }

    [Fact] public void AStrikeOutsideTheDoorwayOrNotThrownInIsNot()
    {
        var gate = Gate();
        var mid = Foot + Height / 2f;
        var inward = P(0f, -0.2f, 4.5f);
        // The plate beside, below, and above the door.
        Assert.False(gate.StrikesDoorway(P(Middle + Width / 2f + 0.12f, mid, Plane - 0.05f), inward));
        Assert.False(gate.StrikesDoorway(P(Middle, Foot - 0.10f, Plane - 0.05f), inward));
        Assert.False(gate.StrikesDoorway(P(Middle, Foot + Height + 0.06f, Plane - 0.05f), inward));
        // The doorway's very edge: the frame.
        Assert.False(gate.StrikesDoorway(P(Middle + Width / 2f - 0.01f, mid, Plane - 0.05f), inward));
        Assert.False(gate.StrikesDoorway(P(Middle, Foot + 0.01f, Plane - 0.05f), inward));
        // Far in front of the door: something else in the cab.
        Assert.False(gate.StrikesDoorway(P(Middle, mid, Plane - 0.6f), inward));
        // Resting, rolling, or bouncing back out.
        Assert.False(gate.StrikesDoorway(P(Middle, mid, Plane - 0.05f), P(0f, -0.5f, 0.2f)));
        Assert.False(gate.StrikesDoorway(P(Middle, mid, Plane - 0.05f), P(0f, 0f, -2f)));
    }

    [Fact] public void AThrowAimedThroughTheOpenDoorwayIsAimedJustInsideIt()
    {
        var gate = Gate();
        var mid = Foot + Height / 2f;
        // The fireman 0.85 m before the door, his eye 1.07 m over its middle;
        // past the doorway the view meets a collider 1.1 m in, far below.
        var eye = P(Middle, mid + 1.07f, Plane - 0.85f);
        var far = P(Middle, mid - 1.36f, Plane + 1.11f);
        Assert.True(gate.AimsThrough(eye, far, out var aim));
        Assert.Equal(Plane + 0.15f * 0.85f / 1.364f, aim.Z, 2);
        Assert.InRange(aim.Y, mid - 0.15f, mid - 0.05f);
        // The makers' colliders across the doorway, 15 cm before it, met in the outline.
        var near = P(Middle, mid + 0.19f, Plane - 0.15f);
        Assert.True(gate.AimsThrough(eye, near, out aim));
        Assert.True(aim.Z > Plane);
    }

    [Fact] public void AThrowAimedElsewhereKeepsItsTarget()
    {
        var gate = Gate();
        var mid = Foot + Height / 2f;
        var eye = P(Middle, mid + 1.07f, Plane - 0.85f);
        // The plate beside the door, the floor before it, a shut door.
        Assert.False(gate.AimsThrough(eye, P(Middle + Width / 2f + 0.12f, mid, Plane), out var aim));
        Assert.Equal(Plane, aim.Z);
        Assert.False(gate.AimsThrough(eye, P(Middle, Foot - 0.6f, Plane - 0.5f), out _));
        Assert.False(gate.AimsThrough(P(Middle, mid, Plane + 0.2f), P(Middle, mid, Plane + 1f), out _));
        gate.Opening = 0f;
        Assert.False(gate.AimsThrough(eye, P(Middle, mid - 1.36f, Plane + 1.11f), out _));
    }

    [Fact] public void AShutOrPartOpenDoorStopsAStrike()
    {
        var gate = Gate();
        var mid = Foot + Height / 2f;
        var inward = P(0f, -0.2f, 4.5f);
        gate.Opening = 0f;
        Assert.False(gate.StrikesDoorway(P(Middle, mid, Plane - 0.05f), inward));
        // A third open: the middle third is open, the leaves stand either side.
        gate.Opening = 1f / 3f;
        Assert.True(gate.StrikesDoorway(P(Middle, mid, Plane - 0.05f), inward));
        Assert.False(gate.StrikesDoorway(P(Middle + Width * 0.35f, mid, Plane - 0.05f), inward));
    }

    [Fact] public void ASurveyedGateKeepsItsOldStrayRule()
    {
        // No frame depth: behind the plane outside the hole is stray at once, as before.
        var gate = new FireholeGate(-0.20f, 0.20f, 1.75f, 2.05f, 0f, -0.75f, 0.75f, 1.30f, 2.65f, 2.4f);
        Assert.Equal(0f, gate.StrayDepth);
        Assert.Equal(FireholeEntry.Stray, gate.Classify(P(0.45f, 1.9f, -0.03f), P(0.45f, 1.9f, 0.03f)));
    }
}
