using System;
using System.Collections.Generic;
using System.Linq;
using DV.CabControls;
using HarmonyLib;
using UnityEngine;
using VerletRope;

namespace RealisticFiring.Shed
{
    /// <summary>Where a shed stand's plug is.</summary>
    internal enum PlugPlace : byte
    {
        /// <summary>Hung on its stand.</summary>
        OnStand,

        /// <summary>In a player's hand.</summary>
        Held,

        /// <summary>Let go, on the ground or falling.</summary>
        Loose,

        /// <summary>In an engine's blower line connection.</summary>
        InEngine,
    }

    /// <summary>
    /// A stand's plug as one machine in a session tells the others: where it
    /// is, who holds it, the engine it is in (by its network ID), and its
    /// pose, absolute (the world's, less its origin shift). A loose plug is
    /// settled once it has come to rest on the machine that let it go.
    /// </summary>
    internal struct PlugReport
    {
        public PlugPlace Place;
        public byte Holder;
        public ushort Car;
        public Vector3 Position;
        public Quaternion Rotation;
        public bool Settled;

        /// <summary>
        /// Its sender's count of what it has said of this stand's plug, one
        /// more each time (wrapping): the Multiplayer mod delivers reliably
        /// but not in order, so one older than the last heard is dropped. A
        /// pose carries the count of the word it follows.
        /// </summary>
        public ushort Seq;

        /// <summary>The same place, holder, and engine, its pose aside.</summary>
        public bool SamePlace(PlugReport other) =>
            Place == other.Place
            && (Place != PlugPlace.Held || Holder == other.Holder)
            && (Place != PlugPlace.InEngine || Car == other.Car);

        public override string ToString() =>
            Place == PlugPlace.Held ? $"held by player {Holder}"
            : Place == PlugPlace.InEngine ? $"in engine {Car}"
            : Place == PlugPlace.Loose ? (Settled ? "loose, at rest" : "loose")
            : "on its stand";
    }

    /// <summary>Who the host passes a client's word on to.</summary>
    internal enum Relay
    {
        /// <summary>No one: it was old, or of a stand not here.</summary>
        None,

        /// <summary>The other clients: taken as it was.</summary>
        Others,

        /// <summary>Every client, the sender too: refused or changed here, and this is how it is.</summary>
        Everyone,
    }

 internal static class ShedNet
    {
        /// <summary>Whether this game is in a Multiplayer session, host or client; set by the add-on.</summary>
        internal static Func<bool> SessionCheck;

        /// <summary>This machine's player in the session; set by the add-on.</summary>
        internal static Func<byte?> LocalPlayerId;

        /// <summary>A car's network ID, 0 for none; set by the add-on.</summary>
        internal static Func<TrainCar, ushort> CarId;

        /// <summary>The car with a network ID, or null; set by the add-on.</summary>
        internal static Func<ushort, TrainCar> CarOf;

        /// <summary>
        /// Sends a stand's plug (key, report, a pose only): from a client to
        /// the host, from the host to every client. Set by the add-on.
        /// </summary>
        internal static Action<string, PlugReport, bool> PlugSender;

        /// <summary>Sends a stand's valve opening and its count (key, opening, count), as <see cref="PlugSender"/>. Set by the add-on.</summary>
        internal static Action<string, float, ushort> ValveSender;

        // A held or falling plug's pose goes this often, s; a turning
        // valve's opening at most this often. A valve set from the network
        // is left this long to settle before its own changes are sent again.
        private const float PoseEvery = 0.1f;
        private const float ValveEvery = 0.125f;
        private const float ValveQuiet = 0.4f;
        private const float ValveStep = 0.003f;

        // A plug let go is sent as it falls for this long at most, s.
        private const float SettleFor = 5f;

        // A player this near a stand's plug or valve, m, may take it or turn
        // it; one putting a plug down, this near its hose's reach.
        private const float Reach = 30f;

        // A plug following another's hand eases to it at this rate, 1/s,
        // and jumps to it from further than this, m.
        private const float FollowRate = 15f;
        private const float FollowJump = 3f;

        // The host takes a plug from another's hand pulled this much past
        // the hose's reach, the holder's own machine having the first go.
        private const float HostSlack = 1.1f;

        // A client puts its plug back where the host has it this often, s,
        // where it has gone astray (an engine not yet loaded, a stand just
        // built).
        private const float ReconcileEvery = 1f;

