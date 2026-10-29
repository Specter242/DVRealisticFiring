using System.Collections.Generic;
using System.Linq;
using RealisticFiring.Core;
using RealisticFiring.Sim;
using RealisticFiring.Visuals;
using UnityEngine;

namespace RealisticFiring
{
 internal sealed class FiremanPanel
    {
        private const int WindowId = 0x5246_4D4E; // "RFMN"

        // Laid out for a 1080-line screen and scaled with its height, as the overlay is.
        private const float Width = 560f;
        private const float Pad = 16f;
        private const float PlateHeight = 26f;
        private const float RowsTop = 84f;
        private const float RowHeight = 30f;
        private const float LabelWidth = 140f;
        private const float NoteGap = 8f;
        private const float NoteTextInset = 30f;
        private const float StatusWidth = 460f;
        private const float StatusPad = 10f;
        private const float RefreshSeconds = 1f;

        private static readonly (FiremanMode Mode, string Plate, float Width)[] Modes =
        {
            (FiremanMode.Off, "OFF", 60f),
            (FiremanMode.KeepAlive, "KEEP IT ALIVE", 122f),
            (FiremanMode.Shunting, "SHUNTING", 96f),
            (FiremanMode.Road, "ROAD", 70f),
        };

        private struct Row
        {
            public string Label, Value;
            public AdviceLevel? Level;
            public bool Small;
            public bool RoadPlanning;
            public float ValueWidth;
        }

        private struct Note
        {
            public AdviceLevel Level;
            public string Text;
            public float Height;
        }

        // What the card, and the small card when it is put away, show, read once a second.
        private sealed class Card
        {
            public string Loco;
            public readonly List<Row> Rows = new List<Row>();
            public readonly List<Note> Notes = new List<Note>();
            public float Height;
            public string Plate, Line;
            public readonly List<Note> Warnings = new List<Note>();
            public float LineWidth, PlateWidth, StatusHeight;
        }

        private Vector2? position;
        private FiremanRig shown;
        private Card card;
        private object readFor;
        private FiremanMode readMode;
        private float readTime = float.NegativeInfinity;
        private GUIStyle panel, fill, plateOn, plateOff, loco, label, value, valueSmall, noteText, statusPlate, statusText, statusNote;
        private Font fontFor;

        public bool Open { get; private set; }

        public void Toggle() => SetOpen(!Open);

        public void SetOpen(bool open) => Open = open;

        public void Draw(FiremanRig rig)
        {
            shown = rig;
            var mode = FiremanOrders.ModeOf(rig);
            if (!Open && !(Main.Settings.ShowFiremanStatus && mode != FiremanMode.Off))
                return;
            Styles();
            if (Stale(rig, mode))
                card = Measure(Read(rig, mode));
            var k = OverlayStyle.Scale;
            var matrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(k, k, 1f));
            if (Open)
            {
                var at = position ?? new Vector2(Screen.width / k - Width - 24f, 96f);
                var rect = GUI.Window(WindowId, new Rect(at, new Vector2(Width, card.Height)), DrawCard, GUIContent.none, GUIStyle.none);
                // Kept on the screen when dragged (Alt frees the mouse).
                position = new Vector2(Mathf.Clamp(rect.x, 0f, Mathf.Max(0f, Screen.width / k - Width)), Mathf.Clamp(rect.y, 0f, Mathf.Max(0f, Screen.height / k - card.Height)));
            }
            else
                DrawStatus(card, Screen.width / k);
            GUI.matrix = matrix;
        }

        private bool Stale(FiremanRig rig, FiremanMode mode)
        {
            if (card != null && ReferenceEquals(rig, readFor) && mode == readMode && Time.unscaledTime - readTime < RefreshSeconds)
                return false;
            readFor = rig;
            readMode = mode;
            readTime = Time.unscaledTime;
            return true;
        }

        private void Call(FiremanRig rig, FiremanCall call, string plate, Rect rect)
        {
            var active = FiremanOrders.CallActive(rig, call);
            var text = active ? $"{plate} · {rig.Fireman.CallLeft:F0} s" : plate;
            if (!GUI.Button(rect, text, active ? plateOn : plateOff))
                return;
            FiremanOrders.Call(rig, call);
        }

