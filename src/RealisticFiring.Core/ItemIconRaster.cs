using System;

namespace RealisticFiring.Core
{
    /// <summary>
    /// The engine-free half of the item icon renderer (the game half is
    /// <c>Items/ItemIcons.cs</c>): where to put the camera so an item fills
    /// its frame, and what to make of two renders of it, one over black and
    /// one over white, in the style of the game's own icons.
    /// </summary>
    /// <remarks>
    /// The game's icons (Derail Valley's <c>ItemIconRenderer</c>, an editor
    /// tool) are lit renders of each item's preview at 128 by 128 pixels,
    /// supersampled, then cropped to the item with a 2 pixel margin, so they
    /// are not square (the shovel's is 76 by 110). Its dropped ("ghost")
    /// icons are the same silhouette in light blue, 133, 212, 239: the item
    /// itself at alpha 25 of 255, inside a 5 pixel outline at alpha 153,
    /// cropped the same way, so 10 pixels larger each way. Measured from the
    /// game's own icons, 2026-11-27.
    ///
    /// Images here are rows of RGBA, bottom row first (as Unity reads a
    /// render back). A render's colors are either linear (a linear-space
    /// project, whose render textures hold sRGB) or gamma; the arithmetic is
    /// done in linear light either way, so edges and downsampling blend as
    /// the GPU blended them.
    /// </remarks>
    public static class ItemIconRaster
    {
        /// <summary>The margin left round the item when it is cropped, px.</summary>
        public const int Margin = 2;

        /// <summary>A dropped icon's outline width, px.</summary>
        public const int DroppedOutline = 5;

        /// <summary>A dropped icon's color, sRGB bytes.</summary>
        public const byte DroppedRed = 133, DroppedGreen = 212, DroppedBlue = 239;

        /// <summary>A dropped icon's alpha inside the item and on its outline.</summary>
        public const float DroppedFill = 25f / 255f, DroppedLine = 153f / 255f;

        // Coverage below this is empty: a crop never reaches for render noise.
        private const float Empty = 0.75f / 255f;

        private static readonly float[] Decode = BuildDecode();

        private static float[] BuildDecode()
        {
            var table = new float[256];
            for (var i = 0; i < 256; i++)
                table[i] = ToLinear(i / 255f);
            return table;
        }

        /// <summary>The sRGB transfer, encoded value to linear light.</summary>
        public static float ToLinear(float encoded) =>
            encoded <= 0.04045f ? encoded / 12.92f : (float)Math.Pow((encoded + 0.055f) / 1.055f, 2.4);

        /// <summary>The sRGB transfer, linear light to encoded value.</summary>
        public static float ToEncoded(float linear) =>
            linear <= 0.0031308f ? linear * 12.92f : 1.055f * (float)Math.Pow(linear, 1d / 2.4d) - 0.055f;

        /// <summary>
        /// Where a camera looking along +z (y up, a square frame whose half
        /// height is <paramref name="tanHalfFov"/> at unit distance) sees all
        /// of <paramref name="points"/> (x, y, z triples, already turned to
        /// the view) within <paramref name="fill"/> of its frame's half width,
        /// as close as that allows: the camera's position and the near and far
        /// planes, with room to spare. False with no points.
        /// </summary>
        public static bool Frame(float[] points, float tanHalfFov, float fill, out float x, out float y, out float z, out float near, out float far)
        {
            x = y = z = near = far = 0f;
            if (points == null || points.Length < 3 || tanHalfFov <= 0f || fill <= 0f)
                return false;
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (var i = 0; i + 2 < points.Length; i += 3)
            {
                minX = Math.Min(minX, points[i]);
                maxX = Math.Max(maxX, points[i]);
                minY = Math.Min(minY, points[i + 1]);
                maxY = Math.Max(maxY, points[i + 1]);
            }
            x = (minX + maxX) * 0.5f;
            y = (minY + maxY) * 0.5f;
            // Each point needs |offset| <= fill * tan * depth: the camera stands back far enough for the most demanding.
            var reach = fill * tanHalfFov;
            z = float.MaxValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            for (var i = 0; i + 2 < points.Length; i += 3)
            {
                var off = Math.Max(Math.Abs(points[i] - x), Math.Abs(points[i + 1] - y));
                z = Math.Min(z, points[i + 2] - off / reach);
                minZ = Math.Min(minZ, points[i + 2]);
                maxZ = Math.Max(maxZ, points[i + 2]);
            }
            // A point exactly on the axis would otherwise put the camera on it.
            var size = Math.Max(Math.Max(maxX - minX, maxY - minY), maxZ - minZ);
            z = Math.Min(z, minZ - Math.Max(size, 1e-3f) * 0.05f);
            near = Math.Max((minZ - z) * 0.5f, 1e-4f);
            far = (maxZ - z) * 2f + near;
            return true;
        }