        // A plug let go as the network moves it: not taken for the hand's
        // own for this long, s, while the hand lets go.
        private const float YankSettle = 0.3f;

        // A plug left beyond its hose's reach goes home after this long, s (PropHose).
        private const float HomeAfter = 2f;

        private sealed class Stand
        {
            public string Key;

            // A valve with no hose of its own: a repair stand's direct steam,
            // on the same hose as its water.
            public bool ValveOnly;
            public PropHose Hose;
            public PluggableObject Plug;
            public PlugSocket Home;
            public Transform Wheel;
            public ControlImplBase Valve;
            public Rigidbody[] Bodies;
            public Collider[] Colliders;
            public bool[] CollidersOn;
            public float RopeLength;

            // The plug as this machine last told or was told.
            public PlugReport Known = OnStand;
            public bool Following;
            public PlugReport Target;
            public bool Settling;
            public float SettleUntil;
            public byte? LooseBy;
            public float PoseAt;
            public float ReconcileAt;
            public float TimeOutside;
            public float YankedUntil;

            // The counts of words on its plug and valve: this machine's
            // own, the host's last heard here (a client), and each client's
            // last heard (the host).
            public ushort Sent;
            public ushort? Heard;
            public readonly Dictionary<byte, ushort> HeardFrom = new Dictionary<byte, ushort>();
            public ushort ValveSent;
            public ushort? ValveHeard;
            public readonly Dictionary<byte, ushort> ValveHeardFrom = new Dictionary<byte, ushort>();

            public float KnownValve;
            public float? PendingValve;
            public float ValveAt;
            public float ValveQuietUntil;

            public float Reach => RopeLength * (Hose != null ? Hose.maxLengthMultiplier : 1.25f);
        }

        private static readonly PlugReport OnStand = new PlugReport { Place = PlugPlace.OnStand, Rotation = Quaternion.identity };

        private static readonly Dictionary<string, Stand> Stands = new Dictionary<string, Stand>();
        private static readonly Dictionary<PropHose, Stand> ByHose = new Dictionary<PropHose, Stand>();
        private static readonly Dictionary<PluggableObject, Stand> ByPlug = new Dictionary<PluggableObject, Stand>();
        private static readonly List<Stand> Each = new List<Stand>();

        // What the host sent for stands this machine has not built yet.
        private static readonly Dictionary<string, PlugReport> EarlyPlugs = new Dictionary<string, PlugReport>();
        private static readonly Dictionary<string, (float Value, ushort Seq)> EarlyValves = new Dictionary<string, (float, ushort)>();

        private static bool wasInSession;

        /// <summary>Whether this game is in a Multiplayer session.</summary>
        public static bool InSession => SessionCheck != null && PlugSender != null && SessionCheck();

        /// <summary>Whether count <paramref name="a"/> comes after <paramref name="b"/>, wrapping.</summary>
        internal static bool Newer(ushort a, ushort b) => (short)(a - b) > 0;

        /// <summary>A stand built here, its hose and valve wheel, under its track's <paramref name="key"/>.</summary>
        public static void Register(string key, PropHose hose, Transform wheel)
        {
            if (string.IsNullOrEmpty(key) || hose == null || hose.plug == null)
                return;
            var rope = hose.GetComponent<RopeBehaviour>();
            var stand = new Stand
            {
                Key = key,
                Hose = hose,
                Plug = hose.plug,
                Home = hose.stationSocket,
                Wheel = wheel,
                Bodies = hose.plug.GetComponentsInChildren<Rigidbody>(true),
                Colliders = hose.plug.GetComponentsInChildren<Collider>(true),
                RopeLength = rope != null ? rope.ropeParams.ropeLength : (HoseKind.ByTag(hose.plug.connectionTag) ?? HoseKind.BlowerLine).Length,
            };
            stand.CollidersOn = new bool[stand.Colliders.Length];
            if (Stands.TryGetValue(key, out var old))
                Forget(old);
            Stands[key] = stand;
            ByHose[hose] = stand;
            ByPlug[hose.plug] = stand;
            Each.Add(stand);
            if (EarlyPlugs.TryGetValue(key, out var early))
            {
                EarlyPlugs.Remove(key);
                stand.Known = early;
                stand.Heard = early.Seq;
            }
            if (EarlyValves.TryGetValue(key, out var valve))
            {
                EarlyValves.Remove(key);
                stand.PendingValve = valve.Value;
                stand.ValveHeard = valve.Seq;
            }
        }

