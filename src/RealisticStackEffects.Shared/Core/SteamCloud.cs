using System;
using System.Collections.Generic;

namespace RealisticStackEffects.Core
{
 public sealed class SteamCloud
    {
        // The exhaust steam leaves at about the atmosphere's boiling point;
        // the flue gas at about this, °C, carrying this much water vapor a kg
        // (the coal's hydrogen and moisture burned).
        private const double SteamTemperature = 100.0;
        public const float FlueTemperature = 300f;
        private const double FlueWater = 0.05;

        /// <summary>The kg of flue gas carrying a kg of the fire's own water (<see cref="FlueWater"/>): the haze of a clean fire, with no steam.</summary>
        public const float FlueGasPerFireWater = (float)(1.0 / FlueWater);

        // Specific heats, kJ/kg K: dry air, water vapor, liquid water, flue
        // gas; and water's latent heat at 0 °C, kJ/kg.
        private const double AirHeat = 1.005;
        private const double VaporHeat = 1.86;
        private const double WaterHeat = 4.19;
        private const double FlueHeat = 1.05;
        private const double Latent = 2501.0;

        // Dilutions tabulated: from this many kg of air a kg of steam, each
        // this many times the last, to about 100,000.
        private const double FirstDilution = 0.25;
        private const double DilutionStep = 1.2;
        private const int Points = 72;

        /// <summary>
        /// The kg of flue gas a kg of steam meets before the air, for the
        /// reference (<see cref="PlumeAir.Reference"/>): the share of it that
        /// mixes with the beats' steam before it leaves the stack (0.6) of
        /// the two or so kg of gas the fire sends up with each kg of steam.
        /// </summary>
        public const float ReferenceFlueGas = 1.2f;

        private readonly float[] liquid = new float[Points];
        private readonly int thickest;

        private SteamCloud(PlumeAir air, float flueGas, float wetness)
        {
            Air = air;
            FlueGas = flueGas;
            Wetness = wetness;
            var best = 0.0;
            for (var i = 0; i < Points; i++)
            {
                var dilution = DilutionAt(i);
                liquid[i] = (float)Condensed(air, flueGas, wetness, dilution);
                // Seen across, the cloud is as thick as its water over its
                // width, a puff's volume growing as the air it mixes in.
                var thickness = liquid[i] / Math.Pow(dilution, 2.0 / 3.0);
                if (thickness > best)
                {
                    best = thickness;
                    thickest = i;
                }
            }
            Thickness = (float)best;
        }

        public PlumeAir Air { get; }

        /// <summary>The kg of flue gas each kg of steam meets before the air.</summary>
        public float FlueGas { get; }

        /// <summary>The steam's water share as it leaves the cylinders, 0 dry to 1.</summary>
        public float Wetness { get; }

        /// <summary>The cloud at its thickest seen across: kg of condensed water a kg of steam over its dilution to the two-thirds.</summary>
        public float Thickness { get; }

        /// <summary>The cloud at its thickest against the reference's (1): 0 shows no white.</summary>
        public float Whiteness => Thickness / Reference.Thickness;

        /// <summary>The dilution at which the cloud is thickest, kg of air a kg of steam.</summary>
        public float ThickestDilution => (float)DilutionAt(thickest);

        /// <summary>The kg of water condensed a kg of steam at <paramref name="dilution"/> kg of air a kg of steam.</summary>
        public float Liquid(float dilution)
        {
            if (!(dilution > FirstDilution))
                return liquid[0];
            var at = Math.Log(dilution / FirstDilution) / Math.Log(DilutionStep);
            if (at >= Points - 1)
                return liquid[Points - 1];
            var i = (int)at;
            var t = (float)(at - i);
            return liquid[i] + (liquid[i + 1] - liquid[i]) * t;
        }

        /// <summary>
        /// The share of its thickest white the cloud shows at <paramref name="dilution"/>,
        /// 0 to 1: forming as the air mixes in (<see cref="Formed"/>), then
        /// evaporating as more air takes up its water (<see cref="Remaining"/>).
        /// </summary>
        public float Left(float dilution) => Math.Min(Formed(dilution), Remaining(dilution));

 public float Formed(float dilution)
        {
            if (!(Thickness > 0f))
                return 0f;
            if (dilution >= ThickestDilution)
                return 1f;
            var d = Math.Max((float)FirstDilution, dilution);
            return Math.Max(0f, Math.Min(1f, Liquid(d) / (float)Math.Pow(d, 2.0 / 3.0) / Thickness));
        }

        /// <summary>
        /// The share of the cloud's water left at <paramref name="dilution"/>
        /// past its thickest, 0 to 1 (1 before it): the air it mixes in takes
        /// up its water until none is left.
        /// </summary>
        public float Remaining(float dilution)
        {
            var most = liquid[thickest];
            if (!(most > 0f))
                return 0f;
            if (dilution <= ThickestDilution)
                return 1f;
            return Math.Max(0f, Math.Min(1f, Liquid(dilution) / most));
        }

