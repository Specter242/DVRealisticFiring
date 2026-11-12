using System;

namespace RealisticStackEffects.Core
{
    /// <summary>
    /// When each of a plume's particles is worked out in full (its rise,
    /// spread, eddies, size, and fade), the game moving it in between. In
    /// turns by its seed, how often by its age: one under a second out of the
    /// stack about every <c>period</c> (25 ms for a plume in view close by),
    /// and half as often for each doubling of its age, to a sixteenth from
    /// eight seconds. What a particle shows changes by its age's share, so
    /// each is worked out again by the time it is a few percent older. The
    /// turns are passes of the plume's motion, counted in powers of two so
    /// that every particle has had its turn once a cycle of the longest.
    /// By the frame's time alone (to 2026-11-11: every 25 ms, over one to
    /// three frames), every particle was worked out every frame below 60
    /// frames a second: the plume cost twice as much just where the game was
    /// already slow (performance audit, 2026-11-11).
    /// </summary>
    public struct PlumeTurns
    {
        /// <summary>Particles younger than this, s, are of the newest tier.</summary>
        public const float YoungestTier = 1f;

        /// <summary>The oldest tier: 2^OldestTier × YoungestTier s and over.</summary>
        public const int OldestTier = 4;

        /// <summary>The longest cycle, passes, as a power of two.</summary>
        public const int LongestCycle = 8;

        /// <summary>How often a plume's newest particles are worked out when it is carried every frame, s.</summary>
        public const float NewestEvery = 0.025f;

        /// <summary>A plume in view nearer than this, m, is carried every frame; one farther than <see cref="FarOff"/> least often.</summary>
        public const float NearBy = 300f;
        public const float FarOff = 1000f;

        // Passes between the newest tier's turns, as a power of two; under 0, the next tiers every pass too.
        private int newest;

        /// <summary>The cycle's length in passes, as a power of two.</summary>
        public int CycleShift { get; private set; }

        /// <summary>Passes in a cycle: by its end every particle has had a turn.</summary>
        public int Passes => 1 << CycleShift;

        /// <summary>
        /// The turns for a cycle of passes <paramref name="elapsed"/> s apart,
        /// the newest particles worked out about every <paramref name="period"/> s.
        /// </summary>
        public static PlumeTurns Plan(float period, float elapsed)
        {
            var ratio = Math.Max(period, 1e-3f) / Math.Max(elapsed, 1e-4f);
            var newest = (int)Math.Round(Math.Log(ratio, 2.0));
            return new PlumeTurns { newest = newest, CycleShift = Math.Max(0, Math.Min(LongestCycle, OldestTier + newest)) };
        }

        /// <summary>A particle's tier by its age: 0 under <see cref="YoungestTier"/> s, one more for each doubling.</summary>
        public static int TierOf(float age)
        {
            var tier = 0;
            for (var from = YoungestTier; tier < OldestTier && age >= from; from *= 2f)
                tier++;
            return tier;
        }

        /// <summary>Passes between the turns of a particle <paramref name="age"/> s old.</summary>
        public int Apart(float age) => 1 << Math.Max(0, Math.Min(CycleShift, TierOf(age) + newest));

        /// <summary>Whether pass <paramref name="turn"/> of the cycle (from 0) is the turn of the particle with <paramref name="seed"/>, <paramref name="age"/> s old.</summary>
        public bool Due(uint seed, float age, int turn)
        {
            var mask = (uint)Apart(age) - 1u;
            return (seed & mask) == ((uint)turn & mask);
        }

        /// <summary>Whether pass <paramref name="turn"/> is the one pass of the cycle that counts the particle with <paramref name="seed"/> (it is always one of its turns).</summary>
        public bool Counted(uint seed, int turn) => (seed & ((uint)Passes - 1u)) == (uint)turn;

        /// <summary>
        /// The frames between a plume's passes, by whether any of it is
        /// <paramref name="inView"/> and how far <paramref name="away"/> its
        /// stack is, m (performance audit, 2026-11-11: every plume was
        /// carried in full every frame, six engines in steam 11 ms a frame
        /// whether beside the player or 3 km off). Its particles' turns are
        /// as much farther apart: <see cref="NewestEvery"/> × this.
        /// </summary>
        public static int FramesApart(bool inView, float away) =>
            inView ? (away < NearBy ? 1 : away < FarOff ? 2 : 4) : away < FarOff ? 4 : 8;
    }
}
