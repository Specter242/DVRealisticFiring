using System;
using System.Collections.Generic;
using DV.InventorySystem;
using DV.ServicePenalty.UI;
using DV.Utils;

namespace RealisticFiring.Upgrades
{
 internal sealed class UpgradesScreen : ScrollableDisplayScreen
    {
        /// <summary>A fitting the screen sells.</summary>
        internal sealed class Upgrade
        {
            public string Name;
            public double Price;
            public string Page;
            public Func<UpgradeRule> Rule;
            public Func<bool> Bought;
            public Func<bool> Fitted;

            /// <summary>Why it can't be bought yet though it isn't bought, or null.</summary>
            public Func<string> Waiting = () => null;

            public Action Buy;
            public string BoughtLine;

            /// <summary>The engines it goes on, for its standing.</summary>
            public string Engines = "every S282 and S060";

            /// <summary>Why it isn't fitted though its rule fits it, or null (the jets without the arch).</summary>
            public Func<string> Missing = () => null;

            /// <summary>Whether it can be bought here and now: a career, the rule to buy it, not yet bought, nothing it waits on, and not a client in another's game.</summary>
            public bool CanBuy => !Net.IsClient && Fittings.InCareer && Rule() == UpgradeRule.BuyInCareer && !Bought() && Waiting() == null;

            public string Value =>
                Net.IsClient ? (Fitted() ? "fitted" : "not fitted")
                : CanBuy ? $"${Price:N0}"
                : Fittings.InCareer && Rule() == UpgradeRule.BuyInCareer && !Bought() ? "later"
                : Fitted() ? "fitted" : "not fitted";

            public string Standing
            {
                get
                {
                    if (Net.IsClient)
                        return Fitted() ? "The host's engines have it." : "The host's engines do not have it; the host buys it.";
                    if (Waiting() is string waiting)
                        return waiting;
                    var missing = Fitted() ? null : Missing();
                    switch (Rule())
                    {
                        case UpgradeRule.AlwaysFitted: return missing == null ? "Fitted: the mod's menu sets it always fitted." : $"Not fitted: {missing}.";
                        case UpgradeRule.NeverFitted: return "Not fitted: the mod's menu sets it never fitted.";
                        default:
                            return !Fittings.InCareer ? (missing == null ? "Fitted: in sandbox there is nothing to buy." : $"Not fitted: {missing}.")
                                : Bought() ? (missing == null ? $"Bought: {Engines} has it." : $"Bought, but not fitted: {missing}.")
                                : $"${Price:N0}.";
                    }
                }
            }
        }

 public const double ArchPrice = 20_000;

 public const double JetsPrice = 15_000;

        internal static readonly Upgrade Arch = new Upgrade
        {
            Name = "Brick arch",
            Price = ArchPrice,
            Page = "A brick arch over the front of the grate makes the gas from the coal travel further and burn before the tubes. " +
                   "Without it the S282 and S060 burn about 7 % more coal for the same work, up to 13 % more worked hard, " +
                   "steam about 13 % less at their most, and smoke more after firing. " +
                   "Bought once, it is fitted to every S282 and S060.",
            Rule = () => Main.Settings?.BrickArch ?? UpgradeRule.BuyInCareer,
            Bought = () => Fittings.ArchBought,
            Fitted = () => Fittings.Arch,
            Buy = Fittings.BuyArch,
            BoughtLine = "Bought. Every S282 and S060 has its brick arch now.",
        };

        internal static readonly Upgrade Jets = new Upgrade
        {
            Name = "Over-fire jets",
            Price = JetsPrice,
            Page = "Steam jets through the firebox's sides blow air across the box over the fire, worked by the JETS valve in the cab. " +
                   "Opened after a round, standing, or drifting, they burn the smoke; worked hard they do less. " +
                   "They take the boiler's steam and the grate's draft: keep the blower on when standing with them open, " +
                   "or the box blows back. Needs the brick arch. Bought once, they are fitted to every S282 and S060.",
            Rule = () => Main.Settings?.OverFireJets ?? UpgradeRule.BuyInCareer,
            Bought = () => Fittings.JetsBought,
            Fitted = () => Fittings.Jets,
            Waiting = () => Fittings.Arch ? null : "Needs the brick arch first.",
            Missing = () => "it needs the brick arch",
            Buy = Fittings.BuyJets,
            BoughtLine = "Bought. Every S282 and S060 has its over-fire jets now: open the JETS valve in the cab.",
        };

 public const double SniftingPrice = 8_000;

