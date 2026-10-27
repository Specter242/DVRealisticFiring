using DV.Utils;
using UnityEngine;

namespace RealisticStackEffects
{
    /// <summary>
    /// Puts a particle system in the game's world-origin space, as the game's
    /// own <c>WorldMoverParticleSimulationSpace</c> does for its smoke: the
    /// game shifts the world back toward the origin as the player travels,
    /// and particles simulated in plain world space are left behind when it
    /// does, so smoke vanishes and starts over. The game's component only
    /// switches a system when it wakes, which can come after the mod copies
    /// a system's space, so the mod sets it itself.
    /// </summary>
    internal static class WorldParticles
    {
        /// <summary>Makes <paramref name="system"/> follow the world's origin shifts. Returns whether it could.</summary>
        public static bool FollowOriginShifts(ParticleSystem system)
        {
            if (system == null || SingletonBehaviour<WorldMover>.Instance == null || WorldMover.OriginShiftParent == null)
                return false;
            var main = system.main;
            main.simulationSpace = ParticleSystemSimulationSpace.Custom;
            main.customSimulationSpace = WorldMover.OriginShiftParent;
            return true;
        }
    }
}
