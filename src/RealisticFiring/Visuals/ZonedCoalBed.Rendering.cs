using RealisticFiring.Core;
using UnityEngine;

namespace RealisticFiring.Visuals
{
    internal sealed partial class ZonedCoalBed
    {
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int AimRectId = Shader.PropertyToID("_AimRect");
        private static readonly int AimColorId = Shader.PropertyToID("_AimColor");
        private static readonly int FlickerSpeedId = Shader.PropertyToID("_FlickerSpeed");
        private static readonly int TilingId = Shader.PropertyToID("_Tiling");
        private static readonly int PulseId = Shader.PropertyToID("_Pulse");
        private static readonly int GlowStrengthId = Shader.PropertyToID("_GlowStrength");
        private static readonly int NoiseId = Shader.PropertyToID("_Noise");
        private static readonly int LumpZonesId = Shader.PropertyToID("_LumpZones");
        private static readonly int BedReliefId = Shader.PropertyToID("_BedRelief");
        private static readonly int BedMinId = Shader.PropertyToID("_BedMin");
        private static readonly int BedMaxId = Shader.PropertyToID("_BedMax");
        private static readonly int DoorSlopeId = Shader.PropertyToID("_DoorSlope");
        private static readonly int UnderLumpsId = Shader.PropertyToID("_UnderLumps");
        private static readonly int UnderDropId = Shader.PropertyToID("_UnderDrop");
        private static readonly int UnderFloorId = Shader.PropertyToID("_UnderFloor");
        private static readonly Color AimColor = new Color(0.25f, 0.6f, 1f);

        // Each pile covers this much more than its zone, so neighbors blend
        // instead of leaving a trough where the stock pile's edges slope down.
        private const float Overlap = 1.3f;

        // The continuous bed's grid, and its coal lumps' relief as a share of
        // the coal's depth there.
        private const int GridAcross = 16;
        private const int GridAlong = 32;
        // Small: the lumps on it (CoalLumps) give the bed its shape; its own
        // relief at the grid's coarse spacing drew big facets up close.
        private const float Relief = 0.06f;

        // The glowing embers lie this far under the dark lumps on top, in m.
        private const float EmberDepth = 0.08f;

        // The embers stay at least this far under the coal layer, in m.
        private const float EmberUnder = 0.01f;

        // The coal texture's repeats across the bed's width: about 5 cm lumps
        // on a metre-wide grate, with smaller pieces between.
        private const float LumpsAcross = 2f;

        // Lit coal (tiles): dull red when burning slowly, orange when working, yellow-white when forced.
        private static readonly Color DullRed = new Color(1f, 0.35f, 0.2f);
        private static readonly Color Orange = new Color(1f, 0.6f, 0.3f);
        private static readonly Color WhiteHot = new Color(1f, 0.88f, 0.7f);

        // Under the lumps (SetLumpSurface) the shaders stand the coal and
        // the embers on the lumps' surface, with its heat, green, ash, and
        // slope: the mesh holds only the clinker and the bare grate for
        // them, which change as slowly as the fire burns. Rebuilt every
        // frame, it cost about 0.65 ms while the fire was looked at (audit
        // V4, 2026-11-09); now a few times a second.
        private const float UnderLumpsInterval = 0.25f;
        private bool lumpSurface;
        private float nextUnderLumpsAt;

        /// <summary>
        /// Makes the continuous coal and ember layers read the lumps' surface
        /// and heat, keeping both below it instead of crossing it where their
        /// CPU height interpolation and heat jitter differ from the shader's.
        /// </summary>
        internal void SetLumpSurface(Texture2D zones, float depth, float upPerUnit, Texture noise)
        {
            var units = 1f / Mathf.Max(1e-5f, upPerUnit);
            var floor = bed.min.y - EmberUnder * units;
            SetLumpSurface(bedRenderer, properties, zones, depth * units, floor, noise);
            SetLumpSurface(emberRenderer, emberProperties, zones, Mathf.Max(EmberDepth, depth + EmberUnder) * units, floor, noise);
            lumpSurface = true;
            // The shader can lift these beyond the CPU mesh at a heaped or
            // overfilled zone. Keep its displaced surface inside the bounds.
            if (mesh != null)
                mesh.bounds = LumpSurfaceBounds;
            if (emberMesh != null)
                emberMesh.bounds = LumpSurfaceBounds;
        }