        internal static readonly Upgrade Snifting = new Upgrade
        {
            Name = "Snifting valves",
            Price = SniftingPrice,
            Page = "Vacuum relief valves on the steam chests. Drifting with the throttle shut, the pistons pull the chests below the air outside; " +
                   "the valves lift and let air in, so the cylinders draw much less smokebox gas and grit through the blast pipe. " +
                   "Drifting hooked up then wears the engine about a third as much; full gear or a cracked throttle still spares it entirely. " +
                   "They work by themselves: you hear them lift as you shut off at speed, breathe with the beats, and seat when steam goes back on. " +
                   "Bought once, they are fitted to every S282 and S060.",
            Rule = () => Main.Settings?.SniftingValves ?? UpgradeRule.BuyInCareer,
            Bought = () => Fittings.SniftingBought,
            Fitted = () => Fittings.Snifting,
            Buy = Fittings.BuySnifting,
            BoughtLine = "Bought. Every S282 and S060 has its snifting valves now: they lift by themselves when you drift.",
        };

 public const double DriftingValvePrice = 6_000;

        internal static readonly Upgrade DriftingValve = new Upgrade
        {
            Name = "Drifting valve",
            Price = DriftingValvePrice,
            Page = "A DRIFTING valve in the cab feeds a little saturated steam from the turret to the steam chests. " +
                   "Open it as you shut off to drift, and shut it before opening the throttle or stopping: with the reverser hooked up, " +
                   "its steam fills much of what the cylinders draw, sparing most of the wear (all of it slower, or with by-pass valves); " +
                   "in full gear it can't keep up. With by-pass valves it also ends the cylinders' braking at any speed. " +
                   "It takes a little steam while the engine drifts. Bought once, it is fitted to every S282 and S060.",
            Rule = () => Main.Settings?.DriftingValve ?? UpgradeRule.BuyInCareer,
            Bought = () => Fittings.DriftingValveBought,
            Fitted = () => Fittings.DriftingValve,
            Buy = Fittings.BuyDriftingValve,
            BoughtLine = "Bought. Every S282 and S060 has its drifting valve now: open the DRIFTING valve in the cab when you drift.",
        };

 public const double ByPassPrice = 12_000;

        internal static readonly Upgrade ByPass = new Upgrade
        {
            Name = "By-pass valves",
            Price = ByPassPrice,
            Page = "By-pass valves on the cylinders open by themselves once the steam in the chests is gone, joining each cylinder's ends, " +
                   "so the pistons hardly pump: the engine drifts freely in any notch until it runs fast (half the braking at a big engine's 40 mph), " +
                   "and with the drifting valve open at any speed; it draws much less smokebox gas, wearing a third as much hooked up. " +
                   "Opening the throttle closes them. " +
                   "Bought once, they are fitted to every S282 and S060.",
            Rule = () => Main.Settings?.ByPassValves ?? UpgradeRule.BuyInCareer,
            Bought = () => Fittings.ByPassBought,
            Fitted = () => Fittings.ByPass,
            Buy = Fittings.BuyByPass,
            BoughtLine = "Bought. Every S282 and S060 has its by-pass valves now: they open by themselves when you drift.",
        };

 public const double StokerPrice = 40_000;

        internal static readonly Upgrade Stoker = new Upgrade
        {
            Name = "S282 stoker",
            Price = StokerPrice,
            Page = "A mechanical stoker feeds the fire from the tender: a screw brings the coal forward and steam jets spread it over the grate. " +
                   "Worked from the cab: the STOKER valve runs its engine, the jet valves place the coal, and the floor lever reverses it to clear a jam. " +
                   "The fireman then shovels only where it starves the fire. Bought once, it is fitted to every S282.",
            Rule = () => Main.Settings?.S282Stoker ?? UpgradeRule.BuyInCareer,
            Bought = () => Fittings.StokerBought,
            Fitted = () => Fittings.Stoker && Main.Session.RetrofitS282Stoker,
            Waiting = () => Main.Session.RetrofitS282Stoker ? null : "Restart the game first: the mod's menu had it never fitted when the game started.",
            Missing = () => Main.Session.RetrofitS282Stoker ? null : "the game needs a restart after the mod's menu changed",
            Buy = Fittings.BuyStoker,
            BoughtLine = "Bought. Every S282 has its stoker now: open the STOKER valve in the cab.",
            Engines = "every S282",
        };

 public const double FeedPumpPrice = 25_000;

