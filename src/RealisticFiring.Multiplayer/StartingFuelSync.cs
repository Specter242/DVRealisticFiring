using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using MPAPI;
using MPAPI.Interfaces;
using MPAPI.Interfaces.Packets;
using RealisticFiring.Items;
using UnityEngine;

namespace RealisticFiring.Multiplayer
{
    public sealed class StartingFuelPacket : ISerializablePacket
    {
        public ushort Item, Seq;
        public byte Action;
        public float Oil;
        public bool Lit;
        public int Count;
        public void Serialize(BinaryWriter w) { w.Write(Item); w.Write(Seq); w.Write(Action); w.Write(Oil); w.Write(Lit); w.Write(Count); }
        public void Deserialize(BinaryReader r) { Item = r.ReadUInt16(); Seq = r.ReadUInt16(); Action = r.ReadByte(); Oil = r.ReadSingle(); Lit = r.ReadBoolean(); Count = r.ReadInt32(); }
    }
    /// <summary>Stock MP owns item identities and movement; RF owns fuel and consumption.</summary>
    internal static class StartingFuelSync
    {
        // Host to clients: an item's oil, flame, and count.
        private const byte Snapshot = 200;
        private static ushort sent;
        private static ushort stateSeq;
        private static readonly Dictionary<byte, ushort> requests = new Dictionary<byte, ushort>();
        private static readonly Dictionary<ushort, ushort> snapshots = new Dictionary<ushort, ushort>();
        private static Type networkType;
        private static System.Reflection.PropertyInfo idProperty;
        private static ushort Id(StartingFuelItem item)
        {
            if (networkType == null)
            {
                networkType = AccessTools.TypeByName("Multiplayer.Components.Networking.World.NetworkedItem");
                if (networkType == null) return 0;
                idProperty = AccessTools.Property(networkType, "NetId");
            }
            var component = item.GetComponent(networkType);
            return component != null && idProperty != null ? (ushort)idProperty.GetValue(component) : (ushort)0;
        }
        private static StartingFuelItem Find(ushort id) => id == 0 ? null : StartingFuelItem.Live.FirstOrDefault(i => i != null && Id(i) == id);
        internal static void Forget(byte player) => requests.Remove(player);
        private static object lifecycle;
        private static System.Reflection.PropertyInfo serverPlayers;
        private static System.Reflection.MethodInfo ownsItem;

        /// <summary>
        /// Whether another player holds or carries the item: the Multiplayer
        /// mod gives the item to that player (ServerPlayer.OwnsItem) and
        /// hides the host's copy until it is dropped or thrown.
        /// </summary>
        private static bool OwnedByPlayer(StartingFuelItem item)
        {
            try
            {
                var id = Id(item);
                if (id == 0) return false;
                if (serverPlayers == null)
                {
                    var type = AccessTools.TypeByName("Multiplayer.Components.Networking.NetworkLifecycle");
                    var instance = type?.BaseType?.GetProperty("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    lifecycle = instance?.GetValue(null);
                    var server = lifecycle?.GetType().GetProperty("Server")?.GetValue(lifecycle);
                    serverPlayers = server?.GetType().GetProperty("ServerPlayers");
                    ownsItem = AccessTools.TypeByName("Multiplayer.Networking.Data.ServerPlayer")?.GetMethod("OwnsItem");
                    if (serverPlayers == null || ownsItem == null) return false;
                }
                var host = lifecycle.GetType().GetProperty("Server")?.GetValue(lifecycle);
                if (!(serverPlayers.GetValue(host) is System.Collections.IEnumerable players)) return false;
                foreach (var player in players)
                    if (player != null && (bool)ownsItem.Invoke(player, new object[] { id })) return true;
            }
            catch (Exception e) { Faults.Report("starting fuel ownership", null, e); }
            return false;
        }

        internal static void Server(IServer server)
        {
            requests.Clear();
            serverPlayers = null;
            StartingFuelItem.HeldRemotely = OwnedByPlayer;
            server.RegisterSerializablePacket<StartingFuelPacket>((p, sender) =>
            {
                try
                {
                    if (p.Action > StartingFuelItem.TakeAction || sender == null) return;
                    if (requests.TryGetValue(sender.PlayerId, out var prior) && !Shed.ShedNet.Newer(p.Seq, prior)) return;
                    requests[sender.PlayerId] = p.Seq;
                    var item = Find(p.Item);
                    if (item == null || item.Consumed || (sender.Position - item.transform.position).sqrMagnitude > 25f) return;
                    // A crate another player holds stays theirs to use.
                    if (p.Action == StartingFuelItem.TakeAction && item.Held) return;
                    item.ApplyAction(p.Action);
                }
                catch (Exception e) { Faults.Report("starting fuel request", null, e); }
            });
        }
        internal static void Client(IClient client)
        {
            snapshots.Clear();
            StartingFuelItem.SendAction = (item, action) =>
            {
                var id = Id(item);
                if (id != 0) client.SendSerializablePacketToServer(new StartingFuelPacket { Item = id, Seq = ++sent, Action = (byte)action });
            };
            client.RegisterSerializablePacket<StartingFuelPacket>(p =>
            {
                try
                {
                    if (p.Action != Snapshot || float.IsNaN(p.Oil) || float.IsInfinity(p.Oil)) return;
                    if (snapshots.TryGetValue(p.Item, out var prior) && !Shed.ShedNet.Newer(p.Seq, prior)) return;
                    var item = Find(p.Item);
                    if (item == null) return;
                    snapshots[p.Item] = p.Seq;
                    item.Rag.Restore(p.Oil, p.Lit); item.Count = Mathf.Clamp(p.Count, 0, 12);
                }
                catch (Exception e) { Faults.Report("starting fuel snapshot", null, e); }
            });
        }
        internal static void Tick(uint tick)
        {
            var api = MultiplayerAPI.Instance;
            if (!Net.InSession || Net.IsClient || api == null || tick % Math.Max(1u, api.TICK_RATE / 2) != 0) return;
            var seq = ++stateSeq;
            foreach (var item in StartingFuelItem.Live)
            {
                if (item == null || item.Consumed) continue;
                var id = Id(item);
                if (id == 0) continue;
                MultiplayerAPI.Server?.SendSerializablePacketToAll(new StartingFuelPacket
                { Item = id, Seq = seq, Action = Snapshot, Oil = item.Rag.Oil, Lit = item.Rag.Lit, Count = item.Count }, reliable: false, excludeSelf: true);
            }
        }
    }
}
