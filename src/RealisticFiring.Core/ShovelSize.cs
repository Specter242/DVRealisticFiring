namespace RealisticFiring.Core
{
    /// <summary>
    /// Shovel capacity for a real shovelful. The stock shovel loads
    /// <c>shovelChunksCapacity × coalChunkMass</c> from the coal pile, and
    /// <c>FireboxSimController.TransferCoal</c> divides that by the firebox's
    /// coal consumption multiplier: on the S282, 3 chunks of 48 kg over 4 is
    /// 36 kg per throw (measured in game).
    /// </summary>
    public static class ShovelSize
    {
        /// <summary>Chunks the game's standard <c>shovel</c> item carries (measured in game).</summary>
        public const int StandardShovelChunks = 3;

        /// <summary>
        /// The pile mass a shovel should load so that a standard shovel puts
        /// <paramref name="shovelfulMass"/> kg into the firebox. Shovels with
        /// other chunk counts keep their stock size ratio.
        /// </summary>
        /// <param name="shovelfulMass">Coal a standard shovel puts into the firebox, in kg.</param>
        /// <param name="shovelChunks">This shovel's stock chunk capacity; 0 or less counts as standard.</param>
        /// <param name="consumptionMultiplier">Pile kg per firebox kg; 0 or less counts as 1.</param>
        public static float PileCapacity(float shovelfulMass, int shovelChunks, float consumptionMultiplier)
        {
            var chunks = shovelChunks > 0 ? shovelChunks : StandardShovelChunks;
            var multiplier = consumptionMultiplier > 0f ? consumptionMultiplier : 1f;
            return shovelfulMass * chunks / StandardShovelChunks * multiplier;
        }
    }
}
