using System;
using System.Collections.Generic;
using System.Globalization;
using RealisticFiring.Core;
using UnityEngine;

namespace RealisticFiring.Visuals
{
    /// <summary>
    /// The chimney plume's look that is not the model's: how the particles
    /// are drawn. The test bed edits it live; the defaults are the tuned values.
    /// </summary>
    internal sealed class PlumeLook
    {
 public float Light = 1.6f;

        /// <summary>The smoke's fraying at its edges, 0 fading evenly.</summary>
        public float Fray = 0.5f;

        /// <summary>A particle's width while it goes fast through the air, against its size.</summary>
        public float JetWidth = 0.5f;

        /// <summary>The narrow-jet zone above the smoke emitter at most, m (the stack's lip where it is known).</summary>
        public float JetZone = 1f;

        /// <summary>How hard the plume's eddies stir it, ×.</summary>
        public float Eddies = 1f;

 public float LipWidth = 0.7f;

        /// <summary>
        /// How far above the lip the smoke is held at least the bore's width,
        /// in bore diameters: the photos' neck, 1 to 3 diameters before it spreads.
        /// </summary>
        public float Neck = 2f;

        /// <summary>
        /// Past the neck, how fast the smoke's least width grows, m of radius
        /// a metre: the photos' 0.2 to 0.25 (a half-angle of 10 to 15°).
        /// </summary>
        public float NeckSpread = 0.2f;

        /// <summary>The heat shimmer's bend at its strongest, a share of the view (0.006 until 2026-10-17, too faint to see).</summary>
        public float ShimmerStrength = 0.02f;

 public float ShimmerSize = 0.75f;

        /// <summary>The shimmer's particles a second while it shows.</summary>
        public float ShimmerRate = 20f;

        /// <summary>How long a shimmer particle lives, s (1.2 to 1.8 until 2026-10-17: it ended just past the bend).</summary>
        public float ShimmerLife = 3f;

        /// <summary>How fast the shimmer weakens as it widens: its strength as its starting width over its width to this power.</summary>
        public float ShimmerDiffuse = 1f;

        public PlumeLook Copy() => (PlumeLook)MemberwiseClone();
    }

    /// <summary>An engine's stack, set by hand in the test bed in place of its fire and engine.</summary>
    internal sealed class StackTest
    {
        public string Name;
        public bool FireOn = true;

        /// <summary>The fire's heat over the most it makes, 0 to 1.</summary>
        public float Heat;

        /// <summary>Visible smoke leaving the fire, 0 to 1 (the plume goes black at 0.5).</summary>
        public float Soot;

        /// <summary>How clear the stack is between beats, 0 to 1 (air to spare).</summary>
        public float Clear;

        /// <summary>Steam up the stack between beats, over the stock maximum (the blower's full is 0.1).</summary>
        public float Flow;

        /// <summary>Exhaust beats a second.</summary>
        public float Beats;

        /// <summary>The beats' exhaust pressure, bar (no puff under 1; the hardest at 6, the heaviest 11).</summary>
        public float Exhaust;

        /// <summary>The train's speed the air goes past the stack at, km/h (the engine itself stands).</summary>
        public float Speed;

        /// <summary>The exhaust steam's water share, 0 to 0.3 (cold cylinders, a saturated engine).</summary>
        public float Wetness;

        /// <summary>The kg of air through the fire a kg of exhaust steam.</summary>
        public float AirPerSteam = 2f;

        public StackTest Copy(string name = null)
        {
            var copy = (StackTest)MemberwiseClone();
            if (name != null)
                copy.Name = name;
            return copy;
        }
    }

 internal static class PlumeTestBed
    {
        private const int WindowId = 0x5246_5442; // "RFTB"
        private const float Width = 620f;
        private const float LabelWidth = 210f;
        private const float ValueWidth = 90f;

        /// <summary>The model's tuning every plume uses.</summary>
        public static readonly PlumeTuning Tuning = new PlumeTuning();

