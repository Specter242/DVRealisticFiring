using System;
using MPAPI;
using MPAPI.Interfaces;
using RealisticFiring.Shed;
using UnityEngine;

namespace RealisticFiring.Multiplayer
{
 internal static class StandSync
    {
        public static void Start()
        {
            ShedNet.SessionCheck = InSession;
            ShedNet.LocalPlayerId = () => MultiplayerAPI.Client != null ? MultiplayerAPI.Client.PlayerId : (byte?)null;
            ShedNet.CarId = car => car != null && MultiplayerAPI.Instance != null && MultiplayerAPI.Instance.TryGetNetId(car, out ushort id) ? id : (ushort)0;
            ShedNet.CarOf = id => MultiplayerAPI.Instance != null && MultiplayerAPI.Instance.TryGetObjectFromNetId(id, out TrainCar car) ? car : null;
            ShedNet.PlugSender = SendPlug;
            ShedNet.ValveSender = SendValve;
        }

        // Hosting or joined; not the Multiplayer mod's own single-player game.
        private static bool InSession()
        {
            var api = MultiplayerAPI.Instance;
            return api != null && api.IsConnected && !(api.IsHost && api.IsSinglePlayer);
        }

        public static void OnServerStarted(IServer server)
        {
            server.RegisterSerializablePacket<StandPlugPacket>(OnHostPlug);
            server.RegisterSerializablePacket<StandValvePacket>(OnHostValve);
        }

        public static void OnClientStarted(IClient client)
        {
            ShedNet.Reset();
            client.RegisterSerializablePacket<StandPlugPacket>(OnClientPlug);
            client.RegisterSerializablePacket<StandValvePacket>(OnClientValve);
        }

        /// <summary>On the host: a player gone lets go of their plug.</summary>
        public static void PlayerLeft(IPlayer player)
        {
            if (player != null)
                ShedNet.PlayerLeft(player.PlayerId);
        }

        /// <summary>On the host: each stand not as it starts, for a client joining.</summary>
        public static void SendStands(IPlayer player)
        {
            var server = MultiplayerAPI.Server;
            if (server == null || player == null)
                return;
            foreach (var (key, plug, valve) in ShedNet.Changed())
            {
                if (plug.HasValue)
                    server.SendSerializablePacketToPlayer(Packet(key, plug.Value, false), player);
                if (valve.HasValue)
                    server.SendSerializablePacketToPlayer(new StandValvePacket { Stand = key, Value = valve.Value.Value, Seq = valve.Value.Seq }, player);
            }
        }

        // ---- Sending ----

        private static void SendPlug(string key, PlugReport report, bool poseOnly)
        {
            var api = MultiplayerAPI.Instance;
            if (api == null || !api.IsConnected)
                return;
            var packet = Packet(key, report, poseOnly);
            if (api.IsHost)
            {
                var server = MultiplayerAPI.Server;
                if (server != null && server.PlayerCount > 1)
                    server.SendSerializablePacketToAll(packet, reliable: !poseOnly, excludeSelf: true);
            }
            else
            {
                MultiplayerAPI.Client?.SendSerializablePacketToServer(packet, reliable: !poseOnly);
            }
        }

        private static void SendValve(string key, float value, ushort seq)
        {
            var api = MultiplayerAPI.Instance;
            if (api == null || !api.IsConnected)
                return;
            var packet = new StandValvePacket { Stand = key, Value = value, Seq = seq };
            if (api.IsHost)
            {
                var server = MultiplayerAPI.Server;
                if (server != null && server.PlayerCount > 1)
                    server.SendSerializablePacketToAll(packet, reliable: true, excludeSelf: true);
            }
            else
            {
                MultiplayerAPI.Client?.SendSerializablePacketToServer(packet);
            }
        }