        /// <summary>A valve with no hose of its own built here, its wheel, under <paramref name="key"/>.</summary>
        public static void RegisterValve(string key, Transform wheel)
        {
            if (string.IsNullOrEmpty(key) || wheel == null)
                return;
            var stand = new Stand { Key = key, ValveOnly = true, Wheel = wheel };
            if (Stands.TryGetValue(key, out var old))
                Forget(old);
            Stands[key] = stand;
            Each.Add(stand);
            if (EarlyValves.TryGetValue(key, out var valve))
            {
                EarlyValves.Remove(key);
                stand.PendingValve = valve.Value;
                stand.ValveHeard = valve.Seq;
            }
        }

        private static void Forget(Stand stand)
        {
            Each.Remove(stand);
            if (stand.Hose != null)
                ByHose.Remove(stand.Hose);
            if (stand.Plug != null)
                ByPlug.Remove(stand.Plug);
        }

        /// <summary>Whether <paramref name="plug"/> follows another machine's report here (held there, or falling there).</summary>
        public static bool IsFollowing(PluggableObject plug) =>
            plug != null && ByPlug.TryGetValue(plug, out var stand) && stand.Following;

        /// <summary>Keeps the stands the same on every machine of a session; call every frame.</summary>
        public static void Update()
        {
            if (Each.Count == 0)
                return;
            var inSession = InSession;
            if (inSession != wasInSession)
            {
                wasInSession = inSession;
                OnSessionChanged(inSession);
            }
            if (!inSession)
                return;
            var now = Time.unscaledTime;
            var me = LocalPlayerId?.Invoke();
            var client = Net.IsClient;
            for (var i = Each.Count - 1; i >= 0; i--)
            {
                var stand = Each[i];
                if (stand.ValveOnly ? stand.Wheel == null : stand.Hose == null || stand.Plug == null)
                {
                    Stands.Remove(stand.Key);
                    Forget(stand);
                    continue;
                }
                if (stand.ValveOnly)
                {
                    SyncValve(stand, now);
                    continue;
                }
                Follow(stand);
                SyncValve(stand, now);
                SyncPlug(stand, now, client, me);
            }
        }

        // Into a session the host's stands are as it finds them, a client's
        // as the host has told it (EarlyPlugs, kept); out of one, each
        // stand is its own again.
        private static void OnSessionChanged(bool inSession)
        {
            if (inSession && Net.IsClient)
                return;
            if (!inSession)
                Reset();
            var me = LocalPlayerId?.Invoke();
            foreach (var stand in Each)
            {
                if (stand.Plug == null && !stand.ValveOnly)
                    continue;
                if (!stand.ValveOnly)
                    stand.Known = Observe(stand, me) ?? OnStand;
                stand.KnownValve = stand.Valve != null ? stand.Valve.Value : 0f;
            }
        }

        /// <summary>
        /// Forgets what other machines said: as a session starts or ends.
        /// A plug following another's hand is let go where it is.
        /// </summary>
        public static void Reset()
        {
            EarlyPlugs.Clear();
            EarlyValves.Clear();
            foreach (var stand in Each)
            {
                if (stand.ValveOnly)
                {
                    stand.ValveHeard = null;
                    stand.ValveHeardFrom.Clear();
                    continue;
                }
                if (stand.Plug == null)
                    continue;
                StopFollowing(stand);
                stand.Settling = false;
                stand.LooseBy = null;
                stand.Heard = stand.ValveHeard = null;
                stand.HeardFrom.Clear();
                stand.ValveHeardFrom.Clear();
            }
        }

        // ---- The plug ----

        private static bool IsMine(PlugReport report, byte? me) =>
            report.Place == PlugPlace.Held && me.HasValue && report.Holder == me.Value;

        private static PlugReport WithPose(PlugReport report, PluggableObject plug)
        {
            report.Position = plug.transform.position - WorldMover.currentMove;
            report.Rotation = plug.transform.rotation;
            return report;
        }

