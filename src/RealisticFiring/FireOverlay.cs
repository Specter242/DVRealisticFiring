using System.Collections.Generic;
using System.Linq;
using RealisticFiring.Core;
using RealisticFiring.Sim;
using RealisticFiring.Visuals;
using UnityEngine;

namespace RealisticFiring
{
 internal sealed class FireOverlay
    {
        private const int WindowId = 0x5246_4952; // "RFIR"

        // Laid out for a 1080-line screen and scaled with its height.
        private const float Width = 540f;
        private const float Height = 462f;
        private const float Pad = 16f;
        private const float MinScale = 0.85f;
        private const float MaxScale = 1.5f;
        private const float RefreshSeconds = 1f;

        public const int Pages = 3;
        private static readonly string[] PageNames = { "FIRE", "WATER", "ENGINE" };

        private struct Gauge
        {
            public string Key, Label, Text;
            public float Value;
            public float? Band0, Band1;
            public AdviceLevel Level;
            // The smoke's color, shown under the draft dial.
            public Color? Smoke;
            // Drawn and measured with the reading, not each frame.
            public Texture2D Face;
            public float PlateWidth, TextWidth;
        }

        private struct Row
        {
            public string Label, Value;
            public AdviceLevel? Level;
            public float? Meter;
            public float ValueWidth;
        }

        // What the card shows, read once a second.
        private sealed class Card
        {
            public string Loco;
            public bool Oil;
            public readonly Gauge[] Gauges = new Gauge[4];
            public readonly List<Row> Rows = new List<Row>();
            public float[] ZoneCoal, ZoneGlow, ZoneClinker;
            public int Thin = -1;
            public IReadOnlyList<LightingStep> Steps;
            public Advice Advice;
        }

        private Vector2 position = new Vector2(24f, 96f);
        private Card card;
        private object readFor;
        private int readPage = -1;
        private float readTime = float.NegativeInfinity;
        private RealisticFirebox shownCoal;
        private GUIStyle panel, plateOn, plateOff, loco, label, value, gaugePlate, gaugeValue, kg, ends, nextText, stepText, stepHow, fill;
        private Font fontFor;

        private static int Page => Mathf.Clamp(Main.Settings.OverlayPage, 0, Pages - 1);

        /// <summary>F6: shows the card at its first page, steps the pages, then hides it.</summary>
        public static void Step(Settings settings)
        {
            if (!settings.ShowOverlay)
            {
                settings.ShowOverlay = true;
                settings.OverlayPage = 0;
            }
            else if (settings.OverlayPage < Pages - 1)
                settings.OverlayPage++;
            else
                settings.ShowOverlay = false;
        }

        public void Draw(RealisticFirebox firebox, float time)
        {
            shownCoal = firebox;
            Styles();
            if (Stale(firebox))
                card = Measure(Read(firebox));
            Show();
        }

        public void DrawOil(RealisticOilFirebox oil)
        {
            shownCoal = null;
            Styles();
            if (Stale(oil))
                card = Measure(Read(oil));
            Show();
        }

        // The dials' faces and the texts' widths, once per reading.
        private Card Measure(Card c)
        {
            for (var i = 0; i < c.Gauges.Length; i++)
            {
                var g = c.Gauges[i];
                g.Face = OverlayArt.Dial(g.Key, g.Value, g.Band0, g.Band1, g.Level);
                g.PlateWidth = gaugePlate.CalcSize(new GUIContent(g.Label)).x;
                g.TextWidth = gaugeValue.CalcSize(new GUIContent(g.Text)).x;
                c.Gauges[i] = g;
            }
            for (var i = 0; i < c.Rows.Count; i++)
            {
                var row = c.Rows[i];
                row.ValueWidth = value.CalcSize(new GUIContent(row.Value)).x;
                c.Rows[i] = row;
            }
            return c;
        }

        private bool Stale(object source)
        {
            if (card != null && ReferenceEquals(source, readFor) && readPage == Page && Time.unscaledTime - readTime < RefreshSeconds)
                return false;
            readFor = source;
            readPage = Page;
            readTime = Time.unscaledTime;
            return true;
        }

        private void Show()
        {
            var k = Mathf.Clamp(Screen.height / 1080f, MinScale, MaxScale);
            var matrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(k, k, 1f));
            var rect = GUI.Window(WindowId, new Rect(position, new Vector2(Width, Height)), DrawCard, GUIContent.none, GUIStyle.none);
            // Kept on the screen when dragged (Alt frees the mouse).
            position = new Vector2(Mathf.Clamp(rect.x, 0f, Mathf.Max(0f, Screen.width / k - Width)), Mathf.Clamp(rect.y, 0f, Mathf.Max(0f, Screen.height / k - Height)));
            GUI.matrix = matrix;
        }

