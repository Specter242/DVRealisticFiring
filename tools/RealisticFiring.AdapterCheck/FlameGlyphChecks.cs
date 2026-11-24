using System;
using System.Collections.Generic;
using System.Diagnostics;
using RealisticFiring.Core;
using UnityEngine;

namespace RealisticFiring.AdapterCheck
{
    /// <summary>
    /// The Firing app's tablet icon (<see cref="FlameGlyphRaster"/>) against
    /// the first version, kept here as it was (with Unity's own Vector2 and
    /// Mathf, under Mono as the game runs it): every pixel the same at the
    /// tablet's 192 and other sizes, and the time it takes. The first version
    /// took 1.5 s of the tablet's first frame in game (tracker #44).
    /// </summary>
    internal static class FlameGlyphChecks
    {
        // Under this at the tablet's size, best of a few runs, ms (tracker #44: "under ~20 ms").
        private const double Budget = 20.0;

        internal static void Run(Action<bool, string> check)
        {
            foreach (var size in new[] { 16, 64, 128, 192, 256 })
            {
                var then = FirstVersion.Alpha(size);
                var now = FlameGlyphRaster.Alpha(size);
                int differ = 0, partial = 0;
                for (var i = 0; i < then.Length; i++)
                {
                    if (then[i] != now[i])
                        differ++;
                    if (then[i] > 0 && then[i] < 255)
                        partial++;
                }
                check(then.Length == now.Length && differ == 0,
                    $"the flame icon at {size} px is the first version's, pixel for pixel ({differ} of {then.Length} differ; {partial} on its edges)");
            }

            var before = Time(() => FirstVersion.Alpha(192), 1);
            var after = Time(() => FlameGlyphRaster.Alpha(192), 7);
            check(after < Budget,
                $"the flame icon at 192 px takes {after:F1} ms under Mono, best of 7 (the first version {before:F0} ms; budget {Budget:F0} ms)");
        }

        private static double Time(Func<byte[]> draw, int runs)
        {
            draw();
            var best = double.MaxValue;
            for (var r = 0; r < runs; r++)
            {
                var clock = Stopwatch.StartNew();
                draw();
                best = Math.Min(best, clock.Elapsed.TotalMilliseconds);
            }
            return best;
        }

        // The first version (CommsTabletApp.FlameGlyph on main 408851f2), its texture's alpha only.
        private static class FirstVersion
        {
            // Cubic Bézier segments, each from the last one's end.
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

            // The signed distance to a closed polygon: negative inside.
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
