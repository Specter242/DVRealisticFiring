using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LocoSim.Implementations;
using RealisticFiring.Sim;

namespace RealisticFiring.Patches
{
    /// <summary>
    /// Contains a failure in the sim steps and save hooks the mod runs in
    /// (audit 2026-11-09). The game's sim loop and its car loader have no
    /// catch of their own: one exception in a step, the mod's or a patch's on
    /// a stock one, stopped every component after it on that car each frame,
    /// and one in a save hook could abort the save or the loading of the
    /// game's cars. A finalizer on each ends the failed step there, and
    /// <see cref="Faults"/> logs it once and counts the rest.
    /// </summary>
    internal static class TickGuard
    {
        private const string Tick = "Tick";
        private const string Save = "GetSaveStateData";
        private const string Load = "SetSaveStateData";

        // The stock steps the mod's patches run in.
        private static readonly (Type Type, string[] Methods)[] Stock =
        {
            (typeof(Boiler), new[] { Tick, "SimulateInjector", "SimulateBlowdown", "SimulateWaterLevel", "SimulateCrownSheet", Save, Load }),
            (typeof(ReciprocatingSteamEngine), new[] { Tick, "SimulateMeanTorque", "SimulateSteamChest", "SimulateValveFlow", Save, Load }),
            (typeof(ResourceContainer), new[] { Save, Load }),
            (typeof(SteamCompressor), new[] { Tick }),
            (typeof(SteamExhaust), new[] { Tick }),
        };

        /// <summary>The steps to guard: the stock ones, and the mod's own sim components'.</summary>
        internal static IEnumerable<(Type Type, string Method)> Guarded(IEnumerable<Type> modTypes)
        {
            foreach (var (type, methods) in Stock)
                foreach (var method in methods)
                    yield return (type, method);
            foreach (var type in modTypes.Where(t => t != null && !t.IsAbstract && typeof(SimComponent).IsAssignableFrom(t)))
                foreach (var method in new[] { Tick, Save, Load })
                    if (AccessTools.DeclaredMethod(type, method) != null)
                        yield return (type, method);
        }

        /// <summary>Puts the guard on each step; returns those it could not be put on.</summary>
        public static List<string> Install(Harmony harmony, IEnumerable<Type> modTypes, Action<string> error) =>
            Install(harmony, Guarded(modTypes).ToList(), error);

        internal static List<string> Install(Harmony harmony, IEnumerable<(Type Type, string Method)> steps, Action<string> error)
        {
            var failed = new List<string>();
            var contain = new HarmonyMethod(typeof(TickGuard), nameof(Contain));
            foreach (var (type, name) in steps)
            {
                try
                {
                    var method = AccessTools.DeclaredMethod(type, name);
                    if (method == null)
                        throw new MissingMethodException(type.FullName, name);
                    harmony.Patch(method, finalizer: contain);
                }
                catch (Exception e)
                {
                    failed.Add($"{type.Name}.{name}");
                    error?.Invoke($"No guard on {type.Name}.{name}; a failure there is not contained: {e.GetBaseException().GetType().Name}: {e.GetBaseException().Message}");
                }
            }
            return failed;
        }

        private static Exception Contain(Exception __exception, MethodBase __originalMethod, object __instance)
        {
            if (__exception == null)
                return null;
            // A fire that cannot be stepped gives no heat, where its last
            // tick's would have stood in the port for good.
            if (__originalMethod.Name == Tick)
            {
                if (__instance is RealisticFirebox coal)
                    coal.NoHeat();
                else if (__instance is RealisticOilFirebox oil)
                    oil.NoHeat();
            }
            Faults.Report($"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}", null, __exception);
            return null;
        }
    }
}