        // Where the plug is here, or null while it snaps into a socket.
        private static PlugReport? Observe(Stand stand, byte? me)
        {
            var plug = stand.Plug;
            if (plug.IsHeldInHand)
                return me.HasValue ? WithPose(new PlugReport { Place = PlugPlace.Held, Holder = me.Value }, plug) : (PlugReport?)null;
            if (stand.Following)
                return stand.Known;
            switch (plug.State)
            {
                case PluggableObject.PluggableState.Snapping:
                    return null;
                case PluggableObject.PluggableState.PluggedIn:
                    if (plug.Socket == stand.Home)
                        return OnStand;
                    var car = ShedConnections.CarAt(plug.Socket);
                    var id = car != null && CarId != null ? CarId(car) : (ushort)0;
                    return id != 0 ? WithPose(new PlugReport { Place = PlugPlace.InEngine, Car = id }, plug) : (PlugReport?)null;
                default:
                    return WithPose(new PlugReport { Place = PlugPlace.Loose, Settled = stand.Known.Settled }, plug);
            }
        }

        private static void SyncPlug(Stand stand, float now, bool client, byte? me)
        {
            var observed = Observe(stand, me);
            if (!observed.HasValue)
                return;
            var seen = observed.Value;
            var mine = IsMine(seen, me) && now >= stand.YankedUntil;
            var wasMine = IsMine(stand.Known, me);
            if (client)
            {
                // A client tells what its own hand does, and where a plug it
                // let go comes to; the rest is the host's, put back here
                // where it has gone astray.
                var owner = wasMine || stand.Settling;
                if (mine && !wasMine)
                    Report(stand, seen, now, me);
                else if (owner && !mine && !seen.SamePlace(stand.Known))
                    Report(stand, seen, now, me);
                else if (!owner && !mine && !seen.SamePlace(stand.Known) && now >= stand.ReconcileAt)
                    Apply(stand, stand.Known, me);
            }
            else if (!seen.SamePlace(stand.Known) && (mine || !IsMine(seen, me)))
            {
                // The host tells everything that happens to its plugs.
                Report(stand, seen, now, me);
            }
            Stream(stand, now, me);
        }

        // This machine's change, for the others.
        private static void Report(Stand stand, PlugReport report, float now, byte? me)
        {
            if (report.Place == PlugPlace.Loose)
            {
                report.Settled = false;
                stand.Settling = true;
                stand.SettleUntil = now + SettleFor;
                stand.LooseBy = me;
            }
            else
            {
                stand.Settling = false;
                stand.LooseBy = null;
            }
            stand.Known = report;
            stand.PoseAt = now + PoseEvery;
            stand.TimeOutside = 0f;
            Say(stand, $"{report} here, sent");
            Send(stand, report);
        }

        private static void Send(Stand stand, PlugReport report)
        {
            report.Seq = ++stand.Sent;
            PlugSender?.Invoke(stand.Key, report, false);
        }

        private static void SendPose(Stand stand, PlugReport report)
        {
            report.Seq = stand.Sent;
            PlugSender?.Invoke(stand.Key, report, true);
        }

        // A held plug's pose, and a plug let go here as it falls, until it rests.
        private static void Stream(Stand stand, float now, byte? me)
        {
            if (now < stand.PoseAt)
                return;
            if (IsMine(stand.Known, me))
            {
                stand.PoseAt = now + PoseEvery;
                SendPose(stand, WithPose(stand.Known, stand.Plug));
            }
            else if (stand.Settling && stand.Known.Place == PlugPlace.Loose)
            {
                stand.PoseAt = now + PoseEvery;
                var report = WithPose(stand.Known, stand.Plug);
                if (Moving(stand) && now < stand.SettleUntil)
                {
                    SendPose(stand, report);
                    return;
                }
                report.Settled = true;
                stand.Known = report;
                stand.Settling = false;
                Send(stand, report);
            }
        }

        private static bool Moving(Stand stand)
        {
            var body = stand.Plug.GetComponent<Rigidbody>();
            return body != null && !body.isKinematic && !body.IsSleeping()
                   && (body.velocity.sqrMagnitude > 0.0004f || body.angularVelocity.sqrMagnitude > 0.01f);
        }

