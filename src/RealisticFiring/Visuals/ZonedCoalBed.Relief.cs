using System.Collections.Generic;
using RealisticFiring.Core;
using UnityEngine;

namespace RealisticFiring.Visuals
{
    internal sealed partial class ZonedCoalBed
    {
        internal static float Lumpiness = 0.3f;
        internal static float LumpSize = 0.35f;
        internal static float LumpChange = 0.003f;
        internal static float HeapShare = 0.6f;
        internal static float HeapSize = 0.1f;
        internal static float HeapSlumped = 0.22f;
        internal static float HeapSlump = 45f;
        internal static float CoalBulk = 800f;
        internal static float HeapTallest = 0.08f;

        private const int ReliefAcross = 24;
        private const int ReliefAlong = 48;
        private const int MaxHeaps = 24;
        private const float HeapLeast = 0.3f;
        private const float ReliefEvery = 0.1f;

        // The relief: its swells (a share of the coal's thickness), its heaps
        // (a share of the stock pile's height), and how fresh the heaps' coal
        // is, on a grid across and along the bed; the heaps; and each zone's
        // fresh and all its coal as last seen, kg.
        private readonly float[] swells = new float[ReliefAcross * ReliefAlong];
        private readonly float[] heaped = new float[ReliefAcross * ReliefAlong];
        private readonly float[] heapFresh = new float[ReliefAcross * ReliefAlong];
        private readonly Color[] reliefPixels = new Color[ReliefAcross * ReliefAlong];
        private readonly List<Heap> heaps = new List<Heap>();
        private readonly float[] lastFresh = new float[GrateLayout.ZoneCount];
        private readonly float[] lastCoal = new float[GrateLayout.ZoneCount];
        private bool reliefStarted;
        private float reliefDue;
        private float reliefClock;
        private float bedWidth = 1f;
        private float bedLength = 1.5f;
        private float pileHeight = 0.3f;
        private Texture2D reliefTexture;
        private readonly System.Random heapRandom = new System.Random(588030);
        private readonly HeapTerms[] heapTerms = new HeapTerms[MaxHeaps];
        private float heapMost;

        private sealed class Heap
        {
            public int Zone;
            public float Across;
            public float Along;
            public float Mass;
            public float Fresh;
            public float Age;
        }

        // Each heap's terms for the relief, the same for every cell of it:
        // worked out in each cell, they were 27,648 spreads an update at the
        // most heaps (performance review, 2026-10-25).
        private struct HeapTerms
        {
            public float Across;
            public float Along;
            // 1 over twice its spread squared, 1/m².
            public float Narrowing;
            // Its height at its middle, m.
            public float Height;
            public float FreshShare;
        }

        /// <summary>
        /// The bed's relief for shaders, across and along the bed: R its
        /// swells, a share of the coal's thickness; G its heaps, a share of
        /// the stock pile's height; B how fresh the heaps' coal is, 0 to 1.
        /// A surface is (smooth height × (1 + R) + G), tapered at the door.
        /// </summary>
        internal Texture2D ReliefTexture => reliefTexture;

        /// <summary>
        /// The most the relief adds to the coal's surface: its swells, as a
        /// share of the coal's depth, and its tallest heap, as a share of the
        /// pile's height (for the fire volume's ceiling).
        /// </summary>
        internal float ReliefSwell => Lumpiness;
        internal float ReliefHeap => heapMost;

        // The relief: a heap for each shovelful that has landed, the heaps
        // slumping and burning down, and the field remade a few times a second.
        private void UpdateRelief(FireModel fire, bool seen)
        {
            var dt = Time.deltaTime;
            for (var i = 0; i < GrateLayout.ZoneCount; i++)
            {
                var zone = fire.Zones[i];
                if (reliefStarted && zone.Fresh - lastFresh[i] > HeapLeast)
                    AddHeap(i, zone.Fresh - lastFresh[i]);
                // A heap burns down with its zone and its coal catches as the
                // zone's fresh coal does.
                var burned = reliefStarted && lastCoal[i] > 0f && zone.Coal < lastCoal[i] ? zone.Coal / lastCoal[i] : 1f;
                var caught = reliefStarted && lastFresh[i] > 0f && zone.Fresh < lastFresh[i] ? zone.Fresh / lastFresh[i] : 1f;
                foreach (var heap in heaps)
                {
                    if (heap.Zone != i)
                        continue;
                    heap.Mass *= burned;
                    heap.Fresh = Mathf.Min(heap.Fresh * caught, heap.Mass);
                }
                lastFresh[i] = zone.Fresh;
                lastCoal[i] = zone.Coal;
            }
            if (!reliefStarted)
            {
                reliefStarted = true;
                var width = BedPoint(1f, 0.5f, 0f) - BedPoint(0f, 0.5f, 0f);
                var length = BedPoint(0.5f, 1f, 0f) - BedPoint(0.5f, 0f, 0f);
                bedWidth = Mathf.Max(0.3f, width.magnitude);
                bedLength = Mathf.Max(0.3f, length.magnitude);
                pileHeight = Mathf.Max(0.05f, (BedPoint(0.5f, 0.5f, 1f) - BedPoint(0.5f, 0.5f, 0f)).magnitude);
            }
            heaps.RemoveAll(heap => heap.Mass < 0.1f);
            foreach (var heap in heaps)
                heap.Age += dt;
            reliefClock += dt;
            reliefDue -= dt;
            // Unseen, the field waits, to be remade as soon as it is seen.
            if (!seen)
            {
                reliefDue = 0f;
                return;
            }
            if (reliefDue > 0f && reliefTexture != null)
                return;
            reliefDue = ReliefEvery;
            MakeRelief();
        }

