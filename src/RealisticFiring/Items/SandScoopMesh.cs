using System.Collections.Generic;
using UnityEngine;

namespace RealisticFiring.Items
{
 internal static class SandScoopMesh
    {
        public const float HandleStart = -0.068f;
        public const float HandleEnd = 0.052f;
        public const float BodyStart = 0.075f;
        public const float Tip = 0.26f;
        public const float HandleRadius = 0.0135f;
        public const float BackRadius = 0.046f;
        public const float LipRadius = 0.036f;

        // The body's opening: from none at the back to this much of its round cut away at the lip, either side of the top.
        private const float OpenAtLip = 0.55f * Mathf.PI;

        private const float Sheet = 0.0015f;
        private const float WireRadius = 0.0026f;
        private const int Sides = 14;
        private const int Around = 32;
        private const int Along = 14;

        // The shovel's atlas regions (u0, v0, u1, v1) and their texels to the metre.
        private static readonly Rect BladeBack = Rect.MinMaxRect(0.45f, 0.47f, 0.82f, 0.83f);
        private static readonly Rect BladeFace = Rect.MinMaxRect(0.05f, 0.08f, 0.40f, 0.40f);
        private static readonly Rect ShaftWood = Rect.MinMaxRect(0.49f, 0.205f, 0.96f, 0.335f);
        private static readonly Rect Socket = Rect.MinMaxRect(0.17f, 0.53f, 0.29f, 0.72f);
        private const float SteelPerMetre = 1.25f;

        /// <summary>The body's radius at <paramref name="z"/>, before its dents.</summary>
        public static float RadiusAt(float z) => Mathf.Lerp(BackRadius, LipRadius, Mathf.InverseLerp(BodyStart, Tip, z));

        // How far the body's round is cut away from its top at z, radians either side.
        private static float OpenAt(float z) => OpenAtLip * Mathf.Pow(Mathf.InverseLerp(BodyStart, Tip, z), 0.7f);

        // Use: a few shallow dents and a slight out-of-round, the same each time.
        private static float Dent(float z, float phi)
        {
            var t = Mathf.InverseLerp(BodyStart, Tip, z);
            var wobble = 0.010f * Mathf.Sin(3f * phi + 1.3f) * Mathf.Sin(Mathf.PI * t) + 0.006f * Mathf.Sin(5f * phi + 4f * t + 0.4f);
            var dent = -0.035f * Mathf.Exp(-Mathf.Pow((phi - 2.25f) / 0.32f, 2f) - Mathf.Pow((t - 0.62f) / 0.16f, 2f));
            var knock = -0.025f * Mathf.Exp(-Mathf.Pow((phi - 4.1f) / 0.25f, 2f) - Mathf.Pow((t - 0.9f) / 0.08f, 2f));
            return 1f + wobble + dent + knock;
        }

        /// <summary>The scoop's extent in its frame, for the game's placing preview and icon.</summary>
        public static Bounds Bounds => new Bounds(new Vector3(0f, 0f, (HandleStart + Tip) / 2f),
            new Vector3(2f * BackRadius + 2f * WireRadius, 2f * BackRadius + 2f * WireRadius, Tip - HandleStart + WireRadius));

        private sealed class Builder
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<int> Triangles = new List<int>();

            public int Vert(Vector3 p, Vector3 n, Vector2 uv)
            {
                Vertices.Add(p);
                Normals.Add(n.normalized);
                Uvs.Add(uv);
                return Vertices.Count - 1;
            }

            public void Quad(int a, int b, int c, int d)
            {
                Triangles.Add(a); Triangles.Add(b); Triangles.Add(c);
                Triangles.Add(a); Triangles.Add(c); Triangles.Add(d);
            }

