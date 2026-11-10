using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace RealisticFiring
{
    /// <summary>
    /// Checks, before anything is patched, that the game still has the
    /// private members the mod reads by name (audit 2026-11-09). They are
    /// bound in static initializers. After a game update that renamed one,
    /// its class failed to initialize on first use and threw on every use
    /// after, each tick, with the patches around it in place and no notice
    /// that anything had failed to load.
    /// </summary>
    internal static class GameMembers
    {
        private const BindingFlags Statics = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        /// <summary>
        /// Runs the static initializer of every class of
        /// <paramref name="types"/> that binds a game field. Returns those
        /// that failed, each with what it could not find.
        /// </summary>
        public static Dictionary<Type, string> Unbound(IEnumerable<Type> types)
        {
            var unbound = new Dictionary<Type, string>();
            foreach (var type in types.Where(t => t != null && !t.ContainsGenericParameters))
            {
                FieldInfo[] bound;
                try
                {
                    bound = type.GetFields(Statics).Where(f => BindsAField(f.FieldType)).ToArray();
                }
                catch (Exception)
                {
                    // A type that needs a mod that is not installed.
                    continue;
                }
                if (bound.Length == 0)
                    continue;
                try
                {
                    RuntimeHelpers.RunClassConstructor(type.TypeHandle);
                    var missing = bound.Where(f => f.GetValue(null) == null).Select(f => f.Name).ToList();
                    if (missing.Count > 0)
                        unbound[type] = $"no game field behind {string.Join(", ", missing)}";
                }
                catch (Exception e)
                {
                    var cause = e.GetBaseException();
                    unbound[type] = $"{cause.GetType().Name}: {cause.Message}";
                }
            }
            return unbound;
        }

        private static bool BindsAField(Type type) =>
            type.IsGenericType && type.GetGenericTypeDefinition() == typeof(AccessTools.FieldRef<,>);

        /// <summary>Whether <paramref name="type"/> is, or lies inside, one of <paramref name="unbound"/>.</summary>
        public static bool Within(Type type, ICollection<Type> unbound)
        {
            for (var t = type; t != null; t = t.DeclaringType)
                if (unbound.Contains(t))
                    return true;
            return false;
        }

        /// <summary>
        /// Whether the mod can run without <paramref name="type"/>: its
        /// simulation cannot. A cab fitting, an item, or a control's feel
        /// that fails to bind goes without, and says so in the log.
        /// </summary>
        public static bool Essential(Type type)
        {
            for (var t = type; t != null; t = t.DeclaringType)
                if (t.Namespace == typeof(Sim.FireboxRegistry).Namespace)
                    return true;
            return false;
        }
    }
}
