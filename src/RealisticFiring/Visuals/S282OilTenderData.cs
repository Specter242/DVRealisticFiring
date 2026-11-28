using System;
using System.IO;
using System.Linq;

namespace RealisticFiring.Visuals
{
    /// <summary>
    /// The S282 oil tender's added geometry (fx/models/S282OilTender.json, built
    /// by tools/s282-oil-tender/build_model.py): the oil tank in the coal space
    /// and the plate over the coal gate, by LOD, in the tender's frame, with
    /// boxes standing in for the coal's colliders. Original geometry; the
    /// materials are the stock tender's, named.
    /// </summary>
    internal sealed class S282OilTenderData
    {
        internal const string Livery = "LocoS282B";
        internal static readonly string[] Materials = { "LocoS282B_Body", "LocoS282A_Interior", "LocoS282A_Gauges" };

        /// <summary>The firepan's firebrick: the mod's own (the brick arch's), in the cab only.</summary>
        internal const string Firebrick = "RealisticFiring_Firebrick";

        public int Version { get; set; }
        public string Credit { get; set; }
        public TankShape Tank { get; set; }
        public Piece[] Meshes { get; set; }
        public Box[] Colliders { get; set; }

        /// <summary>The oil burner's cab equipment, in the engine's frame (LOD 0 near, 1 from across the cab).</summary>
        public Piece[] Cab { get; set; }

        /// <summary>The stock cab's coal-firing parts the oil burner does without, by path under the cab interior.</summary>
        public string[] CabHide { get; set; }

        /// <summary>The cab's own parts copied onto the oil burner's fittings (the atomizer's handwheel).</summary>
        public Clone[] CabClones { get; set; }

        /// <summary>Label plates, as the cab's own.</summary>
        public Label[] CabLabels { get; set; }

        /// <summary>The firing valve's lever, crank, and link, which the cab works (their pieces in <see cref="Cab"/>).</summary>
        public FiringValveShape FiringValve { get; set; }

        /// <summary>The peephole's cover on the blanked fire door, which swings open with the door's control.</summary>
        public PeepholeShape Peephole { get; set; }

        /// <summary>The cab's gauges' needles (the atomizer's steam).</summary>
        public Gauge[] CabGauges { get; set; }

        /// <summary>The tender's gauges' needles (the oil level).</summary>
        public Gauge[] TenderGauges { get; set; }

        /// <summary>The fuel fillers, one each side of the tender: where the stock diesels' fuel tank cap goes.</summary>
        public Filler[] Fillers { get; set; }

        /// <summary>The stock firebox's parts the oil burner does without.</summary>
        public FireboxShape Firebox { get; set; }

        /// <summary>The flue sand's box in the cab: its middle and size (across, up, along), and how far its sand lies under its rim.</summary>
        public SandBoxShape SandBox { get; set; }

        internal sealed class SandBoxShape
        {
            public float[] Middle { get; set; }
            public float[] Size { get; set; }
            public float BelowRim { get; set; }
        }

        internal sealed class Gauge
        {
            /// <summary>The needle's piece, modeled about its pivot (x right, y up the dial, z toward the reader).</summary>
            public string Needle { get; set; }
            public float[] Pivot { get; set; }
            public float[] Normal { get; set; }
            public float[] Up { get; set; }
            /// <summary>Its sweep, degrees clockwise from up as read: at the scale's first figure, and its last.</summary>
            public float From { get; set; }
            public float To { get; set; }
            /// <summary>The scale's first and last figures.</summary>
            public float[] Reads { get; set; }
        }

        internal sealed class Filler
        {
            public float[] At { get; set; }
            /// <summary>-1 the left side, 1 the right.</summary>
            public int Side { get; set; }
        }

        internal sealed class FireboxShape
        {
            public string[] Hide { get; set; }

            /// <summary>The firepan's open floor, in the cab's frame, for the oil flame laid over it.</summary>
            public PanShape Pan { get; set; }
        }

        /// <summary>
        /// The firepan's open floor between the flash wall (<see cref="Back"/>)
        /// and the front wall (<see cref="Front"/>), along the cab's z; the
        /// floor's height at each; the half width inside the linings; the
        /// flash wall's top. Metres, in the cab's frame.
        /// </summary>
        internal sealed class PanShape
        {
            public float Back { get; set; }
            public float Front { get; set; }
            public float BackY { get; set; }
            public float FrontY { get; set; }
            public float HalfWidth { get; set; }
            public float FlashTop { get; set; }
        }

        /// <summary>The moving pieces' names in <see cref="Cab"/>, each modeled about its own pivot.</summary>
        internal const string FiringLever = "firing lever";
        internal const string FiringCrank = "firing crank";
        internal const string FiringLink = "firing link";
        internal const string PeepholeCover = "peephole cover";
        internal const string AtomizerNeedle = "atomizer needle";
        internal const string OilLevelNeedle = "oil level needle";
        internal static readonly string[] MovingPieces = { FiringLever, FiringCrank, FiringLink, PeepholeCover, AtomizerNeedle };

