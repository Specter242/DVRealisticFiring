using System;
using System.Collections.Generic;

namespace RealisticFiring.Core
{
    /// <summary>
    /// The Firing app's icon for the Comms Tablet: a filled flame with a tongue
    /// of flame cut out of its base, as the alpha of a white glyph, drawn from
    /// its signed distance field in a unit square (y up), one byte a pixel,
    /// bottom row first.
    /// </summary>
    /// <remarks>
    /// The same pixels the first version drew, faster. That one measured, for
    /// each pixel, the distance to every edge of both outlines: at 192 pixels
    /// it took 1.5 s of the tablet's first frame (Comms Tablet thread, 23 Nov
    /// 2026). A pixel's alpha only changes within half a pixel of an outline,
    /// so the distance is measured only for pixels within a few pixels of an
    /// edge, and then only to the edges near it. Everywhere a pixel lies
    /// decides inside or outside from the edges spanning its row, with the
    /// first version's own test. Each value that reaches a pixel comes from
    /// the same arithmetic as before, so every pixel is the same.
    /// </remarks>
    public static class FlameGlyphRaster
    {
        // Cubic Bézier segments, each from the last one's end.
        internal static readonly float[][] Outer =
        {
            new[] { 0.50f, 0.14f, 0.68f, 0.14f, 0.78f, 0.27f, 0.78f, 0.42f },
            new[] { 0.78f, 0.42f, 0.78f, 0.53f, 0.73f, 0.60f, 0.70f, 0.67f },
            new[] { 0.70f, 0.67f, 0.66f, 0.60f, 0.62f, 0.58f, 0.60f, 0.58f },
            new[] { 0.60f, 0.58f, 0.62f, 0.70f, 0.56f, 0.80f, 0.46f, 0.88f },
            new[] { 0.46f, 0.88f, 0.47f, 0.76f, 0.40f, 0.68f, 0.32f, 0.58f },
            new[] { 0.32f, 0.58f, 0.25f, 0.50f, 0.22f, 0.45f, 0.22f, 0.40f },
            new[] { 0.22f, 0.40f, 0.22f, 0.25f, 0.33f, 0.14f, 0.50f, 0.14f },
        };

        internal static readonly float[][] Inner =
        {
            new[] { 0.50f, 0.22f, 0.60f, 0.22f, 0.65f, 0.29f, 0.65f, 0.36f },
            new[] { 0.65f, 0.36f, 0.65f, 0.45f, 0.57f, 0.50f, 0.52f, 0.58f },
            new[] { 0.52f, 0.58f, 0.49f, 0.50f, 0.40f, 0.46f, 0.37f, 0.38f },
            new[] { 0.37f, 0.38f, 0.35f, 0.29f, 0.41f, 0.22f, 0.50f, 0.22f },
        };

        /// <summary>Points along each segment of an outline.</summary>
        internal const int Steps = 24;

        // A pixel this many pixels from every edge has a saturated alpha (it changes
        // within half a pixel), with room to spare for rounding.
        private const float Band = 2f;

        /// <summary>The glyph's alpha, <paramref name="size"/> by <paramref name="size"/>, bottom row first.</summary>
        public static byte[] Alpha(int size)
        {
            if (size <= 0)
                throw new ArgumentOutOfRangeException(nameof(size));
            var outer = new Outline(Polygon(Outer), size);
            var inner = new Outline(Polygon(Inner), size);
            var alpha = new byte[size * size];
            for (var y = 0; y < size; y++)
            {
                var v = (y + 0.5f) / size;
                outer.StartRow(v);
                inner.StartRow(v);
                for (var x = 0; x < size; x++)
                {
                    var u = (x + 0.5f) / size;
                    var d = Max(outer.Distance(u, v, x, y), -inner.Distance(u, v, x, y));
                    alpha[y * size + x] = (byte)(Clamp01(0.5f - d * size) * 255f);
                }
            }
            return alpha;
        }

        /// <summary>An outline's points: x and y in turn.</summary>
        internal static float[] Polygon(float[][] segments)
        {
            var points = new List<float>(segments.Length * Steps * 2);
            foreach (var s in segments)
                for (var i = 0; i < Steps; i++)
                {
                    var t = i / (float)Steps;
                    float a = (1 - t) * (1 - t) * (1 - t), b = 3 * (1 - t) * (1 - t) * t, c = 3 * (1 - t) * t * t, d = t * t * t;
                    points.Add(a * s[0] + b * s[2] + c * s[4] + d * s[6]);
                    points.Add(a * s[1] + b * s[3] + c * s[5] + d * s[7]);
                }
            return points.ToArray();
        }

        // Unity's Mathf, written out: the same results without the engine.
        private static float Max(float a, float b) => a > b ? a : b;
        private static float Min(float a, float b) => a < b ? a : b;

        private static float Clamp01(float value)
        {
            if (value < 0f)
                return 0f;
            if (value > 1f)
                return 1f;
            return value;
        }

