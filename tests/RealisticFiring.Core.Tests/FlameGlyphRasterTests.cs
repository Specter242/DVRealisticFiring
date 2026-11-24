using System;
using System.Collections.Generic;
using System.Diagnostics;
using RealisticFiring.Core;
using Xunit;
using Xunit.Abstractions;

namespace RealisticFiring.Core.Tests
{
    public sealed class FlameGlyphRasterTests
    {
        private readonly ITestOutputHelper output;

        public FlameGlyphRasterTests(ITestOutputHelper output) => this.output = output;

        [Theory]
        [InlineData(8)]
        [InlineData(16)]
        [InlineData(64)]
        [InlineData(100)]
        [InlineData(128)]
        [InlineData(192)]
        [InlineData(256)]
        [InlineData(384)]
        public void Every_pixel_is_the_first_versions(int size)
        {
            var then = FirstVersion.Alpha(size);
            var now = FlameGlyphRaster.Alpha(size);
            Assert.Equal(then.Length, now.Length);
            var differ = new List<string>();
            for (var i = 0; i < then.Length && differ.Count < 10; i++)
                if (then[i] != now[i])
                    differ.Add($"({i % size}, {i / size}): {then[i]} then, {now[i]} now");
            Assert.True(differ.Count == 0, string.Join("; ", differ));
        }

        [Fact]
        public void The_icon_has_a_soft_edge_and_a_solid_body()
        {
            var alpha = FlameGlyphRaster.Alpha(192);
            int solid = 0, edge = 0;
            foreach (var a in alpha)
            {
                if (a == 255) solid++;
                else if (a > 0) edge++;
            }
            Assert.InRange(solid, 5000, 30000);
            Assert.InRange(edge, 300, 3000);
            // The tongue cut from its base is clear, the body around it solid.
            Assert.Equal(0, alpha[(int)(0.40f * 192) * 192 + 96]);
            Assert.Equal(255, alpha[(int)(0.18f * 192) * 192 + 96]);
        }

        [Fact]
        public void No_outline_has_an_edge_of_no_length()
        {
            foreach (var outline in new[] { FlameGlyphRaster.Outer, FlameGlyphRaster.Inner })
            {
                var p = FlameGlyphRaster.Polygon(outline);
                for (int i = 0, j = p.Length / 2 - 1; i < p.Length / 2; j = i++)
                    Assert.True(p[2 * i] != p[2 * j] || p[2 * i + 1] != p[2 * j + 1], $"edge {i}");
            }
        }

        [Fact]
        public void It_is_fast()
        {
            FlameGlyphRaster.Alpha(192);
            var best = double.MaxValue;
            for (var r = 0; r < 5; r++)
            {
                var clock = Stopwatch.StartNew();
                FlameGlyphRaster.Alpha(192);
                best = Math.Min(best, clock.Elapsed.TotalMilliseconds);
            }
            var then = Stopwatch.StartNew();
            FirstVersion.Alpha(192);
            output.WriteLine($"192 px: {best:F2} ms, the first version {then.Elapsed.TotalMilliseconds:F0} ms");
            // The adapter check holds the budget under Mono, as the game runs it; this only guards a gross slip.
            Assert.True(best < 50.0, $"{best:F1} ms");
        }

        // The first version (CommsTabletApp.FlameGlyph on main 408851f2), with
        // Unity's Vector2 and Mathf as the engine defines them.
        private static class FirstVersion
        {
            private readonly struct Vector2
            {
                public readonly float x, y;
                public Vector2(float x, float y) { this.x = x; this.y = y; }
                public float sqrMagnitude => x * x + y * y;
                public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
                public static Vector2 operator *(Vector2 a, float d) => new Vector2(a.x * d, a.y * d);
                public static float Dot(Vector2 lhs, Vector2 rhs) => lhs.x * rhs.x + lhs.y * rhs.y;
            }

