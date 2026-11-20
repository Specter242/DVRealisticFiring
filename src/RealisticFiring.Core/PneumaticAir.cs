using System;

namespace RealisticFiring.Core
{
 public static class PneumaticAir
    {
        /// <summary>The burst's length, s.</summary>
        public const float BurstSeconds = 0.55f;

        /// <summary>The hiss loop's length, s.</summary>
        public const float HissSeconds = 1.6f;

        // The burst: a valve's pop under the first few hundredths of a second, then air rushing out, its brightness
        // falling as the pressure behind it drops ("pssht").
        private const double BurstAttack = 0.003;
        private const double BurstDecay = 0.11;
        private const double BurstTail = 0.02;
        private const double BurstBrightFrom = 9000.0, BurstBrightTo = 2800.0;
        private const double BurstLow = 350.0;
        private const double PopHz = 95.0, PopDecay = 0.018, PopShare = 0.35;

        // The hiss: air leaking past the cylinder's seal, steady and high, a little unsteady in level.
        private const double HissLow = 1400.0, HissHigh = 7500.0;
        // A whole number of flutters in the loop, so it joins in step.
        private const double FlutterHz = 5.0, FlutterDepth = 0.08;
        private const double LoopJoin = 0.06;

        /// <summary>The burst, mono at <paramref name="rate"/>, its loudest sample 0.9.</summary>
        public static float[] Burst(int rate, int seed)
        {
            var n = (int)(BurstSeconds * rate);
            var sound = new float[n];
            var random = new Random(seed);
            var high = new Biquad(rate, BurstLow, highPass: true);
            var low = new OnePole();
            for (var i = 0; i < n; i++)
            {
                var t = (double)i / rate;
                var noise = random.NextDouble() * 2.0 - 1.0;
                var bright = BurstBrightTo + (BurstBrightFrom - BurstBrightTo) * Math.Exp(-t / 0.15);
                var air = low.Step(high.Step(noise), bright, rate);
                var envelope = t < BurstAttack ? t / BurstAttack : Math.Exp(-(t - BurstAttack) / BurstDecay) + BurstTail * Math.Exp(-(t - BurstAttack) / 0.25);
                var pop = PopShare * Math.Sin(2.0 * Math.PI * PopHz * t) * Math.Exp(-t / PopDecay);
                sound[i] = (float)(air * envelope + pop);
            }
            // The last few hundredths fade to nothing, so it ends without a click.
            Fade(sound, rate, 0.03);
            Normalize(sound, 0.9f);
            return sound;
        }

        /// <summary>The hiss, mono at <paramref name="rate"/>, joined end to start so it loops without a seam; RMS 0.25.</summary>
        public static float[] Hiss(int rate, int seed)
        {
            var join = (int)(LoopJoin * rate);
            var n = (int)(HissSeconds * rate);
            var raw = new double[n + join];
            var random = new Random(seed);
            var high = new Biquad(rate, HissLow, highPass: true);
            var low = new Biquad(rate, HissHigh, highPass: false);
            // Filters settled before the part kept.
            for (var i = 0; i < rate / 10; i++)
                low.Step(high.Step(random.NextDouble() * 2.0 - 1.0));
            for (var i = 0; i < raw.Length; i++)
            {
                var t = (double)i / rate;
                var flutter = 1.0 + FlutterDepth * Math.Sin(2.0 * Math.PI * FlutterHz * t);
                raw[i] = low.Step(high.Step(random.NextDouble() * 2.0 - 1.0)) * flutter;
            }
            // The extra at the end crossfades over the start (equal power, as the two are unrelated noise).
            var sound = new float[n];
            for (var i = 0; i < n; i++)
            {
                if (i < join)
                {
                    var a = (double)i / join;
                    sound[i] = (float)(raw[i] * Math.Sin(a * Math.PI / 2.0) + raw[n + i] * Math.Cos(a * Math.PI / 2.0));
                }
                else
                    sound[i] = (float)raw[i];
            }
            var squares = 0.0;
            foreach (var s in sound)
                squares += s * s;
            var rms = Math.Sqrt(squares / n);
            if (rms > 0.0)
                for (var i = 0; i < n; i++)
                    sound[i] = (float)(sound[i] * 0.25 / rms);
            return sound;
        }

        private static void Fade(float[] sound, int rate, double seconds)
        {
            var m = Math.Min(sound.Length, (int)(seconds * rate));
            for (var i = 0; i < m; i++)
                sound[sound.Length - 1 - i] *= (float)((double)i / m);
        }

        private static void Normalize(float[] sound, float peak)
        {
            var most = 0f;
            foreach (var s in sound)
                most = Math.Max(most, Math.Abs(s));
            if (most > 0f)
                for (var i = 0; i < sound.Length; i++)
                    sound[i] *= peak / most;
        }

        // A second-order Butterworth high or low pass.
        private sealed class Biquad
        {
            private readonly double b0, b1, b2, a1, a2;
            private double x1, x2, y1, y2;

            public Biquad(int rate, double cutoff, bool highPass)
            {
                var w = 2.0 * Math.PI * Math.Min(cutoff, rate * 0.45) / rate;
                var alpha = Math.Sin(w) / (2.0 * Math.Sqrt(0.5));
                var cos = Math.Cos(w);
                var a0 = 1.0 + alpha;
                if (highPass)
                {
                    b0 = (1.0 + cos) / 2.0 / a0;
                    b1 = -(1.0 + cos) / a0;
                }
                else
                {
                    b0 = (1.0 - cos) / 2.0 / a0;
                    b1 = (1.0 - cos) / a0;
                }
                b2 = b0;
                a1 = -2.0 * cos / a0;
                a2 = (1.0 - alpha) / a0;
            }

            public double Step(double x)
            {
                var y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1;
                x1 = x;
                y2 = y1;
                y1 = y;
                return y;
            }
        }

        // A one-pole low pass whose cutoff may change each sample.
        private sealed class OnePole
        {
            private double y;

            public double Step(double x, double cutoff, int rate)
            {
                var a = 1.0 - Math.Exp(-2.0 * Math.PI * cutoff / rate);
                y += (x - y) * a;
                return y;
            }
        }
    }
}
