using System;
using System.Collections.Generic;
using System.Linq;

namespace RealisticFiring.Core
{
 public static class SupplyPlan
    {
        /// <summary>The most supply places in one station's yard.</summary>
        public const int PerArea = 2;

        /// <summary>No two supply places nearer each other than this, m: a second is a walk, not a step, from the first.</summary>
        public const float Apart = 100f;

        /// <summary>
        /// A container's nearest part from any track's centre line, m: an
        /// engine's 1.6 m half width, its steps and cylinder cocks, and a
        /// little room to walk.
        /// </summary>
        public const float TrackClearance = 2.3f;

        /// <summary>How far each container's footprint reaches from its middle across and along the track, m.</summary>
        public const float HalfAcross = 0.35f, HalfAlong = 0.85f;

        /// <summary>A place supplies could stand, in the world's ground plane.</summary>
        public readonly struct Candidate
        {
            public Candidate(string area, string name, bool shed, float x, float z)
            {
                Area = area ?? "";
                Name = name ?? "";
                Shed = shed;
                X = x;
                Z = z;
            }

            /// <summary>The station's yard, from its track's ID ("HB", "MF").</summary>
            public string Area { get; }

            /// <summary>Its name in the log and its containers' key, the same on every machine.</summary>
            public string Name { get; }

            /// <summary>An engine shed's place (taken first), not a spawn track's.</summary>
            public bool Shed { get; }

            public float X { get; }
            public float Z { get; }

            public override string ToString() => Name;
        }

        /// <summary>
        /// The places that get supplies: sheds before spawn tracks, each kind
        /// in name order so every machine and every load chooses alike; one
        /// is passed by when its yard has <see cref="PerArea"/> already or
        /// one taken stands within <see cref="Apart"/>.
        /// </summary>
        public static List<Candidate> Choose(IEnumerable<Candidate> candidates)
        {
            var chosen = new List<Candidate>();
            if (candidates == null)
                return chosen;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in candidates
                         .Where(c => !float.IsNaN(c.X) && !float.IsNaN(c.Z) && !float.IsInfinity(c.X) && !float.IsInfinity(c.Z))
                         .OrderBy(c => c.Shed ? 0 : 1)
                         .ThenBy(c => c.Name, StringComparer.Ordinal))
            {
                if (!seen.Add(c.Name))
                    continue;
                if (chosen.Count(o => o.Area == c.Area) >= PerArea)
                    continue;
                if (chosen.Any(o => Sq(o.X - c.X) + Sq(o.Z - c.Z) < Apart * Apart))
                    continue;
                chosen.Add(c);
            }
            return chosen;
        }

        /// <summary>A spot to try beside the anchor's track: this far along it, on this side (1 right, -1 left), this far from its centre line, m.</summary>
        public readonly struct Spot
        {
            public Spot(float along, int side, float aside)
            {
                Along = along;
                Side = side;
                Aside = aside;
            }

            public float Along { get; }
            public int Side { get; }
            public float Aside { get; }

            public override string ToString() => $"{Along:F0} m along, {Aside:F1} m to the {(Side > 0 ? "right" : "left")}";
        }

        // Out from the anchor a few metres at a time each way, and closer to
        // and further from the track: the first spot clear of tracks, stands,
        // and buildings is taken. Beside a track inside a yard nothing near
        // is clear of its neighbours, so a second pass looks past them to the
        // yard's edge.
        private static readonly float[] Alongs = { 0f, 4f, -4f, 8f, -8f, 12f, -12f };
        private static readonly float[] Asides = { 3.0f, 3.6f, 2.7f, 4.4f };
        private static readonly float[] FarAsides = { 6f, 8f, 10f, 12.5f, 15f };

        /// <summary>The spots to try in order: beside the track first, then past its neighbours; <paramref name="preferredSide"/> first at each step along.</summary>
        public static IEnumerable<Spot> Spots(int preferredSide)
        {
            var first = preferredSide < 0 ? -1 : 1;
            foreach (var asides in new[] { Asides, FarAsides })
            foreach (var along in Alongs)
            foreach (var side in new[] { first, -first })
            foreach (var aside in asides)
                yield return new Spot(along, side, aside);
        }

