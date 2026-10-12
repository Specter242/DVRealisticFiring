using System;
using System.Collections.Generic;
using RealisticFiring.Core;
using UnityEngine;

namespace RealisticFiring.Visuals
{
 internal static class OverlayArt
    {
        public static readonly Color Cream = Hex(0xf1e6cd);
        public static readonly Color Soft = Hex(0xb8ab92);
        public static readonly Color Dim = Hex(0x8f8573);
        public static readonly Color Brass = Hex(0xc9a24a);
        public static readonly Color Caution = Hex(0xf4c34c);
        public static readonly Color Alarm = Hex(0xff6a4d);
        public static readonly Color Steam = Hex(0x8fd0f5);
        public static readonly Color Iron = new Color(15 / 255f, 11 / 255f, 8 / 255f, 0.9f);
        public static readonly Color Edge = Hex(0x4b3a29);
        public static readonly Color Enamel = Hex(0x0a0a0a);
        public static readonly Color Rule = new Color(1f, 1f, 1f, 0.07f);
        public static readonly Color Ink = Hex(0x1b120c);

        private static Texture2D white, panel, rounded, ring, plateOff, plateOn, circle, triangle, square, rivet, check;
        private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();

        public static Color LevelColor(AdviceLevel level) =>
            level == AdviceLevel.Act ? Alarm : level == AdviceLevel.Watch ? Caution : Soft;

        public static Texture2D White => Live(ref white, () => Solid(Color.white));

        /// <summary>The card: smoked iron, an edge, a brass hairline inside it, rounded 8 px.</summary>
        public static Texture2D Panel => Live(ref panel, () => RoundRect(32, 8f, Iron, Edge, new Color(Brass.r, Brass.g, Brass.b, 0.22f)));

        /// <summary>A white rounded fill, 4 px, tinted where drawn.</summary>
        public static Texture2D Rounded => Live(ref rounded, () => RoundRect(16, 4f, Color.white, Color.clear, Color.clear));

        /// <summary>A white rounded ring, 2 px, tinted where drawn.</summary>
        public static Texture2D Ring => Live(ref ring, () => RoundRect(16, 4f, Color.clear, Color.white, Color.white));

        public static Texture2D PlateOff => Live(ref plateOff, () => RoundRect(16, 3f, Enamel, Hex(0x333333), Color.clear));

        public static Texture2D PlateOn => Live(ref plateOn, () => RoundRect(16, 3f, Enamel, Brass, Color.clear, bottomBar: Brass));

        public static Texture2D Mark(AdviceLevel level) =>
            level == AdviceLevel.Act ? Live(ref square, () => Shape(32, (x, y) => Math.Max(Math.Abs(x), Math.Abs(y)) - 0.62f))
            : level == AdviceLevel.Watch ? Live(ref triangle, () => Shape(32, Triangle))
            : Live(ref circle, () => Shape(32, (x, y) => Mathf.Sqrt(x * x + y * y) - 0.62f));

        public static Texture2D Rivet => Live(ref rivet, () => Rivets(16));

        /// <summary>A white tick, for a step done.</summary>
        public static Texture2D Check => Live(ref check, () => Shape(32, (x, y) =>
            Math.Min(Segment(x, y, -0.6f, 0.05f, -0.15f, -0.45f), Segment(x, y, -0.15f, -0.45f, 0.65f, 0.55f)) - 0.14f));

        // Distance from (x, y) to the segment (ax, ay) to (bx, by).
        private static float Segment(float x, float y, float ax, float ay, float bx, float by)
        {
            var dx = bx - ax;
            var dy = by - ay;
            var t = Mathf.Clamp01(((x - ax) * dx + (y - ay) * dy) / (dx * dx + dy * dy));
            var px = ax + t * dx - x;
            var py = ay + t * dy - y;
            return Mathf.Sqrt(px * px + py * py);
        }

        // A destroyed texture (the scene unloaded it) is made again.
        private static Texture2D Live(ref Texture2D texture, Func<Texture2D> make)
        {
            if (texture == null)
            {
                texture = make();
                texture.hideFlags = HideFlags.HideAndDontSave;
            }
            return texture;
        }