        // Puts the plug where a report has it. False where it cannot be yet
        // (its engine not loaded here, or its connection taken).
        private static bool Apply(Stand stand, PlugReport report, byte? me)
        {
            var plug = stand.Plug;
            var now = Time.unscaledTime;
            stand.Settling = false;
            stand.ReconcileAt = now + ReconcileEvery;
            stand.TimeOutside = 0f;
            var mineNow = IsMine(report, me);
            if (plug.IsHeldInHand && !mineNow)
            {
                plug.YankOutOfHand();
                stand.YankedUntil = now + YankSettle;
            }
            stand.Known = report;
            if (mineNow)
                return true;
            var follow = report.Place == PlugPlace.Held || report.Place == PlugPlace.Loose && !report.Settled;
            if (!follow)
                StopFollowing(stand);
            var done = true;
            switch (report.Place)
            {
                case PlugPlace.OnStand:
                    done = Into(plug, stand.Home);
                    break;
                case PlugPlace.InEngine:
                    var car = CarOf?.Invoke(report.Car);
                    var socket = car != null ? ShedConnections.SocketOf(car, plug.connectionTag) : null;
                    done = socket != null && Into(plug, socket);
                    break;
                default:
                    plug.Unplug();
                    if (follow)
                        StartFollowing(stand, report);
                    else
                        Put(stand, report);
                    break;
            }
            Say(stand, $"{report}, as told" + (done ? "" : " (not yet: its engine or connection is not here)"));
            return done;
        }

        // Into a socket, out of any other first.
        private static bool Into(PluggableObject plug, PlugSocket socket)
        {
            if (socket == null)
                return false;
            if (plug.State == PluggableObject.PluggableState.PluggedIn && plug.Socket == socket)
                return true;
            if (socket.Plug != null && socket.Plug != plug)
                return false;
            plug.Unplug();
            return Snap(plug, socket);
        }

        /// <summary>
        /// Puts a free plug straight into <paramref name="socket"/>, as the
        /// game sends a station's plug home, whether or not the plug takes
        /// sockets out of the hand. False where the socket will not take it.
        /// </summary>
        public static bool Snap(PluggableObject plug, PlugSocket socket)
        {
            if (plug == null || socket == null)
                return false;
            var handsFree = plug.allowHandsFreePlugging;
            plug.allowHandsFreePlugging = true;
            try
            {
                return plug.InstantSnapTo(socket);
            }
            finally
            {
                plug.allowHandsFreePlugging = handsFree;
            }
        }

