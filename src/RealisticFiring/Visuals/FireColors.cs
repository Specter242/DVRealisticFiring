using UnityEngine;

namespace RealisticFiring.Visuals
{
 internal static class FireColors
    {
        private static readonly int DullId = Shader.PropertyToID("_FireRampDull");
        private static readonly int RedId = Shader.PropertyToID("_FireRampRed");
        private static readonly int OrangeId = Shader.PropertyToID("_FireRampOrange");
        private static readonly int CoreId = Shader.PropertyToID("_FireRampCore");
        private static readonly int HotId = Shader.PropertyToID("_FireRampHot");
        private static readonly int WhiteId = Shader.PropertyToID("_FireRampWhite");
        private static readonly int BedId = Shader.PropertyToID("_FireBed");
        private static readonly int WhiteHeatId = Shader.PropertyToID("_FireWhiteHeat");
        private static readonly int ExposureId = Shader.PropertyToID("_FireExposure");

        // The ramp's stops at a quarter, half, three quarters, and all of the
        // heat, as radiance; and the eye's exposure to it. To be set against
        // the footage on the test bed.
        internal static Color Dull = new Color(0.3f, 0.012f, 0f);
        internal static Color Red = new Color(1.2f, 0.1f, 0.002f);
        internal static Color Orange = new Color(3f, 0.33f, 0.012f);
        internal static Color Core = new Color(8f, 1.1f, 0.06f);
        // Only the hottest end turns white. Coal heat stops at 1, so the
        // white stop must be reachable there as well as in the flame cores.
        // Enough blue radiance survives the eye's minimum exposure to make
        // a fully forced fire glare white instead of stopping at yellow.
        internal static Color White = new Color(24f, 18f, 12f);
        // On the way there, yellow: a hotter body's green fills before its
        // blue does. Blended straight to white, whose blue is two thirds of
        // its green, the colors between were peach and pink.
        internal static Color Hot = new Color(16f, 6f, 1f);
        // Spread the yellow-to-white change over the upper quarter of heat.
        // A narrow end stop made cooling coal appear to lose its glare at once.
        internal static float WhiteHeat = 0.75f;
        internal static float Exposure = 0.8f;

        // The fire between and under the lumps at the slowest burn, as heat
        // on the ramp (it runs to 1 with the zone's heat): the inside of a
        // burning bed is far hotter than its top, however slowly it burns
        // (the footage: yellow gaps between dark-capped lumps, drifting as
        // working). And a burning lump's cap, as a share of its zone's heat.
        internal static float BedCore = 0.7f;
        internal static float CapHeat = 0.22f;
        // And between the lumps of a zone at its full heat: yellow-white,
        // short of the ramp's white end. (Nearer white, 0.94 and 0.98, a
        // working fire did not look whiter: its bed threw more light, the
        // eye stopped down for it, 0.8 to 0.7 and 0.6, and its flames came
        // out dimmer; and at its top a working bed's gaps clip to a flat
        // white floor.)
        internal static float BedCoreHot = 0.9f;
        // The fire's light at which the eye takes in half of the cab's light
        // through the door: adapted to a burning bed, it sees coal lit by the
        // cab as black, not grey.
        internal static float DoorDimLight = 0.04f;

        // The adaptation: the fire's light (its zones' glow and flames, on
        // average, FireboxLighting) up to which the exposure is the one
        // above; how far it is stopped down past that, as the light's power
        // (0 not at all, 1 to hold the picture's brightness); and how long the
        // eye takes, s (long against the beats, so it does not pulse with them).
        internal static float AdaptLight = 0.8f;
        internal static float Adaptation = 0.7f;
        internal static float AdaptTime = 1.5f;
        internal static float LeastExposure = 0.28f;
        // And under this light, about a third of a drifting fire's, the eye
        // opens up as the light's power, this many times at most: a dim
        // standing fire seen from a dark cab (the footage's camera in a
        // station: the bed at a median 0.40, the smoky box over it 0.14; ours
        // 0.15 and black).
        internal static float DimLight = 0.4f;
        internal static float MostBrightening = 2.5f;
        // And past a working fire's draft, a share of a full one, the fire
        // burns brighter than the eye can follow, this many times over at
        // full (a hard-driven fire burns hotter, not bigger: its flames are
        // already full and tall at a working draft); smoothed over this long,
        // s, so the beats do not pump it.
        internal static float HardGlow = 2f;
        internal static float HardDraft = 0.45f;
        internal static float HardTime = 0.5f;

        /// <summary>How much brighter than the eye can follow the fire looked at burns.</summary>
        internal static float Overwhelm { get; private set; } = 1f;

        /// <summary>The fire's light the eye has adapted to.</summary>
        internal static float Light { get; private set; }

        /// <summary>The exposure in use.</summary>
        internal static float Exposed { get; private set; } = Exposure;