        /// <summary>
        /// The item's premultiplied color and coverage, RGBA floats in linear
        /// light, from a render over black and the same render over white
        /// (RGBA32 bytes): over black a pixel is a * C, over white a * C + 1 - a,
        /// so their difference is what the item let through. With
        /// <paramref name="linear"/> the bytes are sRGB-encoded and are decoded
        /// first; otherwise the blending was done on the bytes as they are.
        /// </summary>
        public static float[] Unblend(byte[] overBlack, byte[] overWhite, bool linear)
        {
            if (overBlack == null || overWhite == null || overBlack.Length != overWhite.Length || overBlack.Length % 4 != 0)
                throw new ArgumentException("two renders of the same size are needed");
            var result = new float[overBlack.Length];
            for (var i = 0; i < overBlack.Length; i += 4)
            {
                float through = 0f;
                for (var c = 0; c < 3; c++)
                {
                    var b = linear ? Decode[overBlack[i + c]] : overBlack[i + c] / 255f;
                    var w = linear ? Decode[overWhite[i + c]] : overWhite[i + c] / 255f;
                    through += w - b;
                }
                var a = Clamp01(1f - through / 3f);
                if (a < Empty)
                    continue;
                for (var c = 0; c < 3; c++)
                {
                    var b = linear ? Decode[overBlack[i + c]] : overBlack[i + c] / 255f;
                    result[i + c] = Math.Min(b, a);
                }
                result[i + 3] = a;
            }
            return result;
        }

        /// <summary>
        /// <paramref name="image"/> (premultiplied RGBA floats) reduced by
        /// <paramref name="factor"/> each way, each pixel the mean of the
        /// block it covers. The size must divide by the factor.
        /// </summary>
        public static float[] Downsample(float[] image, int width, int height, int factor)
        {
            if (factor < 1 || width % factor != 0 || height % factor != 0 || image.Length != width * height * 4)
                throw new ArgumentException("the image's size must divide by the factor");
            var w = width / factor;
            var h = height / factor;
            var result = new float[w * h * 4];
            var scale = 1f / (factor * factor);
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var o = (y * w + x) * 4;
                    for (var dy = 0; dy < factor; dy++)
                    {
                        var row = ((y * factor + dy) * width + x * factor) * 4;
                        for (var dx = 0; dx < factor * 4; dx++)
                            result[o + dx % 4] += image[row + dx];
                    }
                    for (var c = 0; c < 4; c++)
                        result[o + c] *= scale;
                }
            return result;
        }

        /// <summary>
        /// The smallest rectangle holding every pixel the item covers: its
        /// left and bottom column and row, width and height. False if it
        /// covers none.
        /// </summary>
        public static bool Bounds(float[] image, int width, int height, out int x, out int y, out int w, out int h)
        {
            int minX = width, minY = height, maxX = -1, maxY = -1;
            for (var row = 0; row < height; row++)
                for (var col = 0; col < width; col++)
                {
                    if (image[(row * width + col) * 4 + 3] < Empty)
                        continue;
                    minX = Math.Min(minX, col);
                    maxX = Math.Max(maxX, col);
                    minY = Math.Min(minY, row);
                    maxY = Math.Max(maxY, row);
                }
            x = minX;
            y = minY;
            w = maxX - minX + 1;
            h = maxY - minY + 1;
            return maxX >= 0;
        }

        /// <summary>
        /// The <paramref name="w"/> by <paramref name="h"/> region at
        /// (<paramref name="x"/>, <paramref name="y"/>) of
        /// <paramref name="image"/>, with <see cref="Margin"/> round it, as
        /// RGBA32 bytes with straight alpha, encoded for an sRGB texture when
        /// <paramref name="linear"/>. Empty pixels take the color of the item
        /// beside them, so filtering and mipmaps never draw a dark fringe.
        /// </summary>
        public static byte[] Encode(float[] image, int width, int height, int x, int y, int w, int h, bool linear, out int outWidth, out int outHeight)
        {
            outWidth = w + 2 * Margin;
            outHeight = h + 2 * Margin;
            var result = new byte[outWidth * outHeight * 4];
            for (var row = 0; row < h; row++)
                for (var col = 0; col < w; col++)
                {
                    var sx = x + col;
                    var sy = y + row;
                    if (sx < 0 || sy < 0 || sx >= width || sy >= height)
                        continue;
                    var i = (sy * width + sx) * 4;
                    var a = image[i + 3];
                    if (a < Empty)
                        continue;
                    var o = ((row + Margin) * outWidth + col + Margin) * 4;
                    for (var c = 0; c < 3; c++)
                    {
                        var straight = Clamp01(image[i + c] / a);
                        result[o + c] = Byte(linear ? ToEncoded(straight) : straight);
                    }
                    result[o + 3] = Byte(a);
                }
            Bleed(result, outWidth, outHeight, 4);
            return result;
        }

