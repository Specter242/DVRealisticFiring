using System;
using System.Collections.Generic;
using System.Globalization;
using RealisticStackEffects.Core;
using UnityEngine;

namespace RealisticStackEffects
{
    /// <summary>
    /// The chimney plume's look that is not the model's: how the particles
    /// are drawn. The test bed edits it live; the defaults are the tuned values.
    /// </summary>
    internal sealed class PlumeLook
    {
 public float Light = 1.1f;

        /// <summary>The smoke's fraying at its edges, 0 fading evenly.</summary>
        public float Fray = 0.5f;

        public float SkyLight = 1.5f;
        public float SunLight = 0.65f;
        public float MaxLight = 2f;
        public float Wrap = 0.4f;
        public float Relief = 0.6f;
        public float Bulge = 0.1f;
        public float SelfShadow = 0.5f;
        public float Scatter = 0.25f;

        public float Flat = 1f;
        public float Terminator = 0.25f;
        public float ShadeLight = 0.6f;
        public float SunShadow = 0f;
        public float ShadowDensity = 0.05f;

        public float Volume = 1f;
        public float VolumeShape = 1f;
        public float VolumeDensity = 0.03f;

        /// <summary>
        /// Smoke at the camera fades, shown from this far, m, fully from
        /// this (2026-10-21: inside a fast train's trail the view was a flat
        /// white wall of the nearest particles).
        /// </summary>
        public float NearFadeFrom = 0.5f;
        public float NearFadeTo = 4f;

        /// <summary>The light on the plume greyed toward its brightness, 0 to 1.</summary>
        public float LightGrey = 0f;

        public float NightSkyLight = 0.7f;
        public float NightGrey = 0.5f;

        /// <summary>
        /// A plume particle's size, × the plume's width where it is (0.75 until
        /// 2026-10-21, then 0.9, then 0.6: with the trail spreading faster,
        /// its wisps tens of meters wide drew soft; 0.75 again with 1.13's
        /// wisps and spreading, 2026-10-23).
        /// </summary>
        public float FillWidth = 0.75f;

 public float SmokeFillWidth = 0.75f;

        /// <summary>How far out a particle drifts from where it left the stack, × the plume's widening (0.3 until 2026-10-21).</summary>
        public float Disperse = 0.4f;

        /// <summary>
        /// How far a particle's smoke's opacity is shared with those
        /// overlapping it, 0 (each as opaque as the plume) to 1 (PlumeAloft):
        /// at 1 a fresh charge's smoke all but vanished, the wisps covering
        /// only a fifth of their quads.
        /// </summary>
        public float Overlap = 0.5f;

 public float SmokeAsSteamFrom = 0.5f;

 public float WispLargestFrom = 60f;
        public float WispLargestTo = 150f;
        public float SmokeAsSteamTo = 0.85f;


        /// <summary>How far each particle is lit as its part of the plume's surface, × (0 each alone; PlumeAloft.ShapeOf).</summary>
        public float PlumeShape = 0f;

        /// <summary>How far a beat's particles leave as one cluster, a core and lobes beside it, 0 (strung up the jet) to 1.</summary>
        public float BeatCluster = 0.6f;

        /// <summary>
        /// How far thick steam is drawn as round billows, 0 to 1 (the shader's
        /// second flipbook), from a particle's opacity <see cref="BillowFrom"/>
        /// to <see cref="BillowFull"/>, the wisps as it thins.
        /// </summary>
        public float Billow = 1f;
        public float BillowFrom = 0.3f;
        public float BillowFull = 0.8f;

 public float BillowAge = 1f;

        /// <summary>The surface's up across the particle, ±1 (which way the texture's normal reads).</summary>
        public float BinormalSign = 1f;

        /// <summary>A particle's width while it goes fast through the air, against its size.</summary>
        public float JetWidth = 0.5f;

        /// <summary>The narrow-jet zone above the smoke emitter at most, m (the stack's lip where it is known).</summary>
        public float JetZone = 1f;

        /// <summary>How hard the plume's eddies stir it, ×.</summary>
        public float Eddies = 1f;

 public float RollRate = 0.5f;
        public float RollSlows = 3f;

        /// <summary>Rolling steam: how far the plume's far side from the sun shades its puffs, 0 to 1.</summary>
        public float SixPlume = 0.5f;

