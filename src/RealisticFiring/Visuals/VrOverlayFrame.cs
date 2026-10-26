using System.Collections.Generic;
using RealisticFiring.Core;
using RealisticFiring.Sim;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using VRTK;

namespace RealisticFiring.Visuals
{
    internal sealed class VrOverlayFrame
    {
        private const float Scale = 0.001f;
        private const float GaugeDialWidth = OverlayFrame.GaugeDialWidth;
        private const int RowCapacity = 8;
        private const int StepCapacity = FiringAdvice.LightingSteps;
        private const int TextDensity = 2;
        private const string Glyphs = " 0123456789.,:;%/+-()°·×—'’!?&ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
        private static readonly FireOverlay.Row[] NoRows = new FireOverlay.Row[0];
        private static readonly LightingStep[] NoSteps = new LightingStep[0];
        private static readonly Dictionary<int, Sprite> Sprites = new Dictionary<int, Sprite>();
        private static Font fallbackFont;
        private static VrOverlayFrame activeFrame;

        private sealed class GrateTile
        {
            public Image Fill, Aim, ThinOuter, ThinInner, Clinker, ThinMark;
            public Text Coal;
        }

        private sealed class RowView
        {
            public Text Label, Value;
            public Image Mark, MeterBack, MeterFill, MeterMark, Rule;
        }

        private sealed class StepView
        {
            public Image Dot, Check;
            public Text Number, Title, How;
        }

        private GameObject root;
        private Canvas canvas;
        private Image[] gaugeFaces;
        private Image[] gaugePlates;
        private Text[] gaugeLabels;
        private Text[] gaugeValues;
        private Image[] gaugeMarks;
        private Image[] gaugeSmokes;
        private Image[] gaugeSmokeEdges;
        private Image[] tabs;
        private Text[] tabNames;
        private GrateTile[] grateTiles;
        private RowView[] rowViews;
        private StepView[] stepViews;
        private Text grateFront, grateDoor;
        private Text loco;
        private Transform body;
        private Image adviceFill;
        private Image adviceStripe;
        private Image adviceMark;
        private Text adviceText;
        private FireOverlay.Card shownCard;
        private FireOverlay.Card failedCard;
        private int shownPage = -1;
        private bool shownOil;
        private bool builtWithFallback;
        private string fittedAdvice;
        private bool leftClickWasPressed;
        private GameObject leftHand;
        private GameObject attachedHand;
        private bool rootActive;
        private bool grateShown;
        private int stage;
        private int stageFrame;
        private InputDevice leftDevice;
        private int shownAim = -2;

        public void Show(Camera camera, FireOverlay.Card card, int page, int aimedZone)
        {
            if (leftHand == null)
                leftHand = VRTK_DeviceFinder.GetControllerLeftHand();
            if (camera == null || leftHand == null || card == null)
            {
                Hide();
                return;
            }
            if (ReferenceEquals(card, failedCard))
                return;
            var freshCard = false;
            try
            {
                EnsureBuilt(camera);
                if (stage < 2)
                {
                    // Build, font warm-up and first render each get their own frame.
                    var frame = Time.frameCount;
                    if (frame == stageFrame)
                        return;
                    stageFrame = frame;
                    if (stage == 0)
                    {
                        WarmFonts();
                        stage = 1;
                        return;
                    }
                    stage = 2;
                }
                if (!ReferenceEquals(attachedHand, leftHand))
                {
                    Attach(leftHand.transform, camera);
                    attachedHand = leftHand;
                }
                freshCard = !ReferenceEquals(card, shownCard);
                if (freshCard)
                {
                    RenderGauges(card);
                    RenderBody(card, aimedZone);
                }
                else if (shownAim != aimedZone)
                    UpdateAim(card, shownAim, aimedZone);
                // Pooled parts are set while inactive; the advice fit needs the active canvas's scale.
                if (!rootActive)
                {
                    root.SetActive(true);
                    rootActive = true;
                }
                if (freshCard)
                {
                    RenderAdvice(card.Advice);
                    loco.text = card.Loco;
                }
            }
            catch
            {
                failedCard = card;
                Hide();
                throw;
            }
            shownCard = card;
            shownAim = aimedZone;
            // A page or oil change always arrives as a new card.
            if (!freshCard)
                return;
            if (shownPage != page)
            {
                shownPage = page;
                for (var i = 0; i < tabs.Length; i++)
                {
                    var selected = i == page;
                    tabs[i].sprite = Sprite(selected ? OverlayArt.PlateOn : OverlayArt.PlateOff, 5f);
                    tabNames[i].color = selected ? OverlayTabs.OnText : OverlayTabs.OffText;
                }
            }
            if (shownOil != card.Oil)
            {
                shownOil = card.Oil;
                tabNames[0].text = OverlayTabs.Name(0, card.Oil);
            }
        }

