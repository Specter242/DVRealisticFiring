using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using DV.ThingTypes;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RealisticFiring.Diagnostics
{
 internal static class ShedSurvey
    {
        // Looked for along a track every this many m, this far around, in m.
        private const float SampleStep = 5f;
        private const float SampleRadius = 7f;

        private static bool done;
        private static float lookAt;

        // A stall is covered where a ray up from this far over its rail hits
        // something within this, in m; looked for at turntables within this
        // of the player, whose scenery is loaded, each once a session.
        private const float RoofFrom = 2f;
        private const float RoofReach = 12f;
        private const float LoadedWithin = 400f;
        private static readonly HashSet<TurntableController> roofed = new HashSet<TurntableController>();
        private static float roofLookAt;

        /// <summary>
        /// Looks for roofs over the stall tracks at the turntables near the
        /// player, whose scenery is loaded; call every frame.
        /// </summary>
        public static void ProbeRoofs()
        {
            if (!Main.Settings.SurveySheds || PlayerManager.PlayerTransform == null || Time.unscaledTime < roofLookAt)
                return;
            roofLookAt = Time.unscaledTime + 5f;
            foreach (var controller in Object.FindObjectsOfType<TurntableController>())
            {
                if (controller == null || roofed.Contains(controller) || controller.turntable == null
                    || Vector3.Distance(controller.transform.position, PlayerManager.PlayerTransform.position) > LoadedWithin)
                    continue;
                roofed.Add(controller);
                try
                {
                    var text = new StringBuilder($"Shed survey: roofs at turntable '{controller.turntable.uniqueID}':");
                    foreach (var end in controller.turntable.trackEnds.Where(e => e != null && e.track != null).GroupBy(e => e.track).Select(g => g.First()))
                        text.Append($"\n    {Roof(end.track)}");
                    Main.Mod.Logger.Log(text.ToString());
                }
                catch (Exception e)
                {
                    Main.Mod.Logger.LogException("Shed survey's roof probe failed", e);
                }
            }
        }

        // How much of a track has a roof over it, and what the roof is.
        private static string Roof(RailTrack track)
        {
            var curve = track.curve;
            var steps = Mathf.Max(1, Mathf.CeilToInt(curve.length / SampleStep));
            var covered = 0;
            var names = new Dictionary<string, int>();
            for (var i = 0; i <= steps; i++)
            {
                var point = curve.GetPointAt((float)i / steps) + Vector3.up * RoofFrom;
                var hits = Physics.RaycastAll(point, Vector3.up, RoofReach)
                    .Where(h => h.collider != null && !h.collider.isTrigger && h.collider.GetComponentInParent<RailTrack>() == null
                                && TrainCar.Resolve(h.collider.gameObject) == null)
                    .ToList();
                if (hits.Count == 0)
                    continue;
                covered++;
                var name = Path(hits.OrderBy(h => h.distance).First().collider.transform);
                names[name] = names.TryGetValue(name, out var n) ? n + 1 : 1;
            }
            var id = track.LogicTrack()?.ID?.FullID ?? track.name;
            return $"'{id}' covered at {covered} of {steps + 1} points" +
                   (names.Count > 0 ? $" by {string.Join(", ", names.OrderByDescending(p => p.Value).Take(3).Select(p => $"{p.Key} ×{p.Value}"))}" : "");
        }

        /// <summary>Runs the survey once the world has its turntables and stations; call every frame.</summary>
        public static void TryRun()
        {
            if (done || !Main.Settings.SurveySheds || PlayerManager.PlayerTransform == null || Time.unscaledTime < lookAt)
                return;
            lookAt = Time.unscaledTime + 5f;
            var turntables = Object.FindObjectsOfType<TurntableController>();
            var stations = Object.FindObjectsOfType<PitStopStation>();
            if (turntables.Length == 0 && stations.Length == 0)
                return;
            done = true;
            try
            {
                Main.Mod.Logger.Log(Survey(turntables, stations));
            }
            catch (Exception e)
            {
                Main.Mod.Logger.LogException("Shed survey failed", e);
            }
        }

        private static string Survey(TurntableController[] turntables, PitStopStation[] stations)
        {
            var text = new StringBuilder();
            text.AppendLine($"Shed survey: {turntables.Length} turntables, {stations.Length} service stations");

            foreach (var controller in turntables)
            {
                var deck = controller.turntable;
                var at = World(controller.transform.position);
                text.AppendLine($"  Turntable '{deck?.uniqueID}' at {Fmt(at)}, deck {(deck != null && deck.Track != null ? deck.Track.curve.length : 0f):F1} m, " +
                                $"{deck?.trackEnds.Count ?? 0} tracks off it");
                if (deck == null)
                    continue;
                foreach (var end in deck.trackEnds.Where(e => e != null && e.track != null).GroupBy(e => e.track).Select(g => g.First()))
                    text.AppendLine("    " + DescribeTrack(end.track, controller.transform.position));
            }

            var parking = Object.FindObjectsOfType<RailTrack>().Where(IsParking).ToList();
            text.AppendLine($"  {parking.Count} locomotive parking tracks:");
            foreach (var track in parking)
                text.AppendLine("    " + DescribeTrack(track, null));

            foreach (var station in stations)
            {
                text.AppendLine($"  Service station '{Path(station.transform)}' at {Fmt(World(station.transform.position))}");
                var pitStop = station.pitstop;
                if (pitStop != null)
                {
                    var bounds = pitStop.GetComponentsInChildren<Collider>(true).Select(c => c.bounds).ToList();
                    text.AppendLine($"    pit stop '{Path(pitStop.transform)}' at {Fmt(World(pitStop.transform.position))}" +
                                    (bounds.Count > 0 ? $", its colliders over {Fmt(World(bounds[0].center))} ±{Fmt(bounds[0].extents)}" : ""));
                }
                var modules = station.locoResourceModules != null ? station.locoResourceModules.resourceModules : null;
                foreach (var module in modules ?? new LocoResourceModule[0])
                {
                    if (module == null)
                        continue;
                    text.AppendLine($"    module {module.resourceType} at {Fmt(World(module.transform.position))}, facing {Fmt(module.transform.forward)}" +
                                    (module.resourceHose != null ? ", with a hose:" : ""));
                    if (module.resourceHose != null)
                        DescribeHose(text, module.resourceHose);
                }
            }
            return text.ToString();
        }

        // A track: its ID, length, ends (the far one from a turntable first),
        // whether each end goes on, and what stands over it.
        private static string DescribeTrack(RailTrack track, Vector3? from)
        {
            var curve = track.curve;
            var a = curve.GetPointAt(0f);
            var b = curve.GetPointAt(1f);
            var farFirst = from.HasValue && Vector3.Distance(a, from.Value) < Vector3.Distance(b, from.Value);
            var far = farFirst ? b : a;
            var near = farFirst ? a : b;
            var farGoesOn = farFirst ? track.outIsConnected : track.inIsConnected;
            var id = track.LogicTrack()?.ID?.FullID ?? track.name;
            var over = new Dictionary<string, int>();
            var steps = Mathf.Max(1, Mathf.CeilToInt(curve.length / SampleStep));
            for (var i = 0; i <= steps; i++)
            {
                foreach (var hit in Physics.OverlapSphere(curve.GetPointAt((float)i / steps), SampleRadius))
                {
                    if (hit.GetComponentInParent<RailTrack>() != null || TrainCar.Resolve(hit.gameObject) != null)
                        continue;
                    var name = hit.transform.root.name;
                    over[name] = over.TryGetValue(name, out var n) ? n + 1 : 1;
                }
            }
            var standing = string.Join(", ", over.OrderByDescending(p => p.Value).Take(8).Select(p => $"{p.Key} ×{p.Value}"));
            return $"'{id}' {curve.length:F0} m from {Fmt(World(near))} to {Fmt(World(far))}, the {(from.HasValue ? "far" : "out")} end " +
                   $"{(farGoesOn ? "going on" : "a dead end")}; around it: {(standing.Length > 0 ? standing : "nothing")}";
        }

        private static bool IsParking(RailTrack track)
        {
            var id = track.LogicTrack()?.ID;
            if (id == null || id.IsGeneric())
                return false;
            var parts = id.FullID.Split('-');
            return parts.Length > 0 && parts[parts.Length - 1] == "P";
        }

        // A service station's hose as built: its stand, rope, plug, and sockets.
        private static void DescribeHose(StringBuilder text, PluggableObject plug)
        {
            // The hose, its plug, and its stand's socket share one object (a station's "…HoseAddon").
            var hose = plug.transform.parent != null ? plug.transform.parent.GetComponentInChildren<PropHose>(true) : null;
            text.AppendLine($"      plug '{Path(plug.transform)}' tag '{plug.connectionTag}', layer {LayerMask.LayerToName(plug.gameObject.layer)}, " +
                            $"{Components(plug.gameObject)}; plugged {plug.State}{(plug.Socket != null ? $" into '{Path(plug.Socket.transform)}' ({plug.Socket.socketTag})" : "")}");
            if (hose == null)
            {
                text.AppendLine("      no PropHose found");
                return;
            }
            var rope = hose.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == "RopeBehaviour");
            var length = RopeLength(rope);
            text.AppendLine($"      hose '{Path(hose.transform)}', origin '{(hose.hoseStationOrigin != null ? Path(hose.hoseStationOrigin) : "none")}', " +
                            $"rope {(length.HasValue ? $"{length.Value:F1} m" : "unread")}, {Components(hose.gameObject)}");
            if (hose.stationSocket != null)
                text.AppendLine($"      its stand's socket '{Path(hose.stationSocket.transform)}' tags '{hose.stationSocket.connectionTag}'/'{hose.stationSocket.socketTag}'");
            text.AppendLine($"      under the hose: {string.Join("; ", hose.GetComponentsInChildren<Transform>(true).Take(24).Select(t => $"{t.name} [{Components(t.gameObject)}]"))}");
        }

        private static float? RopeLength(Component rope)
        {
            var parameters = rope?.GetType().GetField("ropeParams", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(rope);
            var length = parameters?.GetType().GetField("ropeLength", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(parameters);
            return length is float l ? l : (float?)null;
        }

        private static string Components(GameObject go) =>
            string.Join(", ", go.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name));

        private static string Path(Transform t)
        {
            var names = new List<string>();
            for (; t != null && names.Count < 6; t = t.parent)
                names.Add(t.name);
            names.Reverse();
            return string.Join("/", names);
        }

        private static Vector3 World(Vector3 position) => position - WorldMover.currentMove;

        private static string Fmt(Vector3 v) => $"({v.x:F1}, {v.y:F1}, {v.z:F1})";
    }
}