        // ---- Reading ----

        private static string P0(float v) => $"{Mathf.RoundToInt(v * 100f)}%";

        private static Card Read(FiremanRig rig, FiremanMode mode)
        {
            var c = new Card { Loco = rig.Car.ID };
            var man = rig.Fireman;
            var r = rig.Readings;
            var warnings = Leveled(rig.Actions.Notices).ToList();
            if (Net.IsClient)
                c.Notes.Add(new Note { Level = AdviceLevel.Ok, Text = "In multiplayer the host's fireman does the work." });
            else if (mode == FiremanMode.Off)
                c.Notes.Add(new Note { Level = AdviceLevel.Ok, Text = "He leaves the fire and the controls to you." });
            else
            {
                ReadRows(rig, c.Rows);
                c.Notes.AddRange(warnings.Select(w => new Note { Level = w.Level, Text = w.Text }));
            }
            if (FiremanRig.Rivals != null)
                c.Notes.Add(new Note { Level = AdviceLevel.Watch, Text = $"{FiremanRig.Rivals} is running too: two firemen will fight; use one." });

            c.Plate = Modes.First(m => m.Mode == mode).Plate;
            c.Line = $"{r.Pressure:F1} of {man.PressureTarget:F1} bar · glass {P0(rig.GlassShown)}" +
                     (man.Call == FiremanCall.HeavyAhead ? " · building for heavy work" : man.Call == FiremanCall.EasingOff ? " · easing off" : "");
            c.Warnings.AddRange(warnings.Select(w => new Note { Level = w.Level, Text = w.Text }));
            return c;
        }

        /// <summary>Orders stay within reach above a compact version of the F7 readings.</summary>
        internal static FireOverlay.Card ReadTablet(FiremanRig rig)
        {
            var reading = Read(rig, FiremanOrders.ModeOf(rig));
            var note = reading.Notes.OrderByDescending(n => n.Level).FirstOrDefault();
            var c = new FireOverlay.Card
            {
                Loco = reading.Loco,
                Oil = rig.OilFireman != null,
                Advice = new Advice(note.Level, note.Text ?? "He is tending the fire. Press an active engineer's call again to cancel it."),
            };
            foreach (var row in reading.Rows)
                if (!row.RoadPlanning)
                    c.Rows.Add(new FireOverlay.Row { Label = row.Label, Value = row.Value, Level = row.Level });
            return c;
        }

