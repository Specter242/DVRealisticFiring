using System;
using System.Linq;
using System.Reflection;
using RealisticFiring.Core;

namespace RealisticFiring.AdapterCheck
{
    /// <summary>
    /// FiremanApi as AI Engineer binds it: found by its assembly-qualified
    /// name, every entry public and static with the parameter and return
    /// types it calls by reflection, the calls named as FiremanCall's names
    /// (CallOf's answers are what SetCall takes), and a loco with no fireman
    /// answered with null or false, not an exception.
    /// </summary>
    internal static class FiremanApiChecks
    {
        internal static void Run(Action<bool, string> check)
        {
            var api = Type.GetType("RealisticFiring.FiremanApi, RealisticFiring", false);
            check(api != null && api.IsPublic && api.IsAbstract && api.IsSealed, "FiremanApi is a public static class found by name");
            if (api == null)
                return;
            var version = api.GetField("Version", BindingFlags.Public | BindingFlags.Static);
            check(version != null && version.IsLiteral && (int)version.GetRawConstantValue() == 1, "FiremanApi.Version is 1");
            foreach (var (name, returns, parameters) in new[]
                     {
                         ("Has", typeof(bool), new[] { typeof(TrainCar) }),
                         ("ModeOf", typeof(string), new[] { typeof(TrainCar) }),
                         ("CallOf", typeof(string), new[] { typeof(TrainCar) }),
                         ("SetMode", typeof(bool), new[] { typeof(TrainCar), typeof(string) }),
                         ("SetCall", typeof(bool), new[] { typeof(TrainCar), typeof(string) }),
                     })
            {
                var method = api.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, parameters, null);
                check(method != null && method.ReturnType == returns,
                    $"FiremanApi.{name}({string.Join(", ", parameters.Select(p => p.Name))}) returns {returns.Name}");
            }
            check(Enum.GetNames(typeof(FiremanCall)).SequenceEqual(new[] { "None", "HeavyAhead", "EasingOff" }),
                "FiremanApi's calls are None, HeavyAhead, and EasingOff, as CallOf answers and SetCall takes");
            check(FiremanApi.CallOf(null) == null && FiremanApi.ModeOf(null) == null && !FiremanApi.Has(null)
                  && !FiremanApi.SetCall(null, "HeavyAhead") && !FiremanApi.SetMode(null, "Road"),
                "FiremanApi answers a loco with no fireman with null or false");
        }
    }
}
