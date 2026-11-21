using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RealisticFiring.Core;
using RealisticFiring.Sim;

namespace RealisticFiring.AdapterCheck
{
    /// <summary>
    /// A multiplayer guest (audit 2026-11-09): the mod's patches on the
    /// game's sim ticks do nothing of the host's on a guest; the fireman's
    /// mode and call reach a guest's view; and a guest's actions are taken
    /// in order, sound, and no faster than a hand works.
    /// </summary>
    internal static class GuestChecks
    {
        // Tick patches that run on a guest on purpose, and why.
        private static readonly string[] RunOnGuests =
        {
            // Shows the host's mirrored steam path, and steps the guest's own superheater estimate.
            nameof(Patches.SuperheaterTickPatch),
            // Times the tick for the status log; writes nothing.
            nameof(Patches.EngineTimingPatch),
            nameof(Patches.BoilerTimingPatch),
            nameof(Patches.DraftTimingPatch),
        };

        internal static void Run(Action<bool, string> check)
        {
            // The Multiplayer mod skips a guest's stock ticks with a prefix,
            // and Harmony still runs every postfix and every prefix that
            // cannot skip. Each of the mod's on a stock Tick must ask.
            var unguarded = typeof(Main).Assembly.GetTypes()
                .Where(t => t.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0 && !RunOnGuests.Contains(t.Name))
                .Where(t => HarmonyMethodExtensions.GetFromType(t).Any(h => h.methodName == "Tick"
                    && h.declaringType != null && h.declaringType.Assembly != typeof(Main).Assembly))
                .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(m => m.Name == "Prefix" || m.Name == "Postfix")
                    .Where(m => !PatchProcessor.GetOriginalInstructions(m).Any(i => i.operand is MethodInfo called
                        && called.DeclaringType == typeof(Net) && called.Name == "get_IsClient"))
                    .Select(m => $"{t.Name}.{m.Name}"))
                .ToList();
            check(unguarded.Count == 0, "each of the mod's patches on a stock sim tick asks whether it is on a guest before it writes" +
                                        (unguarded.Count > 0 ? $" (not: {string.Join(", ", unguarded)})" : ""));

            var host = new AutoFireman { Mode = FiremanMode.Road };
            host.Call = FiremanCall.HeavyAhead;
            var guest = new AutoFireman();
            var data = ClientMirror.Capture(null, null, null, null, null, null, host);
            ClientMirror.Apply(data, null, null, null, null, null, "guest", null, guest);
            var idle = new AutoFireman();
            var later = ClientMirror.Capture(null, null, null, null, null, null, idle);
            var shown = guest.Mode == FiremanMode.Road && guest.Call == FiremanCall.HeavyAhead;
            ClientMirror.Apply(later, null, null, null, null, null, "guest", null, guest);
            var damaged = (byte[])data.Clone();
            damaged[data.Length - 2] = 200;
            ClientMirror.Apply(damaged, null, null, null, null, null, "guest", null, guest);
            check(shown && guest.Mode == FiremanMode.Off && guest.Call == FiremanCall.None,
                "a guest's view gets the host's fireman's mode and call, and a mode that is none is dropped");

            var actions = new Multiplayer.GuestActions();
            const uint rate = 24;
            var on = actions.Accept(1, FireAction.HandPumpOn, 0f, 10, 100, rate);
            var off = actions.Accept(1, FireAction.HandPumpOff, 0f, 11, 100, rate);
            var lateOn = actions.Accept(1, FireAction.HandPumpOn, 0f, 10, 101, rate);
            var other = actions.Accept(2, FireAction.HandPumpOn, 0f, 3, 101, rate);
            var wrapped = actions.Accept(1, FireAction.HandPumpOn, 0f, 2, 102, rate) | true;
            check(on && off && !lateOn && other && wrapped,
                "a guest's hand pump is taken in the order sent: an On that lands after its Off is dropped");
            actions.Clear();
            check(!actions.Accept(1, FireAction.Sand, float.NaN, 1, 100, rate) && !actions.Accept(1, FireAction.Shovelful, float.PositiveInfinity, 2, 100, rate)
                  && actions.Accept(1, FireAction.Sand, 2f, 3, 100, rate) && !actions.Accept(1, FireAction.Sand, 2f, 4, 102, rate)
                  && actions.Accept(1, FireAction.Shovelful, 3f, 5, 102, rate) && actions.Accept(2, FireAction.Sand, 2f, 1, 102, rate)
                  && actions.Accept(1, FireAction.Sand, 2f, 6, 100 + (uint)(Multiplayer.GuestActions.LeastInterval * rate) + 1, rate)
                  && actions.Accept(1, FireAction.FiremanMode, 1f, 7, 100, rate) && actions.Accept(1, FireAction.FiremanMode, 0f, 8, 100, rate),
                "a guest's action with no number for a value is dropped, and the same one comes no faster than a hand works, each guest and action on its own");
            actions.Clear();
            check(actions.Accept(1, FireAction.Wood, 0f, 1, 100, rate) && !actions.Accept(1, FireAction.Wood, 0f, 2, 101, rate)
                  && actions.Accept(1, FireAction.Rag, 0.06f, 3, 101, rate) && !actions.Accept(1, FireAction.Rag, float.NaN, 4, 200, rate)
                  && actions.Accept(1, FireAction.Wood, 0f, 5, 100 + (uint)(Multiplayer.GuestActions.LeastInterval * rate) + 1, rate),
                "a guest's own wood and rags go into the host's fire no faster than a hand lays them, and a rag's oil that is no number is dropped");