        private static void ReadRows(FiremanRig rig, List<Row> rows)
        {
            var man = rig.Fireman;
            var r = rig.Readings;
            rows.Add(new Row { Label = "Pressure", Value = $"{r.Pressure:F1} bar · holding {man.PressureTarget:F1} · lifts {r.SafetyValve:F1}" });
            if (rig.CoalFireman is AutoFireman coal)
            {
                rows.Add(new Row
                {
                    Label = "Fire",
                    Value = $"{rig.Firebox.Model.CoalMass:F0} kg · wants {coal.CoalTarget:F0} kg" +
                            (coal.RoundLeft > 0 ? " · firing" : coal.WaitingOnSmoke ? " · waiting for the smoke" : ""),
                });
                rows.Add(new Row { Label = "Firing for", Value = $"{man.PlannedSteam:F1} kg/s of steam" });
                if (r.HasStoker)
                    rows.Add(r.StokerJammed
                        ? new Row { Label = "Stoker", Value = "jammed · reversing the screw", Level = AdviceLevel.Act }
                        : r.StokerTableLoaded
                            ? new Row { Label = "Stoker", Value = "coal on the table · using the hook", Level = AdviceLevel.Watch }
                        : coal.Stoking
                            ? new Row { Label = "Stoker", Value = $"{P0(rig.Actions.StokerThrottle)} · jets front {P0(rig.Actions.FrontJets)}, back {P0(rig.Actions.BackJets)}" }
                            : new Row { Label = "Stoker", Value = "shut · master jet cracked" });
            }
            else if (rig.OilFireman is AutoOilFireman oil)
            {
                var fire = rig.OilFirebox.Model;
                rows.Add(new Row { Label = "Firing valve", Value = $"{P0(oil.Valve)} · keeping {P0(oil.LeastFire)}" });
                rows.Add(new Row { Label = "Firing for", Value = $"{man.PlannedSteam:F1} kg/s of steam" });
                rows.Add(oil.StackBlack
                    ? new Row { Label = "Stack", Value = "black · less oil, more air", Level = AdviceLevel.Watch }
                    : new Row { Label = "Stack", Value = oil.StackClear ? "clear · less air" : "a light haze" });
                rows.Add(new Row { Label = "Atomizer", Value = $"{P0(rig.Actions.Atomizer)} · {fire.Atomizing:F1}× the oil's need" });
                rows.Add(new Row { Label = "Oil", Value = $"{fire.OilTemperature:F0} °C · heater {P0(rig.Actions.Heater)}" });
                rows.Add(new Row { Label = "Brick", Value = $"{fire.BrickTemperature:F0} °C · flue soot {P0(fire.FlueSoot)}" });
            }
            rows.Add(new Row { Label = "Glass", Value = $"{P0(rig.GlassShown)} · holding {P0(rig.GlassTargetShown)}", Level = rig.Actions.Notices.HasFlag(FiremanNotice.LowWater) ? AdviceLevel.Act : (AdviceLevel?)null });
            rows.Add(new Row
            {
                Label = "Feed",
                Value = (man.OnPump ? $"pump {r.Pump.ToString().ToLowerInvariant()}" : $"injector {InjectorWord(r.Injector)}") +
                        (man.BlowingDown ? $" · blowing down at {r.Solids:F0} ppm" : ""),
                Level = man.OnPump ? null : InjectorLevel(r.Injector),
            });
            var road = rig.Road;
            if (!road.Known)
                rows.Add(new Row { Label = "Road", Value = "not read · standing, or off the end", RoadPlanning = true });
            else
            {
                rows.Add(new Row { Label = "Road", Value = $"{Grade(road.GradeNow)} now · hardest {Grade(road.GradeSoon)} in {road.FireLead / 1000f:F1} km", RoadPlanning = true });
                if (!float.IsNaN(road.GradeAhead))
                    rows.Add(new Row { Label = "Further on", Value = $"{Grade(road.GradeAhead)} by {road.WaterLead / 1000f:F1} km", RoadPlanning = true });
                rows.Add(new Row { Label = "Train", Value = $"{road.Mass / 1000f:F0} t · work soon {P0(road.WorkSoon)}, ahead {P0(road.WorkAhead)}", RoadPlanning = true });
            }
            var held = rig.Hands.Where(h => h.YieldLeft > 0f).Select(h => $"{h.Name} {h.YieldLeft:F0} s").ToList();
            if (held.Count > 0)
                rows.Add(new Row { Label = "Yours for now", Value = string.Join(", ", held) });
        }

        private static string InjectorWord(InjectorState state)
        {
            switch (state)
            {
                case InjectorState.Working: return "working";
                case InjectorState.Off: return "off";
                case InjectorState.Hot: return "too hot";
                case InjectorState.Priming: return "priming";
                case InjectorState.NoWater: return "no water";
                case InjectorState.LowPressure: return "waiting for pressure";
                default: return "jet broken";
            }
        }

        private static AdviceLevel? InjectorLevel(InjectorState state) =>
            state == InjectorState.Hot || state == InjectorState.NoWater || state == InjectorState.Broken ? AdviceLevel.Act
            : state == InjectorState.Priming || state == InjectorState.LowPressure ? AdviceLevel.Watch : (AdviceLevel?)null;

