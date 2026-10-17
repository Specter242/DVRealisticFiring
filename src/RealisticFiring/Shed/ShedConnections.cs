using System.Collections.Generic;
using System.Linq;
using DV.Simulation.Controllers;
using LocoSim.Implementations;
using RealisticFiring.Visuals;
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

        private static readonly string[] CapLiveries = { "LocoDE2", "LocoDH4", "LocoDM3" };
        private const string CapName = "FuelTankCap";
        private const string FittingName = "RealisticFiring Barco fitting";

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

 private static readonly HashSet<string> RightSide = new HashSet<string> { "LocoS060" };

 private static readonly HashSet<string> NoConnection = new HashSet<string> { "em_lms_8f_loc_black", "em_lms_8f_loc_grey" };

        private sealed class Connection
        {
            public TrainCar Car;
            public PlugSocket Socket;
            public Transform Fitting;
            public Vector3 Guessed;
        }

        private static readonly Dictionary<SteamExhaust, Connection> BlowerSockets = new Dictionary<SteamExhaust, Connection>();

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
                    return ShedStands.Reaches(connection.Socket.transform.position, HoseKind.BlowerLine.Length * PullsTo);
            }
            return false;
        }

        // A station hose comes out of the hand pulled past this share of its length (PropHose).
        private const float PullsTo = 1.25f;

        /// <summary>Whether a house blower line is in this engine's connection, its valve open or shut.</summary>
        public static bool LineIn(SteamExhaust exhaust) =>
            exhaust != null && BlowerSockets.TryGetValue(exhaust, out var connection) && connection.Socket != null && connection.Socket.IsPluggedIn;

        // The connection where its engine is seated, or the guess moved by
        // the sliders. Returns a line for the log.
        private static string Place(Connection connection)
        {
            var livery = connection.Car.carLivery != null ? connection.Car.carLivery.id : "";
            var swing = 0f;
            var down = 0f;
            var spin = 0f;
            var further = 0f;
            Vector3 at;
            string how;
            if (Seated.TryGetValue(livery, out var seat))
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
                how = "guessed under the stack";
            }
            var side = at.x > 0f ? 1f : -1f;
            // Facing out of its side; turned down about the car's length and
            // swung toward the front about its up, the same on either side;
            // spun last about its own axis, which turns the plug about it.
            var facing = Quaternion.LookRotation(Vector3.forward, new Vector3(side, 0f, 0f));
            var turned = Quaternion.AngleAxis(-side * swing, Vector3.up) * Quaternion.AngleAxis(-side * down, Vector3.forward) * facing;
            var go = connection.Socket.transform;
            go.localRotation = turned * Quaternion.AngleAxis(spin, Vector3.up);
            go.localPosition = at + go.localRotation * Vector3.up * (FittingProud + further);
            BlowerFittings.Orient(connection.Fitting, connection.Car.transform);
            return $"blower line connection with its flange at ({at.x:F2}, {at.y:F2}, {at.z:F2}) m in the car frame on its {(side > 0f ? "right" : "left")}" +
                   (further != 0f ? $" and {further * 100f:F0} cm further out" : "") +
                   $", swung {swing:F0} toward the front, turned {down:F0} down, and spun {spin:F0} degrees, {how}";
        }

        /// <summary>Gives a fired engine its blower line connection. Returns a line for the log, or null.</summary>
        public static string Attach(TrainCar car, SimulationFlow flow)
        {
            var exhaust = flow?.OrderedSimComps.OfType<SteamExhaust>().FirstOrDefault();
            var reader = car != null ? car.GetComponentInChildren<SteamSmokeParticlePortReader>(true) : null;
            if (exhaust == null || reader == null || car.carLivery != null && NoConnection.Contains(car.carLivery.id))
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
            var height = stack.y - UnderStack;
            var onRight = car.carLivery != null && RightSide.Contains(car.carLivery.id);
            var side = Side(car, new Vector3(0f, height, stack.z), onRight);
            var local = new Vector3(onRight ? side : -side, height, stack.z);

            var socket = Fitting(car);
            if (socket == null)
                return "no diesel fuel cap to copy for the blower line connection";
            var go = socket.gameObject;
            // An oil burner's line feeds its atomizer and heater too, and
            // its own steam takes over sooner (McShane 1921: at 20 lb).
            socket.PluggedIn += (plug, at) => Said(car, Sim.RealisticOilFirebox.TryGet(car, out _)
                ? $"{HoseKind.BlowerLine.Name} in: open the stand's valve and the shed's steam (100 psi) works the blower, atomizer, and oil heater; take it off once the engine makes about 20 psi of its own"
                : $"{HoseKind.BlowerLine.Name} in: open the stand's valve and the shed's steam (100 psi) works the blower; take it off once the engine makes about 50 psi of its own");
            socket.Unplugged += (plug, at) => Said(car, $"{HoseKind.BlowerLine.Name} off");
            var connection = new Connection { Car = car, Socket = socket, Fitting = socket.transform.Find(FittingName), Guessed = local };
            BlowerSockets[exhaust] = connection;
            return Place(connection) + " (prototype)";
        }

        // A copy of a diesel's fuel cap and its socket on the engine, retagged
        // for the blower line, on a pipe back to the smokebox with a flange.
        private static PlugSocket Fitting(TrainCar car)
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
            copy.name = "RealisticFiring blower line connection";
            var socket = copy.GetComponent<PlugSocket>();
            socket.connectionTag = HoseKind.BlowerLine.Tag;
            socket.socketTag = "loco";
            // Smaller, as the blower line's plug is: its cap, not its transform.
            var scale = HoseKind.BlowerLine.Fittings;
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
            var fitting = BlowerFittings.Barco(copy.transform, layer);
            fitting.name = FittingName;
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

        private static void Said(TrainCar car, string text)
        {
            // Not as the game unloads its cars, which unplugs their hoses
            // (and logs an error for an ID read then).
            if (car == null || car.logicCar == null)
                return;
            if (Main.Settings.LogFireboxCreation)
                Main.Mod.Logger.Log($"{car?.ID}: {text}");
            if (car != null && PlayerManager.Car == car)
                FireController.Announce(char.ToUpper(text[0]) + text.Substring(1));
        }
    }
}
