using System;
using System.Collections.Generic;
using RealisticFiring.Core;
using RealisticFiring.Sim;
using RealisticFiring.Visuals;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RealisticFiring.Items
{
 internal sealed class FiringTabletPanel
    {
        /// <summary>The panel's size in the firing tablet's canvas units, before <c>scale</c>.</summary>
        internal const int Width = 540, Height = 462;
        internal const int Pages = FiringTabletGadget.Pages, FiremanPage = FiringTabletGadget.FiremanPage;
        /// <summary>The fireman's four modes, then the engineer's two calls.</summary>
        internal const int Orders = 6;
        /// <summary>The panel's buttons: a tab for each page (0 to 3), then the orders (4 to 9).</summary>
        internal const int Buttons = Pages + Orders;
        /// <summary>Both tablets' canvases count 100 reference pixels a unit; the panels' sprites follow it.</summary>
        internal const float ReferencePixelsPerUnit = 100f;
        private const int MaxRows = 10;
        private static readonly string[] OrderNames = { "OFF", "KEEP IT ALIVE", "SHUNTING", "ROAD", "HEAVY WORK AHEAD", "EASING OFF" };

        private readonly RectTransform root;
        private readonly float scale;
        private readonly string dialKey, title;
        private Text loco, advice, waiting;
        private readonly Image[] tabs = new Image[Pages], zones = new Image[9], zoneBorders = new Image[9];
        private readonly RawImage[] dials = new RawImage[4], smoke = new RawImage[4];
        private readonly Text[] tabNames = new Text[Pages], gaugeNames = new Text[4], gaugeValues = new Text[4], coal = new Text[9];
        private readonly Image[] orderPlates = new Image[Orders];
        private readonly Text[] orderNames = new Text[Orders];
        private Text ordersHeading, callsHeading, firemanInfo;
        private readonly RawImage[] gaugeMarks = new RawImage[4], stepChecks = new RawImage[5];
        private readonly Text[] labels = new Text[MaxRows], values = new Text[MaxRows];
        private readonly Text[] stepTitles = new Text[5], stepHow = new Text[5];
        private readonly Text[] stepNumbers = new Text[5];
        private Text front, door;
        private RawImage nextBar;
        private RealisticFirebox shownCoal;
        private int thin = -1, lastAim = -2;
        private readonly RawImage[] clinker = new RawImage[9];
        private readonly Dictionary<Texture2D, Sprite> panelSprites = new Dictionary<Texture2D, Sprite>();
        private readonly bool[] buttonOn = new bool[Buttons];

        /// <summary>
        /// Builds the panel's graphics under <paramref name="root"/>, on its
        /// layer, at <paramref name="scale"/> canvas units to a firing tablet
        /// unit. <paramref name="dialKey"/> names this panel's dials in the
        /// shared cache; <paramref name="title"/> stands at the top right when
        /// no engine is read.
        /// </summary>
        internal FiringTabletPanel(RectTransform root, float scale, string dialKey, string title)
        {
            this.root = root;
            this.scale = scale;
            this.dialKey = dialKey;
            this.title = title;
            Build();
        }

        private void Build()
        {
            Picture("Background", new Rect(0, 0, Width, Height), OverlayArt.White, new Color(0.035f, 0.028f, 0.021f, 1f));
            Picture("Frame", new Rect(1, 1, Width - 2, 1), OverlayArt.White, OverlayArt.Soft);
            for (var i = 0; i < Pages; i++)
            {
                var rect = ButtonArea(i);
                tabs[i] = Panel("Page plate", rect, OverlayArt.PlateOff, Color.white);
                tabNames[i] = Words("Page", rect, 15, TextAnchor.MiddleCenter, OverlayArt.Cream);
                buttonOn[i] = true;
            }
            loco = Words("Locomotive", new Rect(382, 12, 142, 26), 16, TextAnchor.MiddleRight, OverlayArt.Soft);
            ordersHeading = Words("Orders heading", new Rect(16, 44, 508, 22), 16, TextAnchor.MiddleLeft, OverlayArt.Soft);
            ordersHeading.text = "Fireman's orders";
            callsHeading = Words("Calls heading", new Rect(16, 114, 508, 22), 16, TextAnchor.MiddleLeft, OverlayArt.Soft);
            callsHeading.text = "Engineer's calls";
            firemanInfo = Words("Fireman status", new Rect(40, 204, 460, 148), 22, TextAnchor.MiddleCenter, OverlayArt.Cream);
            for (var i = 0; i < orderPlates.Length; i++)
            {
                var area = OrderArea(i);
                orderPlates[i] = Panel("Order plate", area, OverlayArt.PlateOff, Color.white);
                orderNames[i] = Words("Order", area, 17, TextAnchor.MiddleCenter, OverlayArt.Cream);
            }
            for (var i = 0; i < 4; i++)
            {
                var x = 16 + i * 128;
                dials[i] = Picture("Dial", new Rect(x + 5, 46, 112, 87), null, Color.white);
                gaugeNames[i] = Words("Gauge", new Rect(x, 124, 122, 20), 14, TextAnchor.MiddleCenter, OverlayArt.Cream);
                gaugeMarks[i] = Picture("Reading state", new Rect(x + 2, 155, 11, 11), null, Color.white);
                gaugeValues[i] = Words("Reading", new Rect(x + 16, 147, 106, 26), 18, TextAnchor.MiddleCenter, OverlayArt.Cream);
                smoke[i] = Picture("Smoke color", new Rect(x + 25, 173, 72, 7), OverlayArt.White, Color.clear);
            }
            front = Words("Grate front", new Rect(16, 180, 194, 16), 13, TextAnchor.MiddleCenter, OverlayArt.Soft);
            front.text = "FRONT";
            door = Words("Grate door", new Rect(16, 330, 194, 16), 13, TextAnchor.MiddleCenter, OverlayArt.Soft);
            door.text = "DOOR";
            for (var r = 0; r < 3; r++)
                for (var c = 0; c < 3; c++)
                {
                    var index = (int)GrateLayout.At(2 - r, c);
                    var rect = new Rect(16 + c * 66, 198 + r * 44, 62, 40);
                    zoneBorders[index] = Panel("Zone marker", new Rect(rect.x - 3, rect.y - 3, rect.width + 6, rect.height + 6), OverlayArt.Rounded, Color.clear);
                    zones[index] = Panel("Grate zone", rect, OverlayArt.Rounded, Color.white);
                    clinker[index] = Picture("Clinker", new Rect(rect.x + 4, rect.yMax - 7, rect.width - 8, 4), OverlayArt.White, new Color(0.6f, 0.63f, 0.65f));
                    coal[index] = Words("Coal kg", rect, 17, TextAnchor.MiddleCenter, OverlayArt.Cream);
                }
            for (var i = 0; i < MaxRows; i++)
            {
                labels[i] = Words("Row label", new Rect(), 16, TextAnchor.MiddleLeft, OverlayArt.Soft);
                values[i] = Words("Row value", new Rect(), 19, TextAnchor.MiddleRight, OverlayArt.Cream);
            }
            for (var i = 0; i < 5; i++)
            {
                stepTitles[i] = Words("Lighting step", new Rect(), 17, TextAnchor.MiddleLeft, OverlayArt.Cream);
                stepHow[i] = Words("Lighting instruction", new Rect(), 14, TextAnchor.MiddleLeft, OverlayArt.Soft);
                stepChecks[i] = Picture("Step completed", new Rect(), OverlayArt.Check, OverlayArt.Cream);
                stepNumbers[i] = Words("Step number", new Rect(), 17, TextAnchor.MiddleCenter, OverlayArt.Soft);
            }
            Picture("Advice background", new Rect(16, 388, 508, 64), OverlayArt.White, new Color(1f, 1f, 1f, 0.05f));
            nextBar = Picture("Advice level", new Rect(16, 388, 3, 64), OverlayArt.White, OverlayArt.Soft);
            advice = Words("Next step", new Rect(29, 394, 483, 52), 19, TextAnchor.UpperLeft, OverlayArt.Cream);
            advice.horizontalOverflow = HorizontalWrapMode.Wrap;
            waiting = Words("No engine", new Rect(40, 185, 460, 130), 24, TextAnchor.MiddleCenter, OverlayArt.Cream);
            waiting.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        // ---- Buttons -----------------------------------------------------------

        /// <summary>Where button <paramref name="slot"/> lies in the firing tablet's canvas units: a page tab, then the orders.</summary>
        internal static Rect ButtonArea(int slot) => slot < Pages ? new Rect(16 + slot * 90, 12, 84, 26) : OrderArea(slot - Pages);

        internal static Rect OrderArea(int order)
        {
            if (order == 0) return new Rect(16, 68, 80, 40);
            if (order == 1) return new Rect(104, 68, 160, 40);
            if (order == 2) return new Rect(272, 68, 136, 40);
            if (order == 3) return new Rect(416, 68, 108, 40);
            return new Rect(order == 4 ? 16 : 274, 138, 250, 40);
        }

        /// <summary>
        /// Puts each of the panel's buttons over its rectangle (in the root's
        /// units: <see cref="ButtonArea"/> times the scale), or takes it away:
        /// the tabs always, the orders only on the Fireman page with a fireman.
        /// </summary>
        internal void PlaceButtons(Action<int, Rect, bool> place)
        {
            for (var i = 0; i < Buttons; i++)
                place(i, Scaled(ButtonArea(i)), buttonOn[i]);
        }

        /// <summary>Takes the orders' buttons away before the next reading, when another page is chosen.</summary>
        internal void HideOrders()
        {
            for (var i = Pages; i < Buttons; i++)
                buttonOn[i] = false;
        }

        /// <summary>
        /// Gives the fireman of <paramref name="car"/> an order: a mode (0 to
        /// 3) or the engineer's call (4 heavy work ahead, 5 easing off), through
        /// the same <see cref="FiremanOrders"/> F7 uses (a multiplayer guest's
        /// go to the host). Resolved on every press, never from a cached
        /// reading. False if the car has no fireman.
        /// </summary>
        internal static bool GiveOrder(TrainCar car, int order)
        {
            if (!FiremanRig.TryGetTended(car, out var rig))
                return false;
            if (order >= 0 && order < 4)
                FiremanOrders.Order(rig, (FiremanMode)order);
            else if (order == 4 || order == 5)
                FiremanOrders.Call(rig, order == 4 ? FiremanCall.HeavyAhead : FiremanCall.EasingOff);
            return true;
        }

        // ---- Reading -----------------------------------------------------------

        /// <summary>
        /// Reads <paramref name="page"/> of <paramref name="car"/> and lays it
        /// out; <paramref name="mounted"/> says the screen is on a mount (its
        /// wording when the car has no fire).
        /// </summary>
        internal void Read(TrainCar car, int page, bool mounted)
        {
            FireOverlay.Card card = null;
            FiremanRig fireman = null;
            shownCoal = null;
            if (page == FiremanPage)
            {
                if (FiremanRig.TryGetTended(car, out fireman))
                    card = FiremanPanel.ReadTablet(fireman);
            }
            else if (FireboxRegistry.TryGetTended(car, out var fire))
            {
                shownCoal = fire;
                card = FireOverlay.Read(fire, page);
            }
            else if (RealisticOilFirebox.TryGetTended(car, out var oil))
                card = FireOverlay.Read(oil, page);
            Show(card, page, fireman, mounted);
            UpdateAim();
        }

        private void Show(FireOverlay.Card card, int page, FiremanRig fireman, bool mounted)
        {
            var hasData = card != null;
            var orders = page == FiremanPage;
            waiting.gameObject.SetActive(!hasData);
            waiting.text = mounted
                ? "No supported steam engine\nThis car has no firing readings."
                : "No supported steam engine\nCarry the tablet aboard a locomotive to read its fire.";
            loco.text = hasData ? card.Loco : title;
            for (var i = 0; i < Pages; i++)
            {
                tabs[i].sprite = PanelSprite(i == page ? OverlayArt.PlateOn : OverlayArt.PlateOff);
                tabNames[i].color = i == page ? OverlayArt.Cream : OverlayArt.Dim;
                tabNames[i].text = i == 0 ? (hasData && card.Oil ? "BURNER" : "FIRE") : i == 1 ? "WATER" : i == 2 ? "ENGINE" : "FIREMAN";
            }
            ordersHeading.gameObject.SetActive(orders && hasData);
            callsHeading.gameObject.SetActive(orders && hasData);
            firemanInfo.gameObject.SetActive(orders && hasData && card.Rows.Count == 0);
            firemanInfo.text = Net.IsClient ? "Orders go to the host's fireman.\nLive readings are on the other tabs."
                : "The fire and controls are yours.\nChoose an order to have the fireman take over.";
            for (var i = 0; i < orderPlates.Length; i++)
            {
                var show = orders && fireman != null;
                orderPlates[i].gameObject.SetActive(show);
                orderNames[i].gameObject.SetActive(show);
                buttonOn[Pages + i] = show;
                if (!show) continue;
                var active = i < 4 ? FiremanOrders.ModeOf(fireman) == (FiremanMode)i
                    : FiremanOrders.CallActive(fireman, i == 4 ? FiremanCall.HeavyAhead : FiremanCall.EasingOff);
                orderPlates[i].sprite = PanelSprite(active ? OverlayArt.PlateOn : OverlayArt.PlateOff);
                orderNames[i].color = active ? OverlayArt.Cream : OverlayArt.Dim;
                orderNames[i].text = OrderNames[i] + (active && i >= 4 ? $" · {fireman.Fireman.CallLeft:F0} s" : "");
            }
            for (var i = 0; i < 4; i++)
            {
                dials[i].gameObject.SetActive(hasData && !orders);
                gaugeNames[i].gameObject.SetActive(hasData && !orders);
                gaugeValues[i].gameObject.SetActive(hasData && !orders);
                gaugeMarks[i].gameObject.SetActive(hasData && !orders);
                smoke[i].gameObject.SetActive(hasData && !orders && card.Gauges[i].Smoke.HasValue);
                if (!hasData || orders)
                    continue;
                var gauge = card.Gauges[i];
                dials[i].texture = OverlayArt.Dial(dialKey + i, gauge.Value, gauge.Band0, gauge.Band1, gauge.Level);
                gaugeNames[i].text = gauge.Label;
                gaugeValues[i].text = gauge.Text;
                gaugeMarks[i].texture = OverlayArt.Mark(gauge.Level);
                gaugeMarks[i].color = OverlayArt.LevelColor(gauge.Level);
                smoke[i].color = gauge.Smoke ?? Color.clear;
            }
            var grate = hasData && card.ZoneCoal != null;
            thin = grate ? card.Thin : -1;
            front.gameObject.SetActive(grate);
            door.gameObject.SetActive(grate);
            for (var i = 0; i < 9; i++)
            {
                zones[i].gameObject.SetActive(grate);
                zoneBorders[i].gameObject.SetActive(grate);
                clinker[i].gameObject.SetActive(grate && card.ZoneClinker[i] > 0.1f);
                coal[i].gameObject.SetActive(grate);
                if (!grate)
                    continue;
                zones[i].color = OverlayArt.Ember(card.ZoneGlow[i]);
                coal[i].text = card.ZoneCoal[i].ToString("F1") + (card.Thin == i ? " ▲" : "");
                coal[i].color = card.ZoneGlow[i] > 0.72f ? OverlayArt.Ink : OverlayArt.Cream;
                clinker[i].rectTransform.sizeDelta = new Vector2(54f * Mathf.Clamp(card.ZoneClinker[i], 0.15f, 1f), 4f) * scale;
            }
            var rows = hasData ? card.Rows.Count : 0;
            var rowHeight = Mathf.Min(32f, (orders ? 184f : 196f) / Mathf.Max(1, rows));
            var rowsTop = orders ? 194f : 180f;
            var left = grate ? 238f : 16f;
            var width = 524f - left;
            for (var i = 0; i < MaxRows; i++)
            {
                labels[i].gameObject.SetActive(i < rows);
                values[i].gameObject.SetActive(i < rows);
                if (i >= rows)
                    continue;
                var row = card.Rows[i];
                Place(labels[i].rectTransform, new Rect(left, rowsTop + i * rowHeight, width * 0.32f, rowHeight));
                Place(values[i].rectTransform, new Rect(left + width * 0.33f, rowsTop + i * rowHeight, width * 0.67f, rowHeight));
                labels[i].text = row.Label;
                values[i].text = row.Value;
                values[i].color = row.Level.HasValue ? OverlayArt.LevelColor(row.Level.Value) : OverlayArt.Cream;
            }
            var y = 182f;
            for (var i = 0; i < 5; i++)
            {
                var exists = hasData && card.Steps != null && i < card.Steps.Count;
                stepTitles[i].gameObject.SetActive(exists);
                stepHow[i].gameObject.SetActive(exists && card.Steps[i].Current);
                stepChecks[i].gameObject.SetActive(exists && card.Steps[i].Done);
                stepNumbers[i].gameObject.SetActive(exists && !card.Steps[i].Done);
                if (!exists)
                    continue;
                var step = card.Steps[i];
                Place(stepTitles[i].rectTransform, new Rect(48, y, 476, 26));
                Place(stepChecks[i].rectTransform, new Rect(20, y + 6, 14, 14));
                Place(stepNumbers[i].rectTransform, new Rect(16, y, 22, 26));
                stepNumbers[i].text = (i + 1).ToString();
                stepNumbers[i].color = step.Current ? OverlayArt.Caution : OverlayArt.Soft;
                stepTitles[i].text = step.Title;
                stepTitles[i].color = step.Current ? OverlayArt.Caution : step.Done ? OverlayArt.Dim : OverlayArt.Soft;
                y += 27f;
                if (step.Current)
                {
                    Place(stepHow[i].rectTransform, new Rect(48, y - 4, 476, 20));
                    stepHow[i].text = step.How;
                    y += 18f;
                }
            }
            advice.text = hasData ? card.Advice.Text : "F6 remains available as a screen overlay.";
            nextBar.color = hasData ? OverlayArt.LevelColor(card.Advice.Level) : OverlayArt.Soft;
        }

        /// <summary>
        /// Marks the grate zone the shovel aims at (and the thin one) from the
        /// fire last read; true when the aimed zone changed, so the screen
        /// must be drawn again.
        /// </summary>
        internal bool UpdateAim()
        {
            if (shownCoal == null || zones[0] == null || !zones[0].gameObject.activeSelf)
                return false;
            var aim = shownCoal.HoverAim;
            var aimed = aim.HasValue && aim.Value.IsLiveAt(Time.time) ? (int)aim.Value.Zone : -1;
            var changed = aimed != lastAim;
            lastAim = aimed;
            for (var i = 0; i < 9; i++)
                zoneBorders[i].color = i == aimed ? OverlayArt.Steam
                    : i == thin ? OverlayArt.Caution : Color.clear;
            return changed;
        }

        /// <summary>Lets go of the panel's sprites and dials; its graphics go with the root.</summary>
        internal void Release()
        {
            foreach (var sprite in panelSprites.Values)
                if (sprite != null) Object.Destroy(sprite);
            panelSprites.Clear();
            for (var i = 0; i < 4; i++)
                OverlayArt.ReleaseDial(dialKey + i);
        }

        // ---- Graphics ----------------------------------------------------------

        private Rect Scaled(Rect area) => new Rect(area.x * scale, area.y * scale, area.width * scale, area.height * scale);

        private RectTransform Rect(string name, Rect area)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = root.gameObject.layer;
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(root, false);
            Place(rect, area);
            return rect;
        }

        private void Place(RectTransform rect, Rect area)
        {
            var scaled = Scaled(area);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(scaled.x, -scaled.y);
            rect.sizeDelta = scaled.size;
        }

        private RawImage Picture(string name, Rect area, Texture texture, Color color)
        {
            var image = Rect(name, area).gameObject.AddComponent<RawImage>();
            image.texture = texture;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private Image Panel(string name, Rect area, Texture2D texture, Color color)
        {
            var image = Rect(name, area).gameObject.AddComponent<Image>();
            image.sprite = PanelSprite(texture);
            image.type = Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private Sprite PanelSprite(Texture2D texture)
        {
            if (panelSprites.TryGetValue(texture, out var sprite) && sprite != null)
                return sprite;
            // Match F6's five-pixel slicing, keeping corner size and border
            // width fixed. RawImage stretched these assets across the whole button
            // or cell, enlarging their antialiased edges into a soft band. At a
            // larger scale the sprite counts fewer pixels a unit, so its
            // borders keep their size on the screen.
            sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), ReferencePixelsPerUnit / scale, 0, SpriteMeshType.FullRect,
                new Vector4(5f, 5f, 5f, 5f), false);
            sprite.name = "RealisticFiring_TabletPanel";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            panelSprites[texture] = sprite;
            return sprite;
        }

        private Text Words(string name, Rect area, int size, TextAnchor alignment, Color color)
        {
            var text = Rect(name, area).gameObject.AddComponent<Text>();
            var shown = Mathf.RoundToInt(size * scale);
            text.font = OverlayStyle.Font != null ? OverlayStyle.Font : Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = shown;
            text.alignment = alignment;
            text.color = color;
            text.supportRichText = false;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = Mathf.RoundToInt(12 * scale);
            text.resizeTextMaxSize = shown;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }
    }
}