        internal static readonly Upgrade FeedPump = new Upgrade
        {
            Name = "S282 feed pump",
            Price = FeedPumpPrice,
            Page = "A Worthington feedwater heater and pump: the pump draws water from the tender, the exhaust steam heats it, and it goes into the boiler hot, " +
                   "so the boiler makes more steam for the coal. Worked from the cab by the FEED PUMP valve, its gauge beside it; the injector stays as the second feed. " +
                   "Bought once, it is fitted to every S282.",
            Rule = () => Main.Settings?.S282FeedPump ?? UpgradeRule.BuyInCareer,
            Bought = () => Fittings.FeedPumpBought,
            Fitted = () => Fittings.FeedPump && Main.Session.S282FeedPump,
            Waiting = () => Main.Session.S282FeedPump ? null
                : Main.Settings != null && !Main.Settings.RealisticInjectors ? "Needs the mod's realistic injectors (the mod's menu, then a restart)."
                : "Restart the game first: the mod's menu had it never fitted when the game started.",
            Missing = () => Main.Session.S282FeedPump ? null : "the game needs a restart after the mod's menu changed",
            Buy = Fittings.BuyFeedPump,
            BoughtLine = "Bought. Every S282 has its feed pump now: open the FEED PUMP valve in the cab.",
            Engines = "every S282",
        };

 public const double BoosterPrice = 30_000;

        internal static readonly Upgrade Booster = new Upgrade
        {
            Name = "S282 booster",
            Price = BoosterPrice,
            Page = "A Franklin booster: a small two-cylinder steam engine geared to the trailing truck's axle, for starting heavy trains and climbing slowly. " +
                   "Raise its latch with the reverser forward in full gear and under 12 mph; it cuts out by itself by 21 mph. Its idling valve warms it, its gauge beside them, by the reverser. " +
                   "It takes a lot of steam. Bought once, it is fitted to every S282.",
            Rule = () => Main.Settings?.S282Booster ?? UpgradeRule.BuyInCareer,
            Bought = () => Fittings.BoosterBought,
            Fitted = () => Fittings.Booster && Main.Session.S282Booster,
            Waiting = () => Main.Session.S282Booster ? null : "Restart the game first: the mod's menu had it never fitted when the game started.",
            Missing = () => Main.Session.S282Booster ? null : "the game needs a restart after the mod's menu changed",
            Buy = Fittings.BuyBooster,
            BoughtLine = "Bought. Every S282 has its booster now: its latch, idling valve, and gauge are by the reverser.",
            Engines = "every S282",
        };

        private static readonly Upgrade[] All = { Arch, Jets, Snifting, DriftingValve, ByPass, Stoker, FeedPump, Booster };

        private DisplayScreenSwitcher kiosk;
        private CareerManagerMainScreen main;
        private UpgradePayingScreen paying;
        private CareerManagerInfoScreen info;
        private KioskLines lines;
        private readonly List<Upgrade> entries = new List<Upgrade>();

        protected override int TotalSlotCount => entries.Count;

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
            entries.Clear();
            entries.AddRange(All);
            SetSelectorWithinBounds();
            if (ReferenceEquals(previousScreen, main))
            {
                selector.Reset();
                IndexOfFirstDisplayedEntry = 0;
            }
            else
            {
                SetIndexOfFirstDisplayWithinBounds();
            }
            Scroll(IndexOfFirstDisplayedEntry, selector.Current);
            lines.Title.text = UpgradesMenu.Entry;
            lines.Info.text = Net.IsClient ? "The host's engines" : Fittings.InCareer ? $"You have ${Money:N0}" : "Sandbox: set in the mod's menu";
        }

        public override void Disable()
        {
            lines.Title.text = lines.Info.text = string.Empty;
            lines.ClearRows();
            base.Disable();
        }

        public override void PopulateTextsFromIndex(int startingIndex)
        {
            base.PopulateTextsFromIndex(startingIndex);
            var shown = Math.Min(entries.Count - startingIndex, activeSlotCount);
            for (var i = 0; i < shown; i++)
            {
                lines.Rows[i].Name.text = entries[startingIndex + i].Name;
                lines.Rows[i].Value.text = entries[startingIndex + i].Value;
            }
            lines.ClearRows(Math.Max(shown, 0));
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
                    ScrollUp();
                    break;
                case InputAction.Down when selector.HasElements:
                    ScrollDown();
                    break;
                case InputAction.Cancel:
                    kiosk.SetActiveDisplay(main);
                    break;
                case InputAction.Confirm when selector.HasElements:
                    var upgrade = entries[IndexOfFirstDisplayedEntry + selector.Current];
                    if (!upgrade.CanBuy || !paying.Ready)
                    {
                        Show(upgrade.Name, upgrade.Page + "\n\n" + upgrade.Standing);
                        break;
                    }
                    paying.Sell(upgrade);
                    kiosk.SetActiveDisplay(paying);
                    break;
            }
        }

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
