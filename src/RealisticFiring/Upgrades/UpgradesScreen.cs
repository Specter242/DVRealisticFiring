using System;
using System.Collections.Generic;
using System.Linq;
using DV.InventorySystem;
using DV.ServicePenalty.UI;
using DV.Utils;
using RealisticFiring.Core;

namespace RealisticFiring.Upgrades
{
 internal sealed class UpgradesScreen : ScrollableDisplayScreen
    {
        /// <summary>A fitting the screen sells.</summary>
        internal sealed class Upgrade
        {
            public UpgradeId Id;
            public UpgradeMask Flag;
            public string Name;
            public double Price;
            public string Page;
            public string PerLocoPage;
            public Func<bool> SessionBuilt = () => true;

            /// <summary>Why it can't be bought yet though it isn't bought, or null (fleet-wide).</summary>
            public Func<string> Waiting = () => null;

            /// <summary>Why it can't be bought yet for <c>car</c> in per-loco mode, or null.</summary>
            public Func<TrainCar, string> WaitingFor;

            public string BoughtLine;

            /// <summary>Confirmation sentence for a single locomotive in per-loco mode.</summary>
            public Func<string, string> BoughtLineForLoco;

            /// <summary>The engines it goes on, for its standing.</summary>
            public string Engines = "every S282 and S060";

            /// <summary>Why it isn't fitted though its rule fits it, or null (the jets without the arch).</summary>
            public Func<string> Missing = () => null;

            public UpgradeRule Rule() => Main.Settings != null ? Fittings.Spec(Id).Rule(Main.Settings) : UpgradeRule.BuyInCareer;
            public bool Bought() => Fittings.IsBought(Id);
            public bool Fitted() => Fittings.IsFitted(Id) && SessionBuilt();
            public void Buy() => Fittings.Buy(Id);

            /// <summary>Whether it can be bought here and now (fleet-wide).</summary>
            public bool CanBuy => CanBuyFor(null);

            public bool IsBoughtFor(TrainCar car) =>
                Fittings.IsPerLocomotive ? Fittings.IsBought(Flag, car) : Bought();

            public bool IsFittedFor(TrainCar car) =>
                Fittings.IsPerLocomotive ? Fittings.Has(Flag, car) && SessionBuilt() : Fitted();

            public string WaitingReason(TrainCar car) =>
                Fittings.IsPerLocomotive && WaitingFor != null ? WaitingFor(car) : Waiting();

            public bool CanBuyFor(TrainCar car)
            {
                if (Net.IsClient || !Fittings.InCareer || Rule() != UpgradeRule.BuyInCareer)
                    return false;
                if (Fittings.IsPerLocomotive && Fittings.CarGuidOf(car) == null)
                    return false;
                return !IsBoughtFor(car) && WaitingReason(car) == null;
            }

            public void BuyFor(TrainCar car)
            {
                if (Fittings.IsPerLocomotive)
                    Fittings.Buy(Flag, car);
                else
                    Buy();
            }

            public string Value => ValueFor(null);

            public string ValueFor(TrainCar car) =>
                Net.IsClient ? (IsFittedFor(car) ? "fitted" : "not fitted")
                : CanBuyFor(car) ? $"${Price:N0}"
                : Fittings.InCareer && Rule() == UpgradeRule.BuyInCareer && !IsBoughtFor(car) ? "later"
                : IsFittedFor(car) ? "fitted" : "not fitted";

            public string Standing => StandingFor(null, null);

            public string StandingFor(TrainCar car, string targetId)
            {
                var perLoco = Fittings.IsPerLocomotive && !string.IsNullOrEmpty(targetId);
                var fitted = IsFittedFor(car);
                if (Net.IsClient)
                {
                    if (perLoco)
                        return fitted ? $"The host's {targetId} has it." : $"The host's {targetId} does not have it; the host buys it.";
                    return fitted ? "The host's engines have it." : "The host's engines do not have it; the host buys it.";
                }
                if (WaitingReason(car) is string waiting)
                    return waiting;
                var missing = fitted ? null : Missing();
                switch (Rule())
                {
                    case UpgradeRule.AlwaysFitted: return missing == null ? "Fitted: the mod's menu sets it always fitted." : $"Not fitted: {missing}.";
                    case UpgradeRule.NeverFitted: return "Not fitted: the mod's menu sets it never fitted.";
                    default:
                        return !Fittings.InCareer ? (missing == null ? "Fitted: in sandbox there is nothing to buy." : $"Not fitted: {missing}.")
                            : IsBoughtFor(car) ? (missing == null ? $"Bought: {(perLoco ? targetId : Engines)} has it." : $"Bought, but not fitted: {missing}.")
                            : $"${Price:N0}.";
                }
            }

