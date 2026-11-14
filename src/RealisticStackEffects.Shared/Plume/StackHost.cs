using System;
using RealisticStackEffects.Core;
using UnityEngine;
using UnityModManagerNet;

namespace RealisticStackEffects
{
    /// <summary>
    /// Subsections within the plume's main-thread update. Motion includes the
    /// rest from Shadows on: Carry is the particles' own working out (with
    /// Shadows), Buffers their copying from and to the engine, CustomData the
    /// values written for the shader beside them, Vents the engine's other
    /// steam (SteamVents).
    /// </summary>
    internal enum PlumePhase { OffscreenSetup, Weather, Emission, Motion, Shadows, Buffers, VolumeSubmit, Carry, CustomData, Vents }

    /// <summary>The settings the chimney's effects follow: the hosting mod's.</summary>
    internal interface IStackSettings
    {
        /// <summary>The smoke's resolution and particles; a plume capture sets it for each of its variants.</summary>
        SmokeQuality SmokeQuality { get; set; }

        /// <summary>The season the steam's air is taken from, without DV Seasons.</summary>
        SmokeSeason SmokeSeason { get; }

        bool HeatShimmer { get; }

        /// <summary>Whether the engine's other steam (dynamo, cylinder cocks, safety valves, leaks) is drawn as the plume, as the session started.</summary>
        bool EngineSteam { get; }

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

        // The player's smoke quality, or on Auto the one chosen for the
        // frame rate (AutoSmokeQuality); High through a capture, whose
        // frames are as slow as its files are to save.
        private static readonly AutoSmokeQuality Auto = new AutoSmokeQuality();
        private static int governed = -1;
        private static float smokeSeen = float.NegativeInfinity;

        /// <summary>The smoke quality the plume is drawn at now: never Auto.</summary>
        public static SmokeQuality Quality =>
            Settings == null ? SmokeQuality.High
            : Settings.SmokeQuality != SmokeQuality.Auto ? Settings.SmokeQuality
            : PlumeCapture.Running ? SmokeQuality.High : Auto.Quality;

        /// <summary>Notes that a plume has particles in the air this frame, for the automatic quality.</summary>
        public static void SmokeInTheAir() => smokeSeen = Time.unscaledTime;

        /// <summary>The automatic quality's frame: once a frame, from the first plume updated in it.</summary>
        public static void Govern()
        {
            if (governed == Time.frameCount)
                return;
            governed = Time.frameCount;
            if (Settings == null || Settings.SmokeQuality != SmokeQuality.Auto)
            {
                if (Auto.Why != null)
                    Auto.Reset();
                return;
            }
            if (PlumeCapture.Running)
                return;
            // The rate the game is held to: none in VR, where the headset paces it.
            var camera = PlayerManager.ActiveCamera;
            var heldTo = camera != null && camera.stereoEnabled ? 0f
                : QualitySettings.vSyncCount > 0 ? Screen.currentResolution.refreshRate / (float)QualitySettings.vSyncCount
                : Application.targetFrameRate > 0 ? Application.targetFrameRate : 0f;
            var was = Auto.Quality;
            if (Auto.Step(Time.unscaledDeltaTime, Time.unscaledTime - smokeSeen < 2f, heldTo))
                Mod?.Logger.Log($"Smoke quality (automatic): {was} to {Auto.Quality}, {Auto.Why}");
        }

        /// <summary>Counts a plume's frame, from its <c>Stopwatch.GetTimestamp()</c> at the start, into the host's meter; null for none.</summary>
        public static Action<long> PlumeTimed;

        /// <summary>Camera sorting and command-buffer preparation, separately from the plume update.</summary>
        public static Action<long> CameraTimed;

        /// <summary>Optional nested timings; separate from the overall plume total.</summary>
        public static Action<PlumePhase, long> PhaseTimed;

        public static PhaseTiming TimePhase(PlumePhase phase) => new PhaseTiming(phase);

        internal readonly struct PhaseTiming : IDisposable
        {
            private readonly PlumePhase phase;
            private readonly long started;
            internal PhaseTiming(PlumePhase phase)
            {
                this.phase = phase;
                started = PhaseTimed != null && Settings != null && Settings.StatusLogInterval > 0
                    ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            }
            public void Dispose()
            {
                if (started != 0L) PhaseTimed?.Invoke(phase, started);
            }
        }

        /// <summary>The scale of the developer tools' windows.</summary>
        public static Func<float> GuiScale = () => 1f;
    }
}
