using RealisticFiring.Core;
using RealisticFiring.Diagnostics;
using RealisticStackEffects;
using RealisticStackEffects.Core;
using UnityEngine;

namespace RealisticFiring.Visuals
{
    /// <summary>Realistic Firing as the chimney's effects' host (<see cref="StackHost"/>).</summary>
    internal static class FiringStackHost
    {
        public static void Install()
        {
            StackHost.Mod = Main.Mod;
            StackHost.Settings = new StackSettings();
            StackHost.Assets = new StackAssets();
            StackHost.PlumeTimed = start => PerfMeter.Stop(PerfSection.ChimneyPlume, start);
            StackHost.GuiScale = () => OverlayStyle.Scale;
        }

        private sealed class StackSettings : IStackSettings
        {
            public SmokeQuality SmokeQuality
            {
                get => Main.Settings.SmokeQuality;
                set => Main.Settings.SmokeQuality = value;
            }

            public SmokeSeason SmokeSeason => Main.Settings.SmokeSeason;
            public bool HeatShimmer => Main.Settings.HeatShimmer;
            public bool CustomEffects => Main.Session.CustomSmokeEffects;
            public int StatusLogInterval => Main.Settings.FireStatusLogInterval;
            public bool DeveloperTools => Main.Settings.PlumeTestBedEnabled;
            public bool HalfResolutionAllowed => true;

            public string TestBed
            {
                get => Main.Settings.PlumeTestBed;
                set => Main.Settings.PlumeTestBed = value;
            }

            public void Save() => Main.Settings.Save(Main.Mod);
        }

        private sealed class StackAssets : IStackAssets
        {
            public GameObject PlumePrefab => PlumeAssets.Prefab;
            public Material CompositeMaterial => PlumeAssets.PlumeCompositeMaterial;
            public Material DepthMaterial => PlumeAssets.PlumeDepthMaterial;
        }
    }
}
