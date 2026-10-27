using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LocoSim.Implementations;
using LocoSim.Resources;
using MPAPI;
using MPAPI.Interfaces;
using MPAPI.Types;
using RealisticFiring.Core;
using RealisticFiring.Sim;
using UnityEngine;
using UnityModManagerNet;

namespace RealisticFiring.Multiplayer
{
 public static class Integration
    {
        // A player this close to a loco, in m, may work its fire.
        private const float Reach = 30f;

        // Flue sand in one throw, and coal in one shovelful, at most, in kg.
        private const float MostSand = 20f;
        private const float MostCoal = 60f;

        // Clients' views get each loco's state this often, a second, while a
        // player is within this of it, in m.
        private const int SnapshotsPerSecond = 4;
        private const float SnapshotReach = 250f;

        private static UnityModManager.ModEntry mod;
        private static bool helloDue;
        private static readonly Dictionary<byte, InjectorRig> PumpingBy = new Dictionary<byte, InjectorRig>();

        public static void Start(UnityModManager.ModEntry modEntry)
        {
            mod = modEntry;
            var api = MultiplayerAPI.Instance;
            if (api == null)
            {
                mod.Logger.Warning("The Multiplayer mod's API is not available; the fire will not be shared in a session");
                return;
            }
            CabControlsCompatibility.Install(mod.Info.Id + ".cab-controls", mod.Logger.Log, mod.Logger.Warning);
            api.SetModCompatibility(mod.Info.Id, MultiplayerCompatibility.All);
            Net.ClientCheck = IsClient;
            Net.Sender = Send;
            Net.Broadcaster = Broadcast;
            StandSync.Start();
            MultiplayerAPI.ServerStarted += OnServerStarted;
            MultiplayerAPI.ClientStarted += OnClientStarted;
            MultiplayerAPI.ClientStopped += () =>
            {
                helloDue = false;
                Shed.ShedNet.Reset();
            };
            api.OnTick += OnTick;
            if (MultiplayerAPI.Server != null)
                OnServerStarted(MultiplayerAPI.Server);
            if (MultiplayerAPI.Client != null)
                OnClientStarted(MultiplayerAPI.Client);
            mod.Logger.Log($"Multiplayer support on (Multiplayer {api.MultiplayerVersion}, its API {MultiplayerAPI.LoadedApiVersion})");
        }

        private static bool IsClient()
        {
            var api = MultiplayerAPI.Instance;
            return api != null && api.IsConnected && !api.IsHost;
        }

        /// <summary>
        /// The settings that shape a loco's sim, controls, and fittings, as
        /// this session was built with them (a change waits for a restart),
        /// and the cylinders and shovelful, which a client works out itself.
        /// </summary>
        internal static string Shape()
        {
            var settings = Main.Settings;
            var session = Main.Session;
            return string.Join(";", new[]
            {
                $"realistic fireboxes {session.RealisticFireboxes}",
                $"realistic injectors {session.RealisticInjectors}",
                $"injector steam valves {session.InjectorSteamValves}",
                $"fusible plugs {session.FusiblePlugs}",
                $"S282 stoker {session.RetrofitS282Stoker}",
                $"stoker jams {session.StokerJams}",
                $"boosters {session.FitBoosters}",
                $"feed pumps {session.FitFeedPumps}",
                $"oil burners {session.RealisticOilBurners}",
                $"realistic cylinders {settings.RealisticCylinders}",
                $"shovelful {settings.Shovelful.ToString("F1", CultureInfo.InvariantCulture)} kg",
            });
        }

        private static void OnServerStarted(IServer server)
        {
            Items.FireIron.RegisterAsGameItem();
            Items.FiringTablet.RegisterAsGameItem();
            PumpingBy.Clear();
            server.RegisterSerializablePacket<HelloPacket>(OnHello);
            server.RegisterSerializablePacket<FireActionPacket>(OnFireAction);
            StandSync.OnServerStarted(server);
            snapshotDue = 0u;
            Leaving.Clear();
            server.OnPlayerDisconnected += OnPlayerLeft;
        }

