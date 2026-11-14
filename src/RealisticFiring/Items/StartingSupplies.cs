using System;
using System.Collections.Generic;
using System.Linq;
using LocoSim.Definitions;
using RealisticFiring.Core;
using RealisticFiring.Shed;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Items
{
    /// <summary>
    /// Gives each fired engine its firebox mouth, and the world its supply
    /// places (<see cref="SupplyPlaces"/>).
    /// </summary>
    internal sealed class StartingSupplies : MonoBehaviour
    {
        private float nextScan;
        private void Update()
        {
            if (Time.time < nextScan || PlayerManager.PlayerTransform == null || !Main.ReplacementEnabled) return;
            nextScan = Time.time + 5f;
            FireStartingItems.Register();
            foreach (var fire in FireboxRegistry.All) Attach(fire.Car);
            foreach (var oil in RealisticOilFirebox.All) Attach(oil.Car);
            SupplyPlaces.Update();
        }
        private static void Attach(TrainCar car)
        {
            if (car == null || car.GetComponent<FuelFireboxIntake>() != null) return;
            car.gameObject.AddComponent<FuelFireboxIntake>().Car = car;
        }
    }

 internal static class SupplyPlaces
    {
        // A world's sites, and whether its places are chosen.
        private static Transform world;
        private static bool planned;
        private static float planAt;
        private static int tries;
        private static readonly List<StartingSupplySite> Sites = new List<StartingSupplySite>();
        private static readonly Dictionary<string, bool> SteamLiveries = new Dictionary<string, bool>();

        // A place's track and point, before its exact spot is found.
        private sealed class Anchor
        {
            public string Name, Area, ShedKey;
            public RailTrack Track;
            public float Along;
            public int Side;
            public Vector3 Point;
        }

        /// <summary>The world's site of that name, or null.</summary>
        internal static StartingSupplySite Find(string id) =>
            string.IsNullOrEmpty(id) ? null : Sites.FirstOrDefault(s => s != null && s.Id == id);

        internal static void Update()
        {
            var root = WorldMover.OriginShiftParent;
            if (root == null) return;
            if (!ReferenceEquals(root, world))
            {
                foreach (var site in Sites)
                    if (site != null) UnityEngine.Object.Destroy(site.gameObject);
                Sites.Clear();
                world = root;
                planned = false;
                tries = 0;
                planAt = Time.time + 5f;
            }
            if (planned || Time.time < planAt || !AStartGameData.carsAndJobsLoadingFinished) return;
            List<Anchor> anchors;
            try
            {
                anchors = Anchors();
            }
            catch (Exception e)
            {
                Main.Mod.Logger.LogException("Starting supplies: finding their places failed", e);
                planned = true;
                return;
            }
            // Spawners and turntables wake with the world; wait for them a while.
            if (anchors.Count == 0 && ++tries < 6)
            {
                planAt = Time.time + 10f;
                return;
            }
            planned = true;
            var byName = anchors.GroupBy(a => a.Name).ToDictionary(g => g.Key, g => g.First());
            var chosen = SupplyPlan.Choose(anchors.Select(a => new SupplyPlan.Candidate(a.Area, a.Name, a.ShedKey != null, a.Point.x, a.Point.z)));
            foreach (var place in chosen)
            {
                var a = byName[place.Name];
                var go = new GameObject("RealisticFiring starting supplies " + a.Name);
                go.transform.SetParent(root, false);
                go.transform.position = a.Point + WorldMover.currentMove;
                var site = go.AddComponent<StartingSupplySite>();
                site.Id = a.Name;
                site.ShedKey = a.ShedKey;
                site.Track = a.Track;
                site.Along = a.Along;
                site.Side = a.Side;
                Sites.Add(site);
            }
            var passed = anchors.Select(a => a.Name).Distinct().Except(chosen.Select(c => c.Name)).ToList();
            Main.Mod.Logger.Log($"Starting supplies: {chosen.Count} places, at most {SupplyPlan.PerArea} to a yard: " +
                                string.Join("; ", chosen.Select(c => $"{c.Area} {c.Name}")) +
                                (passed.Count > 0 ? $" (passed by, another near: {string.Join(", ", passed)})" : ""));
        }

        private static List<Anchor> Anchors()
        {
            var anchors = new List<Anchor>();
            foreach (var (key, track, along, side) in ShedStands.SupplyAnchors())
                Add(anchors, track, along, side, key.StartsWith("shop ", StringComparison.Ordinal) ? key : "shed " + key, key);
            foreach (var spawner in UnityEngine.Object.FindObjectsOfType<StationLocoSpawner>())
            {
                if (spawner == null || !SpawnsSteam(spawner)) continue;
                var track = spawner.locoSpawnTrack != null ? spawner.locoSpawnTrack
                    : RailTrackRegistryBase.RailTracks.FirstOrDefault(t => t != null && t.name == spawner.locoSpawnTrackName);
                var points = track != null ? track.GetKinkedPointSet()?.points : null;
                if (points == null || points.Length < 2) continue;
                // Beside the middle, where the game spawns the engine.
                Add(anchors, track, (float)points[points.Length - 1].span / 2f, 0, "spawn " + Label(track), null);
            }
            return anchors;
        }

        private static void Add(List<Anchor> anchors, RailTrack track, float along, int side, string name, string shedKey)
        {
            if (track == null || !PointAt(track, along, out var point, out _)) return;
            anchors.Add(new Anchor
            {
                Name = name, Area = AreaOf(track, point), ShedKey = shedKey,
                Track = track, Along = along, Side = side, Point = point,
            });
        }

        // A track's ID, or its name where its ID is a generic one
        // ("#Y-#S-180-#T" for "[Y]_[CMS]_[P1]").
        private static string Label(RailTrack track)
        {
            var id = track.LogicTrack()?.ID;
            return id != null && !id.IsGeneric() ? id.FullID : track.name;
        }

        // A track's station yard: its ID's, else the one its name gives
        // ("[Y]_[CMS]_[P1]"), else the nearest station's. A generic ID's yard
        // ("#Y") would put every such track in one yard (preview 948's game
        // check: Coal Mine South and the Coal Power Plant went without).
        private static string AreaOf(RailTrack track, Vector3 unshifted)
        {
            var id = track.LogicTrack()?.ID;
            if (id != null && !id.IsGeneric() && !string.IsNullOrEmpty(id.yardId)) return id.yardId;
            var named = SupplyPlan.YardInTrackName(track.name);
            if (named != null) return named;
            StationController nearest = null;
            var best = float.PositiveInfinity;
            foreach (var station in StationController.allStations ?? new List<StationController>())
            {
                if (station == null || station.stationInfo == null) continue;
                var d = (station.transform.position - WorldMover.currentMove - unshifted).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    nearest = station;
                }
            }
            return nearest != null ? nearest.stationInfo.YardID ?? "" : "";
        }

        // A spawner with a steam engine the mod fires among its groups: a
        // firebox in its sim (as the mod's own check), and not one it leaves stock.
        private static bool SpawnsSteam(StationLocoSpawner spawner) =>
            spawner.locoTypeGroupsToSpawn != null && spawner.locoTypeGroupsToSpawn
                .Where(g => g?.liveries != null)
                .SelectMany(g => g.liveries)
                .Any(IsFiredSteam);

        private static bool IsFiredSteam(DV.ThingTypes.TrainCarLivery livery)
        {
            if (livery == null || livery.id == null) return false;
            if (SteamLiveries.TryGetValue(livery.id, out var steam)) return steam;
            steam = livery.prefab != null && livery.prefab.GetComponentInChildren<FireboxDefinition>(true) != null
                    && !LeftStock.Is(livery.id, livery.parentType != null ? livery.parentType.id : null, Main.Session.UnlistedEngines);
            return SteamLiveries[livery.id] = steam;
        }

        /// <summary>The point <paramref name="along"/> m from <paramref name="track"/>'s start, unshifted, and its way on.</summary>
        internal static bool PointAt(RailTrack track, float along, out Vector3 point, out Vector3 forward)
        {
            point = forward = Vector3.zero;
            var points = track != null ? track.GetKinkedPointSet()?.points : null;
            if (points == null || points.Length == 0) return false;
            var i = 1;
            while (i < points.Length - 1 && points[i].span < along) i++;
            if (points.Length == 1)
            {
                point = (Vector3)points[0].position;
                forward = points[0].forward;
                return true;
            }
            var a = points[i - 1];
            var b = points[i];
            var gap = (float)(b.span - a.span);
            var t = gap > 1e-4f ? Mathf.Clamp01((along - (float)a.span) / gap) : 0f;
            point = Vector3.Lerp((Vector3)a.position, (Vector3)b.position, t);
            forward = Vector3.Slerp(a.forward, b.forward, t);
            if (forward.sqrMagnitude < 1e-6f) forward = (Vector3)b.position - (Vector3)a.position;
            forward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
            return forward.sqrMagnitude > 0.5f;
        }
    }

    /// <summary>
    /// One crate of wood and one tin of rags beside a track at a supply
    /// place, nobody's until picked up. Its exact spot is found the first
    /// time a player comes near, when the buildings around it have loaded:
    /// the first spot from <see cref="SupplyPlan.Spots"/> clear of every
    /// other track, the shed's stands, and anything built there. Every 30 s,
    /// while a player is near, a container at its place is filled again and
    /// a missing one replaced; one carried away keeps what is left in it.
    /// The site keeps its own containers by reference: the game deactivates
    /// world items far from the player (ItemDisabler, 192 m), and a site
    /// that lost sight of its sleeping crate would stock another every 30 s.
    /// In multiplayer the host stocks every site: a guest's game finds the
    /// spot where its player is (the host's may not have that ground loaded)
    /// and asks the host for it (2026-11-12: guests saw none where the host
    /// had not been). The host's containers there sleep until the host comes
    /// near, and the Multiplayer mod shows them to guests.
    /// </summary>
    internal sealed class StartingSupplySite : MonoBehaviour
    {
        // Stock only where someone can see it; past this the game puts items to sleep.
        private const float StockingRange = 150f;
        // A shed's stands are waited for this long before its spot is chosen without them.
        private const float StandWait = 60f;
        // Stands' risers and coils are kept this far from, m.
        private const float StandRoom = 1.6f;
        // Other tracks this near the place are looked at, m.
        private const float TrackSearch = 40f;
        private float next;
        // When a player first came near while its spot was unfound; -1 before.
        private float nearSince = -1f;
        internal string Id, ShedKey;
        internal RailTrack Track;
        internal float Along;
        internal int Side;
        private bool settled;
        // Other tracks' centre lines near it, found once.
        private List<IReadOnlyList<(float X, float Z)>> nearTracks;
        private StartingFuelItem crate, tin;
        // The place's point before its spot was found, unshifted.
        private Vector3 anchor;

        /// <summary>The Multiplayer bridge, on a guest: asks the host to stock a site at its spot (unshifted) and turn.</summary>
        internal static Action<string, Vector3, Quaternion> RequestStock;
        // Side by side along the track, in the spot's frame (+x away from the track).
        private static readonly Vector3 WoodAt = new Vector3(0f, 0f, -0.4f), RagAt = new Vector3(0f, 0f, 0.4f);
        private void Start()
        {
            next = Time.time + 5f;
            anchor = transform.position - WorldMover.currentMove;
        }
        private void Update()
        {
            if (Time.time < next || !Main.ReplacementEnabled) return;
            next = Time.time + (settled ? 30f : 5f);
            var player = PlayerManager.PlayerTransform;
            if (player == null || (player.position - transform.position).sqrMagnitude > StockingRange * StockingRange) return;
            if (!settled)
            {
                if (nearSince < 0f) nearSince = Time.time;
                if (!Settle()) return;
            }
            if (Net.IsClient)
            {
                if (!Stands(StartingFuelKind.WoodCrate, WoodAt) || !Stands(StartingFuelKind.RagBox, RagAt))
                    RequestStock?.Invoke(Id, transform.position - WorldMover.currentMove, transform.rotation);
                return;
            }
            Supply(ref crate, StartingFuelKind.WoodCrate, WoodAt);
            Supply(ref tin, StartingFuelKind.RagBox, RagAt);
        }

        // On a guest: whether the host's container stands at its place here.
        private bool Stands(StartingFuelKind kind, Vector3 offset)
        {
            var at = transform.TransformPoint(offset);
            foreach (var item in StartingFuelItem.Live)
                if (item != null && item.Kind == kind && Flat(item.transform.position - at) < 2f)
                    return true;
            return false;
        }

        /// <summary>
        /// On the host: a guest near the site of <paramref name="id"/> found
        /// its spot at <paramref name="spot"/> (unshifted) facing
        /// <paramref name="turn"/>, standing at <paramref name="guest"/> (the
        /// host's world). Stocked there as for the host's own player, the
        /// spot taken if the host has found none itself. Why not, otherwise.
        /// </summary>
        internal static string StockFor(string id, Vector3 spot, Quaternion turn, Vector3 guest)
        {
            if (Net.IsClient || !Main.ReplacementEnabled) return "not hosting with realistic fireboxes";
            var site = SupplyPlaces.Find(id);
            if (site == null) return "no such place";
            var from = guest - WorldMover.currentMove;
            if (!SupplyPlan.AcceptsGuestSpot(site.anchor.x, site.anchor.z, spot.x, spot.z, from.x, from.z, StockingRange))
                return "the spot is not by the place, or the guest is not near it";
            if (!site.settled)
            {
                site.transform.SetPositionAndRotation(spot + WorldMover.currentMove, Quaternion.Euler(0f, turn.eulerAngles.y, 0f));
                site.settled = true;
                Main.Mod.Logger.Log($"Starting supplies '{site.Id}': set out at a guest's spot ({spot.x:F1}, {spot.y:F1}, {spot.z:F1})");
            }
            site.Supply(ref site.crate, StartingFuelKind.WoodCrate, WoodAt);
            site.Supply(ref site.tin, StartingFuelKind.RagBox, RagAt);
            return null;
        }

        // Finds the spot; false to try again later (the shed's stands not yet built).
        private bool Settle()
        {
            var waited = Time.time - nearSince;
            if (ShedKey != null && !ShedStands.HasStands(ShedKey) && waited < StandWait) return false;
            var others = nearTracks ?? (nearTracks = NearTracks());
            var stands = ShedStands.Footprints().Select(p => p - WorldMover.currentMove).ToList();
            var side = Side != 0 ? Side : Clearer(others);
            // The first spot clear of the tracks though something is built
            // there, and the spot with the most room from other tracks.
            Vector3? built = null, roomiest = null;
            Quaternion builtTurn = Quaternion.identity, roomiestTurn = Quaternion.identity;
            SupplyPlan.Spot? builtSpot = null, roomiestSpot = null;
            var room = float.NegativeInfinity;
            foreach (var spot in SupplyPlan.Spots(side))
            {
                if (!Place(spot, out var at, out var turn)) continue;
                if (stands.Any(s => Flat(s - at) < StandRoom)) continue;
                var world = at + WorldMover.currentMove;
                var ground = Ground(world);
                if (ground == null) continue;
                var onGround = new Vector3(world.x, ground.Value, world.z);
                if (!SupplyPlan.ClearOfTracks(at.x, at.z, others))
                {
                    var here = others.Count == 0 ? float.PositiveInfinity : others.Min(l => SupplyPlan.DistanceToLine(at.x, at.z, l));
                    if (here > room)
                    {
                        room = here;
                        roomiest = onGround;
                        roomiestTurn = turn;
                        roomiestSpot = spot;
                    }
                    continue;
                }
                if (built == null)
                {
                    built = onGround;
                    builtTurn = turn;
                    builtSpot = spot;
                }
                if (!Free(onGround, turn)) continue;
                transform.SetPositionAndRotation(onGround, turn);
                settled = true;
                Main.Mod.Logger.Log($"Starting supplies '{Id}': set out {spot} at ({at.x:F1}, {onGround.y - WorldMover.currentMove.y:F1}, {at.z:F1})");
                return true;
            }
            // Nothing clear yet: the buildings and ground may still be loading.
            if (waited < StandWait) return false;
            if (built != null)
            {
                transform.SetPositionAndRotation(built.Value, builtTurn);
                Main.Mod.Logger.Warning($"Starting supplies '{Id}': something is built at every spot clear of the tracks; set out {builtSpot} anyway");
            }
            else if (roomiest != null)
            {
                transform.SetPositionAndRotation(roomiest.Value, roomiestTurn);
                Main.Mod.Logger.Warning($"Starting supplies '{Id}': no spot clear of the tracks; set out {roomiestSpot}, {room:F1} m from the nearest other track");
            }
            else
            {
                Place(new SupplyPlan.Spot(0f, side, 3.0f), out var at, out var turn);
                transform.SetPositionAndRotation(at + WorldMover.currentMove, turn);
                Main.Mod.Logger.Warning($"Starting supplies '{Id}': no ground found beside its track; set out beside its middle");
            }
            settled = true;
            return true;
        }

        // The spot's point beside the track, unshifted, and its frame: facing along the track, +x away from it.
        private bool Place(SupplyPlan.Spot spot, out Vector3 at, out Quaternion turn)
        {
            at = Vector3.zero;
            turn = Quaternion.identity;
            if (!SupplyPlaces.PointAt(Track, Along + spot.Along, out var rail, out var forward)) return false;
            var right = Vector3.Cross(Vector3.up, forward).normalized;
            at = rail + right * (spot.Side * spot.Aside);
            turn = Quaternion.LookRotation(spot.Side > 0 ? forward : -forward, Vector3.up);
            return true;
        }

        // The other tracks' centre lines near the place, unshifted, in the ground plane.
        private List<IReadOnlyList<(float X, float Z)>> NearTracks()
        {
            var lines = new List<IReadOnlyList<(float X, float Z)>>();
            if (!SupplyPlaces.PointAt(Track, Along, out var centre, out _)) return lines;
            foreach (var track in RailTrackRegistryBase.RailTracks)
            {
                if (track == null || track == Track) continue;
                var points = track.GetKinkedPointSet()?.points;
                if (points == null) continue;
                List<(float X, float Z)> line = null;
                for (var i = 0; i < points.Length; i++)
                {
                    // A near point, and those either side of one, so each segment reaching near counts.
                    var keep = Near(points, i, centre) || i > 0 && Near(points, i - 1, centre) || i + 1 < points.Length && Near(points, i + 1, centre);
                    if (keep)
                    {
                        var p = (Vector3)points[i].position;
                        if (line == null) line = new List<(float X, float Z)>();
                        line.Add((p.x, p.z));
                    }
                    else if (line != null)
                    {
                        lines.Add(line);
                        line = null;
                    }
                }
                if (line != null) lines.Add(line);
            }
            return lines;
        }

        private static bool Near(DV.PointSet.EquiPointSet.Point[] points, int i, Vector3 centre) =>
            Flat((Vector3)points[i].position - centre) < TrackSearch;

        // The side with more room from other tracks beside the middle: 1 right, -1 left.
        private int Clearer(List<IReadOnlyList<(float X, float Z)>> others)
        {
            if (!Place(new SupplyPlan.Spot(0f, 1, 3.0f), out var right, out _) || !Place(new SupplyPlan.Spot(0f, -1, 3.0f), out var left, out _)) return 1;
            float Room(Vector3 p) => others.Count == 0 ? float.PositiveInfinity : others.Min(l => SupplyPlan.DistanceToLine(p.x, p.z, l));
            return Room(left) > Room(right) ? -1 : 1;
        }

        private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        // The ground's height under a point, near its rail's height; null where none has loaded.
        private static float? Ground(Vector3 world)
        {
            float? best = null;
            foreach (var hit in Physics.RaycastAll(world + Vector3.up * 3f, Vector3.down, 8f, HoseGround.GroundLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<RailTrack>() != null || !HoseGround.IsGround(hit.collider, null)) continue;
                if (hit.collider.GetComponentInParent<StartingFuelItem>() != null) continue;
                if (hit.point.y < world.y + 1.5f && (best == null || hit.point.y > best.Value)) best = hit.point.y;
            }
            return best;
        }

        // Whether nothing built stands where the two containers go.
        private static bool Free(Vector3 onGround, Quaternion turn)
        {
            var half = new Vector3(SupplyPlan.HalfAcross, 0.4f, SupplyPlan.HalfAlong);
            var centre = onGround + Vector3.up * (0.2f + half.y);
            foreach (var hit in Physics.OverlapBox(centre, half, turn, LayerMask.GetMask("Default"), QueryTriggerInteraction.Ignore))
            {
                if (hit.GetComponentInParent<RailTrack>() != null || hit.GetComponentInParent<StartingFuelItem>() != null) continue;
                if (PlayerManager.PlayerTransform != null && hit.transform.IsChildOf(PlayerManager.PlayerTransform)) continue;
                return false;
            }
            return true;
        }

        private void Supply(ref StartingFuelItem stock, StartingFuelKind kind, Vector3 offset)
        {
            var at = transform.TransformPoint(offset);
            var ground = Ground(at);
            if (ground != null) at.y = ground.Value;
            at += Vector3.up * (StartingFuelBody.Bounds(kind).extents.y + 0.02f);
            var key = Id + "/" + kind;
            // Its own container, carried off (the carrier's now) or gone, is forgotten.
            if (stock != null && (stock.Consumed || (stock.transform.position - at).sqrMagnitude > 4f))
            {
                Main.Mod.Logger.Log($"Starting supplies '{Id}': its {FireStartingItems.Name(kind).ToLowerInvariant()} was taken away; setting out another");
                stock = null;
            }
            if (stock == null)
            {
                // One standing at the place (a player's brought back, or after a load) is taken in.
                foreach (var item in StartingFuelItem.Live)
                    if (item != null && item.Kind == kind && item.SupplyId == key && !item.Held && (item.transform.position - at).sqrMagnitude < 4f)
                    {
                        stock = item;
                        break;
                    }
            }
            if (stock != null)
            {
                if (!stock.Held) stock.Count = FireStartingItems.ContainerCapacity;
                return;
            }
            stock = FireStartingItems.Spawn(kind, at, transform.rotation, belongsToPlayer: false);
            if (stock != null) stock.SupplyId = key;
            else Main.Mod.Logger.Warning($"Starting supplies '{Id}': could not set out a {FireStartingItems.Name(kind).ToLowerInvariant()}");
        }
    }
}