        public void PollVrInput()
        {
            var mode = Main.Settings.VrOverlayToggleMode;
            if (mode == VrOverlayToggle.Off)
            {
                leftClickWasPressed = false;
                return;
            }
            if (VRManager.IsVREnabled()) {
                var valid = leftDevice.isValid;
                if (!valid)
                {
                    leftDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
                    valid = leftDevice.isValid;
                }
                var pressed = valid
                              && leftDevice.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out var clicked)
                              && clicked;
                if (pressed && !leftClickWasPressed && XRSettings.enabled
                    && (mode != VrOverlayToggle.LeftStickWithTrigger || TriggerHeld())
                    && (FireboxRegistry.TryGetTended(PlayerManager.Car, out _) || RealisticOilFirebox.TryGetTended(PlayerManager.Car, out _)))
                    FireOverlay.Step(Main.Settings);
                leftClickWasPressed = pressed;
            }
        }

        private bool TriggerHeld() =>
            leftDevice.TryGetFeatureValue(CommonUsages.triggerButton, out var held) && held;

        public void Hide()
        {
            if (root != null && rootActive)
            {
                root.SetActive(false);
                rootActive = false;
            }
        }

        public static void ApplyTilt(float degrees)
        {
            if (activeFrame != null && activeFrame.root != null)
                activeFrame.root.transform.localRotation = Quaternion.Euler(Mathf.Clamp(degrees, 0f, 60f), 0f, 0f);
        }

