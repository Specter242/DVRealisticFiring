using System.Collections.Generic;
using System.Linq;
using DV.Simulation.Controllers;
using LocoSim.Implementations;
using RealisticFiring.Visuals;
using RealisticStackEffects;
using UnityEngine;

namespace RealisticFiring.Shed
{
 internal static class ShedConnections
    {
        /// <summary>The house line's steam at the engine, absolute bar: 100 psi (plants at 125 to 150 lb, with losses in the stall drops).</summary>
        public const float HousePressure = 7.9f;

        // The smokebox's middle this far under the stack's smoke emitter, in
        // m; the side looked for from this far out; the fitting's cap stands
        // this far proud of it on its pipe (prototype values).
        private const float UnderStack = 1.2f;
        private const float SideSearch = 3f;
        private const float FittingProud = BlowerFittings.Proud;
        private const float FallbackSide = 1.35f;

        // The water connection's guess: this far ahead of the backhead and
        // under the firehole's foot, near the mud ring, m; on an engine whose
        // cab is not fitted, this far behind the blower line's guess.
        private const float AheadOfBackhead = 1.0f;
        private const float UnderFirehole = 0.5f;
        private const float BehindSmokebox = 6f;
        private const float FallbackFirebox = 1.3f;

        private static readonly string[] CapLiveries = { "LocoDE2", "LocoDH4", "LocoDM3" };
        private const string CapName = "FuelTankCap";
        private const string FittingName = "RealisticFiring Barco fitting";
        private const string FillingName = "RealisticFiring filling connection";

 private static readonly Dictionary<string, (Vector3 At, float Swing, float Down, float Spin, float Out)> Seated =
            new Dictionary<string, (Vector3, float, float, float, float)>
        {
            ["LocoS282A"] = (new Vector3(-0.48f, 1.93f, 10.32f), 0f, 44f, 90f, 0.03f),
            ["LocoS060"] = (new Vector3(0.52f, 1.97f, 3.60f), 0f, 34f, 0f, 0f),
            ["S464-730"] = (new Vector3(-0.74f, 2.33f, 12.47f), 0f, 43f, 0f, 0f),
            ["FEF-3"] = (new Vector3(-0.76f, 2.36f, 7.77f), 0f, 54f, 0f, 0f),
            ["S484-840A"] = (new Vector3(-0.79f, 2.45f, 7.57f), 0f, 54f, 0f, 0f),
            ["A.T.&S.F. 2901-A"] = (new Vector3(-0.64f, 2.31f, 8.08f), 0f, 79f, 0f, 0f),
            ["Jk3A"] = (new Vector3(-0.90f, 1.35f, 3.25f), 0f, -13f, 0f, 0f),
        };

 private static readonly Dictionary<string, (Vector3 At, float Swing, float Down, float Spin, float Out)> WaterSeated =
            new Dictionary<string, (Vector3, float, float, float, float)>
        {
            ["LocoS060"] = (new Vector3(-0.65f, 1.47f, -1.00f), 0f, 0f, 0f, 0f),
            ["Jk3A"] = (new Vector3(-0.54f, 1.31f, -1.35f), 0f, 0f, 0f, 0f),
            ["LocoS282A"] = (new Vector3(-0.22f, 1.15f, 2.28f), 0f, 0f, 0f, 0f),
            // The 8F: set on the grey livery; the black is the same engine.
            ["em_lms_8f_loc_grey"] = (new Vector3(-0.66f, 1.55f, -2.15f), 0f, 0f, 0f, 0f),
            ["em_lms_8f_loc_black"] = (new Vector3(-0.66f, 1.55f, -2.15f), 0f, 0f, 0f, 0f),
            // The Dreyfuss Hudson.
            ["S464-730"] = (new Vector3(-0.93f, 1.49f, 0.72f), 0f, 0f, 0f, 0f),
            ["FEF-3"] = (new Vector3(-1.42f, 1.77f, -7.27f), 0f, 0f, 0f, 0f),
            ["S484-840A"] = (new Vector3(-0.72f, 1.41f, -7.23f), 0f, 0f, 0f, 0f),
            // The Santa Fe 2901: far back on its long boiler.
            ["A.T.&S.F. 2901-A"] = (new Vector3(-1.22f, 2.03f, -7.98f), 0f, 0f, 0f, 0f),
        };

        // Seats set this session on the seating bench (Visuals.SeatingBench, a
        // development tool), by livery, as Seated and WaterSeated: they come
        // first and are never saved.
        internal static readonly Dictionary<string, (Vector3 At, float Swing, float Down, float Spin, float Out)> DevSeated =
            new Dictionary<string, (Vector3, float, float, float, float)>();
        internal static readonly Dictionary<string, (Vector3 At, float Swing, float Down, float Spin, float Out)> DevWaterSeated =
            new Dictionary<string, (Vector3, float, float, float, float)>();

