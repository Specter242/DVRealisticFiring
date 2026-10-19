using System;
using System.Collections.Generic;

namespace RealisticFiring.Core
{
    /// <summary>A point on the road ahead: how far along it, in m, and its height, in m.</summary>
    public struct RoadPoint
    {
        public RoadPoint(float distance, float height)
        {
            Distance = distance;
            Height = height;
        }

        public float Distance;
        public float Height;
    }

    /// <summary>A track's points along its length, in increasing span: span in m, height in m.</summary>
    public interface IRoadTrack
    {
        int Count { get; }
        double Span(int index);
        float Height(int index);
    }

 public static class RoadAhead
    {
        /// <summary>
        /// The road from <paramref name="span"/> on <paramref name="track"/>,
        /// going toward its end (<paramref name="forward"/>) or its start, for
        /// <paramref name="distance"/> m, a point about every
        /// <paramref name="spacing"/> m. <paramref name="next"/> gives the track
        /// beyond an end (at its end when <c>true</c>) and whether the road
        /// enters it at its start, or null where the road ends; the route stops
        /// after <paramref name="maxTracks"/> tracks.
        /// </summary>
        public static List<RoadPoint> Walk<T>(T track, double span, bool forward, float distance, float spacing,
            Func<T, IRoadTrack> points, Func<T, bool, (T Track, bool AtStart)?> next, int maxTracks = 400)
            where T : class
        {
            var road = new List<RoadPoint>();
            if (track == null || points == null || next == null || !(distance > 0f))
                return road;
            spacing = Math.Max(1f, spacing);
            var travelled = 0f;
            var at = span;
            var dir = forward ? 1 : -1;
            var current = track;
            var nextSample = 0f;
            for (var tracks = 0; tracks < maxTracks && current != null; tracks++)
            {
                var line = points(current);
                if (line == null || line.Count < 2)
                    break;
                // The first point at or past where the road stands, going its way.
                var i = dir > 0 ? 0 : line.Count - 1;
                while (i >= 0 && i < line.Count && (dir > 0 ? line.Span(i) < at : line.Span(i) > at))
                    i += dir;
                if (road.Count == 0)
                {
                    road.Add(new RoadPoint(0f, HeightAt(line, at)));
                    nextSample = spacing;
                }
                for (; i >= 0 && i < line.Count; i += dir)
                {
                    var along = travelled + (float)Math.Abs(line.Span(i) - at);
                    if (along >= nextSample)
                    {
                        road.Add(new RoadPoint(along, line.Height(i)));
                        nextSample = along + spacing;
                    }
                    if (along >= distance)
                        return road;
                }
                var end = dir > 0 ? line.Span(line.Count - 1) : line.Span(0);
                travelled += (float)Math.Abs(end - at);
                var beyond = next(current, dir > 0);
                if (!beyond.HasValue || beyond.Value.Track == null)
                    break;
                current = beyond.Value.Track;
                var entered = points(current);
                if (entered == null || entered.Count < 2)
                    break;
                at = beyond.Value.AtStart ? entered.Span(0) : entered.Span(entered.Count - 1);
                dir = beyond.Value.AtStart ? 1 : -1;
            }
            return road;
        }

        // The height at a span between a track's points.
        private static float HeightAt(IRoadTrack line, double span)
        {
            if (span <= line.Span(0))
                return line.Height(0);
            for (var i = 1; i < line.Count; i++)
            {
                if (span <= line.Span(i))
                {
                    var t = (float)((span - line.Span(i - 1)) / Math.Max(1e-6, line.Span(i) - line.Span(i - 1)));
                    return line.Height(i - 1) + t * (line.Height(i) - line.Height(i - 1));
                }
            }
            return line.Height(line.Count - 1);
        }

        /// <summary>The road's height at <paramref name="distance"/> along it; NaN past its end.</summary>
        public static float HeightAt(IReadOnlyList<RoadPoint> road, float distance)
        {
            if (road == null || road.Count == 0 || distance > road[road.Count - 1].Distance)
                return float.NaN;
            if (distance <= road[0].Distance)
                return road[0].Height;
            for (var i = 1; i < road.Count; i++)
            {
                if (distance <= road[i].Distance)
                {
                    var span = road[i].Distance - road[i - 1].Distance;
                    var t = span > 0f ? (distance - road[i - 1].Distance) / span : 0f;
                    return road[i - 1].Height + t * (road[i].Height - road[i - 1].Height);
                }
            }
            return road[road.Count - 1].Height;
        }

