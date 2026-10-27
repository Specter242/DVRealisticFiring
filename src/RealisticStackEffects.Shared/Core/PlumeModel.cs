using System;

namespace RealisticStackEffects.Core
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

        /// <summary>
        /// How clear the stack is between beats, 0 to 1: 0 shows a clean
        /// fire's haze, 1 none (an oil fire with too much air).
        /// </summary>
        public float ClearStack;

        /// <summary>
        /// How much of a well-set fire's haze rides in the beats' steam, 0 to
        /// 1: an oil fire's light haze leaves with its exhaust, and a coal
        /// fire's fine ash and light smoke with its, both the tuning's
        /// (<see cref="PlumeModel.WellFiredHaze"/>).
        /// </summary>
        public float PuffHaze;

        /// <summary>The fire's heat over the most it makes, 0 to 1: how hot the gas leaving the stack is.</summary>
        public float HeatShare;

        private float? steamWhiteness;
        private float? waterWhiteness;

        /// <summary>
        /// How white the weather lets the exhaust's and blower's steam be
        /// against the reference air (<see cref="SteamCloud.Whiteness"/>):
        /// 0 in hot, dry air, about 3 in bitter cold. 1 unless set.
        /// </summary>
        public float SteamWhiteness
        {
            get => steamWhiteness ?? 1f;
            set => steamWhiteness = value;
        }

        /// <summary>As <see cref="SteamWhiteness"/>, for the water a clean fire's own burning sends up its stack. 1 unless set.</summary>
        public float WaterWhiteness
        {
            get => waterWhiteness ?? 1f;
            set => waterWhiteness = value;
        }

        private float? blowerWhiteness;

        /// <summary>
        /// As <see cref="SteamWhiteness"/>, for the steady stream between
        /// beats, the blower's mostly: its narrow jet leaves with less of the
        /// flue gas (<see cref="PlumeTuning.BlowerFlueGasMixing"/>). The
        /// exhaust's unless set.
        /// </summary>
        public float BlowerWhiteness
        {
            get => blowerWhiteness ?? SteamWhiteness;
            set => blowerWhiteness = value;
        }
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

        /// <summary>The share of its thickness that is smoke, the rest steam that evaporates (<see cref="PlumeRise"/>).</summary>
        public float SmokeShare;

        /// <summary>The color of its smoke, which it turns as its white steam evaporates.</summary>
        public PlumeColor SmokeColor;
    }

    /// <summary>The steady plume between beats, from the blower and natural draft.</summary>
    public struct Haze
    {
        /// <summary>Particles per second.</summary>
        public float Rate;

        /// <summary>Launch speed, in m/s.</summary>
        public float Speed;

        public PlumeColor Color;

        /// <summary>The share of its thickness that is smoke, the rest steam that evaporates (<see cref="PlumeRise"/>).</summary>
        public float SmokeShare;

        /// <summary>The color of its smoke, which it turns as its white steam evaporates.</summary>
        public PlumeColor SmokeColor;
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

        /// <summary>
        /// How steeply the stack's soot darkens its smoke toward black at
        /// 0.5: its share to this power (1.3 until 2026-10-21, when a
        /// well-fired engine's slight soot still drew a grey trail in hot,
        /// dry air), so light soot greys it little.
        /// </summary>
        public float SootCurve { get; set; } = 1.8f;

        /// <summary>
        /// The opacity of a puff of black smoke, 0 to 1, against a clean
        /// steam puff's <see cref="SteamOpacity"/> (0.8 until 2026-10-21,
        /// when its smoke came to be shared by the particles overlapping it;
        /// 0.9 until 2026-10-22, when heavy coal smoke in the photos stayed
        /// opaque 30 m up where ours went see-through gray).
        /// </summary>
        public float SootOpacity { get; set; } = 0.97f;

 public float HazeOpacity { get; set; } = 0.15f;

        /// <summary>How far the hardest beat's jet carries before it spreads, in m.</summary>
        public float JetLength { get; set; } = 3.5f;

        /// <summary>
        /// Multiplier on the plume's buoyancy, the stack's heat
        /// (<see cref="PlumeRise"/>): spread smoke rises with it, slowing
        /// with height but never stopping.
        /// </summary>
        public float Buoyancy { get; set; } = 1f;

 public float FlueGasMixing { get; set; } = 0.6f;

 public float BlowerFlueGasMixing { get; set; } = 0.2f;

        /// <summary>Multiplier on how fast the exhaust mixes into the air, and so how soon its white evaporates.</summary>
        public float SteamMixing { get; set; } = 3f;

 public float SteamForming { get; set; } = 1f;

        /// <summary>The share of the train's speed its wake carries the plume's base with at the stack's lip (<see cref="PlumeRise.Wake(float, float, float)"/>).</summary>
        public float WakeCarry { get; set; } = PlumeRise.WakeCarry;

        /// <summary>The height the train's wake falls away over, in m.</summary>
        public float WakeDepth { get; set; } = PlumeRise.WakeDepth;

 public float BeatHaze { get; set; } = 0.5f;

 public float TrailSpread { get; set; } = 1f;

        /// <summary>Multiplier on sparks; at 1 the hardest beat under full draft throws about two.</summary>
        public float Sparks { get; set; } = 0.25f;

        /// <summary>Multiplier on the heat shimmer over the stack (0 none).</summary>
        public float Shimmer { get; set; } = 1f;
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

        /// <summary>White exhaust steam: what a parcel's steam shows as.</summary>
        public static readonly PlumeColor Steam = new PlumeColor(0.93f, 0.93f, 0.93f, 1f);
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
            var haze = Clamp01(inputs.PuffHaze);
            // A puff is exhaust steam, darkened by the soot it carries and
            // tinted by the haze, which stays as the steam evaporates.
            var smokeColor = SmokeOf(darkness, (1f - darkness) * haze * PuffHazeSmoke);
            var (alpha, smokeShare) = Weathered(Lerp(tuning.SteamOpacity * ClearSteam(inputs), tuning.SootOpacity, darkness),
                darkness + (1f - darkness) * haze * PuffHazeSmoke, inputs.SteamWhiteness);
            return new Puff
            {
                Speed = PuffSpeed(tuning, strength),
                Size = Lerp(1.4f, 2.8f, strength) * (1f + HeavySize * heavy) * tuning.PuffSize,
                Particles = (int)Math.Round(Lerp(5f, 12f, strength) * (1f + HeavyParticles * heavy) * tuning.PuffParticles),
                // A dark puff carries a denser, darker core.
                SootParticles = darkness > SootCoreFrom
                    ? (int)Math.Round(3f * (darkness - SootCoreFrom) / (1f - SootCoreFrom) * Lerp(0.5f, 1f, strength) * (1f + heavy) * tuning.PuffParticles)
                    : 0,
                LifetimeScale = 1f + HeavyLifetime * heavy,
                Sparks = inputs.FireOn ? tuning.Sparks * 2f * Math.Max(0f, strength - 0.7f) / 0.3f * flow : 0f,
                Color = Tinted(smokeShare, smokeColor, alpha),
                SmokeShare = smokeShare,
                SmokeColor = smokeColor,
            };
        }

        /// <summary>
        /// A beat's launch speed, in m/s, for its strength: the blast's
        /// momentum, spent within the first few metres.
        /// </summary>
        public static float PuffSpeed(PlumeTuning tuning, float strength) =>
            3f + (tuning.LaunchSpeed - 3f) * (float)Math.Sqrt(Clamp01(strength));

        /// <summary>The share of the blower's full flow going up the stack, 0 to 1.</summary>
        public static float SteamThroughStack(float totalFlowNormalized) => Clamp01(Clamp01(totalFlowNormalized) / FullBlowerFlow);

        /// <summary>The launch speed between beats, in m/s: a lazy draft, faster with the blower's steam.</summary>
        public static float HazeSpeed(float steam) => Lerp(1.2f, 8f, Clamp01(steam));

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

        // The blower at full sends about a tenth of the full-work exhaust flow
        // up the stack (the S282's and Hudson's blowers, 1.1 kg/s and up).
        private const float FullBlowerFlow = 0.1f;

        private const float CleanHazeSmoke = 0.35f;

        private const float PuffHazeSmoke = 0.7f;

        private const float ClearStackSteamThinning = 0.5f;

        private static float ClearSteam(PlumeInputs inputs) => 1f - ClearStackSteamThinning * Clamp01(inputs.ClearStack);

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
            var steam = SteamThroughStack(inputs.TotalFlowNormalized);
            var darkness = Darkness(tuning, Soot(inputs));
            var fireboxSmoke = inputs.FireOn ? Math.Max(0f, inputs.FireboxGas) + Math.Max(0f, inputs.FireboxSmokeOut) : 0f;
            var haze = tuning.HazeOpacity * (1f - Clamp01(inputs.ClearStack));
            // The blower's stream is steam; a clean fire's own haze is part
            // smoke, part the water its burning makes, each as white as the
            // weather lets it be.
            var water = (1f - steam) * (1f - CleanHazeSmoke);
            var whiteness = water + steam > 1e-4f
                ? (water * inputs.WaterWhiteness + steam * inputs.BlowerWhiteness) / (water + steam)
                : inputs.BlowerWhiteness;
            var (opacity, smokeShare) = Weathered(Lerp(Lerp(haze, tuning.SteamOpacity * ClearSteam(inputs), steam), 0.7f, darkness),
                darkness + (1f - darkness) * (1f - steam) * CleanHazeSmoke, whiteness);
            var smokeColor = SmokeOf(darkness, (1f - darkness) * (1f - steam) * CleanHazeSmoke);
            return new Haze
            {
                Rate = (inputs.FireOn ? StandingRate : 0f) + StackPuffsPerFireboxPuff * fireboxSmoke + BlowerRate * steam,
                Speed = HazeSpeed(steam),
                Color = Tinted(smokeShare, smokeColor, opacity),
                SmokeShare = smokeShare,
                SmokeColor = smokeColor,
            };
        }

 public static float SmokeTint(float smokeShare) => (float)Math.Sqrt(Clamp01(smokeShare));

        // A parcel's color: white steam tinted by its smoke.
        private static PlumeColor Tinted(float smokeShare, PlumeColor smokeColor, float alpha) =>
            Mix(Steam, smokeColor, SmokeTint(smokeShare), alpha);

        /// <summary>
        /// A parcel of <paramref name="opacity"/> in the reference air,
        /// <paramref name="smokeShare"/> of its thickness smoke, in air that
        /// makes its steam <paramref name="whiteness"/> as white: the steam's
        /// optical depth scaled, the smoke's kept. Returns its opacity and
        /// its smoke's share of its new thickness.
        /// </summary>
        public static (float Opacity, float SmokeShare) Weathered(float opacity, float smokeShare, float whiteness)
        {
            var depth = -(float)Math.Log(1f - Math.Min(0.98f, Math.Max(0f, opacity)));
            var share = Clamp01(smokeShare);
            var smoke = depth * share;
            var steam = depth * (1f - share) * Math.Max(0f, whiteness);
            var total = smoke + steam;
            return (1f - (float)Math.Exp(-total), total > 1e-6f ? smoke / total : share);
        }

        // A coal fire sends up no smoke to see under this soot, and clears
        // its stack fully with this share of the air through it passing
        // unburned beyond the design's 20 % excess.
        private const float CleanSoot = 0.05f;
        private const float ClearingAir = 0.25f;

 public static float CoalClearStack(float soot, float unburnedAirShare) =>
            Clamp01(1f - Math.Max(0f, soot) / CleanSoot) * Clamp01(unburnedAirShare / ClearingAir);

        // The shimmer shows from a fire at this share of its heat on, fully
        // at this; the blower's steam and a hard beat's hide most of it.
        private const float ShimmerFrom = 0.02f;
        private const float ShimmerFull = 0.3f;
        private const float SteamHidesShimmer = 0.7f;
        private const float BeatHidesShimmer = 0.8f;

        /// <summary>
        /// The haze a fire's beats carry (<see cref="PlumeInputs.PuffHaze"/>),
        /// coal or oil: the tuning's, none from a clear stack (no smoke to see
        /// and air to spare).
        /// </summary>
        public static float WellFiredHaze(PlumeTuning tuning, float clearStack) =>
            Clamp01(tuning.BeatHaze) * (1f - Clamp01(clearStack));

 public static float Shimmer(PlumeTuning tuning, PlumeInputs inputs)
        {
            if (!inputs.FireOn)
                return 0f;
            var hot = Clamp01((inputs.HeatShare - ShimmerFrom) / (ShimmerFull - ShimmerFrom));
            var clean = 1f - Hiding(tuning, Soot(inputs));
            var white = Clamp01(inputs.SteamWhiteness);
            var steam = SteamThroughStack(inputs.TotalFlowNormalized) * white;
            var beat = BeatStrength(tuning, inputs.ExhaustPressure) * white;
            return Clamp01(Math.Max(0f, tuning.Shimmer) * hot * clean * (1f - SteamHidesShimmer * steam) * (1f - BeatHidesShimmer * beat));
        }

 public const float SootForBlack = 0.5f;

        // A puff this dark carries a darker core.
        private const float SootCoreFrom = 0.4f;

        // Light soot only greys the smoke; heavy soot blackens it, the more
        // steeply the higher the curve (PlumeTuning.SootCurve).
        private static float Darkness(PlumeTuning tuning, float soot) =>
            Clamp01(tuning.SootDarkness * (float)Math.Pow(Math.Max(0f, soot) / SootForBlack, Math.Max(0.5f, tuning.SootCurve)));

        // How far the stack's soot hides the heat shimmer: on the darkening's
        // curve before it steepened (2026-10-21), which was for how grey a
        // well-fired engine's smoke draws, not for what it hides.
        private const float ShimmerSootCurve = 1.3f;

        private static float Hiding(PlumeTuning tuning, float soot) =>
            Clamp01(tuning.SootDarkness * (float)Math.Pow(Math.Max(0f, soot) / SootForBlack, ShimmerSootCurve));

        // What a parcel's smoke looks like once its steam has gone: its soot
        // and its haze, by their shares of its thickness.
        private static PlumeColor SmokeOf(float soot, float haze)
        {
            var total = soot + haze;
            return total > 1e-4f ? Mix(CleanHaze, Sooty, soot / total, 1f) : Sooty;
        }

        private static PlumeColor Mix(PlumeColor clean, PlumeColor dirty, float t, float alpha) =>
            new PlumeColor(Lerp(clean.R, dirty.R, t), Lerp(clean.G, dirty.G, t), Lerp(clean.B, dirty.B, t), alpha);

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
    }
}
