using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RealisticFiring.Sim;
using RealisticStackEffects;
using UnityEngine;
using Object = UnityEngine.Object;
using UnityModManagerNet;

namespace RealisticFiring
{
    public static class Main
    {
        public static UnityModManager.ModEntry Mod { get; private set; }

        public static Settings Settings { get; internal set; }

        private static Session session;

        /// <summary>The settings that need a restart, as they stood at load; outside a game load (the checks), the current ones.</summary>
        internal static Session Session => session ?? Session.From(Settings);

        /// <summary>
        /// Whether supported locos get the realistic firebox this session.
        /// Read once at load because sim graphs are built as cars spawn.
        /// </summary>
        public static bool ReplacementEnabled { get; private set; }

        /// <summary>
        /// Whether realistic fireboxes also drive their chimney smoke this
        /// session. Read once at load because smoke is set up as cars spawn.
        /// </summary>
        public static bool RealisticSmokeEnabled { get; private set; }

        /// <summary>
        /// Whether steam locos get the realistic injector this session. Read
        /// once at load because injectors are set up as cars spawn.
        /// </summary>
        public static bool RealisticInjectorsEnabled { get; private set; }

        /// <summary>The mod's patch classes that failed to apply this session, by name.</summary>
        internal static IReadOnlyList<string> FailedPatches { get; private set; } = new string[0];

        /// <summary>
        /// Applies each patch class on its own, as Harmony's PatchAll does,
        /// but goes on past one that fails (a game update, or another mod in
        /// the way), naming it to <paramref name="error"/>. PatchAll stops at
        /// the first that throws, leaving every class after it unpatched: the
        /// mod's firebox in place and the shovel's fixes for it not, which
        /// threw on every hover (a player's report with Remote Dispatch,
        /// 2026-10-17). Returns the classes that failed.
        /// </summary>
        internal static List<string> PatchEachClass(Harmony harmony, IEnumerable<Type> types, Action<string> error,
            ICollection<Type> unbound = null)
        {
            var failed = new List<string>();
            foreach (var type in types)
            {
                // A patch class whose game fields are gone would throw on
                // every call of what it patches: it stays off.
                if (unbound != null && GameMembers.Within(type, unbound))
                {
                    if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0)
                    {
                        failed.Add(type.FullName);
                        error?.Invoke($"Patch {type.FullName} left off: the game no longer has a field it reads");
                    }
                    continue;
                }
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                }
                catch (Exception e)
                {
                    failed.Add(type.FullName);
                    error?.Invoke($"Patch {type.FullName} failed; the rest still load: {e.GetBaseException().GetType().Name}: {e.GetBaseException().Message}");
                }
            }
            return failed;
        }

        private static void Step(UnityModManager.ModEntry modEntry, string what, Action step)
        {
            try
            {
                step();
            }
            catch (Exception e)
            {
                modEntry.Logger.LogException($"Starting the {what} failed; the mod goes on without it", e);
            }
        }

        // Entry method named in Info.json. Patches stay applied until the game
        // restarts, so the mod has no OnToggle handler.
        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            Mod = modEntry;
            // Before anything is patched: with a field its simulation reads
            // gone from the game, the mod stays off whole and the game runs
            // stock, where it would have thrown in every engine's tick.
            var types = AccessTools.GetTypesFromAssembly(typeof(Main).Assembly);
            var unbound = GameMembers.Unbound(types);
            foreach (var pair in unbound)
                modEntry.Logger.Error($"{pair.Key.FullName} cannot read the game: {pair.Value}");
            var essential = unbound.Keys.Where(GameMembers.Essential).Select(t => t.Name).ToList();
            if (essential.Count > 0)
            {
                modEntry.Logger.Error($"Realistic Firing is off this session and the game runs stock: the game has changed under its simulation ({string.Join(", ", essential)}). " +
                                      "A game update is the likely cause; please check for a newer Realistic Firing.");
                return false;
            }
            Settings = UnityModManager.ModSettings.Load<Settings>(modEntry);
            var changed = Settings.MoveOldKeys();
            changed |= Settings.ApplyNewDefaults();
            if (changed)
                Settings.Save(modEntry);
            Core.FireTuning.Default.ClinkerTendency = Settings.ClinkerTendency;
            Visuals.FiringStackHost.Install();
            // The test bed's tuning only on a development setup (its key bound).
            if (Settings.PlumeTestBedEnabled)
                PlumeTestBed.Restore(Settings.PlumeTestBed);
            session = Session.From(Settings);
            ReplacementEnabled = session.RealisticFireboxes;
            RealisticSmokeEnabled = session.RealisticSmoke;
            RealisticInjectorsEnabled = session.RealisticInjectors;
            var settingsMenu = new SettingsMenu();
            modEntry.OnGUI = entry => settingsMenu.Draw(Settings, StackEffectsLink.HandedOff);
            modEntry.OnSaveGUI = entry => Settings.Save(entry);
            // Unity Mod Manager opens a mod's settings on its hotkey; sharing
            // the settings' binding keeps a rebind in effect at once.
            modEntry.Hotkey = Settings.OpenSettingsKey;

            var harmony = new Harmony(modEntry.Info.Id);
            FailedPatches = PatchEachClass(harmony, types, error => modEntry.Logger.Error(error), unbound.Keys);
            if (FailedPatches.Count > 0)
                modEntry.Logger.Error($"{FailedPatches.Count} of Realistic Firing's patches failed and the rest loaded: {string.Join(", ", FailedPatches)}. " +
                                      "Another mod, or a game update, may be in the way; please send this log with a bug report.");
            Patches.TickGuard.Install(harmony, types, error => modEntry.Logger.Error(error));

            // The patches are in by now and stay in: each step after them on
            // its own, so one that fails leaves the mod with its controller,
            // keys, and notices, not marked failed with its patches live.
            // The controller first: it shows the notices.
            Step(modEntry, "controller", () =>
            {
                var host = new GameObject("RealisticFiring");
                Object.DontDestroyOnLoad(host);
                host.AddComponent<FireController>();
                host.AddComponent<PlumeCapture>();
                host.AddComponent<Visuals.FireboxCapture>();
            });
            Step(modEntry, "multiplayer add-on", () => Net.TryStartAddOn(modEntry));
            Step(modEntry, "Stack Effects link", () => StackEffectsLink.TryStart(modEntry));
            Step(modEntry, "stereo keyword", StereoKeyword.Install);
            Step(modEntry, "note of other firemen", Sim.FiremanRig.NoteRivals);
            modEntry.Logger.Log(ReplacementEnabled
                ? $"Realistic fireboxes enabled for {string.Join(", ", FireboxRegistry.SupportedLiveries)}"
                : "Realistic fireboxes disabled; every loco keeps the stock firebox");
            return true;
        }
    }
}