        internal Bounds LumpSurfaceBounds
        {
            get
            {
                var reach = new Bounds(bed.center, bed.size);
                reach.Expand(new Vector3(0f, bed.size.y * 4f, 0f));
                return reach;
            }
        }

        // With the lumps' noise, in which the layers under them read where
        // fresh coal lies in patches (CoalSurface.cginc).
        private void SetLumpSurface(Renderer renderer, MaterialPropertyBlock block, Texture2D zones, float depth, float floor, Texture noise)
        {
            if (renderer == null || block == null)
                return;
            renderer.GetPropertyBlock(block);
            block.SetTexture(LumpZonesId, zones);
            if (noise != null)
                block.SetTexture(NoiseId, noise);
            block.SetTexture(BedReliefId, reliefTexture);
            block.SetVector(BedMinId, bed.min);
            block.SetVector(BedMaxId, bed.max);
            block.SetFloat(DoorSlopeId, DoorSlope);
            block.SetFloat(UnderLumpsId, 1f);
            block.SetFloat(UnderDropId, depth);
            block.SetFloat(UnderFloorId, floor);
            renderer.SetPropertyBlock(block);
        }

        private void UpdateMesh(FireModel fire, GrateZone? aimed, float pulse)
        {
            if (!lumpSurface || Time.unscaledTime >= nextUnderLumpsAt)
            {
                UpdateGeometry();
                nextUnderLumpsAt = Time.unscaledTime + UnderLumpsInterval;
            }
            UpdateProperties(aimed, pulse);
        }

        // The bed's vertices: where the coal stands and its state there.
        private void UpdateGeometry()
        {
            for (var j = 0; j <= GridAlong; j++)
            {
                var along = (float)j / GridAlong;
                for (var i = 0; i <= GridAcross; i++)
                {
                    var across = (float)i / GridAcross;
                    var index = j * (GridAcross + 1) + i;
                    var height = SurfaceHeight(across, along);
                    var lump = lumps[index] * Relief * Mathf.Min(height, 0.6f);
                    var up = Mathf.Max(0f, height + lump);
                    vertices[index] = new Vector3(
                        Mathf.Lerp(bed.min.x, bed.max.x, across),
                        Mathf.Lerp(bed.min.y, bed.max.y, up),
                        Mathf.Lerp(bed.min.z, bed.max.z, along));
                    var glow = Sample(heat, across, along) * (0.85f + 0.3f * glowJitter[index]);
                    // A fresh heap lies black as its zone's green coal does.
                    var forced = Sample(forcing, across, along);
                    var heapCover = Grid(heapFresh, across, along) * (1f - (1f - ForcedGreenShows) * forced * forced);
                    colors[index] = new Color(Mathf.Clamp01(glow), Mathf.Max(Sample(cover, across, along), heapCover), Sample(ash, across, along), Sample(clinker, across, along));
                    bare[index] = new Vector2(Sample(empty, across, along), 0f);
                }
            }
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.SetUVs(3, bare);
            // Under the lumps the shader gives the slope, and the bounds
            // stay the lumps' reach (LumpSurfaceBounds), past these heights.
            if (!lumpSurface)
            {
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
            }
            else
                mesh.bounds = LumpSurfaceBounds;
            surfaceReady = true;

            if (emberMesh != null)
            {
                // Always under the coal, even on an empty grate, so the cold coal's
                // surface covers them where both lie flat.
                var drop = EmberDepth / Mathf.Max(1e-4f, Mathf.Abs(fullScale.y));
                var floor = bed.min.y - EmberUnder / Mathf.Max(1e-4f, Mathf.Abs(fullScale.y));
                for (var v = 0; v < vertices.Length; v++)
                    emberVertices[v] = new Vector3(vertices[v].x, Mathf.Max(floor, vertices[v].y - drop), vertices[v].z);
                emberMesh.vertices = emberVertices;
                emberMesh.colors = colors;
                if (!lumpSurface)
                    emberMesh.RecalculateBounds();
                else
                    emberMesh.bounds = LumpSurfaceBounds;
            }
        }

