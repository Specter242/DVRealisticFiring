using System;

namespace RealisticFiring.Core
{
    /// <summary>What the engine and fire feed the chimney plume.</summary>
    public struct PlumeInputs
    {
        /// <summary>Exhaust pressure at the blast pipe (stock EXHAUST_PRESSURE), in bar.</summary>
        public float ExhaustPressure;

        /// <summary>Steam and air through the smokebox over the stock maximum (stock TOTAL_FLOW_NORMALIZED).</summary>
        public float TotalFlowNormalized;

        /// <summary>Visible smoke leaving the fire, 0 to 1 (the fire model's soot density).</summary>
        public float Soot;

        public bool FireOn;

        /// <summary>Gas and the fire's own smoke leaving the firebox for the stack, in firebox puffs a second.</summary>
        public float FireboxGas;

        /// <summary>A shovelful's thick smoke leaving the firebox for the stack, in firebox puffs a second.</summary>
        public float FireboxSmokeOut;
    }

    /// <summary>A particle color, 0 to 1 per channel.</summary>
    public struct PlumeColor
    {
        public PlumeColor(float r, float g, float b, float a)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        public float R { get; }
        public float G { get; }
        public float B { get; }
        public float A { get; }
    }

    /// <summary>One exhaust beat's puff.</summary>
    public struct Puff
    {
        /// <summary>Launch speed up the chimney, in m/s.</summary>
        public float Speed;

        /// <summary>Starting particle size, in m.</summary>
        public float Size;

        public int Particles;

        /// <summary>Extra dark particles for the puff's sooty core.</summary>
        public int SootParticles;

        /// <summary>Its particles' lifetime against the stock smoke's: heavy beats tower and linger.</summary>
        public float LifetimeScale;

        /// <summary>Expected sparks thrown out by the beat; usually well under one.</summary>
        public float Sparks;

        public PlumeColor Color;
    }

    /// <summary>The steady plume between beats, from the blower and natural draft.</summary>
    public struct Haze
    {
        /// <summary>Particles per second.</summary>
        public float Rate;

        /// <summary>Launch speed, in m/s.</summary>
        public float Speed;

        public PlumeColor Color;
    }

    /// <summary>Adjustable look of the chimney smoke; the defaults are the tuned values.</summary>
    public sealed class PlumeTuning
    {
        /// <summary>Exhaust pressure that makes the hardest beat, in bar (stock smoke uses 10).</summary>
        public float MaxBeatPressure { get; set; } = 6f;

        /// <summary>
        /// Exhaust pressure of the heaviest beat, in bar: a long cutoff at
        /// full steam, slogging up a grade (the stock exhaust pressure is the
        /// cutoff times the steam's admission pressure, so it reads the steam
        /// each beat carries). Beats past the hardest swell toward it.
        /// </summary>
        public float HeavyBeatPressure { get; set; } = 11f;

        /// <summary>Multiplier on how much more a heavy beat throws than a hard one (0 none).</summary>
        public float HeavyVolume { get; set; } = 1f;

        /// <summary>Launch speed of the hardest beat's puff, in m/s; a drifting beat leaves at 3.</summary>
        public float LaunchSpeed { get; set; } = 18f;

        /// <summary>Multiplier on the particles per puff.</summary>
        public float PuffParticles { get; set; } = 1f;

        /// <summary>Multiplier on a puff's size once it has spread out.</summary>
        public float PuffSize { get; set; } = 1f;

        /// <summary>Opacity of a clean steam puff, 0 to 1.</summary>
        public float SteamOpacity { get; set; } = 0.7f;

        /// <summary>Multiplier on how quickly soot darkens the smoke.</summary>
        public float SootDarkness { get; set; } = 1f;

        /// <summary>Opacity of the haze from a clean fire between beats, 0 to 1.</summary>
        public float HazeOpacity { get; set; } = 0.25f;

        /// <summary>How far the hardest beat's jet carries before it spreads, in m.</summary>
        public float JetLength { get; set; } = 3.5f;

        /// <summary>Speed at which spread smoke rises, in m/s.</summary>
        public float RiseSpeed { get; set; } = 1.2f;