            public string PageFor(bool perLoco) => perLoco && !string.IsNullOrEmpty(PerLocoPage) ? PerLocoPage : Page;

            public string ConfirmationFor(string targetId) =>
                !string.IsNullOrEmpty(targetId) && BoughtLineForLoco != null ? BoughtLineForLoco(targetId) : BoughtLine;
        }

 public const double ArchPrice = 20_000;

 public const double JetsPrice = 15_000;

        internal static readonly Upgrade Arch = new Upgrade
        {
            Id = UpgradeId.BrickArch,
            Flag = UpgradeMask.BrickArch,
            Name = "Brick arch",
            Price = ArchPrice,
            Page = "A brick arch over the front of the grate makes the gas from the coal travel further and burn before the tubes. " +
                   "Without it the S282 and S060 burn about 7 % more coal for the same work, up to 13 % more worked hard, " +
                   "steam about 13 % less at their most, and smoke more after firing. " +
                   "Bought once, it is fitted to every S282 and S060.",
            PerLocoPage = "A brick arch over the front of the grate makes the gas from the coal travel further and burn before the tubes. " +
                          "Without it the engine burns about 7 % more coal for the same work, up to 13 % more worked hard, " +
                          "steams about 13 % less at its most, and smokes more after firing. " +
                          "Bought for this locomotive, it stays fitted to this locomotive.",
            BoughtLine = "Bought. Every S282 and S060 has its brick arch now.",
            BoughtLineForLoco = id => $"Bought. {id} has its brick arch now.",
        };

        internal static readonly Upgrade Jets = new Upgrade
        {
            Id = UpgradeId.OverFireJets,
            Flag = UpgradeMask.OverFireJets,
            Name = "Over-fire jets",
            Price = JetsPrice,
            Page = "Steam jets through the firebox's sides blow air across the box over the fire, worked by the JETS valve in the cab. " +
                   "Opened after a round, standing, or drifting, they burn the smoke; worked hard they do less. " +
                   "They take the boiler's steam and the grate's draft: keep the blower on when standing with them open, " +
                   "or the box blows back. Needs the brick arch. Bought once, they are fitted to every S282 and S060.",
            PerLocoPage = "Steam jets through the firebox's sides blow air across the box over the fire, worked by the JETS valve in the cab. " +
                          "Opened after a round, standing, or drifting, they burn the smoke; worked hard they do less. " +
                          "They take the boiler's steam and the grate's draft: keep the blower on when standing with them open, " +
                          "or the box blows back. Needs the brick arch on this locomotive.",
            Waiting = () => Fittings.Arch ? null : "Needs the brick arch first.",
            WaitingFor = car => Fittings.HasArch(car) ? null : "Needs the brick arch on this locomotive first.",
            Missing = () => "it needs the brick arch",
            BoughtLine = "Bought. Every S282 and S060 has its over-fire jets now: open the JETS valve in the cab.",
            BoughtLineForLoco = id => $"Bought. {id} has its over-fire jets now: open the JETS valve in its cab.",
        };

 public const double AutoFireDoorPrice = 10_000;

        internal static readonly Upgrade AutoFireDoor = new Upgrade
        {
            Id = UpgradeId.AutomaticFireDoor,
            Flag = UpgradeMask.AutomaticFireDoor,
            Name = "Automatic fire door",
            Price = AutoFireDoorPrice,
            Page = "An air-worked fire door, opened from the floor instead of by hand. With the shovel in hand at the door, hold jump (Space): " +
                   "air swings it open, and it stays open while you hold the key and shuts as soon as you let go. " +
                   "A door cracked for air goes back to its crack, and less cold air reaches the fire between shovelfuls. " +
                   "At the door the key no longer jumps while you hold the shovel. Bought once, it is fitted to every S282 and S060.",
            PerLocoPage = "An air-worked fire door, opened from the floor instead of by hand. With the shovel in hand at the door, hold jump (Space): " +
                          "air swings it open, and it stays open while you hold the key and shuts as soon as you let go. " +
                          "A door cracked for air goes back to its crack, and less cold air reaches the fire between shovelfuls. " +
                          "Bought for this locomotive, it stays fitted to this locomotive.",
            BoughtLine = "Bought. Every S282 and S060 has its automatic fire door now: with the shovel in hand, hold jump at the door.",
            BoughtLineForLoco = id => $"Bought. {id} has its automatic fire door now: with the shovel in hand, hold jump at the door.",
        };