 private static readonly HashSet<string> RightSide = new HashSet<string> { "LocoS060" };

 private static readonly HashSet<string> NoConnection = new HashSet<string> { "em_lms_8f_loc_black", "em_lms_8f_loc_grey" };

        private sealed class Connection
        {
            public TrainCar Car;
            public HoseKind Kind;
            public PlugSocket Socket;
            public Transform Fitting;
            public Vector3 Guessed;
        }

        private static readonly Dictionary<SteamExhaust, Connection> BlowerSockets = new Dictionary<SteamExhaust, Connection>();
        private static readonly Dictionary<TrainCar, Connection> WaterSockets = new Dictionary<TrainCar, Connection>();

        private static IEnumerable<Connection> Connections => BlowerSockets.Values.Concat(WaterSockets.Values);

        /// <summary>
        /// Whether the shed's water hose is in this engine's connection, how
        /// far its stand's valve is open, 0 to 1 (0 with none in), whether its
        /// water is the plant's hot water, and how far its direct steam valve
        /// is open (a repair stand's).
        /// </summary>
        public static (bool In, float Opening, bool Hot, float Steam) WaterLine(TrainCar car)
        {
            if (car == null || !WaterSockets.TryGetValue(car, out var connection) || connection.Socket == null || !connection.Socket.IsPluggedIn)
                return (false, 0f, false, 0f);
            var plug = connection.Socket.Plug;
            return (true, ShedStands.Opening(plug), ShedStands.IsHot(plug), ShedStands.DirectSteam(plug));
        }

        /// <summary>
        /// How far the house blower line in this engine's connection is open,
        /// 0 to 1, by its stand's valve; 0 with none in.
        /// </summary>
        public static float BlowerLine(SteamExhaust exhaust) =>
            LineIn(exhaust) ? ShedStands.Opening(BlowerSockets[exhaust].Socket.Plug) : 0f;

 public static bool StandReaches(TrainCar car)
        {
            if (car == null)
                return false;
            foreach (var connection in BlowerSockets.Values)
            {
                if (connection.Car == car && connection.Socket != null)
                    return ShedStands.Reaches(connection.Socket.transform.position, HoseKind.BlowerLine.Length * PullsTo, HoseKind.BlowerLine);
            }
            return false;
        }

        // A station hose comes out of the hand pulled past this share of its length (PropHose).
        private const float PullsTo = 1.25f;

        /// <summary>The engine whose water connection, with a hose in, is nearest <paramref name="point"/> within <paramref name="within"/> m, or null.</summary>
        public static TrainCar WaterLineNear(Vector3 point, float within)
        {
            TrainCar nearest = null;
            var best = within * within;
            foreach (var connection in WaterSockets.Values)
            {
                if (connection.Car == null || connection.Socket == null || !connection.Socket.IsPluggedIn)
                    continue;
                var distance = (connection.Socket.transform.position - point).sqrMagnitude;
                if (distance < best)
                {
                    best = distance;
                    nearest = connection.Car;
                }
            }
            return nearest;
        }

        /// <summary>The connection on <paramref name="car"/> for the hose whose plug carries <paramref name="tag"/>, or null where it has none (or none yet).</summary>
        public static PlugSocket SocketOf(TrainCar car, string tag)
        {
            if (car == null)
                return null;
            foreach (var connection in Connections)
            {
                if (connection.Car == car && connection.Socket != null && connection.Kind.Tag == tag)
                    return connection.Socket;
            }
            return null;
        }

        /// <summary>The engine whose connection <paramref name="socket"/> is, or null.</summary>
        public static TrainCar CarAt(PlugSocket socket)
        {
            if (socket == null)
                return null;
            foreach (var connection in Connections)
            {
                if (connection.Socket == socket)
                    return connection.Car;
            }
            return null;
        }

        /// <summary>Whether a house blower line is in this engine's connection, its valve open or shut.</summary>
        public static bool LineIn(SteamExhaust exhaust) =>
            exhaust != null && BlowerSockets.TryGetValue(exhaust, out var connection) && connection.Socket != null && connection.Socket.IsPluggedIn;

