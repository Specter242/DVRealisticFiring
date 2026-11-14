using RealisticFiring.Core;
using UnityEngine;

namespace RealisticFiring.Visuals
{
 internal static class WoodFlame
    {
        // Full flames at about four pieces' peak at the game's pace, W.
        private const float FullHeat = 20000f * StarterFuel.Pace;

        /// <summary>
        /// How hard a zone's wood flames burn, 0 to 1. A burning rag alone is
        /// too small a flame for the volume, which would spread it over the
        /// whole zone as a glowing bed (preview 950's check): the intake draws
        /// its own small flame until the wood catches. Glowing char is drawn
        /// by the wood itself.
        /// </summary>
        internal static float Strength(StarterFuel starter) =>
            starter == null || !(starter.Involved > 0f) ? 0f
                : Mathf.Sqrt(Mathf.Clamp01(starter.Heat / FullHeat)) * Mathf.Sqrt(starter.Flaming);

        /// <summary>A burning rag's light, as a flame strength, before the wood catches.</summary>
        internal const float RagLight = 0.2f;

        /// <summary>The wood pile's top over the grate, m: its pieces laid crosswise in pairs, 6.2 cm a layer.</summary>
        internal static float PileHeight(StarterFuel starter) =>
            starter == null ? 0f : Mathf.Min(3f, Mathf.Ceil((starter.Wood + starter.Char * 2.2f) / StarterFuel.PieceMass / 2f)) * 0.062f;

        /// <summary>The flames' top over the grate, m: a hand to a forearm and more over a burning pile.</summary>
        internal static float Top(StarterFuel starter, float strength) =>
            strength <= 0f ? 0f : PileHeight(starter) + 0.10f + 0.35f * strength;

        /// <summary>The flames' colour, as the volume's zone heat: a wood fire's orange-yellow, not a forced coal fire's white.</summary>
        internal const float ColorHeat = 0.45f;
    }
}
