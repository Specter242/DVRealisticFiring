using System;

namespace RealisticStackEffects.Core
{
    /// <summary>The engine's steam other than the stack's: where it leaves and what it is like.</summary>
    public enum VentKind
    {
        /// <summary>The turbo-generator's exhaust pipe, a steady low-pressure stream.</summary>
        Dynamo,

        /// <summary>A cylinder's drain cock, blown open under steam: a hard, wet jet out to the side.</summary>
        CylinderCock,

        /// <summary>A safety valve lifting: the hardest jet on the engine, straight up.</summary>
        SafetyValve,

        /// <summary>The whistle's steam.</summary>
        Whistle,

        /// <summary>The blowdown: boiler water flashing to steam and spray.</summary>
        Blowdown,

        /// <summary>A cracked cylinder's steam, as the game shows a damaged engine's.</summary>
        CylinderCrack,

        /// <summary>A small leak at a joint or fitting.</summary>
        Leak,

        /// <summary>A piston rod's or valve stem's gland, weeping with each stroke.</summary>
        Gland,

        /// <summary>A drain or a weeping valve, barely a breath of steam: seen only in cold, damp air.</summary>
        Weep,

 FittingWeep,

        /// <summary>A steam-driven pump's exhaust, a puff a stroke.</summary>
        PumpExhaust,

        /// <summary>A booster engine's exhaust beside the trailing truck.</summary>
        BoosterExhaust,

        /// <summary>An injector's overflow: wet steam and spray spilling under the cab.</summary>
        Overflow,
    }

    /// <summary>A vent's steam with its source full on.</summary>
    public readonly struct VentSpec
    {
        public VentSpec(float flow, float bore, float speed, float wetness, float spread, float rate, float clear = 0f)
        {
            Clear = clear;
            Flow = flow;
            Bore = bore;
            Speed = speed;
            Wetness = wetness;
            Spread = spread;
            Rate = rate;
        }

        /// <summary>Its steam, kg/s.</summary>
        public float Flow { get; }

        /// <summary>
        /// The width of its jet once it has expanded to the air's pressure,
        /// m: a pipe's own bore, or for steam let go at pressure the bore
        /// that passes its flow at the speed it reaches.
        /// </summary>
        public float Bore { get; }

        /// <summary>The speed it leaves at, m/s.</summary>
        public float Speed { get; }

        /// <summary>The share of it that is water as it leaves, 0 to 1.</summary>
        public float Wetness { get; }

        /// <summary>How far its parcels' ways differ from its axis, radians.</summary>
        public float Spread { get; }

        /// <summary>Parcels drawn a second.</summary>
        public float Rate { get; }

 public float Clear { get; }

 public static VentSpec For(VentKind kind)
        {
            switch (kind)
            {
                case VentKind.Dynamo:
                    return new VentSpec(0.013f, 0.032f, 26f, 0.05f, 0.10f, 40f);
                case VentKind.CylinderCock:
                    return new VentSpec(0.10f, 0.022f, Sonic, 0.35f, 0.10f, 40f);
                case VentKind.SafetyValve:
                    return new VentSpec(2.5f, 0.11f, Sonic, 0f, 0.07f, 44f, clear: 7f);
                case VentKind.Whistle:
                    return new VentSpec(0.15f, 0.06f, 70f, 0.02f, 0.3f, 30f, clear: 3f);
                case VentKind.Blowdown:
                    return new VentSpec(1.2f, 0.09f, 300f, 0.5f, 0.14f, 36f);
                case VentKind.CylinderCrack:
                    return new VentSpec(0.4f, 0.045f, Sonic, 0.2f, 0.25f, 40f);
                case VentKind.Leak:
                    return new VentSpec(0.006f, 0.006f, 120f, 0.02f, 0.3f, 14f, clear: 8f);
                case VentKind.Gland:
                    return new VentSpec(0.003f, 0.008f, 20f, 0.25f, 0.3f, 10f);
                case VentKind.BoosterExhaust:
                    return new VentSpec(0.5f, 0.06f, 60f, 0.15f, 0.15f, 30f);
                case VentKind.Overflow:
                    return new VentSpec(0.05f, 0.03f, 8f, 0.3f, 0.35f, 24f);
                case VentKind.FittingWeep:
                    return new VentSpec(0.00006f, 0.003f, 1f, 0.02f, 0.3f, 4f);
                case VentKind.PumpExhaust:
                    return new VentSpec(0.02f, 0.03f, 40f, 0.1f, 0.12f, 20f);
                default:
                    return new VentSpec(0.0008f, 0.012f, 1.5f, 0.02f, 0.25f, 8f);
            }
        }

        /// <summary>Steam's speed out of an opening it is choked in, m/s.</summary>
        public const float Sonic = 450f;
    }

