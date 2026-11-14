using System;
using System.Collections.Generic;

namespace RealisticFiring.Core
{
 public static class CoalRock
    {
        public enum Kind
        {
            /// <summary>A block, its corners and an edge chipped.</summary>
            Block,

            /// <summary>A block chipped and broken off square once or twice.</summary>
            Broken,

            /// <summary>A slab split along the bedding, chipped and broken once.</summary>
            Slab,
        }

        public sealed class Shape
        {
            /// <summary>The points, x, y, z each.</summary>
            public float[] Points { get; }

            public int[] Triangles { get; }

            public int PointCount => Points.Length / 3;

            internal Shape(float[] points, int[] triangles)
            {
                Points = points;
                Triangles = triangles;
            }
        }

        public static Shape Make(Random random, Kind kind)
        {
            if (random == null)
                throw new ArgumentNullException(nameof(random));
            switch (kind)
            {
                // Chunkier and more chipped than first cut (2026-11-12): thin
                // and little chipped, a pile of them read as broken tiles.
                case Kind.Slab:
                    return BrokenBlock(random, Lying(random, 0.55, 0.8, 0.28, 0.4, 0.15), 2, 3, 0, 1, 1, 1);
                case Kind.Broken:
                    return BrokenBlock(random, Lying(random, 0.65, 0.9, 0.55, 0.85, 0.35), 3, 4, 1, 2, 1, 2);
                default:
                    return BrokenBlock(random, Lying(random, 0.65, 0.9, 0.55, 0.85, 0.35), 3, 5, 1, 2, 0, 0);
            }
        }

        // Its long, middle, and short ways across, up, and along: mostly lying
        // flat (its short way up), some on edge.
        private static V Lying(Random random, double midLow, double midHigh, double shortLow, double shortHigh, double onEdge)
        {
            var mid = Uniform(random, midLow, midHigh);
            var flat = Uniform(random, shortLow, shortHigh);
            if (random.NextDouble() < onEdge)
                return random.NextDouble() < 0.5 ? new V(1, mid, flat) : new V(flat, mid, 1);
            return random.NextDouble() < 0.5 ? new V(1, flat, mid) : new V(mid, flat, 1);
        }

        // Its faces each turned off square by up to 8 degrees, a few corners
        // and edges chipped, and broken off square across some of it.
        private static Shape BrokenBlock(Random random, V extents, int cornersLow, int cornersHigh, int edgesLow, int edgesHigh, int breaksLow, int breaksHigh)
        {
            var faces = Cube(10);
            foreach (var axis in Axes)
            {
                var n = Jitter(axis, 8, random);
                faces = Clip(faces, n, Math.Abs(axis.Dot(extents)) * Uniform(random, 0.95, 1.05));
            }
            for (int i = 0, count = random.Next(cornersLow, cornersHigh + 1); i < count; i++)
            {
                var n = Jitter(new V(Sign(random), Sign(random), Sign(random)).Unit, 15, random);
                faces = Clip(faces, n, Support(faces, n) * Uniform(random, 0.72, 0.9));
            }
            for (int i = 0, count = random.Next(edgesLow, edgesHigh + 1); i < count; i++)
            {
                var a = random.Next(3);
                var b = (a + 1 + random.Next(2)) % 3;
                var s = new double[3];
                s[a] = Sign(random);
                s[b] = Sign(random);
                var n = Jitter(new V(s[0], s[1], s[2]).Unit, 12, random);
                faces = Clip(faces, n, Support(faces, n) * Uniform(random, 0.78, 0.92));
            }
            for (int i = 0, count = random.Next(breaksLow, breaksHigh + 1); i < count; i++)
            {
                var n = Jitter(new V(0, 1, 0), 180, random);
                faces = Clip(faces, n, Support(faces, n) * Uniform(random, 0.55, 0.75));
            }
            return Finish(faces);
        }

        private static readonly V[] Axes =
        {
            new V(1, 0, 0), new V(-1, 0, 0), new V(0, 1, 0), new V(0, -1, 0), new V(0, 0, 1), new V(0, 0, -1),
        };

        private const double Close = 1e-9;

        /// <summary>
        /// Keeps the part of a convex polyhedron (faces wound outward) where
        /// n.x is at most d, closing the cut with a face of its own.
        /// </summary>
        private static List<List<V>> Clip(List<List<V>> faces, V n, double d)
        {
            var kept = new List<List<V>>();
            var cap = new List<V>();
            foreach (var face in faces)
            {
                var output = new List<V>();
                for (var i = 0; i < face.Count; i++)
                {
                    var a = face[i];
                    var b = face[(i + 1) % face.Count];
                    var da = n.Dot(a) - d;
                    var db = n.Dot(b) - d;
                    if (da <= Close)
                    {
                        output.Add(a);
                        if (da >= -Close)
                            cap.Add(a);
                    }
                    if ((da < -Close && db > Close) || (da > Close && db < -Close))
                    {
                        var p = a + (b - a) * (da / (da - db));
                        output.Add(p);
                        cap.Add(p);
                    }
                }
                if (output.Count >= 3)
                    kept.Add(output);
            }
            var unique = new List<V>();
            foreach (var p in cap)
            {
                var seen = false;
                foreach (var q in unique)
                    seen |= (p - q).Length <= 1e-7;
                if (!seen)
                    unique.Add(p);
            }
            if (unique.Count >= 3)
            {
                var middle = new V(0, 0, 0);
                foreach (var p in unique)
                    middle += p;
                middle *= 1.0 / unique.Count;
                var u = (Math.Abs(n.X) < 0.9 ? n.Cross(new V(1, 0, 0)) : n.Cross(new V(0, 1, 0))).Unit;
                var v = n.Cross(u);
                // Counterclockwise about n, its outward normal.
                unique.Sort((p, q) => Math.Atan2((p - middle).Dot(v), (p - middle).Dot(u))
                    .CompareTo(Math.Atan2((q - middle).Dot(v), (q - middle).Dot(u))));
                kept.Add(unique);
            }
            return kept;
        }