        private static StandPlugPacket Packet(string key, PlugReport report, bool poseOnly) => new StandPlugPacket
        {
            Stand = key,
            Place = (byte)report.Place,
            Holder = report.Holder,
            Car = report.Car,
            Position = report.Position,
            Rotation = report.Rotation,
            Settled = report.Settled,
            PoseOnly = poseOnly,
            Seq = report.Seq,
        };

        // ---- Receiving ----

        // A packet's report, or null where it makes no sense (an unknown
        // place, a pose not a number or far off the map).
        internal static PlugReport? Report(StandPlugPacket packet)
        {
            if (packet == null || string.IsNullOrEmpty(packet.Stand) || packet.Stand.Length > StandPlugPacket.MaxStand
                || !Enum.IsDefined(typeof(PlugPlace), packet.Place))
                return null;
            var p = packet.Position;
            var q = packet.Rotation;
            if (!Finite(p.x) || !Finite(p.y) || !Finite(p.z) || p.sqrMagnitude > 1e12f
                || !Finite(q.x) || !Finite(q.y) || !Finite(q.z) || !Finite(q.w))
                return null;
            var length = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (length < 0.5f || length > 1.5f)
                return null;
            return new PlugReport
            {
                Place = (PlugPlace)packet.Place,
                Holder = packet.Holder,
                Car = packet.Car,
                Position = p,
                Rotation = new Quaternion(q.x / length, q.y / length, q.z / length, q.w / length),
                Settled = packet.Settled,
                Seq = packet.Seq,
            };
        }

        private static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);

        private static void OnHostPlug(StandPlugPacket packet, IPlayer sender)
        {
            var report = Report(packet);
            var server = MultiplayerAPI.Server;
            if (sender == null || sender.IsHost || !report.HasValue || server == null)
                return;
            var relay = ShedNet.HostPlug(packet.Stand, report.Value, packet.PoseOnly, sender.PlayerId, sender.Position, out var reply);
            // The others, the sender having it already; or everyone, where
            // it was refused or changed here.
            if (relay == Relay.Others && server.PlayerCount > 2)
                server.SendSerializablePacketToAll(Packet(packet.Stand, reply, packet.PoseOnly), reliable: !packet.PoseOnly, excludeSelf: true, excludePlayer: sender);
            else if (relay == Relay.Everyone)
                server.SendSerializablePacketToAll(Packet(packet.Stand, reply, false), reliable: true, excludeSelf: true);
        }

        private static void OnHostValve(StandValvePacket packet, IPlayer sender)
        {
            var server = MultiplayerAPI.Server;
            if (sender == null || sender.IsHost || server == null || packet == null || string.IsNullOrEmpty(packet.Stand)
                || packet.Stand.Length > StandPlugPacket.MaxStand || !Finite(packet.Value))
                return;
            var relay = ShedNet.HostValve(packet.Stand, Mathf.Clamp01(packet.Value), packet.Seq, sender.PlayerId, sender.Position, out var value, out var seq);
            var reply = new StandValvePacket { Stand = packet.Stand, Value = value, Seq = seq };
            if (relay == Relay.Others && server.PlayerCount > 2)
                server.SendSerializablePacketToAll(reply, reliable: true, excludeSelf: true, excludePlayer: sender);
            else if (relay == Relay.Everyone)
                server.SendSerializablePacketToAll(reply, reliable: true, excludeSelf: true);
        }

        private static void OnClientPlug(StandPlugPacket packet)
        {
            var report = Report(packet);
            if (MultiplayerAPI.Instance == null || MultiplayerAPI.Instance.IsHost || !report.HasValue)
                return;
            ShedNet.ClientPlug(packet.Stand, report.Value, packet.PoseOnly);
        }

        private static void OnClientValve(StandValvePacket packet)
        {
            if (MultiplayerAPI.Instance == null || MultiplayerAPI.Instance.IsHost || packet == null || string.IsNullOrEmpty(packet.Stand)
                || !Finite(packet.Value))
                return;
            ShedNet.ClientValve(packet.Stand, Mathf.Clamp01(packet.Value), packet.Seq);
        }
    }
}
