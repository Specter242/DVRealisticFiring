using System;

namespace RealisticStackEffects.Core
{
    /// <summary>
    /// What a parcel of the stack's smoke carries: how thick its steam and
    /// its smoke are, as optical depths at the stack. The steam evaporates
    /// as it mixes with the air; the smoke only spreads.
    /// </summary>
    public struct PlumeParcel
    {
        public float SteamDepth;
        public float SmokeDepth;
    }

 public static class PlumeRise
    {
        /// <summary>
        /// How much sooner a puff becomes a wisp so a moving stack leaves no
        /// more than <paramref name="trailMeters"/> of rounded trail. The
        /// stationary handoff is a share of its flipbook's playing time.
        /// </summary>
        public static float PuffAgeScale(float speed, float playingTime, float endAge, float trailMeters)
        {
            if (!(speed > 0f) || !(playingTime > 0f) || !(endAge > 0f) || !(trailMeters > 0f))
                return 1f;
            return Math.Min(1f, trailMeters / (speed * playingTime * endAge));
        }

        /// <summary>The share of the fire's heat that leaves by the stack in the flue gas, at about 300 °C.</summary>
        public const float FlueHeatShare = 0.17f;

        /// <summary>
        /// The exhaust's and blower's steam is as buoyant as this much heat
        /// per kg, in J: at 100 °C it is half as dense as the air (0.3 MJ),
        /// and as it condenses into the white cloud its latent heat (2.26 MJ)
        /// warms the plume until the cloud dries, a share of it counted.
        /// </summary>
        public const float SteamBuoyancy = 0.8e6f;

        private const float Gravity = 9.81f;
        private const float AirDensity = 1.2f;
        private const float AirHeatCapacity = 1005f;
        private const float AirTemperature = 288f;

        // A plume's speed in still air, 3 (F/z)^(1/3) (a top-hat plume with
        // entrainment 0.1 gives 2.6, the centerline 4.7), and bent over in
        // moving air, 1.35 (F/U)^(1/2) z^(-1/2) (from Briggs' rise, 1.6
        // F^(1/3) x^(2/3) / U). Heights count from this far below the lip,
        // where the plume would start from a point, in m.
        private const float CalmRise = 3f;
        private const float BentRise = 1.35f;
        private const float VirtualSource = 3f;

        // Air slower than this past a plume does not bend it over (a calm,
        // as dispersion models take it), in m/s; a light breeze bent a
        // standing engine's plume over from the stack.
        private const float CalmAir = 1f;

 public const float WakeCarry = 0.3f;

        /// <summary>The height over which the train's wake falls away, m.</summary>
        public const float WakeDepth = 1.5f;

        /// <summary>The height above the stack lip over which slipstream downwash decays, m.</summary>
        public const float DownwashDepth = 2f;

        /// <summary>
        /// Lowest height relative to the stack lip at which slipstream downwash
        /// acts, m (boiler-top / cab-roof level): below this downwash is zero so
        /// soft smoke rolls along the boiler instead of diving through it.
        /// </summary>
        public const float DownwashFloor = -1f;

        /// <summary>Maximum gentle downward velocity pulled into the smokebox wake, m/s.</summary>
        public const float MaxDownwash = 2.2f;

        /// <summary>
        /// Horizontal distance from the stack lip beyond which the locomotive's
        /// low-pressure wake ends and slipstream downwash is zero, in m.
        /// </summary>
        public const float DownwashWakeLength = 22f;

        /// <summary>
        /// Characteristic travel length over which parcel-age downwash fades out
        /// in the locomotive's wake (tau = DownwashWakeDecayLength / max(trainSpeed, 1)), in m.
        /// </summary>
        public const float DownwashWakeDecayLength = 15f;

        /// <summary>
        /// Minimum look-ahead step used to bound downward downwash velocity against
        /// <see cref="DownwashFloor"/>, in s, preventing inter-pass overshoot on
        /// delayed particle updates.
        /// </summary>
        public const float DownwashFloorHorizon = 0.5f;

        // Train speed and exhaust punch thresholds for slipstream downwash ("beating down").
        private const float MinDownwashSpeed = 3f;
        private const float FullDownwashSpeed = 18f;
        private const float WeakDownwashRatio = 0.15f;
        private const float ClearDownwashRatio = 0.65f;
        private const float WeakDownwashPunch = 3f;
        private const float ClearDownwashPunch = 10f;

        // Diurnal thermal inversion vertical transition and lateral flattening scales.
        private const float InversionSoftBand = 6f;
        private const float InversionDecayDepth = 8f;
        private const float InversionSpreadShare = 0.45f;