        private static void OnClientStarted(IClient client)
        {
            Items.FireIron.RegisterAsGameItem();
            Items.FiringTablet.RegisterAsGameItem();
            client.RegisterSerializablePacket<NoticePacket>(notice => FireController.Announce(notice.Text));
            client.RegisterSerializablePacket<SnapshotPacket>(OnSnapshot);
            client.RegisterSerializablePacket<FireEventPacket>(OnFireEvent);
            StandSync.OnClientStarted(client);
            helloDue = true;
        }

        private static uint snapshotDue;

        private static void OnTick(uint tick)
        {
            SendSnapshots(tick);
            SendHello();
            WarnAndDisconnect(tick);
        }

        // A player who joined with another version or other settings: told why
        // on screen for a while (a notice lasts 4 s, so it is sent again), then
        // disconnected. Kicked at once, they saw only "You were kicked!", the
        // whispered reason gone with them (a player, 2026-10-14).
        private sealed class Leaver
        {
            public IPlayer Player;
            public string Reason;
            public uint Next;
            public uint Kick;
        }

        private static readonly List<Leaver> Leaving = new List<Leaver>();
        private const float WarningSeconds = 12f;
        private const float NoticeEvery = 3f;

        private static void WarnAndDisconnect(uint tick)
        {
            var api = MultiplayerAPI.Instance;
            var server = MultiplayerAPI.Server;
            if (Leaving.Count == 0 || api == null || server == null)
                return;
            foreach (var leaver in Leaving.ToList())
            {
                if (tick >= leaver.Kick)
                {
                    Leaving.Remove(leaver);
                    server.KickPlayer(leaver.Player);
                }
                else if (tick >= leaver.Next)
                {
                    leaver.Next = tick + (uint)Math.Max(1f, NoticeEvery * api.TICK_RATE);
                    server.SendSerializablePacketToPlayer(new NoticePacket { Text = leaver.Reason }, leaver.Player);
                }
            }
        }

        // On the host: each loco a player is near, a few times a second.
        private static void SendSnapshots(uint tick)
        {
            var api = MultiplayerAPI.Instance;
            var server = MultiplayerAPI.Server;
            if (api == null || server == null || !api.IsHost || server.PlayerCount < 2 || tick < snapshotDue)
                return;
            snapshotDue = tick + Math.Max(1u, api.TICK_RATE / SnapshotsPerSecond);
            foreach (var car in ClientMirror.Cars())
            {
                if (server.AnyPlayerSqrMag(car.gameObject) > SnapshotReach * SnapshotReach || !api.TryGetNetId(car, out ushort netId))
                    continue;
                var data = ClientMirror.Capture(car);
                if (data != null)
                    server.SendSerializablePacketToAll(new SnapshotPacket { Car = netId, Data = data }, reliable: false, excludeSelf: true);
            }
        }

        private static void OnSnapshot(SnapshotPacket packet)
        {
            if (MultiplayerAPI.Instance != null && MultiplayerAPI.Instance.TryGetObjectFromNetId(packet.Car, out TrainCar car) && car != null)
                ClientMirror.Apply(car, packet.Data);
        }

        // On the host: what a loco's fire did, for everyone else.
        private static void Broadcast(TrainCar car, FireEvent fireEvent, float value)
        {
            var api = MultiplayerAPI.Instance;
            var server = MultiplayerAPI.Server;
            if (api == null || server == null || !api.IsHost || server.PlayerCount < 2 || !api.TryGetNetId(car, out ushort netId))
                return;
            server.SendSerializablePacketToAll(new FireEventPacket { Car = netId, Event = (byte)fireEvent, Value = value }, reliable: true, excludeSelf: true);
        }

        private static void OnFireEvent(FireEventPacket packet)
        {
            if (MultiplayerAPI.Instance != null && MultiplayerAPI.Instance.TryGetObjectFromNetId(packet.Car, out TrainCar car) && car != null)
                Net.Receive(car, (FireEvent)packet.Event, packet.Value);
        }