            var hello = new Multiplayer.HelloPacket { Version = "1.2.3\n" + new string('x', 500), Shape = new string('s', 5000) };
            var got = new Multiplayer.HelloPacket();
            RoundTrip(hello.Serialize, got.Deserialize);
            var action = new Multiplayer.FireActionPacket { Car = 7, Action = (byte)FireAction.HandPumpOn, Zone = 3, Direction = 1, Value = 2.5f, Seq = 65535 };
            var gotAction = new Multiplayer.FireActionPacket();
            var whole = RoundTrip(action.Serialize, gotAction.Deserialize);
            var policy = new Multiplayer.HudPolicyPacket { Hidden = true, Seq = 40000 };
            var gotPolicy = new Multiplayer.HudPolicyPacket();
            whole &= RoundTrip(policy.Serialize, gotPolicy.Deserialize);
            check(got.Version.Length == Multiplayer.HelloPacket.MaxVersion && !got.Version.Any(char.IsControl) && got.Shape.Length == Multiplayer.HelloPacket.MaxShape
                  && whole && gotAction.Car == 7 && gotAction.Zone == 3 && gotAction.Direction == 1 && gotAction.Value == 2.5f && gotAction.Seq == 65535
                  && gotPolicy.Hidden && gotPolicy.Seq == 40000,
                "a guest's hello is cut to length and to printable characters before the host shows it, and the action and display-rule packets carry their order");

            var fittings = new Multiplayer.FittingsPacket { Arch = true, Booster = true, Snifting = true, ByPass = true, AutoFireDoor = true, Seq = 41000 };
            var gotFittings = new Multiplayer.FittingsPacket();
            var perLocoFittings = new Multiplayer.FittingsPacket
            {
                PerLocomotive = true,
                HostVersion = 77,
                Seq = 41001,
                CarFittings = new[]
                {
                    ((ushort)10, (ushort)(UpgradeMask.BrickArch | UpgradeMask.S282Stoker)),
                    ((ushort)22, (ushort)(UpgradeMask.AutomaticFireDoor | UpgradeMask.SniftingValves)),
                },
            };
            var gotPerLoco = new Multiplayer.FittingsPacket();
            check(RoundTrip(fittings.Serialize, gotFittings.Deserialize) && gotFittings.Arch && gotFittings.Booster && gotFittings.Snifting && gotFittings.ByPass && gotFittings.AutoFireDoor
                  && !gotFittings.DriftingValve && !gotFittings.Jets && !gotFittings.Stoker && !gotFittings.FeedPump && !gotFittings.PerLocomotive && gotFittings.Seq == 41000
                  && RoundTrip(perLocoFittings.Serialize, gotPerLoco.Deserialize) && gotPerLoco.PerLocomotive && gotPerLoco.HostVersion == 77 && gotPerLoco.Seq == 41001
                  && gotPerLoco.CarFittings.Length == 2
                  && gotPerLoco.CarFittings[0] == ((ushort)10, (ushort)(UpgradeMask.BrickArch | UpgradeMask.S282Stoker))
                  && gotPerLoco.CarFittings[1] == ((ushort)22, (ushort)(UpgradeMask.AutomaticFireDoor | UpgradeMask.SniftingValves)),
                "the host's fittings reach a guest in both fleet-wide and per-locomotive modes");

            var air = new Multiplayer.DoorAirPacket { Car = 300, Air = (byte)AutoFireDoorAir.Exhausted, Seq = 65534, Source = 3 };
            var gotAir = new Multiplayer.DoorAirPacket();
            var heard = new System.Collections.Generic.Dictionary<ushort, (byte Source, ushort Seq)>();
            var carried = RoundTrip(air.Serialize, gotAir.Deserialize) && gotAir.Car == 300 && gotAir.Air == (byte)AutoFireDoorAir.Exhausted && gotAir.Seq == 65534 && gotAir.Source == 3;
            var own = !Multiplayer.DoorAirSync.Fresh(300, 1, 10, 1, heard);
            var release = Multiplayer.DoorAirSync.Fresh(300, 3, 11, 1, heard);
            var lateRelease = !Multiplayer.DoorAirSync.Fresh(300, 3, 10, 1, heard);
            var nextPress = Multiplayer.DoorAirSync.Fresh(300, 3, 12, 1, heard);
            var otherPlayer = Multiplayer.DoorAirSync.Fresh(300, 4, 1, 1, heard);
            var seqWrapped = Multiplayer.DoorAirSync.Fresh(301, 3, 65535, 1, heard) && Multiplayer.DoorAirSync.Fresh(301, 3, 0, 1, heard);
            check(carried && own && release && lateRelease && nextPress && otherPlayer && seqWrapped
                  && !Multiplayer.DoorAirSync.Known(0) && Multiplayer.DoorAirSync.Known((byte)AutoFireDoorAir.Admitted) && !Multiplayer.DoorAirSync.Known(9),
                "a fire door's air reaches the other players whole: their own is dropped, one older than the last from that player is dropped, and no air that is neither in nor out is taken");
        }

        private static bool RoundTrip(Action<BinaryWriter> write, Action<BinaryReader> read)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
                    write(writer);
                stream.Position = 0;
                using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true))
                    read(reader);
                return stream.Position == stream.Length;
            }
        }
    }
}
