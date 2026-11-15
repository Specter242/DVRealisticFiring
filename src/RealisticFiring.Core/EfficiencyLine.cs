using System;

namespace RealisticFiring.Core
{
 public readonly struct EfficiencyLine
    {
        public EfficiencyLine(float light, float forced, float mostRate)
        {
            Light = light;
            Forced = forced;
            MostRate = mostRate;
        }

        /// <summary>The share fired lightly, at no rate.</summary>
        public float Light { get; }

        /// <summary>The share at <see cref="MostRate"/>.</summary>
        public float Forced { get; }

        /// <summary>The most the fire burns, kg/s.</summary>
        public float MostRate { get; }

        /// <summary>The share burning <paramref name="rate"/> kg/s.</summary>
        public float At(float rate) => Smoothing.Lerp(Light, Forced, rate / MostRate);

        /// <summary>The most heat the fire gives, in W.</summary>
        public float MostHeat => MostRate * FireboxParameters.CoalHeatContent * Forced;

        /// <summary>The line with each end kept by its own share: a box without the fitting that gains them.</summary>
        public EfficiencyLine Kept(float light, float forced) => new EfficiencyLine(Light * light, Forced * forced, MostRate);

        /// <summary>The burn rate that gives <paramref name="heat"/> W, in kg/s, at most <see cref="MostRate"/>.</summary>
        public float RateFor(float heat)
        {
            if (!(heat > 0f))
                return 0f;
            var c = FireboxParameters.CoalHeatContent;
            // heat = r c (light + (forced - light) r / most), solved for r.
            var a = (Forced - Light) * c / MostRate;
            var b = Light * c;
            var disc = b * b + 4f * a * heat;
            if (disc <= 0f)
                return MostRate;
            return Math.Min(MostRate, 2f * heat / (b + (float)Math.Sqrt(disc)));
        }
    }
}
