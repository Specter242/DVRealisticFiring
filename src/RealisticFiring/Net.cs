using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using RealisticFiring.Core;
using UnityModManagerNet;

[assembly: InternalsVisibleTo("RealisticFiring.Multiplayer")]
[assembly: InternalsVisibleTo("RealisticFiring.StackEffects")]

namespace RealisticFiring
{
    /// <summary>What a multiplayer client asks the host to do to a loco's fire.</summary>
    internal enum FireAction : byte
    {
        Shake,
        Wiggle,
        Rake,
        Slice,
        Push,
        HandPumpOn,
        HandPumpOff,
        // An old client's hydrant key, answered with where the water hose is.
        Hydrant,
        Sand,
        Shovelful,
        SteamLine,
        FiremanMode,
        FiremanCall,
    }

    /// <summary>Something the host's fire did that players near the loco should see and hear.</summary>
    internal enum FireEvent : byte
    {
        Flareback,
        PlugMelted,
    }

 internal static class Net
    {
        private const string AddOnFile = "RealisticFiring.Multiplayer.dll";
        private const string AddOnEntry = "RealisticFiring.Multiplayer.Integration";

        /// <summary>Whether this game is a multiplayer client; set by the add-on.</summary>
        internal static Func<bool> ClientCheck;

        /// <summary>Sends a fire action to the host; set by the add-on.</summary>
        internal static Action<TrainCar, FireAction, int, int, float> Sender;

        /// <summary>Sends a fire event to the players near a loco; set by the add-on, on the host.</summary>
        internal static Action<TrainCar, FireEvent, float> Broadcaster;

        /// <summary>On the host of a session: tells the players near <paramref name="car"/> what its fire did.</summary>
        public static void Broadcast(TrainCar car, FireEvent fireEvent, float value = 0f)
        {
            if (car != null && Broadcaster != null && !IsClient)
                Broadcaster(car, fireEvent, value);
        }

        /// <summary>On a client: what the host's fire did, shown and heard here.</summary>
        public static void Receive(TrainCar car, FireEvent fireEvent, float value)
        {
            if (car == null)
                return;
            switch (fireEvent)
            {
                case FireEvent.Flareback:
                    Visuals.OilFlareback.PlayEffects(car, value);
                    break;
                case FireEvent.PlugMelted:
                    FireController.Announce(Sim.BoilerWaterRig.PlugMeltedNotice);
                    break;
            }
        }

        /// <summary>True on a multiplayer client, where the host simulates.</summary>
        public static bool IsClient => ClientCheck != null && ClientCheck();

        /// <summary>
        /// On a client, sends <paramref name="action"/> on <paramref name="car"/>
        /// to the host and returns true, so the caller leaves its own copy alone;
        /// in single player or on the host returns false, and the caller acts.
        /// </summary>
        public static bool TrySend(TrainCar car, FireAction action, int zone = -1, int direction = -1, float value = 0f)
        {
            if (car == null || Sender == null || !IsClient)
                return false;
            Sender(car, action, zone, direction, value);
            return true;
        }

        /// <summary>Loads the add-on when the Multiplayer mod runs. Never throws.</summary>
        public static void TryStartAddOn(UnityModManager.ModEntry mod)
        {
            try
            {
                var multiplayer = UnityModManager.FindMod("Multiplayer");
                if (multiplayer == null || !multiplayer.Active)
                    return;
                var path = Path.Combine(mod.Path, AddOnFile);
                if (!File.Exists(path))
                {
                    mod.Logger.Warning($"Multiplayer is running but {AddOnFile} is missing; the fire will not be shared in a session");
                    return;
                }
                var start = Assembly.LoadFrom(path).GetType(AddOnEntry)?.GetMethod("Start", BindingFlags.Public | BindingFlags.Static);
                start?.Invoke(null, new object[] { mod });
            }
            catch (Exception e)
            {
                mod.Logger.LogException("The multiplayer add-on failed to start; the fire will not be shared in a session", e);
            }
        }
    }
}
