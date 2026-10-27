using System;
using RealisticStackEffects.Core;
using UnityEngine;
using UnityModManagerNet;

namespace RealisticStackEffects
{
    /// <summary>The settings the chimney's effects follow: the hosting mod's.</summary>
    internal interface IStackSettings
    {
        /// <summary>The smoke's resolution and particles; a plume capture sets it for each of its variants.</summary>
        SmokeQuality SmokeQuality { get; set; }

        /// <summary>The season the steam's air is taken from, without DV Seasons.</summary>
        SmokeSeason SmokeSeason { get; }

        bool HeatShimmer { get; }

        /// <summary>Whether the custom effects draw the smoke, as the session started (a change needs a restart).</summary>
        bool CustomEffects { get; }

        /// <summary>Seconds between the plume's status lines in the log on the player's engine; 0 for none.</summary>
        int StatusLogInterval { get; }

        /// <summary>Whether the developer's plume tools (the test bed, captures) are on: their key bound.</summary>
        bool DeveloperTools { get; }

        /// <summary>The test bed's tuning and look, kept with the settings.</summary>
        string TestBed { get; set; }

        /// <summary>
        /// Whether the smoke may be drawn at half resolution: not beside
        /// another copy of the plume drawing its own pass with the same
        /// globals (an older Realistic Firing beside Realistic Stack Effects).
        /// </summary>
        bool HalfResolutionAllowed { get; }

        void Save();
    }

    /// <summary>The plume's assets from the hosting mod's effects bundle; each null without it.</summary>
    internal interface IStackAssets
    {
        /// <summary>The custom effects' prefab (<see cref="ChimneyPlume"/>).</summary>
        GameObject PlumePrefab { get; }

        /// <summary>The materials that draw the smoke at half resolution (<see cref="PlumeOffscreen"/>).</summary>
        Material CompositeMaterial { get; }
        Material DepthMaterial { get; }
    }

 internal static class StackHost
    {
        public static UnityModManager.ModEntry Mod;
        public static IStackSettings Settings;
        public static IStackAssets Assets;

        /// <summary>Counts a plume's frame, from its <c>Stopwatch.GetTimestamp()</c> at the start, into the host's meter; null for none.</summary>
        public static Action<long> PlumeTimed;

        /// <summary>The scale of the developer tools' windows.</summary>
        public static Func<float> GuiScale = () => 1f;
    }
}