        // A shovelful's heap where it landed: on the aim in its zone, toward
        // it in a neighbor it spilled into, or anywhere in a zone fired
        // without one (the stoker, the test bed).
        private void AddHeap(int zone, float mass)
        {
            var column = GrateLayout.Column((GrateZone)zone);
            var row = GrateLayout.Row((GrateZone)zone);
            var across = (column + 0.5f) / GrateLayout.Columns;
            var along = (row + 0.5f) / GrateLayout.Rows;
            var aim = firebox.LastDeliveryAim;
            float Jitter(float reach) => ((float)heapRandom.NextDouble() * 2f - 1f) * reach;
            if (aim.HasValue && Time.time - aim.Value.Time < 3f && (int)aim.Value.Zone == zone)
            {
                // A shovelful scatters as it lands.
                across = aim.Value.Across + Jitter(0.08f);
                along = aim.Value.Depth + Jitter(0.08f);
            }
            else if (aim.HasValue && Time.time - aim.Value.Time < 3f)
            {
                across = Mathf.Lerp(across, aim.Value.Across, 0.4f);
                along = Mathf.Lerp(along, aim.Value.Depth, 0.4f);
            }
            else
            {
                across += Jitter(0.3f / GrateLayout.Columns);
                along += Jitter(0.3f / GrateLayout.Rows);
            }
            if (heaps.Count >= MaxHeaps)
                heaps.RemoveAt(0);
            heaps.Add(new Heap { Zone = zone, Across = Mathf.Clamp01(across), Along = Mathf.Clamp01(along), Mass = mass, Fresh = mass });
        }

        private void MakeRelief()
        {
            var big = Mathf.Max(0.05f, LumpSize);
            var tallest = Mathf.Max(0.01f, HeapTallest);
            var drift = reliefClock * LumpChange;
            var heapCount = Mathf.Min(heaps.Count, heapTerms.Length);
            for (var h = 0; h < heapCount; h++)
            {
                var heap = heaps[h];
                var spread = Mathf.Lerp(HeapSize, HeapSlumped, 1f - Mathf.Exp(-heap.Age / Mathf.Max(1f, HeapSlump)));
                heapTerms[h] = new HeapTerms
                {
                    Across = heap.Across,
                    Along = heap.Along,
                    Narrowing = 1f / (2f * spread * spread),
                    Height = HeapShare * heap.Mass / Mathf.Max(100f, CoalBulk) / (2f * Mathf.PI * spread * spread),
                    FreshShare = heap.Fresh / Mathf.Max(heap.Mass, 1e-3f),
                };
            }
            heapMost = 0f;
            for (var j = 0; j < ReliefAlong; j++)
            {
                var along = (j + 0.5f) / ReliefAlong;
                for (var i = 0; i < ReliefAcross; i++)
                {
                    var across = (i + 0.5f) / ReliefAcross;
                    var x = across * bedWidth;
                    var z = along * bedLength;
                    var noise = 0.65f * Mathf.PerlinNoise(x / big + 13.7f, z / big + drift)
                                + 0.35f * Mathf.PerlinNoise(x * 2f / big + 41.3f, z * 2f / big - drift);
                    var k = j * ReliefAcross + i;
                    swells[k] = Lumpiness * Mathf.Clamp(4f * (noise - 0.5f), -1f, 1f);
                    var up = 0f;
                    var fresh = 0f;
                    for (var h = 0; h < heapCount; h++)
                    {
                        var dx = (across - heapTerms[h].Across) * bedWidth;
                        var dz = (along - heapTerms[h].Along) * bedLength;
                        var d2 = (dx * dx + dz * dz) * heapTerms[h].Narrowing;
                        if (d2 > 9f)
                            continue;
                        var bump = Mathf.Exp(-d2);
                        up += heapTerms[h].Height * bump;
                        fresh = Mathf.Max(fresh, bump * heapTerms[h].FreshShare);
                    }
                    // m, softly no taller than the tallest, as a share of the pile's height.
                    up = tallest * (float)System.Math.Tanh(up / tallest) / pileHeight;
                    heaped[k] = up;
                    heapMost = Mathf.Max(heapMost, up);
                    heapFresh[k] = fresh;
                    reliefPixels[k] = new Color(swells[k], up, fresh, 0f);
                }
            }
            if (reliefTexture == null)
            {
                reliefTexture = new Texture2D(ReliefAcross, ReliefAlong, TextureFormat.RGBAHalf, false, true)
                {
                    name = "RealisticFiring bed relief",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
            }
            reliefTexture.SetPixels(reliefPixels);
            reliefTexture.Apply(false);
        }

        // A relief grid's value at (across, along), bilinear between its
        // cells' middles as a shader reads it.
        private static float Grid(float[] grid, float across, float along)
        {
            var x = Mathf.Clamp(across * ReliefAcross - 0.5f, 0f, ReliefAcross - 1f);
            var z = Mathf.Clamp(along * ReliefAlong - 0.5f, 0f, ReliefAlong - 1f);
            var i0 = Mathf.Min((int)x, ReliefAcross - 2);
            var j0 = Mathf.Min((int)z, ReliefAlong - 2);
            var fx = x - i0;
            var fz = z - j0;
            float V(int i, int j) => grid[j * ReliefAcross + i];
            return Mathf.Lerp(Mathf.Lerp(V(i0, j0), V(i0 + 1, j0), fx), Mathf.Lerp(V(i0, j0 + 1), V(i0 + 1, j0 + 1), fx), fz);
        }

        private void DisposeRelief()
        {
            if (reliefTexture != null)
                Destroy(reliefTexture);
        }
    }
}