            private static class Mathf
            {
                public static float Max(float a, float b) => a > b ? a : b;
                public static float Min(float a, float b) => a < b ? a : b;
                public static float Sqrt(float f) => (float)Math.Sqrt(f);

                public static float Clamp01(float value)
                {
                    if (value < 0F) return 0F;
                    if (value > 1F) return 1F;
                    return value;
                }
            }

            private static readonly float[][] Outer =
            {
                new[] { 0.50f, 0.14f, 0.68f, 0.14f, 0.78f, 0.27f, 0.78f, 0.42f },
                new[] { 0.78f, 0.42f, 0.78f, 0.53f, 0.73f, 0.60f, 0.70f, 0.67f },
                new[] { 0.70f, 0.67f, 0.66f, 0.60f, 0.62f, 0.58f, 0.60f, 0.58f },
                new[] { 0.60f, 0.58f, 0.62f, 0.70f, 0.56f, 0.80f, 0.46f, 0.88f },
                new[] { 0.46f, 0.88f, 0.47f, 0.76f, 0.40f, 0.68f, 0.32f, 0.58f },
                new[] { 0.32f, 0.58f, 0.25f, 0.50f, 0.22f, 0.45f, 0.22f, 0.40f },
                new[] { 0.22f, 0.40f, 0.22f, 0.25f, 0.33f, 0.14f, 0.50f, 0.14f },
            };

            private static readonly float[][] Inner =
            {
                new[] { 0.50f, 0.22f, 0.60f, 0.22f, 0.65f, 0.29f, 0.65f, 0.36f },
                new[] { 0.65f, 0.36f, 0.65f, 0.45f, 0.57f, 0.50f, 0.52f, 0.58f },
                new[] { 0.52f, 0.58f, 0.49f, 0.50f, 0.40f, 0.46f, 0.37f, 0.38f },
                new[] { 0.37f, 0.38f, 0.35f, 0.29f, 0.41f, 0.22f, 0.50f, 0.22f },
            };

            internal static byte[] Alpha(int size)
            {
                var outer = Polygon(Outer);
                var inner = Polygon(Inner);
                var pixels = new byte[size * size];
                for (var y = 0; y < size; y++)
                    for (var x = 0; x < size; x++)
                    {
                        float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                        var d = Mathf.Max(Distance(outer, u, v), -Distance(inner, u, v));
                        pixels[y * size + x] = (byte)(Mathf.Clamp01(0.5f - d * size) * 255f);
                    }
                return pixels;
            }

            private static Vector2[] Polygon(float[][] segments)
            {
                const int steps = 24;
                var points = new List<Vector2>();
                foreach (var s in segments)
                    for (var i = 0; i < steps; i++)
                    {
                        var t = i / (float)steps;
                        float a = (1 - t) * (1 - t) * (1 - t), b = 3 * (1 - t) * (1 - t) * t, c = 3 * (1 - t) * t * t, d = t * t * t;
                        points.Add(new Vector2(a * s[0] + b * s[2] + c * s[4] + d * s[6], a * s[1] + b * s[3] + c * s[5] + d * s[7]));
                    }
                return points.ToArray();
            }

            private static float Distance(Vector2[] v, float x, float y)
            {
                var p = new Vector2(x, y);
                var best = float.MaxValue;
                var sign = 1f;
                for (int i = 0, j = v.Length - 1; i < v.Length; j = i++)
                {
                    var e = v[j] - v[i];
                    var w = p - v[i];
                    var t = Mathf.Clamp01(Vector2.Dot(w, e) / Vector2.Dot(e, e));
                    best = Mathf.Min(best, (w - e * t).sqrMagnitude);
                    bool above = p.y >= v[i].y, below = p.y < v[j].y, left = e.x * w.y > e.y * w.x;
                    if ((above && below && left) || (!above && !below && !left))
                        sign = -sign;
                }
                return sign * Mathf.Sqrt(best);
            }
        }
    }
}
