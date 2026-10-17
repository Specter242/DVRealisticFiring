using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DV;
using DV.CabControls;
using DV.Simulation.Ports;
using RealisticFiring.Visuals;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RealisticFiring.Shed
{
 internal static class ShedStands
    {
        /// <summary>
        /// A roundhouse: its turntable, the stall tracks off it that get a
        /// stand, and where each stall's stand stands: this far out along the
        /// stall from the turntable and this far to its right looking out, m.
        /// </summary>
        private readonly struct Roundhouse
        {
            public Roundhouse(string turntable, string stallPrefix, float along, float aside, float turn = 0f)
            {
                Turntable = turntable;
                StallPrefix = stallPrefix;
                Along = along;
                Aside = aside;
                Turn = turn;
            }

            public string Turntable { get; }
            public string StallPrefix { get; }
            public float Along { get; }
            public float Aside { get; }

            /// <summary>How far the stand is turned from facing out along its stall, degrees.</summary>
            public float Turn { get; }

        }

 private readonly struct Shop
        {
            public Shop(string track, float along, float aside, float turn = 0f)
            {
                Track = track;
                Along = along;
                Aside = aside;
                Turn = turn;
            }

            public string Track { get; }
            public float Along { get; }
            public float Aside { get; }
            public float Turn { get; }

            /// <summary>Its stands' key among the sites.</summary>
            public string Key => FormattableString.Invariant($"shop {Track} at {Along:F1}");


        }

        private static readonly Shop[] Shops =
        {
            new Shop("SM-A-02-P", 26.3f, -3.39f),
            new Shop("CW-A-02-D", 94.8f, 3.30f),
            new Shop("OR-A-04-S", 26.8f, -2.82f),
            new Shop("OWN-A-02-D", 130.8f, -3.55f),
            new Shop("GF-A-01-D", 105.0f, -3.30f),
            new Shop("FF-A-04-D", 24.2f, 3.26f),
        };

        // The tracks by their IDs.
        private static Dictionary<string, RailTrack> tracks;

        private static RailTrack TrackNamed(string id)
        {
            if (tracks == null || tracks.Count == 0)
            {
                tracks = new Dictionary<string, RailTrack>();
                foreach (var track in RailTrackRegistryBase.RailTracks)
                {
                    var name = track != null ? track.LogicTrack()?.ID?.FullID : null;
                    if (name != null && !tracks.ContainsKey(name))
                        tracks[name] = track;
                }
            }
            return tracks.TryGetValue(id, out var found) ? found : null;
        }

        // A track's point this far along it from its start, in the world,
        // and its way on.
        private static bool AlongTrack(RailTrack track, float along, out Vector3 rail, out Vector3 forward)
        {
            rail = forward = Vector3.zero;
            var points = track.GetKinkedPointSet()?.points;
            if (points == null || points.Length == 0)
                return false;
            var best = points.OrderBy(p => System.Math.Abs(p.span - along)).First();
            rail = (Vector3)best.position + WorldMover.currentMove;
            forward = best.forward;
            return true;
        }

        private static readonly Roundhouse[] Roundhouses =
        {
            new Roundhouse("T_HB_1", "HB-T1-", 22f, 2.47f),
            // Machine Factory's: the same roundhouse, seven 50 m stalls fanned
            // as Harbor's, so Harbor's place (2026-10-17).
            new Roundhouse("T_MF_1", "MF-T1-", 22f, 2.47f),
            // City South's big roundhouse: 25 stalls of 44 m off a 40 m deck,
            // fanned about as far apart as Harbor's; Harbor's 22 m out on
            // its 50 m stalls made 20 m (2026-10-17, to be checked in game).
            new Roundhouse("T-CS-1", "CS-M-", 20f, 2.47f),
            // The small engine houses, each stand 45 % out along its stall as
            // Harbor's and City South's are, near a spawned engine's middle
            // (the game spawns them centred on the track), 2.47 m to the
            // right (2026-10-17, to be checked in game): Oil Well Central's
            // and the Steel Mill's 38 m stalls, Iron Mine West's 28 m, the
            // Sawmill's one of 81 m.
            new Roundhouse("T_OWC_1", "OWC-T1-", 12.9f, 2.47f), 
            new Roundhouse("T_IMW_1", "IMW-T1-", 12.5f, 2.47f),
            new Roundhouse("T_SM_1", "SM-T1-", 17f, 2.47f),
            new Roundhouse("T_SW_1", "SW-T1-", 36f, 2.47f),
        };

        // The station's diesel stand, in its own frame, m: its riser's foot
        // (the standpipe mesh sits 0.905 m behind the frame's origin), and
        // the stem on its low branch's valve, 0.29 m out along it and topping
        // out about 1.0 m up, where the handwheel goes (read offline from
        // the game's DieselFuelStation mesh, 2026-10-16). The base sits
        // this far under the rail, the roundhouse's floor about at it.
        private static readonly Vector3 RiserFoot = new Vector3(0f, 0f, -0.905f);
        private static readonly Vector3 ValveStem = new Vector3(0f, 1.0f, -0.615f);

        private const float OverFloor = 0.09f;
        private const float FloorSearchFrom = 1.5f;
        private const float FloorSearch = 4f;
        private const float FloorUnderRail = 0.17f;

        private const float WheelScale = 1.8f;

        // The handwheel: the S282's blower valve's.
        private const string WheelLivery = "LocoS282A";
        private const string WheelValve = "Left/Valves/Blower";
        private const string WheelControl = "C_Blower";

        private static readonly List<GameObject> Built = new List<GameObject>();

        // Each roundhouse's stands, by its turntable, built once its turntable
        // and a hose to copy are found.
        private static readonly Dictionary<string, List<GameObject>> ByRoundhouse = new Dictionary<string, List<GameObject>>();
        private const string StandName = "RealisticFiring shed stand ";

        // Each stand's hose plug and its valve's handwheel (its control made by
        // the game as the copy wakes, looked up as it is read).
        private static readonly Dictionary<PluggableObject, Transform> Valves = new Dictionary<PluggableObject, Transform>();
        private static float lookAt;

        /// <summary>Whether a stand's hose reaches <paramref name="point"/>: its gooseneck within <paramref name="reach"/> m.</summary>
        public static bool Reaches(Vector3 point, float reach)
        {
            foreach (var stand in Built)
            {
                if (stand == null)
                    continue;
                var hose = stand.GetComponentInChildren<PropHose>(true);
                var from = hose != null ? hose.transform.position : stand.transform.position;
                if ((from - point).sqrMagnitude <= reach * reach)
                    return true;
            }
            return false;
        }

        /// <summary>How far the valve on <paramref name="plug"/>'s stand is open, 0 shut to 1; 0 for a plug not of a stand.</summary>
        public static float Opening(PluggableObject plug)
        {
            if (plug == null || !Valves.TryGetValue(plug, out var wheel) || wheel == null)
                return 0f;
            var control = wheel.GetComponent<ControlImplBase>();
            return control != null ? Mathf.Clamp01(control.Value) : 0f;
        }

        // The floor under a point, world height, or null where nothing of the
        // shed is loaded under it.
        private static float? Floor(Vector3 at, Transform ignore = null)
        {
            var from = at + Vector3.up * FloorSearchFrom;
            float? best = null;
            foreach (var hit in Physics.RaycastAll(from, Vector3.down, FloorSearch, HoseGround.GroundLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<RailTrack>() != null || !HoseGround.IsGround(hit.collider, ignore))
                    continue;
                if (hit.point.y < at.y + 1f && (!best.HasValue || hit.point.y > best.Value))
                    best = hit.point.y;
            }
            return best;
        }

        private static float RopeFloor(Transform stand, float footFloor)
        {
            var coil = Floor(stand.TransformPoint(ShedHoses.CoilAt), stand);
            return coil.HasValue ? Mathf.Max(footFloor, coil.Value) : footFloor;
        }

        private const float RefloorEvery = 2f;
        private const float RefloorStep = 0.005f;
        private static float refloorAt;

        private static void FloorStands()
        {
            if (Time.unscaledTime < refloorAt)
                return;
            refloorAt = Time.unscaledTime + RefloorEvery;
            foreach (var go in Built)
            {
                if (go == null)
                    continue;
                var stand = go.transform;
                var floor = Floor(stand.TransformPoint(RiserFoot), stand);
                if (!floor.HasValue)
                    continue;
                var rise = floor.Value + OverFloor - stand.position.y;
                if (Mathf.Abs(rise) < RefloorStep)
                    continue;
                stand.position += Vector3.up * rise;
                var hose = stand.GetComponentInChildren<PropHose>(true);
                if (hose != null)
                    ShedHoses.SetFloor(hose, RopeFloor(stand, floor.Value));
            }
        }

        private static List<(Roundhouse Roundhouse, TurntableController Controller)> Found(TurntableController[] controllers) =>
            Roundhouses
                .Select(r => (Roundhouse: r, Controller: controllers.FirstOrDefault(c => c != null && c.turntable != null && c.turntable.uniqueID == r.Turntable)))
                .Where(f => f.Controller != null)
                .ToList();

        // A stand's hose is simulated only with the player this near it: the
        // map's 54 hoses each run a rope of 81 points, 100 passes a frame.
        // A far one keeps the shape it had.
        private const float SimulateWithin = 150f;
        private const float SleepEvery = 1f;
        private static float sleepAt;

        private static void SleepFar()
        {
            if (Time.unscaledTime < sleepAt || PlayerManager.PlayerTransform == null)
                return;
            sleepAt = Time.unscaledTime + SleepEvery;
            var player = PlayerManager.PlayerTransform.position;
            foreach (var stand in Built)
            {
                if (stand == null)
                    continue;
                var near = Vector3.Distance(stand.transform.position, player) < SimulateWithin;
                foreach (var solver in stand.GetComponentsInChildren<VerletRope.VerletSolver>(true))
                {
                    if (solver.enabled == near)
                        continue;
                    if (!near)
                        solver.Complete();
                    solver.enabled = near;
                }
            }
        }

        /// <summary>Builds the stands once their places and a hose to copy are loaded; call every frame.</summary>
        public static void Update()
        {
            SleepFar();
            FloorStands();
            if (Time.unscaledTime < lookAt || WorldMover.OriginShiftParent == null)
                return;
            lookAt = Time.unscaledTime + 2f;
            Built.RemoveAll(go => go == null);
            if (ByRoundhouse.Count >= Roundhouses.Length + Shops.Length || ShedHoses.Template() == null)
                return;
            foreach (var gone in Valves.Keys.Where(k => k == null).ToList())
                Valves.Remove(gone);
            var found = Found(Object.FindObjectsOfType<TurntableController>());
            foreach (var (roundhouse, controller) in found)
            {
                if (ByRoundhouse.ContainsKey(roundhouse.Turntable))
                    continue;
                var built = ByRoundhouse[roundhouse.Turntable] = new List<GameObject>();
                var stalls = controller.turntable.trackEnds
                    .Where(e => e != null && e.track != null)
                    .Select(e => e.track)
                    .Distinct()
                    .Where(t => (t.LogicTrack()?.ID?.FullID ?? "").StartsWith(roundhouse.StallPrefix))
                    .ToList();
                var lines = new List<string>();
                foreach (var stall in stalls)
                {
                    var stand = BuildStand(stall, DeckCentre(controller), roundhouse, out var line);
                    if (stand != null)
                    {
                        Built.Add(stand);
                        built.Add(stand);
                    }
                    lines.Add(line);
                }
                if (Main.Settings.LogFireboxCreation)
                    Main.Mod.Logger.Log($"Shed stands at turntable '{roundhouse.Turntable}': {string.Join("; ", lines)}");
            }
            foreach (var shop in Shops)
            {
                if (ByRoundhouse.ContainsKey(shop.Key))
                    continue;
                var track = TrackNamed(shop.Track);
                if (track == null || !AlongTrack(track, shop.Along, out var rail, out var forward))
                    continue;
                var stand = BuildStandAt($"{shop.Track} at {shop.Along:F1}", rail, forward, shop.Aside, shop.Turn, $"{shop.Along:F1} m along", out var line);
                ByRoundhouse[shop.Key] = stand != null ? new List<GameObject> { stand } : new List<GameObject>();
                if (stand != null)
                    Built.Add(stand);
                if (Main.Settings.LogFireboxCreation)
                    Main.Mod.Logger.Log($"Shed stand at a shop on '{shop.Track}': {line}");
            }
        }

        // The turntable's deck's middle: its controller's object can stand
        // well off it (City South's 27 m).
        private static Vector3 DeckCentre(TurntableController controller)
        {
            var deck = controller.turntable != null ? controller.turntable.Track : null;
            return deck != null && deck.curve != null ? deck.curve.GetPointAt(0.5f) : controller.transform.position;
        }

        private static GameObject BuildStand(RailTrack stall, Vector3 turntable, Roundhouse roundhouse, out string line)
        {
            var id = stall.LogicTrack()?.ID?.FullID ?? stall.name;
            var curve = stall.curve;
            // Out along the stall from its end at the turntable.
            var fromStart = Vector3.Distance(curve.GetPointAt(0f), turntable) < Vector3.Distance(curve.GetPointAt(1f), turntable);
            var share = Mathf.Clamp01(roundhouse.Along / Mathf.Max(1f, curve.length));
            var t = fromStart ? share : 1f - share;
            var rail = curve.GetPointAt(t);
            var outward = curve.GetPointAt(Mathf.Clamp01(fromStart ? t + 0.02f : t - 0.02f)) - rail;
            return BuildStandAt(id, rail, outward, roundhouse.Aside, roundhouse.Turn, $"{roundhouse.Along:F1} m out", out line);
        }

        // A stand beside a track at the rail point rail, facing along it
        // (toward outward), aside m to its right (to its left when
        // negative, the stand turned about so its spare hose lies away from
        // the track), turned turn degrees more.
        private static GameObject BuildStandAt(string id, Vector3 rail, Vector3 outward, float aside, float turn, string along, out string line)
        {
            outward = Vector3.ProjectOnPlane(outward, Vector3.up).normalized;
            if (outward.sqrMagnitude < 1e-6f)
            {
                line = $"'{id}': no direction along it";
                return null;
            }
            var right = Vector3.Cross(Vector3.up, outward).normalized;
            var footAt = rail + right * aside;
            var floor = Floor(footAt);
            var basePoint = new Vector3(footAt.x, (floor ?? rail.y - FloorUnderRail) + OverFloor, footAt.z);
            // The stand's branch runs out along the track, its plug side to
            // it, its coil away from it.
            var facing = Quaternion.AngleAxis(turn, Vector3.up) * Quaternion.LookRotation(aside >= 0f ? outward : -outward, Vector3.up);

            var stand = new GameObject(StandName + id);
            stand.transform.SetParent(WorldMover.OriginShiftParent, false);
            // The stand's frame: its riser's foot at the base point.
            stand.transform.SetPositionAndRotation(basePoint - facing * RiserFoot, facing);
            var frame = stand.transform;

            var kind = HoseKind.BlowerLine;
            var length = kind.Length;
            var ropeFloor = RopeFloor(frame, basePoint.y - OverFloor);
            var hose = ShedHoses.Build(kind, frame, new Pose(frame.position, frame.rotation), ropeFloor, length);
            var valveAt = ValveStem;
            var wheel = hose != null ? PlaceWheel(frame, valveAt) : null;
            var blows = false;
            if (hose != null && wheel != null)
            {
                Valves[hose.plug] = wheel;
                blows = HoseSteam.Fit(hose);
            }
            var world = basePoint - WorldMover.currentMove;
            line = $"'{id}' at ({world.x:F1}, {world.y:F1}, {world.z:F1}), {along}, {aside:F2} m aside" +
                   (hose != null ? $", its {kind.Name} {length:F0} m" : ", no hose (none to copy)") +
                   (wheel != null ? ", its valve" : ", no valve wheel (no S282 cab to copy it from)") +
                   (wheel != null && !blows ? ", no steam from its open end (no effects bundle)" : "");
            return stand;
        }

        // The valve's handwheel on top of it, facing up: a copy of the S282's
        // blower valve, its keys and its cab port left behind, read for the
        // shed steam it lets through. Null without the S282's cab.
        private static Transform PlaceWheel(Transform frame, Vector3 at)
        {
            if (!Globals.G.Types.TryGetLivery(WheelLivery, out var livery) || livery.interiorPrefab == null)
                return null;
            var source = livery.interiorPrefab.transform.Find(WheelValve);
            var sourceControl = source != null ? source.Find(WheelControl) : null;
            if (sourceControl == null)
                return null;
            var holder = new GameObject("RealisticFiring valve (being built)");
            holder.SetActive(false);
            var valve = Object.Instantiate(source.gameObject, holder.transform, false).transform;
            valve.name = "shed steam valve";
            var control = valve.Find(WheelControl);
            var body = valve.gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            foreach (var input in control.GetComponents<MonoBehaviour>()
                         .Where(c => c != null && (StokerCab.InputComponents.Contains(c.GetType().Name) || c is InteractablePortFeeder)).ToList())
                Object.DestroyImmediate(input);
            // The wheel faces up, turning about the stem: its z down into the valve.
            var turned = Quaternion.LookRotation(-frame.up, frame.forward);
            valve.rotation = turned * Quaternion.Inverse(control.localRotation);
            valve.position += frame.TransformPoint(at) - control.position;
            valve.SetParent(frame, true);
            valve.localScale *= WheelScale;
            // The control the game builds as it wakes may set its layers:
            // they are set after, and kept so (WorldLayer).
            valve.gameObject.AddComponent<WorldLayer>();
            valve.position += frame.TransformPoint(at) - control.position;
            Object.Destroy(holder);
            control.gameObject.AddComponent<HoverName>().Text = "Shed steam";
            return control;
        }
    }
}