        /// <summary>The plume's look every plume uses.</summary>
        public static readonly PlumeLook Look = new PlumeLook();

        /// <summary>Bumped when a look setting that plumes apply once (their materials) changes.</summary>
        public static int LookVersion { get; private set; }

        public static bool Open { get; private set; }

        public static void Toggle()
        {
            Open = !Open;
            if (!Open)
                SaveIfChanged(force: true);
        }

        private const float SaveAfter = 2f;
        private static bool changed;
        private static float changedAt;

        /// <summary>Puts back the values kept in <paramref name="kept"/> (name=value pairs).</summary>
        public static void Restore(string kept)
        {
            if (string.IsNullOrEmpty(kept))
                return;
            foreach (var pair in kept.Split(';'))
            {
                var parts = pair.Split('=');
                if (parts.Length != 2 || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    continue;
                var property = typeof(PlumeTuning).GetProperty(parts[0]);
                if (property != null && property.CanWrite && property.PropertyType == typeof(float))
                    property.SetValue(Tuning, value);
                var field = typeof(PlumeLook).GetField(parts[0]);
                if (field != null && field.FieldType == typeof(float))
                    field.SetValue(Look, value);
            }
            LookVersion++;
        }

        // The values away from the defaults, as name=value pairs.
        private static string Kept()
        {
            var parts = new List<string>();
            foreach (var property in typeof(PlumeTuning).GetProperties())
                if (property.CanWrite && property.PropertyType == typeof(float)
                    && !Mathf.Approximately((float)property.GetValue(Tuning), (float)property.GetValue(DefaultTuning)))
                    parts.Add($"{property.Name}={Format(property.GetValue(Tuning))}");
            foreach (var field in typeof(PlumeLook).GetFields())
                if (field.FieldType == typeof(float) && !Mathf.Approximately((float)field.GetValue(Look), (float)field.GetValue(DefaultLook)))
                    parts.Add($"{field.Name}={Format(field.GetValue(Look))}");
            return string.Join(";", parts);
        }

        private static void Changed()
        {
            changed = true;
            changedAt = Time.unscaledTime;
        }

        private static void SaveIfChanged(bool force = false)
        {
            if (!changed || !force && Time.unscaledTime - changedAt < SaveAfter || Main.Settings == null)
                return;
            changed = false;
            Main.Settings.PlumeTestBed = Kept();
            Main.Settings.Save(Main.Mod);
        }

        // The weather: the game's, or set here.
        public static bool WeatherOn;
        public static float Temperature = 10f;
        public static float Humidity = 0.8f;
        public static float WindSpeed;
        public static float WindToward = 90f;

        // The stack: the engine's own, or set here on the target engine.
        public static bool StackOn;
        private static ChimneyPlume target;

        /// <summary>The engine whose stack the test sets: the nearest plume when the stack test was turned on.</summary>
        public static ChimneyPlume Target => target != null && target.Active ? target : null;

        /// <summary>The stack test for <paramref name="plume"/>, or null when its own fire and engine drive it.</summary>
        public static StackTest StackFor(ChimneyPlume plume) => Open && StackOn && plume != null && plume == Target ? Stack : null;

        /// <summary>The test's air, or null when the game's weather drives the plume.</summary>
        public static PlumeAir? Air => Open && WeatherOn ? new PlumeAir(Temperature, Humidity) : (PlumeAir?)null;

        /// <summary>The test's wind, m/s in the world, when the weather test is on.</summary>
        public static Vector3? Wind
        {
            get
            {
                if (!(Open && WeatherOn))
                    return null;
                var (x, z) = PlumeWind.Toward(WindToward);
                return new Vector3(x, 0f, z) * WindSpeed;
            }
        }

        private struct WeatherPreset
        {
            public string Name;
            public float Temperature;
            public float Humidity;
        }

        private static readonly WeatherPreset[] WeatherPresets =
        {
            new WeatherPreset { Name = "Hot, dry 36° 34% (03)", Temperature = 36.4f, Humidity = 0.34f },
            new WeatherPreset { Name = "Summer day 26° 30% (01)", Temperature = 26f, Humidity = 0.30f },
            new WeatherPreset { Name = "Warm, humid 27° 79% (09)", Temperature = 26.8f, Humidity = 0.79f },
            new WeatherPreset { Name = "Muggy 23.5° 83% (11)", Temperature = 23.5f, Humidity = 0.83f },
            new WeatherPreset { Name = "Mild, dry 15° 31% (18)", Temperature = 14.9f, Humidity = 0.31f },
            new WeatherPreset { Name = "Mild, damp 14° 82% (16)", Temperature = 14.3f, Humidity = 0.82f },
            new WeatherPreset { Name = "Cool, damp 10° 80% (tuned)", Temperature = 10f, Humidity = 0.80f },
            new WeatherPreset { Name = "Cold, dry -1° 41% (29)", Temperature = -1.1f, Humidity = 0.41f },
            new WeatherPreset { Name = "Frosty dawn 2.5° 92% (31)", Temperature = 2.5f, Humidity = 0.92f },
            new WeatherPreset { Name = "Hard frost -7° 85% (27)", Temperature = -6.9f, Humidity = 0.85f },
            new WeatherPreset { Name = "Drizzle 16° 97% (33)", Temperature = 15.8f, Humidity = 0.97f },
            new WeatherPreset { Name = "Fog 8° 99% (32)", Temperature = 8.1f, Humidity = 0.99f },
            new WeatherPreset { Name = "Bitter -20° 70%", Temperature = -20f, Humidity = 0.70f },
        };

        private static readonly StackTest[] StackPresets =
        {
            new StackTest { Name = "Fire out", FireOn = false },
            new StackTest { Name = "Banked fire", Heat = 0.05f, Soot = 0.02f, Clear = 0.5f, AirPerSteam = 20f },
            new StackTest { Name = "Clean hot fire, standing", Heat = 0.3f, Clear = 1f, AirPerSteam = 20f },
            new StackTest { Name = "Blower on, standing", Heat = 0.35f, Soot = 0.03f, Clear = 0.2f, Flow = 0.1f, AirPerSteam = 6f },
            new StackTest { Name = "Fresh charge, door shut", Heat = 0.3f, Soot = 0.34f, Flow = 0.05f, AirPerSteam = 6f },
            new StackTest { Name = "Overfired (black)", Heat = 0.6f, Soot = 0.6f, Flow = 0.4f, Beats = 6f, Exhaust = 4f, Speed = 25f },
            new StackTest { Name = "Thin fire, air to spare", Heat = 0.3f, Clear = 1f, Flow = 0.3f, Beats = 6f, Exhaust = 2.5f, Speed = 25f, AirPerSteam = 3f },
            new StackTest { Name = "Well fired, working (light gray)", Heat = 0.5f, Soot = 0.08f, Flow = 0.45f, Beats = 6f, Exhaust = 4f, Speed = 30f },
            new StackTest { Name = "Working light, 50 km/h", Heat = 0.35f, Soot = 0.05f, Flow = 0.3f, Beats = 12.6f, Exhaust = 2.5f, Speed = 50f },
            new StackTest { Name = "Starting a train", Heat = 0.8f, Soot = 0.12f, Flow = 0.7f, Beats = 2.5f, Exhaust = 7f, Speed = 10f },
            new StackTest { Name = "Slogging up a grade", Heat = 1f, Soot = 0.15f, Flow = 0.9f, Beats = 5f, Exhaust = 11f, Speed = 20f },
            new StackTest { Name = "Fast running, 90 km/h", Heat = 0.6f, Soot = 0.05f, Flow = 0.55f, Beats = 22f, Exhaust = 3f, Speed = 90f },
            new StackTest { Name = "Drifting, 60 km/h", Heat = 0.2f, Soot = 0.02f, Beats = 15f, Exhaust = 0.5f, Speed = 60f, AirPerSteam = 20f },
            new StackTest { Name = "Cold start, wet cylinders", Heat = 0.5f, Soot = 0.1f, Flow = 0.6f, Beats = 1.5f, Exhaust = 6f, Speed = 3f, Wetness = 0.15f },
        };

        // Declared after its presets: static fields start in order.
        public static StackTest Stack = StackPresets[7].Copy();


        private static readonly PlumeTuning DefaultTuning = new PlumeTuning();
        private static readonly PlumeLook DefaultLook = new PlumeLook();

        private static Vector2? position;
        private static Vector2 scroll;
        private static float height = 640f;

        /// <summary>Draws the window when open; Alt frees the mouse to use it.</summary>
        public static void Draw()
        {
            SaveIfChanged();
            if (!Open)
                return;
            var k = OverlayStyle.Scale;
            var matrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(k, k, 1f));
            var screen = new Vector2(Screen.width / k, Screen.height / k);
            height = Mathf.Min(900f, screen.y - 40f);
            var at = position ?? new Vector2(24f, 60f);
            var rect = GUI.Window(WindowId, new Rect(at, new Vector2(Width, height)), DrawWindow, "Plume test bed (Alt frees the mouse)");
            position = new Vector2(Mathf.Clamp(rect.x, 0f, Mathf.Max(0f, screen.x - Width)), Mathf.Clamp(rect.y, 0f, Mathf.Max(0f, screen.y - height)));
            GUI.matrix = matrix;
        }