        // A loose plug set down where another machine has it at rest.
        private static void Put(Stand stand, PlugReport report)
        {
            var at = report.Position + WorldMover.currentMove;
            stand.Plug.transform.SetPositionAndRotation(at, report.Rotation);
            foreach (var body in stand.Bodies)
            {
                if (body == null)
                    continue;
                if (!body.isKinematic)
                {
                    body.velocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
                body.WakeUp();
            }
            var root = stand.Plug.GetComponent<Rigidbody>();
            if (root != null)
            {
                root.position = at;
                root.rotation = report.Rotation;
            }
        }

        // Following another machine's plug: carried as reported, neither
        // grabbed nor caught by a socket here.
        private static void StartFollowing(Stand stand, PlugReport report)
        {
            stand.Target = report;
            if (stand.Following)
                return;
            stand.Following = true;
            foreach (var body in stand.Bodies)
            {
                if (body != null)
                    body.isKinematic = true;
            }
            for (var i = 0; i < stand.Colliders.Length; i++)
            {
                var collider = stand.Colliders[i];
                stand.CollidersOn[i] = collider != null && collider.enabled;
                if (collider != null)
                    collider.enabled = false;
            }
            Follow(stand);
        }

        private static void StopFollowing(Stand stand)
        {
            if (!stand.Following)
                return;
            stand.Following = false;
            for (var i = 0; i < stand.Colliders.Length; i++)
            {
                if (stand.Colliders[i] != null && stand.CollidersOn[i])
                    stand.Colliders[i].enabled = true;
            }
            if (stand.Plug.State == PluggableObject.PluggableState.Free && !stand.Plug.IsHeldInHand)
            {
                foreach (var body in stand.Bodies)
                {
                    if (body != null)
                        body.isKinematic = false;
                }
            }
        }

        private static void Follow(Stand stand)
        {
            if (!stand.Following)
                return;
            var plug = stand.Plug.transform;
            var at = stand.Target.Position + WorldMover.currentMove;
            if ((plug.position - at).sqrMagnitude > FollowJump * FollowJump)
            {
                plug.SetPositionAndRotation(at, stand.Target.Rotation);
                return;
            }
            var ease = 1f - Mathf.Exp(-FollowRate * Time.deltaTime);
            plug.SetPositionAndRotation(Vector3.Lerp(plug.position, at, ease), Quaternion.Slerp(plug.rotation, stand.Target.Rotation, ease));
        }

        // A pose for a plug already where the report says.
        private static void OnPose(Stand stand, PlugReport report)
        {
            if (stand.Following && report.SamePlace(stand.Known) && !(report.Place == PlugPlace.Loose && stand.Known.Settled))
                stand.Target = report;
        }

        private static PlugReport Current(Stand stand) =>
            stand.Known.Place == PlugPlace.OnStand ? stand.Known : WithPose(stand.Known, stand.Plug);

        // ---- The hose's reach (PropHose.Update in a session) ----

        /// <summary>
        /// A stand's hose pulled too far in a session: the game's own check
        /// (<c>PropHose.Update</c>), which the Multiplayer mod leaves to the
        /// host and to hoses it networks, done here for ours. The hand on
        /// this machine lets go of a plug pulled past the hose's reach; the
        /// host pulls one out of an engine moved off and sends a plug left
        /// beyond reach home. True where it was a stand's hose in a session.
        /// </summary>
        internal static bool Tether(PropHose hose)
        {
            if (ByHose.Count == 0 || !ByHose.TryGetValue(hose, out var stand) || !InSession)
                return false;
            var plug = stand.Plug;
            if (plug == null || hose.hoseStationOrigin == null)
                return true;
            var reach = stand.Reach;
            var distance = Vector3.Distance(hose.hoseStationOrigin.position, plug.transform.position);
            if (distance <= reach)
            {
                stand.TimeOutside = 0f;
                return true;
            }
            if (plug.IsHeldInHand)
            {
                plug.Unplug();
                plug.YankOutOfHand();
                Pull(stand, hose);
                return true;
            }
            if (Net.IsClient)
                return true;
            if (stand.Following && stand.Known.Place == PlugPlace.Held)
            {
                if (distance > reach * HostSlack)
                {
                    // Out of a client's hand: the host lets it fall.
                    StopFollowing(stand);
                    Pull(stand, hose);
                    Report(stand, WithPose(new PlugReport { Place = PlugPlace.Loose }, plug), Time.unscaledTime, LocalPlayerId?.Invoke());
                }
                return true;
            }
            if (plug.State == PluggableObject.PluggableState.PluggedIn)
            {
                plug.Unplug();
                Pull(stand, hose);
                return true;
            }
            stand.TimeOutside += Time.deltaTime;
            if (stand.TimeOutside > HomeAfter)
            {
                stand.TimeOutside = 0f;
                StopFollowing(stand);
                Snap(plug, stand.Home);
            }
            return true;
        }

        private static void Pull(Stand stand, PropHose hose)
        {
            var body = stand.Plug.GetComponent<Rigidbody>();
            if (body == null || body.isKinematic || hose.yankInwardForce <= 0f)
                return;
            var inward = (hose.hoseStationOrigin.position - stand.Plug.transform.position).normalized;
            body.AddForce(inward * hose.yankInwardForce, ForceMode.Impulse);
        }

        // ---- The valve ----

        private static void SyncValve(Stand stand, float now)
        {
            if (stand.Valve == null)
            {
                stand.Valve = stand.Wheel != null ? stand.Wheel.GetComponent<ControlImplBase>() : null;
                if (stand.Valve == null)
                    return;
                stand.KnownValve = stand.Valve.Value;
            }
            if (stand.PendingValve.HasValue)
            {
                var pending = stand.PendingValve.Value;
                stand.PendingValve = null;
                SetValve(stand, pending, now);
                return;
            }
            var value = stand.Valve.Value;
            if (now < stand.ValveQuietUntil)
            {
                stand.KnownValve = value;
                return;
            }
            if (Mathf.Abs(value - stand.KnownValve) <= ValveStep || now < stand.ValveAt)
                return;
            stand.KnownValve = value;
            stand.ValveAt = now + ValveEvery;
            ValveSender?.Invoke(stand.Key, value, ++stand.ValveSent);
        }

        // False where this machine's own hand has the wheel.
        private static bool SetValve(Stand stand, float value, float now)
        {
            if (stand.Valve == null)
            {
                stand.PendingValve = value;
                return true;
            }
            if (stand.Valve.IsGrabbed())
                return false;
            stand.Valve.SetValue(Mathf.Clamp01(value));
            stand.KnownValve = stand.Valve.Value;
            stand.ValveQuietUntil = now + ValveQuiet;
            return true;
        }

        // ---- From the network ----

        /// <summary>On a client: the host's word on a stand's plug, dropped where an older one than last heard.</summary>
        public static void ClientPlug(string key, PlugReport report, bool poseOnly)
        {
            if (key == null)
                return;
            if (!Stands.TryGetValue(key, out var stand))
            {
                if (!poseOnly && (!EarlyPlugs.TryGetValue(key, out var early) || Newer(report.Seq, early.Seq)))
                    EarlyPlugs[key] = report;
                return;
            }
            if (stand.ValveOnly)
                return;
            if (poseOnly)
            {
                if (!stand.Heard.HasValue || !Newer(stand.Heard.Value, report.Seq))
                    OnPose(stand, report);
                return;
            }
            if (stand.Heard.HasValue && !Newer(report.Seq, stand.Heard.Value))
                return;
            stand.Heard = report.Seq;
            Apply(stand, report, LocalPlayerId?.Invoke());
        }

        /// <summary>On a client: the host's word on a stand's valve, dropped where an older one than last heard.</summary>
        public static void ClientValve(string key, float value, ushort seq)
        {
            if (key == null)
                return;
            if (!Stands.TryGetValue(key, out var stand))
            {
                if (!EarlyValves.TryGetValue(key, out var early) || Newer(seq, early.Seq))
                    EarlyValves[key] = (value, seq);
                return;
            }
            if (stand.ValveHeard.HasValue && !Newer(seq, stand.ValveHeard.Value))
                return;
            stand.ValveHeard = seq;
            SetValve(stand, value, Time.unscaledTime);
        }

        /// <summary>
        /// On the host: a client's word on a stand's plug, from player
        /// <paramref name="sender"/> at <paramref name="senderAt"/> (world).
        /// Taken as the host's own where they may: passed to the others as
        /// <paramref name="reply"/>. Where they may not, or it could not be
        /// done here, <paramref name="reply"/> is how it is, for everyone.
        /// </summary>
        public static Relay HostPlug(string key, PlugReport report, bool poseOnly, byte sender, Vector3 senderAt, out PlugReport reply)
        {
            reply = default;
            if (key == null || !Stands.TryGetValue(key, out var stand) || stand.Plug == null)
                return Relay.None;
            var heard = stand.HeardFrom.TryGetValue(sender, out var last) ? last : (ushort?)null;
            if (report.Place == PlugPlace.Held)
                report.Holder = sender;
            var known = stand.Known;
            var heldBySender = known.Place == PlugPlace.Held && known.Holder == sender;
            if (poseOnly)
            {
                var looseBySender = known.Place == PlugPlace.Loose && !known.Settled && stand.LooseBy == sender;
                if (heard.HasValue && Newer(heard.Value, report.Seq)
                    || !(report.Place == PlugPlace.Held && heldBySender || report.Place == PlugPlace.Loose && looseBySender))
                    return Relay.None;
                OnPose(stand, report);
                reply = report;
                reply.Seq = stand.Sent;
                return Relay.Others;
            }
            if (heard.HasValue && !Newer(report.Seq, heard.Value))
                return Relay.None;
            stand.HeardFrom[sender] = report.Seq;
            // A plug another holds is theirs; one taken must be in reach of
            // the hand, and one put down in reach of its hose. Not "held by
            // the sender": their pick-up may come after (delivered unordered).
            var heldByOther = known.Place == PlugPlace.Held && !heldBySender;
            var from = report.Place == PlugPlace.Held ? stand.Plug.transform.position
                : stand.Hose.hoseStationOrigin != null ? stand.Hose.hoseStationOrigin.position : stand.Plug.transform.position;
            var within = report.Place == PlugPlace.Held ? Reach : stand.Reach + Reach;
            if (heldByOther || (from - senderAt).sqrMagnitude > within * within)
            {
                Say(stand, $"player {sender}'s {report} refused: it is {known}");
                reply = Current(stand);
                reply.Seq = ++stand.Sent;
                return Relay.Everyone;
            }
            var me = LocalPlayerId?.Invoke();
            if (!Apply(stand, report, me))
            {
                // Its engine's connection not here, or taken: let go where
                // the client had it, for everyone.
                var at = report.Place == PlugPlace.InEngine ? report : WithPose(new PlugReport(), stand.Plug);
                var loose = new PlugReport { Place = PlugPlace.Loose, Settled = true, Position = at.Position, Rotation = at.Rotation };
                StopFollowing(stand);
                Put(stand, loose);
                stand.Known = loose;
                stand.LooseBy = null;
                Say(stand, $"player {sender}'s {report} could not be done here: let go");
                reply = loose;
                reply.Seq = ++stand.Sent;
                return Relay.Everyone;
            }
            stand.LooseBy = report.Place == PlugPlace.Loose && !report.Settled ? sender : (byte?)null;
            reply = report;
            reply.Seq = ++stand.Sent;
            return Relay.Others;
        }

        /// <summary>
        /// On the host: a client's turn of a stand's valve. Taken where they
        /// are near it and the host's own hand has it not, and passed to the
        /// others; else the valve as it is, for everyone.
        /// </summary>
        public static Relay HostValve(string key, float value, ushort seq, byte sender, Vector3 senderAt, out float reply, out ushort replySeq)
        {
            reply = 0f;
            replySeq = 0;
            if (key == null || !Stands.TryGetValue(key, out var stand) || stand.Plug == null && !(stand.ValveOnly && stand.Wheel != null))
                return Relay.None;
            if (stand.ValveHeardFrom.TryGetValue(sender, out var last) && !Newer(seq, last))
                return Relay.None;
            stand.ValveHeardFrom[sender] = seq;
            var at = stand.Wheel != null ? stand.Wheel.position : stand.Plug.transform.position;
            var taken = (at - senderAt).sqrMagnitude <= Reach * Reach && SetValve(stand, value, Time.unscaledTime);
            reply = taken ? Mathf.Clamp01(value) : stand.Valve != null ? stand.Valve.Value : 0f;
            replySeq = ++stand.ValveSent;
            return taken ? Relay.Others : Relay.Everyone;
        }

        /// <summary>
        /// On the host: each stand not as it starts (its plug off its stand,
        /// its valve open), with its counts, for a client joining.
        /// </summary>
        public static IEnumerable<(string Key, PlugReport? Plug, (float Value, ushort Seq)? Valve)> Changed()
        {
            foreach (var stand in Each)
            {
                if (stand.Plug == null && !stand.ValveOnly)
                    continue;
                PlugReport? plug = null;
                if (!stand.ValveOnly && stand.Known.Place != PlugPlace.OnStand)
                {
                    var report = Current(stand);
                    report.Seq = stand.Sent;
                    plug = report;
                }
                var valve = stand.Valve != null && stand.Valve.Value > ValveStep ? (stand.Valve.Value, stand.ValveSent) : ((float, ushort)?)null;
                if (plug.HasValue || valve.HasValue)
                    yield return (stand.Key, plug, valve);
            }
        }

        /// <summary>On the host: a player gone lets go of any plug they held or were letting fall.</summary>
        public static void PlayerLeft(byte player)
        {
            var me = LocalPlayerId?.Invoke();
            foreach (var stand in Each.ToList())
            {
                stand.ValveHeardFrom.Remove(player);
                if (stand.Plug == null)
                    continue;
                stand.HeardFrom.Remove(player);
                var known = stand.Known;
                if (known.Place == PlugPlace.Held && known.Holder == player
                    || known.Place == PlugPlace.Loose && !known.Settled && stand.LooseBy == player)
                {
                    StopFollowing(stand);
                    Report(stand, WithPose(new PlugReport { Place = PlugPlace.Loose }, stand.Plug), Time.unscaledTime, me);
                }
            }
        }

        private static void Say(Stand stand, string what)
        {
            if (Main.Settings.LogFireboxCreation)
                Main.Mod.Logger.Log($"Shed stand '{stand.Key}' plug: {what}");
        }
    }

    /// <summary>
    /// A stand's hose in a Multiplayer session keeps its reach by
    /// <see cref="ShedNet.Tether"/>, not the game's check, which the
    /// Multiplayer mod leaves to the host and to hoses it networks itself:
    /// on a client, a plug carried off would never come out of the hand.
    /// </summary>
    [HarmonyPatch(typeof(PropHose), "Update")]
    internal static class StandHoseReachPatch
    {
        private static bool Prefix(PropHose __instance) => !ShedNet.Tether(__instance);
    }
}
