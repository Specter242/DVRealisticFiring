using RealisticFiring.Core;
using UnityEngine;

namespace RealisticFiring.Visuals
{
    internal sealed partial class FireboxVolume
    {
        // A wood fire's flames at full strength against a coal zone's.
        private const float WoodStrength = 0.8f;
        private const int MostJets = 6;

        private readonly Color[] pixelsA = new Color[GrateLayout.ZoneCount];
        private readonly Color[] pixelsB = new Color[GrateLayout.ZoneCount];
        private readonly float[] strength = new float[GrateLayout.ZoneCount];
        private readonly float[] top = new float[GrateLayout.ZoneCount];
        // Each zone's gas over the fire, a share of a forced zone's burning rate.
        private readonly float[] gas = new float[GrateLayout.ZoneCount];

        private float jetBurnout;
        private float choked;
        private readonly Vector4[] jetPlaces = new Vector4[MostJets];
        private readonly Vector4[] jetWays = new Vector4[MostJets];
        private JetTubes jetTubes;
        private float jetsFull = -1f;

        // Each zone's flames and smoke from its coal: how hard and how tall its
        // flames burn, its smoke, its coal's surface; its heat and glow.
        private int zonesFrame = -1;

        // Also used by the lighting behind a shut door. Nine source samples
        // stay current without running the airflow or uploading any textures.
        internal void MeasureZones()
        {
            if (zonesFrame == Time.frameCount)
                return;
            zonesFrame = Time.frameCount;
            if (!firebox.Model.FireOn)
            {
                System.Array.Clear(strength, 0, strength.Length);
                System.Array.Clear(top, 0, top.Length);
                return;
            }
            var draft = flames.Draft;
            var source = firebox.Smoke;
            var length = Mathf.Lerp(LazyLength, 1f, draft);
            var forced = Mathf.Max(1e-5f, firebox.Model.ForcedZoneRate);
            for (var i = 0; i < GrateLayout.ZoneCount; i++)
            {
                var zone = (GrateZone)i;
                var heat = bed.ZoneHeat(zone);
                var burn = flames.ZoneFlame(zone);
                var green = bed.ZoneCover(zone);
                // The gas leaving the bed, burning over it, smothered as its coke's flame is.
                // (The gas the jets burn burns in their streams, short and
                // quick: counted whole, a smoky round lit by them as they
                // opened filled the view with soft flame: the council.)
                var share = firebox.Model.ZoneOverFireGas(zone) * (firebox.Model.OverFireBurnout - (1f - JetFlameShare) * firebox.Model.JetBurnout) / forced;
                var gas = Mathf.Pow(Mathf.Clamp01(share / GasFull), GasExponent) * flames.ZoneFlameShown(zone);
                strength[i] = Mathf.Clamp01(burn * (0.35f + 0.65f * heat) + GasStrength * gas);
                this.gas[i] = share;
                var smoke = GasSmoke * source.GasRate(zone) / FireboxSmoke.SmokePerZone + SmokeOutPerKg * source.Charge(zone);
                // Less of it where the jets burn it.
                smoke *= 1f - JetSmokeClear * jetBurnout;

                var x = (GrateLayout.Column(zone) + 0.5f) / GrateLayout.Columns;
                var z = (GrateLayout.Row(zone) + 0.5f) / GrateLayout.Rows;
                var surface = box.InverseTransformPoint(bed.BedPoint(x, z, bed.SmoothHeight(x, z))).y + 0.5f;
                // The old height spent a whole box height above an already
                // raised bed, saturating at the crown even at a light firing.
                var lazy = LazyTop * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, LazyDraft, draft))) * Mathf.Clamp01((heat - 0.25f) * 4f);
                top[i] = strength[i] < 0.01f ? 0f
                    : Mathf.Clamp01(0.05f + CokeFlame * burn + GasFlame * gas * length * (1f - JetShorten * firebox.Model.JetBurnout) + lazy) * Mathf.Max(0f, 1f - surface);
                // Its wood and rag fire, in the same flames (WoodFlame): from
                // the grate, over any coal laid on it, to the pile's flame top.
                var starter = firebox.Model.Starters[i];
                var wood = WoodFlame.Strength(starter);
                var flameHeat = heat;
                if (wood > 0f && boxSize.y > 0f)
                {
                    var grate = box.InverseTransformPoint(bed.BedPoint(x, z, 0f)).y + 0.5f;
                    var woodTop = Mathf.Clamp01((WoodFlame.Top(starter, wood) - (surface - grate) * boxSize.y) / boxSize.y);
                    strength[i] = Mathf.Max(strength[i], WoodStrength * wood);
                    top[i] = Mathf.Max(top[i], woodTop);
                    flameHeat = Mathf.Max(flameHeat, WoodFlame.ColorHeat * wood);
                }
                var glow = FireboxLighting.ZoneGlow(heat, green).maxColorComponent * FireboxLighting.BedLight;
                // The texture's rows run along the bed (the door first), its columns across.
                var texel = GrateLayout.Row(zone) * GrateLayout.Columns + GrateLayout.Column(zone);
                pixelsA[texel] = new Color(strength[i], top[i], Mathf.Min(smoke, 1.5f), surface);
                // (Its green coal's share as the bed's shaders show it: none
                // dark in a zone driven flat out, CoalSurface.cginc.)
                pixelsB[texel] = new Color(flameHeat, glow, gas, green * (1f - 0.9f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.93f, 1f, heat))));
            }
        }

        private void Shape(float draft)
        {
            MeasureZones();
            zonesA.SetPixels(pixelsA);
            zonesA.Apply(false);
            // The light filling the box, on average, for the smoke in it: the
            // flames' and the bed's glow (in a closed box the bed lights all
            // of it, not only what is over it).
            var filled = Color.black;
            for (var i = 0; i < GrateLayout.ZoneCount; i++)
            {
                var zone = (GrateZone)i;
                // (With a hard-driven fire's white hearts: the box's light whiter,
                // and its smoke paler, as the footage's wisps among working flames.)
                var flameHeat = 0.25f + 0.75f * pixelsB[GrateLayout.Row(zone) * GrateLayout.Columns + GrateLayout.Column(zone)].r + 0.5f * WhiteHeart * Mathf.InverseLerp(WhiteDraftLow, WhiteDraftFull, draft);
                filled += FireColors.At(flameHeat) * (strength[i] * top[i] * Glow)
                          + FireboxLighting.ZoneGlow(bed.ZoneHeat(zone), bed.ZoneCover(zone)) * (FireboxLighting.BedGlow * FireboxLighting.BedLight);
            }
            filled *= SmokeBoxLight / GrateLayout.ZoneCount;
            var smokeMean = 0f;
            for (var i = 0; i < GrateLayout.ZoneCount; i++)
                smokeMean += pixelsA[i].b / GrateLayout.ZoneCount;
            material.SetFloat(SmokeMeanId, smokeMean);
            material.SetVector(BoxLightId, new Vector4(filled.r, filled.g, filled.b, 0f));
            // The grate's height, where the coal slopes down to at the door.
            grateLevel = box.InverseTransformPoint(bed.BedPoint(0.5f, 0.5f, 0f)).y + 0.5f;
            material.SetFloat(GrateLevelId, grateLevel);
            // The bed's relief, added in the shader to the zones' smooth surface.
            var bedRise = box.InverseTransformPoint(bed.BedPoint(0.5f, 0.5f, 1f)).y + 0.5f - grateLevel;
            material.SetFloat(BedRiseId, bedRise);
            // How high in the box anything is drawn, for the march to stop
            // under: the coal's highest surface with its swells and heaps;
            // over it the tallest tongue, as the shader draws it (to 1.4 of
            // its zone's height by its hot spots and lobes, swung by a beat
            // and a puff, no taller than half the room, its ragged tip half
            // as far again), or the short flames in the gaps; and the smoke's
            // top. A drifting fire's march took every step up to the crown
            // for flames a hand high (33 ms a frame from inside the box).
            var surfaceMost = 0f;
            var surfaceLeast = 1f;
            var topMost = 0f;
            for (var i = 0; i < GrateLayout.ZoneCount; i++)
            {
                surfaceMost = Mathf.Max(surfaceMost, pixelsA[i].a);
                surfaceLeast = Mathf.Min(surfaceLeast, pixelsA[i].a);
                topMost = Mathf.Max(topMost, pixelsA[i].g);
            }
            var coalMost = grateLevel + Mathf.Max(0f, surfaceMost - grateLevel) * (1f + bed.ReliefSwell) + bed.ReliefHeap * bedRise;
            var tongue = Mathf.Min(1.4f * (1f + Mathf.Max(0f, BeatHeight)) * 1.55f * topMost, 0.5f * Mathf.Max(0f, 1f - Mathf.Min(surfaceLeast, grateLevel)));
            var licks = boxSize.y > 0f ? 1.8f * LickHeight / boxSize.y : 1f;
            var flameCeiling = coalMost + Mathf.Max(1.5f * tongue, licks) + 0.03f;
            material.SetFloat(CeilingId, Mathf.Clamp01(Mathf.Max(flameCeiling, smokeCeiling)));
            if (bed.ReliefTexture != null)
                material.SetTexture(BedReliefId, bed.ReliefTexture);
            zonesB.SetPixels(pixelsB);
            zonesB.Apply(false);
        }

        private void SetJets()
        {
            if (jetTubes == null)
                jetTubes = GetComponent<JetTubes>();
            var model = firebox.Model;
            var air = model.JetAirFlow;
            var count = 0;
            if (jetTubes != null && jetTubes.Shown && air > 0f && model.Jets != null && box != null)
            {
                if (jetsFull < 0f)
                    jetsFull = model.Jets.Count * OverFireJets.PerJet(model.Jets.Tube, 150f / 14.5038f).Air;
                var strength = Mathf.Clamp01(Mathf.Sqrt(air / Mathf.Max(jetsFull, 1e-4f)));
                var reach = Mathf.Max(0.05f, model.JetPenetration * boxSize.x);
                foreach (var (mouth, inward) in jetTubes.Mouths)
                {
                    if (count >= MostJets)
                        break;
                    var at = box.InverseTransformPoint(mouth) + 0.5f * Vector3.one;
                    var way = Vector3.Scale(box.InverseTransformDirection(inward), boxSize).normalized;
                    jetPlaces[count] = new Vector4(at.x, at.y, at.z, strength);
                    jetWays[count] = new Vector4(way.x, way.y, way.z, reach);
                    count++;
                }
                // Bent up with the gas, and on toward the tube plate.
                var bend = new Vector3(0f, 1f, 0.5f * alongSign).normalized;
                material.SetVector(JetBendId, new Vector4(bend.x, bend.y, bend.z, JetBendBy));
                material.SetVector(JetShapeId, new Vector4(JetCore * jetTubes.Radius, jetTubes.Radius, JetSpread, JetPush));
                material.SetVectorArray(JetsId, jetPlaces);
                material.SetVectorArray(JetWaysId, jetWays);
            }
            material.SetFloat(JetCountId, count);
            material.SetFloat(JetClearId, JetClear);
        }
    }
}
