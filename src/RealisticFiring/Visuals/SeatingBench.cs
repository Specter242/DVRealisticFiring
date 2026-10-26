using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RealisticFiring.Shed;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Visuals
{
    /// <summary>
    /// The seating bench, a development tool, not for players: a window to
    /// seat a new engine's added cab parts and shed connections in game, as
    /// the placement sliders did until they left the menu (1.8.0, the
    /// firehole's in 1.11, the shed connections' in 1.13). Shown and hidden by
    /// <see cref="Settings.DevSeatingKey"/>, unbound unless set in Settings.xml
    /// and never in the menu; Alt frees the mouse. It seats the parts on the
    /// steam engine the player is on, or the nearest: each starts from the
    /// part's seat for that livery (its table's, or its default), moves live
    /// as its sliders do, and "Write to the log" puts each seat in the mod's
    /// log in its table's form, to be pasted into the table. Nothing it sets
    /// is saved; closed and never used, every part is placed as it always is.
    /// </summary>
    internal static class SeatingBench
    {
        private const int WindowId = 0x5246_5342; // "RFSB"
        private const float Width = 600f;
        private const float LabelWidth = 230f;
        private const float ValueWidth = 70f;

        // A part moved is rebuilt at most this often while its slider moves, s.
        private const float RebuildEvery = 0.15f;

        // With the player on no steam engine, the nearest within this, m.
        private const float NearestWithin = 40f;

        public static bool Open { get; private set; }

        public static void Toggle()
        {
            Open = !Open;
            if (Open)
                Retarget();
        }

        private static TrainCar target;
        private static string livery;
        private static Vector2? position;
        private static Vector2 scroll;
        private static float height = 640f;
        private static float rebuiltAt;

        /// <summary>A part the bench seats: its sliders and how its seat is read, set, and written.</summary>
        private sealed class Part
        {
            public string Title;
            public string Table;
            public string Unknown = "not built yet";
            public string[] Labels;
            public float[] Spans;
            public float[] Least;
            public float[] Values;
            public float[] Starts;
            public bool Loaded;
            public bool Dirty;
            public Func<TrainCar, bool> Present;
            public Func<Part, TrainCar, string, bool> Read;
            public Action<string, float[]> Apply;
            public Action<string> Clear;
            public Action<TrainCar> Rebuild;
            public Func<string, float[], string> Line;
        }

        private static Transform Interior(TrainCar car) => car != null && car.loadedInterior != null ? car.loadedInterior.transform : null;

        private static bool InCab(TrainCar car, string root) => Interior(car) is Transform interior && interior.Find(root) != null;

        // The manifold carries the stoker's lever and the feed pump's valve
        // and gauge: one rebuild for all three.
        private static readonly Action<TrainCar> RebuildManifold = car => StokerCab.RebuildLoaded();

        // A hand-fired cab's feed pump valve and gauge stand on the backhead alone.
        private static bool OnBackhead(TrainCar car) => InCab(car, FeedPumpCab.BackheadRoot);

        private static readonly Part[] Parts =
        {
            new Part
            {
                Title = "Stoker manifold (from the fireman's stoker valve's mount)",
                Table = "StokerCab.Placements",
                Labels = new[] { "Across, cm (+ toward the cab's middle)", "Up, cm", "Out, cm (+ into the panel)", "Turn, °", "Tilt, °" },
                Spans = new[] { 100f, 100f, 40f, 90f, 90f },
                Present = car => InCab(car, StokerCab.RootName),
                Read = (part, car, id) =>
                {
                    var p = StokerCab.PlacementFor(id);
                    part.Values = new[] { p.OffsetCm.x, p.OffsetCm.y, p.OffsetCm.z, p.Turn, p.Tilt };
                    return true;
                },
                Apply = (id, v) => StokerCab.DevPlacements[id] = new StokerCab.Placement(new Vector3(v[0], v[1], v[2]), v[3], v[4]),
                Clear = id => StokerCab.DevPlacements.Remove(id),
                Rebuild = RebuildManifold,
                Line = (id, v) => $"[\"{id}\"] = new Placement(new Vector3({Cm(v[0])}, {Cm(v[1])}, {Cm(v[2])}), {Deg(v[3])}, {Deg(v[4])}),",
            },
            new Part
            {
                Title = "Stoker operating lever (moved from under the manifold, car frame)",
                Table = "StokerCab.LeverMoves",
                Labels = new[] { "Across, m (+ right)", "Up, m (where the floor is looked for)", "Along, m (+ forward)" },
                Spans = new[] { 1f, 0.5f, 1f },
                Present = car => InCab(car, StokerCab.RootName),
                Read = (part, car, id) =>
                {
                    var m = StokerCab.LeverMoveFor(id);
                    part.Values = new[] { m.x, m.y, m.z };
                    return true;
                },
                Apply = (id, v) => StokerCab.DevLeverMoves[id] = new Vector3(v[0], v[1], v[2]),
                Clear = id => StokerCab.DevLeverMoves.Remove(id),
                Rebuild = RebuildManifold,
                Line = (id, v) => $"[\"{id}\"] = new Vector3({M(v[0])}, {M(v[1])}, {M(v[2])}),",
            },
            new Part
            {
                Title = "Feed pump valve and gauge beside the manifold (or on a hand-fired cab's backhead)",
                Table = "FeedPumpCab.Spots",
                Present = car => InjectorRig.TryGet(car, out var rig) && rig.Pump != null && (InCab(car, StokerCab.RootName) || OnBackhead(car)),
                Read = (part, car, id) =>
                {
                    var spot = OnBackhead(car) ? FeedPumpCab.BackheadSpotFor(id) : FeedPumpCab.SpotFor(id);
                    if (spot.ValveInCar is Vector3 inCar)
                    {
                        part.Labels = new[] { "Valve across, m (car frame, + right)", "Valve up, m (car frame)", "Valve along, m (car frame, + forward)",
                            "Gauge from the valve across, m (its frame)", "Gauge from the valve up, m" };
                        part.Spans = new[] { 0.5f, 0.5f, 0.5f, 0.3f, 0.3f };
                        part.Values = new[] { inCar.x, inCar.y, inCar.z, spot.Gauge.x, spot.Gauge.y };
                    }
                    else
                    {
                        part.Labels = new[] { "Valve across, m (manifold frame, + toward the cab's middle)", "Valve up, m",
                            "Gauge across, m", "Gauge up, m" };
                        part.Spans = new[] { 0.5f, 0.5f, 0.5f, 0.5f };
                        part.Values = new[] { spot.Valve.x, spot.Valve.y, spot.Gauge.x, spot.Gauge.y };
                    }
                    return true;
                },
                Apply = (id, v) => FeedPumpCab.DevSpots[id] = v.Length == 5
                    ? new FeedPumpCab.Spot(Vector2.zero, new Vector2(v[3], v[4]), new Vector3(v[0], v[1], v[2]))
                    : new FeedPumpCab.Spot(new Vector2(v[0], v[1]), new Vector2(v[2], v[3])),
                Clear = id => FeedPumpCab.DevSpots.Remove(id),
                Rebuild = car =>
                {
                    StokerCab.RebuildLoaded();
                    FeedPumpCab.RebuildOnBackhead();
                },
                Line = (id, v) => v.Length == 5
                    ? $"[\"{id}\"] = new Spot(Vector2.zero, new Vector2({M(v[3])}, {M(v[4])}), new Vector3({M(v[0])}, {M(v[1])}, {M(v[2])})),"
                    : $"[\"{id}\"] = new Spot(new Vector2({M(v[0])}, {M(v[1])}), new Vector2({M(v[2])}, {M(v[3])})),",
            },
            new Part
            {
                Title = "Feed pump gauge on the cab wall (an oil burner's, from its handwheel)",
                Table = "FeedPumpCab.LeverGaugeSeats",
                Labels = new[] { "Along the wall, m (+ toward the front)", "Up, m", "Out from the wall, m (+ into the cab)" },
                Spans = new[] { 0.6f, 0.6f, 0.2f },
                Present = car => InCab(car, FeedPumpCab.LeverGaugeRoot),
                Read = (part, car, id) =>
                {
                    var seat = FeedPumpCab.LeverGaugeSeatFor(id);
                    part.Values = new[] { seat.x, seat.y, seat.z };
                    return true;
                },
                Apply = (id, v) => FeedPumpCab.DevLeverGaugeSeats[id] = new Vector3(v[0], v[1], v[2]),
                Clear = id => FeedPumpCab.DevLeverGaugeSeats.Remove(id),
                Rebuild = car => FeedPumpCab.RebuildLeverGauges(),
                Line = (id, v) => $"[\"{id}\"] = new Vector3({M(v[0])}, {M(v[1])}, {M(v[2])}),",
            },
            new Part
            {
                Title = "Injector steam lever on the backhead (from the stock handle)",
                Table = "InjectorSteamCab.Seats",
                Labels = new[] { "Toward the cab's middle, m", "Up, m", "Out from the backhead, m", "Turn, ° (about the backhead's up)",
                    "Tilt, ° (about across)", "Twist, ° (about its face)" },
                Spans = new[] { 0.5f, 0.5f, 0.2f, 180f, 90f, 90f },
                Present = car => InCab(car, InjectorSteamCab.RootName)
                                 && InjectorRig.TryGet(car, out var rig) && rig.Circuits.Any(c => c.Added != null && !c.OwnLeverIsSteam),
                Read = (part, car, id) =>
                {
                    var seat = InjectorSteamCab.SeatFor(id);
                    part.Values = new[] { seat.Move.x, seat.Move.y, seat.Move.z, seat.Turns.x, seat.Turns.y, seat.Turns.z };
                    return true;
                },
                Apply = (id, v) => InjectorSteamCab.DevSeats[id] = (new Vector3(v[0], v[1], v[2]), new Vector3(v[3], v[4], v[5])),
                Clear = id => InjectorSteamCab.DevSeats.Remove(id),
                Rebuild = car => InjectorSteamCab.RebuildLoaded(),
                Line = (id, v) => $"[\"{id}\"] = (new Vector3({M(v[0])}, {M(v[1])}, {M(v[2])}), new Vector3({Deg(v[3])}, {Deg(v[4])}, {Deg(v[5])})),",
            },
            new Part
            {
                Title = "Ashpan lever's pull (car frame)",
                Table = "AshpanLever.Placements",
                Unknown = "not built yet: load the engine's outside (walk near it) once",
                Labels = new[] { "Across, m (+ right)", "Up, m", "Along, m (+ forward)" },
                Spans = new[] { 0.6f, 0.6f, 1.5f },
                Present = car => car != null && car.GetComponent<AshpanLever>() != null,
                Read = (part, car, id) =>
                {
                    if (!AshpanLever.TrySeat(id, out var pull))
                        return false;
                    part.Values = new[] { pull.x, pull.y, pull.z };
                    return true;
                },
                Apply = (id, v) => AshpanLever.DevPlacements[id] = new Vector3(v[0], v[1], v[2]),
                Clear = id => AshpanLever.DevPlacements.Remove(id),
                Rebuild = car => AshpanLever.RebuildLoaded(),
                Line = (id, v) => $"[\"{id}\"] = new Vector3({M(v[0])}, {M(v[1])}, {M(v[2])}),",
            },
            new Part
            {
                Title = "Drop grate lever (from the fire-dump lever, car frame)",
                Table = "DropGrateCab.Placements",
                Labels = new[] { "Across, m (+ right)", "Up, m (where the floor is looked for)", "Along, m (+ forward)" },
                Spans = new[] { 1f, 0.5f, 1f },
                Present = car => InCab(car, DropGrateCab.RootName),
                Read = (part, car, id) =>
                {
                    var seat = DropGrateCab.SeatFor(id);
                    part.Values = new[] { seat.x, seat.y, seat.z };
                    return true;
                },
                Apply = (id, v) => DropGrateCab.DevPlacements[id] = new Vector3(v[0], v[1], v[2]),
                Clear = id => DropGrateCab.DevPlacements.Remove(id),
                Rebuild = car => DropGrateCab.RebuildLoaded(),
                Line = (id, v) => $"[\"{id}\"] = new Vector3({M(v[0])}, {M(v[1])}, {M(v[2])}),",
            },
            new Part
            {
                Title = "Firehole for the aim (car frame; the keypad on the door shows it)",
                Table = "FireholeOpening.Holes",
                Unknown = "not known yet: aim the shovel at the open door once (keypad on)",
                Labels = new[] { "Middle across, m (+ right)", "Foot, m", "Width, m", "Height, m", "Its plane along, m (+ forward, deeper)" },
                Spans = new[] { 0.5f, 0.5f, 0.4f, 0.4f, 0.8f },
                Least = new[] { float.MinValue, float.MinValue, 0.05f, 0.05f, float.MinValue },
                Present = car => FireboxRegistry.TryGet(car, out _),
                Read = (part, car, id) =>
                {
                    if (!FireholeOpening.TryHole(id, out var hole))
                        return false;
                    part.Values = new[] { hole.Middle, hole.Foot, hole.Width, hole.Height, hole.Plane };
                    return true;
                },
                Apply = (id, v) => FireholeOpening.DevHoles[id] = (v[0], v[1], v[2], v[3], v[4]),
                Clear = id => FireholeOpening.DevHoles.Remove(id),
                // Worked out at each aim: nothing to rebuild.
                Rebuild = car => { },
                Line = (id, v) => $"[\"{id}\"] = ({Firehole(v[0])}, {Firehole(v[1])}, {Firehole(v[2])}, {Firehole(v[3])}, {Firehole(v[4])}),",
            },
            ShedPart(false),
            ShedPart(true),
        };

        private static Part ShedPart(bool water) => new Part
        {
            Title = water ? "Shed water hose connection (its flange, car frame)" : "Shed blower line connection (its flange, car frame)",
            Table = water ? "ShedConnections.WaterSeated" : "ShedConnections.Seated",
            Labels = new[] { "Across, m (− left, + right: its side)", "Up, m", "Along, m (+ forward)", "Swung toward the front, °",
                "Turned down, °", "Spun about its axis, °", "Further out along its axis, m" },
            Spans = new[] { 1.5f, 1f, 2f, 90f, 90f, 180f, 0.2f },
            Present = car => ShedConnections.TrySeat(car, water, out _),
            Read = (part, car, id) =>
            {
                if (!ShedConnections.TrySeat(car, water, out var seat))
                    return false;
                part.Values = new[] { seat.At.x, seat.At.y, seat.At.z, seat.Swing, seat.Down, seat.Spin, seat.Out };
                return true;
            },
            Apply = (id, v) => (water ? ShedConnections.DevWaterSeated : ShedConnections.DevSeated)[id] = (new Vector3(v[0], v[1], v[2]), v[3], v[4], v[5], v[6]),
            Clear = id => (water ? ShedConnections.DevWaterSeated : ShedConnections.DevSeated).Remove(id),
            Rebuild = car =>
            {
                var line = ShedConnections.Replace(car, water);
                if (line != null && Main.Settings.LogFireboxCreation)
                    Main.Mod.Logger.Log($"{car.LogName()}: {line}");
            },
            Line = (id, v) => $"[\"{id}\"] = (new Vector3({M(v[0])}, {M(v[1])}, {M(v[2])}), {Deg(v[3])}, {Deg(v[4])}, {Deg(v[5])}, {M(v[6])}),",
        };

        /// <summary>Seats the engine the player is on, or else the nearest steam engine.</summary>
        private static void Retarget()
        {
            var on = PlayerManager.Car;
            if (on != null && InjectorRig.TryGet(on, out _))
            {
                Target(on);
                return;
            }
            var player = PlayerManager.PlayerTransform != null ? PlayerManager.PlayerTransform.position : (Vector3?)null;
            TrainCar nearest = null;
            var best = NearestWithin;
            if (player.HasValue)
            {
                foreach (var rig in InjectorRig.All)
                {
                    if (rig.Car == null)
                        continue;
                    var distance = Vector3.Distance(rig.Car.transform.position, player.Value);
                    if (distance < best)
                    {
                        best = distance;
                        nearest = rig.Car;
                    }
                }
            }
            Target(nearest);
        }

        private static void Target(TrainCar car)
        {
            target = car;
            var id = car != null && car.carLivery != null ? car.carLivery.id : null;
            if (id == livery)
                return;
            livery = id;
            foreach (var part in Parts)
            {
                part.Loaded = false;
                part.Dirty = false;
            }
        }

        /// <summary>Draws the window when open; Alt frees the mouse to use it.</summary>
        public static void Draw()
        {
            if (!Open)
                return;
            // Boarding another steam engine seats that one.
            var on = PlayerManager.Car;
            if (on != null && on != target && InjectorRig.TryGet(on, out _))
                Target(on);
            if (target == null && livery != null)
                Target(null);
            RebuildMoved();

            var k = OverlayStyle.Scale;
            var matrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(k, k, 1f));
            var screen = new Vector2(Screen.width / k, Screen.height / k);
            height = Mathf.Min(900f, screen.y - 40f);
            var at = position ?? new Vector2(24f, 60f);
            var rect = GUI.Window(WindowId, new Rect(at, new Vector2(Width, height)), DrawWindow, "Seating bench (development; Alt frees the mouse)");
            position = new Vector2(Mathf.Clamp(rect.x, 0f, Mathf.Max(0f, screen.x - Width)), Mathf.Clamp(rect.y, 0f, Mathf.Max(0f, screen.y - height)));
            GUI.matrix = matrix;
        }

        // Rebuilds the parts whose sliders moved, at most every RebuildEvery.
        private static void RebuildMoved()
        {
            if (Event.current == null || Event.current.type != EventType.Repaint || Time.unscaledTime < rebuiltAt + RebuildEvery)
                return;
            var rebuilt = new HashSet<Action<TrainCar>>();
            foreach (var part in Parts.Where(p => p.Dirty))
            {
                part.Dirty = false;
                if (target != null && rebuilt.Add(part.Rebuild))
                    part.Rebuild(target);
                rebuiltAt = Time.unscaledTime;
            }
        }

        private static void DrawWindow(int id)
        {
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(height - 30f));
            GUILayout.BeginHorizontal();
            GUILayout.Label(target != null ? $"Seating {target.LogName()} ('{livery}')" : "No steam engine here: board one or stand by it", GUILayout.Width(Width - 230f));
            if (GUILayout.Button("Nearest"))
                Retarget();
            if (GUILayout.Button("Write to the log"))
                WriteToLog();
            GUILayout.EndHorizontal();
            GUILayout.Label("Each part starts from its seat for this livery (its table's, or its default) and moves live; nothing here is saved.");
            if (target != null && livery != null)
            {
                foreach (var part in Parts)
                {
                    GUILayout.Space(8f);
                    DrawPart(part);
                }
            }
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0f, 0f, Width, 20f));
        }

        private static void DrawPart(Part part)
        {
            Header(part.Title);
            if (!part.Present(target))
            {
                GUILayout.Label("Not on this engine, or its cab or outside not loaded.");
                return;
            }
            if (!part.Loaded)
            {
                if (!part.Read(part, target, livery))
                {
                    GUILayout.Label(part.Unknown);
                    return;
                }
                part.Starts = (float[])part.Values.Clone();
                part.Loaded = true;
            }
            for (var i = 0; i < part.Values.Length; i++)
            {
                var least = part.Least != null ? part.Least[i] : float.MinValue;
                var before = part.Values[i];
                var value = Slider(part.Labels[i], before, Mathf.Max(least, part.Starts[i] - part.Spans[i]), part.Starts[i] + part.Spans[i]);
                if (!Mathf.Approximately(value, before))
                {
                    part.Values[i] = value;
                    part.Apply(livery, part.Values);
                    part.Dirty = true;
                }
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{part.Table}: {part.Line(livery, part.Values)}");
            if (GUILayout.Button("Start over", GUILayout.Width(90f)))
            {
                part.Clear(livery);
                part.Rebuild(target);
                part.Loaded = false;
            }
            GUILayout.EndHorizontal();
        }

        /// <summary>Puts each part's seat on this engine in the log, in its table's form, to paste in.</summary>
        private static void WriteToLog()
        {
            if (target == null || livery == null)
            {
                Main.Mod.Logger.Log("Seating bench: no steam engine to seat");
                return;
            }
            Main.Mod.Logger.Log($"Seating bench on {target.LogName()} ('{livery}'), each line its table's entry:");
            foreach (var part in Parts)
            {
                if (!part.Present(target))
                    continue;
                if (!part.Loaded && !part.Read(part, target, livery))
                    continue;
                Main.Mod.Logger.Log($"Seating bench, {part.Table}: {part.Line(livery, part.Values)}");
            }
        }

        // Numbers as the tables write them: invariant, no "-0", with the float suffix.
        private static string Number(float value, string format)
        {
            var text = value.ToString(format, CultureInfo.InvariantCulture);
            if (text.StartsWith("-") && float.Parse(text, CultureInfo.InvariantCulture) == 0f)
                text = text.Substring(1);
            return text + "f";
        }

        private static string M(float value) => Number(value, "0.##");
        private static string Cm(float value) => Number(value, "0.#");
        private static string Deg(float value) => Number(value, "0");
        private static string Firehole(float value) => Number(value, "0.000");

        private static GUIStyle header;

        private static void Header(string text)
        {
            header = header ?? new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            GUILayout.Label(text, header);
        }

        private static float Slider(string label, float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(LabelWidth));
            value = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.Label(value.ToString("0.###", CultureInfo.InvariantCulture), GUILayout.Width(ValueWidth));
            GUILayout.EndHorizontal();
            return value;
        }
    }
}