        /// <summary>The average grade from <paramref name="from"/> to <paramref name="to"/> m along the road (0.01 is 1 %); NaN where the road does not reach.</summary>
        public static float Grade(IReadOnlyList<RoadPoint> road, float from, float to)
        {
            if (!(to > from))
                return float.NaN;
            var a = HeightAt(road, from);
            var b = HeightAt(road, to);
            return float.IsNaN(a) || float.IsNaN(b) ? float.NaN : (b - a) / (to - from);
        }

        /// <summary>
        /// The steepest climb (the greatest average grade over
        /// <paramref name="window"/> m) starting between <paramref name="from"/>
        /// and <paramref name="to"/> m along the road; NaN where none fits.
        /// </summary>
        public static float Steepest(IReadOnlyList<RoadPoint> road, float from, float to, float window, float step = 25f)
        {
            var steepest = float.NaN;
            for (var start = Math.Max(0f, from); start <= to; start += Math.Max(1f, step))
            {
                var grade = Grade(road, start, start + window);
                if (!float.IsNaN(grade) && (float.IsNaN(steepest) || grade > steepest))
                    steepest = grade;
            }
            return steepest;
        }

        /// <summary>
        /// The steepest descent (the least average grade over
        /// <paramref name="window"/> m, negative going down) starting between
        /// <paramref name="from"/> and <paramref name="to"/> m along the road;
        /// NaN where none fits.
        /// </summary>
        public static float SteepestDescent(IReadOnlyList<RoadPoint> road, float from, float to, float window, float step = 25f)
        {
            var steepest = float.NaN;
            for (var start = Math.Max(0f, from); start <= to; start += Math.Max(1f, step))
            {
                var grade = Grade(road, start, start + window);
                if (!float.IsNaN(grade) && (float.IsNaN(steepest) || grade < steepest))
                    steepest = grade;
            }
            return steepest;
        }
    }

    /// <summary>
    /// How hard the engine works for its pull, as its fireman learns it: the
    /// work he hears against the train's weight lifted and rolled at its
    /// speed, followed over steady running. With it he turns a grade ahead
    /// into the work it will take.
    /// </summary>
    public sealed class RoadReckoner
    {
        public const float Gravity = 9.81f;

        /// <summary>Rolling and curve resistance, as a grade: about 4 lb a ton on good track.</summary>
        public const float Rolling = 0.002f;

        /// <summary>He learns the ratio over this long of steady running, in s.</summary>
        public const float LearnTime = 120f;

        // He learns only from a pull of at least this share of what he
        // reckons full work pulls, and work he hears of at least this.
        private const float LeastLoadShare = 0.1f;
        private const float LeastWork = 0.1f;

        /// <param name="fullWorkPower">What he reckons the engine pulls at full work, in W, to start from.</param>
        public RoadReckoner(float fullWorkPower)
        {
            FullWorkPower = fullWorkPower > 0f ? fullWorkPower : 1e6f;
            WorkPerWatt = 1f / FullWorkPower;
        }

        public float FullWorkPower { get; }

        /// <summary>The work he hears per W of pull, as he has learned it.</summary>
        public float WorkPerWatt { get; private set; }

        /// <summary>The pull of a train of <paramref name="mass"/> kg at <paramref name="speed"/> m/s on <paramref name="grade"/>, in W.</summary>
        public static float Pull(float mass, float speed, float grade) =>
            Math.Max(0f, mass) * Gravity * Math.Abs(speed) * (grade + Rolling);

        /// <summary>
        /// The work he expects where the road climbs at
        /// <paramref name="gradeThere"/>, from the work he hears on
        /// <paramref name="gradeNow"/>: what he hears, changed by the pull the
        /// difference makes at this speed. 0 or more.
        /// </summary>
        public float WorkAt(float heardWork, float mass, float speed, float gradeNow, float gradeThere)
        {
            if (float.IsNaN(gradeNow) || float.IsNaN(gradeThere))
                return heardWork;
            var change = Math.Max(0f, mass) * Gravity * Math.Abs(speed) * (gradeThere - gradeNow);
            return Math.Max(0f, heardWork + WorkPerWatt * change);
        }

        /// <summary>Learns from steady running on <paramref name="grade"/>.</summary>
        public void Learn(float dt, float heardWork, float mass, float speed, float grade, bool steady)
        {
            if (!steady || !(dt > 0f) || float.IsNaN(grade) || heardWork < LeastWork)
                return;
            var pull = Pull(mass, speed, grade);
            if (pull < LeastLoadShare * FullWorkPower)
                return;
            var seen = heardWork / pull;
            // Within reason: a tenth to ten times where he started.
            seen = Math.Max(0.1f / FullWorkPower, Math.Min(10f / FullWorkPower, seen));
            WorkPerWatt += (seen - WorkPerWatt) * Math.Min(1f, dt / LearnTime);
        }
    }
}