        // The fires seen this frame so far, the nearest's light; and the last
        // whole frame's, the frame it was seen in.
        private static int seenFrame = -1;
        private static float nearest = float.MaxValue;
        private static float nearestLight;
        private static float nearestDraft;
        private static float lastSeenDraft;
        private static int lastSeenFrame = -1;
        private static float lastSeenLight;
        private static int adaptedFrame = -1;
        private static float adaptedAt;

        /// <summary>The fire's color at this heat.</summary>
        internal static Color At(float heat)
        {
            var x = heat * 4f;
            var c = Dull * Mathf.Clamp01(x);
            c = Color.LerpUnclamped(c, Red, Mathf.Clamp01(x - 1f));
            c = Color.LerpUnclamped(c, Orange, Mathf.Clamp01(x - 2f));
            c = Color.LerpUnclamped(c, Core, Mathf.Clamp01(x - 3f));
            var white = 2f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(WhiteStart, 1f, heat));
            c = Color.LerpUnclamped(c, Hot, Mathf.Clamp01(white));
            return Color.LerpUnclamped(c, White, Mathf.Clamp01(white - 1f));
        }

        /// <summary>The heat of the fire between and under the lumps of a zone at <paramref name="heat"/> (FireBedCore in FireColor.cginc).</summary>
        internal static float CoreHeat(float heat) => Mathf.Lerp(BedCore, BedCoreHot, Mathf.Clamp01(heat) * Mathf.Clamp01(heat)) * Mathf.Clamp01(heat * 20f);

        private static float WhiteStart => Mathf.Clamp(WhiteHeat, 0.75f, 0.99f);

        /// <summary>A flame's color at its temperature, 0 to 1: the ramp from its dull red up.</summary>
        internal static Color Flame(float t) => At(0.25f + 0.75f * Mathf.Clamp01(t));

        /// <summary>
        /// A fire seen this frame, through its open door: its light, and the
        /// camera's distance from it, m. The eye adapts to the nearest.
        /// </summary>
        internal static void Seen(float light, float distance, float draft)
        {
            NextFrame();
            if (distance < nearest)
            {
                nearest = distance;
                nearestLight = light;
                nearestDraft = draft;
            }
        }

        // Keeps the last whole frame's fires when the first of a new frame's
        // comes, or the frame's adaptation.
        private static void NextFrame()
        {
            var frame = Time.frameCount;
            if (frame == seenFrame)
                return;
            if (nearest < float.MaxValue)
            {
                lastSeenFrame = seenFrame;
                lastSeenLight = nearestLight;
                lastSeenDraft = nearestDraft;
            }
            seenFrame = frame;
            nearest = float.MaxValue;
        }

        // Once a frame: toward the light of the fire seen last frame, or the
        // dark with none, over the time since the eye last adapted (none
        // adapts while no firebox is shown at all).
        private static void Adapt()
        {
            var frame = Time.frameCount;
            if (frame == adaptedFrame)
                return;
            NextFrame();
            var now = Time.time;
            var elapsed = adaptedFrame < 0 ? 0f : Mathf.Max(0f, now - adaptedAt);
            adaptedFrame = frame;
            adaptedAt = now;
            var seen = lastSeenFrame == frame - 1;
            var target = seen ? lastSeenLight : 0f;
            Light += (target - Light) * (1f - Mathf.Exp(-elapsed / Mathf.Max(AdaptTime, 0.01f)));
            var hard = seen ? 1f + HardGlow * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(HardDraft, 1f, lastSeenDraft)) : 1f;
            Overwhelm += (hard - Overwhelm) * (1f - Mathf.Exp(-elapsed / Mathf.Max(HardTime, 0.01f)));
            var over = Light / Mathf.Max(AdaptLight, 1e-3f);
            var dim = Light / Mathf.Max(DimLight, 1e-3f);
            var opened = dim < 1f ? Mathf.Min(Mathf.Pow(Mathf.Max(dim, 1e-3f), -Mathf.Clamp01(Adaptation)), Mathf.Max(1f, MostBrightening)) : 1f;
            Exposed = Mathf.Max(Exposure * (over > 1f ? Mathf.Pow(over, -Mathf.Clamp01(Adaptation)) : opened), Mathf.Min(LeastExposure, Exposure)) * Overwhelm;
        }

        /// <summary>Sets the ramp for the shaders, as vectors (a color would be taken as sRGB), and the exposure, adapted once a frame.</summary>
        internal static void Publish()
        {
            Adapt();
            Shader.SetGlobalVector(DullId, Dull);
            Shader.SetGlobalVector(RedId, Red);
            Shader.SetGlobalVector(OrangeId, Orange);
            Shader.SetGlobalVector(CoreId, Core);
            Shader.SetGlobalVector(HotId, Hot);
            Shader.SetGlobalVector(WhiteId, White);
            Shader.SetGlobalVector(BedId, new Vector4(BedCore, CapHeat, 1f / (1f + Light / Mathf.Max(DoorDimLight, 1e-4f)), Mathf.Clamp(BedCoreHot, BedCore, 1f)));
            Shader.SetGlobalFloat(WhiteHeatId, WhiteStart);
            Shader.SetGlobalFloat(ExposureId, Exposed);
        }
    }
}