        // ---- Reading ----

        private static string P0(float v) => $"{Mathf.RoundToInt(v * 100f)}%";

        private static string FireWord(float x) => x < 0.15f ? "low" : x < 0.4f ? "light" : x < 0.65f ? "working" : x < 0.9f ? "hard" : "flat out";

        private const float DraftScale = 2.5f;
        private const float ChokedDraft = 0.9f;
        private const float ThinDraft = 1.6f;
        private const float SmotheredDraft = 0.6f;

        private static Gauge Draft(bool fireOn, float ratio, string low, string high, Color smoke)
        {
            if (!fireOn)
                return new Gauge { Key = "draft", Label = "DRAFT", Value = 0f, Band0 = ChokedDraft / DraftScale, Band1 = ThinDraft / DraftScale, Text = "no fire", Level = AdviceLevel.Ok, Smoke = smoke };
            var word = ratio < ChokedDraft ? low : ratio > ThinDraft ? high : "right";
            return new Gauge
            {
                Key = "draft", Label = "DRAFT", Value = ratio / DraftScale, Band0 = ChokedDraft / DraftScale, Band1 = ThinDraft / DraftScale,
                Text = $"{word} {Mathf.RoundToInt(Mathf.Min(ratio, 9.99f) * 100f)}%",
                Level = ratio < SmotheredDraft ? AdviceLevel.Act : ratio < ChokedDraft || ratio > ThinDraft ? AdviceLevel.Watch : AdviceLevel.Ok,
                Smoke = smoke,
            };
        }

        // Clear smoke pale, through grey, to black.
        private static Color CoalSmoke(float density) =>
            Color.Lerp(Color.Lerp(new Color(0.81f, 0.79f, 0.75f), new Color(0.45f, 0.42f, 0.39f), Mathf.Clamp01(density / 0.4f)),
                new Color(0.07f, 0.06f, 0.06f), Mathf.Clamp01((density - 0.4f) / 0.5f));

        // The stack as an oil fireman reads it: clear, a light brown haze, grey, black.
        private static Color StackSmoke(bool fireOn, float stack, float vapor) =>
            !fireOn ? (vapor > 0.05f ? new Color(0.92f, 0.93f, 0.93f) : new Color(0.33f, 0.32f, 0.31f))
            : stack >= FiringAdvice.BlackStack ? new Color(0.16f, 0.13f, 0.11f)
            : stack >= 0.3f ? new Color(0.6f, 0.56f, 0.53f)
            : stack >= FiringAdvice.HazeStack ? new Color(0.8f, 0.72f, 0.61f)
            : new Color(0.87f, 0.91f, 0.94f);

        private static AdviceLevel GlassLevel(float x) => x < FiringAdvice.LowGlass ? AdviceLevel.Act : x < 0.4f || x > 0.85f ? AdviceLevel.Watch : AdviceLevel.Ok;

        private static string InjectorWord(InjectorFlow flow)
        {
            switch (flow.State)
            {
                case InjectorState.Working: return $"feeding {flow.WaterIn:F1} kg/s";
                case InjectorState.Off: return "off";
                case InjectorState.Hot: return "too hot";
                case InjectorState.Priming: return "priming";
                case InjectorState.NoWater: return "no water";
                case InjectorState.LowPressure: return "waiting for pressure";
                default: return "jet broken";
            }
        }

        private static AdviceLevel InjectorLevel(InjectorState state) =>
            state == InjectorState.Hot || state == InjectorState.NoWater || state == InjectorState.Broken ? AdviceLevel.Act
            : state == InjectorState.Priming || state == InjectorState.LowPressure ? AdviceLevel.Watch : AdviceLevel.Ok;

        private static string Valves(InjectorCircuit circuit) => circuit.SteamValve != null
            ? $"steam {InjectorValves.Position(circuit.SteamValve.Value)} · water {P0(circuit.WaterValve.Value)}" + (circuit.TenderValveShut ? " · tender shut" : "")
            : $"lever {P0(circuit.Control.Value)}";

        private static string LocoName(TrainCar car) => car.ID;

        private static IReadOnlyList<InjectorReading> Injectors(InjectorRig rig) => rig == null
            ? new InjectorReading[0]
            : rig.Circuits.Select(c => new InjectorReading(c.Flow.State, c.Feed.Value, c.SteamValve != null, c.TenderValveShut)).ToList();

