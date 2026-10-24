using System;

namespace RealisticFiring.Core
{
 public sealed class FiringValveStop
    {
        /// <summary>A valve opened less than this is shut: a click there sets no stop.</summary>
        public const float LeastStop = 0.01f;

        /// <summary>Where the stop stands, 0 shut to 1 wide open; 0 with it out.</summary>
        public float At { get; private set; }

        /// <summary>Whether the stop is in.</summary>
        public bool In => At > 0f;

        /// <summary>The valve's opening with the lever at <paramref name="lever"/>: never under the stop.</summary>
        public float Limit(float lever) => Math.Max(Clamp01(lever), At);

        /// <summary>
        /// A click on the valve with the lever at <paramref name="lever"/>:
        /// takes the stop out if it is in, else sets it there (none on a shut
        /// valve). True when the stop is now in.
        /// </summary>
        public bool Toggle(float lever)
        {
            var at = Clamp01(lever);
            At = In || at < LeastStop ? 0f : at;
            return In;
        }

        /// <summary>
        /// Where the fireman puts the lever, wanting <paramref name="want"/>:
        /// never under the stop; to shut the valve he takes the stop out.
        /// </summary>
        public float ForFireman(float want)
        {
            if (want > 0f)
                return Math.Max(Clamp01(want), At);
            At = 0f;
            return 0f;
        }

        /// <summary>Puts back a saved stop; one out of range or not a number leaves it out.</summary>
        public void Restore(float at) => At = at >= LeastStop ? Clamp01(at) : 0f;

        private static float Clamp01(float value) => value > 0f ? Math.Min(value, 1f) : 0f;
    }
}
