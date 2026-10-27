using System;

namespace RealisticStackEffects.Core
{
 public static class Psychrometrics
    {
        /// <summary>The atmosphere's pressure, Pa.</summary>
        public const double Pressure = 101325.0;

        // Water vapor's molar mass over dry air's.
        private const double VaporOverAir = 0.622;

        /// <summary>
        /// Water's saturation vapor pressure over liquid water at
        /// <paramref name="temperature"/> °C, in Pa: Buck's (1996) fit up to
        /// 50 °C, Antoine's water constants above (to boiling, where the
        /// steam leaving the stack starts).
        /// </summary>
        public static double SaturationPressure(double temperature)
        {
            if (temperature <= 50.0)
                return 611.21 * Math.Exp((18.678 - temperature / 234.5) * (temperature / (257.14 + temperature)));
            return 133.322 * Math.Pow(10.0, 8.07131 - 1730.63 / (233.426 + temperature));
        }

        /// <summary>The kg of water vapor a kg of dry air holds when saturated at <paramref name="temperature"/> °C.</summary>
        public static double SaturationRatio(double temperature)
        {
            var vapor = Math.Min(SaturationPressure(temperature), 0.99 * Pressure);
            return VaporOverAir * vapor / (Pressure - vapor);
        }

        /// <summary>The kg of water vapor a kg of dry air carries at <paramref name="temperature"/> °C and <paramref name="humidity"/> (relative, 0 to 1).</summary>
        public static double VaporRatio(double temperature, double humidity)
        {
            var vapor = Math.Max(0.0, Math.Min(1.0, humidity)) * SaturationPressure(temperature);
            return VaporOverAir * vapor / (Pressure - vapor);
        }

        /// <summary>The dew point of air at <paramref name="temperature"/> °C and <paramref name="humidity"/>, in °C.</summary>
        public static float DewPoint(float temperature, float humidity)
        {
            // Buck's fit, inverted by bisection over the plausible range.
            var vapor = Math.Max(1e-3, Math.Min(1.0, humidity)) * SaturationPressure(temperature);
            double low = -80.0, high = temperature;
            for (var i = 0; i < 50; i++)
            {
                var middle = 0.5 * (low + high);
                if (SaturationPressure(middle) > vapor)
                    high = middle;
                else
                    low = middle;
            }
            return (float)(0.5 * (low + high));
        }

 public static float SaturationDeficit(float temperature, float humidity) =>
            (float)(SaturationPressure(temperature) * (1.0 - Math.Max(0.0, Math.Min(1.0, humidity))) / 100.0);
    }

    /// <summary>The air the stack's plume mixes into: its temperature and relative humidity.</summary>
    public struct PlumeAir
    {
        public PlumeAir(float temperature, float humidity)
        {
            Temperature = temperature;
            Humidity = Math.Max(0f, Math.Min(1f, humidity));
        }

        /// <summary>The air's temperature, °C.</summary>
        public float Temperature { get; }

        /// <summary>Its relative humidity, 0 to 1.</summary>
        public float Humidity { get; }

        public float DewPoint => Psychrometrics.DewPoint(Temperature, Humidity);

        /// <summary>How much more water it could hold, hPa (<see cref="Psychrometrics.SaturationDeficit"/>).</summary>
        public float SaturationDeficit => Psychrometrics.SaturationDeficit(Temperature, Humidity);

        /// <summary>
        /// The air the plume's white steam was tuned in, which shows it at
        /// its tuned opacity: a cool, damp day (10 °C, 80 %), where the
        /// reference photos show a working engine's white plume for some
        /// seconds.
        /// </summary>
        public static PlumeAir Reference => new PlumeAir(10f, 0.8f);

        public override string ToString() => $"{Temperature:F0} °C, {Humidity * 100f:F0} % (dew point {DewPoint:F0} °C)";
    }

 public static class PlumeClimate
    {
        // The day's coolest hour and its warmest, when a clear day's air is
        // coolest (around sunrise) and warmest (mid-afternoon).
        private const float CoolestHour = 5f;
        private const float WarmestHour = 15f;