        // Glass and boiler, the same on every engine: the glass as the cab's
        // shows it, and the boiler's gauge against its working pressure, its
        // good band 85 to 100 %.
        private static void WaterGauges(Card card, InjectorRig rig)
        {
            var glass = Visuals.GaugeGlass.Read(rig);
            card.Gauges[2] = new Gauge { Key = "glass", Label = "GLASS", Value = glass, Band0 = 0.4f, Band1 = 0.75f, Text = P0(glass), Level = GlassLevel(glass) };
            var gauge = rig != null ? rig.BoilerPressure - 1f : 0f;
            var working = rig != null ? Mathf.Max(1f, rig.WorkingPressure - 1f) : 1f;
            var share = gauge / working;
            card.Gauges[3] = new Gauge
            {
                Key = "boiler", Label = "BOILER", Value = share / 1.1f, Band0 = 0.85f / 1.1f, Band1 = 1f / 1.1f, Text = $"{gauge:F1} bar",
                Level = share < 0.6f || share > 1.02f ? AdviceLevel.Watch : AdviceLevel.Ok,
            };
        }

        private Card Read(RealisticFirebox firebox)
        {
            var fire = firebox.Model;
            var car = firebox.Car;
            InjectorRig.TryGetTended(car, out var rig);
            // Raising steam, the game's startup speeds the fire up: said, as before.
            var c = new Card { Loco = firebox.StartupMultiplier > 1.01f ? $"{LocoName(car)} · startup ×{firebox.StartupMultiplier:F0}" : LocoName(car) };
            var heat = fire.Heat / Mathf.Max(1f, fire.Parameters.MaxHeatOutput);
            c.Gauges[0] = new Gauge
            {
                Key = "fire", Label = "FIRE", Value = heat, Band0 = 0.4f, Band1 = 0.9f, Text = fire.FireOn ? $"{FireWord(heat)} {P0(heat)}" : "out",
                Level = !fire.FireOn || heat < 0.15f ? AdviceLevel.Watch : AdviceLevel.Ok,
            };
            c.Gauges[1] = Draft(fire.FireOn, fire.AirRatio, "choked", "thin", CoalSmoke(fire.SmokeDensity));
            WaterGauges(c, rig);
            var door = firebox.HasDoorPort ? firebox.DoorOpening : 0f;
            c.Advice = FiringAdvice.ForCoal(fire, door, rig != null ? Visuals.GaugeGlass.Read(rig) : 1f, Injectors(rig));

            switch (Page)
            {
                case 0:
                    var perZone = fire.Parameters.MaxCombustionRate / GrateLayout.ZoneCount;
                    c.ZoneCoal = fire.Zones.Select(z => z.Coal).ToArray();
                    c.ZoneGlow = fire.Zones.Select(z => Mathf.Clamp01(z.CombustionRate / perZone)).ToArray();
                    c.ZoneClinker = fire.Zones.Select(z => Mathf.Clamp01(z.Clinker / Mathf.Max(1e-3f, 0.5f * fire.ReferenceZoneMass))).ToArray();
                    c.Thin = FiringAdvice.ThinZone(fire.Zones);
                    c.Rows.Add(new Row { Label = "Door", Value = firebox.HasDoorPort ? P0(door) : "—" });
                    c.Rows.Add(new Row { Label = "Ashpan", Value = P0(fire.AshpanFill), Meter = fire.AshpanFill, Level = fire.AshpanFill >= FiringAdvice.FullAshpan ? AdviceLevel.Watch : AdviceLevel.Ok });
                    c.Rows.Add(new Row { Label = "Clinker", Value = $"{fire.ClinkerMass:F1} kg", Level = FiringAdvice.WantsCleaning(fire) ? AdviceLevel.Watch : AdviceLevel.Ok });
                    c.Rows.Add(new Row { Label = "In the box", Value = $"{fire.CoalMass:F0} kg" });
                    c.Rows.Add(firebox.LastStoker is StokerFlow stoker
                        ? new Row { Label = "Stoker", Value = $"{(stoker.Jammed ? "JAMMED" : P0(firebox.StokerEngineSpeed))} · jets {stoker.ManifoldPressure:F1} bar", Level = stoker.Jammed ? AdviceLevel.Act : (AdviceLevel?)null }
                        : new Row { Label = "Firebox", Value = $"{fire.Temperature:F0} °C" });
                    break;
                case 1:
                    WaterRows(c, rig);
                    break;
                default:
                    EngineRows(c, car, firebox);
                    break;
            }
            return c;
        }