        // The connection where its engine is seated, or its guess. Returns a
        // line for the log.
        private static string Place(Connection connection)
        {
            var livery = connection.Car.carLivery != null ? connection.Car.carLivery.id : "";
            var water = connection.Kind == HoseKind.Water;
            var swing = 0f;
            var down = 0f;
            var spin = 0f;
            var further = 0f;
            Vector3 at;
            string how;
            if ((water ? DevWaterSeated : DevSeated).TryGetValue(livery, out var seat)
                || (water ? WaterSeated : Seated).TryGetValue(livery, out seat))
            {
                at = seat.At;
                swing = seat.Swing;
                down = seat.Down;
                spin = seat.Spin;
                further = seat.Out;
                how = "seated for the livery";
            }
            else
            {
                at = connection.Guessed;
                how = water ? "guessed low on the firebox" : "guessed under the stack";
            }
            var side = at.x > 0f ? 1f : -1f;
            // Facing out of its side; turned down about the car's length and
            // swung toward the front about its up, the same on either side;
            // spun last about its own axis, which turns the plug about it.
            var facing = Quaternion.LookRotation(Vector3.forward, new Vector3(side, 0f, 0f));
            var turned = Quaternion.AngleAxis(-side * swing, Vector3.up) * Quaternion.AngleAxis(-side * down, Vector3.forward) * facing;
            var go = connection.Socket.transform;
            go.localRotation = turned * Quaternion.AngleAxis(spin, Vector3.up);
            go.localPosition = at + go.localRotation * Vector3.up * ((water ? WaterFittings.Proud : FittingProud) + further);
            if (!water)
                BlowerFittings.Orient(connection.Fitting, connection.Car.transform);
            return $"{connection.Kind.Name} connection with its flange at ({at.x:F2}, {at.y:F2}, {at.z:F2}) m in the car frame on its {(side > 0f ? "right" : "left")}" +
                   (further != 0f ? $" and {further * 100f:F0} cm further out" : "") +
                   $", swung {swing:F0} toward the front, turned {down:F0} down, and spun {spin:F0} degrees, {how}";
        }

        // The car's connection of the kind, or null.
        private static Connection ConnectionOf(TrainCar car, bool water) =>
            car == null ? null
            : water ? WaterSockets.TryGetValue(car, out var found) && found.Socket != null ? found : null
            : BlowerSockets.Values.FirstOrDefault(c => c.Car == car && c.Socket != null);

        /// <summary>
        /// Where <paramref name="car"/>'s connection of the kind is now: the
        /// bench's seat, the table's, or its guess with no turns; false when
        /// it has none.
        /// </summary>
        internal static bool TrySeat(TrainCar car, bool water, out (Vector3 At, float Swing, float Down, float Spin, float Out) seat)
        {
            seat = default;
            var connection = ConnectionOf(car, water);
            if (connection == null)
                return false;
            var livery = car.carLivery != null ? car.carLivery.id : "";
            if (!(water ? DevWaterSeated : DevSeated).TryGetValue(livery, out seat) && !(water ? WaterSeated : Seated).TryGetValue(livery, out seat))
                seat = (connection.Guessed, 0f, 0f, 0f, 0f);
            return true;
        }

        /// <summary>Puts <paramref name="car"/>'s connection of the kind where its seat now says (the seating bench). Returns a line for the log, or null.</summary>
        internal static string Replace(TrainCar car, bool water)
        {
            var connection = ConnectionOf(car, water);
            return connection != null ? Place(connection) : null;
        }

        /// <summary>Gives a fired engine its blower line and water connections. Returns a line for the log, or null.</summary>
        public static string Attach(TrainCar car, SimulationFlow flow)
        {
            foreach (var stale in WaterSockets.Keys.Where(k => k == null || WaterSockets[k].Socket == null).ToList())
                WaterSockets.Remove(stale);
            var blower = AttachBlower(car, flow, out var stackGuess);
            var water = AttachWater(car, flow, stackGuess);
            return blower != null && water != null ? blower + "; " + water : blower ?? water;
        }