        // Rain cools the air this much at its heaviest, in °C, and brings it
        // to this humidity; fog, to saturation.
        private const float RainCooling = 4f;
        private const float RainHumidity = 0.97f;

        private struct Season
        {
            public float Mean;
            public float Swing;
            public float DewPoint;
        }

        private static Season Of(SmokeSeason season)
        {
            switch (season)
            {
                case SmokeSeason.Winter:
                    return new Season { Mean = -3f, Swing = 4f, DewPoint = -6f };
                case SmokeSeason.SpringOrAutumn:
                    return new Season { Mean = 10f, Swing = 5f, DewPoint = 5f };
                default:
                    return new Season { Mean = 17f, Swing = 6f, DewPoint = 10f };
            }
        }

        /// <summary>
        /// The air at <paramref name="hour"/> (0 to 24) of a day in
        /// <paramref name="season"/>, with <paramref name="rain"/> and
        /// <paramref name="fog"/> 0 to 1.
        /// </summary>
        public static PlumeAir Air(SmokeSeason season, float hour, float rain, float fog)
        {
            var day = Of(season);
            var temperature = day.Mean + day.Swing * DayShape(hour);
            var dewPoint = Math.Min(day.DewPoint, temperature);
            var humidity = (float)(Psychrometrics.SaturationPressure(dewPoint) / Psychrometrics.SaturationPressure(temperature));
            var wet = Math.Max(0f, Math.Min(1f, rain));
            temperature -= RainCooling * wet;
            humidity = Math.Min(1f, humidity + (RainHumidity - humidity) * Math.Max(0f, wet * 2f - wet * wet));
            humidity += (1f - humidity) * Math.Max(0f, Math.Min(1f, fog));
            return new PlumeAir(temperature, humidity);
        }

        // DV Seasons' day: warmest at 15 and coolest at 3, swinging this much
        // either way under a clear sky, the less the more it rains (its own
        // swing shrinks with cloud, which this does not see), in °C; the
        // night's coolest brings the air to about this far above its dew
        // point.
        private const float SeasonsSwing = 5f;
        private const float SeasonsRainDamping = 3.5f;
        private const float NightAboveDewPoint = 1f;

        /// <summary>
        /// The air at <paramref name="temperature"/> °C (DV Seasons'), at
        /// <paramref name="hour"/>, with <paramref name="rain"/> and
        /// <paramref name="fog"/> 0 to 1: its dew point a degree under the
        /// day's coolest, which DV Seasons' daily swing puts 3 in the morning
        /// (it has no humidity), so humid at night and drier in the afternoon.
        /// </summary>
        public static PlumeAir WithTemperature(float temperature, float hour, float rain, float fog)
        {
            var wet = Math.Max(0f, Math.Min(1f, rain));
            var swing = SeasonsSwing - SeasonsRainDamping * wet;
            var phase = (float)Math.Sin((hour - 9.0) * Math.PI / 12.0);
            var coolest = temperature - swing * (phase + 1f);
            var dewPoint = Math.Min(temperature, coolest - NightAboveDewPoint);
            var humidity = (float)(Psychrometrics.SaturationPressure(dewPoint) / Psychrometrics.SaturationPressure(temperature));
            humidity = Math.Min(1f, humidity + (RainHumidity - humidity) * Math.Max(0f, wet * 2f - wet * wet));
            humidity += (1f - humidity) * Math.Max(0f, Math.Min(1f, fog));
            return new PlumeAir(temperature, humidity);
        }

        // -1 at the coolest hour, 1 at the warmest: rising over the morning,
        // falling over the evening and night.
        private static float DayShape(float hour)
        {
            var h = ((hour % 24f) + 24f) % 24f;
            if (h >= CoolestHour && h <= WarmestHour)
                return -(float)Math.Cos(Math.PI * (h - CoolestHour) / (WarmestHour - CoolestHour));
            var since = h > WarmestHour ? h - WarmestHour : h + 24f - WarmestHour;
            return (float)Math.Cos(Math.PI * since / (24f - WarmestHour + CoolestHour));
        }
    }

    /// <summary>The season the chimney smoke's air is taken from (the game has no seasons).</summary>
    public enum SmokeSeason
    {
        Summer,
        SpringOrAutumn,
        Winter,
    }
}