        // The shaders' state for the bed that changes frame to frame: its
        // flicker, the beat, and the aimed zone's pulse.
        private void UpdateProperties(GrateZone? aimed, float pulse)
        {
            bedRenderer.GetPropertyBlock(properties);
            properties.SetFloat(TilingId, LumpsAcross);
            properties.SetFloat(FlickerSpeedId, 0.3f + 1.4f * fireForcing);
            var flames = GetComponent<FireboxFlames>();
            properties.SetFloat(PulseId, flames != null ? flames.Pulse : 0f);
            if (emberRenderer != null)
            {
                emberRenderer.GetPropertyBlock(emberProperties);
                emberProperties.SetFloat(TilingId, LumpsAcross);
                emberProperties.SetFloat(FlickerSpeedId, properties.GetFloat(FlickerSpeedId));
                emberProperties.SetFloat(PulseId, flames != null ? flames.Pulse : 0f);
                emberProperties.SetFloat(GlowStrengthId, FireboxLighting.EmberGlow);
                emberRenderer.SetPropertyBlock(emberProperties);
            }
            if (aimed is GrateZone zone)
            {
                var c = GrateLayout.Column(zone);
                var r = GrateLayout.Row(zone);
                properties.SetVector(AimRectId, new Vector4(
                    (float)c / GrateLayout.Columns, (float)r / GrateLayout.Rows,
                    (float)(c + 1) / GrateLayout.Columns, (float)(r + 1) / GrateLayout.Rows));
                properties.SetColor(AimColorId, new Color(AimColor.r, AimColor.g, AimColor.b, 1.5f * pulse));
            }
            else
            {
                properties.SetColor(AimColorId, new Color(0f, 0f, 0f, 0f));
            }
            bedRenderer.SetPropertyBlock(properties);
        }

        private void UpdateTiles(FireModel fire, GrateZone? aimed, float pulse)
        {
            for (var i = 0; i < GrateLayout.ZoneCount; i++)
            {
                var zone = fire.Zones[i];
                var isAimed = aimed == (GrateZone)i;
                var visible = zone.Coal >= EmptyZoneMass || isAimed;
                tileRenderers[i].enabled = visible;
                if (!visible)
                    continue;

                Place(i, Mathf.Clamp(zone.Coal / zoneShare, MinHeightScale, MaxHeightScale));

                var glow = heat[i] * (1f - 0.85f * cover[i]);
                var flicker = 0.85f + 0.15f * Mathf.PerlinNoise(Time.time * 1.7f, i * 3.1f);
                var emission = FireColor(glow) * (Mathf.Lerp(0.4f, 3.2f, glow) * glow * flicker);
                if (isAimed)
                    emission += AimColor * (1.5f * pulse);
                tileMaterials[i].SetColor(EmissionColorId, emission);
            }
        }

        private static Color FireColor(float glow) =>
            glow < 0.5f ? Color.Lerp(DullRed, Orange, glow * 2f) : Color.Lerp(Orange, WhiteHot, glow * 2f - 1f);

        private void Place(int index, float height)
        {
            var zone = (GrateZone)index;
            var across = Overlap / GrateLayout.Columns;
            var along = Overlap / GrateLayout.Rows;
            var x = ZoneCenter(bed.min.x, bed.max.x, GrateLayout.Column(zone), GrateLayout.Columns, across);
            var z = ZoneCenter(bed.min.z, bed.max.z, GrateLayout.Row(zone), GrateLayout.Rows, along);
            var offset = new Vector3(
                x - across * bed.center.x,
                bed.min.y * (1f - height),
                z - along * bed.center.z);
            tiles[index].localPosition = rest + frame * Vector3.Scale(fullScale, offset);
            tiles[index].localScale = ToPivot(new Vector3(fullScale.x * across, fullScale.y * height, fullScale.z * along));
        }

        private static float ZoneCenter(float min, float max, int index, int count, float scale)
        {
            var center = min + (index + 0.5f) * (max - min) / count;
            var halfPile = scale * (max - min) / 2f;
            return Mathf.Clamp(center, min + halfPile, max - halfPile);
        }
    }
}
