using System;
using System.IO;
using System.Reflection;
using RealisticFiring.Sim;
using RealisticFiring.Visuals;
using RealisticStackEffects;
using UnityModManagerNet;

namespace RealisticFiring
{
 internal static class StackEffectsLink
    {
        public const string ModId = "RealisticStackEffects";
        private const string AddOnFile = "RealisticFiring.StackEffects.dll";
        private const string AddOnEntry = "RealisticFiring.StackEffects.Integration";

        /// <summary>Whether Realistic Stack Effects draws the chimneys' smoke this session.</summary>
        public static bool HandedOff { get; private set; }

        /// <summary>Checks for Realistic Stack Effects and starts the add-on that feeds it. Never throws.</summary>
        public static void TryStart(UnityModManager.ModEntry mod)
        {
            try
            {
                var stack = UnityModManager.FindMod(ModId);
                if (stack == null || !stack.Active)
                    return;
                HandedOff = true;
                var path = Path.Combine(mod.Path, AddOnFile);
                if (!File.Exists(path))
                {
                    mod.Logger.Warning($"Realistic Stack Effects draws the chimney smoke, but {AddOnFile} is missing: it reads the realistic fires' stock ports");
                    return;
                }
                var start = Assembly.LoadFrom(path).GetType(AddOnEntry)?.GetMethod("Start", BindingFlags.Public | BindingFlags.Static);
                var started = start?.Invoke(null, new object[] { mod }) is bool ok && ok;
                mod.Logger.Log(started
                    ? "Realistic Stack Effects draws the chimney smoke, fed by the realistic fires"
                    : "Realistic Stack Effects draws the chimney smoke; it could not take the realistic fires and reads their stock ports");
            }
            catch (Exception e)
            {
                mod.Logger.LogException("Feeding the realistic fires to Realistic Stack Effects failed; it reads their stock ports", e);
            }
        }

        /// <summary>The realistic fire under <paramref name="car"/>'s stack, or null for the game's own.</summary>
        internal static IStackFire FireFor(TrainCar car) =>
            FireboxRegistry.TryGet(car, out var firebox) ? new CoalStack(car, firebox)
            : RealisticOilFirebox.TryGet(car, out var oil) ? new OilStack(car, oil)
            : (IStackFire)null;

        /// <summary>
        /// Optional stack geometry hint for <paramref name="car"/> from built-in
        /// locomotive profiles or loaded maker fits; null for excluded or unhinted cars.
        /// </summary>
        internal static RealisticStackEffects.Core.StackGeometryHint? StackGeometryFor(TrainCar car)
        {
            if (car == null || car.carLivery == null || LeftStock.IsExcluded(car))
                return null;
            var livery = car.carLivery.id;
            if (LocomotiveProfiles.TryGet(livery, out var profile)
                && profile.StackGeometry is RealisticStackEffects.Core.StackGeometryHint builtIn)
            {
                var clean = builtIn.Sanitize();
                if (clean.HasAny)
                    return clean;
            }
            if (MakerFitRegistry.TryGetStackGeometry(livery, out var maker))
                return maker;
            return null;
        }

        /// <summary><see cref="StackHost.StackGeometry"/> callback when Realistic Firing hosts the plume itself.</summary>
        internal static bool TryGetStackGeometry(TrainCar car, out RealisticStackEffects.Core.StackGeometryHint hint, out string by)
        {
            var found = StackGeometryFor(car);
            if (found.HasValue)
            {
                hint = found.Value;
                by = "RealisticFiring";
                return true;
            }
            hint = default;
            by = null;
            return false;
        }
    }
}