        private Card Read(RealisticOilFirebox oil)
        {
            var fire = oil.Model;
            var car = oil.Car;
            InjectorRig.TryGetTended(car, out var rig);
            var c = new Card { Loco = LocoName(car), Oil = true };
            var heat = fire.Heat / Mathf.Max(1f, fire.Parameters.MaxHeatOutput);
            c.Gauges[0] = new Gauge
            {
                Key = "fire", Label = "FIRE", Value = heat, Band0 = 0.4f, Band1 = 0.9f, Text = fire.FireOn ? $"{FireWord(heat)} {P0(heat)}" : "out",
                Level = !fire.FireOn || heat < 0.15f ? AdviceLevel.Watch : AdviceLevel.Ok,
            };
            var stack = fire.StackSmoke;
            // The oil's air against its best mix, so 100 % is right here too.
            c.Gauges[1] = Draft(fire.FireOn, fire.ExcessAir / OilFire.BestExcessAir, "rich", "lean", StackSmoke(fire.FireOn, stack, fire.Vapor));
            WaterGauges(c, rig);
            var reading = FireController.ReadOil(oil);
            c.Advice = FiringAdvice.ForOil(reading);

            switch (Page)
            {
                case 0:
                    if (!fire.FireOn)
                    {
                        c.Steps = FiringAdvice.Steps(reading);
                        break;
                    }
                    var valve = oil.Feed != null ? oil.Feed.FiringValve : 0f;
                    c.Rows.Add(new Row { Label = "Firing valve", Value = $"{P0(valve)} · {fire.FeedRate:F2} kg/s" });
                    c.Rows.Add(new Row { Label = "Atomizer", Value = $"{P0(oil.AtomizerValve)} · {fire.Atomizing:F1}× need", Level = fire.Atomizing < 1f ? AdviceLevel.Act : fire.Atomizing > 2.2f ? AdviceLevel.Watch : AdviceLevel.Ok });
                    c.Rows.Add(new Row { Label = "Oil", Value = $"{fire.OilTemperature:F0} °C · heater {P0(oil.HeaterValve)}", Level = fire.OilTemperature < OilFire.WarmOil || fire.OilTemperature > 80f ? AdviceLevel.Watch : AdviceLevel.Ok });
                    c.Rows.Add(new Row { Label = "Brick", Value = $"{fire.BrickTemperature:F0} °C" });
                    c.Rows.Add(new Row { Label = "Shop steam", Value = fire.OnSteamLine ? "on the shed's line" : oil.SteamLineOpening > 0f ? "open" : "shut" });
                    c.Rows.Add(new Row { Label = "Flues", Value = $"{P0(fire.FlueSoot)} sooted", Level = fire.FlueSoot > FiringAdvice.SootedFlues ? AdviceLevel.Watch : (AdviceLevel?)null });
                    break;
                case 1:
                    WaterRows(c, rig);
                    break;
                default:
                    EngineRows(c, car, null);
                    break;
            }
            return c;
        }

        private static void WaterRows(Card c, InjectorRig rig)
        {
            if (rig == null)
            {
                c.Rows.Add(new Row { Label = "Injector", Value = "the engine's own" });
                return;
            }
            if (rig.Circuits.Count == 1)
            {
                var only = rig.Circuits[0];
                c.Rows.Add(new Row { Label = "Injector", Value = InjectorWord(only.Flow), Level = InjectorLevel(only.Flow.State) });
                c.Rows.Add(new Row { Label = "Its valves", Value = Valves(only) });
            }
            else
            {
                foreach (var circuit in rig.Circuits.Take(2))
                    c.Rows.Add(new Row { Label = $"{circuit.Name} injector".Trim(), Value = $"{InjectorWord(circuit.Flow)} · {Valves(circuit)}", Level = InjectorLevel(circuit.Flow.State) });
            }
            var water = rig.Circuits.Sum(x => x.Flow.Overflow);
            var steam = rig.Circuits.Sum(x => x.Flow.OverflowSteam);
            c.Rows.Add(new Row { Label = "Overflow", Value = water > 0.05f || steam > 0.05f ? $"{water:F1} kg/s water, {steam:F1} steam" : "dry", Level = steam > 0.05f ? AdviceLevel.Watch : (AdviceLevel?)null });
            c.Rows.Add(new Row { Label = "Feed pump", Value = rig.Pump == null ? "none fitted" : Pump(rig.PumpFlow), Level = rig.Pump != null && (rig.PumpFlow.State == FeedPumpState.Racing || rig.PumpFlow.State == FeedPumpState.LostSuction) ? AdviceLevel.Act : (AdviceLevel?)null });
            if (BoilerWaterRig.TryGet(rig.Car, out var waterRig))
            {
                c.Rows.Add(Main.Settings.BoilerWaterImpurities
                    ? new Row { Label = "Impurities", Value = $"{waterRig.Water.FoamingConcentration:F0} ppm · foam {P0(waterRig.Water.Foam)}", Level = waterRig.Water.Foam > 0.05f ? AdviceLevel.Watch : (AdviceLevel?)null }
                    : new Row { Label = "Impurities", Value = "off" });
                c.Rows.Add(waterRig.Plug != null
                    ? new Row { Label = "Fusible plug", Value = waterRig.Plug.Melted ? "MELTED" : $"{waterRig.Plug.CoreTemperature:F0} °C", Level = waterRig.Plug.Melted ? AdviceLevel.Act : (AdviceLevel?)null }
                    : waterRig.Alarm != null
                        ? new Row { Label = "Low water", Value = waterRig.Alarm.Sounding ? "ALARM" : "alarm quiet", Level = waterRig.Alarm.Sounding ? AdviceLevel.Act : (AdviceLevel?)null }
                        : new Row { Label = "Low water", Value = "the engine's own" });
            }
        }

