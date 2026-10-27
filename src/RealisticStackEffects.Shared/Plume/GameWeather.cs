using DV.WeatherSystem;
using UnityEngine;

namespace RealisticStackEffects
{
    /// <summary>
    /// The game's weather, found rather than taken from its singleton, which
    /// would make one if there were none yet: for the chimney smoke's wind and
    /// the rain on the coal. Nothing before the weather has loaded.
    /// </summary>
    internal static class GameWeather
    {
        // Looked for again this often, in s.
        private const float LookAgain = 5f;
        private static WeatherDriver driver;
        private static float nextLook;

        /// <summary>The game's weather driver, or null before it has loaded.</summary>
        public static WeatherDriver Driver
        {
            get
            {
                if (driver == null && Time.time >= nextLook)
                {
                    nextLook = Time.time + LookAgain;
                    driver = Object.FindObjectOfType<WeatherDriver>();
                }
                return driver;
            }
        }

        /// <summary>
        /// How wet the world is, 0 dry to 1 soaked: it rises with the rain and
        /// dries over hours of game time with the sun, as the game keeps it
        /// for its wet surfaces. 0 before the weather has loaded.
        /// </summary>
        public static float Wetness
        {
            get
            {
                var weather = Driver;
                return weather != null ? Mathf.Clamp01(weather.WetnessValue.CurrentValue) : 0f;
            }
        }
    }
}