 public const double SniftingPrice = 8_000;

        internal static readonly Upgrade Snifting = new Upgrade
        {
            Id = UpgradeId.SniftingValves,
            Flag = UpgradeMask.SniftingValves,
            Name = "Snifting valves",
            Price = SniftingPrice,
            Page = "Vacuum relief valves on the steam chests. Drifting with the throttle shut, the pistons pull the chests below the air outside; " +
                   "the valves lift and let air in, so the cylinders draw much less smokebox gas and grit through the blast pipe. " +
                   "Drifting hooked up then wears the engine about a third as much; full gear or a cracked throttle still spares it entirely. " +
                   "They work by themselves: you hear them lift as you shut off at speed, breathe with the beats, and seat when steam goes back on. " +
                   "Bought once, they are fitted to every S282 and S060.",
            PerLocoPage = "Vacuum relief valves on the steam chests. Drifting with the throttle shut, the valves lift and let air in, " +
                          "so the cylinders draw much less smokebox gas and grit through the blast pipe and drifting hooked up wears the engine about a third as much. " +
                          "Bought for this locomotive, they stay fitted to this locomotive.",
            BoughtLine = "Bought. Every S282 and S060 has its snifting valves now: they lift by themselves when you drift.",
            BoughtLineForLoco = id => $"Bought. {id} has its snifting valves now: they lift by themselves when you drift.",
        };

 public const double DriftingValvePrice = 6_000;

        internal static readonly Upgrade DriftingValve = new Upgrade
        {
            Id = UpgradeId.DriftingValve,
            Flag = UpgradeMask.DriftingValve,
            Name = "Drifting valve",
            Price = DriftingValvePrice,
            Page = "A DRIFTING valve in the cab feeds a little saturated steam from the turret to the steam chests. " +
                   "Open it as you shut off to drift, and shut it before opening the throttle or stopping: with the reverser hooked up, " +
                   "its steam fills much of what the cylinders draw, sparing most of the wear (all of it slower, or with by-pass valves); " +
                   "in full gear it can't keep up. With by-pass valves it also ends the cylinders' braking at any speed. " +
                   "It takes a little steam while the engine drifts. Bought once, it is fitted to every S282 and S060.",
            PerLocoPage = "A DRIFTING valve in the cab feeds a little saturated steam from the turret to the steam chests when drifting with the throttle shut, " +
                          "sparing most cylinder wear hooked up and ending braking with by-pass valves. " +
                          "Bought for this locomotive, it stays fitted to this locomotive.",
            BoughtLine = "Bought. Every S282 and S060 has its drifting valve now: open the DRIFTING valve in the cab when you drift.",
            BoughtLineForLoco = id => $"Bought. {id} has its drifting valve now: open the DRIFTING valve in its cab when you drift.",
        };

 public const double ByPassPrice = 12_000;

        internal static readonly Upgrade ByPass = new Upgrade
        {
            Id = UpgradeId.ByPassValves,
            Flag = UpgradeMask.ByPassValves,
            Name = "By-pass valves",
            Price = ByPassPrice,
            Page = "By-pass valves on the cylinders open by themselves once the steam in the chests is gone, joining each cylinder's ends, " +
                   "so the pistons hardly pump: the engine drifts freely in any notch until it runs fast (half the braking at a big engine's 40 mph), " +
                   "and with the drifting valve open at any speed; it draws much less smokebox gas, wearing a third as much hooked up. " +
                   "Opening the throttle closes them. " +
                   "Bought once, they are fitted to every S282 and S060.",
            PerLocoPage = "By-pass valves on the cylinders open by themselves when drifting with the throttle shut, joining each cylinder's ends " +
                          "so the engine drifts freely in any notch and draws much less smokebox gas. " +
                          "Bought for this locomotive, they stay fitted to this locomotive.",
            BoughtLine = "Bought. Every S282 and S060 has its by-pass valves now: they open by themselves when you drift.",
            BoughtLineForLoco = id => $"Bought. {id} has its by-pass valves now: they open by themselves when you drift.",
        };

