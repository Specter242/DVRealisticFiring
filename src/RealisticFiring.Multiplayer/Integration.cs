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
            api.SetModCompatibility(mod.Info.Id, MultiplayerCompatibility.All);
            Net.ClientCheck = IsClient;
            Net.Sender = Send;
            Net.Broadcaster = Broadcast;
            MultiplayerAPI.ServerStarted += OnServerStarted;
            MultiplayerAPI.ClientStarted += OnClientStarted;
            MultiplayerAPI.ClientStopped += () => helloDue = false;
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

        /// <summary>The settings that shape a loco's sim and controls, and the shovelful, which a client works out itself.</summary>
        internal static string Shape()
        {
            var settings = Main.Settings;
            return string.Join(";", new[]
            {
                $"realistic fireboxes {Main.ReplacementEnabled}",
                $"realistic injectors {Main.RealisticInjectorsEnabled}",
                $"S282 stoker {settings.RetrofitS282Stoker}",
                $"boosters {settings.FitBoosters}",
                $"feed pumps {settings.FitFeedPumps}",
                $"oil burners {settings.RealisticOilBurners}",
                $"realistic cylinders {settings.RealisticCylinders}",
                $"shovelful {settings.ShovelfulMass.ToString("F1", CultureInfo.InvariantCulture)} kg",
            });
        }

        private static void OnServerStarted(IServer server)
        {
            Items.FireIron.RegisterAsGameItem();
            PumpingBy.Clear();
            server.RegisterSerializablePacket<HelloPacket>(OnHello);
            server.RegisterSerializablePacket<FireActionPacket>(OnFireAction);
            snapshotDue = 0u;
            server.OnPlayerDisconnected += OnPlayerLeft;
        }

        private static void OnClientStarted(IClient client)
        {
            Items.FireIron.RegisterAsGameItem();
            client.RegisterSerializablePacket<NoticePacket>(notice => FireController.Announce(notice.Text));
            client.RegisterSerializablePacket<SnapshotPacket>(OnSnapshot);
            client.RegisterSerializablePacket<FireEventPacket>(OnFireEvent);
            helloDue = true;
        }

        private static uint snapshotDue;

        private static void OnTick(uint tick)
        {
            SendSnapshots(tick);
            SendHello();
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
                return;
            }
            var reason = hello.Version != version
                ? $"Realistic Firing {hello.Version} against the host's {version}"
                : $"Realistic Firing settings differ from the host's ({Differences(hello.Shape, shape)})";
            mod.Logger.Warning($"{sender.DisplayName}: {reason}; disconnecting them");
            var server = MultiplayerAPI.Server;
            server?.SendWhisperChatMessage($"{reason}. Match the host's, restart, and rejoin.", sender);
            server?.KickPlayer(sender);
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
            if (!Near(sender, car))
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
                    if (rig == null)
                        break;
                    rig.RemoteHandPumping = action == FireAction.HandPumpOn;
                    if (rig.RemoteHandPumping)
                        PumpingBy[sender.PlayerId] = rig;
                    else
                        PumpingBy.Remove(sender.PlayerId);
                    break;
                case FireAction.Hydrant:
                    if (rig != null)
                        Tell(sender, FireController.ToggleHydrant(rig));
                    break;
                case FireAction.SteamLine:
                    if (RealisticOilFirebox.TryGet(car, out var lined))
                        Tell(sender, FireController.ToggleSteamLine(lined));
                    break;
                case FireAction.Shovelful:
                    firebox?.DeliverShovelful(Mathf.Clamp(packet.Value, 0f, MostCoal), zone);
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
            if (player != null && PumpingBy.TryGetValue(player.PlayerId, out var rig))
            {
                rig.RemoteHandPumping = false;
                PumpingBy.Remove(player.PlayerId);
            }
        }
    }
}