        private static string Pump(FeedPumpFlow flow)
        {
            switch (flow.State)
            {
                case FeedPumpState.Off: return "shut";
                case FeedPumpState.Feeding: return $"{flow.WaterIn:F1} kg/s at {flow.FeedTemperature:F0} °C";
                case FeedPumpState.Choked: return $"drifting, {flow.WaterIn:F1} kg/s";
                case FeedPumpState.Racing: return "RACING: ease it";
                case FeedPumpState.LostSuction: return "LOST SUCTION";
                default: return flow.State.ToString();
            }
        }

        private static void EngineRows(Card c, TrainCar car, RealisticFirebox firebox)
        {
            if (EngineCylinders.TryGet(car, out var engine, out var cylinders))
            {
                var cycle = cylinders.Cycle;
                c.Rows.Add(new Row { Label = "Cut-off", Value = P0(cycle.Events.Cutoff) });
                c.Rows.Add(new Row { Label = "Steam chest", Value = $"{engine.steamChestPressureReadOut.Value - 1f:F1} bar" });
                c.Rows.Add(new Row { Label = "Mean pressure", Value = $"{cycle.MeanEffectivePressure:F1} bar" });
                if (EngineSuperheater.TryGet(engine, out var superheater))
                    c.Rows.Add(new Row
                    {
                        Label = "Superheat",
                        Value = superheater.Model.Spec.Saturated
                            ? "none: saturated"
                            : $"{superheater.Model.Superheat:F0} °C · steam {superheater.SteamTemperature(engine.intakePressure.Value):F0} °C",
                    });
                var condensing = cylinders.Admitted > 0f ? cylinders.Condensed / cylinders.Admitted : 0f;
                c.Rows.Add(new Row
                {
                    Label = "Steam used",
                    Value = condensing >= 0.01f ? $"{engine.exhaustFlowReadOut.Value:F2} kg/s · {P0(condensing)} condensing" : $"{engine.exhaustFlowReadOut.Value:F2} kg/s",
                });
            }
            else
                c.Rows.Add(new Row { Label = "Cylinders", Value = "the engine's own" });
            c.Rows.Add(firebox != null && firebox.LastStoker is StokerFlow stoker
                ? new Row { Label = "Stoker", Value = $"{(stoker.Jammed ? "JAMMED" : P0(firebox.StokerEngineSpeed))} · jets {stoker.ManifoldPressure:F1} bar", Level = stoker.Jammed ? AdviceLevel.Act : (AdviceLevel?)null }
                : new Row { Label = "Stoker", Value = "none fitted" });
            c.Rows.Add(RealisticBooster.TryGet(car, out var booster)
                ? new Row { Label = "Booster", Value = $"{(booster.Flow.State != BoosterState.CutOut ? "up" : "down")} · warm {P0(booster.Model.Warmth)}" }
                : new Row { Label = "Booster", Value = "none fitted" });
        }

        // ---- Drawing ----