        /// <summary>
        /// The dropped icon for the same region (<see cref="Encode"/>'s
        /// arguments): the item's coverage at <see cref="DroppedFill"/> inside
        /// an outline <see cref="DroppedOutline"/> pixels wide at
        /// <see cref="DroppedLine"/>, in the dropped icons' blue, with
        /// <see cref="Margin"/> round it; RGBA32 bytes, sRGB.
        /// </summary>
        public static byte[] Dropped(float[] image, int width, int height, int x, int y, int w, int h, out int outWidth, out int outHeight)
        {
            var border = DroppedOutline + Margin;
            outWidth = w + 2 * border;
            outHeight = h + 2 * border;
            var coverage = new float[outWidth * outHeight];
            for (var row = 0; row < h; row++)
                for (var col = 0; col < w; col++)
                {
                    var sx = x + col;
                    var sy = y + row;
                    if (sx >= 0 && sy >= 0 && sx < width && sy < height)
                        coverage[(row + border) * outWidth + col + border] = Clamp01(image[(sy * width + sx) * 4 + 3]);
                }
            var grown = Grow(coverage, outWidth, outHeight, DroppedOutline);
            var result = new byte[outWidth * outHeight * 4];
            for (var i = 0; i < coverage.Length; i++)
            {
                var o = i * 4;
                result[o] = DroppedRed;
                result[o + 1] = DroppedGreen;
                result[o + 2] = DroppedBlue;
                var a = coverage[i];
                result[o + 3] = Byte(DroppedFill * a + DroppedLine * Math.Max(0f, grown[i] - a));
            }
            return result;
        }

        /// <summary>
        /// Coverage grown by <paramref name="radius"/> pixels with a soft
        /// edge: each pixel the most any covered pixel within the radius gives
        /// it, fading out over the pixel beyond.
        /// </summary>
        internal static float[] Grow(float[] coverage, int width, int height, float radius)
        {
            var reach = (int)Math.Ceiling(radius + 1f);
            var offsets = new System.Collections.Generic.List<(int dx, int dy, float weight)>();
            for (var dy = -reach; dy <= reach; dy++)
                for (var dx = -reach; dx <= reach; dx++)
                {
                    var weight = Clamp01(radius + 1f - (float)Math.Sqrt(dx * dx + dy * dy));
                    if (weight > 0f)
                        offsets.Add((dx, dy, weight));
                }
            var result = new float[coverage.Length];
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var a = coverage[y * width + x];
                    if (a <= 0f)
                        continue;
                    foreach (var (dx, dy, weight) in offsets)
                    {
                        var tx = x + dx;
                        var ty = y + dy;
                        if (tx < 0 || ty < 0 || tx >= width || ty >= height)
                            continue;
                        var i = ty * width + tx;
                        var v = a * weight;
                        if (v > result[i])
                            result[i] = v;
                    }
                }
            return result;
        }

        /// <summary>
        /// Gives each empty pixel (alpha 0) the mean color of the filled
        /// pixels beside it, <paramref name="passes"/> pixels out; alpha is
        /// untouched.
        /// </summary>
        internal static void Bleed(byte[] rgba, int width, int height, int passes)
        {
            var filled = new bool[width * height];
            for (var i = 0; i < filled.Length; i++)
                filled[i] = rgba[i * 4 + 3] > 0;
            for (var pass = 0; pass < passes; pass++)
            {
                var next = (bool[])filled.Clone();
                for (var y = 0; y < height; y++)
                    for (var x = 0; x < width; x++)
                    {
                        var i = y * width + x;
                        if (filled[i])
                            continue;
                        int r = 0, g = 0, b = 0, n = 0;
                        for (var dy = -1; dy <= 1; dy++)
                            for (var dx = -1; dx <= 1; dx++)
                            {
                                var tx = x + dx;
                                var ty = y + dy;
                                if (tx < 0 || ty < 0 || tx >= width || ty >= height || !filled[ty * width + tx])
                                    continue;
                                var j = (ty * width + tx) * 4;
                                r += rgba[j];
                                g += rgba[j + 1];
                                b += rgba[j + 2];
                                n++;
                            }
                        if (n == 0)
                            continue;
                        rgba[i * 4] = (byte)(r / n);
                        rgba[i * 4 + 1] = (byte)(g / n);
                        rgba[i * 4 + 2] = (byte)(b / n);
                        next[i] = true;
                    }
                filled = next;
            }
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

        private static byte Byte(float v) => (byte)Math.Round(Clamp01(v) * 255f);
    }
}
