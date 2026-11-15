using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DV.ServicePenalty.UI;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace RealisticFiring.Upgrades
{
    /// <summary>
    /// The Upgrades entry in every career manager kiosk's main menu, the way
    /// Railroad Contracts adds its own: the stock menu writes its four
    /// entries (fees, licenses, owned vehicles, stats) down one column, and
    /// the display has free lines below them. Upgrades takes the next free
    /// line below the menu's last entry, so with Contracts installed it goes
    /// below Contracts (this patch runs after Contracts', which wants a stock
    /// menu of four). A kiosk without such a line keeps its menu.
    ///
    /// With Career Manager API active (wwwDayDream, Nexus 951), that mod
    /// rebuilds every kiosk's main menu from its own list and takes the
    /// menu's input, so Upgrades goes on its list instead.
    /// </summary>
    internal static class UpgradesMenu
    {
        public const string Entry = "Upgrades";

        /// <summary>Railroad Contracts' mod and Harmony ID.</summary>
        public const string ContractsId = "Contracts";

        internal sealed class Extension
        {
            /// <summary>The menu line this mod added; null when Career Manager API holds the menu.</summary>
            public TextMeshPro Line;

            /// <summary>The line's place in the menu's entries.</summary>
            public int Index;

            public UpgradesScreen Screen;
        }

        private static readonly ConditionalWeakTable<CareerManagerMainScreen, Extension> extensions =
            new ConditionalWeakTable<CareerManagerMainScreen, Extension>();

        public static bool TryGet(CareerManagerMainScreen main, out Extension extension) =>
            extensions.TryGetValue(main, out extension);

        internal static void Extend(CareerManagerMainScreen main)
        {
            var kiosk = main.screenSwitcher;
            var t = Traverse.Create(main);
            var selectable = t.Field("selectableText").GetValue<TextMeshPro[]>();
            if (kiosk == null || selectable == null || selectable.Length < 4 || main.fees == null || main.stats == null)
            {
                Warn($"kiosk {Where(kiosk)}: main menu not as expected; Upgrades left out");
                return;
            }
            var line = LineBelow(kiosk, main.fees, main.stats, selectable);
            if (line == null)
            {
                Warn($"kiosk {Where(kiosk)}: no free line below the menu's {selectable.Length} entries; Upgrades left out");
                return;
            }
            var lines = KioskLines.From(kiosk);
            if (lines == null)
            {
                Warn($"kiosk {Where(kiosk)}: screens not as expected; Upgrades left out");
                return;
            }
            var index = selectable.Length;
            t.Field("selectableText").SetValue(selectable.Concat(new[] { line }).ToArray());
            t.Field("selector").SetValue(new IntIterator(0, index + 1, isWrappable: true));
            t.Field("activeSlotCount").SetValue(index + 1);
            var holder = new GameObject("RealisticFiring " + Entry);
            holder.transform.SetParent(main.transform.parent, false);
            Attach(main, line, index, holder.AddComponent<UpgradesScreen>(), lines);
        }

        /// <summary>Upgrades on a kiosk whose main menu Career Manager API holds: an entry on its list, which opens the screen the API made for it.</summary>
        internal static void ExtendThrough(CareerManagerMainScreen main, Func<UpgradesScreen> addEntry)
        {
            if (main == null || extensions.TryGetValue(main, out _))
                return;
            var lines = KioskLines.From(main.screenSwitcher);
            if (lines == null)
            {
                Warn($"kiosk {Where(main.screenSwitcher)}: screens not as expected; Upgrades left out");
                return;
            }
            var screen = addEntry();
            if (screen == null)
            {
                Warn($"kiosk {Where(main.screenSwitcher)}: Career Manager API did not take the Upgrades entry; Upgrades left out");
                return;
            }
            Attach(main, null, -1, screen, lines);
        }

        private static void Attach(CareerManagerMainScreen main, TextMeshPro line, int index, UpgradesScreen screen, KioskLines lines)
        {
            screen.Init(main.screenSwitcher, main, lines);
            extensions.Add(main, new Extension { Line = line, Index = index, Screen = screen });
        }

        /// <summary>
        /// The next line down the main menu's column below its last entry: the
        /// stock entries are evenly spaced, so it lies one step below.
        /// </summary>
        private static TextMeshPro LineBelow(DisplayScreenSwitcher kiosk, TextMeshPro first, TextMeshPro fourth, TextMeshPro[] used)
        {
            Vector3 At(Component c) => kiosk.transform.InverseTransformPoint(c.transform.position);
            var step = (At(fourth) - At(first)) / 3f;
            if (step.magnitude < 1e-4f)
                return null;
            var last = used[used.Length - 1];
            var target = At(last) + step;
            return kiosk.allTextFields
                .Where(f => f != null && !used.Contains(f) && f.transform.parent == fourth.transform.parent)
                .Select(f => (Field: f, Off: (At(f) - target).magnitude))
                .Where(c => c.Off < step.magnitude * 0.25f)
                .OrderBy(c => c.Off)
                .Select(c => c.Field)
                .FirstOrDefault();
        }

        private static readonly MethodInfo SetInfo = AccessTools.Method(typeof(CareerManagerInfoScreen), "SetInfoData",
            new[] { typeof(IDisplayScreen), typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(Action) });

        /// <summary>A title and a paragraph on the kiosk's info screen, going back to <paramref name="back"/> after.</summary>
        internal static void ShowInfo(CareerManagerInfoScreen screen, IDisplayScreen back, string title, string paragraph) =>
            SetInfo?.Invoke(screen, new object[] { back, title, paragraph, "", "", "", "", null });

        internal static void Warn(string text) => Main.Mod?.Logger.Warning(text);

        internal static string Where(Component c)
        {
            if (c == null)
                return "?";
            var t = c.transform;
            while (t.parent != null && t.parent.name != "Offices_w3" && t.parent.name != "Offices")
                t = t.parent;
            return t.name;
        }
    }

    [HarmonyPatch(typeof(CareerManagerMainScreen), "Awake")]
    [HarmonyAfter(UpgradesMenu.ContractsId)]
    internal static class UpgradesMainScreenAwake
    {
        private static void Postfix(CareerManagerMainScreen __instance)
        {
            try
            {
                if (!UpgradesCareerManagerApi.Hook())
                    UpgradesMenu.Extend(__instance);
            }
            catch (Exception e)
            {
                Main.Mod?.Logger.Error($"kiosk {UpgradesMenu.Where(__instance)}: adding Upgrades failed: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(CareerManagerMainScreen), nameof(CareerManagerMainScreen.Activate))]
    [HarmonyAfter(UpgradesMenu.ContractsId)]
    internal static class UpgradesMainScreenActivate
    {
        private static void Postfix(CareerManagerMainScreen __instance)
        {
            if (!UpgradesMenu.TryGet(__instance, out var x) || x.Line == null)
                return;
            x.Line.text = UpgradesMenu.Entry;
            x.Line.color = __instance.CurrentSelection == x.Index ? __instance.screenSwitcher.HIGHLIGHTED_COLOR : __instance.screenSwitcher.REGULAR_COLOR;
        }
    }

    [HarmonyPatch(typeof(CareerManagerMainScreen), nameof(CareerManagerMainScreen.Disable))]
    internal static class UpgradesMainScreenDisable
    {
        private static void Postfix(CareerManagerMainScreen __instance)
        {
            if (UpgradesMenu.TryGet(__instance, out var x) && x.Line != null)
                x.Line.text = string.Empty;
        }
    }

    [HarmonyPatch(typeof(CareerManagerMainScreen), nameof(CareerManagerMainScreen.HandleInputAction))]
    internal static class UpgradesMainScreenInput
    {
        private static bool Prefix(CareerManagerMainScreen __instance, InputAction input)
        {
            if (input != InputAction.Confirm || !UpgradesMenu.TryGet(__instance, out var x) || x.Line == null || __instance.CurrentSelection != x.Index)
                return true;
            __instance.screenSwitcher.SetActiveDisplay(x.Screen);
            return false;
        }
    }

    [HarmonyPatch(typeof(CareerManagerMainScreen), nameof(CareerManagerMainScreen.GetCurrentSelection))]
    internal static class UpgradesMainScreenSelection
    {
        private static bool Prefix(CareerManagerMainScreen __instance, ref string __result)
        {
            if (!UpgradesMenu.TryGet(__instance, out var x) || x.Line == null || __instance.CurrentSelection != x.Index)
                return true;
            __result = UpgradesMenu.Entry;
            return false;
        }
    }

    /// <summary>
    /// Career Manager API, reached by reflection when it is active, as
    /// Railroad Contracts reaches it: Upgrades is one of its entries, added
    /// when it starts a kiosk.
    /// </summary>
    internal static class UpgradesCareerManagerApi
    {
        private const string ModId = "CareerManagerAPI";
        private static bool looked, hooked;
        private static Type canConfirmType;
        private static MethodInfo tryAdd;
        private static EventInfo awake;

        public static bool Active
        {
            get
            {
                if (!looked)
                {
                    looked = true;
                    var api = AccessTools.TypeByName("CareerManagerAPI.CareerManagerAPI");
                    var trackerType = AccessTools.TypeByName("CareerManagerAPI.CareerManagerScreenTracker");
                    canConfirmType = api?.GetNestedType("OnCanConfirm");
                    awake = api?.GetEvent("CareerManagerAwake");
                    tryAdd = trackerType?.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "TryAddToMainScreen" && m.IsGenericMethodDefinition && m.GetParameters().Length == 4);
                    if (api != null && (trackerType == null || canConfirmType == null || awake == null || tryAdd == null))
                        UpgradesMenu.Warn("Career Manager API: its tracker, event or TryAddToMainScreen not found; Upgrades adds its own menu line");
                }
                var mod = UnityModManagerNet.UnityModManager.FindMod(ModId);
                return mod != null && mod.Active && awake != null && tryAdd != null && canConfirmType != null;
            }
        }

        /// <summary>With Career Manager API active, listens for its kiosks (once) and returns true. Otherwise false.</summary>
        public static bool Hook()
        {
            if (!Active)
                return false;
            if (hooked)
                return true;
            try
            {
                var handler = Delegate.CreateDelegate(awake.EventHandlerType,
                    typeof(UpgradesCareerManagerApi).GetMethod(nameof(OnKiosk), BindingFlags.NonPublic | BindingFlags.Static));
                awake.AddEventHandler(null, handler);
                hooked = true;
                Main.Mod?.Logger.Log("Career Manager API: Upgrades goes on its kiosk menus");
                return true;
            }
            catch (Exception e)
            {
                Main.Mod?.Logger.Error($"Career Manager API: listening for its kiosks failed; Upgrades adds its own menu line: {e.Message}");
                awake = null;
                return false;
            }
        }

        private static void OnKiosk(MonoBehaviour tracker, string location, StationController station, TrainCar trainCar)
        {
            try
            {
                var main = tracker == null ? null : tracker.GetComponent<CareerManagerMainScreen>();
                UpgradesMenu.ExtendThrough(main, () => AddEntry(tracker, main));
            }
            catch (Exception e)
            {
                Main.Mod?.Logger.Error($"kiosk {location}: adding Upgrades through Career Manager API failed: {e}");
            }
        }

        private static UpgradesScreen AddEntry(MonoBehaviour tracker, CareerManagerMainScreen main)
        {
            var parent = main.feesScreen.transform.parent;
            var before = new System.Collections.Generic.HashSet<UpgradesScreen>(parent.GetComponentsInChildren<UpgradesScreen>(true));
            var canConfirm = Delegate.CreateDelegate(canConfirmType,
                typeof(UpgradesCareerManagerApi).GetMethod(nameof(CanOpen), BindingFlags.NonPublic | BindingFlags.Static));
            var added = (bool)tryAdd.MakeGenericMethod(typeof(UpgradesScreen))
                .Invoke(tracker, new object[] { UpgradesMenu.Entry, canConfirm, null, null });
            return added ? parent.GetComponentsInChildren<UpgradesScreen>(true).FirstOrDefault(s => !before.Contains(s)) : null;
        }

        private static bool CanOpen(out string why)
        {
            why = string.Empty;
            return true;
        }
    }
}