        private void Styles()
        {
            var font = PlumeAssets.OverlayFont;
            if (panel != null && fontFor == font)
                return;
            fontFor = font;
            // Measured with another font: read again.
            readTime = float.NegativeInfinity;
            var plateFont = PlumeAssets.PlateFont != null ? PlumeAssets.PlateFont : font;
            GUIStyle Text(int size, Color color, TextAnchor anchor, Font f = null) => new GUIStyle(GUI.skin.label)
            {
                font = f != null ? f : font, fontSize = size, alignment = anchor, wordWrap = false, clipping = TextClipping.Clip,
                padding = new RectOffset(), margin = new RectOffset(), normal = { textColor = color },
            };
            GUIStyle Back(Texture2D texture, int border) => new GUIStyle { normal = { background = texture }, border = new RectOffset(border, border, border, border) };
            panel = Back(OverlayArt.Panel, 10);
            fill = Back(OverlayArt.Rounded, 5);
            plateOff = new GUIStyle(Text(15, OverlayArt.Dim, TextAnchor.MiddleCenter, plateFont)) { normal = { background = OverlayArt.PlateOff, textColor = OverlayArt.Dim }, hover = { background = OverlayArt.PlateOff, textColor = OverlayArt.Soft }, active = { background = OverlayArt.PlateOn, textColor = OverlayArt.Cream }, border = new RectOffset(5, 5, 5, 5) };
            plateOn = new GUIStyle(plateOff) { normal = { background = OverlayArt.PlateOn, textColor = OverlayArt.Cream }, hover = { background = OverlayArt.PlateOn, textColor = OverlayArt.Cream } };
            loco = Text(16, OverlayArt.Soft, TextAnchor.MiddleRight);
            label = Text(16, OverlayArt.Soft, TextAnchor.MiddleLeft);
            value = Text(19, OverlayArt.Cream, TextAnchor.MiddleRight);
            gaugePlate = new GUIStyle(Text(14, OverlayArt.Cream, TextAnchor.MiddleCenter, plateFont)) { normal = { background = OverlayArt.PlateOff, textColor = OverlayArt.Cream }, border = new RectOffset(5, 5, 5, 5), padding = new RectOffset(8, 8, 2, 1) };
            gaugeValue = Text(18, OverlayArt.Cream, TextAnchor.MiddleLeft);
            kg = Text(17, OverlayArt.Cream, TextAnchor.MiddleCenter, plateFont);
            ends = Text(13, OverlayArt.Soft, TextAnchor.MiddleCenter, plateFont);
            nextText = new GUIStyle(Text(19, OverlayArt.Cream, TextAnchor.UpperLeft)) { wordWrap = true };
            nextFit = new GUIStyle(nextText);
            nextFitFor = null;
            stepText = Text(17, OverlayArt.Soft, TextAnchor.MiddleLeft);
            stepHow = Text(14, OverlayArt.Dim, TextAnchor.MiddleLeft);
        }

        private void DrawCard(int id)
        {
            var c = card;
            if (c == null)
                return;
            GUI.Box(new Rect(0f, 0f, Width, Height), GUIContent.none, panel);
            GUI.DrawTexture(new Rect(7f, 7f, 6f, 6f), OverlayArt.Rivet);
            GUI.DrawTexture(new Rect(Width - 13f, 7f, 6f, 6f), OverlayArt.Rivet);

            // The page plates; Alt+click one to turn to it.
            for (var i = 0; i < Pages; i++)
            {
                var name = i == 0 && c.Oil ? "BURNER" : PageNames[i];
                if (GUI.Button(new Rect(Pad + i * 102f, 12f, 96f, 26f), name, i == Page ? plateOn : plateOff) && i != Page)
                    Main.Settings.OverlayPage = i;
            }
            GUI.Label(new Rect(Width - Pad - 200f, 12f, 200f, 26f), c.Loco, loco);

            var cell = (Width - 2f * Pad - 18f) / 4f;
            for (var i = 0; i < 4; i++)
                DrawGauge(c.Gauges[i], Pad + i * (cell + 6f), 46f, cell);

            var body = new Rect(Pad, 180f, Width - 2f * Pad, 196f);
            if (c.Steps != null)
                DrawSteps(c.Steps, body);
            else if (c.ZoneCoal != null)
            {
                DrawGrate(c, body.x, body.y);
                DrawRows(c.Rows, new Rect(body.x + 222f, body.y, body.width - 222f, body.height), 30f);
            }
            else
                DrawRows(c.Rows, body, Mathf.Min(32f, body.height / Mathf.Max(1, c.Rows.Count)));

            DrawNext(c.Advice, new Rect(Pad, 388f, Width - 2f * Pad, 64f));

            // Dragged by its head, outside the plates.
            GUI.DragWindow(new Rect(Pad + 3f * 102f, 0f, Width - Pad - 3f * 102f, 42f));
        }