 public const float WidthPerHeight = 0.3f;

        // Seen across, the plume thins as its height to the two-thirds once
        // it has formed, above this (from the virtual source), in m; below,
        // the beats' puffs are still whole.
        private const float DilutionFrom = 10f;

        // Particles are retired as the plume widens so that the survivors, as
        // wide as the plume, still overlap: all of them up to this width, in m
        // (the width 9 m up).
        private const float RetireFrom = WidthPerHeight * 12f;

        // The air's turbulence spreads a parcel this fast, in m/s of width, in
        // still air, and this much faster for every m/s of air past it; a
        // trail spread wider than this thins as its width.
        private const float StillSpread = 0.4f;
        private const float SpreadPerAirSpeed = 0.08f;
        private const float SpreadThinsFrom = 2.5f;

        /// <summary>A parcel thinner than this is gone.</summary>
        public const float Invisible = 0.01f;

        /// <summary>The buoyancy flux of a plume carrying <paramref name="heat"/> W, in m⁴/s³.</summary>
        public static float BuoyancyFlux(float heat) =>
            Gravity * Math.Max(0f, heat) / (AirDensity * AirHeatCapacity * AirTemperature);

        /// <summary>
        /// The buoyancy flux of the stack of a fire making <paramref name="fireHeat"/>
        /// W, with <paramref name="steamFlow"/> kg/s of exhaust and blower steam.
        /// </summary>
        public static float StackFlux(float fireHeat, float steamFlow) =>
            BuoyancyFlux(FlueHeatShare * Math.Max(0f, fireHeat) + SteamBuoyancy * Math.Max(0f, steamFlow));

        /// <summary>
        /// The plume's rise speed <paramref name="height"/> m above the stack,
        /// in m/s, with the air moving past it at <paramref name="airSpeed"/>
        /// m/s: the slower of the still-air and bent-over plume, the still
        /// air's × <paramref name="calmShare"/> (the speed its visible
        /// surface shows, where the bent-over plume's trajectory is the
        /// air's and its rise's together and is kept).
        /// </summary>
        public static float RiseSpeed(float flux, float height, float airSpeed, float calmShare = 1f)
        {
            if (!(flux > 0f))
                return 0f;
            var z = Math.Max(0f, height) + VirtualSource;
            var calm = CalmRise * Math.Max(0f, calmShare) * (float)Math.Pow(flux / z, 1.0 / 3.0);
            var bending = airSpeed - CalmAir;
            if (!(bending > 0.1f))
                return calm;
            var bent = BentRise * (float)Math.Sqrt(flux / bending / z);
            return Math.Min(calm, bent);
        }

        /// <summary>
        /// As <see cref="RiseSpeed(float, float, float, float)"/>, damped by a
        /// diurnal thermal inversion layer at <paramref name="inversionCeiling"/> m
        /// with <paramref name="inversionStrength"/> (0 none to 1 calm dawn).
        /// </summary>
        public static float RiseSpeed(float flux, float height, float airSpeed, float calmShare, float inversionCeiling, float inversionStrength) =>
            RiseSpeed(flux, height, airSpeed, calmShare) * InversionDamping(height, inversionCeiling, inversionStrength);

        /// <summary>
        /// Vertical rise multiplier (0 to 1) from a diurnal thermal inversion
        /// layer at <paramref name="ceiling"/> m above the stack with
        /// <paramref name="strength"/> (0 to 1): 1 below the stable layer or
        /// when no inversion applies, easing smoothly toward 0 aloft so a calm
        /// dawn's plume levels off into a hanging layer.
        /// </summary>
        public static float InversionDamping(float height, float ceiling, float strength)
        {
            if (!(strength > 0f) || float.IsInfinity(ceiling) || !(ceiling > 0f))
                return 1f;
            var s = strength > 1f ? 1f : strength;
            var onset = Math.Max(0f, ceiling - InversionSoftBand);
            var over = height - onset;
            if (!(over > 0f))
                return 1f;
            return (float)Math.Exp(-s * over * over / (InversionDecayDepth * (over + InversionDecayDepth)));
        }

        /// <summary>
        /// Extra horizontal spreading rate, in m/s, when an inversion slows
        /// vertical rise: suppressed buoyancy spreads laterally into a flat layer
        /// without accelerating dilution or retirement.
        /// </summary>
        public static float InversionSpread(float undampedRise, float damping) =>
            InversionSpreadShare * Math.Max(0f, undampedRise) * (1f - Math.Min(1f, Math.Max(0f, damping)));