        private const float MixingAtOneSecond = 12f;
        private const float MixingPower = 1.3f;
        private const float CalmAir = 1f;

        /// <summary>
        /// The kg of air mixed into each kg of exhaust <paramref name="age"/>
        /// s after it left the stack, the air going past at <paramref name="airSpeed"/>
        /// m/s, × <paramref name="mixing"/>.
        /// </summary>
        public static float Dilution(float age, float airSpeed, float mixing) =>
            MixingAtOneSecond * (float)Math.Pow(Math.Max(CalmAir, airSpeed), 1.0 / 3.0)
                              * (float)Math.Pow(Math.Max(0f, age), MixingPower) * Math.Max(0.01f, mixing);

        /// <summary>The age at which the mixing reaches <paramref name="dilution"/> (<see cref="Dilution"/> inverted), s.</summary>
        public static float AgeAt(float dilution, float airSpeed, float mixing) =>
            (float)Math.Pow(Math.Max(0f, dilution) / (MixingAtOneSecond * Math.Pow(Math.Max(CalmAir, airSpeed), 1.0 / 3.0) * Math.Max(0.01f, mixing)),
                1.0 / MixingPower);

        /// <summary>
        /// The share of its thickest white the cloud shows <paramref name="age"/>
        /// s on (<see cref="Left(float)"/>), forming with the mixing ×
        /// <paramref name="forming"/> (<see cref="PlumeTuning.SteamForming"/>).
        /// </summary>
        public float Left(float age, float airSpeed, float mixing, float forming = 1f) =>
            Math.Min(Formed(Dilution(age, airSpeed, mixing * forming)), Remaining(Dilution(age, airSpeed, mixing)));

        /// <summary>Whether the white has formed and only evaporates from <paramref name="age"/> s on (true when it never forms).</summary>
        public bool PastThickest(float age, float airSpeed, float mixing, float forming = 1f) =>
            !(Thickness > 0f) || age >= ThickestAge(airSpeed, mixing, forming);

        // The age at which the white is at its thickest, s.
        private float ThickestAge(float airSpeed, float mixing, float forming) =>
            Math.Max(AgeAt(ThickestDilution, airSpeed, mixing), AgeAt(ThickestDilution, airSpeed, mixing * forming));

        /// <summary>
        /// How long the white takes to form to <paramref name="share"/> of its
        /// thickest, s: for the test bed's readout. 0 when it shows at once;
        /// infinity when it never forms.
        /// </summary>
        public float Forming(float airSpeed, float mixing, float forming, float share)
        {
            if (!(Thickness > 0f))
                return float.PositiveInfinity;
            float low = 0f, high = ThickestAge(airSpeed, mixing, forming);
            if (Left(low, airSpeed, mixing, forming) >= share)
                return 0f;
            for (var i = 0; i < 30; i++)
            {
                var middle = 0.5f * (low + high);
                if (Left(middle, airSpeed, mixing, forming) >= share)
                    high = middle;
                else
                    low = middle;
            }
            return high;
        }

        /// <summary>
        /// How long until the cloud, past its thickest, has evaporated to
        /// <paramref name="share"/> of its water, in s from the stack (at most
        /// 600), the air going past at <paramref name="airSpeed"/> m/s: for
        /// the test bed's readout.
        /// </summary>
        public float Lasting(float airSpeed, float mixing, float share, float forming = 1f)
        {
            if (!(Thickness > 0f))
                return 0f;
            var low = ThickestAge(airSpeed, mixing, forming);
            var high = Math.Max(0.5f, 2f * low);
            while (Left(high, airSpeed, mixing, forming) > share)
            {
                low = high;
                high *= 2f;
                if (high > 600f)
                    return 600f;
            }
            for (var i = 0; i < 30; i++)
            {
                var middle = 0.5f * (low + high);
                if (Left(middle, airSpeed, mixing, forming) > share)
                    low = middle;
                else
                    high = middle;
            }
            return high;
        }

        /// <summary>
        /// The most of the fire's gas a kg of a beat's steam meets at the
        /// blast's core, kg, and the share of the beat's white that core
        /// shows. The core leaves before the gas around the blast has mixed
        /// into it, so on a mild day it still shows a short white where the
        /// mixed steam shows none (78youbaba, Nexus, 2026-10-28: a summer
        /// afternoon's engine showed no white at all); in hot, dry air it
        /// shows none either, as the reference photos do. Fitted so that the
        /// reference air and the damp photos (16, 31) are the mixed steam's alone.
        /// </summary>
        public const float CoreFlueGas = 0.9f;
        public const float CoreWhite = 0.7f;

