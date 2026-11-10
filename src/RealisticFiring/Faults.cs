using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace RealisticFiring
{
    /// <summary>
    /// Where the mod's contained failures are counted (audit 2026-11-09). A
    /// step that throws inside the game's sim loop stops every component
    /// after it on that car and writes a stack trace each frame; contained,
    /// it is skipped and tried again, the first failure of each place is
    /// logged whole and announced, and later ones are counted and summed up
    /// once a minute.
    /// </summary>
    internal static class Faults
    {
        private sealed class Seen
        {
            public int Count;
            public long NextLog;
        }

        // How often a place that keeps failing is summed up in the log, ms.
        private const long Every = 60_000;
        // No more notices than this in a session: the log has the rest.
        private const int MostNotices = 3;

        private static readonly Dictionary<string, Seen> ByPlace = new Dictionary<string, Seen>();
        private static readonly Stopwatch Clock = Stopwatch.StartNew();

        /// <summary>How many distinct places have failed this session.</summary>
        internal static int Places => ByPlace.Count;

        /// <summary>How often <paramref name="place"/> has failed this session.</summary>
        internal static int CountAt(string place) => ByPlace.TryGetValue(place, out var seen) ? seen.Count : 0;

        /// <summary>Notes a contained failure at <paramref name="place"/>, on <paramref name="car"/> where known.</summary>
        public static void Report(string place, TrainCar car, Exception e)
        {
            var now = Clock.ElapsedMilliseconds;
            if (!ByPlace.TryGetValue(place, out var seen))
            {
                ByPlace[place] = new Seen { Count = 1, NextLog = now + Every };
                Main.Mod?.Logger.LogException($"{Where(place, car)} failed and was skipped; it is tried again, and further failures there are counted, not logged each time", e);
                if (ByPlace.Count <= MostNotices)
                    FireController.Announce($"Realistic Firing: an error in {place}; it may misbehave. Please send the Unity Mod Manager log with a bug report");
                return;
            }
            seen.Count++;
            if (now < seen.NextLog)
                return;
            seen.NextLog = now + Every;
            Main.Mod?.Logger.Error($"{Where(place, car)} has failed {seen.Count} times this session; the last: {e.GetType().Name}: {e.Message}");
        }

        private static string Where(string place, TrainCar car)
        {
            string id = null;
            // A car being destroyed can throw on its own ID.
            try { id = car != null ? car.ID : null; }
            catch (Exception) { }
            return id != null ? $"{id}: {place}" : place;
        }

        /// <summary>Forgets what failed: for the checks.</summary>
        internal static void Reset() => ByPlace.Clear();
    }
}