        private static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);

        private static Texture2D Solid(Color color)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            t.SetPixel(0, 0, color);
            t.Apply();
            return t;
        }

        // A rounded rectangle for nine-slicing: fill, an outer edge, and a
        // hairline one pixel inside it; optionally a bar along the bottom.
        private static Texture2D RoundRect(int size, float radius, Color fill, Color edge, Color hairline, Color? bottomBar = null)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var px = x + 0.5f;
                    var py = y + 0.5f;
                    // Distance inside the rounded rectangle's edge, in pixels.
                    var cx = Mathf.Clamp(px, radius, size - radius);
                    var cy = Mathf.Clamp(py, radius, size - radius);
                    var inside = radius - Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                    var cover = Mathf.Clamp01(inside + 0.5f);
                    var c = fill;
                    if (inside < 1.5f)
                        c = Over(c, edge, Mathf.Clamp01(1.5f - inside));
                    else if (inside < 2.5f)
                        c = Over(c, hairline, Mathf.Clamp01(2.5f - inside));
                    // The texture's bottom row is the plate's bottom edge.
                    if (bottomBar.HasValue && py < 3f)
                        c = Over(c, bottomBar.Value, 1f);
                    c.a *= cover;
                    pixels[y * size + x] = c;
                }
            }
            t.SetPixels(pixels);
            t.Apply();
            return t;
        }

        private static Color Over(Color under, Color over, float amount)
        {
            var a = over.a * amount;
            var outA = a + under.a * (1f - a);
            if (outA <= 0f)
                return Color.clear;
            var rgb = ((Vector4)over * a + (Vector4)under * under.a * (1f - a)) / outA;
            return new Color(rgb.x, rgb.y, rgb.z, outA);
        }

        private static float Triangle(float x, float y)
        {
            // An upward triangle in [-1, 1]: texture y runs up.
            var d1 = -y - 0.62f;
            var d2 = (0.866f * x + 0.5f * y) - 0.42f;
            var d3 = (-0.866f * x + 0.5f * y) - 0.42f;
            return Math.Max(d1, Math.Max(d2, d3));
        }

        // A white shape from a signed distance in [-1, 1] space, edges smoothed.
        private static Texture2D Shape(int size, Func<float, float, float> distance)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var d = distance((x + 0.5f) / size * 2f - 1f, (y + 0.5f) / size * 2f - 1f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - d * size / 2f));
                }
            t.SetPixels(pixels);
            t.Apply();
            return t;
        }

        private static Texture2D Rivets(int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var light = Hex(0xf0d48a);
            var dark = Hex(0x8a6a2a);
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var u = (x + 0.5f) / size * 2f - 1f;
                    var v = (y + 0.5f) / size * 2f - 1f;
                    var r = Mathf.Sqrt(u * u + v * v);
                    var lit = Mathf.Clamp01(1f - Mathf.Sqrt((u + 0.3f) * (u + 0.3f) + (v - 0.3f) * (v - 0.3f)));
                    var c = Color.Lerp(dark, light, lit);
                    c.a = Mathf.Clamp01((1f - r) * size / 2f);
                    t.SetPixel(x, y, c);
                }
            t.Apply();
            return t;
        }

        // ---- Dials ----

        // The dial's face as the study drew it: a 240° arc from lower left to
        // lower right in a 100 × 78 box, its centre at (50, 52), radius 40.
        public const float DialWidth = 100f;
        public const float DialHeight = 78f;
        private const float DialScale = 2.24f;
        private const float ArcStart = -210f;
        private const float ArcSweep = 240f;

        /// <summary>
        /// A dial face with its good band and its needle at <paramref name="value"/>,
        /// drawn once and kept while the needle stays within half a degree.
        /// </summary>
        public static Texture2D Dial(string key, float value, float? band0, float? band1, AdviceLevel level)
        {
            var needle = Mathf.Round(Mathf.Clamp01(value) * ArcSweep * 2f) / 2f;
            var id = $"{key}|{needle}|{band0}|{band1}|{level}";
            if (Cache.TryGetValue(key, out var cached) && cached != null && cached.name == id)
                return cached;
            var width = Mathf.RoundToInt(DialWidth * DialScale);
            var height = Mathf.RoundToInt(DialHeight * DialScale);
            var t = cached != null ? cached : new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            t.name = id;
            var pixels = new Color32[width * height];
            var track = new Color(1f, 1f, 1f, 0.15f);
            var bandColor = new Color(Brass.r, Brass.g, Brass.b, 0.6f);
            var tick = new Color(1f, 1f, 1f, 0.33f);
            var needleColor = level == AdviceLevel.Ok ? Cream : LevelColor(level);
            var needleAngle = (ArcStart + needle) * Mathf.Deg2Rad;
            var nx = Mathf.Cos(needleAngle);
            var ny = Mathf.Sin(needleAngle);
            var px1 = 1f / DialScale;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    // In the study's box: x right, y down.
                    var gx = (x + 0.5f) / DialScale;
                    var gy = (height - y - 0.5f) / DialScale;
                    var dx = gx - 50f;
                    var dy = gy - 52f;
                    var r = Mathf.Sqrt(dx * dx + dy * dy);
                    var angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                    if (angle > 90f)
                        angle -= 360f;
                    var t01 = (angle - ArcStart) / ArcSweep;
                    var c = Color.clear;
                    if (t01 >= 0f && t01 <= 1f)
                    {
                        var onArc = Mathf.Clamp01((3.5f - Mathf.Abs(r - 40f)) / px1 / 2f + 0.5f);
                        if (onArc > 0f)
                        {
                            var inBand = band0.HasValue && band1.HasValue && t01 >= band0.Value && t01 <= band1.Value;
                            c = Over(c, inBand ? bandColor : track, onArc);
                        }
                        for (var k = 0; k <= 4; k++)
                        {
                            var ta = (ArcStart + ArcSweep * k / 4f) * Mathf.Deg2Rad;
                            var across = Mathf.Abs(-dx * Mathf.Sin(ta) + dy * Mathf.Cos(ta));
                            if (r >= 38f && r <= 43f)
                                c = Over(c, tick, Mathf.Clamp01((0.75f - across) / px1 + 0.5f));
                        }
                    }
                    // The needle: a 3-unit line from the hub to radius 30, and the hub.
                    var along = dx * nx + dy * ny;
                    if (along >= 0f && along <= 30f)
                    {
                        var off = Mathf.Abs(-dx * ny + dy * nx);
                        c = Over(c, needleColor, Mathf.Clamp01((1.5f - off) / px1 + 0.5f));
                    }
                    if (r <= 4.5f)
                        c = Over(c, Brass, Mathf.Clamp01((4f - r) / px1 + 0.5f));
                    pixels[y * width + x] = c;
                }
            }
            t.SetPixels32(pixels);
            t.Apply();
            Cache[key] = t;
            return t;
        }

        /// <summary>A zone's glow: dark iron when cold, through red and orange to white heat.</summary>
        public static Color Ember(float glow)
        {
            var stops = new[]
            {
                (0f, new Color(46 / 255f, 36 / 255f, 30 / 255f)), (0.35f, new Color(120 / 255f, 42 / 255f, 16 / 255f)),
                (0.65f, new Color(214 / 255f, 92 / 255f, 24 / 255f)), (0.85f, new Color(1f, 160 / 255f, 52 / 255f)), (1f, new Color(1f, 222 / 255f, 140 / 255f)),
            };
            glow = Mathf.Clamp01(glow);
            for (var i = 1; i < stops.Length; i++)
                if (glow <= stops[i].Item1)
                    return Color.Lerp(stops[i - 1].Item2, stops[i].Item2, (glow - stops[i - 1].Item1) / (stops[i].Item1 - stops[i - 1].Item1));
            return stops[stops.Length - 1].Item2;
        }
    }
}