        /// <summary>Whether a footprint centred <paramref name="aside"/> m from its own track is clear of it.</summary>
        public static bool ClearOfOwnTrack(float aside) => aside - HalfAcross >= TrackClearance;

        /// <summary>
        /// The ground-plane distance from (<paramref name="x"/>, <paramref name="z"/>)
        /// to the nearest segment of <paramref name="line"/>'s points; infinity for none.
        /// </summary>
        public static float DistanceToLine(float x, float z, IReadOnlyList<(float X, float Z)> line)
        {
            if (line == null || line.Count == 0)
                return float.PositiveInfinity;
            if (line.Count == 1)
                return (float)Math.Sqrt(Sq(line[0].X - x) + Sq(line[0].Z - z));
            var best = float.PositiveInfinity;
            for (var i = 1; i < line.Count; i++)
            {
                var (ax, az) = line[i - 1];
                var (bx, bz) = line[i];
                float dx = bx - ax, dz = bz - az;
                var length = dx * dx + dz * dz;
                var t = length > 1e-9f ? Math.Max(0f, Math.Min(1f, ((x - ax) * dx + (z - az) * dz) / length)) : 0f;
                var d = Sq(ax + t * dx - x) + Sq(az + t * dz - z);
                if (d < best)
                    best = d;
            }
            return (float)Math.Sqrt(best);
        }

        /// <summary>
        /// Whether a footprint at (<paramref name="x"/>, <paramref name="z"/>)
        /// stands clear of every other track: its middle at least
        /// <see cref="TrackClearance"/> and its half length along from each
        /// (a track can pass the footprint's corner aslant).
        /// </summary>
        public static bool ClearOfTracks(float x, float z, IEnumerable<IReadOnlyList<(float X, float Z)>> others)
        {
            if (others == null)
                return true;
            var reach = TrackClearance + HalfAlong;
            return others.All(line => DistanceToLine(x, z, line) >= reach);
        }

        /// <summary>
        /// The station yard a track's name gives ("[Y]_[CMS]_[P1]" gives
        /// "CMS"), or null. Some spawn tracks have generic IDs whose yard
        /// ("#Y") is no station's.
        /// </summary>
        public static string YardInTrackName(string name)
        {
            if (name == null || !name.StartsWith("[Y]_[", StringComparison.Ordinal))
                return null;
            var end = name.IndexOf(']', 5);
            return end > 5 ? name.Substring(5, end - 5) : null;
        }

        /// <summary>How far from its place's anchor a guest may ask for its spot, m: the spots reach 12 m along and 15 m aside, past a shed's stands.</summary>
        public const float GuestSpotReach = 30f;

        /// <summary>How much farther than the stocking range a guest may be when the host hears them, m: they may have walked on.</summary>
        public const float GuestRangeSlack = 30f;

        /// <summary>
        /// Whether the host stocks a supply place at the spot a guest found
        /// for it (multiplayer, 2026-11-12: a place stocked only while the
        /// host was near, so a guest elsewhere never saw one). The guest's
        /// game has the ground there; the host's may not. The spot must lie
        /// by the place, and the guest near enough to see it. Positions are
        /// unshifted, in the ground plane.
        /// </summary>
        public static bool AcceptsGuestSpot(float anchorX, float anchorZ, float spotX, float spotZ, float guestX, float guestZ, float stockingRange)
        {
            if (!Finite(anchorX) || !Finite(anchorZ) || !Finite(spotX) || !Finite(spotZ) || !Finite(guestX) || !Finite(guestZ) || !Finite(stockingRange))
                return false;
            var fromAnchor = (float)Math.Sqrt(Sq(spotX - anchorX) + Sq(spotZ - anchorZ));
            var fromGuest = (float)Math.Sqrt(Sq(spotX - guestX) + Sq(spotZ - guestZ));
            return fromAnchor <= GuestSpotReach && fromGuest <= stockingRange + GuestRangeSlack;
        }

        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        private static float Sq(float v) => v * v;
    }
}