        /// <summary>
        /// Rolling steam: the sun's light a puff scatters into its shade, 0 to
        /// 1 (at 0.6 its lobes hardly showed; 0.35 matched the photos' shade).
        /// </summary>
        public float RollShade = 0.4f;

 public float ThickFloor = 0.05f;

        /// <summary>Development: rolling steam's opacity drawn red, × this (0 off), to find faint coverage.</summary>
        public float DebugAlpha = 0f;

        /// <summary>Development: the cloud and sooty core hidden (above 0), to time what drawing them costs.</summary>
        public float HidePlume = 0f;

        /// <summary>Development: the jet's streaks hidden (above 0).</summary>
        public float HideJet = 0f;

        /// <summary>Development: the heat shimmer hidden (above 0).</summary>
        public float HideHeat = 0f;

        /// <summary>
        /// Development: the smoke at half resolution laid over the scene as
        /// 1, a magenta tint over the whole screen; 2, its coverage in red;
        /// 3, the scene's depth in green and the half-size depth in blue,
        /// each a hundredth per meter (<see cref="PlumeOffscreen"/>); 0 off.
        /// </summary>
        public float OffscreenDebug = 0f;

        /// <summary>
        /// Rolling steam: the share of the sun's light scattered into a puff's
        /// shade that takes the sky's color rather than the sun's, 0 to 1
        /// (none drew a dawn's whole plume peach; 0.6 tinted a day's plume blue-gray).
        /// </summary>
        public float ShadeSky = 0.3f;

 public float RiseShown = 0.7f;

 public float WispBelow = 0.15f;
        public float WispAbove = 0.6f;

 public float WispFrayEdge = 0.5f;

 public float WispAgeFrom = 0.2f;
        public float WispAgeTo = 0.45f;
        // Limit the rounded trail at speed; 0 retains the stationary handoff.
        public float PuffTrail = 10f;
        // Development comparison with the old, mismatched steam priorities.
        public float MatchSteamOrder = 1f;

        /// <summary>
        /// Rolling steam: how far a wisp blends to its next frame, 0 to 1 (0:
        /// it steps, as the plume's flipbook did on 2026-10-21).
        /// </summary>
        public float WispBlend = 0f;

 public float WispSheet = 1f;

        /// <summary>
        /// Rolling steam: 1, each wisp keeps its own row of the sheet, from a
        /// frame of its own past the young compact ones; 0, the whole sheet
        /// in turn (WispSheet), one row blending into another, its strands
        /// fading out and others in (Sol, 2026-10-23: the light firing's
        /// flashing; its trail crisper, its curls clearer).
        /// </summary>
        public float WispCoherence = 1f;

 public float WispHold = 0f;

 public float WispFadeFrom = 0.8f;

 public float ThinWispAgeFrom = 0.1f;
        public float ThinWispAgeTo = 0.3f;
        public float LongPuffsFrom = 0.6f;
        public float LongPuffsBy = 1f;

        /// <summary>
        /// Rolling steam: 1, the dead dropped before the particles are set,
        /// so each keeps its own custom data (Sol, 2026-10-23); 0, a survivor
        /// moved into a dead one's place drew its wisp for a frame.
        /// </summary>
        public float StableParticleData = 1f;

 public float WispBaked = 1f;

        /// <summary>
        /// Rolling steam: 1, each wisp at its own turn, any way, slowly
        /// spinning, as 1.13's; 0, its particle's, near upright as the puffs:
        /// all its strands lay one way, a grain round a rope (2026-10-23).
        /// </summary>
        public float WispTurn = 1f;

 public float WispSpin = 0.3f;
        public float WispSpinEase = 1f;

        /// <summary>Those wisps lit as 1.13 lit them (WispPuffLit 0): its plume light.</summary>
        public float WispBakedLight = 1.6f;

        /// <summary>
        /// Rolling steam: 1, those wisps lit as the puffs are, as a puff of the
        /// flipbook's mean six ways' light, at WispPuffLight of it (by 1.13's
        /// light the wisps were tan and the puffs white, and each puff handing
        /// off drew a pale disc with a tan rim).
        /// </summary>
        public float WispPuffLit = 1f;
        public float WispPuffLight = 1f;

 public float WispShare = 0f;

 public float WispRelief = 1f;
        public float WispBulge = 0.4f;
        public float WispScatter = 0.5f;
        public float WispSunLight = 0.9f;

        /// <summary>Rolling steam: opacity below this is drawn as none (a pixel dropped unblended, the plume's cost mostly the blending).</summary>
        public float LeastOpacity = 0.002f;

