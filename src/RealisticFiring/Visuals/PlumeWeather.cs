using RealisticFiring.Core;
using UnityEngine;

namespace RealisticFiring.Visuals
{
    /// <summary>
    /// The air the chimney smoke drifts and mixes in, from the game's weather:
    /// the wind (<see cref="PlumeWind"/>), its direction and its rain and
    /// thunder, with the breeze's spells and gusts from slow and quick noise;
    /// and the air's temperature and humidity, which the game has not, as a
    /// day of the season chosen in the settings at the game's hour, damp in
    /// the rain (<see cref="PlumeClimate"/>), or DV Seasons' temperature
    /// when it runs (<see cref="Sim.SeasonsMod"/>). The plume test bed's air and
    /// wind replace them while it sets them. Worked out once a frame; still,
    /// mild air before the weather has loaded.
    /// </summary>
    internal static class PlumeWeather
    {
        // A spell of calm or breeze lasts minutes; a gust, seconds.
        private const float SpellTime = 240f;
        private const float GustTime = 5f;

        private static int frame = -1;
        private static Vector3 wind;
        private static float speed;
        private static float degrees;
        private static PlumeAir air = PlumeAir.Reference;
        private static string source = "no weather yet";

        /// <summary>Where the air comes from: the test bed, DV Seasons, or the season setting.</summary>
        public static string Source
        {
            get
            {
                Update();
                return source;
            }
        }

        /// <summary>The air's temperature and humidity.</summary>
        public static PlumeAir Air
        {
            get
            {
                Update();
                return air;
            }
        }

        /// <summary>The wind's velocity, in m/s, in the world.</summary>
        public static Vector3 Wind
        {
            get
            {
                Update();
                return wind;
            }
        }

        /// <summary>The wind in a few words, for the status log.</summary>
        public static string Describe()
        {
            Update();
            return (speed > 0.05f ? $"wind {speed:F1} m/s toward {Mathf.Repeat(degrees, 360f):F0}°" : "still air") + $", {air}";
        }

        private static void Update()
        {
            if (frame == Time.frameCount)
                return;
            frame = Time.frameCount;
            wind = Vector3.zero;
            speed = 0f;
            var weather = Sim.GameWeather.Driver;
            if (PlumeTestBed.Air is PlumeAir testAir)
            {
                air = testAir;
                source = "the test bed's";
            }
            else if (weather != null && Sim.SeasonsMod.TryRead(out var celsius, out var season))
            {
                air = PlumeClimate.WithTemperature(celsius, weather.TimeOfDayHours.CurrentValue, weather.RainValue.CurrentValue, 0f);
                source = season;
            }
            else if (weather != null)
            {
                air = PlumeClimate.Air(Main.Settings.SmokeSeason, weather.TimeOfDayHours.CurrentValue, weather.RainValue.CurrentValue, 0f);
                source = $"a {Main.Settings.SmokeSeason} day (the setting)";
            }
            else
            {
                air = PlumeAir.Reference;
                source = "no weather yet";
            }
            if (PlumeTestBed.Wind is Vector3 test)
            {
                wind = test;
                speed = test.magnitude;
                degrees = PlumeTestBed.WindToward;
                return;
            }
            if (weather == null)
                return;
            degrees = weather.WindDirection.CurrentValue;
            var time = Time.time;
            speed = PlumeWind.Speed(weather.RainValue.CurrentValue, weather.ThunderValue.CurrentValue,
                Mathf.PerlinNoise(time / SpellTime, 17.3f), Mathf.PerlinNoise(time / GustTime, 51.7f), 1f);
            var (x, z) = PlumeWind.Toward(degrees);
            wind = new Vector3(x, 0f, z) * speed;
        }
    }
}
