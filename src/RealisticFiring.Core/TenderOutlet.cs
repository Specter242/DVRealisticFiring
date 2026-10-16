using System;

namespace RealisticFiring.Core
{
 public static class TenderOutlet
    {
        public const float Gravity = 9.81f;

        /// <summary>The tank's length and depth, and the outlet sump's depth below its floor, m.</summary>
        public const float TankLength = 6f;
        public const float TankDepth = 1.8f;
        public const float SumpDepth = 0.05f;

        /// <summary>How long the shallow water takes to run to its new level, s.</summary>
        public const float SloshTime = 2f;

        /// <summary>The water over the outlet before it is uncovered again, m: it has to run back to cover it.</summary>
        public const float CoverAgain = 0.02f;

        /// <summary>
        /// The water's depth over the outlet, m (below 0 the outlet is
        /// uncovered): the tank <paramref name="share"/> full, and the pull of
        /// gravity and the tender's acceleration along the tank toward its
        /// outlet end, <paramref name="pull"/> m/s² (positive drives the water
        /// to the outlet), tilting the water's surface along the tank.
        /// </summary>
        public static float OutletDepth(float share, float pull) =>
            Math.Max(0f, Math.Min(1f, share)) * TankDepth + SumpDepth + 0.5f * TankLength * pull / Gravity;
    }
}
