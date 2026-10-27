using System;
using System.Collections.Generic;
using HarmonyLib;
using RealisticStackEffects.Core;
using UnityEngine;
using UnityModManagerNet;
using Object = UnityEngine.Object;

namespace RealisticStackEffects
{
    /// <summary>
    /// Realistic Stack Effects: the chimney's smoke and steam on every steam
    /// locomotive, read from the game's own firebox and exhaust, or from
    /// another mod's fire where one feeds it.
    /// </summary>
    public static class Main
    {
        internal static UnityModManager.ModEntry Mod { get; private set; }
        internal static Settings Settings { get; private set; }

        // Read once at load: the plumes are set up as cars spawn.
        private static bool customEffects;

        // Entry method named in Info.json. Patches stay applied until the game
        // restarts, so the mod has no OnToggle handler.
        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            Mod = modEntry;
            Settings = UnityModManager.ModSettings.Load<Settings>(modEntry);
            customEffects = Settings.CustomSmokeEffects;
            StackHost.Mod = modEntry;
            StackHost.Settings = new HostSettings();
            StackHost.Assets = new HostAssets();
            // The test bed's tuning only on a development setup (its key bound).
            if (Settings.PlumeTestBedEnabled)
                PlumeTestBed.Restore(Settings.PlumeTestBed);
            modEntry.OnGUI = entry => Settings.Draw(entry);
            modEntry.OnSaveGUI = entry => Settings.Save(entry);

            var failed = PatchEachClass(new Harmony(modEntry.Info.Id), AccessTools.GetTypesFromAssembly(typeof(Main).Assembly),
                error => modEntry.Logger.Error(error));
            if (failed.Count > 0)
                modEntry.Logger.Error($"{failed.Count} of Realistic Stack Effects' patches failed and the rest loaded: {string.Join(", ", failed)}. " +
                                      "Another mod, or a game update, may be in the way; please send this log with a bug report.");
            StereoKeyword.Install();

            var host = new GameObject("RealisticStackEffects");
            Object.DontDestroyOnLoad(host);
            host.AddComponent<StackController>();
            host.AddComponent<PlumeCapture>();
            modEntry.Logger.Log("Realistic chimney smoke on every steam locomotive" + (customEffects ? "" : ", on the game's own smoke systems"));
            return true;
        }

        // Each patch class on its own, so one that fails (a game update, or
        // another mod in the way) is named and the rest still apply.
        private static List<string> PatchEachClass(Harmony harmony, IEnumerable<Type> types, Action<string> error)
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

        private sealed class HostSettings : IStackSettings
        {
            public SmokeQuality SmokeQuality
            {
                get => Settings.SmokeQuality;
                set => Settings.SmokeQuality = value;
            }

            public SmokeSeason SmokeSeason => Settings.SmokeSeason;
            public bool HeatShimmer => Settings.HeatShimmer;
            public bool CustomEffects => customEffects;
            public int StatusLogInterval => Settings.StatusLogInterval;
            public bool DeveloperTools => Settings.PlumeTestBedEnabled;
            public bool HalfResolutionAllowed => !SmokeReaderInitPatch.OlderFiringFound;

            public string TestBed
            {
                get => Settings.PlumeTestBed;
                set => Settings.PlumeTestBed = value;
            }

            public void Save() => Settings.Save(Mod);
        }

        private sealed class HostAssets : IStackAssets
        {
            public GameObject PlumePrefab => StackAssets.Prefab;
            public Material CompositeMaterial => StackAssets.CompositeMaterial;
            public Material DepthMaterial => StackAssets.DepthMaterial;
        }
    }
}
