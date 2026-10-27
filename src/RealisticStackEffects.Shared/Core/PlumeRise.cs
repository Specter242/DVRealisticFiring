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
            var width = PlumeWidth(height, spreadWidth);
            return width <= RetireFrom ? 1f : (float)Math.Pow(RetireFrom / width, 4.0 / 3.0);
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

        /// <summary>The opacity of <paramref name="depth"/> (at the stack) thinned to <paramref name="height"/> and spread to <paramref name="spreadWidth"/>.</summary>
        public static float OpacityOf(float depth, float height, float spreadWidth = 0f) =>
            1f - (float)Math.Exp(-Math.Max(0f, depth) * Dilution(height, spreadWidth));
    }
}
