namespace RealisticStackEffects.Core
{
 public static class PlumeOrder
    {
        /// <summary>
        /// A parcel changes sides only once it is this far past the plume's
        /// middle, m: one drifting through the plume at about its depth
        /// would otherwise flick in front of the smoke and behind it.
        /// </summary>
        public const float Margin = 0.3f;

        /// <summary>
        /// Whether a parcel <paramref name="depth"/> m from the eye is drawn
        /// behind the plume, whose middle is <paramref name="plume"/> m from
        /// the eye on its line of sight; <paramref name="was"/> is the side
        /// it was drawn on until now.
        /// </summary>
        public static bool Behind(bool was, float depth, float plume, float margin = Margin) =>
            depth > plume + (was ? -margin : margin);
    }
}