        // The texts' widths and the notes' heights, once per reading.
        private Card Measure(Card c)
        {
            var room = Width - 2f * Pad - LabelWidth - 17f;
            for (var i = 0; i < c.Rows.Count; i++)
            {
                var row = c.Rows[i];
                var content = new GUIContent(row.Value);
                row.ValueWidth = value.CalcSize(content).x;
                if (row.ValueWidth > room)
                {
                    row.Small = true;
                    row.ValueWidth = valueSmall.CalcSize(content).x;
                }
                c.Rows[i] = row;
            }
            var y = RowsTop + c.Rows.Count * RowHeight;
            for (var i = 0; i < c.Notes.Count; i++)
            {
                var note = c.Notes[i];
                note.Height = Mathf.Max(34f, noteText.CalcHeight(new GUIContent(note.Text), Width - 2f * Pad - NoteTextInset - 10f) + 12f);
                c.Notes[i] = note;
                y += NoteGap + note.Height;
            }
            c.Height = y + 14f;

            c.PlateWidth = statusPlate.CalcSize(new GUIContent(c.Plate)).x + 20f;
            c.LineWidth = statusText.CalcSize(new GUIContent(c.Line)).x;
            var line = Mathf.Max(c.LineWidth, StatusWidth - 3f * StatusPad - c.PlateWidth);
            y = StatusPad + PlateHeight;
            for (var i = 0; i < c.Warnings.Count; i++)
            {
                var warning = c.Warnings[i];
                warning.Height = statusNote.CalcHeight(new GUIContent(warning.Text), c.PlateWidth + line + StatusPad - 20f) + 4f;
                c.Warnings[i] = warning;
                y += warning.Height + 2f;
            }
            c.StatusHeight = y + StatusPad;
            return c;
        }

        // ---- Drawing ----

        private void Styles()
        {
            var font = OverlayStyle.Font;
            if (panel != null && fontFor == font)
                return;
            fontFor = font;
            // Measured with another font: read again.
            readTime = float.NegativeInfinity;
            panel = OverlayStyle.Back(OverlayArt.Panel, 10);
            fill = OverlayStyle.Back(OverlayArt.Rounded, 5);
            plateOff = OverlayStyle.PlateOff(15);
            plateOn = OverlayStyle.PlateOn(plateOff);
            loco = OverlayStyle.Text(16, OverlayArt.Soft, TextAnchor.MiddleRight);
            label = OverlayStyle.Text(16, OverlayArt.Soft, TextAnchor.MiddleLeft);
            value = OverlayStyle.Text(19, OverlayArt.Cream, TextAnchor.MiddleRight);
            valueSmall = OverlayStyle.Text(16, OverlayArt.Cream, TextAnchor.MiddleRight);
            noteText = new GUIStyle(OverlayStyle.Text(17, OverlayArt.Cream, TextAnchor.UpperLeft)) { wordWrap = true };
            statusPlate = OverlayStyle.PlateOn(OverlayStyle.PlateOff(14));
            statusText = OverlayStyle.Text(17, OverlayArt.Cream, TextAnchor.MiddleLeft);
            statusNote = new GUIStyle(OverlayStyle.Text(15, OverlayArt.Cream, TextAnchor.UpperLeft)) { wordWrap = true };
        }

        private void DrawCard(int id)
        {
            var rig = shown;
            var c = card;
            if (rig == null || c == null)
                return;
            var mode = FiremanOrders.ModeOf(rig);
            GUI.Box(new Rect(0f, 0f, Width, c.Height), GUIContent.none, panel);
            OverlayStyle.Rivets(Width);

            // His orders, the one he has lit.
            var x = Pad;
            foreach (var (each, plate, width) in Modes)
            {
                if (GUI.Button(new Rect(x, 12f, width, PlateHeight), plate, each == mode ? plateOn : plateOff) && each != mode)
                    FiremanOrders.Order(rig, each);
                x += width + 6f;
            }
            var close = new Rect(Width - Pad - PlateHeight, 12f, PlateHeight, PlateHeight);
            GUI.Label(new Rect(x, 12f, close.x - 8f - x, PlateHeight), c.Loco, loco);
            if (GUI.Button(close, "×", plateOff))
                SetOpen(false);

            GUI.Label(new Rect(Pad, 46f, 130f, PlateHeight), "Engineer's calls", label);
            Call(rig, FiremanCall.HeavyAhead, "HEAVY WORK AHEAD", new Rect(150f, 46f, 206f, PlateHeight));
            Call(rig, FiremanCall.EasingOff, "EASING OFF", new Rect(362f, 46f, 150f, PlateHeight));

            DrawRows(c.Rows);
            var y = RowsTop + c.Rows.Count * RowHeight;
            foreach (var note in c.Notes)
            {
                var rect = new Rect(Pad, y + NoteGap, Width - 2f * Pad, note.Height);
                OverlayStyle.LevelBox(rect, note.Level, fill);
                GUI.Label(new Rect(rect.x + NoteTextInset, rect.y + 6f, rect.width - NoteTextInset - 10f, rect.height - 10f), note.Text, noteText);
                y = rect.yMax;
            }

            // Dragged anywhere off its plates.
            GUI.DragWindow();
        }