 public const double StokerPrice = 40_000;

        internal static readonly Upgrade Stoker = new Upgrade
        {
            Id = UpgradeId.S282Stoker,
            Flag = UpgradeMask.S282Stoker,
            Name = "S282 stoker",
            Price = StokerPrice,
            Page = "A mechanical stoker feeds the fire from the tender: a screw brings the coal forward and steam jets spread it over the grate. " +
                   "Worked from the cab: the STOKER valve runs its engine, the jet valves place the coal, and the floor lever reverses it to clear a jam. " +
                   "The fireman then shovels only where it starves the fire. Bought once, it is fitted to every S282.",
            PerLocoPage = "A mechanical stoker feeds the fire from the tender: a screw brings the coal forward and steam jets spread it over the grate. " +
                          "Worked from the cab by the STOKER valve, jet valves, and floor lever. " +
                          "Bought for this locomotive, it stays fitted to this locomotive.",
            SessionBuilt = () => Main.Session.RetrofitS282Stoker,
            Waiting = () => Main.Session.RetrofitS282Stoker ? null : "Restart the game first: the mod's menu had it never fitted when the game started.",
            Missing = () => Main.Session.RetrofitS282Stoker ? null : "the game needs a restart after the mod's menu changed",
            BoughtLine = "Bought. Every S282 has its stoker now: open the STOKER valve in the cab.",
            BoughtLineForLoco = id => $"Bought. {id} has its stoker now: open the STOKER valve in its cab.",
            Engines = "every S282",
        };

 public const double FeedPumpPrice = 25_000;

        internal static readonly Upgrade FeedPump = new Upgrade
        {
            Id = UpgradeId.S282FeedPump,
            Flag = UpgradeMask.S282FeedPump,
            Name = "S282 feed pump",
            Price = FeedPumpPrice,
            Page = "A Worthington feedwater heater and pump: the pump draws water from the tender, the exhaust steam heats it, and it goes into the boiler hot, " +
                   "so the boiler makes more steam for the coal. Worked from the cab by the FEED PUMP valve, its gauge beside it; the injector stays as the second feed. " +
                   "Bought once, it is fitted to every S282.",
            PerLocoPage = "A Worthington feedwater heater and pump: exhaust steam heats water from the tender so the boiler makes more steam for the coal. " +
                          "Worked from the cab by the FEED PUMP valve, its gauge beside it. " +
                          "Bought for this locomotive, it stays fitted to this locomotive.",
            SessionBuilt = () => Main.Session.S282FeedPump,
            Waiting = () => Main.Session.S282FeedPump ? null
                : Main.Settings != null && !Main.Settings.RealisticInjectors ? "Needs the mod's realistic injectors (the mod's menu, then a restart)."
                : "Restart the game first: the mod's menu had it never fitted when the game started.",
            Missing = () => Main.Session.S282FeedPump ? null : "the game needs a restart after the mod's menu changed",
            BoughtLine = "Bought. Every S282 has its feed pump now: open the FEED PUMP valve in the cab.",
            BoughtLineForLoco = id => $"Bought. {id} has its feed pump now: open the FEED PUMP valve in its cab.",
            Engines = "every S282",
        };

 public const double BoosterPrice = 30_000;

        internal static readonly Upgrade Booster = new Upgrade
        {
            Id = UpgradeId.S282Booster,
            Flag = UpgradeMask.S282Booster,
            Name = "S282 booster",
            Price = BoosterPrice,
            Page = "A Franklin booster: a small two-cylinder steam engine geared to the trailing truck's axle, for starting heavy trains and climbing slowly. " +
                   "Raise its latch with the reverser forward in full gear and under 12 mph; it cuts out by itself by 21 mph. Its idling valve warms it, its gauge beside them, by the reverser. " +
                   "It takes a lot of steam. Bought once, it is fitted to every S282.",
            PerLocoPage = "A Franklin booster geared to the trailing truck's axle for starting heavy trains and climbing slowly. " +
                          "Worked from the cab by its latch, idling valve, and gauge by the reverser. " +
                          "Bought for this locomotive, it stays fitted to this locomotive.",
            SessionBuilt = () => Main.Session.S282Booster,
            Waiting = () => Main.Session.S282Booster ? null : "Restart the game first: the mod's menu had it never fitted when the game started.",
            Missing = () => Main.Session.S282Booster ? null : "the game needs a restart after the mod's menu changed",
            BoughtLine = "Bought. Every S282 has its booster now: its latch, idling valve, and gauge are by the reverser.",
            BoughtLineForLoco = id => $"Bought. {id} has its booster now: its latch, idling valve, and gauge are by the reverser.",
            Engines = "every S282",
        };

