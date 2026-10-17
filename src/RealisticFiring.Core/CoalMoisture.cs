using System;

namespace RealisticFiring.Core
{
 public sealed class CoalMoisture
    {
        /// <summary>
        /// Coal as loaded, %: drained sized coal keeps 0.3 to 3 % (Bureau of
        /// Mines IC 7009, 1938, pp.7-8), run of mine a little more (inferred).
        /// </summary>
        public const float Delivered = 0.03f;

        /// <summary>
        /// Coal rained on: "When wet down or when rained upon, fine coal or
        /// slack may hold 10 per cent. of surface moisture" (Cosgrove, Coal,
        /// 1916, p.38).
        /// </summary>
        public const float Soaked = 0.10f;

        /// <summary>The coal at the shovel plate takes up rain with this time constant, s (inferred: a shower wets the face within the quarter hour).</summary>
        public const float SoakTime = 900f;

        /// <summary>
        /// And gives it up no faster than this as the weather dries, s
        /// (inferred: wet screenings were held 24 to 48 hours to drain,
        /// Illinois EES Circular 6, 1918, p.88; coarse coal drains fast).
        /// </summary>
        public const float DryTime = 3600f;

        /// <summary>The water's share of the wet coal's mass, 0 to 1.</summary>
        public float Moisture { get; private set; } = Delivered;

        /// <summary>Water carried by each kg of dry coal, kg.</summary>
        public float WaterPerCoal => WaterPerKg(Moisture);

        /// <summary>Water carried by each kg of dry coal at <paramref name="moisture"/>, kg.</summary>
        public static float WaterPerKg(float moisture)
        {
            var m = Math.Max(0f, Math.Min(0.5f, moisture));
            return m / (1f - m);
        }

        /// <summary>
        /// Soaks or dries the coal for <paramref name="dt"/> seconds toward what
        /// the weather's <paramref name="wetness"/> (0 dry to 1 soaked, rising
        /// with the rain and drying with the sun, as the game keeps it) leaves it.
        /// </summary>
        public void Step(float dt, float wetness)
        {
            if (!(dt > 0f))
                return;
            var w = float.IsNaN(wetness) ? 0f : Math.Max(0f, Math.Min(1f, wetness));
            var target = Delivered + (Soaked - Delivered) * w;
            var time = target > Moisture ? SoakTime : DryTime;
            Moisture += (target - Moisture) * (1f - (float)Math.Exp(-dt / time));
        }

        /// <summary>
        /// Coal loaded at the coaling stage, <paramref name="added"/> kg as
        /// delivered, mixes with the <paramref name="remaining"/> kg already in.
        /// </summary>
        public void Load(float remaining, float added)
        {
            if (!(added > 0f))
                return;
            var left = Math.Max(0f, remaining);
            Moisture = (Moisture * left + Delivered * added) / (left + added);
        }

        /// <summary>Puts the coal back as saved; out-of-range values take the delivered coal's.</summary>
        public void Restore(float moisture) =>
            Moisture = moisture >= 0f && moisture <= Soaked ? moisture : Delivered;
    }
}
