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
        private static readonly Dictionary<int, Sprite> Sprites = new Dictionary<int, Sprite>();
        private static VrOverlayFrame activeFrame;

        private GameObject root;
        private Canvas canvas;
        private Image[] gaugeFaces;
        private Image[] gaugePlates;
        private Text[] gaugeLabels;
        private Text[] gaugeValues;
        private Image[] gaugeMarks;
        private Image[] gaugeSmokes;
        private Image[] tabs;
        private Text[] tabNames;
        private Text loco;
        private Transform body;
        private Image adviceFill;
        private Image adviceStripe;
        private Image adviceMark;
        private Text adviceText;
        private FireOverlay.Card shownCard;
        private int shownPage = -1;
        private bool shownOil;
        private bool leftClickWasPressed;
        private int shownAim = -2;

        public void Show(Camera camera, FireOverlay.Card card, int page, int aimedZone)
        {
            var leftHand = VRTK_DeviceFinder.GetControllerLeftHand();
            if (camera == null || leftHand == null || card == null)
            {
                Hide();
                return;
            }
            EnsureBuilt(camera);
            if (root.transform.parent != leftHand.transform)
                Attach(leftHand.transform, camera);
            if (!root.activeSelf)
                root.SetActive(true);
            var freshCard = !ReferenceEquals(card, shownCard);
            if (freshCard)
            {
                shownCard = card;
                RenderGauges(card);
                RenderAdvice(card.Advice);
                loco.text = card.Loco;
            }
            if (freshCard || shownAim != aimedZone)
            {
                shownAim = aimedZone;
                RenderBody(card, aimedZone);
            }
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
            var device = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            var pressed = device.isValid
                          && device.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out var clicked)
                          && clicked;
            if (XRSettings.enabled && pressed && !leftClickWasPressed)
                FireOverlay.Cycle(Main.Settings);
            leftClickWasPressed = pressed;
        }

        public void Hide()
        {
            if (root != null && root.activeSelf)
                root.SetActive(false);
        }

        public static void ApplyTilt(float degrees)
        {
            if (activeFrame != null && activeFrame.root != null)
                activeFrame.root.transform.localRotation = Quaternion.Euler(Mathf.Clamp(degrees, 0f, 60f), 0f, 0f);
        }

        private void EnsureBuilt(Camera camera)
        {
            if (root != null)
                return;
            root = new GameObject("RealisticFiring VR overlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(Image));
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
            var cell = (OverlayFrame.Width - 2f * OverlayFrame.Pad - 3f * OverlayFrame.GaugeGap) / 4f;
            for (var i = 0; i < 4; i++)
                BuildGauge(i, OverlayFrame.Pad + i * (cell + OverlayFrame.GaugeGap), OverlayFrame.GaugeTop, cell);

            body = new GameObject("Reading body", typeof(RectTransform)).transform;
            body.SetParent(root.transform, false);
            Place(body as RectTransform, OverlayFrame.Pad, OverlayFrame.BodyTop,
                OverlayFrame.Width - 2f * OverlayFrame.Pad, OverlayFrame.BodyHeight);
            BuildAdvice();
            root.SetActive(false);
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
            gaugeSmokes[index] = AddImage("Gauge smoke " + index, root.transform,
                x + (width - 72f) / 2f, y + OverlayFrame.GaugeSmokeTop, 72f, 7f, OverlayArt.White, Color.clear, 0f);
            AddImage("Gauge smoke edge " + index, root.transform,
                x + (width - 74f) / 2f, y + OverlayFrame.GaugeSmokeTop - 1f, 74f, 9f, OverlayArt.White,
                new Color(1f, 1f, 1f, 0.3f), 0f).transform.SetAsFirstSibling();
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
                gaugePlates[i].rectTransform.sizeDelta = new Vector2(plateWidth, OverlayFrame.GaugePlateHeight);
                gaugePlates[i].rectTransform.anchoredPosition = new Vector2(
                    OverlayFrame.Pad + i * (cell + OverlayFrame.GaugeGap) + (cell - plateWidth) / 2f,
                    -(OverlayFrame.GaugeTop + OverlayFrame.GaugePlateTop));
                gaugeLabels[i].rectTransform.sizeDelta = new Vector2(plateWidth, OverlayFrame.GaugePlateHeight);
                gaugeLabels[i].rectTransform.anchoredPosition = gaugePlates[i].rectTransform.anchoredPosition;
                gaugeValues[i].text = gauge.Text;
                var total = 12f + 6f + gauge.TextWidth;
                var start = OverlayFrame.Pad + i * (cell + OverlayFrame.GaugeGap) + (cell - total) / 2f;
                gaugeMarks[i].rectTransform.anchoredPosition = new Vector2(start, -(OverlayFrame.GaugeTop + OverlayFrame.GaugeMarkTop));
                gaugeMarks[i].sprite = Sprite(OverlayArt.Mark(gauge.Level), 0f);
                gaugeMarks[i].color = OverlayArt.LevelColor(gauge.Level);
                gaugeValues[i].rectTransform.anchoredPosition = new Vector2(start + 18f, -(OverlayFrame.GaugeTop + OverlayFrame.GaugeValueTop));
                gaugeValues[i].rectTransform.sizeDelta = new Vector2(gauge.TextWidth + 4f, OverlayFrame.GaugeValueHeight);
                gaugeValues[i].alignment = TextAnchor.MiddleLeft;
                gaugeSmokes[i].gameObject.SetActive(gauge.Smoke.HasValue);
                if (gauge.Smoke.HasValue)
                    gaugeSmokes[i].color = gauge.Smoke.Value;
            }
        }

        private void RenderBody(FireOverlay.Card card, int aimedZone)
        {
            ClearChildren(body);
            if (card.Steps != null)
                DrawSteps(card.Steps);
            else if (card.ZoneCoal != null)
            {
                DrawGrate(card, aimedZone);
                DrawRows(card.Rows, 222f, 0f, OverlayFrame.Width - 2f * OverlayFrame.Pad - 222f, 30f);
            }
            else
                DrawRows(card.Rows, 0f, 0f, OverlayFrame.Width - 2f * OverlayFrame.Pad,
                    Mathf.Min(32f, OverlayFrame.BodyHeight / Mathf.Max(1, card.Rows.Count)));
        }

        private void DrawGrate(FireOverlay.Card card, int aimedZone)
        {
            const float tileWidth = 62f;
            const float tileHeight = 40f;
            const float gap = 4f;
            var width = 3f * tileWidth + 2f * gap;
            AddText("Grate front", body, new Rect(0f, 0f, width, 16f), "FRONT", OverlayStyle.PlateFont, 13,
                OverlayArt.Soft, TextAnchor.MiddleCenter);
            for (var row = GrateLayout.Rows - 1; row >= 0; row--)
            for (var col = 0; col < GrateLayout.Columns; col++)
            {
                var zone = (int)GrateLayout.At(row, col);
                var y = 18f + (GrateLayout.Rows - 1 - row) * (tileHeight + gap);
                var x = col * (tileWidth + gap);
                var rect = new Rect(x, y, tileWidth, tileHeight);
                if (zone == aimedZone)
                    AddImage("Aimed zone " + zone, body, x - 3f, y - 3f, tileWidth + 6f, tileHeight + 6f,
                        OverlayArt.Rounded, OverlayArt.Steam, 5f);
                else if (zone == card.Thin)
                {
                    AddImage("Thin zone edge " + zone, body, x - 4f, y - 4f, tileWidth + 8f, tileHeight + 8f,
                        OverlayArt.Rounded, OverlayArt.Caution, 5f);
                    AddImage("Thin zone inset " + zone, body, x - 2f, y - 2f, tileWidth + 4f, tileHeight + 4f,
                        OverlayArt.Rounded, OverlayArt.Ink, 5f);
                }
                var glow = card.ZoneGlow[zone];
                AddImage("Zone " + zone, body, x, y, tileWidth, tileHeight, OverlayArt.Rounded, OverlayArt.Ember(glow), 4f);
                if (card.ZoneClinker[zone] > 0.1f)
                    AddImage("Zone clinker " + zone, body, x + 4f, y + tileHeight - 7f,
                        (tileWidth - 8f) * Mathf.Clamp(card.ZoneClinker[zone], 0.15f, 1f), 4f, OverlayArt.White,
                        new Color(0.6f, 0.63f, 0.65f), 0f);
                AddText("Zone coal " + zone, body, rect, card.ZoneCoal[zone].ToString("F1"), OverlayStyle.PlateFont, 17,
                    glow > 0.72f ? OverlayArt.Ink : OverlayArt.Cream, TextAnchor.MiddleCenter);
                if (zone == card.Thin && zone != aimedZone)
                    AddImage("Thin zone mark " + zone, body, x + tileWidth - 13f, y + 3f, 10f, 10f,
                        OverlayArt.Mark(AdviceLevel.Watch), OverlayArt.Ink, 0f);
            }
            AddText("Grate door", body, new Rect(0f, 18f + 3f * (tileHeight + gap), width, 16f), "DOOR",
                OverlayStyle.PlateFont, 13, OverlayArt.Soft, TextAnchor.MiddleCenter);
        }

        private void DrawRows(IReadOnlyList<FireOverlay.Row> rows, float x, float y, float width, float height)
        {
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var rowRect = new Rect(x, y + i * height, width, height);
                AddText("Row label " + i, body, new Rect(rowRect.x, rowRect.y, width * 0.45f, height), row.Label,
                    OverlayStyle.Font, 16, OverlayArt.Soft, TextAnchor.MiddleLeft);
                var valueWidth = Mathf.Min(row.ValueWidth, width - 8f);
                AddText("Row value " + i, body, new Rect(rowRect.xMax - valueWidth, rowRect.y, valueWidth, height), row.Value,
                    OverlayStyle.Font, 19, OverlayArt.Cream, TextAnchor.MiddleRight);
                var left = rowRect.xMax - valueWidth - 6f;
                if (row.Level.HasValue)
                {
                    AddImage("Row warning " + i, body, left - 11f, rowRect.y + (height - 11f) / 2f, 11f, 11f,
                        OverlayArt.Mark(row.Level.Value), OverlayArt.LevelColor(row.Level.Value), 0f);
                    left -= 17f;
                }
                if (row.Meter.HasValue)
                {
                    var bar = new Rect(left - 64f, rowRect.y + (height - 8f) / 2f, 64f, 8f);
                    AddImage("Meter back " + i, body, bar.x, bar.y, bar.width, bar.height, OverlayArt.White,
                        new Color(1f, 1f, 1f, 0.1f), 0f);
                    AddImage("Meter fill " + i, body, bar.x, bar.y, bar.width * Mathf.Clamp01(row.Meter.Value), bar.height,
                        OverlayArt.White, OverlayArt.Soft, 0f);
                    AddImage("Meter marker " + i, body, bar.x + bar.width / 2f - 1f, bar.y - 2f, 2f, bar.height + 4f,
                        OverlayArt.White, OverlayArt.Caution, 0f);
                }
                AddImage("Row rule " + i, body, rowRect.x, rowRect.yMax - 1f, width, 1f, OverlayArt.White, OverlayArt.Rule, 0f);
            }
        }

        private void DrawSteps(IReadOnlyList<LightingStep> steps)
        {
            var y = 2f;
            for (var i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                var dotColor = step.Current ? OverlayArt.Caution
                    : step.Done ? new Color(1f, 1f, 1f, 0.12f)
                    : new Color(1f, 1f, 1f, 0.05f);
                AddImage("Lighting step " + i, body, 0f, y + 2f, 22f, 22f, OverlayArt.Rounded, dotColor, 4f);
                if (step.Done)
                    AddImage("Lighting check " + i, body, 5f, y + 7f, 12f, 12f, OverlayArt.Check, OverlayArt.Cream, 0f);
                else
                    AddText("Lighting number " + i, body, new Rect(0f, y + 2f, 22f, 22f), (i + 1).ToString(),
                        OverlayStyle.PlateFont, 13, step.Current ? OverlayArt.Ink : OverlayArt.Soft, TextAnchor.MiddleCenter);
                var textColor = step.Current ? OverlayArt.Cream : step.Done ? OverlayArt.Dim : OverlayArt.Soft;
                AddText("Lighting title " + i, body, new Rect(32f, y, OverlayFrame.Width - 2f * OverlayFrame.Pad - 32f, 26f),
                    step.Title, OverlayStyle.Font, 17, textColor, TextAnchor.MiddleLeft);
                y += 27f;
                if (step.Current)
                {
                    AddText("Lighting detail " + i, body, new Rect(32f, y - 4f, OverlayFrame.Width - 2f * OverlayFrame.Pad - 32f, 20f),
                        step.How, OverlayStyle.PlateFont, 14, OverlayArt.Dim, TextAnchor.MiddleLeft);
                    y += 18f;
                }
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
            adviceText.text = advice.Text;
            for (var size = 19; size >= 14; size--)
            {
                adviceText.fontSize = size;
                if (adviceText.preferredHeight <= adviceText.rectTransform.rect.height)
                    break;
            }
        }

        private static void ClearChildren(Transform parent)
        {
            for (var i = parent.childCount - 1; i >= 0; i--)
                Object.Destroy(parent.GetChild(i).gameObject);
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
            Color color, TextAnchor anchor, Texture2D background = null, float border = 0f)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            var transform = go.GetComponent<RectTransform>();
            Place(transform, rect.x, rect.y, rect.width, rect.height);
            var label = go.GetComponent<Text>();
            label.text = text;
            label.font = font;
            label.fontSize = size;
            label.alignment = anchor;
            label.color = color;
            label.supportRichText = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.raycastTarget = false;
            if (background != null)
            {
                var image = go.AddComponent<Image>();
                image.sprite = Sprite(background, border);
                image.type = border > 0f ? Image.Type.Sliced : Image.Type.Simple;
                image.color = Color.white;
                image.raycastTarget = false;
            }
            return label;
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, -y);
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