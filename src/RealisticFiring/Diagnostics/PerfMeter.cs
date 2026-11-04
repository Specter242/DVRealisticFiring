using System.Diagnostics;
using System.Linq;
using UnityEngine;

namespace RealisticFiring.Diagnostics
{
    /// <summary>Parts of the mod timed by <see cref="PerfMeter"/>.</summary>
    internal enum PerfSection
    {
        Firebox,
        Fireman,
        EngineTick,
        Stoker,
        Injector,
        ChimneyPlume,
        PlumeCamera,
        CoalBed,
        FireboxVolume,
        FireboxLighting,
        CoalLumps,
        Overflow,
        Controller,
        Gui,
    }

    /// <summary>
    /// Wall-clock time of instrumented main-thread sections, per
    /// timed section, against the frame rate, for the status log. Timing
    /// costs well under a microsecond a call. Not in it: the plume volume's
    /// worker thread, the GPU's drawing, and any part not timed (the sum was
    /// once labeled the whole mod's; performance review, 2026-10-25).
    /// </summary>
    internal static class PerfMeter
    {
        private static readonly int Sections = System.Enum.GetValues(typeof(PerfSection)).Length;
        private static readonly long[] Elapsed = new long[Sections];
        private static readonly int[] Calls = new int[Sections];
        private static readonly int Phases = System.Enum.GetValues(typeof(RealisticStackEffects.PlumePhase)).Length;
        private static readonly long[] PlumeElapsed = new long[Phases];
        private static readonly int[] PlumeCalls = new int[Phases];
        private static int frames;
        private static int physicsSteps;
        private static float since = -1f;

        public static long Start() => Stopwatch.GetTimestamp();

        public static void Stop(PerfSection section, long start)
        {
            Elapsed[(int)section] += Stopwatch.GetTimestamp() - start;
            Calls[(int)section]++;
        }

        public static void StopPlumePhase(RealisticStackEffects.PlumePhase phase, long start)
        {
            PlumeElapsed[(int)phase] += Stopwatch.GetTimestamp() - start;
            PlumeCalls[(int)phase]++;
        }

        // Child measurements overlap their parent. Keeping separate arrays
        // prevents double-counting them in the existing main-thread sum.
        internal static long TotalTicks => Elapsed.Sum();

        internal static string PlumeBreakdown(double perFrame, int frameCount) => string.Join(", ",
            Enumerable.Range(0, Phases).Select(i => $"{(RealisticStackEffects.PlumePhase)i} {PlumeElapsed[i] * perFrame:F2} " +
                $"({(frameCount > 0 ? (float)PlumeCalls[i] / frameCount : 0f):F1} calls/frame)"));

        /// <summary>Counts a rendered frame; call once per frame.</summary>
        public static void Frame()
        {
            if (since < 0f)
                since = Time.realtimeSinceStartup;
            frames++;
        }

        /// <summary>Counts a physics step; call from FixedUpdate.</summary>
        public static void PhysicsStep() => physicsSteps++;

        /// <summary>The frame rate and each section's milliseconds per frame since the last report; resets.</summary>
        public static string Report()
        {
            var now = Time.realtimeSinceStartup;
            var seconds = since >= 0f ? now - since : 0f;
            var perFrame = frames > 0 ? 1000.0 / Stopwatch.Frequency / frames : 0.0;
            var total = TotalTicks * perFrame;
            var parts = string.Join(", ", Enumerable.Range(0, Sections)
                .Select(i => $"{(PerfSection)i} {Elapsed[i] * perFrame:F2} ({(frames > 0 ? (float)Calls[i] / frames : 0f):F1} calls/frame)"));
            var steps = frames > 0 ? (float)physicsSteps / frames : 0f;
            var text = $"frame rate {(seconds > 0f ? frames / seconds : 0f):F0} fps over {seconds:F0} s, {steps:F1} physics steps a frame; " +
                       $"timed sections {total:F2} ms a frame on the main thread ({parts}; EngineTick includes the stock engine; not the worker or the GPU); " +
                       $"plume breakdown within ChimneyPlume, not added to total ({PlumeBreakdown(perFrame, frames)}; Motion includes Shadows, Buffers and VolumeSubmit; VolumeSubmit excludes worker execution)";
            System.Array.Clear(Elapsed, 0, Elapsed.Length);
            System.Array.Clear(Calls, 0, Calls.Length);
            System.Array.Clear(PlumeElapsed, 0, PlumeElapsed.Length);
            System.Array.Clear(PlumeCalls, 0, PlumeCalls.Length);
            frames = 0;
            physicsSteps = 0;
            since = now;
            return text;
        }
    }
}