 public float Fringe = 0.8f;
        public float FringeCore = 0.5f;

 public float Upright = 1f;
        public float Grow = 1f;
        public float WispReach = 1f;

        /// <summary>Rolling steam: a fringe's strands' opacity, × its particle's wisp's, 0 to 1.</summary>
        public float FringeShare = 1f;

        /// <summary>
        /// Rolling steam: a particle is fringed from this far on the plume's
        /// silhouette (its way out from the plume's middle across the view, ×
        /// how far out; PlumeAloft.ExtraOf), wholly by the next: over the
        /// plume's faces turned toward the view the strands veiled its lobes.
        /// Set for the measure across the plume (<see cref="ShapeAcross"/>):
        /// from its age band's middle, 0.45 and 0.8, along the trail too it
        /// measured particles further out, and across at those a puff frayed
        /// a tenth as much (2026-10-24: as many fray from 0.3 and 0.55).
        /// </summary>
        public float FringeRimFrom = 0.3f;
        public float FringeRimTo = 0.55f;

 public float ShapeAcross = 1f;

 public float ShapeSmoothing = 1f;

 public float SootAlbedo = 0.04f;

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

        /// <summary>Every tuning and look value, as name=value pairs <see cref="Restore"/> takes (the capture run's, to put them back).</summary>
        internal static string Snapshot()
        {
            var parts = new List<string>();
            foreach (var property in typeof(PlumeTuning).GetProperties())
                if (property.CanWrite && property.PropertyType == typeof(float))
                    parts.Add($"{property.Name}={((float)property.GetValue(Tuning)).ToString("R", CultureInfo.InvariantCulture)}");
            foreach (var field in typeof(PlumeLook).GetFields())
                if (field.FieldType == typeof(float))
                    parts.Add($"{field.Name}={((float)field.GetValue(Look)).ToString("R", CultureInfo.InvariantCulture)}");
            return string.Join(";", parts);
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
            if (!changed || !force && Time.unscaledTime - changedAt < SaveAfter || StackHost.Settings == null)
                return;
            changed = false;
            StackHost.Settings.TestBed = Kept();
            StackHost.Settings.Save();
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

        /// <summary>The engine the stack test was last aimed at, gone or not.</summary>
        internal static ChimneyPlume Aimed => target;

        /// <summary>Aims the stack test at <paramref name="plume"/> (the capture run's engine).</summary>
        internal static void Aim(ChimneyPlume plume) => target = plume;

        // The test bed's settings drive the plume while its window is open or a capture runs.
        private static bool Driving => Open || PlumeCapture.Running;

        /// <summary>The stack test for <paramref name="plume"/>, or null when its own fire and engine drive it.</summary>
        public static StackTest StackFor(ChimneyPlume plume) => Driving && StackOn && plume != null && plume == Target ? Stack : null;

        /// <summary>The test's air, or null when the game's weather drives the plume.</summary>
        public static PlumeAir? Air => Driving && WeatherOn ? new PlumeAir(Temperature, Humidity) : (PlumeAir?)null;

        /// <summary>The test's wind, m/s in the world, when the weather test is on.</summary>
        public static Vector3? Wind
        {
            get
            {
                if (!(Driving && WeatherOn))
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
            // Photo 19: a K-36 hard at the foot of a 4 % grade on a smoky fire,
            // 15 to 25 km/h with a following breeze (the air past it about 10).
            new StackTest { Name = "Hard and smoky (19)", Heat = 1f, Soot = 0.6f, Flow = 0.9f, Beats = 4f, Exhaust = 11f, Speed = 10f },
            new StackTest { Name = "Fast running, 90 km/h", Heat = 0.6f, Soot = 0.05f, Flow = 0.55f, Beats = 22f, Exhaust = 3f, Speed = 90f },
            new StackTest { Name = "Drifting, 60 km/h", Heat = 0.2f, Soot = 0.02f, Beats = 15f, Exhaust = 0.5f, Speed = 60f, AirPerSteam = 20f },
            new StackTest { Name = "Cold start, wet cylinders", Heat = 0.5f, Soot = 0.1f, Flow = 0.6f, Beats = 1.5f, Exhaust = 6f, Speed = 3f, Wetness = 0.15f },
        };

        // Declared after its presets: static fields start in order.
        public static StackTest Stack = StackPresets[7].Copy();

        /// <summary>The weather preset named <paramref name="name"/> or starting so.</summary>
        internal static bool WeatherNamed(string name, out float temperature, out float humidity)
        {
            var found = Find(WeatherPresets, p => p.Name, name);
            temperature = found >= 0 ? WeatherPresets[found].Temperature : 0f;
            humidity = found >= 0 ? WeatherPresets[found].Humidity : 0f;
            return found >= 0;
        }

        /// <summary>A copy of the stack preset named <paramref name="name"/> or starting so, or null.</summary>
        internal static StackTest StackNamed(string name)
        {
            var found = Find(StackPresets, p => p.Name, name);
            return found >= 0 ? StackPresets[found].Copy() : null;
        }

        private static int Find<T>(T[] presets, Func<T, string> named, string name)
        {
            var found = Array.FindIndex(presets, p => string.Equals(named(p), name, StringComparison.OrdinalIgnoreCase));
            return found >= 0 || name == null ? found : Array.FindIndex(presets, p => named(p).StartsWith(name, StringComparison.OrdinalIgnoreCase));
        }


        private static readonly PlumeTuning DefaultTuning = new PlumeTuning();
        private static readonly PlumeLook DefaultLook = new PlumeLook();

        private static Vector2? position;
        private static Vector2 scroll;
        private static float height = 640f;

        /// <summary>Draws the window when open; Alt frees the mouse to use it.</summary>
        public static void Draw()
        {
            SaveIfChanged();
            if (!Open || PlumeCapture.Running)
                return;
            var k = StackHost.GuiScale();
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
            Slider("Flue gas mixing into the blower's jet", () => Tuning.BlowerFlueGasMixing, v => Tuning.BlowerFlueGasMixing = v, 0f, 1.5f, "F2");
            Slider("Steam mixing into the air, ×", () => Tuning.SteamMixing, v => Tuning.SteamMixing = v, 0.25f, 10f, "F2");
            Slider("White forming above the stack, ×", () => Tuning.SteamForming, v => Tuning.SteamForming = v, 0.1f, 10f, "F2");
            Slider("Steam opacity (tuned air)", () => Tuning.SteamOpacity, v => Tuning.SteamOpacity = v, 0.2f, 0.95f, "F2");
            Slider("Clean haze opacity", () => Tuning.HazeOpacity, v => Tuning.HazeOpacity = v, 0f, 0.5f, "F2");
            Slider("Haze in a well-fired engine's beats", () => Tuning.BeatHaze, v => Tuning.BeatHaze = v, 0f, 1f, "F2");
            Slider("Puff size, ×", () => Tuning.PuffSize, v => Tuning.PuffSize = v, 0.4f, 2.5f, "F2");
            Slider("Jet length, m", () => Tuning.JetLength, v => Tuning.JetLength = v, 1f, 8f, "F1");
            Slider("Buoyancy, ×", () => Tuning.Buoyancy, v => Tuning.Buoyancy = v, 0.2f, 3f, "F2");
            Slider("Trail spreading, ×", () => Tuning.TrailSpread, v => Tuning.TrailSpread = v, 0.25f, 5f, "F2");
            Slider("Soot darkening curve", () => Tuning.SootCurve, v => Tuning.SootCurve = v, 0.5f, 3f, "F2");
            Slider("Black smoke opacity", () => Tuning.SootOpacity, v => Tuning.SootOpacity = v, 0.3f, 0.98f, "F2");
            Slider("Wake: share of train speed", () => Tuning.WakeCarry, v => Tuning.WakeCarry = v, 0f, 0.6f, "F2");
            Slider("Wake: falls away over, m", () => Tuning.WakeDepth, v => Tuning.WakeDepth = v, 0.2f, 6f, "F1");
            Header("Look");
            Kept("Width at the lip, × bore", ref Look.LipWidth, 0.4f, 2f, "F2");
            Kept("Neck, bore diameters", ref Look.Neck, 0f, 5f, "F1");
            Kept("Spread past the neck, m/m", ref Look.NeckSpread, 0f, 0.6f, "F2");
            Kept("Jet narrowing while fast", ref Look.JetWidth, 0.2f, 1f, "F2");
            Kept("Eddies, ×", ref Look.Eddies, 0f, 3f, "F2");
            Kept("Particle width, × plume's", ref Look.FillWidth, 0.3f, 1.5f, "F2");
            Kept("Dense smoke's particle width, × plume's", ref Look.SmokeFillWidth, 0.3f, 1.5f, "F2");
            Kept("Drift across the plume", ref Look.Disperse, 0f, 1f, "F2");
            Kept("Smoke's opacity shared by overlapping particles", ref Look.Overlap, 0f, 1.5f, "F2");
            Kept("Smoke drawn as steam from, opacity at the lip", ref Look.SmokeAsSteamFrom, 0f, 1f, "F2");
            Kept("Wholly as steam by", ref Look.SmokeAsSteamTo, 0f, 1f, "F2");
            Kept("Wisps no wider than, m", ref Look.WispLargestFrom, 10f, 500f, "F0");
            Kept("Wholly puffs from, m", ref Look.WispLargestTo, 10f, 800f, "F0");
            LookSlider("Plume light, ×", ref Look.Light, 0.4f, 3f, "F2");
            LookSlider("Fraying", ref Look.Fray, 0f, 1f, "F2");
            LookSlider("Sky light, ×", ref Look.SkyLight, 0f, 6f, "F2");
            LookSlider("Sun light, ×", ref Look.SunLight, 0f, 3f, "F2");
            LookSlider("Brightest light", ref Look.MaxLight, 0.5f, 5f, "F2");
            LookSlider("Sun wrapping around", ref Look.Wrap, 0f, 1f, "F2");
            LookSlider("Surface relief", ref Look.Relief, 0f, 2f, "F2");
            LookSlider("Roundness", ref Look.Bulge, 0f, 1f, "F2");
            LookSlider("Thick smoke's own shadow", ref Look.SelfShadow, 0f, 1f, "F2");
            LookSlider("Sun through thin smoke", ref Look.Scatter, 0f, 3f, "F2");
            LookSlider("Light greyed", ref Look.LightGrey, 0f, 1f, "F2");
            LookSlider("Lit as the plume's surface", ref Look.PlumeShape, 0f, 3f, "F2");
            Kept("Beats leave as clusters", ref Look.BeatCluster, 0f, 1f, "F2");
            LookSlider("Billows where thick", ref Look.Billow, 0f, 1f, "F2");
            LookSlider("Billows from opacity", ref Look.BillowFrom, 0f, 1f, "F2");
            LookSlider("Billows fully at opacity", ref Look.BillowFull, 0f, 1f, "F2");
            LookSlider("Billows roll up over, s", ref Look.BillowAge, 0f, 3f, "F2");
            LookSlider("Flat-lit, with a terminator", ref Look.Flat, 0f, 1f, "F2");
            LookSlider("Terminator's softness", ref Look.Terminator, 0.02f, 1f, "F2");
            LookSlider("Sun diffused into the shade", ref Look.ShadeLight, 0f, 1f, "F2");
            LookSlider("Shadowed by the plume", ref Look.SunShadow, 0f, 1f, "F2");
            LookSlider("Plume's shadow density, ×", ref Look.ShadowDensity, 0f, 5f, "F2");
            LookSlider("Lit as one volume", ref Look.Volume, 0f, 1f, "F2");
            LookSlider("The volume's shape in the light", ref Look.VolumeShape, 0f, 3f, "F2");
            LookSlider("The volume's shadow density, ×", ref Look.VolumeDensity, 0f, 2f, "F3");
            LookSlider("Rolling: loops a second off the jet", ref Look.RollRate, 0f, 3f, "F2");
            LookSlider("Rolling: halves by, s", ref Look.RollSlows, 0f, 20f, "F1");
            LookSlider("Rolling: far side shaded", ref Look.SixPlume, 0f, 1f, "F2");
            LookSlider("Rolling: sun into the shade", ref Look.RollShade, 0f, 1f, "F2");
            LookSlider("Rolling: thickness taken as none below", ref Look.ThickFloor, 0f, 0.1f, "F3");
            LookSlider("Rolling: the shade's light from the sky", ref Look.ShadeSky, 0f, 1f, "F2");
            LookSlider("Rolling: wholly a wisp at or below", ref Look.WispBelow, 0f, 1f, "F2");
            LookSlider("Rolling: wholly a puff at or above", ref Look.WispAbove, 0f, 1f, "F2");
            LookSlider("Rolling: a thinning wisp grows by, × fray (1.12 as the puffs)", ref Look.WispFrayEdge, 0f, 1.5f, "F2");
            LookSlider("Rolling: wisps from, share of life", ref Look.WispAgeFrom, 0f, 1f, "F2");
            LookSlider("Rolling: wholly wisps by", ref Look.WispAgeTo, 0f, 1f, "F2");
            LookSlider("Rolling: wisps' frames blended", ref Look.WispBlend, 0f, 1f, "F2");
            LookSlider("Rolling: wisps play the whole sheet", ref Look.WispSheet, 0f, 1f, "F0");
            LookSlider("Rolling: wisps each keep their own row", ref Look.WispCoherence, 0f, 1f, "F0");
            LookSlider("Rolling: so kept, each holds one frame", ref Look.WispHold, 0f, 1f, "F0");
            LookSlider("Rolling: strands fade out from, share of reach", ref Look.WispFadeFrom, 0f, 1f, "F2");
            LookSlider("Rolling: a light beat's wisps from", ref Look.ThinWispAgeFrom, 0f, 1f, "F2");
            LookSlider("Rolling: a light beat's wholly wisps by", ref Look.ThinWispAgeTo, 0f, 1f, "F2");
            LookSlider("Rolling: puffs longer from beat strength", ref Look.LongPuffsFrom, 0f, 1f, "F2");
            LookSlider("Rolling: longest by", ref Look.LongPuffsBy, 0.01f, 1f, "F2");
            LookSlider("Rolling: custom data stays with its particle", ref Look.StableParticleData, 0f, 1f, "F0");
            LookSlider("Rolling: wisps as 1.13 drew them", ref Look.WispBaked, 0f, 1f, "F0");
            LookSlider("Rolling: wisps at their own turn", ref Look.WispTurn, 0f, 1f, "F0");
            Kept("Rolling: wisps' most spin, rad/s", ref Look.WispSpin, 0f, 1f, "F2");
            Kept("Rolling: their spin halved by, s (0 steady)", ref Look.WispSpinEase, 0f, 8f, "F1");
            LookSlider("Rolling: their light (1.13's plume light)", ref Look.WispBakedLight, 0f, 3f, "F2");
            LookSlider("Rolling: those wisps lit as the puffs", ref Look.WispPuffLit, 0f, 1f, "F0");
            LookSlider("Rolling: so lit, their share of the puffs' light", ref Look.WispPuffLight, 0f, 2f, "F2");
            Kept("Rolling: wisps' steam shared by overlapping particles", ref Look.WispShare, 0f, 1f, "F2");
            LookSlider("Rolling: wisps' relief", ref Look.WispRelief, 0f, 4f, "F2");
            LookSlider("Rolling: wisps' roundness", ref Look.WispBulge, 0f, 1f, "F2");
            LookSlider("Rolling: sun through thin wisps", ref Look.WispScatter, 0f, 3f, "F2");
            LookSlider("Rolling: sun on the wisps", ref Look.WispSunLight, 0f, 2f, "F2");
            LookSlider("Rolling: a puff's edge frays", ref Look.Fringe, 0f, 1f, "F2");
            LookSlider("Rolling: its edge found across the plume", ref Look.ShapeAcross, 0f, 1f, "F0");
            LookSlider("Rolling: its edge eases over, s (0 at once)", ref Look.ShapeSmoothing, 0f, 3f, "F2");
            LookSlider("Rolling: its edge, below thickness", ref Look.FringeCore, 0.01f, 1f, "F2");
            LookSlider("Rolling: upright, facing the camera's place", ref Look.Upright, 0f, 1f, "F0");
            LookSlider("Rolling: quad, × the particle", ref Look.Grow, 1f, 2f, "F2");
            LookSlider("Rolling: wisp's reach, × half-width", ref Look.WispReach, 0.5f, 2f, "F2");
            LookSlider("Rolling: fringe's strands' opacity, ×", ref Look.FringeShare, 0f, 1f, "F2");
            LookSlider("Rolling: black smoke's reflectance", ref Look.SootAlbedo, 0f, 0.5f, "F2");
            Kept("Rise shown, × the plume's", ref Look.RiseShown, 0.2f, 1.5f, "F2");
            LookSlider("Shown from, m from the camera", ref Look.NearFadeFrom, 0f, 10f, "F1");
            LookSlider("Fully shown from, m", ref Look.NearFadeTo, 0f, 20f, "F1");
            LookSlider("Night: sky light, ×", ref Look.NightSkyLight, 0f, 6f, "F2");
            LookSlider("Night: light greyed", ref Look.NightGrey, 0f, 1f, "F2");
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
                StackHost.Mod.Logger.Log(Describe());
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