        /// <summary>
        /// The white a beat's steam makes (<see cref="For"/>), meeting
        /// <paramref name="flueGas"/> kg of the fire's gas a kg mixed in, or
        /// its core's (<see cref="CoreFlueGas"/>), whichever shows the more;
        /// <paramref name="whiteness"/> is how white it shows, as <see cref="Whiteness"/>.
        /// </summary>
        public static SteamCloud ForBeats(PlumeAir air, float flueGas, float wetness, out float whiteness)
        {
            var mixed = For(air, flueGas, wetness);
            var core = For(air, Math.Min(flueGas, CoreFlueGas), wetness);
            if (CoreWhite * core.Whiteness > mixed.Whiteness)
            {
                whiteness = CoreWhite * core.Whiteness;
                return core;
            }
            whiteness = mixed.Whiteness;
            return mixed;
        }

        private static double DilutionAt(int i) => FirstDilution * Math.Pow(DilutionStep, i);

        /// <summary>
        /// The kg of water condensed when a kg of steam (<paramref name="wetness"/>
        /// of it water) and <paramref name="flueGas"/> kg of flue gas have
        /// mixed with <paramref name="dilution"/> kg of <paramref name="air"/>.
        /// </summary>
        public static double Condensed(PlumeAir air, double flueGas, double wetness, double dilution)
        {
            var airVapor = Psychrometrics.VaporRatio(air.Temperature, air.Humidity);
            var wet = Math.Max(0.0, Math.Min(1.0, wetness));
            var flue = Math.Max(0.0, flueGas);
            var dry = dilution + flue;
            var water = 1.0 + dilution * airVapor + flue * FlueWater;
            // The mixture's heat, kJ, from 0 °C liquid water and dry gas.
            var heat = (1.0 - wet) * (Latent + VaporHeat * SteamTemperature) + wet * WaterHeat * SteamTemperature
                       + flue * (FlueHeat * FlueTemperature + FlueWater * (Latent + VaporHeat * FlueTemperature))
                       + dilution * (AirHeat * air.Temperature + airVapor * (Latent + VaporHeat * air.Temperature));
            // All vapor: is the mixture saturated at its temperature?
            var vaporTemperature = (heat - water * Latent) / (dry * AirHeat + water * VaporHeat);
            if (water <= dry * Psychrometrics.SaturationRatio(vaporTemperature))
                return 0.0;
            // Saturated: the temperature where its heat balances with the
            // water past saturation condensed.
            double low = -60.0, high = 110.0;
            for (var i = 0; i < 40; i++)
            {
                var t = 0.5 * (low + high);
                var vapor = Math.Min(water, dry * Psychrometrics.SaturationRatio(t));
                var h = dry * AirHeat * t + vapor * (Latent + VaporHeat * t) + (water - vapor) * WaterHeat * t;
                if (h > heat)
                    high = t;
                else
                    low = t;
            }
            return Math.Max(0.0, water - dry * Psychrometrics.SaturationRatio(0.5 * (low + high)));
        }

        // Clouds already worked out, by their air, flue gas, and wetness at
        // the steps they are told apart by.
        private static readonly Dictionary<long, SteamCloud> Known = new Dictionary<long, SteamCloud>();
        private const int MostKnown = 512;
        private static SteamCloud reference;

        /// <summary>The reference cloud: the reference air, its flue gas, dry steam (<see cref="Whiteness"/> 1).</summary>
        public static SteamCloud Reference => reference ?? (reference = For(PlumeAir.Reference, ReferenceFlueGas));

        /// <summary>
        /// The cloud for steam <paramref name="wetness"/> wet meeting
        /// <paramref name="flueGas"/> kg of flue gas a kg before mixing into
        /// <paramref name="air"/>; told apart at half a degree, a percent of
        /// humidity, about 2 % of flue gas, and a percent of wetness.
        /// </summary>
        public static SteamCloud For(PlumeAir air, float flueGas, float wetness = 0f)
        {
            var t = (long)Math.Round(Math.Max(-60f, Math.Min(60f, air.Temperature)) * 2f) + 200;
            var h = (long)Math.Round(air.Humidity * 100f);
            var f = (long)Math.Round(Math.Log(1.0 + Math.Max(0f, Math.Min(50f, flueGas))) * 50.0);
            var w = (long)Math.Round(Math.Max(0f, Math.Min(1f, wetness)) * 100f);
            var key = ((t * 128 + h) * 512 + f) * 128 + w;
            lock (Known)
            {
                if (Known.TryGetValue(key, out var cloud))
                    return cloud;
                if (Known.Count >= MostKnown)
                    Known.Clear();
                // Made from the steps, so equal keys make equal clouds.
                cloud = new SteamCloud(new PlumeAir((t - 200) / 2f, h / 100f), (float)(Math.Exp(f / 50.0) - 1.0), w / 100f);
                Known[key] = cloud;
                return cloud;
            }
        }
    }
}