        internal sealed class FiringValveShape
        {
            /// <summary>The lever's pivot, in the cab's frame; it swings about the cab's x.</summary>
            public float[] Pivot { get; set; }
            public float Length { get; set; }
            /// <summary>Degrees from upright, toward the backhead positive: shut, and wide open.</summary>
            public float Shut { get; set; }
            public float Open { get; set; }
            public float Shown { get; set; }
            /// <summary>The tail pin's drop below the pivot, m.</summary>
            public float Tail { get; set; }
            /// <summary>The link rod's line across the cab, x.</summary>
            public float LinkX { get; set; }
            /// <summary>The regulating valve's crank: its spindle's middle, and its pin's radius.</summary>
            public float[] Crank { get; set; }
            public float CrankRadius { get; set; }
            /// <summary>The grip, from the pivot along the lever upright.</summary>
            public float[] Grip { get; set; }
        }

        internal sealed class PeepholeShape
        {
            /// <summary>The cover's hinge (it swings about the cab's y), the hole's middle (on the boss's face), and its radius.</summary>
            public float[] Hinge { get; set; }
            public float[] Middle { get; set; }
            public float Radius { get; set; }
            public float CoverRadius { get; set; }
        }

        internal sealed class Clone
        {
            public string Name { get; set; }
            public string Source { get; set; }
            public float[] SourceAxis { get; set; }
            public float[] At { get; set; }
            public float[] Axis { get; set; }
            public float Scale { get; set; }
            /// <summary>The working valve to copy (its holder, and its control under it) and the port it works.</summary>
            public string Holder { get; set; }
            public string Control { get; set; }
            public string Port { get; set; }
        }

        internal sealed class Label
        {
            public string Text { get; set; }
            public float[] At { get; set; }
            public float[] Facing { get; set; }
        }

        internal sealed class TankShape
        {
            public float Front { get; set; }
            public float Rear { get; set; }
            public float SlopeStart { get; set; }
            public float HalfWidth { get; set; }
            public float Bottom { get; set; }
            public float Top { get; set; }
            public float EdgeTop { get; set; }
            public float ManholeZ { get; set; }
        }

        internal sealed class Piece
        {
            public string Name { get; set; }
            public string Material { get; set; }
            public int Lod { get; set; }
            public float[][] Vertices { get; set; }
            public float[][] Normals { get; set; }
            public float[][] Uv { get; set; }
            public int[] Triangles { get; set; }
            /// <summary>Vertex colours (rgba), or null: the firebrick's tone and warmth.</summary>
            public float[][] Colors { get; set; }
        }

        internal sealed class Box
        {
            public string Name { get; set; }
            public float[] Center { get; set; }
            public float[] Size { get; set; }
            public float RotationX { get; set; }
        }