        private void EnsureBuilt(Camera camera)
        {
            if (root != null && builtWithFallback && OverlayStyle.Font != null && OverlayStyle.PlateFont != null)
            {
                root.SetActive(false);
                Object.Destroy(root);
                root = null;
            }
            if (root != null)
                return;
            root = new GameObject("RealisticFiring VR overlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(Image));
            root.SetActive(false);
            try
            {
                Build(camera);
            }
            catch
            {
                Object.Destroy(root);
                root = null;
                throw;
            }
        }

        private void Build(Camera camera)
        {
            activeFrame = this;
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 100;
            canvas.worldCamera = camera;
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(OverlayFrame.Width, OverlayFrame.Height);
            root.transform.localScale = Vector3.one * Scale;
            var panel = root.GetComponent<Image>();
            panel.sprite = Sprite(OverlayArt.Panel, 10f);
            panel.type = Image.Type.Sliced;
            panel.color = Color.white;
            panel.raycastTarget = false;

            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            scaler.dynamicPixelsPerUnit = TextDensity;

            shownCard = null;
            shownPage = -1;
            shownOil = false;
            shownAim = -2;
            attachedHand = null;
            rootActive = false;
            // Pooled objects start active, so the first UpdateGrate must hide them.
            grateShown = true;
            stage = 0;
            stageFrame = Time.frameCount;
            fittedAdvice = null;
            builtWithFallback = OverlayStyle.Font == null || OverlayStyle.PlateFont == null;

            AddImage("Rivet left", root.transform, 7f, 7f, 6f, 6f, OverlayArt.Rivet, Color.white, 0f);
            AddImage("Rivet right", root.transform, OverlayFrame.Width - 13f, 7f, 6f, 6f, OverlayArt.Rivet, Color.white, 0f);

            tabs = new Image[OverlayTabs.Count];
            tabNames = new Text[OverlayTabs.Count];
            for (var i = 0; i < OverlayTabs.Count; i++)
            {
                var x = OverlayFrame.Pad + i * (OverlayTabs.Width + OverlayTabs.Gap);
                tabs[i] = AddImage("Tab " + i, root.transform, x, OverlayFrame.HeaderTop, OverlayTabs.Width, OverlayFrame.TabHeight,
                    OverlayArt.PlateOff, Color.white, 5f);
                tabNames[i] = AddText("Tab name " + i, tabs[i].transform, new Rect(0f, 0f, OverlayTabs.Width, OverlayFrame.TabHeight),
                    OverlayTabs.Name(i), OverlayStyle.PlateFont, 15, OverlayTabs.OffText, TextAnchor.MiddleCenter);
            }
            loco = AddText("Locomotive", root.transform,
                new Rect(OverlayFrame.Width - OverlayFrame.Pad - OverlayFrame.LocoWidth, OverlayFrame.HeaderTop, OverlayFrame.LocoWidth, OverlayFrame.TabHeight), "", OverlayStyle.Font, 16,
                OverlayArt.Soft, TextAnchor.MiddleRight);

            gaugeFaces = new Image[4];
            gaugePlates = new Image[4];
            gaugeLabels = new Text[4];
            gaugeValues = new Text[4];
            gaugeMarks = new Image[4];
            gaugeSmokes = new Image[4];
            gaugeSmokeEdges = new Image[4];
            var cell = (OverlayFrame.Width - 2f * OverlayFrame.Pad - 3f * OverlayFrame.GaugeGap) / 4f;
            for (var i = 0; i < 4; i++)
                BuildGauge(i, OverlayFrame.Pad + i * (cell + OverlayFrame.GaugeGap), OverlayFrame.GaugeTop, cell);

            body = new GameObject("Reading body", typeof(RectTransform)).transform;
            body.SetParent(root.transform, false);
            Place(body as RectTransform, OverlayFrame.Pad, OverlayFrame.BodyTop,
                OverlayFrame.Width - 2f * OverlayFrame.Pad, OverlayFrame.BodyHeight);
            // Pooled objects start active; the first RenderBody sets every one of them.
            BuildBody();
            BuildAdvice();
        }

        // A new glyph size grows the font atlas and rebuilds the whole canvas, so request them once, up front.
        private void WarmFonts()
        {
            var warmed = new HashSet<long>();
            foreach (var text in root.GetComponentsInChildren<Text>(true))
            {
                if (text.font == null)
                    continue;
                var from = text == adviceText ? 14 : text.fontSize;
                for (var size = from; size <= text.fontSize; size++)
                    if (warmed.Add(((long)text.font.GetInstanceID() << 8) | (long)size))
                        text.font.RequestCharactersInTexture(Glyphs, size * TextDensity, text.fontStyle);
            }
        }

        private void BuildGauge(int index, float x, float y, float width)
        {
            var dialHeight = GaugeDialWidth * OverlayArt.DialHeight / OverlayArt.DialWidth;
            gaugeFaces[index] = AddImage("Gauge dial " + index, root.transform,
                x + (width - GaugeDialWidth) / 2f, y, GaugeDialWidth, dialHeight, null, Color.white, 0f);
            gaugePlates[index] = AddImage("Gauge plate " + index, root.transform,
                x, y + OverlayFrame.GaugePlateTop, width, OverlayFrame.GaugePlateHeight, OverlayArt.PlateOff, Color.white, 5f);
            gaugeLabels[index] = AddText("Gauge label " + index, root.transform,
                new Rect(x, y + OverlayFrame.GaugePlateTop, width, OverlayFrame.GaugePlateHeight), "", OverlayStyle.PlateFont, 14, OverlayArt.Cream, TextAnchor.MiddleCenter);
            gaugeValues[index] = AddText("Gauge value " + index, root.transform,
                new Rect(x, y + OverlayFrame.GaugeValueTop, width, OverlayFrame.GaugeValueHeight), "", OverlayStyle.Font, 18, OverlayArt.Cream, TextAnchor.MiddleCenter);
            gaugeMarks[index] = AddImage("Gauge mark " + index, root.transform, x, y + OverlayFrame.GaugeMarkTop, 11f, 11f,
                OverlayArt.Mark(AdviceLevel.Ok), OverlayArt.Soft, 0f);
            gaugeSmokeEdges[index] = AddImage("Gauge smoke edge " + index, root.transform,
                x + (width - 74f) / 2f, y + OverlayFrame.GaugeSmokeTop - 1f, 74f, 9f, OverlayArt.White,
                new Color(1f, 1f, 1f, 0.3f), 0f);
            gaugeSmokes[index] = AddImage("Gauge smoke " + index, root.transform,
                x + (width - 72f) / 2f, y + OverlayFrame.GaugeSmokeTop, 72f, 7f, OverlayArt.White, Color.clear, 0f);
        }

        private void RenderGauges(FireOverlay.Card card)
        {
            var cell = (OverlayFrame.Width - 2f * OverlayFrame.Pad - 3f * OverlayFrame.GaugeGap) / 4f;
            for (var i = 0; i < card.Gauges.Length; i++)
            {
                var gauge = card.Gauges[i];
                gaugeFaces[i].sprite = Sprite(gauge.Face, 0f);
                gaugeLabels[i].text = gauge.Label;
                var plateWidth = Mathf.Min(cell, Mathf.Max(40f, gauge.PlateWidth));
                var plateX = OverlayFrame.Pad + i * (cell + OverlayFrame.GaugeGap) + (cell - plateWidth) / 2f;
                Move(gaugePlates[i].rectTransform, plateX, OverlayFrame.GaugeTop + OverlayFrame.GaugePlateTop,
                    plateWidth, OverlayFrame.GaugePlateHeight);
                Move(gaugeLabels[i].rectTransform, plateX, OverlayFrame.GaugeTop + OverlayFrame.GaugePlateTop,
                    plateWidth, OverlayFrame.GaugePlateHeight);
                gaugeValues[i].text = gauge.Text;
                var total = 12f + 6f + gauge.TextWidth;
                var start = OverlayFrame.Pad + i * (cell + OverlayFrame.GaugeGap) + (cell - total) / 2f;
                SetPos(gaugeMarks[i].rectTransform, start, OverlayFrame.GaugeTop + OverlayFrame.GaugeMarkTop);
                gaugeMarks[i].sprite = Sprite(OverlayArt.Mark(gauge.Level), 0f);
                gaugeMarks[i].color = OverlayArt.LevelColor(gauge.Level);
                Move(gaugeValues[i].rectTransform, start + 18f, OverlayFrame.GaugeTop + OverlayFrame.GaugeValueTop,
                    gauge.TextWidth + 4f, OverlayFrame.GaugeValueHeight);
                gaugeValues[i].alignment = TextAnchor.MiddleLeft;
                var showSmoke = i == 1 && gauge.Smoke.HasValue;
                SetActive(gaugeSmokes[i], showSmoke);
                SetActive(gaugeSmokeEdges[i], showSmoke);
                if (showSmoke)
                    gaugeSmokes[i].color = gauge.Smoke.Value;
            }
        }

        private void RenderBody(FireOverlay.Card card, int aimedZone)
        {
            var steps = card.Steps != null;
            var grated = HasGrate(card);
            UpdateSteps(steps ? card.Steps : NoSteps);
            UpdateGrate(grated ? card : null, aimedZone);
            if (steps)
                UpdateRows(NoRows, 0f, 0f, 0f, 0f);
            else if (grated)
                UpdateRows(card.Rows, 222f, 0f, OverlayFrame.Width - 2f * OverlayFrame.Pad - 222f, 30f);
            else
                UpdateRows(card.Rows, 0f, 0f, OverlayFrame.Width - 2f * OverlayFrame.Pad,
                    Mathf.Min(32f, OverlayFrame.BodyHeight / Mathf.Max(1, card.Rows.Count)));
        }

        private void BuildBody()
        {
            const float tileWidth = 62f, tileHeight = 40f, tileGap = 4f;
            var grateWidth = 3f * tileWidth + 2f * tileGap;
            grateFront = AddText("Grate front", body, new Rect(0f, 0f, grateWidth, 16f), "FRONT",
                OverlayStyle.PlateFont, 13, OverlayArt.Soft, TextAnchor.MiddleCenter);
            grateDoor = AddText("Grate door", body,
                new Rect(0f, 18f + 3f * (tileHeight + tileGap), grateWidth, 16f), "DOOR",
                OverlayStyle.PlateFont, 13, OverlayArt.Soft, TextAnchor.MiddleCenter);
            grateTiles = new GrateTile[GrateLayout.ZoneCount];
            for (var visualRow = 0; visualRow < GrateLayout.Rows; visualRow++)
            for (var col = 0; col < GrateLayout.Columns; col++)
            {
                var zone = (int)GrateLayout.At(GrateLayout.Rows - 1 - visualRow, col);
                var x = col * (tileWidth + tileGap);
                var y = 18f + visualRow * (tileHeight + tileGap);
                var tile = new GrateTile
                {
                    Aim = AddImage("Zone aim " + zone, body, x - 3f, y - 3f, tileWidth + 6f, tileHeight + 6f,
                        OverlayArt.Rounded, OverlayArt.Steam, 5f),
                    ThinOuter = AddImage("Zone thin outer " + zone, body, x - 4f, y - 4f, tileWidth + 8f, tileHeight + 8f,
                        OverlayArt.Rounded, OverlayArt.Caution, 5f),
                    ThinInner = AddImage("Zone thin inner " + zone, body, x - 2f, y - 2f, tileWidth + 4f, tileHeight + 4f,
                        OverlayArt.Rounded, OverlayArt.Ink, 5f),
                    Fill = AddImage("Zone fill " + zone, body, x, y, tileWidth, tileHeight,
                        OverlayArt.Rounded, OverlayArt.Ember(0f), 4f),
                    Clinker = AddImage("Zone clinker " + zone, body, x + 4f, y + tileHeight - 7f,
                        tileWidth - 8f, 4f, OverlayArt.White, new Color(0.6f, 0.63f, 0.65f), 0f),
                    Coal = AddText("Zone coal " + zone, body, new Rect(x, y, tileWidth, tileHeight), "",
                        OverlayStyle.PlateFont, 17, OverlayArt.Cream, TextAnchor.MiddleCenter),
                    ThinMark = AddImage("Zone thin mark " + zone, body, x + tileWidth - 13f, y + 3f, 10f, 10f,
                        OverlayArt.Mark(AdviceLevel.Watch), OverlayArt.Ink, 0f),
                };
                tile.Clinker.type = Image.Type.Filled;
                tile.Clinker.fillMethod = Image.FillMethod.Horizontal;
                tile.Clinker.fillOrigin = (int)Image.OriginHorizontal.Left;
                grateTiles[zone] = tile;
            }

            rowViews = new RowView[RowCapacity];
            for (var i = 0; i < rowViews.Length; i++)
                rowViews[i] = BuildRow(i);

            stepViews = new StepView[StepCapacity];
            for (var i = 0; i < stepViews.Length; i++)
                stepViews[i] = BuildStep(i);
        }

        private RowView BuildRow(int index)
        {
            return new RowView
            {
                Label = AddText("Row label " + index, body, new Rect(0f, 0f, 1f, 1f), "",
                    OverlayStyle.Font, 16, OverlayArt.Soft, TextAnchor.MiddleLeft),
                Value = AddText("Row value " + index, body, new Rect(0f, 0f, 1f, 1f), "",
                    OverlayStyle.Font, 19, OverlayArt.Cream, TextAnchor.MiddleRight),
                Mark = AddImage("Row warning " + index, body, 0f, 0f, 11f, 11f,
                    OverlayArt.Mark(AdviceLevel.Ok), OverlayArt.Soft, 0f),
                MeterBack = AddImage("Meter back " + index, body, 0f, 0f, 64f, 8f, OverlayArt.White,
                    new Color(1f, 1f, 1f, 0.1f), 0f),
                MeterFill = AddImage("Meter fill " + index, body, 0f, 0f, 64f, 8f, OverlayArt.White,
                    OverlayArt.Soft, 0f),
                MeterMark = AddImage("Meter marker " + index, body, 0f, 0f, 2f, 12f, OverlayArt.White,
                    OverlayArt.Caution, 0f),
                Rule = AddImage("Row rule " + index, body, 0f, 0f, 1f, 1f, OverlayArt.White, OverlayArt.Rule, 0f),
            };
        }

        private StepView BuildStep(int index)
        {
            return new StepView
            {
                Dot = AddImage("Lighting step " + index, body, 0f, 0f, 22f, 22f, OverlayArt.Rounded,
                    new Color(1f, 1f, 1f, 0.05f), 4f),
                Check = AddImage("Lighting check " + index, body, 0f, 0f, 12f, 12f, OverlayArt.Check,
                    OverlayArt.Cream, 0f),
                Number = AddText("Lighting number " + index, body, new Rect(0f, 0f, 22f, 22f), "",
                    OverlayStyle.PlateFont, 13, OverlayArt.Soft, TextAnchor.MiddleCenter),
                Title = AddText("Lighting title " + index, body, new Rect(32f, 0f,
                    OverlayFrame.Width - 2f * OverlayFrame.Pad - 32f, 26f), "", OverlayStyle.Font, 17,
                    OverlayArt.Soft, TextAnchor.MiddleLeft),
                How = AddText("Lighting detail " + index, body, new Rect(32f, 0f,
                    OverlayFrame.Width - 2f * OverlayFrame.Pad - 32f, 20f), "", OverlayStyle.PlateFont, 14,
                    OverlayArt.Dim, TextAnchor.MiddleLeft),
            };
        }

        private static void SetActive(Component part, bool active)
        {
            var go = part.gameObject;
            if (go.activeSelf != active)
                go.SetActive(active);
        }

        private void UpdateGrate(FireOverlay.Card card, int aimedZone)
        {
            var show = card != null;
            if (!show && !grateShown)
                return;
            grateShown = show;
            SetActive(grateFront, show);
            SetActive(grateDoor, show);
            for (var i = 0; i < grateTiles.Length; i++)
            {
                var tile = grateTiles[i];
                SetGrateVisible(tile, show);
                if (!show)
                    continue;
                var glow = card.ZoneGlow[i];
                tile.Fill.color = OverlayArt.Ember(glow);
                tile.Coal.text = card.ZoneCoal[i].ToString("F1");
                tile.Coal.color = glow > 0.72f ? OverlayArt.Ink : OverlayArt.Cream;
                var clinker = card.ZoneClinker[i];
                tile.Clinker.fillAmount = Mathf.Clamp(clinker, 0.15f, 1f);
                SetActive(tile.Clinker, clinker > 0.1f);
                MarkZone(i, card, aimedZone);
            }
        }

        private static bool HasGrate(FireOverlay.Card card) =>
            card.Steps == null && card.ZoneCoal != null && card.ZoneGlow != null && card.ZoneClinker != null;

        private void UpdateAim(FireOverlay.Card card, int from, int to)
        {
            if (!HasGrate(card))
                return;
            if (from >= 0 && from < grateTiles.Length)
                MarkZone(from, card, to);
            if (to >= 0 && to < grateTiles.Length)
                MarkZone(to, card, to);
        }

        private void MarkZone(int zone, FireOverlay.Card card, int aimedZone)
        {
            var tile = grateTiles[zone];
            var aimed = zone == aimedZone;
            var thin = zone == card.Thin && !aimed;
            SetActive(tile.Aim, aimed);
            SetActive(tile.ThinOuter, thin);
            SetActive(tile.ThinInner, thin);
            SetActive(tile.ThinMark, thin);
        }

        private void UpdateRows(IReadOnlyList<FireOverlay.Row> rows, float x, float y, float width, float height)
        {
            var count = Mathf.Min(rows.Count, rowViews.Length);
            for (var i = count; i < rowViews.Length; i++)
                SetRowVisible(rowViews[i], false);
            for (var i = 0; i < count; i++)
            {
                var row = rows[i];
                var view = rowViews[i];
                SetRowVisible(view, true);
                var rowRect = new Rect(x, y + i * height, width, height);
                var valueWidth = Mathf.Min(row.ValueWidth, width - 8f);
                view.Label.text = row.Label;
                Move(view.Label.rectTransform, rowRect.x, rowRect.y, width * 0.45f, height);
                view.Value.text = row.Value;
                Move(view.Value.rectTransform, rowRect.xMax - valueWidth, rowRect.y, valueWidth, height);
                Move(view.Rule.rectTransform, rowRect.x, rowRect.yMax - 1f, width, 1f);
                var left = rowRect.xMax - valueWidth - 6f;
                SetActive(view.Mark, row.Level.HasValue);
                if (row.Level.HasValue)
                {
                    view.Mark.sprite = Sprite(OverlayArt.Mark(row.Level.Value), 0f);
                    view.Mark.color = OverlayArt.LevelColor(row.Level.Value);
                    SetPos(view.Mark.rectTransform, left - 11f, rowRect.y + (height - 11f) / 2f);
                    left -= 17f;
                }
                SetActive(view.MeterBack, row.Meter.HasValue);
                SetActive(view.MeterFill, row.Meter.HasValue);
                SetActive(view.MeterMark, row.Meter.HasValue);
                if (row.Meter.HasValue)
                {
                    var bar = new Rect(left - 64f, rowRect.y + (height - 8f) / 2f, 64f, 8f);
                    SetPos(view.MeterBack.rectTransform, bar.x, bar.y);
                    Move(view.MeterFill.rectTransform, bar.x, bar.y, bar.width * Mathf.Clamp01(row.Meter.Value), bar.height);
                    SetPos(view.MeterMark.rectTransform, bar.x + bar.width / 2f - 1f, bar.y - 2f);
                }
            }
        }

        private void UpdateSteps(IReadOnlyList<LightingStep> steps)
        {
            var count = Mathf.Min(steps.Count, stepViews.Length);
            for (var i = count; i < stepViews.Length; i++)
                SetStepVisible(stepViews[i], false);
            var y = 2f;
            for (var i = 0; i < count; i++)
            {
                var step = steps[i];
                var view = stepViews[i];
                SetStepVisible(view, true);
                var dotColor = step.Current ? OverlayArt.Caution
                    : step.Done ? new Color(1f, 1f, 1f, 0.12f)
                    : new Color(1f, 1f, 1f, 0.05f);
                view.Dot.color = dotColor;
                Move(view.Dot.rectTransform, 0f, y + 2f, 22f, 22f);
                SetActive(view.Check, step.Done);
                SetActive(view.Number, !step.Done);
                if (step.Done)
                    Move(view.Check.rectTransform, 5f, y + 7f, 12f, 12f);
                else
                {
                    view.Number.text = (i + 1).ToString();
                    view.Number.color = step.Current ? OverlayArt.Ink : OverlayArt.Soft;
                    Move(view.Number.rectTransform, 0f, y + 2f, 22f, 22f);
                }
                view.Title.text = step.Title;
                view.Title.color = step.Current ? OverlayArt.Cream : step.Done ? OverlayArt.Dim : OverlayArt.Soft;
                Move(view.Title.rectTransform, 32f, y, OverlayFrame.Width - 2f * OverlayFrame.Pad - 32f, 26f);
                var showHow = step.Current && !string.IsNullOrEmpty(step.How);
                SetActive(view.How, showHow);
                if (showHow)
                {
                    view.How.text = step.How;
                    Move(view.How.rectTransform, 32f, y + 23f, OverlayFrame.Width - 2f * OverlayFrame.Pad - 32f, 20f);
                }
                y += 27f + (step.Current ? 18f : 0f);
            }
        }

        private static void SetGrateVisible(GrateTile tile, bool visible)
        {
            SetActive(tile.Fill, visible);
            SetActive(tile.Coal, visible);
            if (!visible)
            {
                SetActive(tile.Aim, false);
                SetActive(tile.ThinOuter, false);
                SetActive(tile.ThinInner, false);
                SetActive(tile.Clinker, false);
                SetActive(tile.ThinMark, false);
            }
        }

        private static void SetRowVisible(RowView row, bool visible)
        {
            SetActive(row.Label, visible);
            SetActive(row.Value, visible);
            SetActive(row.Rule, visible);
            if (!visible)
            {
                SetActive(row.Mark, false);
                SetActive(row.MeterBack, false);
                SetActive(row.MeterFill, false);
                SetActive(row.MeterMark, false);
            }
        }

        private static void SetStepVisible(StepView step, bool visible)
        {
            SetActive(step.Dot, visible);
            SetActive(step.Title, visible);
            if (!visible)
            {
                SetActive(step.Check, false);
                SetActive(step.Number, false);
                SetActive(step.How, false);
            }
        }

        private void BuildAdvice()
        {
            adviceFill = AddImage("Advice fill", root.transform, OverlayFrame.Pad, OverlayFrame.NextTop,
                OverlayFrame.Width - 2f * OverlayFrame.Pad, OverlayFrame.NextHeight, OverlayArt.Rounded, Color.white, 5f);
            adviceStripe = AddImage("Advice stripe", root.transform, OverlayFrame.Pad, OverlayFrame.NextTop, 3f,
                OverlayFrame.NextHeight, OverlayArt.White, OverlayArt.Soft, 0f);
            adviceMark = AddImage("Advice mark", root.transform, OverlayFrame.Pad + 12f, OverlayFrame.NextTop + 12f,
                11f, 11f, OverlayArt.Mark(AdviceLevel.Ok), OverlayArt.Soft, 0f);
            adviceText = AddText("Advice text", root.transform,
                new Rect(OverlayFrame.Pad + 30f, OverlayFrame.NextTop + 6f, OverlayFrame.Width - 2f * OverlayFrame.Pad - 40f,
                    OverlayFrame.NextHeight - 10f), "", OverlayStyle.Font, 19, OverlayArt.Cream, TextAnchor.UpperLeft);
            adviceText.horizontalOverflow = HorizontalWrapMode.Wrap;
            adviceText.verticalOverflow = VerticalWrapMode.Truncate;
        }

        private void RenderAdvice(Advice advice)
        {
            var level = advice.Level;
            var color = OverlayArt.LevelColor(level);
            adviceFill.color = level == AdviceLevel.Act
                ? new Color(OverlayArt.Alarm.r, OverlayArt.Alarm.g, OverlayArt.Alarm.b, 0.08f)
                : new Color(1f, 1f, 1f, 0.05f);
            adviceStripe.color = color;
            adviceMark.sprite = Sprite(OverlayArt.Mark(level), 0f);
            adviceMark.color = color;
            if (advice.Text == fittedAdvice)
                return;
            adviceText.text = advice.Text;
            for (var size = 19; size >= 14; size--)
            {
                adviceText.fontSize = size;
                if (adviceText.preferredHeight <= adviceText.rectTransform.rect.height)
                    break;
            }
            fittedAdvice = advice.Text;
        }

        private static Image AddImage(string name, Transform parent, float x, float y, float width, float height,
            Texture2D texture, Color color, float border)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            Place(rect, x, y, width, height);
            var image = go.GetComponent<Image>();
            image.sprite = Sprite(texture, border);
            image.type = border > 0f ? Image.Type.Sliced : Image.Type.Simple;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Text AddText(string name, Transform parent, Rect rect, string text, Font font, int size,
            Color color, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            var transform = go.GetComponent<RectTransform>();
            Place(transform, rect.x, rect.y, rect.width, rect.height);
            var label = go.GetComponent<Text>();
            label.text = text;
            if (font == null)
            {
                if (fallbackFont == null)
                    fallbackFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
                font = fallbackFont;
            }
            label.font = font;
            label.fontSize = size;
            label.alignment = anchor;
            label.color = color;
            label.supportRichText = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.raycastTarget = false;
            return label;
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            Move(rect, x, y, width, height);
        }

        // Unity dirties the graphic on every set, so write only a changed value.
        private static void Move(RectTransform rect, float x, float y, float width, float height)
        {
            SetSize(rect, width, height);
            SetPos(rect, x, y);
        }

        private static void SetSize(RectTransform rect, float width, float height)
        {
            var size = new Vector2(width, height);
            if (rect.sizeDelta != size)
                rect.sizeDelta = size;
        }

        private static void SetPos(RectTransform rect, float x, float y)
        {
            var position = new Vector2(x, -y);
            if (rect.anchoredPosition != position)
                rect.anchoredPosition = position;
        }

        private static Sprite Sprite(Texture2D texture, float border)
        {
            if (texture == null)
                return null;
            var id = texture.GetInstanceID();
            if (!Sprites.TryGetValue(id, out var sprite) || sprite == null || sprite.texture != texture)
            {
                var edge = new Vector4(border, border, border, border);
                sprite = UnityEngine.Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, edge);
                sprite.hideFlags = HideFlags.HideAndDontSave;
                Sprites[id] = sprite;
            }
            return sprite;
        }

        private void Attach(Transform leftHand, Camera camera)
        {
            root.transform.SetParent(leftHand, false);
            root.transform.localPosition = new Vector3(0.16f, 0.11f, 0.18f);
            root.transform.localRotation = Quaternion.Euler(Mathf.Clamp(Main.Settings.VrOverlayTilt, 0f, 60f), 0f, 0f);
            canvas.worldCamera = camera;
        }
    }
}