        internal static readonly Upgrade[] All = { Arch, Jets, AutoFireDoor, Snifting, DriftingValve, ByPass, Stoker, FeedPump, Booster };

        private DisplayScreenSwitcher kiosk;
        private CareerManagerMainScreen main;
        private UpgradePayingScreen paying;
        private CareerManagerInfoScreen info;
        private KioskLines lines;

        /// <summary>The rows of the fleet-wide list, or of the picked engine's page.</summary>
        private readonly List<Upgrade> entries = new List<Upgrade>();
        private readonly OwnedLocomotivePicker picker = new OwnedLocomotivePicker();
        private readonly Dictionary<string, TrainCar> ownedCars = new Dictionary<string, TrainCar>(StringComparer.Ordinal);
        private bool screenActive;
        private bool? builtPerLoco;

        /// <summary>
        /// Whether the screen lists owned engines first: per-locomotive upgrades in a career, or on a guest
        /// of a host who sells them so. In sandbox every engine has them (nothing is bought), so it stays as
        /// fleet-wide.
        /// </summary>
        private static bool PerLocoNow => Fittings.IsPerLocomotive && (Fittings.InCareer || Net.IsClient);

        private bool ViewPerLoco => builtPerLoco == true;
        private bool OnList => ViewPerLoco && picker.OnList;
        internal OwnedLocomotivePicker Picker => picker;

        private TrainCar PickedCar =>
            picker.PickedGuid != null && ownedCars.TryGetValue(picker.PickedGuid, out var car) && car != null ? car : null;

        protected override int TotalSlotCount
        {
            get
            {
                EnsureView();
                return OnList ? picker.Engines.Count : entries.Count;
            }
        }

        private void Update() => EnsureView();

        /// <summary>
        /// Rebuilds the view if the upgrade scope changed since it was built, or the picked engine is gone
        /// (destroyed), so it never shows rows for another mode or a car that isn't there.
        /// </summary>
        private bool EnsureView()
        {
            if (!screenActive)
                return false;
            if (builtPerLoco == PerLocoNow && (!ViewPerLoco || picker.OnList || PickedCar != null))
                return false;
            RebuildView(resetScroll: true);
            return true;
        }

        internal void Init(DisplayScreenSwitcher kiosk, CareerManagerMainScreen main, KioskLines lines)
        {
            this.kiosk = kiosk;
            this.main = main;
            this.lines = lines;
            info = kiosk.GetComponentInChildren<CareerManagerInfoScreen>(true);
            paying = gameObject.AddComponent<UpgradePayingScreen>();
            paying.Init(kiosk, this, info);
            if (!paying.Ready)
                UpgradesMenu.Warn($"kiosk {UpgradesMenu.Where(kiosk)}: no license payment screen to pay at; upgrades can't be bought there");
            scrollUpArrow = lines.ArrowUp;
            scrollDownArrow = lines.ArrowDown;
            activeSlotCount = lines.Rows.Count;
            selector = new IntIterator(0, 0, isWrappable: true);
        }

        public override void Activate(IDisplayScreen previousScreen)
        {
            screenActive = true;
            var freshEntry = PrepareForActivation(previousScreen);
            RebuildView(resetScroll: freshEntry);
        }

        /// <summary>
        /// True when opening the Upgrades list fresh (from the main menu or another top-level screen)
        /// rather than returning from its own payment or info sub-screens.
        /// </summary>
        internal bool IsFreshEntry(IDisplayScreen previousScreen) =>
            previousScreen == null
            || (!ReferenceEquals(previousScreen, paying) && !ReferenceEquals(previousScreen, info));