        /// <summary>Multiplier on sparks; at 1 the hardest beat under full draft throws about two.</summary>
        public float Sparks { get; set; } = 0.25f;
    }

    /// <summary>
    /// Maps engine and fire state to chimney smoke. Each beat throws a puff
    /// whose speed follows the exhaust's force; color comes from what leaves
    /// the stack: white exhaust steam in the puffs, a faint haze from a clean
    /// fire between them, both darkening toward a dark grey as soot builds.
    /// </summary>
    public static class PlumeModel
    {
        /// <summary>Below this exhaust pressure a beat has no force behind it, in bar.</summary>
        public const float MinBeatPressure = 1f;

        private static readonly PlumeColor Steam = new PlumeColor(0.93f, 0.93f, 0.93f, 1f);
        private static readonly PlumeColor CleanHaze = new PlumeColor(0.72f, 0.69f, 0.65f, 1f);
        private static readonly PlumeColor Sooty = new PlumeColor(0.16f, 0.15f, 0.14f, 1f);

        /// <summary>0 for a drifting beat to 1 for the hardest.</summary>
        public static float BeatStrength(PlumeTuning tuning, float exhaustPressure) =>
            Clamp01((exhaustPressure - MinBeatPressure) / Math.Max(0.1f, tuning.MaxBeatPressure - MinBeatPressure));

        // What the firebox makes leaves by the stack: a firebox puff makes
        // this many of the stack's (they are bigger and live longer). Thick
        // smoke-out darkens the smoke fully and the fire's gas this much, the
        // more so the more of it there is, fully at this many puffs a second.
        private const float StackPuffsPerFireboxPuff = 0.15f;
        private const float GasDarkness = 0.35f;
        private const float ThickSmokeRate = 40f;

        /// <summary>
        /// How sooty the firebox's smoke makes what leaves the stack, 0 to 1:
        /// the share of it that is thick smoke-out (the gas counting for
        /// less), scaled by how much smoke there is.
        /// </summary>
        public static float FireboxSoot(PlumeInputs inputs)
        {
            var gas = Math.Max(0f, inputs.FireboxGas);
            var smokeOut = Math.Max(0f, inputs.FireboxSmokeOut);
            var total = gas + smokeOut;
            if (!inputs.FireOn || total <= 0f)
                return 0f;
            return Clamp01((smokeOut + GasDarkness * gas) / total * Clamp01(total / ThickSmokeRate));
        }

        private static float Soot(PlumeInputs inputs) =>
            inputs.FireOn ? Clamp01(Math.Max(inputs.Soot, FireboxSoot(inputs))) : 0f;

        // A heaviest beat throws this many times more particles, this much
        // bigger, living this much longer than a hard one (at HeavyVolume 1).
        private const float HeavyParticles = 1.5f;
        private const float HeavySize = 0.6f;
        private const float HeavyLifetime = 0.5f;

        /// <summary>How far a beat is past the hardest toward the heaviest, 0 to 1.</summary>
        public static float Heaviness(PlumeTuning tuning, float exhaustPressure) =>
            Clamp01((exhaustPressure - tuning.MaxBeatPressure) / Math.Max(0.5f, tuning.HeavyBeatPressure - tuning.MaxBeatPressure));

        public static Puff ForBeat(PlumeTuning tuning, PlumeInputs inputs)
        {
            var strength = BeatStrength(tuning, inputs.ExhaustPressure);
            var heavy = Heaviness(tuning, inputs.ExhaustPressure) * Math.Max(0f, tuning.HeavyVolume);
            var soot = Soot(inputs);
            var flow = Clamp01(inputs.TotalFlowNormalized);
            var darkness = Darkness(tuning, soot);
            return new Puff
            {
                // The blast's momentum, spent within the first few metres.
                Speed = 3f + (tuning.LaunchSpeed - 3f) * (float)Math.Sqrt(strength),
                Size = Lerp(1.4f, 2.8f, strength) * (1f + HeavySize * heavy) * tuning.PuffSize,
                Particles = (int)Math.Round(Lerp(5f, 12f, strength) * (1f + HeavyParticles * heavy) * tuning.PuffParticles),
                SootParticles = soot > 0.4f
                    ? (int)Math.Round(3f * (soot - 0.4f) / 0.6f * Lerp(0.5f, 1f, strength) * (1f + heavy) * tuning.PuffParticles)
                    : 0,
                LifetimeScale = 1f + HeavyLifetime * heavy,
                Sparks = inputs.FireOn ? tuning.Sparks * 2f * Math.Max(0f, strength - 0.7f) / 0.3f * flow : 0f,
                Color = Mix(Steam, Sooty, darkness, Lerp(tuning.SteamOpacity, 0.8f, darkness)),
            };
        }