        private void DrawRows(List<Row> rows)
        {
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var r = new Rect(Pad, RowsTop + i * RowHeight, Width - 2f * Pad, RowHeight);
                GUI.Label(new Rect(r.x, r.y, LabelWidth, r.height), row.Label, label);
                var valueWidth = Mathf.Min(row.ValueWidth, r.width - LabelWidth);
                GUI.Label(new Rect(r.xMax - valueWidth, r.y, valueWidth, r.height), row.Value, row.Small ? valueSmall : value);
                if (row.Level.HasValue)
                    OverlayStyle.Mark(row.Level.Value, new Rect(r.xMax - valueWidth - 17f, r.y + (r.height - 11f) / 2f, 11f, 11f));
                OverlayStyle.Rule(r);
            }
        }

        // Put away: his order lit, the pressure and glass, and his warnings, at the top right.
        private void DrawStatus(Card c, float screenWidth)
        {
            var line = Mathf.Max(c.LineWidth, StatusWidth - 3f * StatusPad - c.PlateWidth);
            var width = c.Warnings.Count > 0 ? c.PlateWidth + line + 3f * StatusPad : c.PlateWidth + c.LineWidth + 3f * StatusPad;
            var back = new Rect(screenWidth - width - 16f, 12f, width, c.StatusHeight);
            GUI.Box(back, GUIContent.none, panel);
            var plate = new Rect(back.x + StatusPad, back.y + StatusPad, c.PlateWidth, PlateHeight);
            GUI.Label(plate, c.Plate, statusPlate);
            GUI.Label(new Rect(plate.xMax + StatusPad, plate.y, back.xMax - StatusPad - plate.xMax - StatusPad, PlateHeight), c.Line, statusText);
            var y = plate.yMax + 2f;
            foreach (var warning in c.Warnings)
            {
                OverlayStyle.Mark(warning.Level, new Rect(plate.x + 2f, y + 6f, 11f, 11f));
                GUI.Label(new Rect(plate.x + 20f, y + 2f, back.width - 2f * StatusPad - 20f, warning.Height), warning.Text, statusNote);
                y += warning.Height + 2f;
            }
        }

        // ---- For the log ----

        /// <summary>What he is doing, a line at a time.</summary>
        internal static IEnumerable<string> Describe(FiremanRig rig)
        {
            var man = rig.Fireman;
            if (man.Mode == FiremanMode.Off)
            {
                yield return "Off: he leaves the fire and the controls to you.";
                yield break;
            }
            var r = rig.Readings;
            yield return $"Pressure {r.Pressure:F1} bar, holding {man.PressureTarget:F1} (safety valve {r.SafetyValve:F1})";
            if (rig.CoalFireman is AutoFireman coal)
            {
                var fire = rig.Firebox.Model;
                yield return $"Fire {fire.CoalMass:F0} kg, wants {coal.CoalTarget:F0} kg for {man.PlannedSteam:F1} kg/s of steam" +
                             (coal.RoundLeft > 0 ? ", firing" : coal.WaitingOnSmoke ? ", waiting for the smoke to clear" : "");
                if (r.HasStoker)
                    yield return r.StokerJammed ? "Stoker jammed: he is reversing it to clear the screw"
                        : coal.Stoking ? $"Stoker running at {rig.Actions.StokerThrottle:P0}, jets: front {rig.Actions.FrontJets:P0}, back {rig.Actions.BackJets:P0}"
                        : "Stoker shut, the master jet cracked to keep the plate cool";
            }
            else if (rig.OilFireman is AutoOilFireman oil)
            {
                var fire = rig.OilFirebox.Model;
                yield return $"Firing valve {oil.Valve:P0} for {man.PlannedSteam:F1} kg/s of steam, keeping at least {oil.LeastFire:P0} on the brick at {fire.BrickTemperature:F0} C; " +
                             (oil.StackBlack ? "black at the stack: less oil, more air" : oil.StackClear ? "clear at the stack, less air" : "a light haze at the stack");
                yield return $"Atomizer {rig.Actions.Atomizer:P0} ({fire.Atomizing:F1}x the oil's need); oil {fire.OilTemperature:F0} C, heater {rig.Actions.Heater:P0}; flue soot {fire.FlueSoot:P0}";
            }
            yield return $"Glass {rig.GlassShown:P0}, holding {rig.GlassTargetShown:P0}; " +
                         (man.OnPump ? $"feed pump {r.Pump.ToString().ToLowerInvariant()}" : $"injector {r.Injector.ToString().ToLowerInvariant()}") +
                         (man.BlowingDown ? $"; blowing down ({r.Solids:F0} ppm)" : "");
            yield return DescribeRoad(rig.Road);
            var held = rig.Hands.Where(h => h.YieldLeft > 0f).Select(h => $"{h.Name} ({h.YieldLeft:F0} s)").ToList();
            if (held.Count > 0)
                yield return "Yours for now: " + string.Join(", ", held);
            foreach (var notice in Notices(rig.Actions.Notices))
                yield return notice;
        }

        /// <summary>The road as he reads it: the grade he is on, the hardest ahead for the fire and the water, and the train.</summary>
        internal static string DescribeRoad(FiremanRig.RoadView road)
        {
            if (!road.Known)
                return "Road: not read (standing, or off the track's end)";
            var ahead = float.IsNaN(road.GradeAhead) ? "" : $", {Grade(road.GradeAhead)} by {road.WaterLead / 1000f:F1} km";
            return $"Road: {Grade(road.GradeNow)} now, hardest {Grade(road.GradeSoon)} within {road.FireLead / 1000f:F1} km{ahead}; " +
                   $"train {road.Mass / 1000f:F0} t; work soon {road.WorkSoon:P0}, ahead {road.WorkAhead:P0}";
        }

        private static string Grade(float grade) => $"{grade * 100f:+0.0;-0.0;0.0} %";

        internal static IEnumerable<string> Notices(FiremanNotice notices) => Leveled(notices).Select(n => n.Text);

        // His warnings, the ones that stop the engine marked to act on.
        private static IEnumerable<(AdviceLevel Level, string Text)> Leveled(FiremanNotice notices)
        {
            if (notices.HasFlag(FiremanNotice.FireOut))
                yield return (AdviceLevel.Act, "The fire is out: light it (your lighter, lit, at the open fire door), and he will tend it.");
            if (notices.HasFlag(FiremanNotice.TenderWaterOut))
                yield return (AdviceLevel.Act, "The tender is dry: he has stopped firing.");
            if (notices.HasFlag(FiremanNotice.LowWater))
                yield return (AdviceLevel.Act, "The water is out of sight in the glass!");
            if (notices.HasFlag(FiremanNotice.TenderCoalOut))
                yield return (AdviceLevel.Act, "The tender is out of fuel.");
            if (notices.HasFlag(FiremanNotice.CleanFire))
                yield return (AdviceLevel.Watch, "Clinker: the fire wants cleaning at the next stop.");
            if (notices.HasFlag(FiremanNotice.EmptyAshpan))
                yield return (AdviceLevel.Watch, "The ashpan is half full: empty it at the next stop.");
            if (notices.HasFlag(FiremanNotice.ColdOil))
                yield return (AdviceLevel.Watch, "The oil is cold: he has the heater open; it fires poorly until it warms.");
            if (notices.HasFlag(FiremanNotice.NoSand))
                yield return (AdviceLevel.Watch, "The flues want sanding and the sand is out.");
        }
    }
}