        /// <summary>
        /// A fresh visit starts on the list of owned engines; back from paying or an upgrade's page, the
        /// picked engine's page stays. Returns whether the visit is fresh.
        /// </summary>
        internal bool PrepareForActivation(IDisplayScreen previousScreen)
        {
            var freshEntry = IsFreshEntry(previousScreen);
            if (freshEntry)
                picker.Back();
            return freshEntry;
        }

        /// <summary>The list page's engines: <paramref name="cars"/> by car ID, keeping the picked one while it is owned.</summary>
        internal void ApplyOwned(IEnumerable<TrainCar> cars)
        {
            ownedCars.Clear();
            var owned = new List<OwnedLocomotive>();
            foreach (var car in cars)
            {
                var guid = Fittings.CarGuidOf(car);
                if (guid == null || car.carLivery == null)
                    continue;
                owned.Add(new OwnedLocomotive(guid, car.logicCar.ID, car.carLivery.id));
                ownedCars[guid] = car;
            }
            picker.Refresh(owned);
        }

        private void RebuildView(bool resetScroll, int highlight = -1)
        {
            builtPerLoco = PerLocoNow;
            entries.Clear();
            if (ViewPerLoco)
            {
                ApplyOwned(OwnedEngines.Cars());
                if (picker.Picked is OwnedLocomotive picked && PickedCar != null)
                    entries.AddRange(All.Where(u => UpgradeKeys.Allows(picked.Livery, u.Flag)));
                else
                    picker.Back();
            }
            else
            {
                picker.Back();
                entries.AddRange(All);
            }

            SetSelectorWithinBounds();
            if (resetScroll)
            {
                selector.Reset();
                IndexOfFirstDisplayedEntry = 0;
            }
            else
            {
                SetIndexOfFirstDisplayWithinBounds();
            }
            // A new page: no row keeps the last page's highlight (the stock scroll only clears the row it moves from).
            for (var row = 0; row < lines.Rows.Count; row++)
                lines.Color(row, kiosk.REGULAR_COLOR);
            if (highlight >= 0 && highlight < TotalSlotCount)
            {
                // Back on the list, the engine just left is highlighted, scrolled into view.
                var first = Math.Min(highlight, HighestFirstDisplayEntryIndex);
                Scroll(first, highlight - first);
            }
            else
            {
                Scroll(IndexOfFirstDisplayedEntry, selector.Current);
            }
            UpdateHeader();
        }

        private void UpdateHeader()
        {
            Paragraph(string.Empty);
            if (!ViewPerLoco)
            {
                lines.Title.text = UpgradesMenu.Entry;
                lines.Info.text = Net.IsClient ? "The host's engines"
                    : Fittings.InCareer ? $"You have ${Money:N0}"
                    : "Sandbox: set in the mod's menu";
                return;
            }
            if (picker.Picked is OwnedLocomotive picked)
            {
                lines.Title.text = $"Upgrades: {picked.Id}";
                lines.Info.text = Net.IsClient ? $"The host's {picked.Id}" : $"You have ${Money:N0}";
                return;
            }
            lines.Title.text = UpgradesMenu.Entry;
            if (picker.Engines.Count > 0)
            {
                lines.Info.text = Net.IsClient ? "The host's locomotives" : "Pick a locomotive";
                return;
            }
            var none = Net.IsClient ? "The host owns no S282 or S060" : "You own no S282 or S060";
            if (lines.Paragraph != null)
            {
                Paragraph(none + ".");
                lines.Info.text = "Confirm for more";
            }
            else
            {
                lines.Info.text = none;
            }
        }

        private void Paragraph(string text)
        {
            if (lines.Paragraph != null)
                lines.Paragraph.text = text;
        }

        public override void Disable()
        {
            screenActive = false;
            lines.Title.text = lines.Info.text = string.Empty;
            Paragraph(string.Empty);
            lines.ClearRows();
            base.Disable();
        }

        public override void PopulateTextsFromIndex(int startingIndex)
        {
            if (EnsureView())
                return;
            base.PopulateTextsFromIndex(startingIndex);
            var total = TotalSlotCount;
            var shown = Math.Min(total - startingIndex, activeSlotCount);
            for (var i = 0; i < shown; i++)
            {
                var slot = startingIndex + i;
                var (name, value) = RowText(slot);
                lines.Rows[i].Name.text = name;
                lines.Rows[i].Value.text = value;
            }
            lines.ClearRows(Math.Max(shown, 0));
        }