 public static class VentSteam
    {
        private const double SteamDensity = 0.6;
        private const double AirDensity = 1.2;
        private const double AxisDecay = 6.2;
        private const double Entrainment = 0.32;

        // Light through a cloud of droplets: its depth is the water in the
        // way, kg/m², × this (droplets some 10 µm across).
        private const double Extinction = 150.0;

        // Specific heats, kJ/kg K, and water's latent heat, kJ/kg.
        private const double AirHeat = 1.005;
        private const double VaporHeat = 1.86;
        private const double Latent = 2257.0;
        private const double Gravity = 9.81;

        /// <summary>The air's own pressure, bar.</summary>
        public const float Atmosphere = 1.01325f;

 public static float Gauge(float absolute) => float.IsNaN(absolute) ? 0f : Math.Max(0f, absolute - Atmosphere);

        /// <summary>A jet's width as the air sees it, m: <paramref name="bore"/> × the root of steam's density over the air's.</summary>
        public static float EffectiveBore(float bore) => (float)(Math.Max(1e-4, bore) * Math.Sqrt(SteamDensity / AirDensity));

        /// <summary>
        /// A jet's strength, m²/s: its speed on the axis × the distance
        /// along it, the same all the way out. From the speed it leaves at
        /// through the air and its bore.
        /// </summary>
        public static float JetStrength(float speed, float bore) => (float)(AxisDecay * Math.Max(0f, speed) * EffectiveBore(bore));

        /// <summary>
        /// How far a parcel has gone through the air <paramref name="age"/> s
        /// after it left at <paramref name="speed"/> m/s, m: its speed
        /// falling as one over the distance from the start.
        /// </summary>
        public static float Travel(float age, float speed, float bore)
        {
            var strength = JetStrength(speed, bore);
            if (!(strength > 0f) || !(speed > 0f))
                return 0f;
            // The distance at which the far jet's law gives the leaving speed.
            var start = strength / speed;
            return (float)(Math.Sqrt(start * start + 2.0 * strength * Math.Max(0f, age)) - start);
        }

        /// <summary>A parcel's speed through the air <paramref name="age"/> s after it left, m/s.</summary>
        public static float SpeedAt(float age, float speed, float bore)
        {
            var strength = JetStrength(speed, bore);
            if (!(strength > 0f) || !(speed > 0f))
                return 0f;
            var start = strength / speed;
            return (float)(strength / Math.Sqrt(start * start + 2.0 * strength * Math.Max(0f, age)));
        }

        /// <summary>
        /// How far a jet's white has formed <paramref name="travel"/> m out, 0
        /// to 1: none through its clear core (<see cref="VentSpec.Clear"/>
        /// bores of <paramref name="bore"/> m), all of it by twice that.
        /// </summary>
        public static float Formed(float travel, float bore, float clear)
        {
            var core = Math.Max(0f, clear) * Math.Max(0f, bore);
            if (!(core > 0f))
                return 1f;
            var t = Math.Max(0f, Math.Min(1f, (travel - core) / core));
            return t * t * (3f - 2f * t);
        }

        /// <summary>The kg of air a kg of the jet's steam has taken in <paramref name="travel"/> m out.</summary>
        public static float JetDilution(float travel, float bore) =>
            (float)(Entrainment * Math.Max(0f, travel) / EffectiveBore(bore));