        private void DrawGauge(Gauge g, float x, float y, float width)
        {
            const float dialWidth = 112f;
            var dialHeight = dialWidth * OverlayArt.DialHeight / OverlayArt.DialWidth;
            if (g.Face != null)
                GUI.DrawTexture(new Rect(x + (width - dialWidth) / 2f, y, dialWidth, dialHeight), g.Face);
            GUI.Box(new Rect(x + (width - g.PlateWidth) / 2f, y + 78f, g.PlateWidth, 20f), g.Label, gaugePlate);
            var textWidth = g.TextWidth;
            var total = 12f + 6f + textWidth;
            var start = x + (width - total) / 2f;
            Mark(g.Level, new Rect(start, y + 107f, 11f, 11f));
            GUI.Label(new Rect(start + 18f, y + 101f, textWidth + 2f, 24f), g.Text, gaugeValue);
            if (g.Smoke.HasValue)
            {
                // The smoke's color, edged so black still shows on the iron.
                var pill = new Rect(x + (width - 72f) / 2f, y + 127f, 72f, 7f);
                Fill(Grow(pill, 1f), new Color(1f, 1f, 1f, 0.3f));
                Fill(pill, g.Smoke.Value);
            }
        }

        private static void Mark(AdviceLevel level, Rect rect)
        {
            var old = GUI.color;
            GUI.color = OverlayArt.LevelColor(level);
            GUI.DrawTexture(rect, OverlayArt.Mark(level));
            GUI.color = old;
        }

        private void Fill(Rect rect, Color color)
        {
            var old = GUI.backgroundColor;
            GUI.backgroundColor = color;
            GUI.Box(rect, GUIContent.none, fill);
            GUI.backgroundColor = old;
        }

        private static Rect Grow(Rect r, float by) => new Rect(r.x - by, r.y - by, r.width + 2f * by, r.height + 2f * by);

        // The grate as seen from the cab: the front at the top, the door at the bottom.
        private void DrawGrate(Card c, float x, float y)
        {
            const float w = 62f, h = 40f, gap = 4f;
            var width = 3f * w + 2f * gap;
            GUI.Label(new Rect(x, y, width, 16f), "FRONT", ends);
            var aim = shownCoal != null ? shownCoal.HoverAim : null;
            var aimed = aim.HasValue && aim.Value.IsLiveAt(Time.time) ? (int)aim.Value.Zone : -1;
            for (var r = GrateLayout.Rows - 1; r >= 0; r--)
            {
                for (var col = 0; col < GrateLayout.Columns; col++)
                {
                    var i = (int)GrateLayout.At(r, col);
                    var rect = new Rect(x + col * (w + gap), y + 18f + (GrateLayout.Rows - 1 - r) * (h + gap), w, h);
                    if (i == aimed)
                        Fill(Grow(rect, 3f), OverlayArt.Steam);
                    else if (i == c.Thin)
                    {
                        Fill(Grow(rect, 4f), OverlayArt.Caution);
                        Fill(Grow(rect, 2f), OverlayArt.Ink);
                    }
                    var glow = c.ZoneGlow[i];
                    Fill(rect, OverlayArt.Ember(glow));
                    if (c.ZoneClinker[i] > 0.1f)
                    {
                        var old = GUI.color;
                        GUI.color = new Color(0.6f, 0.63f, 0.65f);
                        GUI.DrawTexture(new Rect(rect.x + 4f, rect.yMax - 7f, (w - 8f) * Mathf.Clamp(c.ZoneClinker[i], 0.15f, 1f), 4f), OverlayArt.White);
                        GUI.color = old;
                    }
                    kg.normal.textColor = glow > 0.72f ? OverlayArt.Ink : OverlayArt.Cream;
                    GUI.Label(rect, c.ZoneCoal[i].ToString("F1"), kg);
                    if (i == c.Thin && i != aimed)
                    {
                        var old = GUI.color;
                        GUI.color = OverlayArt.Ink;
                        GUI.DrawTexture(new Rect(rect.xMax - 13f, rect.y + 3f, 10f, 10f), OverlayArt.Mark(AdviceLevel.Watch));
                        GUI.color = old;
                    }
                }
            }
            GUI.Label(new Rect(x, y + 18f + 3f * (h + gap), width, 16f), "DOOR", ends);
        }