        private (string Name, string Value) RowText(int slot)
        {
            if (OnList)
            {
                var engine = picker.Engines[slot];
                ownedCars.TryGetValue(engine.Guid, out var car);
                return (engine.Id, Has(engine, car));
            }
            var upgrade = entries[slot];
            return ViewPerLoco ? (upgrade.Name, upgrade.ValueFor(PickedCar)) : (upgrade.Name, upgrade.Value);
        }

        /// <summary>How many of its upgrades an engine on the list has, as its page would show them fitted.</summary>
        private static string Has(OwnedLocomotive engine, TrainCar car)
        {
            var mine = All.Where(u => UpgradeKeys.Allows(engine.Livery, u.Flag)).ToList();
            var fitted = car == null ? 0 : mine.Count(u => u.IsFittedFor(car));
            return $"{fitted}/{mine.Count} fitted";
        }

        public override void HighlightSelected(int newHighlight, int prevHighlighted = -1)
        {
            if (prevHighlighted != -1 && prevHighlighted != newHighlight)
                lines.Color(prevHighlighted, kiosk.REGULAR_COLOR);
            if (newHighlight != -1)
                lines.Color(newHighlight, kiosk.HIGHLIGHTED_COLOR);
        }

        public override void HandleInputAction(InputAction input)
        {
            switch (input)
            {
                case InputAction.Up when selector.HasElements:
                    if (EnsureView())
                        break;
                    ScrollUp();
                    break;
                case InputAction.Down when selector.HasElements:
                    if (EnsureView())
                        break;
                    ScrollDown();
                    break;
                case InputAction.Cancel:
                    if (ViewPerLoco && !picker.OnList && builtPerLoco == PerLocoNow)
                        RebuildView(resetScroll: true, highlight: picker.Back());
                    else
                        kiosk.SetActiveDisplay(main);
                    break;
                case InputAction.Confirm:
                    if (EnsureView())
                        break;
                    if (selector.HasElements)
                        HandleConfirm(IndexOfFirstDisplayedEntry + selector.Current);
                    else if (OnList)
                        ShowNoneOwned();
                    break;
            }
        }

        private void HandleConfirm(int slot)
        {
            if (OnList)
            {
                if (picker.Pick(slot))
                    RebuildView(resetScroll: true);
                return;
            }
            if (slot < 0 || slot >= entries.Count)
                return;
            var upgrade = entries[slot];
            if (ViewPerLoco)
            {
                var car = PickedCar;
                var id = picker.Picked?.Id;
                if (car == null || id == null)
                    return;
                if (!upgrade.CanBuyFor(car) || !paying.Ready)
                {
                    Show(upgrade.Name, upgrade.PageFor(perLoco: true) + "\n\n" + upgrade.StandingFor(car, id));
                    return;
                }
                paying.Sell(upgrade, UpgradeScope.PerLocomotive, car, id);
                kiosk.SetActiveDisplay(paying);
                return;
            }
            if (!upgrade.CanBuy || !paying.Ready)
            {
                Show(upgrade.Name, upgrade.Page + "\n\n" + upgrade.Standing);
                return;
            }
            paying.Sell(upgrade, UpgradeScope.FleetWide);
            kiosk.SetActiveDisplay(paying);
        }

        private void ShowNoneOwned() =>
            Show("No locomotive",
                (Net.IsClient
                    ? "Upgrades here go on each S282 and S060 the host owns, as the host's Owned Vehicles lists them. The host owns none yet."
                    : "Upgrades here go on each S282 and S060 you own, as Owned Vehicles lists them: an engine you have restored. You own none yet.") +
                "\n\nRestore one, or set Career upgrade scope to Fleet-wide in the mod's settings to fit every engine.");

        private void Show(string title, string text)
        {
            if (info == null)
            {
                lines.Info.text = text.Length > 37 ? text.Substring(0, 37) : text;
                return;
            }
            UpgradesMenu.ShowInfo(info, this, title, text);
            kiosk.SetActiveDisplay(info);
        }

        private static double Money => SingletonBehaviour<Inventory>.Instance?.PlayerMoney ?? 0.0;
    }
}
