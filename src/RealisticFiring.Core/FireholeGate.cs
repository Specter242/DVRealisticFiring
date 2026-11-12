using System;
using System.Linq;

namespace RealisticFiring.Core
{
    /// <summary>A point or vector in a car's own frame: x across, y up, z forward.</summary>
    public readonly struct CarPoint
    {
        public CarPoint(float x, float y, float z) { X = x; Y = y; Z = z; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public static CarPoint operator -(CarPoint a, CarPoint b) => new CarPoint(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public override string ToString() => $"({X:F3}, {Y:F3}, {Z:F3})";
    }

    /// <summary>How a loose item's latest movement relates to the firehole.</summary>
    public enum FireholeEntry
    {
        /// <summary>Still in the cab, or nowhere near the box.</summary>
        None,
        /// <summary>Its centre passed through the open hole since the last frame.</summary>
        Through,
        /// <summary>Let go behind the plane by a hand that reached in through the hole.</summary>
        ReleasedInside,
        /// <summary>Behind the door plane without having come through the hole: through a wall, the crown, or from below.</summary>
        Stray,
    }

    /// <summary>
    /// The firehole as a doorway in the backhead, in the car's frame: a
    /// rectangle across the door plane, and the box behind it. An item
    /// enters only by its centre passing through the rectangle, or by being
    /// let go behind the plane by a hand reached in through it. Being inside
    /// the box is never enough: the box is a generous outline of the
    /// firebox, not its walls, and something that got there another way
    /// came through a wall.
    /// </summary>
    public sealed class FireholeGate
    {
        private readonly float holeMinX, holeMaxX, holeMinY, holeMaxY, plane;
        private readonly float boxMinX, boxMaxX, boxMinY, boxMaxY, boxMaxZ;

        /// <param name="holeMinX">The hole's left edge.</param>
        /// <param name="holeMaxX">The hole's right edge.</param>
        /// <param name="holeMinY">The hole's bottom edge.</param>
        /// <param name="holeMaxY">The hole's top edge.</param>
        /// <param name="plane">The door plane; the box lies at greater z.</param>
        /// <param name="boxMinX">The box behind the plane: its left side.</param>
        /// <param name="boxMaxX">Its right side.</param>
        /// <param name="boxMinY">Its floor (the grate).</param>
        /// <param name="boxMaxY">Its crown.</param>
        /// <param name="boxMaxZ">Its front (the tube plate).</param>
        public FireholeGate(float holeMinX, float holeMaxX, float holeMinY, float holeMaxY, float plane,
            float boxMinX, float boxMaxX, float boxMinY, float boxMaxY, float boxMaxZ)
        {
            if (!(holeMaxX > holeMinX) || !(holeMaxY > holeMinY) || !(boxMaxZ > plane))
                throw new ArgumentException("The firehole and the box behind it need some size.");
            this.holeMinX = holeMinX; this.holeMaxX = holeMaxX; this.holeMinY = holeMinY; this.holeMaxY = holeMaxY;
            this.plane = plane;
            // The box always contains the hole's own outline.
            this.boxMinX = Math.Min(boxMinX, holeMinX); this.boxMaxX = Math.Max(boxMaxX, holeMaxX);
            this.boxMinY = Math.Min(boxMinY, holeMinY); this.boxMaxY = Math.Max(boxMaxY, holeMaxY);
            this.boxMaxZ = boxMaxZ;
        }

        // The surveyed doorway: which cells of the hole's rectangle are open
        // (null: all of it), columns across by rows up.
        private bool[] openCells;
        private int columns = 1, rows = 1;
        private float opening = 1f;

        /// <summary>
        /// The open cells of the hole's rectangle, as surveyed in the cab:
        /// <paramref name="open"/>[row * columns + column], row 0 at the bottom.
        /// </summary>
        public void SetAperture(bool[] open, int columns, int rows)
        {
            if (open == null || columns < 1 || rows < 1 || open.Length != columns * rows)
                throw new ArgumentException("The aperture needs one flag for each cell.");
            openCells = (bool[])open.Clone();
            this.columns = columns;
            this.rows = rows;
        }

        /// <summary>
        /// How far the door stands open, 0 to 1. A part-open door leaves that
        /// share of the doorway's width open, about its middle. The S282's
        /// door leaves are solid and stop a throw themselves (r21); this
        /// keeps a cab whose doors are not from passing a shut door's throws.
        /// </summary>
        public float Opening
        {
            get => opening;
            set => opening = float.IsNaN(value) ? 0f : Math.Max(0f, Math.Min(1f, value));
        }

        /// <summary>
        /// A firehole fitted on its cab (the keypad's hole: its middle across,
        /// its foot, width, and height, in the door plane at
        /// <paramref name="plane"/>, the box at greater z), its frame
        /// <paramref name="depth"/> deep: the backhead's face may stand that
        /// far behind the fitted plane (the S282's 4.6 cm), so a piece that
        /// strikes it around the door is not taken for one gone through a wall.
        /// </summary>
        public static FireholeGate Fitted(float middle, float foot, float width, float height, float plane, float depth,
            float boxMinX, float boxMaxX, float boxMinY, float boxMaxY, float boxMaxZ) =>
            new FireholeGate(middle - width / 2f, middle + width / 2f, foot, foot + height, plane,
                boxMinX, boxMaxX, boxMinY, boxMaxY, Math.Max(boxMaxZ, plane + depth + 0.01f)) { StrayDepth = depth };

        /// <summary>
        /// How far behind the door plane a piece must be, not having come
        /// through the hole, to count as gone through a wall, m: 0 for a
        /// doorway surveyed at the backhead's own face.
        /// </summary>
        public float StrayDepth
        {
            get => strayDepth;
            set => strayDepth = float.IsNaN(value) ? 0f : Math.Max(0f, value);
        }
        private float strayDepth;

        // Behind the plane by more than the frame's depth, within the box.
        private bool Deep(CarPoint p) => Inside(p) && p.Z > plane + strayDepth;

        /// <summary>Whether a point on the door plane is in the open doorway.</summary>
        public bool OpenAt(float x, float y)
        {
            if (opening <= 0f || x < holeMinX || x > holeMaxX || y < holeMinY || y > holeMaxY) return false;
            var middle = 0.5f * (holeMinX + holeMaxX);
            if (Math.Abs(x - middle) > 0.5f * opening * (holeMaxX - holeMinX)) return false;
            if (openCells == null) return true;
            var c = Math.Min(columns - 1, (int)((x - holeMinX) / (holeMaxX - holeMinX) * columns));
            var r = Math.Min(rows - 1, (int)((y - holeMinY) / (holeMaxY - holeMinY) * rows));
            return openCells[r * columns + c];
        }

        /// <summary>Whether the doorway was surveyed (<see cref="SetAperture"/>) rather than taken as the whole rectangle.</summary>
        public bool Surveyed => openCells != null;

        /// <summary>The share of the rectangle's cells found open, 1 when not surveyed.</summary>
        public float OpenShare => openCells == null ? 1f : (float)openCells.Count(o => o) / openCells.Length;

        public float Plane => plane;
        public float HoleMinX => holeMinX;
        public float HoleMaxX => holeMaxX;
        public float HoleMinY => holeMinY;
        public float HoleMaxY => holeMaxY;
        public float BoxMinX => boxMinX;
        public float BoxMaxX => boxMaxX;
        public float BoxMinY => boxMinY;
        public float BoxMaxY => boxMaxY;
        public float BoxMaxZ => boxMaxZ;

        /// <summary>Behind the door plane and within the box.</summary>
        public bool Inside(CarPoint p) =>
            p.Z > plane && p.Z <= boxMaxZ && p.X >= boxMinX && p.X <= boxMaxX && p.Y >= boxMinY && p.Y <= boxMaxY;

        /// <summary>The segment from <paramref name="a"/> (in front of the plane) to <paramref name="b"/> (behind it) passes through the hole.</summary>
        public bool CrossesHole(CarPoint a, CarPoint b)
        {
            if (!(a.Z < plane) || !(b.Z >= plane) || b.Z - a.Z < 1e-6f) return false;
            var t = (plane - a.Z) / (b.Z - a.Z);
            return OpenAt(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        }

        /// <summary>
        /// Classifies a loose item's move from <paramref name="from"/> to
        /// <paramref name="to"/>, each already in the car's frame of its own
        /// moment.
        /// </summary>
        public FireholeEntry Classify(CarPoint from, CarPoint to)
        {
            if (CrossesHole(from, to)) return FireholeEntry.Through;
            // Into the box, past the doorway's frame, without passing through the hole.
            if (Deep(to) && !Deep(from)) return FireholeEntry.Stray;
            return FireholeEntry.None;
        }

        /// <summary>How far in front of the door plane a struck piece's middle may be and still count, m: the makers' own item colliders stand 5 to 15 cm before the plane on the FEF-3, S464-730, 8F and QJ, and a split 38 cm long meets them with its end.</summary>
        public const float StrikeReach = 0.45f;

        /// <summary>A piece whose middle would pass this near the doorway's edge strikes the frame, not the doorway, m.</summary>
        public const float StrikeInset = 0.03f;

        /// <summary>Slower than this into the box, m/s, a piece is resting or rolling, not thrown in.</summary>
        public const float StrikeSpeed = 0.8f;

 public bool StrikesDoorway(CarPoint centre, CarPoint velocity, float gravityY = 0f)
        {
            if (!(velocity.Z >= StrikeSpeed)) return false;
            if (!(centre.Z >= plane - StrikeReach && centre.Z <= plane + strayDepth)) return false;
            // Where its middle crosses the door plane on that flight; past the plane, where it is.
            var t = Math.Max(0f, (plane - centre.Z) / velocity.Z);
            var x = centre.X + velocity.X * t;
            var y = centre.Y + velocity.Y * t + 0.5f * Math.Min(0f, gravityY) * t * t;
            if (x < holeMinX + StrikeInset || x > holeMaxX - StrikeInset
                || y < holeMinY + StrikeInset || y > holeMaxY - StrikeInset) return false;
            return OpenAt(x, y);
        }

        /// <summary>How far inside the doorway a throw aimed through it is aimed, along the view, m.</summary>
        public const float AimDepth = 0.15f;

        /// <summary>
        /// Where a throw aimed from <paramref name="eye"/> at
        /// <paramref name="target"/> (what the view's ray met first) is aimed,
        /// if the view passes through the open doorway: just inside it, along
        /// the view. Past an open doorway the ray meets the box's far wall,
        /// or none, or a cab's catch-all item collider under the grate (the
        /// 8F's, 1.1 m in and 1.4 m down), and a throw at that flies over
        /// the doorway. A view that meets something well before the door, or
        /// passes beside it, keeps its target.
        /// </summary>
        public bool AimsThrough(CarPoint eye, CarPoint target, out CarPoint aim)
        {
            aim = target;
            var d = target - eye;
            if (!(d.Z > 1e-4f) || eye.Z >= plane) return false;
            // The view's line meets the door plane, extended as far as needed:
            // a target a little before the plane is the makers' colliders across the doorway.
            if (target.Z < plane - StrikeReach) return false;
            var t = (plane - eye.Z) / d.Z;
            var x = eye.X + d.X * t;
            var y = eye.Y + d.Y * t;
            if (x < holeMinX + StrikeInset || x > holeMaxX - StrikeInset
                || y < holeMinY + StrikeInset || y > holeMaxY - StrikeInset || !OpenAt(x, y)) return false;
            var length = (float)Math.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z);
            var past = AimDepth / length;
            aim = new CarPoint(x + d.X * past, y + d.Y * past, plane + d.Z * past);
            return true;
        }

        /// <summary>
        /// Classifies a release: an item let go at <paramref name="at"/>
        /// behind the plane counts when the reach from <paramref name="eye"/>
        /// to it passes through the hole.
        /// </summary>
        public FireholeEntry ClassifyRelease(CarPoint eye, CarPoint at)
        {
            if (!Inside(at)) return FireholeEntry.None;
            return CrossesHole(eye, at) ? FireholeEntry.ReleasedInside : FireholeEntry.Stray;
        }

        /// <summary>
        /// The zone a piece will come down in: where its flight, relative to
        /// the engine (its own velocity less the car's at that point), meets
        /// the grate's top at <paramref name="grateTopY"/> under
        /// <paramref name="gravityY"/> (negative), over a grate spanning
        /// <paramref name="grateMinX"/>-<paramref name="grateMaxX"/> across and
        /// <paramref name="grateMinZ"/>-<paramref name="grateMaxZ"/> along.
        /// </summary>
        public static GrateZone LandingZone(CarPoint at, CarPoint itemVelocity, CarPoint carVelocity,
            float grateMinX, float grateMaxX, float grateMinZ, float grateMaxZ, float grateTopY, float gravityY)
        {
            var v = itemVelocity - carVelocity;
            var t = 0f;
            var above = at.Y - grateTopY;
            if (above > 0f && gravityY < 0f)
            {
                // 0.5 g t² + vy t + above = 0, the later (falling) root.
                var root = (float)Math.Sqrt(Math.Max(0f, v.Y * v.Y - 2f * gravityY * above));
                t = Math.Min(1f, (-v.Y - root) / gravityY);
            }
            var x = at.X + v.X * t;
            var z = at.Z + Math.Max(0f, v.Z) * t;
            return DoorAim.ZoneFor(Fraction(grateMinX, grateMaxX, x), Fraction(grateMinZ, grateMaxZ, z));
        }

        private static float Fraction(float min, float max, float value) =>
            max > min ? Math.Max(0f, Math.Min(1f, (value - min) / (max - min))) : 0.5f;
    }
}
