using UnityEngine;

namespace RealisticFiring.Visuals
{
 internal static class FireboxTestBed
    {
        /// <summary>The flames' draft, 0 to 1, in place of the fire's; null for the fire's own.</summary>
        public static float? Draft;

        /// <summary>Exhaust beats a second laid over the flames, as a working engine's; 0 for none.</summary>
        public static float BeatRate;

        /// <summary>Each laid beat's strength, 0 to 1, as a beat's exhaust pressure gives it.</summary>
        public static float BeatStrength = 1f;

        /// <summary>
        /// The air through the grate, kg/s, in place of the engine's draft,
        /// so a standing engine with a cold boiler (no steam for its blower)
        /// burns as hard as a working one; null for the engine's own.
        /// </summary>
        public static float? Air;

        /// <summary>The box's surfaces as drawn (0), their albedo alone (1), or the fire's light on white (2).</summary>
        public static int LightView;

        /// <summary>The volume as drawn (0), its flames alone (1), or its smoke alone (2).</summary>
        public static int VolumeView;

        /// <summary>The box's smoke and the flame cards hidden, to see the box behind them.</summary>
        public static bool HideSmoke;
        public static bool HideFlames;

        public static bool HidingSmoke => Enabled && HideSmoke;
        public static bool HidingFlames => Enabled && HideFlames;

        public static bool Enabled => Main.Settings != null && Main.Settings.FireboxTestBedEnabled;

        /// <summary>The flames' draft: the test bed's, when set on a development setup, or <paramref name="fire"/>'s.</summary>
        public static float DraftOr(float fire) => Enabled && Draft is float set ? Mathf.Clamp01(set) : fire;

        /// <summary>Whether the test bed sets the fire's air (on a development setup).</summary>
        public static bool AirSet => Enabled && Air.HasValue;

        /// <summary>The fire's air: the test bed's, when set on a development setup, or <paramref name="engine"/>'s draft.</summary>
        public static float AirOr(float engine) => Enabled && Air is float set ? Mathf.Max(0f, set) : engine;

        /// <summary>
        /// Whether a laid beat falls in the next <paramref name="dt"/> s on a
        /// fire whose beats have run to <paramref name="clock"/> (each fire
        /// keeps its own).
        /// </summary>
        public static bool Beat(ref float clock, float dt)
        {
            if (!Enabled || BeatRate <= 0f)
                return false;
            clock += dt * BeatRate;
            if (clock < 1f)
                return false;
            clock -= Mathf.Floor(clock);
            return true;
        }

        /// <summary>
        /// Sets the firebox's look by name, as "name=value" pairs separated by
        /// ';' (FireboxVolume's, FireboxLighting's, CoalLumps', and FireColors'
        /// tunables, a color as "r,g,b", e.g. "Glow=2.5;Core=2,0.9,0.15"). Returns what
        /// it could not set, or null. A value set holds for later states too,
        /// until <see cref="Clear"/> puts every value set back as it was.
        /// </summary>
        public static string Tune(string pairs)
        {
            if (string.IsNullOrEmpty(pairs))
                return null;
            var unknown = new System.Collections.Generic.List<string>();
            foreach (var pair in pairs.Split(';'))
            {
                var parts = pair.Split('=');
                var field = parts.Length == 2 ? Tunable(parts[0].Trim()) : null;
                var numbers = parts.Length == 2 ? Numbers(parts[1]) : null;
                if (field == null || numbers == null)
                    unknown.Add(pair);
                else if (field.FieldType == typeof(float) && numbers.Length == 1)
                    Set(field, numbers[0]);
                else if (field.FieldType == typeof(Color) && numbers.Length == 3)
                    Set(field, new Color(numbers[0], numbers[1], numbers[2]));
                else
                    unknown.Add(pair);
            }
            return unknown.Count > 0 ? string.Join(";", unknown) : null;
        }

        // Each value set, as it was before.
        private static readonly System.Collections.Generic.Dictionary<System.Reflection.FieldInfo, object> untuned =
            new System.Collections.Generic.Dictionary<System.Reflection.FieldInfo, object>();

        private static void Set(System.Reflection.FieldInfo field, object value)
        {
            if (!untuned.ContainsKey(field))
                untuned[field] = field.GetValue(null);
            field.SetValue(null, value);
        }

        private static System.Reflection.FieldInfo Tunable(string name)
        {
            foreach (var type in new[] { typeof(FireboxVolume), typeof(FireboxLighting), typeof(FireboxFlames), typeof(CoalLumps), typeof(FireColors), typeof(StokerSpray) })
            {
                var field = type.GetField(name, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (field != null && (field.FieldType == typeof(float) || field.FieldType == typeof(Color)) && !field.IsInitOnly && !field.IsLiteral)
                    return field;
            }
            return null;
        }

        private static float[] Numbers(string text)
        {
            var items = text.Split(',');
            var numbers = new float[items.Length];
            for (var i = 0; i < items.Length; i++)
                if (!float.TryParse(items[i].Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out numbers[i]))
                    return null;
            return numbers;
        }

        public static void Clear()
        {
            Draft = null;
            Air = null;
            LightView = 0;
            VolumeView = 0;
            HideSmoke = HideFlames = false;
            BeatRate = 0f;
            BeatStrength = 1f;
            foreach (var entry in untuned)
                entry.Key.SetValue(null, entry.Value);
            untuned.Clear();
        }
    }
}