        // The hello goes once the client is connected; the host needs none.
        private static void SendHello()
        {
            if (!helloDue)
                return;
            var client = MultiplayerAPI.Client;
            if (client == null || !client.IsConnected)
                return;
            helloDue = false;
            if (MultiplayerAPI.Instance == null || MultiplayerAPI.Instance.IsHost)
                return;
            client.SendSerializablePacketToServer(new HelloPacket { Version = mod.Info.Version, Shape = Shape() });
        }

        private static void OnHello(HelloPacket hello, IPlayer sender)
        {
            if (sender == null || sender.IsHost)
                return;
            var version = mod.Info.Version;
            var shape = Shape();
            if (hello.Version == version && hello.Shape == shape)
            {
                mod.Logger.Log($"{sender.DisplayName} joined with Realistic Firing {hello.Version}, settings matching");
                StandSync.SendStands(sender);
                return;
            }
            var reason = hello.Version != version
                ? $"Realistic Firing {hello.Version} against the host's {version}"
                : $"Realistic Firing settings differ from the host's ({Differences(hello.Shape, shape)})";
            mod.Logger.Warning($"{sender.DisplayName}: {reason}; disconnecting them");
            var server = MultiplayerAPI.Server;
            var tell = $"{reason}. Match the host's, restart, and rejoin. Disconnecting in {WarningSeconds:F0} s.";
            server?.SendWhisperChatMessage(tell, sender);
            FireController.Announce($"{sender.DisplayName}: {reason}; disconnecting them in {WarningSeconds:F0} s");
            var api = MultiplayerAPI.Instance;
            var now = api != null ? api.CurrentTick : 0u;
            var rate = api != null ? api.TICK_RATE : 1u;
            Leaving.RemoveAll(l => l.Player == sender);
            Leaving.Add(new Leaver { Player = sender, Reason = tell, Next = now, Kick = now + (uint)(WarningSeconds * rate) });
        }

        private static string Differences(string theirs, string ours)
        {
            var mine = ours.Split(';');
            var other = (theirs ?? "").Split(';');
            return string.Join(", ", mine.Zip(other, (a, b) => a == b ? null : $"{b}, host {a}").Where(d => d != null));
        }

        // On a client: what the player did to a fire, for the host.
        private static void Send(TrainCar car, FireAction action, int zone, int direction, float value)
        {
            var api = MultiplayerAPI.Instance;
            var client = MultiplayerAPI.Client;
            if (api == null || client == null || !api.TryGetNetId(car, out ushort netId))
            {
                mod.Logger.Warning($"{car?.ID}: could not send {action} to the host (no network ID for the car)");
                return;
            }
            client.SendSerializablePacketToServer(new FireActionPacket
            {
                Car = netId,
                Action = (byte)action,
                Zone = (sbyte)Mathf.Clamp(zone, -1, GrateLayout.ZoneCount - 1),
                Direction = (sbyte)Mathf.Clamp(direction, -1, 3),
                Value = value,
            });
        }

