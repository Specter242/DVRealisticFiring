using System;
using System.Linq;
using System.Reflection;

namespace RealisticFiring
{
 internal static class CommsTabletLink
    {
        internal const string HostAssembly = "CommsTablet";
        internal const string ContractType = "CommsTablet.GuestContract";
        internal const string RetiredNotice = "The firing tablet's pages are the Firing app on the Comms Tablet's home screen";

        private static Func<string, bool> merged;
        private static int assembliesSeen = -1;
        private static bool logged, lately;
        private static DateTime askedAt;

        /// <summary>Whether the Comms Tablet has bound this mod's app, so the firing tablet is retired.</summary>
        internal static bool Merged
        {
            get
            {
                if (merged == null && !Find())
                    return false;
                bool answer;
                try
                {
                    answer = merged(CommsTabletApp.Id);
                }
                catch (Exception)
                {
                    return false;
                }
                if (answer && !logged)
                {
                    logged = true;
                    Main.Mod?.Logger.Log("Merged into the Comms Tablet: the firing tablet is retired while it runs (not given or sold); its pages are the Comms Tablet's Firing app");
                }
                return answer;
            }
        }

        /// <summary><see cref="Merged"/>, asked at most once a second: for a window drawn every frame.</summary>
        internal static bool MergedLately
        {
            get
            {
                var now = DateTime.UtcNow;
                if ((now - askedAt).TotalSeconds >= 1d)
                {
                    askedAt = now;
                    lately = Merged;
                }
                return lately;
            }
        }

        // The Comms Tablet's contract, looked for again only when more assemblies have loaded.
        private static bool Find()
        {
            Assembly[] assemblies;
            try
            {
                assemblies = AppDomain.CurrentDomain.GetAssemblies();
            }
            catch (Exception)
            {
                return false;
            }
            if (assemblies.Length == assembliesSeen)
                return false;
            assembliesSeen = assemblies.Length;
            try
            {
                var host = assemblies.FirstOrDefault(a => a.GetName().Name == HostAssembly);
                var method = host?.GetType(ContractType, false)?.GetMethod("Merged",
                    BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
                if (method == null || method.ReturnType != typeof(bool))
                    return false;
                merged = (Func<string, bool>)Delegate.CreateDelegate(typeof(Func<string, bool>), method, false);
            }
            catch (Exception)
            {
                merged = null;
            }
            return merged != null;
        }
    }
}
