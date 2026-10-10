using HarmonyLib;
using RealisticFiring.Sim;
using UnityEngine;
using UnityModManagerNet;

namespace RealisticFiring
{
    public static class Main
    {
        public static UnityModManager.ModEntry Mod { get; private set; }

        public static Settings Settings { get; internal set; }

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

        // Entry method named in Info.json. Patches stay applied until the game
        // restarts, so the mod has no OnToggle handler.
        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            Mod = modEntry;
            Settings = UnityModManager.ModSettings.Load<Settings>(modEntry);
            Core.FireTuning.Default.ClinkerTendency = Settings.ClinkerTendency;
            ReplacementEnabled = Settings.ReplaceStockFireboxes;
            RealisticSmokeEnabled = ReplacementEnabled && Settings.RealisticSmoke;
            RealisticInjectorsEnabled = Settings.RealisticInjectors;
            modEntry.OnGUI = entry => Settings.Draw(entry);
            modEntry.OnSaveGUI = entry => Settings.Save(entry);
            // Unity Mod Manager opens a mod's settings on its hotkey; sharing
            // the settings' binding keeps a rebind in effect at once.
            modEntry.Hotkey = Settings.OpenSettingsKey;

            new Harmony(modEntry.Info.Id).PatchAll(typeof(Main).Assembly);
            Net.TryStartAddOn(modEntry);

            var host = new GameObject("RealisticFiring");
            Object.DontDestroyOnLoad(host);
            host.AddComponent<FireController>();

            modEntry.Logger.Log(ReplacementEnabled
                ? $"Realistic fireboxes enabled for {string.Join(", ", FireboxRegistry.SupportedLiveries)}"
                : "Realistic fireboxes disabled; every loco keeps the stock firebox");
            return true;
        }
    }
}
