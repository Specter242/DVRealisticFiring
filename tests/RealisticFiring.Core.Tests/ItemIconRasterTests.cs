using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests
{
    public sealed class ItemIconRasterTests
    {
        // A box's eight corners, x, y, z triples.
        private static float[] Box(float x0, float y0, float z0, float x1, float y1, float z1)
        {
            var points = new List<float>();
            foreach (var x in new[] { x0, x1 })
                foreach (var y in new[] { y0, y1 })
                    foreach (var z in new[] { z0, z1 })
                        points.AddRange(new[] { x, y, z });
            return points.ToArray();
        }

        [Fact]
        public void Framing_puts_every_corner_inside_the_fill_and_one_on_its_edge()
        {
            const float tan = 0.2f, fill = 0.8f;
            var points = Box(-0.1f, 0.3f, 2f, 0.4f, 0.5f, 2.3f);
            Assert.True(ItemIconRaster.Frame(points, tan, fill, out var x, out var y, out var z, out var near, out var far));
            Assert.Equal(0.15f, x, 4);
            Assert.Equal(0.4f, y, 4);
            var most = 0f;
            for (var i = 0; i < points.Length; i += 3)
            {
                var depth = points[i + 2] - z;
                Assert.True(depth > near && depth < far, "a corner is clipped");
                var u = Math.Abs(points[i] - x) / (depth * tan);
                var v = Math.Abs(points[i + 1] - y) / (depth * tan);
                Assert.True(u <= fill + 1e-4f && v <= fill + 1e-4f);
                most = Math.Max(most, Math.Max(u, v));
            }
            Assert.Equal(fill, most, 3);
        }

        [Fact]
        public void Framing_backs_off_from_a_flat_item_on_the_axis()
        {
            var points = Box(0f, 0f, 1f, 0f, 0f, 1f);
            Assert.True(ItemIconRaster.Frame(points, 0.2f, 0.8f, out _, out _, out var z, out var near, out _));
            Assert.True(z < 1f && near > 0f);
            Assert.False(ItemIconRaster.Frame(new float[0], 0.2f, 0.8f, out _, out _, out _, out _, out _));
        }

        [Fact]
        public void Unblending_recovers_color_and_coverage()
        {
            // Half covered by a mid red, in gamma bytes: over black 0.5 * C, over white 0.5 * C + 0.5.
            var black = new byte[] { 100, 20, 10, 255, 0, 0, 0, 255 };
            var white = new byte[] { 228, 148, 138, 255, 255, 255, 255, 255 };
            var image = ItemIconRaster.Unblend(black, white, linear: false);
            Assert.Equal(128f / 255f, image[3], 2);
            Assert.Equal(100f / 255f, image[0], 3);
            Assert.Equal(0f, image[7]);
            Assert.Equal(0f, image[4]);
        }

        [Fact]
        public void Unblending_in_linear_light_reads_an_opaque_pixel_exactly()
        {
            var black = new byte[] { 200, 120, 40, 255 };
            var image = ItemIconRaster.Unblend(black, black, linear: true);
            Assert.Equal(1f, image[3], 4);
            var bytes = ItemIconRaster.Encode(image, 1, 1, 0, 0, 1, 1, linear: true, out var w, out var h);
            var center = (ItemIconRaster.Margin * w + ItemIconRaster.Margin) * 4;
            Assert.Equal(new byte[] { 200, 120, 40, 255 }, bytes.Skip(center).Take(4).ToArray());
        }

        [Fact]
        public void Downsampling_averages_each_block()
        {
            var image = new float[4 * 2 * 4];
            // The left 2 by 2 block: one opaque white pixel of four.
            image[0] = image[1] = image[2] = image[3] = 1f;
            var small = ItemIconRaster.Downsample(image, 4, 2, 2);
            Assert.Equal(8, small.Length);
            Assert.Equal(0.25f, small[3], 5);
            Assert.Equal(0.25f, small[0], 5);
            Assert.Equal(0f, small[7]);
            Assert.Throws<ArgumentException>(() => ItemIconRaster.Downsample(image, 4, 2, 3));
        }

        [Fact]
        public void Bounds_and_crop_keep_the_item_with_its_margin()
        {
            const int size = 10;
            var image = new float[size * size * 4];
            foreach (var (x, y) in new[] { (3, 2), (6, 7) })
            {
                var i = (y * size + x) * 4;
                image[i] = image[i + 3] = 1f;
            }
            Assert.True(ItemIconRaster.Bounds(image, size, size, out var bx, out var by, out var bw, out var bh));
            Assert.Equal((3, 2, 4, 6), (bx, by, bw, bh));
            var bytes = ItemIconRaster.Encode(image, size, size, bx, by, bw, bh, linear: false, out var w, out var h);
            Assert.Equal((4 + 2 * ItemIconRaster.Margin, 6 + 2 * ItemIconRaster.Margin), (w, h));
            // The corner pixel is the item's; a pixel of the margin is empty but carries its neighbor's color.
            var corner = (ItemIconRaster.Margin * w + ItemIconRaster.Margin) * 4;
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, bytes.Skip(corner).Take(4).ToArray());
            var beside = corner - 4;
            Assert.Equal(0, bytes[beside + 3]);
            Assert.Equal(255, bytes[beside]);
            Assert.False(ItemIconRaster.Bounds(new float[16], 2, 2, out _, out _, out _, out _));
        }

        [Fact]
        public void The_dropped_icon_is_the_games_ghost_a_solid_fill_inside_a_five_pixel_outline()
        {
            const int size = 20;
            var image = new float[size * size * 4];
            for (var y = 5; y < 15; y++)
                for (var x = 5; x < 15; x++)
                    image[(y * size + x) * 4 + 3] = 1f;
            var bytes = ItemIconRaster.Dropped(image, size, size, 5, 5, 10, 10, out var w, out var h);
            var border = ItemIconRaster.DroppedOutline + ItemIconRaster.Margin;
            Assert.Equal((10 + 2 * border, 10 + 2 * border), (w, h));
            byte Alpha(int x, int y) => bytes[(y * w + x) * 4 + 3];
            var mid = h / 2;
            // Inside: the fill; then the outline, five pixels wide; then the margin, empty.
            Assert.Equal(25, Alpha(border + 5, mid));
            for (var x = border - ItemIconRaster.DroppedOutline; x < border; x++)
                Assert.Equal(153, Alpha(x, mid));
            Assert.Equal(0, Alpha(ItemIconRaster.Margin - 1, mid));
            Assert.Equal(new byte[] { 133, 212, 239 }, bytes.Skip((mid * w + border) * 4).Take(3).ToArray());
        }

        [Fact]
        public void The_srgb_transfer_round_trips()
        {
            for (var i = 0; i <= 255; i += 17)
                Assert.Equal(i / 255f, ItemIconRaster.ToEncoded(ItemIconRaster.ToLinear(i / 255f)), 4);
        }
    }
}