        private static void DrawWindow(int id)
        {
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(height - 30f));
            DrawWeather();
            GUILayout.Space(8f);
            DrawStack();
            GUILayout.Space(8f);
            DrawReadout();
            GUILayout.Space(8f);
            DrawLook();
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0f, 0f, Width, 20f));
        }

        private static void DrawWeather()
        {
            Header("Weather");
            WeatherOn = Choice(WeatherOn, "The game's", "Set here");
            if (WeatherOn)
            {
                var chosen = Grid(Array.ConvertAll(WeatherPresets, p => p.Name), 2);
                if (chosen >= 0)
                {
                    Temperature = WeatherPresets[chosen].Temperature;
                    Humidity = WeatherPresets[chosen].Humidity;
                }
                Slider("Temperature, °C", ref Temperature, -25f, 40f, "F1");
                var percent = Humidity * 100f;
                Slider("Humidity, %", ref percent, 5f, 100f, "F0");
                Humidity = percent / 100f;
                Slider("Wind, m/s", ref WindSpeed, 0f, 15f, "F1");
                Slider("Wind toward, °", ref WindToward, 0f, 360f, "F0");
            }
            var air = PlumeWeather.Air;
            GUILayout.Label($"Air: {air}, {air.SaturationDeficit:F1} hPa short of saturation; from {PlumeWeather.Source}");
        }

        private static void DrawStack()
        {
            Header("Stack");
            var was = StackOn;
            StackOn = Choice(StackOn, "The engine's", "Set here");
            if (StackOn && (!was || Target == null))
                target = ChimneyPlume.Nearest();
            if (!StackOn)
                return;
            GUILayout.BeginHorizontal();
            GUILayout.Label(Target != null ? $"On {Target.CarName} (it stands; the air goes past at the speed set)" : "No engine with a plume near");
            if (GUILayout.Button("Nearest", GUILayout.Width(90f)))
                target = ChimneyPlume.Nearest();
            GUILayout.EndHorizontal();
            var chosen = Grid(Array.ConvertAll(StackPresets, p => p.Name), 2);
            if (chosen >= 0)
                Stack = StackPresets[chosen].Copy();
            var s = Stack;
            s.FireOn = GUILayout.Toggle(s.FireOn, "Fire lit");
            Slider("Train's speed, km/h", ref s.Speed, 0f, 120f, "F0");
            Slider("Beats a second", ref s.Beats, 0f, 25f, "F1");
            Slider("Exhaust, bar", ref s.Exhaust, 0f, 12f, "F1");
            Slider("Steam between beats", ref s.Flow, 0f, 1f, "F2");
            Slider("Soot (black at 0.5)", ref s.Soot, 0f, 1f, "F2");
            Slider("Clear stack (air to spare)", ref s.Clear, 0f, 1f, "F2");
            Slider("Fire's heat, share", ref s.Heat, 0f, 1f, "F2");
            Slider("Air through the fire, kg/kg steam", ref s.AirPerSteam, 0f, 20f, "F1");
            Slider("Exhaust wetness", ref s.Wetness, 0f, 0.3f, "F2");
        }

        private static void DrawReadout()
        {
            Header("What the plume does");
            var plume = Target ?? ChimneyPlume.Nearest();
            if (plume == null)
            {
                GUILayout.Label("No engine with a plume near");
                return;
            }
            GUILayout.Label(plume.Readout());
        }

        private static void DrawLook()
        {
            Header("Steam and smoke");
            Slider("Flue gas mixing into the steam", () => Tuning.FlueGasMixing, v => Tuning.FlueGasMixing = v, 0f, 1.5f, "F2");
            Slider("Steam mixing into the air, ×", () => Tuning.SteamMixing, v => Tuning.SteamMixing = v, 0.25f, 10f, "F2");
            Slider("White forming above the stack, ×", () => Tuning.SteamForming, v => Tuning.SteamForming = v, 0.1f, 10f, "F2");
            Slider("Steam opacity (tuned air)", () => Tuning.SteamOpacity, v => Tuning.SteamOpacity = v, 0.2f, 0.95f, "F2");
            Slider("Clean haze opacity", () => Tuning.HazeOpacity, v => Tuning.HazeOpacity = v, 0f, 0.5f, "F2");
            Slider("Haze in a well-fired engine's beats", () => Tuning.BeatHaze, v => Tuning.BeatHaze = v, 0f, 1f, "F2");
            Slider("Puff size, ×", () => Tuning.PuffSize, v => Tuning.PuffSize = v, 0.4f, 2.5f, "F2");
            Slider("Jet length, m", () => Tuning.JetLength, v => Tuning.JetLength = v, 1f, 8f, "F1");
            Slider("Buoyancy, ×", () => Tuning.Buoyancy, v => Tuning.Buoyancy = v, 0.2f, 3f, "F2");
            Slider("Wake: share of train speed", () => Tuning.WakeCarry, v => Tuning.WakeCarry = v, 0f, 0.6f, "F2");
            Slider("Wake: falls away over, m", () => Tuning.WakeDepth, v => Tuning.WakeDepth = v, 0.2f, 6f, "F1");
            Header("Look");
            Kept("Width at the lip, × bore", ref Look.LipWidth, 0.4f, 2f, "F2");
            Kept("Neck, bore diameters", ref Look.Neck, 0f, 5f, "F1");
            Kept("Spread past the neck, m/m", ref Look.NeckSpread, 0f, 0.6f, "F2");
            Kept("Jet narrowing while fast", ref Look.JetWidth, 0.2f, 1f, "F2");
            Kept("Eddies, ×", ref Look.Eddies, 0f, 3f, "F2");
            LookSlider("Plume light, ×", ref Look.Light, 0.8f, 2.5f, "F2");
            LookSlider("Fraying", ref Look.Fray, 0f, 1f, "F2");
            Header("Heat shimmer");
            Slider("Shimmer, ×", () => Tuning.Shimmer, v => Tuning.Shimmer = v, 0f, 3f, "F2");
            LookSlider("Shimmer bend, share of view", ref Look.ShimmerStrength, 0f, 0.06f, "F3");
            Kept("Shimmer width, × lip", ref Look.ShimmerSize, 0.3f, 3f, "F2");
            Kept("Shimmer particles a second", ref Look.ShimmerRate, 2f, 60f, "F0");
            LookSlider("Shimmer life, s", ref Look.ShimmerLife, 0.5f, 8f, "F1");
            Kept("Shimmer weakening as it widens", ref Look.ShimmerDiffuse, 0f, 3f, "F2");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset to the defaults"))
                Reset();
            if (GUILayout.Button("Write to the log"))
                Main.Mod.Logger.Log(Describe());
            GUILayout.EndHorizontal();
        }

        private static void Reset()
        {
            foreach (var property in typeof(PlumeTuning).GetProperties())
                if (property.CanWrite)
                    property.SetValue(Tuning, property.GetValue(DefaultTuning));
            foreach (var field in typeof(PlumeLook).GetFields())
                field.SetValue(Look, field.GetValue(DefaultLook));
            LookVersion++;
            Changed();
        }

        /// <summary>Every test bed value in a line, for the log.</summary>
        public static string Describe()
        {
            var parts = new List<string>();
            foreach (var property in typeof(PlumeTuning).GetProperties())
                parts.Add($"{property.Name} {Format(property.GetValue(Tuning))}");
            foreach (var field in typeof(PlumeLook).GetFields())
                parts.Add($"{field.Name} {Format(field.GetValue(Look))}");
            var weather = WeatherOn ? $"air {Temperature:F1} °C {Humidity * 100f:F0} %, wind {WindSpeed:F1} m/s toward {WindToward:F0}°" : $"the game's air ({PlumeWeather.Air})";
            var stack = StackOn && Target != null
                ? $"stack '{Stack.Name}' on {Target.CarName}: fire {(Stack.FireOn ? "lit" : "out")}, heat {Stack.Heat:F2}, soot {Stack.Soot:F2}, clear {Stack.Clear:F2}, " +
                  $"flow {Stack.Flow:F2}, {Stack.Beats:F1} beats/s at {Stack.Exhaust:F1} bar, {Stack.Speed:F0} km/h, wetness {Stack.Wetness:F2}, air {Stack.AirPerSteam:F1} kg/kg"
                : "the engine's own stack";
            return $"Plume test bed: {weather}; {stack}; {string.Join(", ", parts)}";
        }

        private static string Format(object value) =>
            value is float f ? f.ToString("0.###", CultureInfo.InvariantCulture) : Convert.ToString(value, CultureInfo.InvariantCulture);

        private static GUIStyle header;

        private static void Header(string text)
        {
            header = header ?? new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            GUILayout.Label(text, header);
        }

        private static bool Choice(bool on, string off, string set)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(!on, off, "Button"))
                on = false;
            if (GUILayout.Toggle(on, set, "Button"))
                on = true;
            GUILayout.EndHorizontal();
            return on;
        }

        // A preset grid: the one pressed this frame, or -1.
        private static int Grid(string[] names, int columns)
        {
            var pressed = -1;
            for (var i = 0; i < names.Length; i += columns)
            {
                GUILayout.BeginHorizontal();
                for (var j = i; j < Math.Min(names.Length, i + columns); j++)
                    if (GUILayout.Button(names[j]))
                        pressed = j;
                GUILayout.EndHorizontal();
            }
            return pressed;
        }

        private static void Slider(string label, ref float value, float min, float max, string format)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(LabelWidth));
            value = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.Label(value.ToString(format, CultureInfo.InvariantCulture), GUILayout.Width(ValueWidth));
            GUILayout.EndHorizontal();
        }

        // A look or tuning slider: kept in the settings when moved.
        private static void Kept(string label, ref float value, float min, float max, string format)
        {
            var before = value;
            Slider(label, ref value, min, max, format);
            if (!Mathf.Approximately(before, value))
                Changed();
        }

        private static void Slider(string label, Func<float> get, Action<float> set, float min, float max, string format)
        {
            var value = get();
            Slider(label, ref value, min, max, format);
            if (!Mathf.Approximately(value, get()))
            {
                set(value);
                Changed();
            }
        }

        // A look setting the plumes put on their materials once: changing it re-applies.
        private static void LookSlider(string label, ref float value, float min, float max, string format)
        {
            var before = value;
            Slider(label, ref value, min, max, format);
            if (!Mathf.Approximately(before, value))
            {
                LookVersion++;
                Changed();
            }
        }
    }
}