        /// <summary>
        /// Accumulates lateral width gained under a thermal inversion ceiling, in m,
        /// from <paramref name="inversionSpreadWidth"/> m over <paramref name="seconds"/> s
        /// at lateral spreading rate <paramref name="inversionSpread"/> m/s.
        /// Kept apart from <see cref="SpreadOn"/> so a calm-dawn capped layer expands
        /// horizontally without accelerating <see cref="Dilution(float, float)"/> or
        /// <see cref="SurvivalAt(float)"/>.
        /// </summary>
        public static float InversionSpreadOn(float inversionSpreadWidth, float seconds, float inversionSpread)
        {
            var current = inversionSpreadWidth > 0f && !float.IsInfinity(inversionSpreadWidth) ? inversionSpreadWidth : 0f;
            var rate = inversionSpread > 0f && !float.IsInfinity(inversionSpread) ? inversionSpread : 0f;
            var dt = seconds > 0f && !float.IsInfinity(seconds) ? seconds : 0f;
            var next = current + (double)dt * rate;
            return next >= float.MaxValue ? float.MaxValue : (float)next;
        }

        /// <summary>
        /// Lateral widening velocity scale, in m/s, combining buoyant expansion,
        /// ambient turbulence spread, and any inversion-driven horizontal flattening.
        /// </summary>
        public static float WideningRate(float rise, float airSpeed, float spreadScale = 1f, float inversionSpread = 0f) =>
            WidthPerHeight * Math.Max(0f, rise) + SpreadRate(airSpeed, spreadScale) + Math.Max(0f, inversionSpread);

        /// <summary>
        /// Frame-constant peak slipstream downwash velocity at the stack lip, in
        /// m/s (&gt;= 0), from train speed, exhaust exit speed, and buoyancy flux.
        /// Precompute once per frame and pass to
        /// <see cref="Downwash(float, float, float, float, float, float)"/>.
        /// </summary>
        public static float DownwashStrength(float trainSpeed, float exitSpeed, float flux)
        {
            if (!(trainSpeed > MinDownwashSpeed))
                return 0f;
            var speedFactor = SmoothStep(MinDownwashSpeed, FullDownwashSpeed, trainSpeed);
            var buoyancyPunch = flux > 0f ? 0.5f * (float)Math.Pow(flux, 1.0 / 3.0) : 0f;
            var punch = Math.Max(0f, exitSpeed) + buoyancyPunch;
            var ratio = punch / trainSpeed;
            var weakByRatio = 1f - SmoothStep(WeakDownwashRatio, ClearDownwashRatio, ratio);
            var weakByPunch = 1f - SmoothStep(WeakDownwashPunch, ClearDownwashPunch, punch);
            var weakExhaust = weakByRatio * weakByPunch;
            if (!(weakExhaust > 0f))
                return 0f;
            return Math.Min(MaxDownwash, MaxDownwash * speedFactor * weakExhaust);
        }

        /// <summary>
        /// Downward velocity, in m/s (&gt;= 0, subtracted from vertical rise),
        /// pulled into the locomotive's low-pressure wake ("smoke beating down"):
        /// confined to <see cref="DownwashWakeLength"/> m horizontally from the
        /// stack in either travel direction and decaying with parcel age,
        /// decaying rapidly with height, and bounded so a step of
        /// <paramref name="stepDt"/> s (at least <see cref="DownwashFloorHorizon"/> s)
        /// cannot carry smoke below <see cref="DownwashFloor"/>.
        /// </summary>
        public static float Downwash(float strength, float height, float horizontalDistance, float age, float trainSpeed, float stepDt = DownwashFloorHorizon)
        {
            if (!(strength > 0f) || !(height > DownwashFloor))
                return 0f;
            if (!(horizontalDistance < DownwashWakeLength) || float.IsNaN(horizontalDistance))
                return 0f;
            var dist = Math.Max(0f, horizontalDistance);
            var distanceFactor = 1f - SmoothStep(0f, DownwashWakeLength, dist);
            if (!(distanceFactor > 0f))
                return 0f;
            var tau = DownwashWakeDecayLength / Math.Max(trainSpeed, 1f);
            var ageFactor = age > 0f ? (float)Math.Exp(-age / tau) : 1f;
            var heightFactor = height >= 0f
                ? (float)Math.Exp(-height / DownwashDepth)
                : SmoothStep(DownwashFloor, 0f, height);
            var raw = Math.Min(MaxDownwash, strength * distanceFactor * ageFactor * heightFactor);
            var horizon = Math.Max(float.IsNaN(stepDt) ? DownwashFloorHorizon : stepDt, DownwashFloorHorizon);
            var maxAllowed = (height - DownwashFloor) / horizon;
            return Math.Min(raw, maxAllowed);
        }

