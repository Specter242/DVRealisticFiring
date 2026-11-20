using System;
using System.Collections.Generic;
using MPAPI;
using MPAPI.Interfaces;
using RealisticFiring.Core;
using RealisticFiring.Visuals;
using UnityEngine;

namespace RealisticFiring.Multiplayer
{
 internal static class DoorAirSync
    {
        // A guest's door air is taken only from near the engine, as their hands could reach it.
        private const float Reach = 30f;

        private static ushort sent;
        private static readonly Dictionary<ushort, (byte Source, ushort Seq)> Heard = new Dictionary<ushort, (byte, ushort)>();

        /// <summary>Whether a packet's air is one this game should hear: not its own, and not older than the last from that player.</summary>
        internal static bool Fresh(ushort car, byte source, ushort seq, byte self, Dictionary<ushort, (byte Source, ushort Seq)> heard)
        {
            if (source == self)
                return false;
            if (heard.TryGetValue(car, out var last) && last.Source == source && !Shed.ShedNet.Newer(seq, last.Seq))
                return false;
            heard[car] = (source, seq);
            return true;
        }

        internal static bool Known(byte air) => air == (byte)AutoFireDoorAir.Admitted || air == (byte)AutoFireDoorAir.Exhausted;

        private static byte Self => MultiplayerAPI.Client != null ? MultiplayerAPI.Client.PlayerId : byte.MaxValue;

        internal static void Server(IServer server)
        {
            Heard.Clear();
            Net.DoorAirSender = Send;
            server.RegisterSerializablePacket<DoorAirPacket>((p, sender) =>
            {
                try
                {
                    var api = MultiplayerAPI.Instance;
                    if (p == null || sender == null || api == null || !Known(p.Air) || !api.TryGetObjectFromNetId(p.Car, out TrainCar car) || car == null)
                        return;
                    if (sender.OccupiedCar != car && (sender.Position - car.transform.position).sqrMagnitude > Reach * Reach)
                        return;
                    p.Source = sender.PlayerId;
                    if (Fresh(p.Car, p.Source, p.Seq, Self, Heard))
                        AutoFireDoorSounds.Remote(car, (AutoFireDoorAir)p.Air);
                    // To everyone else; the guest it came from drops its own.
                    server.SendSerializablePacketToAll(p, reliable: true, excludeSelf: true);
                }
                catch (Exception e)
                {
                    Faults.Report("a guest's fire door air", null, e);
                }
            });
        }

        internal static void Client(IClient client)
        {
            Heard.Clear();
            Net.DoorAirSender = Send;
            client.RegisterSerializablePacket<DoorAirPacket>(p =>
            {
                try
                {
                    var api = MultiplayerAPI.Instance;
                    if (p == null || api == null || api.IsHost || !Known(p.Air) || !api.TryGetObjectFromNetId(p.Car, out TrainCar car) || car == null)
                        return;
                    if (Fresh(p.Car, p.Source, p.Seq, Self, Heard))
                        AutoFireDoorSounds.Remote(car, (AutoFireDoorAir)p.Air);
                }
                catch (Exception e)
                {
                    Faults.Report("a fire door's air from the host", null, e);
                }
            });
        }

        internal static void Stopped()
        {
            Heard.Clear();
            Net.DoorAirSender = null;
        }

        // This game's own door: from the host to everyone, from a guest to the host.
        private static void Send(TrainCar car, AutoFireDoorAir air)
        {
            var api = MultiplayerAPI.Instance;
            if (car == null || api == null || !Net.InSession || !api.TryGetNetId(car, out ushort netId))
                return;
            var packet = new DoorAirPacket { Car = netId, Air = (byte)air, Seq = ++sent, Source = Self };
            if (api.IsHost)
                MultiplayerAPI.Server?.SendSerializablePacketToAll(packet, reliable: true, excludeSelf: true);
            else
                MultiplayerAPI.Client?.SendSerializablePacketToServer(packet);
        }
    }
}
