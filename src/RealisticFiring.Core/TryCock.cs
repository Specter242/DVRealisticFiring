using System;

namespace RealisticFiring.Core
{
    /// <summary>What a try cock lets out at a moment, kg/s.</summary>
    public readonly struct TryCockFlow
    {
        public TryCockFlow(float water, float steam, float flashed, float wet, float froth)
        {
            Water = water;
            Steam = steam;
            Flashed = flashed;
            Wet = wet;
            Froth = froth;
        }

        /// <summary>Boiler water out of the cock, kg/s, the part that flashes to steam as it leaves included.</summary>
        public float Water { get; }

        /// <summary>Steam from the steam space out of the cock, kg/s.</summary>
        public float Steam { get; }

        /// <summary>The part of <see cref="Water"/> that flashes to steam in the air, kg/s.</summary>
        public float Flashed { get; }

        /// <summary>The water left as water, spray falling from the spout, kg/s.</summary>
        public float Spray => Math.Max(0f, Water - Flashed);

        /// <summary>The steam the cock shows, dry and flashed, kg/s.</summary>
        public float AllSteam => Steam + Flashed;

        /// <summary>How much of the cock's mouth is under the water, 0 in the steam to 1 under; between, it spits both as the surface rises and falls at it.</summary>
        public float Wet { get; }

        /// <summary>How frothy the water out of it is, 0 to 1: foaming water froths out.</summary>
        public float Froth { get; }

        public bool Flowing => Water > 0f || Steam > 0f;
    }

 public static class TryCock
    {
        /// <summary>The cock's bore, m (3/16 in; inferred, a small cock passing a steady jet).</summary>
        public const float Bore = 0.0048f;

        private static readonly float Area = (float)(Math.PI / 4.0) * Bore * Bore;

        // A sharp-edged small orifice's coefficient of discharge.
        private const float Discharge = 0.6f;

        // Steam out of a choked orifice by Napier's rule: 1 lb/s for every
        // 70 psia on a square inch, here in kg/s for each bar absolute.
        private static readonly float NapierPerBar = 14.5038f * (Area / 6.4516e-4f) / 70f * 0.4536f;

        // Hot boiler water's density, kg/m³, and the air's pressure, bar.
        private const float HotWater = 870f;
        private const float Atmosphere = 1.01325f;

        // A cold boiler's water runs out under its own head, this many m
        // (inferred: the water a hand's span or two above a cock).
        private const float ColdHead = 0.3f;

        // The water's surface is this thick at a cock, in the boiler's height
        // (about an inch on a two-metre boiler): within it the cock spits
        // water and steam by turns. Foam thickens it by up to this much.
        public const float SurfaceBand = 0.008f;
        public const float FrothBand = 0.03f;

        // Water's heat a kelvin above boiling at the air's pressure, kJ/kg K,
        // and its latent heat there, kJ/kg: the share of the water that
        // flashes to steam as it leaves.
        private const float HotWaterHeat = 4.35f;
        private const float Latent = 2257f;

        /// <summary>
        /// How much of the cock's mouth is under the water, 0 to 1, from its
        /// <paramref name="depth"/> under the surface (negative above), in
        /// the boiler's height, and the water's <paramref name="foam"/>.
        /// </summary>
        public static float WaterShare(float depth, float foam)
        {
            depth = Smoothing.Finite(depth);
            var band = SurfaceBand + FrothBand * Smoothing.Clamp01(Smoothing.Finite(foam));
            var t = Smoothing.Clamp01(0.5f + depth / (2f * band));
            return t * t * (3f - 2f * t);
        }

        /// <summary>The share of boiler water at <paramref name="waterTemperature"/>, °C, that flashes to steam in the air.</summary>
        public static float FlashShare(float waterTemperature) =>
            Smoothing.Clamp01(HotWaterHeat * (Smoothing.Finite(waterTemperature) - 100f) / Latent);

        /// <summary>Water through the cock wide open, kg/s, at <paramref name="pressure"/> bar absolute.</summary>
        public static float WaterFlow(float pressure)
        {
            var gauge = Math.Max(0f, Smoothing.Finite(pressure) - Atmosphere);
            var head = gauge * 1e5f / (HotWater * 9.81f) + ColdHead;
            return Discharge * Area * (float)Math.Sqrt(2.0 * 9.81 * head) * HotWater;
        }

        /// <summary>Steam through the cock wide open, kg/s, at <paramref name="pressure"/> bar absolute; none without pressure.</summary>
        public static float SteamFlow(float pressure)
        {
            pressure = Smoothing.Finite(pressure);
            return pressure > Atmosphere ? NapierPerBar * pressure * Smoothing.Clamp01((pressure - Atmosphere) / 0.5f) : 0f;
        }

        /// <summary>
        /// What the cock lets out, opened <paramref name="opening"/> (0 shut to
        /// 1), <paramref name="depth"/> under the surface, with the water
        /// foaming <paramref name="foam"/>, at <paramref name="pressure"/> bar
        /// absolute and <paramref name="waterTemperature"/> °C.
        /// </summary>
        public static TryCockFlow Flow(float opening, float depth, float foam, float pressure, float waterTemperature)
        {
            opening = Smoothing.Clamp01(Smoothing.Finite(opening));
            if (!(opening > 0f))
                return default;
            var wet = WaterShare(depth, foam);
            var water = opening * wet * WaterFlow(pressure);
            var steam = opening * (1f - wet) * SteamFlow(pressure);
            var flashed = water * FlashShare(waterTemperature);
            return new TryCockFlow(water, steam, flashed, wet, Smoothing.Clamp01(Smoothing.Finite(foam)) * wet);
        }
    }
}
