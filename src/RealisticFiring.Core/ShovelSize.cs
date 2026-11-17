using System;

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

        /// <summary>
        /// The player's shovelful, kg: <paramref name="picked"/> by the mouse
        /// wheel, from <paramref name="least"/> up to the
        /// <paramref name="largest"/> set in the menu, or a full one where
        /// none is picked (0, or anything below the least).
        /// </summary>
        public static float Scoop(float picked, float largest, float least) =>
            picked >= least ? Math.Min(picked, largest) : largest;

        /// <summary>A normal shovelful, kg: the setting's default, and the least the fireman fires.</summary>
        public const float Normal = 3f;

 public static float Fireman(float largest) => Math.Max(Normal, largest);

        /// <summary>What one click of the mouse wheel changes the shovelful by, kg.</summary>
        public const float WheelStep = 0.5f;

        /// <summary>
        /// The shovelful after <paramref name="clicks"/> of the mouse wheel
        /// (forward bigger, back smaller), on the half-kilogram marks between
        /// <paramref name="least"/> and <paramref name="most"/> kg. A size
        /// set between two marks goes to the next mark the wheel turns toward.
        /// </summary>
        public static float Wheeled(float shovelful, int clicks, float least, float most)
        {
            var marks = shovelful / WheelStep;
            var mark = clicks > 0 ? Math.Floor(marks + 1e-3f) + clicks
                : clicks < 0 ? Math.Ceiling(marks - 1e-3f) + clicks
                : marks;
            return Math.Max(least, Math.Min(most, (float)mark * WheelStep));
        }
    }
}
