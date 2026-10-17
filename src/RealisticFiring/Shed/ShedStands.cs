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
            public Roundhouse(string turntable, string stallPrefix, float along, float aside, float turn = 0f, string repairStall = null)
            {
                Turntable = turntable;
                StallPrefix = stallPrefix;
                Along = along;
                Aside = aside;
                Turn = turn;
                RepairStall = repairStall;
            }

            public string Turntable { get; }
            public string StallPrefix { get; }
            public float Along { get; }
            public float Aside { get; }

            /// <summary>How far the stand is turned from facing out along its stall, degrees.</summary>
            public float Turn { get; }

 public string RepairStall { get; }

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
            public string Key => "shop " + Id;

            /// <summary>Its stand's name, and its key in a session: the same on every machine, whatever its language.</summary>
            public string Id => FormattableString.Invariant($"{Track} at {Along:F1}");


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
            new Roundhouse("T_HB_1", "HB-T1-", 22f, 2.47f, repairStall: "HB-T1-03-P"),
            // Machine Factory's: the same roundhouse, seven 50 m stalls fanned
            // as Harbor's, so Harbor's place (2026-10-17).
            new Roundhouse("T_MF_1", "MF-T1-", 22f, 2.47f, repairStall: "MF-T1-05-P"),
            // City South's big roundhouse: 25 stalls of 44 m off a 40 m deck,
            // fanned about as far apart as Harbor's; Harbor's 22 m out on
            // its 50 m stalls made 20 m (2026-10-17, to be checked in game).
            new Roundhouse("T-CS-1", "CS-M-", 20f, 2.47f, repairStall: "CS-M-24-P"),
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

        // Each stand's kind of hose.
        private static readonly Dictionary<GameObject, HoseKind> KindOf = new Dictionary<GameObject, HoseKind>();


        /// <summary>
        /// A water stand's place beside its steam stand, in the steam stand's
        /// frame: this far along the way its branch points and this far aside,
        /// away from the track, m, and turned, degrees.
        /// </summary>
        private readonly struct WaterPlace
        {
            public WaterPlace(float along, float aside, float turn)
            {
                Along = along;
                Aside = aside;
                Turn = turn;
            }

            public float Along { get; }
            public float Aside { get; }
            public float Turn { get; }

            public override string ToString() => FormattableString.Invariant($"{Along:F1} m along, {Aside:F2} m aside, turned {Turn:F0} degrees");
        }

        private static readonly WaterPlace WaterUsual = new WaterPlace(7.6f, 1.09f, 0f);

        private static readonly Dictionary<string, WaterPlace> WaterBySite = new Dictionary<string, WaterPlace>
        {
            ["T_IMW_1"] = new WaterPlace(3.2f, 0.15f, 0f),
            ["shop OR-A-04-S at 26.8"] = new WaterPlace(3.2f, -0.11f, 0f),
            ["shop OWN-A-02-D at 130.8"] = new WaterPlace(3.2f, -0.03f, 0f),
            ["shop GF-A-01-D at 105.0"] = new WaterPlace(-1.6f, 0.05f, 0f),
            ["shop FF-A-04-D at 24.2"] = new WaterPlace(-1.6f, 0.01f, 0f),
        };
        private static readonly Dictionary<string, WaterPlace> WaterByStand = new Dictionary<string, WaterPlace>
        {
            ["HB-T1-07-P"] = new WaterPlace(7.6f, 0.72f, 0f),
            ["CS-M-21-P"] = new WaterPlace(1.9f, 0.72f, 0f),
            ["CS-M-22-P"] = new WaterPlace(-1.6f, 0.72f, 0f),
            ["SW-T1-01-P"] = new WaterPlace(-5.0f, 0.27f, 0f),
            ["CW-A-02-D at 94.8"] = new WaterPlace(-5.0f, 0.01f, 0f),
            ["SM-A-02-P at 26.3"] = new WaterPlace(-5.0f, 0.05f, 0f),
            ["SM-T1-01-P"] = new WaterPlace(-5.1f, 0.05f, 0f),
            ["MF-T1-04-P"] = new WaterPlace(12.0f, 1.09f, 0f),
            ["MF-T1-07-P"] = new WaterPlace(8.1f, 0.71f, 0f),
        };

        private static WaterPlace WaterFor(string site, string id) =>
            WaterByStand.TryGetValue(id, out var place) ? place : WaterBySite.TryGetValue(site, out place) ? place : WaterUsual;

        // Each roundhouse's stands, by its turntable, built once its turntable
        // and a hose to copy are found.
        private static readonly Dictionary<string, List<GameObject>> ByRoundhouse = new Dictionary<string, List<GameObject>>();
        private const string StandName = "RealisticFiring shed stand ";

        // Each stand's hose plug and its valve's handwheel (its control made by
        // the game as the copy wakes, looked up as it is read).
        private static readonly Dictionary<PluggableObject, Transform> Valves = new Dictionary<PluggableObject, Transform>();
        private static float lookAt;

        /// <summary>Whether a stand's hose of <paramref name="kind"/> reaches <paramref name="point"/>: its gooseneck within <paramref name="reach"/> m.</summary>
        public static bool Reaches(Vector3 point, float reach, HoseKind kind)
        {
            foreach (var stand in Built)
            {
                if (stand == null || !KindOf.TryGetValue(stand, out var its) || its != kind)
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
            ShedNet.Update();
            if (Time.unscaledTime < lookAt || WorldMover.OriginShiftParent == null)
                return;
            lookAt = Time.unscaledTime + 2f;
            Built.RemoveAll(go => go == null);
            foreach (var gone in KindOf.Keys.Where(k => k == null).ToList())
                KindOf.Remove(gone);
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
                    foreach (var kind in HoseKind.All)
                    {
                        var stand = BuildStand(kind, roundhouse.Turntable, stall, DeckCentre(controller), roundhouse, out var line);
                        if (stand != null && kind == HoseKind.Water && (stall.LogicTrack()?.ID?.FullID ?? stall.name) == roundhouse.RepairStall)
                            line += MakeRepairStand(stand, stall.LogicTrack()?.ID?.FullID ?? stall.name);
                        Keep(stand, kind, built);
                        lines.Add(line);
                    }
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
                var built = ByRoundhouse[shop.Key] = new List<GameObject>();
                var lines = new List<string>();
                foreach (var kind in HoseKind.All)
                {
                    var stand = BuildStandAt(kind, shop.Key, shop.Id, rail, forward, shop.Aside, shop.Turn, $"{shop.Along:F1} m along", out var line);
                    Keep(stand, kind, built);
                    lines.Add(line);
                }
                if (Main.Settings.LogFireboxCreation)
                    Main.Mod.Logger.Log($"Shed stands at a shop on '{shop.Track}': {string.Join("; ", lines)}");
            }
        }

        private static void Keep(GameObject stand, HoseKind kind, List<GameObject> built)
        {
            if (stand == null)
                return;
            Built.Add(stand);
            KindOf[stand] = kind;
            built.Add(stand);
        }

        // ---- Repair stands (direct steaming) ----

        // Each repair stand's water plug and its direct steam handwheel.
        private static readonly Dictionary<PluggableObject, Transform> SteamValves = new Dictionary<PluggableObject, Transform>();

        private static readonly Vector3 SteamWheelAt = new Vector3(0.08f, 1.35f, -0.905f);

        /// <summary>Whether <paramref name="plug"/> is a repair stand's water hose: its water is the plant's hot water.</summary>
        public static bool IsHot(PluggableObject plug) => plug != null && SteamValves.ContainsKey(plug);

        /// <summary>How far a repair stand's direct steam valve on <paramref name="plug"/>'s hose is open, 0 to 1; 0 for any other.</summary>
        public static float DirectSteam(PluggableObject plug)
        {
            if (plug == null || !SteamValves.TryGetValue(plug, out var wheel) || wheel == null)
                return 0f;
            var control = wheel.GetComponent<ControlImplBase>();
            return control != null ? Mathf.Clamp01(control.Value) : 0f;
        }

        /// <summary>The steam let into <paramref name="plug"/>'s hose, 0 to 1: a blower line's valve, a repair stand's direct steam.</summary>
        public static float SteamOpening(PluggableObject plug) =>
            plug != null && plug.connectionTag == HoseKind.BlowerLine.Tag ? Opening(plug) : DirectSteam(plug);

        // Makes a stall's water stand its roundhouse's repair stand: the
        // direct steam handwheel on its riser, and steam from its hose's open
        // end. Returns more for the log.
        private static string MakeRepairStand(GameObject stand, string id)
        {
            var hose = stand.GetComponentInChildren<PropHose>(true);
            if (hose == null || hose.plug == null)
                return ", no repair stand (no hose)";
            var at = SteamWheelAt;
            var wheel = PlaceWheel(stand.transform, at, "Direct steam", Vector3.left, Vector3.up);
            if (wheel == null)
                return ", no direct steam handwheel (no S282 cab to copy it from)";
            var standMesh = stand.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault(r => r.name == "DieselFuelStation");
            var stem = stand.AddComponent<SteamValveStem>();
            stem.Wheel = wheel;
            stem.Layer = standMesh != null ? standMesh.gameObject.layer : 0;
            SteamValves[hose.plug] = wheel;
            var name = Valves.TryGetValue(hose.plug, out var water) && water != null ? water.GetComponent<HoverName>() : null;
            if (name != null)
                name.Text = "Hot water";
            HoseSteam.Fit(hose);
            ShedNet.RegisterValve(id + HoseKind.Water.Key + " steam", wheel);
            return FormattableString.Invariant($", the repair stand: hot water and direct steam, its handwheel at ({at.x:F2}, {at.y:F2}, {at.z:F2}) m in the stand's frame");
        }

        // The turntable's deck's middle: its controller's object can stand
        // well off it (City South's 27 m).
        private static Vector3 DeckCentre(TurntableController controller)
        {
            var deck = controller.turntable != null ? controller.turntable.Track : null;
            return deck != null && deck.curve != null ? deck.curve.GetPointAt(0.5f) : controller.transform.position;
        }

        private static GameObject BuildStand(HoseKind kind, string site, RailTrack stall, Vector3 turntable, Roundhouse roundhouse, out string line)
        {
            var id = stall.LogicTrack()?.ID?.FullID ?? stall.name;
            var curve = stall.curve;
            // Out along the stall from its end at the turntable.
            var fromStart = Vector3.Distance(curve.GetPointAt(0f), turntable) < Vector3.Distance(curve.GetPointAt(1f), turntable);
            var share = Mathf.Clamp01(roundhouse.Along / Mathf.Max(1f, curve.length));
            var t = fromStart ? share : 1f - share;
            var rail = curve.GetPointAt(t);
            var outward = curve.GetPointAt(Mathf.Clamp01(fromStart ? t + 0.02f : t - 0.02f)) - rail;
            return BuildStandAt(kind, site, id, rail, outward, roundhouse.Aside, roundhouse.Turn, $"{roundhouse.Along:F1} m out", out line);
        }

        // A stand for a hose of kind beside a track at the rail point rail,
        // facing along it (toward outward), aside m to its right (to its left
        // when negative, the stand turned about so its spare hose lies away
        // from the track), turned turn degrees more; a water stand beside
        // where the steam stand is.
        private static GameObject BuildStandAt(HoseKind kind, string site, string id, Vector3 rail, Vector3 outward, float aside, float turn, string along, out string line)
        {
            outward = Vector3.ProjectOnPlane(outward, Vector3.up).normalized;
            if (outward.sqrMagnitude < 1e-6f)
            {
                line = $"'{id}': no direction along it";
                return null;
            }
            var right = Vector3.Cross(Vector3.up, outward).normalized;
            var footAt = rail + right * aside;
            // The stand's branch runs out along the track, its plug side to
            // it, its coil away from it.
            var facing = Quaternion.AngleAxis(turn, Vector3.up) * Quaternion.LookRotation(aside >= 0f ? outward : -outward, Vector3.up);
            if (kind == HoseKind.Water)
            {
                var place = WaterFor(site, id);
                footAt += facing * new Vector3(place.Aside, 0f, place.Along);
                facing = Quaternion.AngleAxis(place.Turn, Vector3.up) * facing;
            }
            var floor = Floor(footAt);
            var basePoint = new Vector3(footAt.x, (floor ?? rail.y - FloorUnderRail) + OverFloor, footAt.z);

            var stand = new GameObject(StandName + id + kind.Key);
            stand.transform.SetParent(WorldMover.OriginShiftParent, false);
            // The stand's frame: its riser's foot at the base point.
            stand.transform.SetPositionAndRotation(basePoint - facing * RiserFoot, facing);
            var frame = stand.transform;

            var length = kind.Length;
            var ropeFloor = RopeFloor(frame, basePoint.y - OverFloor);
            var hose = ShedHoses.Build(kind, frame, new Pose(frame.position, frame.rotation), ropeFloor, length);
            var valveAt = ValveStem;
            var wheel = hose != null ? PlaceWheel(frame, valveAt, kind.Valve) : null;
            var blows = false;
            if (hose != null && wheel != null)
            {
                Valves[hose.plug] = wheel;
                // Steam from a blower line's open end, water from a water hose's.
                blows = kind == HoseKind.BlowerLine ? HoseSteam.Fit(hose) : HoseWater.Fit(hose);
            }
            if (hose != null)
                ShedNet.Register(id + kind.Key, hose, wheel);
            var world = basePoint - WorldMover.currentMove;
            line = $"'{id}' at ({world.x:F1}, {world.y:F1}, {world.z:F1}), {along}, {aside:F2} m aside" +
                   (hose != null ? $", its {kind.Name} {length:F0} m" : ", no hose (none to copy)") +
                   (wheel != null ? ", its valve" : ", no valve wheel (no S282 cab to copy it from)") +
                   (wheel != null && !blows ? $", nothing from its open end (no effects bundle)" : "");
            return stand;
        }

        // The valve's handwheel on top of it, facing up: a copy of the S282's
        // blower valve, its keys and its cab port left behind, read for the
        // shed steam it lets through. Null without the S282's cab.
        private static Transform PlaceWheel(Transform frame, Vector3 at, string name) =>
            PlaceWheel(frame, at, name, Vector3.down, Vector3.forward);

        // A handwheel at at, facing out against into (both in the stand's
        // frame), its spokes' up toward up.
        private static Transform PlaceWheel(Transform frame, Vector3 at, string name, Vector3 into, Vector3 up)
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
            var turned = Quaternion.LookRotation(frame.TransformDirection(into), frame.TransformDirection(up));
            valve.rotation = turned * Quaternion.Inverse(control.localRotation);
            valve.position += frame.TransformPoint(at) - control.position;
            valve.SetParent(frame, true);
            valve.localScale *= WheelScale;
            // The control the game builds as it wakes may set its layers:
            // they are set after, and kept so (WorldLayer).
            valve.gameObject.AddComponent<WorldLayer>();
            valve.position += frame.TransformPoint(at) - control.position;
            Object.Destroy(holder);
            control.gameObject.AddComponent<HoverName>().Text = name;
            return control;
        }
    }
}
