using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace RealisticStackEffects
{
 internal static class SeasonsMod
    {
        internal const string AssemblyName = "DVSeasons";
        internal const string MainType = "DVSeasons.Mod.Main";
        internal const string StateType = "DVSeasons.Core.SeasonState";
        internal const string TemperatureProperty = "TemperatureCelsius";

        // Looked for again this often until found, in s (the mod loads before the game).
        private const float LookAgain = 10f;

        private static float nextLook;

        // Seconds since first asked, on .NET's own clock (no Unity call, so
        // the adapter check can run this outside the game).
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static float Now => (float)Clock.Elapsed.TotalSeconds;
        private static bool found;
        private static FieldInfo runtime;
        private static PropertyInfo currentState;
        private static PropertyInfo temperature;
        private static PropertyInfo current;
        private static PropertyInfo next;
        private static PropertyInfo transition;
        private static PropertyInfo snow;

        /// <summary>DV Seasons' state now: false when it is not running or has no state yet.</summary>
        public static bool TryRead(out float celsius, out string season)
        {
            celsius = 0f;
            season = null;
            if (!Find())
                return false;
            try
            {
                var owner = runtime.GetValue(null);
                var state = owner != null ? currentState.GetValue(owner, null) : null;
                if (state == null)
                    return false;
                if (temperature == null)
                {
                    var type = state.GetType();
                    temperature = type.GetProperty(TemperatureProperty);
                    current = type.GetProperty("Current");
                    next = type.GetProperty("Next");
                    transition = type.GetProperty("Transition");
                    snow = type.GetProperty("SnowAmount");
                    if (temperature == null)
                        return false;
                }
                celsius = (float)temperature.GetValue(state, null);
                if (float.IsNaN(celsius) || float.IsInfinity(celsius))
                    return false;
                var shift = transition != null ? (float)transition.GetValue(state, null) : 0f;
                season = $"DV Seasons: {current?.GetValue(state, null)}" +
                         (shift > 0.01f ? $" turning {next?.GetValue(state, null)} ({shift:P0})" : "") +
                         (snow != null ? $", snow {(float)snow.GetValue(state, null):P0}" : "");
                return true;
            }
            catch (Exception e)
            {
                // Its layout changed: stop asking, and say so once.
                found = false;
                runtime = null;
                nextLook = float.PositiveInfinity;
                StackHost.Mod?.Logger.Warning($"DV Seasons' state could not be read ({e.GetType().Name}: {e.Message}); the chimney's steam takes its air from the season setting");
                return false;
            }
        }

        /// <summary>DV Seasons' runtime field on its <paramref name="main"/> type and the runtime's current state property; nulls when not there.</summary>
        internal static (FieldInfo Runtime, PropertyInfo CurrentState) Resolve(Type main)
        {
            var field = main?.GetField("runtime", BindingFlags.NonPublic | BindingFlags.Static);
            return (field, field?.FieldType.GetProperty("CurrentState", BindingFlags.Public | BindingFlags.Instance));
        }

        private static bool Find()
        {
            if (found)
                return true;
            if (Now < nextLook)
                return false;
            nextLook = Now + LookAgain;
            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name.StartsWith(AssemblyName, StringComparison.Ordinal) && a.GetType(MainType) != null);
            var main = assembly?.GetType(MainType);
            (runtime, currentState) = Resolve(main);
            found = runtime != null && currentState != null;
            if (assembly != null)
            {
                nextLook = found ? nextLook : float.PositiveInfinity;
                StackHost.Mod?.Logger.Log(found
                    ? $"DV Seasons {assembly.GetName().Version}: the chimney's steam takes its air's temperature from its season"
                    : "DV Seasons is loaded but its state was not found; the chimney's steam takes its air from the season setting");
            }
            return found;
        }
    }
}
