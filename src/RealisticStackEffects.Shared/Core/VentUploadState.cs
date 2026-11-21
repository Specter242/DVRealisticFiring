namespace RealisticStackEffects.Core
{
    /// <summary>
    /// Tracks the last uploaded particle counts for the front and behind vent
    /// particle systems so redundant empty uploads (0 to 0) are skipped while
    /// every active frame and every transition back to empty (N to 0) still
    /// uploads once to clear Unity's particle system.
    /// </summary>
    public struct VentUploadState
    {
        private int lastFrontCount;
        private int lastBehindCount;

        /// <summary>The last particle count uploaded to the front system.</summary>
        public int LastFrontCount => lastFrontCount;

        /// <summary>The last particle count uploaded to the behind system.</summary>
        public int LastBehindCount => lastBehindCount;

        /// <summary>
        /// Records <paramref name="count"/> for the front system and returns
        /// whether its particles and custom data must be uploaded.
        /// </summary>
        public bool TakeFront(int count) => Take(ref lastFrontCount, count);

        /// <summary>
        /// Records <paramref name="count"/> for the behind system and returns
        /// whether its particles and custom data must be uploaded.
        /// </summary>
        public bool TakeBehind(int count) => Take(ref lastBehindCount, count);

        /// <summary>Resets both tracked counts to zero.</summary>
        public void Reset()
        {
            lastFrontCount = 0;
            lastBehindCount = 0;
        }

        private static bool Take(ref int last, int current)
        {
            var clamped = current > 0 ? current : 0;
            var upload = clamped > 0 || last > 0;
            last = clamped;
            return upload;
        }
    }
}
