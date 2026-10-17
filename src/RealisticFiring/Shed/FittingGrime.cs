using UnityEngine;

namespace RealisticFiring.Shed
{
 internal static class FittingGrime
    {
        private const int Size = 128;

        /// <summary>A material for <paramref name="metal"/>, its texture made now.</summary>
        public static Material Material(Metal metal)
        {
            var material = new Material(Shader.Find("Standard")) { name = $"RealisticFiring fitting {metal.ToString().ToLowerInvariant()}" };
            var albedo = new Texture2D(Size, Size, TextureFormat.RGBA32, true, false) { name = material.name + " albedo", wrapMode = TextureWrapMode.Repeat };
            var gloss = new Texture2D(Size, Size, TextureFormat.RGBA32, true, true) { name = material.name + " metallic", wrapMode = TextureWrapMode.Repeat };
            albedo.filterMode = gloss.filterMode = FilterMode.Trilinear;
            albedo.anisoLevel = gloss.anisoLevel = 2;
            var colors = new Color[Size * Size];
            var surfaces = new Color[Size * Size];
            var meanMetal = 0f;
            var meanSmooth = 0f;
            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    var u = (float)x / Size;
                    var v = (float)y / Size;
                    var (color, metallic, smoothness) = Shade(metal, u, v);
                    colors[y * Size + x] = color;
                    surfaces[y * Size + x] = new Color(metallic, 0f, 0f, smoothness);
                    meanMetal += metallic;
                    meanSmooth += smoothness;
                }
            }
            albedo.SetPixels(colors);
            albedo.Apply(true, true);
            gloss.SetPixels(surfaces);
            gloss.Apply(true, true);
            material.color = Color.white;
            material.mainTexture = albedo;
            material.SetTexture("_MetallicGlossMap", gloss);
            material.EnableKeyword("_METALLICGLOSSMAP");
            material.SetFloat("_GlossMapScale", 1f);
            // Without the map's variant, the mean surface.
            material.SetFloat("_Metallic", meanMetal / colors.Length);
            material.SetFloat("_Glossiness", meanSmooth / colors.Length);
            return material;
        }

        private static (Color Color, float Metallic, float Smoothness) Shade(Metal metal, float u, float v)
        {
            switch (metal)
            {
                case Metal.Brass:
                {
                    var worn = Smooth(0.6f, 0.8f, Fbm(u, v, 5, 5, 3, 11));
                    var grime = Smooth(0.42f, 0.72f, Fbm(u, v, 6, 6, 4, 23));
                    var green = 0.35f * Smooth(0.7f, 0.86f, Fbm(u, v, 9, 9, 2, 37));
                    var soot = 0.3f * Fbm(u, v, 3, 3, 3, 41);
                    var color = Color.Lerp(new Color(0.42f, 0.31f, 0.15f), new Color(0.72f, 0.55f, 0.3f), worn);
                    color = Color.Lerp(color, new Color(0.15f, 0.11f, 0.065f), grime * (1f - 0.7f * worn));
                    color = Color.Lerp(color, new Color(0.27f, 0.35f, 0.26f), green);
                    color *= 1f - soot;
                    var dull = Mathf.Max(grime * (1f - worn), green);
                    return (color, Mathf.Lerp(0.85f, 0.35f, dull), Mathf.Lerp(Mathf.Lerp(0.4f, 0.62f, worn), 0.14f, dull));
                }
                case Metal.Steel:
                {
                    var soot = Smooth(0.42f, 0.7f, Fbm(u, v, 4, 4, 4, 53));
                    var rust = 0.6f * Smooth(0.64f, 0.8f, Fbm(u, v, 7, 7, 3, 61));
                    var streak = 0.35f * Smooth(0.55f, 0.8f, Fbm(u, v, 14, 2, 3, 67));
                    var color = Color.Lerp(new Color(0.26f, 0.25f, 0.235f), new Color(0.06f, 0.058f, 0.055f), Mathf.Max(soot, streak));
                    color = Color.Lerp(color, new Color(0.27f, 0.15f, 0.08f), rust);
                    var dull = Mathf.Max(Mathf.Max(soot, streak), rust);
                    return (color, Mathf.Lerp(0.65f, 0.2f, dull), Mathf.Lerp(0.4f, 0.12f, dull));
                }
                default:
                {
                    var soot = Smooth(0.45f, 0.7f, Fbm(u, v, 4, 4, 4, 71));
                    var rust = 0.6f * Smooth(0.66f, 0.8f, Fbm(u, v, 8, 8, 3, 79));
                    var ash = 0.35f * Smooth(0.55f, 0.8f, Fbm(u, v, 3, 3, 3, 83));
                    var streak = 0.4f * Smooth(0.55f, 0.8f, Fbm(u, v, 14, 2, 3, 89));
                    var color = Color.Lerp(new Color(0.085f, 0.08f, 0.075f), new Color(0.035f, 0.033f, 0.03f), Mathf.Max(soot, streak));
                    color = Color.Lerp(color, new Color(0.22f, 0.115f, 0.065f), rust);
                    color = Color.Lerp(color, new Color(0.32f, 0.3f, 0.27f), ash);
                    var dull = Mathf.Max(Mathf.Max(soot, rust), Mathf.Max(ash, streak));
                    return (color, Mathf.Lerp(0.45f, 0.15f, dull), Mathf.Lerp(0.35f, 0.1f, dull));
                }
            }
        }

        private static float Smooth(float from, float to, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, x));

        // Fractal value noise seamless over the texture: cells of
        // (periodX, periodY) at the coarsest, halved each octave.
        private static float Fbm(float u, float v, int periodX, int periodY, int octaves, int seed)
        {
            var sum = 0f;
            var weight = 0.5f;
            var total = 0f;
            for (var o = 0; o < octaves; o++)
            {
                var px = periodX << o;
                var py = periodY << o;
                sum += weight * Noise(u * px, v * py, px, py, seed + 101 * o);
                total += weight;
                weight *= 0.5f;
            }
            return sum / total;
        }

        private static float Noise(float x, float y, int periodX, int periodY, int seed)
        {
            var x0 = Mathf.FloorToInt(x);
            var y0 = Mathf.FloorToInt(y);
            var fx = x - x0;
            var fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            int Wrap(int i, int period) => ((i % period) + period) % period;
            float At(int i, int j) => Hash(Wrap(i, periodX), Wrap(j, periodY), seed);
            var a = Mathf.Lerp(At(x0, y0), At(x0 + 1, y0), fx);
            var b = Mathf.Lerp(At(x0, y0 + 1), At(x0 + 1, y0 + 1), fx);
            return Mathf.Lerp(a, b, fy);
        }

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                var h = (uint)(x * 374761393 + y * 668265263 + seed * 982451653);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }
    }
}