        private static string AttachWater(TrainCar car, SimulationFlow flow, Vector3? stackGuess)
        {
            if (car == null || flow == null || !flow.OrderedSimComps.OfType<Boiler>().Any())
                return null;
            Vector3 local;
            if (FireholeOpening.Backhead(car, out var plane, out var foot))
            {
                var height = foot - UnderFirehole;
                var z = plane + AheadOfBackhead;
                local = new Vector3(-Side(car, new Vector3(0f, height, z), false), height, z);
            }
            else
            {
                var z = stackGuess.HasValue ? stackGuess.Value.z - BehindSmokebox : 0f;
                local = new Vector3(-Side(car, new Vector3(0f, FallbackFirebox, z), false), FallbackFirebox, z);
            }
            var socket = Fitting(car, HoseKind.Water);
            if (socket == null)
                return "no diesel fuel cap to copy for the water connection";
            var fitting = socket.transform.Find(FillingName);
            // Its cock open with the hose on, shut without.
            socket.PluggedIn += (plug, at) =>
            {
                WaterFittings.Cock(fitting, true);
                HoseGrip.Reach(plug, car);
                Said(car, socket, WaterIn(car, ShedStands.IsHot(plug)));
            };
            socket.Unplugged += (plug, at) =>
            {
                WaterFittings.Cock(fitting, false);
                HoseGrip.Let(plug);
                var billed = Sim.InjectorRig.TryGet(car, out var rig) && (rig.PlantWaterBilled >= 1f || rig.PlantFuelBilled >= 1f);
                Said(car, socket, $"{HoseKind.Water.Name} off" + (billed
                    ? $": {rig.PlantWaterBilled:F0} kg of the plant's water and {rig.PlantFuelBilled:F0} kg of its {rig.PlantFuelName} are on the loco's fees at the Career Manager"
                    : ""));
            };
            WaterFittings.Cock(fitting, socket.IsPluggedIn);
            var connection = new Connection { Car = car, Kind = HoseKind.Water, Socket = socket, Fitting = fitting, Guessed = local };
            WaterSockets[car] = connection;
            return Place(connection);
        }

        // What plugging the water hose in says: it fills only a cold boiler;
        // a repair stand's fills it hot and steams it.
        private static string WaterIn(TrainCar car, bool repair)
        {
            var pressure = Sim.InjectorRig.TryGet(car, out var rig) && rig.BoilerPressure > Sim.InjectorRig.HoseMaxPressure;
            if (repair)
                return pressure
                    ? $"{HoseKind.Water.Name} in at the repair stand: open its Direct steam handwheel and the plant's steam ({Sim.InjectorRig.PlantGauge:F0} psi) raises the boiler's; the water fills only a cold boiler"
                    : $"{HoseKind.Water.Name} in at the repair stand: open Hot water to wash out and fill the boiler at 180 F, only to the bottom of the glass (the steam's water fills it, a fifth of the boiler again), then Direct steam to raise it to the plant's {Sim.InjectorRig.PlantGauge:F0} psi";
            return pressure
                ? $"{HoseKind.Water.Name} in, but the boiler has pressure: the hose fills only a cold boiler, under about {Sim.InjectorRig.HoseMaxGauge:F0} psi"
                : $"{HoseKind.Water.Name} in: open the stand's valve and the shed's water washes the boiler out and fills it; shut it at the top of the glass";
        }

        // The blower line's, on the smokebox, from the stack's place (also
        // given out as the water connection's last resort).
        private static string AttachBlower(TrainCar car, SimulationFlow flow, out Vector3? stackGuess)
        {
            stackGuess = null;
            var exhaust = flow?.OrderedSimComps.OfType<SteamExhaust>().FirstOrDefault();
            var reader = car != null ? car.GetComponentInChildren<SteamSmokeParticlePortReader>(true) : null;
            if (exhaust == null || reader == null)
                return null;
            foreach (var stale in BlowerSockets.Keys.Where(k => k == null || BlowerSockets[k].Socket == null).ToList())
                BlowerSockets.Remove(stale);

            // The stack where its smoke leaves, as the plume takes it: the
            // largest smoke system's emitter. Some engines keep their smoke
            // systems under an object at the car's origin (the FEF-3's, the
            // S484's: their guesses were at the origin, 2026-10-17).
            var smoke = reader.smokeParticlesParent != null
                ? reader.smokeParticlesParent.GetComponentsInChildren<ParticleSystem>(true).OrderByDescending(s => s.main.maxParticles).FirstOrDefault()
                : null;
            var stackAt = smoke != null ? ChimneyPlume.Emitter(smoke)
                : reader.smokeParticlesParent != null ? reader.smokeParticlesParent.transform.position : reader.transform.position;
            var stack = car.transform.InverseTransformPoint(stackAt);
            stackGuess = stack;
            if (car.carLivery != null && NoConnection.Contains(car.carLivery.id))
                return null;
            var height = stack.y - UnderStack;
            var onRight = car.carLivery != null && RightSide.Contains(car.carLivery.id);
            var side = Side(car, new Vector3(0f, height, stack.z), onRight);
            var local = new Vector3(onRight ? side : -side, height, stack.z);

            var socket = Fitting(car, HoseKind.BlowerLine);
            if (socket == null)
                return "no diesel fuel cap to copy for the blower line connection";
            // An oil burner's line feeds its atomizer and heater too, and
            // its own steam takes over sooner (McShane 1921: at 20 lb).
            socket.PluggedIn += (plug, at) =>
            {
                HoseGrip.Reach(plug, car);
                Said(car, socket, Sim.RealisticOilFirebox.TryGet(car, out _)
                    ? $"{HoseKind.BlowerLine.Name} in: open the stand's valve and the shed's steam (100 psi) works the blower, atomizer, and oil heater; take it off once the engine makes about 20 psi of its own"
                    : $"{HoseKind.BlowerLine.Name} in: open the stand's valve and the shed's steam (100 psi) works the blower; take it off once the engine makes about 50 psi of its own");
            };
            socket.Unplugged += (plug, at) =>
            {
                HoseGrip.Let(plug);
                Said(car, socket, $"{HoseKind.BlowerLine.Name} off");
            };
            var connection = new Connection { Car = car, Kind = HoseKind.BlowerLine, Socket = socket, Fitting = socket.transform.Find(FittingName), Guessed = local };
            BlowerSockets[exhaust] = connection;
            return Place(connection) + " (prototype)";
        }