        /// <summary>
        /// How fast a parcel's width grows once the jet is spent, m/s: the
        /// air's own turbulence stirring it, more in a wind, ×
        /// <paramref name="mixing"/>.
        /// </summary>
        public static float AmbientGrowth(float airSpeed, float mixing) =>
            (0.05f + 0.06f * Math.Max(0f, airSpeed)) * Math.Max(0f, mixing);

        /// <summary>
        /// The kg of air a kg of steam takes in a second as its parcel,
        /// <paramref name="size"/> m wide at <paramref name="dilution"/>,
        /// widens by <paramref name="growth"/> m/s: its mass as its width cubed.
        /// </summary>
        public static float AmbientMixing(float dilution, float size, float growth) =>
            3f * (1f + Math.Max(0f, dilution)) * Math.Max(0f, growth) / Math.Max(0.02f, size);

        /// <summary>
        /// The width of a parcel of <paramref name="mass"/> kg of steam mixed
        /// with <paramref name="dilution"/> times its weight of air, m: a
        /// ball of the mixture at the air's density.
        /// </summary>
        public static float Size(float mass, float dilution) =>
            (float)Math.Pow(Math.Max(0f, mass) * (1.0 + Math.Max(0f, dilution)) * 6.0 / (Math.PI * AirDensity), 1.0 / 3.0);

        /// <summary>
        /// How opaque a parcel is seen through its middle, 0 to 1: its
        /// <paramref name="mass"/> kg of steam holding <paramref name="liquid"/>
        /// kg of droplets a kg, spread over a disk <paramref name="size"/> m
        /// wide, × <paramref name="density"/>.
        /// </summary>
        public static float Opacity(float mass, float liquid, float size, float density = 1f)
        {
            if (!(mass > 0f) || !(liquid > 0f) || !(size > 0f))
                return 0f;
            var depth = Extinction * mass * liquid / (Math.PI / 4.0 * size * size) * Math.Max(0f, density);
            return (float)(1.0 - Math.Exp(-depth));
        }

        /// <summary>
        /// How much warmer than the air a parcel is, K: the steam's heat and
        /// what its condensing gave up, spread through the air it has mixed
        /// with.
        /// </summary>
        public static float Excess(float airTemperature, float dilution, float liquid)
        {
            var mixed = Math.Max(0.05, dilution);
            var heat = VaporHeat * Math.Max(0.0, 100.0 - airTemperature) + Latent * Math.Max(0f, Math.Min(1f, liquid));
            return (float)Math.Min(100.0 - airTemperature, heat / (mixed * AirHeat + VaporHeat));
        }

        /// <summary>
        /// How fast a warm parcel rises, m/s: a thermal <paramref name="size"/>
        /// m wide, <paramref name="excess"/> K warmer than air at
        /// <paramref name="airTemperature"/> °C.
        /// </summary>
        public static float Rise(float excess, float airTemperature, float size) =>
            (float)(0.6 * Math.Sqrt(Gravity * Math.Max(0f, excess) / (273.15 + airTemperature) * Math.Max(0f, size)));

        /// <summary>
        /// How long a vent's steam stays white, s, in still air: until its
        /// cloud, past its thickest, shows less than <paramref name="faint"/>
        /// opacity. For the test bed's readout and the tests; 0 when it
        /// never shows.
        /// </summary>
        public static float Lasting(VentSpec spec, SteamCloud cloud, float airSpeed, float faint = 0.02f, float mixing = 1f)
        {
            var mass = spec.Flow / Math.Max(1f, spec.Rate);
            var ambient = 0f;
            var seen = false;
            const float step = 0.02f;
            for (var age = step; age < 120f; age += step)
            {
                var travel = Travel(age, spec.Speed, spec.Bore);
                var jet = JetDilution(travel, spec.Bore);
                var dilution = jet + ambient;
                var size = Size(mass, dilution);
                ambient += AmbientMixing(dilution, size, AmbientGrowth(airSpeed, mixing)) * step;
                var opacity = Opacity(mass, cloud.Liquid(dilution), size) * Formed(travel, spec.Bore, spec.Clear);
                if (opacity >= faint)
                    seen = true;
                else if (seen)
                    return age;
                else if (dilution > 2000f)
                    return 0f;
            }
            return seen ? 120f : 0f;
        }
    }
}
