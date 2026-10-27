using System;
using System.Threading;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace RealisticStackEffects
{
 internal sealed class PlumeVolume
    {
        /// <summary>The texture's cells a side; the grid has at most as many.</summary>
        public const int Cells = 48;

        private const float UpdateEvery = 0.1f;

        // The grid's cells are no smaller than this, m, and it has this many
        // cells' margin round the particles (for the smoothing and the
        // outward way at its edge).
        private const float SmallestCell = 0.75f;
        private const int Margin = 2;

        // A particle's smoke is a ball this share of its quad's width (the
        // wisps fill the middle of their quads), its opacity through its
        // middle at most this.
        private const float BallShare = 0.8f;
        private const float MostOpacity = 0.95f;

        // Where the particles will be when the texture shows: this far along
        // their velocity, s (gathered, worked out, then blended in).
        private const float Lead = 0.15f;

        private struct Ball
        {
            public Vector3 Position;
            public float Radius;
            public float Depth;
        }

        // The particles' simulation space (null: the world's).
        private readonly Transform space;

        private Ball[] gathered = new Ball[512];
        private int gatheredCount;
        private float nextUpdate;

        /// <summary>Whether this frame's particles are wanted (<see cref="Gather"/>).</summary>
        public bool Gathering { get; private set; }

        // The worker's: its particles and sun, its grids, and its result.
        private Ball[] working = new Ball[512];
        private int workingCount;
        private Vector3 workingSun;
        private float workingDensity;
        private volatile bool busy;
        private volatile bool finished;
        private string failed;
        private readonly byte[] texels = new byte[Cells * Cells * Cells * 4];
        private float[] density = new float[0];
        private float[] scratch = new float[0];
        private float[] light = new float[0];
        private Vector3 resultLow;
        private float resultCell;

        // The textures shown: the newest, blended in over the one before.
        private Texture3D shown;
        private Texture3D before;
        private Matrix4x4 shownMap;
        private Matrix4x4 beforeMap;
        private float shownAt = float.NegativeInfinity;

        /// <summary>The share of the plume's particles the sun's light reached under half of, at the last update.</summary>
        public float ShadedShare { get; private set; }

        public PlumeVolume(Transform space)
        {
            this.space = space;
        }

        /// <summary>Starts a frame: whether its particles are wanted.</summary>
        public void Begin(float strength)
        {
            Gathering = strength > 0f && !busy && !finished && Time.time >= nextUpdate;
            gatheredCount = 0;
        }

        /// <summary>Adds a system's particles, its first <paramref name="count"/> in <paramref name="particles"/>.</summary>
        public void Gather(ParticleSystem system, ParticleSystem.Particle[] particles, int count)
        {
            if (!Gathering)
                return;
            var from = SpaceOf(system);
            var convert = from != space;
            if (gathered.Length < gatheredCount + count)
                Array.Resize(ref gathered, Mathf.NextPowerOfTwo(gatheredCount + count));
            for (var i = 0; i < count; i++)
            {
                var particle = particles[i];
                var opacity = ((Color)particle.startColor).a;
                if (opacity < 0.01f || particle.remainingLifetime <= 0f)
                    continue;
                var position = particle.position + particle.velocity * Lead;
                if (convert)
                {
                    var world = from != null ? from.TransformPoint(position) : position;
                    position = space != null ? space.InverseTransformPoint(world) : world;
                }
                gathered[gatheredCount++] = new Ball
                {
                    Position = position,
                    Radius = 0.5f * BallShare * particle.startSize,
                    Depth = -Mathf.Log(1f - Mathf.Min(opacity, MostOpacity)),
                };
            }
        }

        /// <summary>
        /// Ends a frame: hands the gathered particles to the worker, shows a
        /// finished texture once the last has blended in, and sets the
        /// materials' volume (<paramref name="toSun"/> the world's way to the
        /// sun, <paramref name="shadowDensity"/> how dark its shadow is).
        /// </summary>
        public void End(Vector3 toSun, float strength, float shadowDensity, Material[] materials)
        {
            if (failed != null)
            {
                StackHost.Mod.Logger.Warning($"Plume volume: {failed}");
                failed = null;
            }
            if (Gathering && gatheredCount > 0 && toSun.sqrMagnitude > 0.5f)
            {
                var swap = working;
                working = gathered;
                gathered = swap;
                workingCount = gatheredCount;
                workingSun = (space != null ? space.InverseTransformDirection(toSun) : toSun).normalized;
                workingDensity = shadowDensity;
                busy = true;
                nextUpdate = Time.time + UpdateEvery;
                ThreadPool.QueueUserWorkItem(_ => Work());
            }
            Gathering = false;

            if (finished && Time.time - shownAt >= UpdateEvery)
                Show();

            var on = shown != null && strength > 0f;
            var worldToSpace = space != null ? space.worldToLocalMatrix : Matrix4x4.identity;
            var spaceToWorld = space != null ? space.localToWorldMatrix : Matrix4x4.identity;
            var blend = Mathf.Clamp01((Time.time - shownAt) / UpdateEvery);
            foreach (var material in materials)
            {
                if (material == null || !material.HasProperty(VolumeId))
                    continue;
                material.SetFloat(VolumeId, on ? strength : 0f);
                if (!on)
                    continue;
                material.SetTexture(VolumeTexId, shown);
                material.SetTexture(VolumePrevId, before);
                material.SetMatrix(VolumeMapId, shownMap * worldToSpace);
                material.SetMatrix(VolumePrevMapId, beforeMap * worldToSpace);
                material.SetMatrix(VolumeToWorldId, spaceToWorld);
                material.SetFloat(VolumeBlendId, blend);
            }
        }

        public void Destroy()
        {
            if (shown != null)
                UnityEngine.Object.Destroy(shown);
            if (before != null && before != shown)
                UnityEngine.Object.Destroy(before);
            shown = before = null;
        }

        private static readonly int VolumeId = Shader.PropertyToID("_Volume");
        private static readonly int VolumeTexId = Shader.PropertyToID("_VolumeTex");
        private static readonly int VolumePrevId = Shader.PropertyToID("_VolumePrev");
        private static readonly int VolumeMapId = Shader.PropertyToID("_VolumeMap");
        private static readonly int VolumePrevMapId = Shader.PropertyToID("_VolumePrevMap");
        private static readonly int VolumeToWorldId = Shader.PropertyToID("_VolumeToWorld");
        private static readonly int VolumeBlendId = Shader.PropertyToID("_VolumeBlend");

        // The worker's texture into the one no longer shown, then shown.
        private void Show()
        {
            var target = before != null && before != shown ? before : Create();
            target.SetPixelData(texels, 0);
            target.Apply(false);
            // A grid point's texture coordinate: its cell's middle.
            var map = Matrix4x4.Translate(Vector3.one * (0.5f / Cells))
                      * Matrix4x4.Scale(Vector3.one / (resultCell * Cells))
                      * Matrix4x4.Translate(-resultLow);
            if (shown == null)
            {
                before = target;
                beforeMap = map;
            }
            else
            {
                before = shown;
                beforeMap = shownMap;
            }
            shown = target;
            shownMap = map;
            shownAt = Time.time;
            finished = false;
        }

        private static Texture3D Create() =>
            new Texture3D(Cells, Cells, Cells, GraphicsFormat.R8G8B8A8_UNorm, TextureCreationFlags.None)
            {
                name = "ChimneyPlumeVolume",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

        /// <summary>A system's simulation space (null: the world's).</summary>
        public static Transform SpaceOf(ParticleSystem system)
        {
            var main = system.main;
            switch (main.simulationSpace)
            {
                case ParticleSystemSimulationSpace.Local:
                    return system.transform;
                case ParticleSystemSimulationSpace.Custom:
                    return main.customSimulationSpace;
                default:
                    return null;
            }
        }

        private void Work()
        {
            try
            {
                Build();
            }
            catch (Exception e)
            {
                failed = e.Message;
            }
            finally
            {
                finished = true;
                busy = false;
            }
        }

        // The grid over the particles, its light and outward way, as texels.
        private void Build()
        {
            var low = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var high = -low;
            for (var i = 0; i < workingCount; i++)
            {
                var ball = working[i];
                var reach = Vector3.one * ball.Radius;
                low = Vector3.Min(low, ball.Position - reach);
                high = Vector3.Max(high, ball.Position + reach);
            }
            var extent = high - low;
            var cell = Mathf.Max(SmallestCell, Mathf.Max(extent.x, Mathf.Max(extent.y, extent.z)) / (Cells - 2 * Margin - 1));
            low -= Vector3.one * (Margin * cell);
            var nx = Mathf.Min(Cells, Mathf.CeilToInt(extent.x / cell) + 2 * Margin + 1);
            var ny = Mathf.Min(Cells, Mathf.CeilToInt(extent.y / cell) + 2 * Margin + 1);
            var nz = Mathf.Min(Cells, Mathf.CeilToInt(extent.z / cell) + 2 * Margin + 1);
            var count = nx * ny * nz;
            if (density.Length < count)
            {
                density = new float[count];
                scratch = new float[count];
                light = new float[count];
            }
            Array.Clear(density, 0, count);

            // Each ball's smoke over the grid points within it, its depth
            // through the middle spread over its volume (per m³).
            var perVolume = 1f / (cell * cell * cell);
            for (var i = 0; i < workingCount; i++)
                Splat(working[i], (working[i].Position - low) / cell, cell, perVolume, nx, ny, nz);
            Smooth(nx, ny, nz);

            // The sun's light from its side: each point's is the light a step
            // toward the sun (its largest axis a whole cell) less the smoke
            // between, the points taken from the sun's side so each step's is
            // known before.
            var sun = workingSun;
            var largest = Mathf.Max(Mathf.Abs(sun.x), Mathf.Max(Mathf.Abs(sun.y), Mathf.Abs(sun.z)));
            var step = sun / largest;
            var length = step.magnitude * cell * workingDensity;
            var (ax, ay, az) = (Order(step.x, nx), Order(step.y, ny), Order(step.z, nz));
            for (var oz = 0; oz < nz; oz++)
            for (var oy = 0; oy < ny; oy++)
            for (var ox = 0; ox < nx; ox++)
            {
                int x = ax(ox), y = ay(oy), z = az(oz);
                var at = new Vector3(x + step.x, y + step.y, z + step.z);
                var here = density[(z * ny + y) * nx + x];
                var depth = 0.5f * (here + Sample(density, at, nx, ny, nz, 0f)) * length;
                light[(z * ny + y) * nx + x] = Sample(light, at, nx, ny, nz, 1f) * Mathf.Exp(-depth);
            }

            // The plume's outward way, against how its smoke thickens, and how
            // sure it is (sharpest at the plume's edge); the shade from the sun.
            var thickest = 1e-6f;
            for (var i = 0; i < count; i++)
                thickest = Mathf.Max(thickest, density[i]);
            for (var i = 0; i < texels.Length; i += 4)
            {
                texels[i] = texels[i + 1] = texels[i + 2] = 128;
                texels[i + 3] = 0;
            }
            var shaded = 0;
            var smoky = 0;
            for (var z = 0; z < nz; z++)
            for (var y = 0; y < ny; y++)
            for (var x = 0; x < nx; x++)
            {
                var i = (z * ny + y) * nx + x;
                var gradient = new Vector3(
                    density[i + (x < nx - 1 ? 1 : 0)] - density[i - (x > 0 ? 1 : 0)],
                    density[i + (y < ny - 1 ? nx : 0)] - density[i - (y > 0 ? nx : 0)],
                    density[i + (z < nz - 1 ? nx * ny : 0)] - density[i - (z > 0 ? nx * ny : 0)]) * 0.5f;
                var size = gradient.magnitude;
                var outward = size > 1e-9f ? -gradient / size * Mathf.Clamp01(size / (density[i] + 0.05f * thickest)) : Vector3.zero;
                var shade = 1f - light[i];
                var texel = ((z * Cells + y) * Cells + x) * 4;
                texels[texel] = Byte(outward.x * 0.5f + 0.5f);
                texels[texel + 1] = Byte(outward.y * 0.5f + 0.5f);
                texels[texel + 2] = Byte(outward.z * 0.5f + 0.5f);
                texels[texel + 3] = Byte(shade);
                if (density[i] > 0.05f * thickest)
                {
                    smoky++;
                    if (shade > 0.5f)
                        shaded++;
                }
            }
            ShadedShare = smoky > 0 ? (float)shaded / smoky : 0f;
            resultLow = low;
            resultCell = cell;
        }

        // The grid's points along an axis in the order the sun's light reaches them.
        private static Func<int, int> Order(float toSun, int n) => toSun > 0f ? (Func<int, int>)(o => n - 1 - o) : o => o;

        private void Splat(Ball ball, Vector3 at, float cell, float perVolume, int nx, int ny, int nz)
        {
            var smoke = ball.Depth * (2f * Mathf.PI / 3f) * ball.Radius * ball.Radius * perVolume;
            var reach = ball.Radius / cell;
            if (reach < 1f)
            {
                // Smaller than a cell: shared among the eight round it.
                var x0 = Mathf.Clamp(Mathf.FloorToInt(at.x), 0, nx - 2);
                var y0 = Mathf.Clamp(Mathf.FloorToInt(at.y), 0, ny - 2);
                var z0 = Mathf.Clamp(Mathf.FloorToInt(at.z), 0, nz - 2);
                var fx = Mathf.Clamp01(at.x - x0);
                var fy = Mathf.Clamp01(at.y - y0);
                var fz = Mathf.Clamp01(at.z - z0);
                for (var dz = 0; dz < 2; dz++)
                for (var dy = 0; dy < 2; dy++)
                for (var dx = 0; dx < 2; dx++)
                    density[((z0 + dz) * ny + y0 + dy) * nx + x0 + dx] +=
                        smoke * (dx == 0 ? 1f - fx : fx) * (dy == 0 ? 1f - fy : fy) * (dz == 0 ? 1f - fz : fz);
                return;
            }
            // Over the points within it, most in its middle.
            int xa = Mathf.Max(0, Mathf.CeilToInt(at.x - reach)), xb = Mathf.Min(nx - 1, Mathf.FloorToInt(at.x + reach));
            int ya = Mathf.Max(0, Mathf.CeilToInt(at.y - reach)), yb = Mathf.Min(ny - 1, Mathf.FloorToInt(at.y + reach));
            int za = Mathf.Max(0, Mathf.CeilToInt(at.z - reach)), zb = Mathf.Min(nz - 1, Mathf.FloorToInt(at.z + reach));
            var within = 1f / (reach * reach);
            var total = 0f;
            for (var pass = 0; pass < 2; pass++)
            {
                var share = pass == 0 ? 0f : smoke / Mathf.Max(total, 1e-6f);
                for (var z = za; z <= zb; z++)
                for (var y = ya; y <= yb; y++)
                for (var x = xa; x <= xb; x++)
                {
                    var d = ((x - at.x) * (x - at.x) + (y - at.y) * (y - at.y) + (z - at.z) * (z - at.z)) * within;
                    if (d >= 1f)
                        continue;
                    var weight = (1f - d) * (1f - d);
                    if (pass == 0)
                        total += weight;
                    else
                        density[(z * ny + y) * nx + x] += share * weight;
                }
            }
        }

        // A [1 2 1] smoothing along each axis.
        private void Smooth(int nx, int ny, int nz)
        {
            SmoothAlong(density, scratch, nx, ny, nz, 1, nx);
            SmoothAlong(scratch, density, nx, ny, nz, nx, ny);
            SmoothAlong(density, scratch, nx, ny, nz, nx * ny, nz);
            Array.Copy(scratch, density, nx * ny * nz);
        }

        private static void SmoothAlong(float[] from, float[] to, int nx, int ny, int nz, int stride, int n)
        {
            var count = nx * ny * nz;
            for (var i = 0; i < count; i++)
            {
                var along = i / stride % n;
                var before = along > 0 ? from[i - stride] : 0f;
                var after = along < n - 1 ? from[i + stride] : 0f;
                to[i] = 0.25f * before + 0.5f * from[i] + 0.25f * after;
            }
        }

        // A grid's value at a point, from the eight round it; outside the grid, <paramref name="outside"/>.
        private static float Sample(float[] grid, Vector3 at, int nx, int ny, int nz, float outside)
        {
            var x0 = Mathf.FloorToInt(at.x);
            var y0 = Mathf.FloorToInt(at.y);
            var z0 = Mathf.FloorToInt(at.z);
            float fx = at.x - x0, fy = at.y - y0, fz = at.z - z0;
            var sum = 0f;
            for (var dz = 0; dz < 2; dz++)
            for (var dy = 0; dy < 2; dy++)
            for (var dx = 0; dx < 2; dx++)
            {
                var weight = (dx == 0 ? 1f - fx : fx) * (dy == 0 ? 1f - fy : fy) * (dz == 0 ? 1f - fz : fz);
                if (weight <= 0f)
                    continue;
                int x = x0 + dx, y = y0 + dy, z = z0 + dz;
                var value = x < 0 || y < 0 || z < 0 || x >= nx || y >= ny || z >= nz ? outside : grid[(z * ny + y) * nx + x];
                sum += weight * value;
            }
            return sum;
        }

        private static byte Byte(float value) => (byte)Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255);
    }
}
