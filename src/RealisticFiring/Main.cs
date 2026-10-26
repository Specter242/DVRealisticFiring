using System;
using System.Collections.Generic;
using HarmonyLib;
using RealisticFiring.Sim;
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
        internal static List<string> PatchEachClass(Harmony harmony, IEnumerable<Type> types, Action<string> error)
        {
            var failed = new List<string>();
            foreach (var type in types)
            {
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

        // Entry method named in Info.json. Patches stay applied until the game
        // restarts, so the mod has no OnToggle handler.
        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            Mod = modEntry;
            Settings = UnityModManager.ModSettings.Load<Settings>(modEntry);
            var changed = Settings.MoveOldKeys();
            changed |= Settings.ApplyNewDefaults();
            if (changed)
                Settings.Save(modEntry);
            Core.FireTuning.Default.ClinkerTendency = Settings.ClinkerTendency;
            // The test bed's tuning only on a development setup (its key bound).
            if (Settings.PlumeTestBedEnabled)
                Visuals.PlumeTestBed.Restore(Settings.PlumeTestBed);
            session = Session.From(Settings);
            ReplacementEnabled = session.RealisticFireboxes;
            RealisticSmokeEnabled = session.RealisticSmoke;
            RealisticInjectorsEnabled = session.RealisticInjectors;
            modEntry.OnGUI = entry => Settings.Draw(entry);
            modEntry.OnSaveGUI = entry => Settings.Save(entry);
            // Unity Mod Manager opens a mod's settings on its hotkey; sharing
            // the settings' binding keeps a rebind in effect at once.
            modEntry.Hotkey = Settings.OpenSettingsKey;

            FailedPatches = PatchEachClass(new Harmony(modEntry.Info.Id), AccessTools.GetTypesFromAssembly(typeof(Main).Assembly),
                error => modEntry.Logger.Error(error));
            if (FailedPatches.Count > 0)
                modEntry.Logger.Error($"{FailedPatches.Count} of Realistic Firing's patches failed and the rest loaded: {string.Join(", ", FailedPatches)}. " +
                                      "Another mod, or a game update, may be in the way; please send this log with a bug report.");
            Net.TryStartAddOn(modEntry);
            Visuals.StereoKeyword.Install();

            var host = new GameObject("RealisticFiring");
            Object.DontDestroyOnLoad(host);
            host.AddComponent<FireController>();
            host.AddComponent<Visuals.PlumeCapture>();
            host.AddComponent<Visuals.FireboxCapture>();

            Sim.FiremanRig.NoteRivals();
            modEntry.Logger.Log(ReplacementEnabled
                ? $"Realistic fireboxes enabled for {string.Join(", ", FireboxRegistry.SupportedLiveries)}"
                : "Realistic fireboxes disabled; every loco keeps the stock firebox");
            return true;
        }
    }
}