        private static double Support(List<List<V>> faces, V n)
        {
            var most = double.MinValue;
            foreach (var face in faces)
                foreach (var p in face)
                    most = Math.Max(most, n.Dot(p));
            return most;
        }

        private static List<List<V>> Cube(double h)
        {
            V C(int x, int y, int z) => new V(x * h, y * h, z * h);
            // Each face counterclockwise seen from outside.
            return new List<List<V>>
            {
                new List<V> { C(1, -1, -1), C(1, 1, -1), C(1, 1, 1), C(1, -1, 1) },
                new List<V> { C(-1, -1, -1), C(-1, -1, 1), C(-1, 1, 1), C(-1, 1, -1) },
                new List<V> { C(-1, 1, -1), C(-1, 1, 1), C(1, 1, 1), C(1, 1, -1) },
                new List<V> { C(-1, -1, -1), C(1, -1, -1), C(1, -1, 1), C(-1, -1, 1) },
                new List<V> { C(-1, -1, 1), C(1, -1, 1), C(1, 1, 1), C(-1, 1, 1) },
                new List<V> { C(-1, -1, -1), C(-1, 1, -1), C(1, 1, -1), C(1, -1, -1) },
            };
        }

        // Its points shared between its faces, each face a fan of triangles,
        // about its middle with its largest half extent 1.
        private static Shape Finish(List<List<V>> faces)
        {
            var points = new List<V>();
            var triangles = new List<int>();
            int Index(V p)
            {
                for (var i = 0; i < points.Count; i++)
                    if ((p - points[i]).Length <= 1e-6)
                        return i;
                points.Add(p);
                return points.Count - 1;
            }
            foreach (var face in faces)
            {
                var ids = new List<int>();
                foreach (var p in face)
                {
                    var id = Index(p);
                    if (ids.Count == 0 || ids[ids.Count - 1] != id)
                        ids.Add(id);
                }
                if (ids.Count > 1 && ids[0] == ids[ids.Count - 1])
                    ids.RemoveAt(ids.Count - 1);
                for (var k = 1; k + 1 < ids.Count; k++)
                {
                    triangles.Add(ids[0]);
                    triangles.Add(ids[k]);
                    triangles.Add(ids[k + 1]);
                }
            }
            var low = new V(double.MaxValue, double.MaxValue, double.MaxValue);
            var high = new V(double.MinValue, double.MinValue, double.MinValue);
            foreach (var p in points)
            {
                low = new V(Math.Min(low.X, p.X), Math.Min(low.Y, p.Y), Math.Min(low.Z, p.Z));
                high = new V(Math.Max(high.X, p.X), Math.Max(high.Y, p.Y), Math.Max(high.Z, p.Z));
            }
            var middle = (low + high) * 0.5;
            var half = 0.5 * Math.Max(high.X - low.X, Math.Max(high.Y - low.Y, high.Z - low.Z));
            var output = new float[points.Count * 3];
            for (var i = 0; i < points.Count; i++)
            {
                var p = (points[i] - middle) * (1.0 / half);
                output[3 * i] = (float)p.X;
                output[3 * i + 1] = (float)p.Y;
                output[3 * i + 2] = (float)p.Z;
            }
            return new Shape(output, triangles.ToArray());
        }

        // n turned by up to this many degrees in a random direction.
        private static V Jitter(V n, double degrees, Random random)
        {
            V r;
            do
                r = new V(Uniform(random, -1, 1), Uniform(random, -1, 1), Uniform(random, -1, 1));
            while (r.Dot(r) <= 0.01 || r.Dot(r) > 1.0);
            var side = n.Cross(r);
            if (side.Length < 1e-6)
                return n;
            side = side.Unit;
            var angle = Uniform(random, 0, degrees) * Math.PI / 180.0;
            return (n * Math.Cos(angle) + side * Math.Sin(angle)).Unit;
        }

        private static double Uniform(Random random, double low, double high) => low + (high - low) * random.NextDouble();

        private static double Sign(Random random) => random.Next(2) == 0 ? -1 : 1;

        private readonly struct V
        {
            public readonly double X;
            public readonly double Y;
            public readonly double Z;

            public V(double x, double y, double z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public double Dot(V o) => X * o.X + Y * o.Y + Z * o.Z;
            public V Cross(V o) => new V(Y * o.Z - Z * o.Y, Z * o.X - X * o.Z, X * o.Y - Y * o.X);
            public double Length => Math.Sqrt(Dot(this));
            public V Unit => this * (1.0 / Length);
            public static V operator +(V a, V b) => new V(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
            public static V operator -(V a, V b) => new V(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
            public static V operator *(V a, double s) => new V(a.X * s, a.Y * s, a.Z * s);
        }
    }
}