        // On the host: a client's action, carried out with the same methods
        // the host's own player uses, for a player on or near the loco.
        private static void OnFireAction(FireActionPacket packet, IPlayer sender)
        {
            var api = MultiplayerAPI.Instance;
            if (sender == null || api == null || !api.TryGetObjectFromNetId(packet.Car, out TrainCar car) || car == null)
                return;
            // Letting go of the hand pump is always allowed, from anywhere.
            if ((FireAction)packet.Action != FireAction.HandPumpOff && !Near(sender, car))
            {
                mod.Logger.Warning($"{sender.DisplayName} tried to work {car.ID}'s fire from too far away");
                return;
            }
            var action = (FireAction)packet.Action;
            var zone = packet.Zone >= 0 && packet.Zone < GrateLayout.ZoneCount ? (GrateZone?)packet.Zone : null;
            FireboxRegistry.TryGet(car, out var firebox);
            InjectorRig.TryGet(car, out var rig);
            switch (action)
            {
                case FireAction.Shake:
                    firebox?.ShakeGrate();
                    break;
                case FireAction.Wiggle:
                    firebox?.ShakeGrate(ShakeStroke.Wiggle);
                    break;
                case FireAction.Rake when zone.HasValue:
                    firebox?.Rake(zone.Value);
                    break;
                case FireAction.Slice when zone.HasValue:
                    firebox?.Slice(zone.Value);
                    break;
                case FireAction.Push when zone.HasValue && Enum.IsDefined(typeof(GrateDirection), (int)packet.Direction):
                    firebox?.Push(zone.Value, (GrateDirection)packet.Direction);
                    break;
                case FireAction.HandPumpOn:
                case FireAction.HandPumpOff:
                    // A player pumps one boiler at a time: any other they were
                    // pumping is let go, and only their own hand comes off.
                    if (PumpingBy.TryGetValue(sender.PlayerId, out var pumped) && (pumped != rig || action == FireAction.HandPumpOff))
                    {
                        pumped.RemotePump(sender.PlayerId, false);
                        PumpingBy.Remove(sender.PlayerId);
                    }
                    if (action == FireAction.HandPumpOn && rig != null)
                    {
                        rig.RemotePump(sender.PlayerId, true);
                        PumpingBy[sender.PlayerId] = rig;
                    }
                    break;
                case FireAction.Hydrant:
                    // An old client's key: the stand's water hose does it now.
                    Tell(sender, FireController.HydrantHint);
                    break;
                case FireAction.SteamLine:
                    if (RealisticOilFirebox.TryGet(car, out var lined))
                        Tell(sender, FireController.ToggleSteamLine(lined));
                    break;
                case FireAction.Shovelful:
                    firebox?.DeliverShovelful(Mathf.Clamp(packet.Value, 0f, MostCoal), zone);
                    break;
                case FireAction.FiremanMode when FiremanRig.TryGet(car, out var fireman)
                                                 && Enum.IsDefined(typeof(FiremanMode), (int)packet.Value):
                    fireman.SetMode((FiremanMode)(int)packet.Value);
                    Tell(sender, $"Fireman: {(FiremanMode)(int)packet.Value}");
                    break;
                case FireAction.FiremanCall when FiremanRig.TryGet(car, out var called)
                                                 && Enum.IsDefined(typeof(FiremanCall), (int)packet.Value):
                    called.Fireman.Call = (FiremanCall)(int)packet.Value;
                    break;
                case FireAction.Sand:
                    if (RealisticOilFirebox.TryGet(car, out var oil))
                    {
                        var thrown = TakeSand(car, Mathf.Clamp(packet.Value, 0f, MostSand));
                        if (thrown > 0f)
                            oil.ThrowSand(thrown);
                    }
                    break;
            }
        }

        private static void Tell(IPlayer player, string text)
        {
            if (!string.IsNullOrEmpty(text))
                MultiplayerAPI.Server?.SendSerializablePacketToPlayer(new NoticePacket { Text = text }, player);
        }

        // On the loco, on a car coupled to it (its tender), or within reach.
        private static bool Near(IPlayer player, TrainCar car)
        {
            var on = player.OccupiedCar;
            if (on == car)
                return true;
            if (on != null && new[] { car.frontCoupler, car.rearCoupler }.Any(c => c != null && c.coupledTo != null && c.coupledTo.train == on))
                return true;
            return (player.Position - car.transform.position).sqrMagnitude <= Reach * Reach;
        }

        // The loco's own sand, taken on the host for a client's throw.
        private static float TakeSand(TrainCar car, float kg)
        {
            var flow = car.SimController != null ? car.SimController.SimulationFlow : null;
            var sand = flow?.OrderedSimComps.OfType<ResourceContainer>().FirstOrDefault(c => c.resourceType == ResourceContainerType.SAND);
            if (sand == null)
                return 0f;
            var taken = Mathf.Min(kg, Mathf.Max(0f, sand.amountReadOut.Value));
            if (taken > 0f)
                sand.consumeExtIn.Value = taken;
            return taken;
        }

        // A player who leaves lets go of the hand pump.
        private static void OnPlayerLeft(IPlayer player)
        {
            Leaving.RemoveAll(l => l.Player == player);
            StandSync.PlayerLeft(player);
            if (player != null && PumpingBy.TryGetValue(player.PlayerId, out var rig))
            {
                rig.RemotePump(player.PlayerId, false);
                PumpingBy.Remove(player.PlayerId);
            }
        }
    }
}
