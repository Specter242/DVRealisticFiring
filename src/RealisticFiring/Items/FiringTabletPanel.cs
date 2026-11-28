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
        internal const int Pages = FiringTabletGadget.Pages;
        internal const int FiremanPage = FiringTabletGadget.FiremanPage;
        internal const int ChecklistPage = FiringTabletGadget.ChecklistPage;
        /// <summary>The fireman's four modes, then the engineer's two calls.</summary>
        internal const int Orders = 6;
        /// <summary>The checklist page's four action buttons: Prev, Auto, Next, and Cab Controls spotlight.</summary>
        internal const int ChecklistActions = 4;
        /// <summary>The panel's buttons: a tab for each page (0 to 4), then up to 6 page action buttons (5 to 10).</summary>
        internal const int Buttons = Pages + Orders;
        /// <summary>Both tablets' canvases count 100 reference pixels a unit; the panels' sprites follow it.</summary>
        internal const float ReferencePixelsPerUnit = 100f;
        private const int MaxRows = 10;
        private const int MaxSteps = 7;
        private static readonly string[] OrderNames = { "OFF", "KEEP IT ALIVE", "SHUNTING", "ROAD", "HEAVY WORK AHEAD", "EASING OFF" };
        private static readonly string[] ChecklistActionNames = { "◀ PREV", "AUTO", "NEXT ▶", "CAB" };

        private readonly RectTransform root;
        private readonly float scale;
        private readonly string dialKey, title;
        private readonly bool vr;
        private Text loco, advice, waiting;
        private readonly Image[] tabs = new Image[Pages], zones = new Image[9], zoneBorders = new Image[9];
        private readonly RawImage[] dials = new RawImage[4], smoke = new RawImage[4];
        private readonly Text[] tabNames = new Text[Pages], gaugeNames = new Text[4], gaugeValues = new Text[4], coal = new Text[9];
        private readonly Image[] orderPlates = new Image[Orders];
        private readonly Text[] orderNames = new Text[Orders];
        private readonly Image[] checklistPlates = new Image[ChecklistActions];
        private readonly Text[] checklistPlateNames = new Text[ChecklistActions];
        private Text checklistHeading;
        private Text ordersHeading, callsHeading, firemanInfo;
        private readonly RawImage[] gaugeMarks = new RawImage[4], stepChecks = new RawImage[MaxSteps];
        private readonly Text[] labels = new Text[MaxRows], values = new Text[MaxRows];
        private readonly Text[] stepTitles = new Text[MaxSteps], stepHow = new Text[MaxSteps];
        private readonly Text[] stepNumbers = new Text[MaxSteps];
        private Text front, door;
        private RawImage nextBar;
        private RealisticFirebox shownCoal;
        private int thin = -1, lastAim = -2;
        private readonly RawImage[] clinker = new RawImage[9];
        private readonly Dictionary<Texture2D, Sprite> panelSprites = new Dictionary<Texture2D, Sprite>();
        private readonly bool[] buttonOn = new bool[Buttons];

        private bool onChecklistPage;
        private ChecklistId? manualChecklist;
        private ChecklistId lastChecklistId;
        private ChecklistSnapshot lastSnapshot;

        /// <summary>
        /// Builds the panel's graphics under <paramref name="root"/>, on its
        /// layer, at <paramref name="scale"/> canvas units to a firing tablet
        /// unit. <paramref name="dialKey"/> names this panel's dials in the
        /// shared cache; <paramref name="title"/> stands at the top right when
        /// no engine is read.
        /// </summary>
        internal FiringTabletPanel(RectTransform root, float scale, string dialKey, string title, bool vr = false)
        {
            this.root = root;
            this.scale = scale;
            this.dialKey = dialKey;
            this.title = title;
            this.vr = vr;
            Build();
        }

        /// <summary>Whether the OnGUI cab spotlight button is available on this tablet (desktop only).</summary>
        internal bool ShowSpotlightButton => !vr && CabControlSpotlight.Supported;

        private void Build()
        {
            Picture("Background", new Rect(0, 0, Width, Height), OverlayArt.White, new Color(0.035f, 0.028f, 0.021f, 1f));
            Picture("Frame", new Rect(1, 1, Width - 2, 1), OverlayArt.White, OverlayArt.Soft);
            for (var i = 0; i < Pages; i++)
            {
                var rect = ButtonArea(i);
                tabs[i] = Panel("Page plate", rect, OverlayArt.PlateOff, Color.white);
                tabNames[i] = Words("Page", rect, 14, TextAnchor.MiddleCenter, OverlayArt.Cream);
                buttonOn[i] = true;
            }
            loco = Words("Locomotive", new Rect(400, 12, 124, 26), 15, TextAnchor.MiddleRight, OverlayArt.Soft);
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
            checklistHeading = Words("Checklist heading", new Rect(78, 46, 252, 30), 17, TextAnchor.MiddleCenter, OverlayArt.Cream);
            for (var i = 0; i < ChecklistActions; i++)
            {
                var area = ChecklistButtonArea(i);
                checklistPlates[i] = Panel("Checklist button", area, OverlayArt.PlateOff, Color.white);
                checklistPlateNames[i] = Words("Checklist button label", area, 13, TextAnchor.MiddleCenter, OverlayArt.Cream);
                checklistPlateNames[i].text = ChecklistActionNames[i];
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
            for (var i = 0; i < MaxSteps; i++)
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

        /// <summary>Where button <paramref name="slot"/> lies in the firing tablet's canvas units: a page tab, then the Fireman orders.</summary>
        internal static Rect ButtonArea(int slot) => slot < Pages ? new Rect(16 + slot * 76, 12, 72, 26) : OrderArea(slot - Pages);

        internal static Rect OrderArea(int order)
        {
            if (order == 0) return new Rect(16, 68, 80, 40);
            if (order == 1) return new Rect(104, 68, 160, 40);
            if (order == 2) return new Rect(272, 68, 136, 40);
            if (order == 3) return new Rect(416, 68, 108, 40);
            return new Rect(order == 4 ? 16 : 274, 138, 250, 40);
        }

        /// <summary>Where checklist action <paramref name="action"/> lies on the Check page (0 Prev, 1 Auto, 2 Next, 3 Cab controls).</summary>
        internal static Rect ChecklistButtonArea(int action)
        {
            if (action == 0) return new Rect(16, 46, 56, 30);
            if (action == 1) return new Rect(336, 46, 62, 30);
            if (action == 2) return new Rect(404, 46, 56, 30);
            return new Rect(466, 46, 58, 30);
        }

        /// <summary>
        /// Puts each of the panel's buttons over its rectangle (in the root's
        /// units: <see cref="ButtonArea"/> or <see cref="ChecklistButtonArea"/>
        /// times the scale), or takes it away.
        /// </summary>
        internal void PlaceButtons(Action<int, Rect, bool> place)
        {
            for (var i = 0; i < Buttons; i++)
            {
                var area = i < Pages ? ButtonArea(i)
                    : onChecklistPage && i - Pages < ChecklistActions ? ChecklistButtonArea(i - Pages)
                    : OrderArea(i - Pages);
                place(i, Scaled(area), buttonOn[i]);
            }
        }

        /// <summary>Takes the page action buttons away before the next reading, when another page is chosen.</summary>
        internal void HideOrders()
        {
            for (var i = Pages; i < Buttons; i++)
                buttonOn[i] = false;
        }

        /// <summary>Handles an action button pressed on the Check page (0 Prev, 1 Auto, 2 Next, 3 Cab controls spotlight).</summary>
        internal void HandleChecklistAction(TrainCar car, int action)
        {
            switch (action)
            {
                case 0:
                    manualChecklist = FiringChecklists.StepSelection(lastSnapshot, lastChecklistId, -1);
                    break;
                case 1:
                    manualChecklist = null;
                    break;
                case 2:
                    manualChecklist = FiringChecklists.StepSelection(lastSnapshot, lastChecklistId, 1);
                    break;
                case 3:
                {
                    if (!ShowSpotlightButton)
                        break;
                    var notice = CabControlSpotlight.Toggle(car);
                    if (!string.IsNullOrEmpty(notice))
                        FireController.Announce(notice);
                    break;
                }
            }
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
            Checklist checklist = null;
            shownCoal = null;
            if (page == ChecklistPage)
            {
                if (ChecklistReader.TryRead(car, manualChecklist, out var snapshot, out checklist))
                {
                    lastSnapshot = snapshot;
                    lastChecklistId = checklist.Id;
                    if (FireboxRegistry.TryGetTended(car, out var coal))
                        card = FireOverlay.Read(coal, 0);
                    else if (RealisticOilFirebox.TryGetTended(car, out var oil))
                        card = FireOverlay.Read(oil, 0);
                }
            }
            else if (page == FiremanPage)
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
            Show(card, page, fireman, checklist, mounted);
            UpdateAim();
        }

        /// <summary>
        /// Lays out the Fire page with an example reading of a good coal fire
        /// under way, for the tablet's inventory icon (<see cref="FiringTabletBody"/>):
        /// its dials, the grate's glowing zones, and its rows.
        /// </summary>
        internal void ShowSample()
        {
            var card = new FireOverlay.Card { Loco = "S282" };
            card.Gauges[0] = new FireOverlay.Gauge { Key = "fire", Label = "FIRE", Value = 0.74f, Band0 = 0.4f, Band1 = 0.9f, Text = "bright 74%", Level = AdviceLevel.Ok };
            card.Gauges[1] = new FireOverlay.Gauge { Key = "draft", Label = "DRAFT", Value = 0.5f, Band0 = 0.36f, Band1 = 0.64f, Text = "right 125%", Level = AdviceLevel.Ok,
                Smoke = new Color(0.78f, 0.78f, 0.76f) };
            card.Gauges[2] = new FireOverlay.Gauge { Key = "glass", Label = "GLASS", Value = 0.62f, Band0 = 0.4f, Band1 = 0.75f, Text = "62%", Level = AdviceLevel.Ok };
            card.Gauges[3] = new FireOverlay.Gauge { Key = "boiler", Label = "BOILER", Value = 0.88f, Band0 = 0.85f / 1.1f, Band1 = 1f / 1.1f, Text = "13.4 bar", Level = AdviceLevel.Ok };
            card.ZoneCoal = new[] { 21f, 24f, 22f, 26f, 28f, 25f, 30f, 32f, 29f };
            card.ZoneGlow = new[] { 0.62f, 0.7f, 0.66f, 0.78f, 0.86f, 0.8f, 0.9f, 0.95f, 0.88f };
            card.ZoneClinker = new float[9];
            card.Rows.Add(new FireOverlay.Row { Label = "Door", Value = "0%" });
            card.Rows.Add(new FireOverlay.Row { Label = "Ashpan", Value = "12%", Meter = 0.12f, Level = AdviceLevel.Ok });
            card.Rows.Add(new FireOverlay.Row { Label = "Clinker", Value = "0.4 kg", Level = AdviceLevel.Ok });
            card.Rows.Add(new FireOverlay.Row { Label = "In the box", Value = "237 kg" });
            card.Rows.Add(new FireOverlay.Row { Label = "Firebox", Value = "1180 °C" });
            card.Advice = new Advice(AdviceLevel.Ok, "A good fire: fire little and often, to the back corners.");
            Show(card, 0, null, null, false);
        }

        private void Show(FireOverlay.Card card, int page, FiremanRig fireman, Checklist checklist, bool mounted)
        {
            var hasData = card != null;
            var orders = page == FiremanPage;
            var checkView = page == ChecklistPage;
            onChecklistPage = checkView;

            waiting.gameObject.SetActive(!hasData);
            waiting.text = mounted
                ? "No supported steam engine\nThis car has no firing readings."
                : "No supported steam engine\nCarry the tablet aboard a locomotive to read its fire.";
            loco.text = hasData ? card.Loco : title;
            for (var i = 0; i < Pages; i++)
            {
                tabs[i].sprite = PanelSprite(i == page ? OverlayArt.PlateOn : OverlayArt.PlateOff);
                tabNames[i].color = i == page ? OverlayArt.Cream : OverlayArt.Dim;
                tabNames[i].text = i == 0 ? (hasData && card.Oil ? "BURNER" : "FIRE")
                    : i == 1 ? "WATER" : i == 2 ? "ENGINE" : i == 3 ? "FIREMAN" : "CHECK";
            }

            // Fireman orders UI
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
                if (!checkView)
                    buttonOn[Pages + i] = show;
                if (!show) continue;
                var active = i < 4 ? FiremanOrders.ModeOf(fireman) == (FiremanMode)i
                    : FiremanOrders.CallActive(fireman, i == 4 ? FiremanCall.HeavyAhead : FiremanCall.EasingOff);
                orderPlates[i].sprite = PanelSprite(active ? OverlayArt.PlateOn : OverlayArt.PlateOff);
                orderNames[i].color = active ? OverlayArt.Cream : OverlayArt.Dim;
                orderNames[i].text = OrderNames[i] + (active && i >= 4 ? $" · {fireman.Fireman.CallLeft:F0} s" : "");
            }

            // Checklist UI header & buttons
            var showChecklist = checkView && hasData && checklist != null;
            checklistHeading.gameObject.SetActive(showChecklist);
            if (showChecklist)
                checklistHeading.text = $"{checklist.Name} · {checklist.CompletedCount}/{checklist.Steps.Count}";
            var showCabButton = ShowSpotlightButton;
            for (var i = 0; i < ChecklistActions; i++)
            {
                var showAction = showChecklist && (i != 3 || showCabButton);
                checklistPlates[i].gameObject.SetActive(showAction);
                checklistPlateNames[i].gameObject.SetActive(showAction);
                if (checkView)
                    buttonOn[Pages + i] = showAction;
                if (!showAction) continue;
                var highlighted = (i == 1 && checklist.AutoSelected) || (i == 3 && CabControlSpotlight.Active);
                checklistPlates[i].sprite = PanelSprite(highlighted ? OverlayArt.PlateOn : OverlayArt.PlateOff);
                checklistPlateNames[i].color = highlighted ? OverlayArt.Cream : OverlayArt.Dim;
            }
            if (checkView)
            {
                for (var i = ChecklistActions; i < Orders; i++)
                    buttonOn[Pages + i] = false;
            }

            // Dials
            var showDials = hasData && !orders && !checkView;
            for (var i = 0; i < 4; i++)
            {
                dials[i].gameObject.SetActive(showDials);
                gaugeNames[i].gameObject.SetActive(showDials);
                gaugeValues[i].gameObject.SetActive(showDials);
                gaugeMarks[i].gameObject.SetActive(showDials);
                smoke[i].gameObject.SetActive(showDials && card.Gauges[i].Smoke.HasValue);
                if (!showDials)
                    continue;
                var gauge = card.Gauges[i];
                dials[i].texture = OverlayArt.Dial(dialKey + i, gauge.Value, gauge.Band0, gauge.Band1, gauge.Level);
                gaugeNames[i].text = gauge.Label;
                gaugeValues[i].text = gauge.Text;
                gaugeMarks[i].texture = OverlayArt.Mark(gauge.Level);
                gaugeMarks[i].color = OverlayArt.LevelColor(gauge.Level);
                smoke[i].color = gauge.Smoke ?? Color.clear;
            }

            // Grate map
            var grate = hasData && !checkView && card.ZoneCoal != null;
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

            // Rows
            var rows = hasData && !checkView ? card.Rows.Count : 0;
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

            // Steps: either full live checklist on CHECK page, or oil lighting steps on unlit BURNER page
            if (showChecklist)
            {
                var y = 86f;
                var count = Math.Min(MaxSteps, checklist.Steps.Count);
                var rowStep = count > 6 ? 33f : 38f;
                for (var i = 0; i < MaxSteps; i++)
                {
                    var exists = i < count;
                    stepTitles[i].gameObject.SetActive(exists);
                    stepHow[i].gameObject.SetActive(exists && checklist.Steps[i].Current);
                    stepChecks[i].gameObject.SetActive(exists && checklist.Steps[i].Done);
                    stepNumbers[i].gameObject.SetActive(exists && !checklist.Steps[i].Done);
                    if (!exists)
                        continue;
                    var step = checklist.Steps[i];
                    Place(stepTitles[i].rectTransform, new Rect(48, y, 476, 26));
                    Place(stepChecks[i].rectTransform, new Rect(20, y + 6, 14, 14));
                    Place(stepNumbers[i].rectTransform, new Rect(16, y, 22, 26));
                    stepNumbers[i].text = (i + 1).ToString();
                    stepNumbers[i].color = step.Current ? OverlayArt.Caution : OverlayArt.Soft;
                    stepTitles[i].text = step.Title;
                    stepTitles[i].color = step.Current ? OverlayArt.Caution : step.Done ? OverlayArt.Dim : OverlayArt.Soft;
                    y += rowStep;
                    if (step.Current)
                    {
                        Place(stepHow[i].rectTransform, new Rect(48, y - rowStep + 24f, 476, 24));
                        stepHow[i].text = step.How;
                        y += 20f;
                    }
                }
            }
            else
            {
                var y = 182f;
                for (var i = 0; i < MaxSteps; i++)
                {
                    var exists = hasData && !checkView && card.Steps != null && i < card.Steps.Count;
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
            text.resizeTextMinSize = Mathf.RoundToInt(11 * scale);
            text.resizeTextMaxSize = shown;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }
    }
}