        // A copy of a diesel's fuel cap and its socket on the engine, retagged
        // for the hose of kind, drawn as its fitting: the Barco fitting on the
        // smokebox for the blower line, the filling connection for water.
        private static PlugSocket Fitting(TrainCar car, HoseKind kind)
        {
            Transform cap = null;
            foreach (var id in CapLiveries)
            {
                if (DV.Globals.G.Types.TryGetLivery(id, out var livery) && livery.prefab != null)
                    cap = livery.prefab.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == CapName && t.GetComponent<PlugSocket>() != null);
                if (cap != null)
                    break;
            }
            if (cap == null)
                return null;
            var holder = new GameObject("RealisticFiring fitting (being built)");
            holder.SetActive(false);
            var copy = Object.Instantiate(cap.gameObject, holder.transform, false);
            copy.name = $"RealisticFiring {kind.Name} connection";
            var socket = copy.GetComponent<PlugSocket>();
            socket.connectionTag = kind.Tag;
            socket.socketTag = "loco";
            // Sized as its hose's plug is: its cap, not its transform.
            var scale = kind.Fittings;
            foreach (Transform part in copy.transform)
            {
                part.localPosition *= scale;
                part.localScale *= scale;
            }
            foreach (var sphere in copy.GetComponents<SphereCollider>())
            {
                sphere.radius *= scale;
                sphere.center *= scale;
            }
            // Drawn as the Barco fitting, not the diesel's cap.
            var layer = copy.GetComponentsInChildren<Renderer>(true).Select(r => r.gameObject.layer).DefaultIfEmpty(copy.layer).First();
            foreach (var renderer in copy.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;
            var fitting = kind == HoseKind.Water ? WaterFittings.Filling(copy.transform, layer, kind.Scale) : BlowerFittings.Barco(copy.transform, layer);
            fitting.name = kind == HoseKind.Water ? FillingName : FittingName;
            BlowerFittings.Outline(copy.transform, fitting);
            copy.transform.SetParent(car.transform, false);
            Object.Destroy(holder);
            return socket;
        }

        // How far out the engine's side is at a point in its frame, its left
        // or its right, found from its own colliders, in m.
        private static float Side(TrainCar car, Vector3 local, bool right)
        {
            var sign = right ? 1f : -1f;
            var from = car.transform.TransformPoint(new Vector3(sign * SideSearch, local.y, local.z));
            var toward = -sign * car.transform.right;
            var best = float.MaxValue;
            foreach (var hit in Physics.RaycastAll(from, toward, SideSearch, ~0, QueryTriggerInteraction.Ignore))
            {
                if (TrainCar.Resolve(hit.collider.gameObject) == car && hit.distance < best)
                    best = hit.distance;
            }
            return best < float.MaxValue ? SideSearch - best : FallbackSide;
        }

        // Said to a player on the engine or near its connection: a hose is
        // put in from the ground.
        private const float SaidWithin = 15f;

        private static void Said(TrainCar car, PlugSocket socket, string text)
        {
            // Not as the game unloads its cars, which unplugs their hoses
            // (and logs an error for an ID read then).
            if (car == null || car.logicCar == null)
                return;
            if (Main.Settings.LogFireboxCreation)
                Main.Mod.Logger.Log($"{car?.ID}: {text}");
            var near = PlayerManager.PlayerTransform != null && socket != null
                       && Vector3.Distance(PlayerManager.PlayerTransform.position, socket.transform.position) < SaidWithin;
            if (PlayerManager.Car == car || near)
                FireController.Announce(char.ToUpper(text[0]) + text.Substring(1));
        }
    }
}
