using System;

namespace RealisticFiring.Core
{
    /// <summary>
    /// Decides when an empty shovel held in VR rakes the firebox. Physical
    /// shoveling in VR loads and unloads coal by moving the blade into the
    /// coal pile or the open firehole, with no click; raking works the same
    /// way: thrusting or sweeping an empty blade into the open firehole levels
    /// the targeted grate zone.
    ///
    /// Unloading coal leaves the shovel empty while the blade is still inside
    /// the firehole, so a transition from loaded to empty locks out raking
    /// until the blade leaves the firebox and <see cref="UnloadGrace"/> has
    /// passed, preventing a throw from immediately raking.
    /// </summary>
    public sealed class VrShovelRake
    {
        /// <summary>The door opening VR shoveling and raking need (matches ShovelNonPhysicalCoal.REQUIRED_DOOR_OPENED_PERCENTAGE_VR).</summary>
        public const float DoorOpening = 0.25f;

        /// <summary>Shortest time between two rakes, in s.</summary>
        public const float Cooldown = 0.75f;

        /// <summary>Time after unloading coal during which raking is locked out, in s.</summary>
        public const float UnloadGrace = 1.0f;

        /// <summary>Blade travel in the car's frame while inside the firehole to rake again without pulling out, in m.</summary>
        public const float SweepDistance = 0.16f;

        /// <summary>Continuous time outside the open firebox needed to count as an exit when the blade stays within <see cref="SweepDistance"/> of its reference point, in s.</summary>
        public const float ExitDebounce = 0.2f;

        private bool wasLoaded;
        private bool insideFirebox;
        private bool mustExitBeforeRake;
        private float unloadGraceUntil = float.NegativeInfinity;
        private float outsideSince = float.PositiveInfinity;
        private float lastRakeTime = float.NegativeInfinity;
        private float lastRakeX;
        private float lastRakeY;
        private float lastRakeZ;
        private bool hasLastRake;

        /// <summary>Whether the blade is currently in an open firebox and eligible to highlight its targeted zone for raking.</summary>
        public bool CanHighlight(bool grabbed, bool loaded, bool inOpenFirebox, float now) =>
            grabbed && !loaded && inOpenFirebox && !wasLoaded
            && (!mustExitBeforeRake || now >= outsideSince + ExitDebounce)
            && now >= unloadGraceUntil;

        /// <summary>Resets entry and lockout state, e.g. when the shovel is disabled.</summary>
        public void Reset()
        {
            wasLoaded = false;
            insideFirebox = false;
            mustExitBeforeRake = false;
            unloadGraceUntil = float.NegativeInfinity;
            outsideSince = float.PositiveInfinity;
            lastRakeTime = float.NegativeInfinity;
            hasLastRake = false;
        }

        /// <summary>
        /// Advances one frame. Returns true on a frame where the empty shovel
        /// should rake <paramref name="zone"/>. Coordinates are in the car's
        /// frame so a moving train never reads as a sweep.
        /// </summary>
        public bool Step(bool grabbed, bool loaded, bool inOpenFirebox, GrateZone zone, float tipX, float tipY, float tipZ, float now)
        {
            if (!grabbed)
            {
                insideFirebox = false;
                mustExitBeforeRake = false;
                outsideSince = float.PositiveInfinity;
                wasLoaded = loaded;
                return false;
            }

            if (loaded)
            {
                wasLoaded = true;
                if (inOpenFirebox)
                {
                    insideFirebox = true;
                    mustExitBeforeRake = true;
                    outsideSince = float.PositiveInfinity;
                    SetReferencePoint(tipX, tipY, tipZ);
                }
                else
                {
                    ClearIfExited(tipX, tipY, tipZ, now);
                }
                return false;
            }

            if (wasLoaded)
            {
                wasLoaded = false;
                unloadGraceUntil = now + UnloadGrace;
                mustExitBeforeRake = insideFirebox || inOpenFirebox;
                if (inOpenFirebox)
                {
                    insideFirebox = true;
                    outsideSince = float.PositiveInfinity;
                    SetReferencePoint(tipX, tipY, tipZ);
                }
            }

            if (!inOpenFirebox)
            {
                ClearIfExited(tipX, tipY, tipZ, now);
                return false;
            }

            if (!float.IsPositiveInfinity(outsideSince))
            {
                if (now >= outsideSince + ExitDebounce)
                {
                    insideFirebox = false;
                    mustExitBeforeRake = false;
                }
                outsideSince = float.PositiveInfinity;
            }

            if (mustExitBeforeRake || now < unloadGraceUntil)
            {
                if (!insideFirebox || !hasLastRake)
                    SetReferencePoint(tipX, tipY, tipZ);
                insideFirebox = true;
                return false;
            }

            if (now - lastRakeTime < Cooldown)
            {
                if (!insideFirebox || !hasLastRake)
                    SetReferencePoint(tipX, tipY, tipZ);
                insideFirebox = true;
                return false;
            }

            var trigger = !insideFirebox || MovedSweepDistance(tipX, tipY, tipZ);

            insideFirebox = true;
            if (!trigger)
                return false;

            lastRakeTime = now;
            SetReferencePoint(tipX, tipY, tipZ);
            return true;
        }

        private void ClearIfExited(float tipX, float tipY, float tipZ, float now)
        {
            if (!insideFirebox && !mustExitBeforeRake)
            {
                outsideSince = float.PositiveInfinity;
                return;
            }

            if (float.IsPositiveInfinity(outsideSince))
                outsideSince = now;

            if (now >= outsideSince + ExitDebounce || MovedSweepDistance(tipX, tipY, tipZ))
            {
                insideFirebox = false;
                mustExitBeforeRake = false;
                outsideSince = float.PositiveInfinity;
            }
        }

        private bool MovedSweepDistance(float tipX, float tipY, float tipZ)
        {
            if (!hasLastRake)
                return false;
            var dx = tipX - lastRakeX;
            var dy = tipY - lastRakeY;
            var dz = tipZ - lastRakeZ;
            return dx * dx + dy * dy + dz * dz >= SweepDistance * SweepDistance;
        }

        private void SetReferencePoint(float tipX, float tipY, float tipZ)
        {
            lastRakeX = tipX;
            lastRakeY = tipY;
            lastRakeZ = tipZ;
            hasLastRake = true;
        }
    }
}