        /// <summary>
        /// Convenience overload of <see cref="Downwash(float, float, float, float, float, float)"/>
        /// computing <see cref="DownwashStrength"/> from train speed, exit speed, and buoyancy flux.
        /// </summary>
        public static float Downwash(float height, float trainSpeed, float exitSpeed, float flux,
            float horizontalDistance = 0f, float age = 0f, float stepDt = DownwashFloorHorizon) =>
            Downwash(DownwashStrength(trainSpeed, exitSpeed, flux), height, horizontalDistance, age, trainSpeed, stepDt);

        /// <summary>
        /// Bounds a parcel's vertical velocity <paramref name="verticalVelocity"/>
        /// (m/s) while inside the locomotive's downwash wake so a step of
        /// <paramref name="stepDt"/> s (at least <see cref="DownwashFloorHorizon"/> s)
        /// cannot carry the parcel below <see cref="DownwashFloor"/>.
        /// </summary>
        public static float ClampDownwashVertical(float verticalVelocity, float height, float stepDt = DownwashFloorHorizon)
        {
            if (verticalVelocity >= 0f)
                return verticalVelocity;
            if (!(height > DownwashFloor))
                return 0f;
            var horizon = Math.Max(float.IsNaN(stepDt) ? DownwashFloorHorizon : stepDt, DownwashFloorHorizon);
            var minVelocity = -(height - DownwashFloor) / horizon;
            return Math.Max(verticalVelocity, minVelocity);
        }