            // Rows of a grid, each row's vertices in order, stitched so their faces face the way the normals do
            // when rows advance along +along and columns along +across with (along × across) pointing out.
            public void Stitch(List<int[]> rows, bool flip)
            {
                for (var i = 0; i < rows.Count - 1; i++)
                for (var a = 0; a < rows[i].Length - 1; a++)
                {
                    int i0 = rows[i][a], i1 = rows[i][a + 1], j0 = rows[i + 1][a], j1 = rows[i + 1][a + 1];
                    if (flip) Quad(i0, i1, j1, j0);
                    else Quad(i0, j0, j1, i1);
                }
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(Vertices);
                mesh.SetNormals(Normals);
                mesh.SetUVs(0, Uvs);
                mesh.SetTriangles(Triangles, 0);
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        private static Vector2 In(Rect r, float u, float v) => new Vector2(Mathf.Lerp(r.xMin, r.xMax, u), Mathf.Lerp(r.yMin, r.yMax, v));

        public static Mesh Create()
        {
            var b = new Builder();
            Handle(b);
            Body(b);
            Back(b);
            return b.Build("sand scoop");
        }

        // A turned profile about z: (z, r) stations, its UVs in a region (along u, round v), its ends capped where asked.
        private static void Turned(Builder b, (float Z, float R)[] profile, Rect region, float alongPerMetre, float roundPerMetre)
        {
            var rows = new List<int[]>();
            var z0 = profile[0].Z;
            for (var i = 0; i < profile.Length; i++)
            {
                var (z, r) = profile[i];
                // The normal leans by the profile's slope.
                var prev = profile[Mathf.Max(0, i - 1)];
                var next = profile[Mathf.Min(profile.Length - 1, i + 1)];
                var slope = next.Z - prev.Z > 1e-6f ? -(next.R - prev.R) / (next.Z - prev.Z) : 0f;
                var row = new int[Sides + 1];
                for (var s = 0; s <= Sides; s++)
                {
                    var angle = 2f * Mathf.PI * s / Sides;
                    var d = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                    var u = Mathf.Clamp01((z - z0) * alongPerMetre / Mathf.Max(1e-4f, region.width) + 0.05f);
                    var v = Mathf.Clamp01((float)s / Sides * 2f * Mathf.PI * r * roundPerMetre / Mathf.Max(1e-4f, region.height));
                    row[s] = b.Vert(new Vector3(0f, 0f, z) + d * r, d + Vector3.forward * slope, In(region, u, v));
                }
                rows.Add(row);
            }
            // Rows along +z, columns counterclockwise seen from behind: (z × round) points out.
            b.Stitch(rows, true);
        }

        private static void Disk(Builder b, Vector3 center, float r, float facing, Rect region, int sides)
        {
            var n = new Vector3(0f, 0f, facing);
            var c = b.Vert(center, n, In(region, 0.5f, 0.5f));
            var ring = b.Vertices.Count;
            for (var s = 0; s < sides; s++)
            {
                var angle = 2f * Mathf.PI * s / sides;
                var d = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                b.Vert(center + new Vector3(d.x, d.y, 0f) * r, n, In(region, 0.5f + 0.5f * d.x * r / 0.05f, 0.5f + 0.5f * d.y * r / 0.05f));
            }
            for (var s = 0; s < sides; s++)
            {
                var a1 = ring + s;
                var a2 = ring + (s + 1) % sides;
                if (facing > 0f) { b.Triangles.Add(c); b.Triangles.Add(a1); b.Triangles.Add(a2); }
                else { b.Triangles.Add(c); b.Triangles.Add(a2); b.Triangles.Add(a1); }
            }
        }

        private static void Handle(Builder b)
        {
            // From the butt: a rounded knob, a groove, the swell for the hand, a groove, into the ferrule.
            var wood = new (float, float)[]
            {
                (HandleStart, 0.0045f), (HandleStart + 0.0015f, 0.0090f), (HandleStart + 0.004f, 0.0118f), (HandleStart + 0.008f, 0.0128f),
                (HandleStart + 0.012f, 0.0122f), (HandleStart + 0.015f, 0.0106f), (HandleStart + 0.018f, 0.0122f), (HandleStart + 0.024f, 0.0136f),
                (HandleStart + 0.045f, 0.0146f), (HandleStart + 0.070f, 0.0144f), (HandleStart + 0.090f, 0.0133f), (HandleStart + 0.097f, 0.0120f),
                (HandleStart + 0.100f, 0.0108f), (HandleStart + 0.103f, 0.0118f), (HandleEnd, 0.0118f),
            };
            Turned(b, wood, ShaftWood, 0.85f, 1.4f);
            Disk(b, new Vector3(0f, 0f, HandleStart), 0.0045f, -1f, ShaftWood, Sides);
            // The ferrule over the handle's end, a bead at its mouth; the socket flaring into the back plate.
            var steel = new (float, float)[]
            {
                (HandleEnd - 0.012f, 0.0126f), (HandleEnd - 0.011f, 0.0134f), (HandleEnd - 0.009f, 0.0134f), (HandleEnd - 0.008f, 0.0127f),
                (HandleEnd + 0.004f, 0.0124f), (HandleEnd + 0.010f, 0.0112f), (BodyStart - 0.006f, 0.0118f), (BodyStart - 0.002f, 0.0150f),
                (BodyStart - 0.0005f, 0.0185f),
            };
            Turned(b, steel, Socket, 1.3f, 1.3f);
        }

        private static void Body(Builder b)
        {
            var outer = new List<int[]>();
            var inner = new List<int[]>();
            var mid = new List<Vector3[]>();
            var normals = new List<Vector3[]>();
            for (var i = 0; i <= Along; i++)
            {
                var z = Mathf.Lerp(BodyStart, Tip, (float)i / Along);
                var open = OpenAt(z);
                var o = new int[Around + 1];
                var n = new int[Around + 1];
                var m = new Vector3[Around + 1];
                var ns = new Vector3[Around + 1];
                for (var a = 0; a <= Around; a++)
                {
                    // From one cut edge, down round the bottom, to the other (angle from the top, clockwise seen from behind).
                    var phi = Mathf.Lerp(open, 2f * Mathf.PI - open, (float)a / Around);
                    var d = new Vector3(Mathf.Sin(phi), Mathf.Cos(phi), 0f);
                    var r = RadiusAt(z) * Dent(z, phi);
                    var center = new Vector3(0f, 0f, z);
                    // On the atlas: across by the distance round from the bottom, along by z.
                    var across = (phi - Mathf.PI) * RadiusAt(z) * SteelPerMetre;
                    var alongV = (z - BodyStart) * SteelPerMetre;
                    o[a] = b.Vert(center + d * r, d, In(BladeBack, 0.5f + across / BladeBack.width, alongV / BladeBack.height));
                    n[a] = b.Vert(center + d * (r - Sheet), -d, In(BladeFace, 0.5f + across / BladeFace.width, 0.1f + alongV / BladeFace.height));
                    m[a] = center + d * (r - Sheet / 2f);
                    ns[a] = d;
                }
                outer.Add(o);
                inner.Add(n);
                mid.Add(m);
                normals.Add(ns);
            }
            // Rows along +z; columns run clockwise seen from behind, so (z × round) points out: the outside as is.
            b.Stitch(outer, false);
            b.Stitch(inner, true);

            // The wired edge: round the cut from the back's top down one side, across the lip, and back up the other.
            var edge = new List<(Vector3 P, Vector3 Out)>();
            for (var i = 0; i <= Along; i++)
                edge.Add((mid[i][0], normals[i][0]));
            for (var a = 1; a <= Around; a++)
                edge.Add((mid[Along][a], normals[Along][a]));
            for (var i = Along - 1; i >= 0; i--)
                edge.Add((mid[i][Around], normals[i][Around]));
            Wire(b, edge, WireRadius);

            // The seam along the bottom, lapped: a low strip outside.
            var seam = new List<int[]>();
            var bottom = Around / 2;
            for (var i = 0; i <= Along; i++)
            {
                var z = Mathf.Lerp(BodyStart, Tip, (float)i / Along);
                var d = normals[i][bottom];
                var side = Vector3.Cross(Vector3.forward, d).normalized;
                var p = mid[i][bottom] + d * (Sheet / 2f + 0.0009f);
                var u = (z - BodyStart) * SteelPerMetre / BladeBack.width;
                seam.Add(new[]
                {
                    b.Vert(p - side * 0.0042f - d * 0.0009f, d - side, In(BladeBack, u, 0.95f)),
                    b.Vert(p - side * 0.0030f, d, In(BladeBack, u, 0.96f)),
                    b.Vert(p + side * 0.0030f, d, In(BladeBack, u, 0.98f)),
                    b.Vert(p + side * 0.0042f - d * 0.0009f, d + side, In(BladeBack, u, 0.99f)),
                });
            }
            b.Stitch(seam, true);
        }

        // A round wire of radius w along a path, each point with the surface's outward normal there.
        private static void Wire(Builder b, List<(Vector3 P, Vector3 Out)> path, float w)
        {
            const int sides = 8;
            var rows = new List<int[]>();
            var length = 0f;
            for (var i = 0; i < path.Count; i++)
            {
                var prev = path[Mathf.Max(0, i - 1)].P;
                var next = path[Mathf.Min(path.Count - 1, i + 1)].P;
                if (i > 0)
                    length += (path[i].P - path[i - 1].P).magnitude;
                var tangent = (next - prev).normalized;
                var outward = Vector3.ProjectOnPlane(path[i].Out, tangent).normalized;
                var binormal = Vector3.Cross(tangent, outward);
                var row = new int[sides + 1];
                for (var s = 0; s <= sides; s++)
                {
                    var angle = 2f * Mathf.PI * s / sides;
                    var d = outward * Mathf.Cos(angle) + binormal * Mathf.Sin(angle);
                    row[s] = b.Vert(path[i].P + d * w, d, In(BladeBack, Mathf.Repeat(length * SteelPerMetre / BladeBack.width, 1f), 0.02f + 0.06f * s / sides));
                }
                rows.Add(row);
            }
            // Rows along the path, columns round it from out toward the binormal: (path × round) points out.
            b.Stitch(rows, true);
        }

        private static void Back(Builder b)
        {
            // The back plate: outside and in, a rolled flange round it, and three rivets round the socket.
            Disk(b, new Vector3(0f, 0f, BodyStart), BackRadius, -1f, BladeBack, Around);
            Disk(b, new Vector3(0f, 0f, BodyStart + Sheet), BackRadius - Sheet, 1f, BladeFace, Around);
            var flange = new List<(Vector3 P, Vector3 Out)>();
            for (var a = 0; a <= Around; a++)
            {
                var phi = 2f * Mathf.PI * a / Around;
                var d = new Vector3(Mathf.Sin(phi), Mathf.Cos(phi), 0f);
                flange.Add((new Vector3(0f, 0f, BodyStart + 0.001f) + d * (BackRadius * Dent(BodyStart, Mathf.Max(phi, 1e-3f)) - 0.0005f), d));
            }
            Wire(b, flange, 0.0022f);
            for (var k = 0; k < 3; k++)
            {
                var angle = Mathf.PI / 2f + 2f * Mathf.PI * k / 3f;
                Rivet(b, new Vector3(0.027f * Mathf.Cos(angle), 0.027f * Mathf.Sin(angle), BodyStart), 0.0034f);
            }
        }

        // A round rivet head on the back plate, facing back.
        private static void Rivet(Builder b, Vector3 at, float r)
        {
            const int rings = 3;
            var rows = new List<int[]>();
            for (var k = 0; k <= rings; k++)
            {
                var theta = 0.5f * Mathf.PI * k / rings;
                var row = new int[Sides + 1];
                for (var s = 0; s <= Sides; s++)
                {
                    var angle = 2f * Mathf.PI * s / Sides;
                    var d = new Vector3(Mathf.Cos(angle) * Mathf.Cos(theta), Mathf.Sin(angle) * Mathf.Cos(theta), -Mathf.Sin(theta));
                    row[s] = b.Vert(at + new Vector3(d.x * r, d.y * r, d.z * r * 0.7f), d, In(Socket, 0.5f + 0.3f * d.x, 0.5f + 0.3f * d.y));
                }
                rows.Add(row);
            }
            b.Stitch(rows, false);
        }

        /// <summary>A quart of sand in the body: level with the lip's cut, heaped a little toward the back.</summary>
        public static Mesh CreateSand()
        {
            var b = new Builder();
            const int across = 14;
            var level = LipRadius * Mathf.Cos(OpenAtLip) + 0.004f;
            var rows = new List<int[]>();
            for (var i = 0; i <= Along; i++)
            {
                var t = (float)i / Along;
                var z = Mathf.Lerp(BodyStart + 0.004f, Tip - 0.012f, t);
                var r = RadiusAt(z) * 0.96f - 0.002f;
                var y = level + 0.012f * (1f - t);
                var half = Mathf.Sqrt(Mathf.Max(0f, r * r - y * y));
                var row = new int[across + 1];
                for (var a = 0; a <= across; a++)
                {
                    var x = Mathf.Lerp(-half, half, (float)a / across);
                    var u = half > 0f ? x / half : 0f;
                    var grain = 0.0015f * Mathf.Sin(97f * x + 13f * z) * Mathf.Sin(71f * z - 5f * x);
                    row[a] = b.Vert(new Vector3(x, y + 0.006f * (1f - u * u) + grain, z), Vector3.up, new Vector2(x * 6f, z * 6f));
                }
                rows.Add(row);
            }
            b.Stitch(rows, false);
            var mesh = b.Build("sand scoop load");
            mesh.RecalculateNormals();
            return mesh;
        }
    }
}