        internal void Validate()
        {
            if (Version != 1 || Credit == null || !Credit.StartsWith("S282 oil tender", StringComparison.Ordinal))
                throw new InvalidDataException("Not the supported S282 oil tender model");
            if (Tank == null || !(Tank.Front > Tank.SlopeStart && Tank.SlopeStart > Tank.Rear && Tank.HalfWidth > 0.5f && Tank.HalfWidth < 1.6f
                                  && Tank.Top > Tank.Bottom && Tank.Top < 4.5f))
                throw new InvalidDataException("Invalid oil tank shape");
            if (Meshes == null || Meshes.Length == 0 || Meshes.Length > 16)
                throw new InvalidDataException("Expected the oil tender's pieces");
            var vertices = 0;
            foreach (var p in Meshes)
            {
                if (p == null || string.IsNullOrEmpty(p.Name) || !Materials.Contains(p.Material) || p.Lod < 0 || p.Lod > 3)
                    throw new InvalidDataException("Unknown oil tender piece, material, or LOD");
                var n = p.Vertices?.Length ?? 0;
                if (n == 0 || n > 20000 || p.Normals?.Length != n || p.Uv?.Length != n
                    || p.Triangles == null || p.Triangles.Length == 0 || p.Triangles.Length % 3 != 0
                    || p.Triangles.Length > 60000 || p.Triangles.Any(i => i < 0 || i >= n))
                    throw new InvalidDataException("Invalid oil tender mesh arrays");
                for (var i = 0; i < n; i++)
                {
                    Vector(p.Vertices[i], 3); Vector(p.Normals[i], 3); Vector(p.Uv[i], 2);
                }
                Colors(p, n);
                vertices += n;
            }
            if (vertices > 40000 || !Meshes.Any(p => p.Lod == 0))
                throw new InvalidDataException("The oil tender is incomplete or unexpectedly large");
            foreach (var p in Cab ?? new Piece[0])
            {
                if (p == null || string.IsNullOrEmpty(p.Name) || !(Materials.Contains(p.Material) || p.Material == Firebrick) || p.Lod < 0 || p.Lod > 1)
                    throw new InvalidDataException("Unknown oil burner cab piece, material, or LOD");
                var n = p.Vertices?.Length ?? 0;
                if (n == 0 || n > 20000 || p.Normals?.Length != n || p.Uv?.Length != n
                    || p.Triangles == null || p.Triangles.Length == 0 || p.Triangles.Length % 3 != 0
                    || p.Triangles.Length > 60000 || p.Triangles.Any(i => i < 0 || i >= n))
                    throw new InvalidDataException("Invalid oil burner cab mesh arrays");
                for (var i = 0; i < n; i++)
                {
                    Vector(p.Vertices[i], 3); Vector(p.Normals[i], 3); Vector(p.Uv[i], 2);
                }
                Colors(p, n);
            }
            foreach (var g in (CabGauges ?? new Gauge[0]).Concat(TenderGauges ?? new Gauge[0]))
            {
                if (g == null || string.IsNullOrEmpty(g.Needle) || !(g.To > g.From) || g.To - g.From > 360f)
                    throw new InvalidDataException("Invalid gauge");
                Vector(g.Pivot, 3); Vector(g.Normal, 3); Vector(g.Up, 3); Vector(g.Reads, 2);
            }
            foreach (var f in Fillers ?? new Filler[0])
            {
                if (f == null || (f.Side != 1 && f.Side != -1))
                    throw new InvalidDataException("Invalid filler");
                Vector(f.At, 3);
            }
            if (SandBox != null)
            {
                Vector(SandBox.Middle, 3); Vector(SandBox.Size, 3);
                if (SandBox.Size.Any(v => !(v > 0.05f && v < 1f)) || !(SandBox.BelowRim >= 0f && SandBox.BelowRim < SandBox.Size[1]))
                    throw new InvalidDataException("Invalid sand box");
            }
            if ((Firebox?.Hide ?? new string[0]).Any(h => string.IsNullOrEmpty(h) || h.Contains("..")))
                throw new InvalidDataException("Invalid firebox part to hide");
            if ((CabHide ?? new string[0]).Any(h => string.IsNullOrEmpty(h) || h.Contains("..")))
                throw new InvalidDataException("Invalid cab part to hide");
            foreach (var c in CabClones ?? new Clone[0])
            {
                if (c == null || string.IsNullOrEmpty(c.Source) || !(c.Scale > 0.2f && c.Scale < 5f))
                    throw new InvalidDataException("Invalid cab clone");
                Vector(c.SourceAxis, 3); Vector(c.At, 3); Vector(c.Axis, 3);
            }
            foreach (var l in CabLabels ?? new Label[0])
            {
                if (l == null || string.IsNullOrEmpty(l.Text) || l.Text.Length > 24)
                    throw new InvalidDataException("Invalid cab label");
                Vector(l.At, 3); Vector(l.Facing, 3);
            }
            if (FiringValve != null)
            {
                Vector(FiringValve.Pivot, 3); Vector(FiringValve.Crank, 3); Vector(FiringValve.Grip, 3);
                if (!(FiringValve.Shut > FiringValve.Open) || FiringValve.Shut - FiringValve.Open > 170f || !(FiringValve.Length > 0f) || !(FiringValve.CrankRadius > 0f))
                    throw new InvalidDataException("Invalid firing valve");
            }
            if (Peephole != null)
            {
                Vector(Peephole.Hinge, 3); Vector(Peephole.Middle, 3);
                if (!(Peephole.Radius > 0f && Peephole.Radius < 0.3f))
                    throw new InvalidDataException("Invalid peephole");
            }
            if (Colliders == null || Colliders.Length == 0 || Colliders.Length > 8)
                throw new InvalidDataException("Expected the oil tank's colliders");
            foreach (var b in Colliders)
            {
                if (b == null) throw new InvalidDataException("Invalid oil tank collider");
                Vector(b.Center, 3); Vector(b.Size, 3);
                if (b.Size.Any(s => s <= 0f || s > 10f)) throw new InvalidDataException("Invalid oil tank collider size");
            }
        }

        private static void Colors(Piece p, int n)
        {
            if (p.Colors == null)
                return;
            if (p.Colors.Length != n)
                throw new InvalidDataException("Invalid oil tender mesh colours");
            foreach (var c in p.Colors)
                Vector(c, 4);
        }

        private static void Vector(float[] value, int count)
        {
            if (value == null || value.Length != count || value.Any(x => float.IsNaN(x) || float.IsInfinity(x)))
                throw new InvalidDataException("Invalid model coordinate");
        }
    }
}
