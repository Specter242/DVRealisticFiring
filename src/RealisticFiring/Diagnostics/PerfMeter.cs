using System.Diagnostics;
using System.Linq;
using UnityEngine;

namespace RealisticFiring.Diagnostics
{
    /// <summary>Parts of the mod timed by <see cref="PerfMeter"/>.</summary>
    internal enum PerfSection
    {
        Firebox,
        Stoker,
        Injector,
        ChimneyPlume,
        CoalBed,
        Overflow,
        Controller,
        Gui,
    }

    /// <summary>
    /// Wall-clock time the mod's own code takes, per section, against the
    /// frame rate, for the status log. Timing costs well under a microsecond
    /// a call.
    /// </summary>
    internal static class PerfMeter
    {
        private static readonly int Sections = System.Enum.GetValues(typeof(PerfSection)).Length;
        private static readonly long[] Elapsed = new long[Sections];
        private static int frames;
        private static int physicsSteps;
        private static float since = -1f;

        public static long Start() => Stopwatch.GetTimestamp();

        public static void Stop(PerfSection section, long start) =>
            Elapsed[(int)section] += Stopwatch.GetTimestamp() - start;

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
            var total = Elapsed.Sum() * perFrame;
            var parts = string.Join(", ", Enumerable.Range(0, Sections)
                .Select(i => $"{(PerfSection)i} {Elapsed[i] * perFrame:F2}"));
            var steps = frames > 0 ? (float)physicsSteps / frames : 0f;
            var text = $"frame rate {(seconds > 0f ? frames / seconds : 0f):F0} fps over {seconds:F0} s, {steps:F1} physics steps a frame; " +
                       $"mod {total:F2} ms a frame ({parts})";
            System.Array.Clear(Elapsed, 0, Elapsed.Length);
            frames = 0;
            physicsSteps = 0;
            since = now;
            return text;
        }
    }
}
