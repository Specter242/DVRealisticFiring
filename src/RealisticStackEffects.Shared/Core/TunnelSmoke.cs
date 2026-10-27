using System;

namespace RealisticStackEffects.Core
{
 public static class TunnelSmoke
    {
        /// <summary>Half a single-track bore's width at the smoke's height, m (assumed).</summary>
        public const float HalfWidth = 2.5f;

        /// <summary>
        /// The share of the train's speed the bore's air moves at with the
        /// train in it (assumed for a tight single-track bore).
        /// </summary>
        public const float PistonShare = 0.3f;

        /// <summary>The ceiling jet leaves the plume at this share of its rise under the roof.</summary>
        public const float JetShare = 0.8f;

        /// <summary>The ceiling jet slows over this time, s.</summary>
        public const float JetSlowing = 20f;

        // A particle's middle is held this share of its size under the roof,
        // at most the most, in m; and this share of its size off the walls.
        private const float RoofClearance = 0.3f;
        private const float MostRoofClearance = 1f;
        private const float WallClearance = 0.25f;

        /// <summary>A particle is never drawn wider than the bore, m.</summary>
        public const float WidestParticle = 2f * HalfWidth;

        /// <summary>
        /// The highest a particle <paramref name="size"/> m wide may sit under a
        /// roof at <paramref name="roof"/>, never below <paramref name="floor"/>
        /// (the stack's mouth), on the same height scale.
        /// </summary>
        public static float Cap(float roof, float size, float floor) =>
            Math.Max(floor, roof - Math.Min(MostRoofClearance, RoofClearance * Math.Max(0f, size)));

        /// <summary>How far to the side of the track a particle <paramref name="size"/> m wide may sit, m.</summary>
        public static float WallReach(float size) => Math.Max(0f, HalfWidth - WallClearance * Math.Max(0f, size));

        /// <summary>The ceiling jet's speed along the roof, m/s, from a plume rising at <paramref name="rise"/> m/s, <paramref name="age"/> s on.</summary>
        public static float JetSpeed(float rise, float age) =>
            JetShare * Math.Max(0f, rise) * (float)Math.Exp(-Math.Max(0f, age) / JetSlowing);
    }
}