        private void DrawRows(List<Row> rows, Rect area, float height)
        {
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var r = new Rect(area.x, area.y + i * height, area.width, height);
                GUI.Label(new Rect(r.x, r.y, r.width * 0.45f, r.height), row.Label, label);
                var size = row.ValueWidth;
                var valueWidth = Mathf.Min(size, r.width - 8f);
                GUI.Label(new Rect(r.xMax - valueWidth, r.y, valueWidth, r.height), row.Value, value);
                var left = r.xMax - valueWidth - 6f;
                if (row.Level.HasValue)
                {
                    Mark(row.Level.Value, new Rect(left - 11f, r.y + (r.height - 11f) / 2f, 11f, 11f));
                    left -= 17f;
                }
                if (row.Meter.HasValue)
                {
                    var bar = new Rect(left - 64f, r.y + (r.height - 8f) / 2f, 64f, 8f);
                    var old = GUI.color;
                    GUI.color = new Color(1f, 1f, 1f, 0.1f);
                    GUI.DrawTexture(bar, OverlayArt.White);
                    GUI.color = OverlayArt.Soft;
                    GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(row.Meter.Value), bar.height), OverlayArt.White);
                    GUI.color = OverlayArt.Caution;
                    GUI.DrawTexture(new Rect(bar.x + bar.width / 2f - 1f, bar.y - 2f, 2f, bar.height + 4f), OverlayArt.White);
                    GUI.color = old;
                }
                var line = GUI.color;
                GUI.color = OverlayArt.Rule;
                GUI.DrawTexture(new Rect(r.x, r.yMax - 1f, r.width, 1f), OverlayArt.White);
                GUI.color = line;
            }
        }

        // Lighting a cold oil fire: done steps ticked, the current one lit.
        private void DrawSteps(IReadOnlyList<LightingStep> steps, Rect area)
        {
            var y = area.y + 2f;
            for (var i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                var dot = new Rect(area.x, y + 2f, 22f, 22f);
                Fill(dot, step.Current ? OverlayArt.Caution : step.Done ? new Color(1f, 1f, 1f, 0.12f) : new Color(1f, 1f, 1f, 0.05f));
                ends.normal.textColor = step.Current ? OverlayArt.Ink : step.Done ? OverlayArt.Cream : OverlayArt.Soft;
                if (step.Done)
                {
                    var old = GUI.color;
                    GUI.color = OverlayArt.Cream;
                    GUI.DrawTexture(Grow(dot, -5f), OverlayArt.Check);
                    GUI.color = old;
                }
                else
                    GUI.Label(dot, (i + 1).ToString(), ends);
                ends.normal.textColor = OverlayArt.Soft;
                stepText.normal.textColor = step.Current ? OverlayArt.Cream : step.Done ? OverlayArt.Dim : OverlayArt.Soft;
                GUI.Label(new Rect(area.x + 32f, y, area.width - 32f, 26f), step.Title, stepText);
                y += 27f;
                if (step.Current)
                {
                    GUI.Label(new Rect(area.x + 32f, y - 4f, area.width - 32f, 20f), step.How, stepHow);
                    y += 18f;
                }
            }
        }

        private const int SmallestNext = 14;
        private GUIStyle nextFit;
        private string nextFitFor;

        private GUIStyle FitNext(string text, Rect area)
        {
            if (text == nextFitFor)
                return nextFit;
            nextFitFor = text;
            var content = new GUIContent(text);
            for (var size = nextText.fontSize; size >= SmallestNext; size--)
            {
                nextFit.fontSize = size;
                if (nextFit.CalcHeight(content, area.width) <= area.height)
                    break;
            }
            return nextFit;
        }

        private void DrawNext(Advice advice, Rect rect)
        {
            Fill(rect, advice.Level == AdviceLevel.Act ? new Color(OverlayArt.Alarm.r, OverlayArt.Alarm.g, OverlayArt.Alarm.b, 0.08f) : new Color(1f, 1f, 1f, 0.05f));
            var old = GUI.color;
            GUI.color = OverlayArt.LevelColor(advice.Level);
            GUI.DrawTexture(new Rect(rect.x, rect.y, 3f, rect.height), OverlayArt.White);
            GUI.color = old;
            Mark(advice.Level, new Rect(rect.x + 12f, rect.y + 12f, 11f, 11f));
            var area = new Rect(rect.x + 30f, rect.y + 6f, rect.width - 40f, rect.height - 10f);
            GUI.Label(area, advice.Text, FitNext(advice.Text, area));
        }
    }
}
