using DV.WeatherSystem;
using RealisticFiring.Core;
using UnityEngine;

namespace RealisticFiring.Visuals
{
    /// <summary>
    /// The wind the chimney smoke drifts in, from the game's weather
    /// (<see cref="PlumeWind"/>): its direction and its rain and thunder,
    /// with the breeze's spells and gusts from slow and quick noise. Worked
    /// out once a frame; still air before the weather has loaded.
    /// </summary>
    internal static class PlumeWeather
    {
        // A spell of calm or breeze lasts minutes; a gust, seconds.
        private const float SpellTime = 240f;
        private const float GustTime = 5f;

        // The game's weather, found rather than taken from its singleton,
        // which would make one if there were none yet; looked for again this often, in s.
        private const float LookAgain = 5f;
        private static WeatherDriver driver;
        private static float nextLook;

        private static int frame = -1;
        private static Vector3 wind;
        private static float speed;
        private static float degrees;

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
            return speed > 0.05f ? $"wind {speed:F1} m/s toward {Mathf.Repeat(degrees, 360f):F0}°" : "still air";
        }

        private static void Update()
        {
            if (frame == Time.frameCount)
                return;
            frame = Time.frameCount;
            wind = Vector3.zero;
            speed = 0f;
            var scale = Main.Settings.SmokeWind;
            if (!(scale > 0f))
                return;
            if (driver == null && Time.time >= nextLook)
            {
                nextLook = Time.time + LookAgain;
                driver = Object.FindObjectOfType<WeatherDriver>();
            }
            if (driver == null)
                return;
            var weather = driver;
            degrees = weather.WindDirection.CurrentValue;
            var time = Time.time;
            speed = PlumeWind.Speed(weather.RainValue.CurrentValue, weather.ThunderValue.CurrentValue,
                Mathf.PerlinNoise(time / SpellTime, 17.3f), Mathf.PerlinNoise(time / GustTime, 51.7f), scale);
            var (x, z) = PlumeWind.Toward(degrees);
            wind = new Vector3(x, 0f, z) * speed;
        }
    }
}