        /// <summary>
        /// A closed polygon's signed distance (negative inside), worked out only
        /// where it is not saturated: elsewhere a stand-in as far as a band.
        /// </summary>
        private sealed class Outline
        {
            private readonly int size;
            private readonly int count;
            // Point i, and edge i from point i to point i - 1 (the first version's j).
            private readonly float[] px, py, ex, ey;
            // Pixels within the band of some edge.
            private readonly bool[] near;
            // The edges that could be nearest a pixel in each cell.
            private readonly int cell, cells;
            private readonly int[][] nearby;
            // This row's edges that can change the sign, and whether they do so on their left.
            private readonly List<int> spanning = new List<int>();
            private readonly List<bool> flipsOnLeft = new List<bool>();
            private readonly float far;

            public Outline(float[] points, int size)
            {
                this.size = size;
                count = points.Length / 2;
                px = new float[count];
                py = new float[count];
                ex = new float[count];
                ey = new float[count];
                for (var i = 0; i < count; i++)
                {
                    px[i] = points[2 * i];
                    py[i] = points[2 * i + 1];
                }
                var longest = 0f;
                for (var i = 0; i < count; i++)
                {
                    var j = i == 0 ? count - 1 : i - 1;
                    ex[i] = px[j] - px[i];
                    ey[i] = py[j] - py[i];
                    longest = Math.Max(longest, (float)Math.Sqrt(ex[i] * ex[i] + ey[i] * ey[i]) * size);
                }
                // A pixel's stand-in distance, past the band: its sign is all that counts.
                far = 4f * Band / size;

                near = new bool[size * size];
                for (var i = 0; i < count; i++)
                {
                    Box(i, Band, out var x0, out var y0, out var x1, out var y1);
                    for (var y = y0; y <= y1; y++)
                        for (var x = x0; x <= x1; x++)
                            near[y * size + x] = true;
                }

                // A pixel in the band is within the box's diagonal and the edge's length of
                // an edge; every edge that close is listed in its cell.
                var reach = Band * 1.5f + longest + 2f;
                cell = Math.Max(1, (int)Math.Ceiling(reach));
                cells = (size + cell - 1) / cell;
                var lists = new List<int>[cells * cells];
                for (var i = 0; i < count; i++)
                {
                    Box(i, reach, out var x0, out var y0, out var x1, out var y1);
                    for (var cy = y0 / cell; cy <= y1 / cell; cy++)
                        for (var cx = x0 / cell; cx <= x1 / cell; cx++)
                            (lists[cy * cells + cx] ?? (lists[cy * cells + cx] = new List<int>())).Add(i);
                }
                nearby = new int[lists.Length][];
                for (var k = 0; k < lists.Length; k++)
                    nearby[k] = lists[k] != null ? lists[k].ToArray() : Array.Empty<int>();
            }

            // The pixels whose centers lie within a margin of edge i's bounding box, clipped to the square.
            private void Box(int i, float margin, out int x0, out int y0, out int x1, out int y1)
            {
                var j = i == 0 ? count - 1 : i - 1;
                float minX = Math.Min(px[i], px[j]) * size - 0.5f, maxX = Math.Max(px[i], px[j]) * size - 0.5f;
                float minY = Math.Min(py[i], py[j]) * size - 0.5f, maxY = Math.Max(py[i], py[j]) * size - 0.5f;
                x0 = Math.Max(0, (int)Math.Floor(minX - margin));
                y0 = Math.Max(0, (int)Math.Floor(minY - margin));
                x1 = Math.Min(size - 1, (int)Math.Ceiling(maxX + margin));
                y1 = Math.Min(size - 1, (int)Math.Ceiling(maxY + margin));
            }

            /// <summary>
            /// Finds the edges that can change the sign on the row at <paramref name="v"/>:
            /// with the first version's above and below, only an edge for which both or
            /// neither hold flips it, and that depends on its side alone.
            /// </summary>
            public void StartRow(float v)
            {
                spanning.Clear();
                flipsOnLeft.Clear();
                for (int i = 0, j = count - 1; i < count; j = i++)
                {
                    bool above = v >= py[i], below = v < py[j];
                    if (above != below)
                        continue;
                    spanning.Add(i);
                    flipsOnLeft.Add(above);
                }
            }

            public float Distance(float u, float v, int x, int y)
            {
                var sign = 1f;
                for (var k = 0; k < spanning.Count; k++)
                {
                    var i = spanning[k];
                    float wx = u - px[i], wy = v - py[i];
                    var left = ex[i] * wy > ey[i] * wx;
                    if (left == flipsOnLeft[k])
                        sign = -sign;
                }
                if (!near[y * size + x])
                    return sign * far;

                var best = float.MaxValue;
                foreach (var i in nearby[(y / cell) * cells + x / cell])
                {
                    float wx = u - px[i], wy = v - py[i];
                    var t = Clamp01((wx * ex[i] + wy * ey[i]) / (ex[i] * ex[i] + ey[i] * ey[i]));
                    float dx = wx - ex[i] * t, dy = wy - ey[i] * t;
                    best = Min(best, dx * dx + dy * dy);
                }
                return sign * (float)Math.Sqrt(best);
            }
        }
    }
}
