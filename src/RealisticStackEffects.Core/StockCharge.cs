using System;

namespace RealisticStackEffects.Core
{
    /// <summary>
    /// A shovelful's smoke-out from the game's own firebox, which has none:
    /// coal landing on a hot fire gives off thick, dark gas until it catches
    /// and the draft clears it, as Realistic Firing's firebox smoke does
    /// (its <c>FireboxSmoke</c>). A shovelful is told when it lands
    /// (<see cref="Throw"/>, from the game's coal transfer), not read from
    /// the firebox's coal, which a stoker's feed and the fire's burning move
    /// too. A stock throw is far bigger than a real one (36 kg on the S282),
    /// so it smokes no thicker than this much coal at once, and longer
    /// instead. Rates are in firebox puffs a second.
    /// </summary>
    public sealed class StockCharge
    {
        /// <summary>Smoke-out puffs a second per kg of charge left, as Realistic Firing's.</summary>
        public const float SmokeOutPerKg = 8f;

        /// <summary>
        /// The most charge that smokes at once, in kg: 40 puffs a second,
        /// the thickest the stack shows (<see cref="PlumeModel.FireboxSoot"/>).
        /// </summary>
        public const float MostSmoking = 5f;

        // The charge clears over these times, in s, from a dull fire to a
        // forced one; the smoke takes this long through the tubes, in s.
        private const float SmokeOutSlow = 7f;
        private const float SmokeOutFast = 3f;
        private const float TubeTransit = 1.5f;

        // The fire's warmth at which green coal gives off its gas fully, °C.
        private const float WarmFire = 900f;

        // Coal landed since the last step, kg.
        private float landed;

        /// <summary>The charge left to smoke out, in kg of coal.</summary>
        public float Charge { get; private set; }

        /// <summary>The smoke-out in the firebox, in puffs a second.</summary>
        public float SmokeOutRate { get; private set; }

        /// <summary>The smoke-out reaching the stack through the tubes, in puffs a second.</summary>
        public float StackRate { get; private set; }

        // The most coal waiting to land, kg: a firebox's worth and more, should no step come.
        private const float MostLanded = 1000f;

        /// <summary>A shovelful landing on the fire, <paramref name="kg"/> of coal as the firebox took it.</summary>
        public void Throw(float kg)
        {
            if (kg > 0f && !float.IsInfinity(kg))
                landed = Math.Min(MostLanded, landed + kg);
        }

        /// <summary>
        /// Steps the charge <paramref name="dt"/> s on, with the firebox's
        /// temperature (°C) and how hard it is forced (its combustion over the
        /// most, 0 to 1).
        /// </summary>
        public void Step(float dt, bool fireOn, float temperature, float forcing)
        {
            if (!(dt > 0f))
                return;
            if (!fireOn)
            {
                // Coal laid on a dead fire smokes nothing when it is lit.
                Charge = SmokeOutRate = landed = 0f;
            }
            else
            {
                Charge += landed;
                landed = 0f;
                var warming = Clamp01(0.4f + 0.6f * temperature / WarmFire);
                SmokeOutRate = SmokeOutPerKg * Math.Min(Charge, MostSmoking) * warming;
                var clearing = SmokeOutSlow + (SmokeOutFast - SmokeOutSlow) * Clamp01(forcing);
                Charge *= (float)Math.Exp(-dt / clearing);
            }
            StackRate += (SmokeOutRate - StackRate) * (1f - (float)Math.Exp(-dt / TubeTransit));
        }

        private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));
    }
}