        private static float SmoothStep(float edge0, float edge1, float value)
        {
            var t = (value - edge0) / (edge1 - edge0);
            t = t < 0f ? 0f : t > 1f ? 1f : t;
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// The share of the train's speed the air it drags along carries the
        /// plume with, <paramref name="height"/> m above the stack: it lifts
        /// over the train before it streams back.
        /// </summary>
        public static float Wake(float height) => Wake(height, WakeCarry, WakeDepth);

        /// <summary>
        /// The share of the train's speed its wake carries the plume with,
        /// <paramref name="height"/> m above the stack: <paramref name="carry"/>
        /// at the lip, falling away over <paramref name="depth"/> m.
        /// </summary>
        public static float Wake(float height, float carry, float depth) =>
            Math.Max(0f, carry) * (float)Math.Exp(-Math.Max(0f, height) / Math.Max(0.05f, depth));

        /// <summary>The plume's width <paramref name="height"/> m above the stack, in m.</summary>
        public static float Width(float height) => WidthPerHeight * (Math.Max(0f, height) + VirtualSource);

        /// <summary>
        /// How fast the air's turbulence spreads a parcel, in m/s of width,
        /// with the air going past at <paramref name="airSpeed"/> m/s, ×
        /// <paramref name="scale"/> (<see cref="PlumeTuning.TrailSpread"/>).
        /// </summary>
        public static float SpreadRate(float airSpeed, float scale = 1f) =>
            (StillSpread + SpreadPerAirSpeed * Math.Max(0f, airSpeed)) * Math.Max(0f, scale);

        /// <summary>
        /// How wide the air's turbulence has spread a parcel <paramref name="age"/>
        /// s old, with the air going past at <paramref name="airSpeed"/> m/s, in m.
        /// </summary>
        public static float SpreadWidth(float age, float airSpeed, float scale = 1f) => SpreadRate(airSpeed, scale) * Math.Max(0f, age);

        /// <summary>
        /// A parcel spread <paramref name="spreadWidth"/> m wide, spread on for
        /// <paramref name="seconds"/> s more with the air going past at
        /// <paramref name="airSpeed"/> m/s, in m. What the air has spread
        /// stays spread when it slows (loady740, Nexus, 2026-10-27: a line of
        /// puffs hung where the train had run; the spread was the air's speed
        /// now over the parcel's whole age, so a train that stopped drew its
        /// trail back in, narrower and thicker, and kept it).
        /// </summary>
        public static float SpreadOn(float spreadWidth, float seconds, float airSpeed, float scale = 1f) =>
            Math.Max(0f, spreadWidth) + SpreadWidth(seconds, airSpeed, scale);

        /// <summary>The plume's width at a parcel: its rise's and its spread's together, in m.</summary>
        public static float PlumeWidth(float height, float spreadWidth)
        {
            var rising = Width(height);
            return (float)Math.Sqrt(rising * rising + spreadWidth * spreadWidth);
        }

        /// <summary>How thick the plume is seen across at <paramref name="height"/>, against at the stack, 0 to 1.</summary>
        public static float Dilution(float height) => Dilution(height, 0f);

        /// <summary>
        /// How thick a parcel is seen across, against at the stack, 0 to 1:
        /// thinned by its rise (as its height to the two-thirds) or by the
        /// turbulence's spread (a trail of it as its width), whichever more.
        /// </summary>
        public static float Dilution(float height, float spreadWidth)
        {
            var z = Math.Max(0f, height) + VirtualSource;
            var rising = z <= DilutionFrom ? 1f : (float)Math.Pow(DilutionFrom / z, 2.0 / 3.0);
            var spreading = spreadWidth <= SpreadThinsFrom ? 1f : SpreadThinsFrom / spreadWidth;
            return Math.Min(rising, spreading);
        }

        /// <summary>The share of particles kept at <paramref name="height"/>, unspread.</summary>
        public static float Survival(float height) => Survival(height, 0f);

        /// <summary>
        /// The share of particles kept where the plume is as wide as its
        /// height or its spread make it: as it widens its particles grow with
        /// it, and fewer are needed to cover it (the survivors' spacing grows
        /// as their width).
        /// </summary>
        public static float Survival(float height, float spreadWidth)
        {
            return SurvivalAt(PlumeWidth(height, spreadWidth));
        }

        /// <summary>
        /// A parcel leaving the stack with opacity <paramref name="opacity"/>,
        /// <paramref name="smokeShare"/> of it smoke and the rest steam.
        /// </summary>
        public static PlumeParcel Parcel(float opacity, float smokeShare)
        {
            var depth = -(float)Math.Log(1f - Math.Min(0.98f, Math.Max(0f, opacity)));
            var smoke = Math.Min(1f, Math.Max(0f, smokeShare));
            return new PlumeParcel { SteamDepth = depth * (1f - smoke), SmokeDepth = depth * smoke };
        }

        /// <summary>
        /// The parcel's opacity <paramref name="age"/> s after it left the
        /// stack, now <paramref name="height"/> m above it, its steam's white
        /// <paramref name="cloud"/> evaporating as it mixes into the air going
        /// past at <paramref name="airSpeed"/> m/s (<see cref="SteamCloud"/>).
        /// </summary>
        public static float Opacity(PlumeParcel parcel, SteamCloud cloud, float age, float height, float airSpeed, float mixing = 1f, float forming = 1f) =>
            OpacityOf(SteamLeft(parcel, cloud, age, airSpeed, mixing, forming) + parcel.SmokeDepth, height);

        /// <summary>
        /// The parcel's white steam <paramref name="age"/> s on, formed and
        /// not yet evaporated (<see cref="SteamCloud.Left(float, float, float, float)"/>),
        /// as an optical depth at the stack.
        /// </summary>
        public static float SteamLeft(PlumeParcel parcel, SteamCloud cloud, float age, float airSpeed, float mixing = 1f, float forming = 1f) =>
            cloud == null ? parcel.SteamDepth : parcel.SteamDepth * cloud.Left(age, airSpeed, mixing, forming);

        /// <summary>
        /// As <see cref="SteamLeft(PlumeParcel, SteamCloud, float, float, float, float)"/>,
        /// its white formed no less than the jet's sheath has it at its
        /// height (<see cref="SteamCloud.Sheath"/>).
        /// </summary>
        public static float SteamLeft(PlumeParcel parcel, SteamCloud cloud, float age, float airSpeed, float mixing, float forming, float sheath) =>
            cloud == null ? parcel.SteamDepth : parcel.SteamDepth * cloud.Left(age, airSpeed, mixing, forming, sheath);

        /// <summary>The opacity of <paramref name="depth"/> (at the stack) thinned to <paramref name="height"/> and spread to <paramref name="spreadWidth"/>.</summary>
        public static float OpacityOf(float depth, float height, float spreadWidth = 0f) => OpacityAt(depth, Dilution(height, spreadWidth));

        /// <summary>The opacity of <paramref name="depth"/> (at the stack) where the plume is thinned by <paramref name="dilution"/> (<see cref="Dilution(float, float)"/>).</summary>
        public static float OpacityAt(float depth, float dilution) => 1f - (float)Math.Exp(-Math.Max(0f, depth) * dilution);

        /// <summary>The share of particles kept where the plume is <paramref name="width"/> m wide (<see cref="PlumeWidth"/>).</summary>
        public static float SurvivalAt(float width) => width <= RetireFrom ? 1f : (float)Math.Pow(RetireFrom / width, 4.0 / 3.0);
    }
}
