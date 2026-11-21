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
        // A guest's own piece of wood or rag laid or thrown into the box, when the Multiplayer mod shares no items:
        // the zone aimed at; for a rag, its oil (Value) and whether it burns (Direction 1).
        Wood,
        Rag,
        // A guest's firewood, rag, lighter ignition, or fallback door toggle on a caboose stove.
        StoveWood,
        StoveRag,
        StoveIgnite,
        StoveDoorToggle,
    }

    /// <summary>Something the host's fire did that players near the loco should see and hear.</summary>
    internal enum FireEvent : byte
    {
        Flareback,
        PlugMelted,
        SmokeWarning,
        SmokeFeeStarted,
    }

 internal static class Net
    {
        private const string AddOnFile = "RealisticFiring.Multiplayer.dll";
        private const string AddOnEntry = "RealisticFiring.Multiplayer.Integration";

        /// <summary>Whether this game is a multiplayer client; set by the add-on.</summary>
        internal static Func<bool> ClientCheck;

        /// <summary>A connected host or client, excluding Multiplayer's single-player mode.</summary>
        internal static Func<bool> SessionCheck;
        public static bool InSession => SessionCheck != null && SessionCheck();

        /// <summary>Sends a fire action to the host; set by the add-on.</summary>
        internal static Action<TrainCar, FireAction, int, int, float> Sender;

        /// <summary>
        /// Passes this game's automatic fire door air to the other players (the
        /// host to everyone, a guest through the host); set by the add-on in a session.
        /// </summary>
        internal static Action<TrainCar, AutoFireDoorAir> DoorAirSender;

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
                case FireEvent.SmokeWarning:
                case FireEvent.SmokeFeeStarted:
                    Sim.SmokeFees.ShowNotice(car, fireEvent);
                    break;
                case FireEvent.PlugMelted:
                    // Named: every guest hears of it, wherever they are.
                    FireController.Announce($"{Sim.CarNames.LogName(car)}: {Sim.BoilerWaterRig.PlugMeltedNoticeFor(car)}");
                    break;
            }
        }

        /// <summary>True on a multiplayer client, where the host simulates.</summary>
        public static bool IsClient => ClientCheck != null && ClientCheck();

        /// <summary>Whether the Multiplayer mod shares world items between the players' games; set by the add-on.</summary>
        internal static Func<bool> ItemsSharedCheck;

        /// <summary>
        /// Whether a crate, tin, piece, or rag here is the host's, shown by
        /// the Multiplayer mod, so the host acts on it: on a client whose
        /// Multiplayer shares items. Its 0.1.16 shares none (its item packets'
        /// handlers are empty; Ace, DV Discord, 2026-11-19: "they straight up
        /// arent spawning in mp atleast for clients"), so a guest's are its
        /// own, as in single player, and only what it lays in a fire goes to
        /// the host (<see cref="FireAction.Wood"/>, <see cref="FireAction.Rag"/>).
        /// </summary>
        public static bool ItemsRemote => IsClient && ItemsSharedCheck != null && ItemsSharedCheck();

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

        /// <summary>
        /// Why the fire is not shared in a session, when the Multiplayer mod
        /// runs and the add-on does not; null when all is well. Shown once in
        /// game: only the log said so, and such a guest's fires ran on their
        /// own with no word to anyone (audit 2026-11-09).
        /// </summary>
        internal static string AddOnProblem;

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
                    AddOnProblem = $"{AddOnFile} is missing from the mod's folder";
                    return;
                }
                var start = Assembly.LoadFrom(path).GetType(AddOnEntry)?.GetMethod("Start", BindingFlags.Public | BindingFlags.Static);
                start?.Invoke(null, new object[] { mod });
            }
            catch (Exception e)
            {
                mod.Logger.LogException("The multiplayer add-on failed to start; the fire will not be shared in a session", e);
                AddOnProblem = "its multiplayer add-on failed to start";
            }
        }
    }
}