        /// <summary>
        /// Linear drag on smoke, per second: it stops the hardest beat's
        /// puff, launched at <see cref="PlumeTuning.LaunchSpeed"/>, after
        /// <see cref="PlumeTuning.JetLength"/> metres.
        /// </summary>
        public static float JetDrag(PlumeTuning tuning) => tuning.LaunchSpeed / Math.Max(0.1f, tuning.JetLength);

        /// <summary>
        /// Drag on a cinder, per second. A coal fragment is heavy for its
        /// size, so the air slows it far less than smoke and gravity wins:
        /// it arcs up and falls back.
        /// </summary>
        public const float SparkDrag = 0.8f;

        public const float Gravity = 9.81f;

        /// <summary>A cinder rides the blast and leaves a little faster than the smoke around it, in m/s.</summary>
        public static float SparkLaunchSpeed(Puff puff) => 1.2f * puff.Speed;

        /// <summary>A cinder's upward speed after <paramref name="dt"/> seconds of drag and gravity.</summary>
        public static float SparkVelocityAfter(float upwardSpeed, float dt) =>
            upwardSpeed * (float)Math.Exp(-SparkDrag * dt) - Gravity * dt;

        /// <summary>Upward acceleration on smoke, in m/s², that balances the drag at the rise speed.</summary>
        public static float Lift(PlumeTuning tuning) => tuning.RiseSpeed * JetDrag(tuning);

        // The blower at full sends about a tenth of the full-work exhaust flow
        // up the stack (the S282's and Hudson's blowers, 1.1 kg/s and up).
        private const float FullBlowerFlow = 0.1f;

        // A lit fire's lazy column, and the blower's steady stream on top, in
        // particles a second.
        private const float StandingRate = 3f;
        private const float BlowerRate = 12f;

        /// <summary>
        /// What leaves the stack between beats: a lit fire always sends a lazy
        /// column up by its own draft, faint when clean, fuller and darker as
        /// the firebox smokes (a fresh charge's smoke-out darkest); steam
        /// through the stack (the blower, mostly) makes a steady, faster,
        /// whiter stream on top of it.
        /// </summary>
        public static Haze Between(PlumeTuning tuning, PlumeInputs inputs)
        {
            var steam = Clamp01(Clamp01(inputs.TotalFlowNormalized) / FullBlowerFlow);
            var darkness = Darkness(tuning, Soot(inputs));
            var fireboxSmoke = inputs.FireOn ? Math.Max(0f, inputs.FireboxGas) + Math.Max(0f, inputs.FireboxSmokeOut) : 0f;
            var clean = Mix(CleanHaze, Steam, steam, 1f);
            var opacity = Lerp(Lerp(tuning.HazeOpacity, tuning.SteamOpacity, steam), 0.7f, darkness);
            return new Haze
            {
                Rate = (inputs.FireOn ? StandingRate : 0f) + StackPuffsPerFireboxPuff * fireboxSmoke + BlowerRate * steam,
                Speed = Lerp(1.2f, 8f, steam),
                Color = Mix(clean, Sooty, darkness, opacity),
            };
        }

        // Light soot only greys the smoke; it takes heavy soot to darken it.
        private static float Darkness(PlumeTuning tuning, float soot) =>
            Clamp01(tuning.SootDarkness * (float)Math.Pow(soot, 1.3));

        private static PlumeColor Mix(PlumeColor clean, PlumeColor dirty, float t, float alpha) =>
            new PlumeColor(Lerp(clean.R, dirty.R, t), Lerp(clean.G, dirty.G, t), Lerp(clean.B, dirty.B, t), alpha);

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
    }
}
