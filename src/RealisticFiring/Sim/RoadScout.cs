using System.Collections.Generic;
using System.Linq;
using DV.PointSet;
using RealisticFiring.Core;
using UnityEngine;

namespace RealisticFiring.Sim
{
 internal static class RoadScout
    {
        // Each track's points as the road reads them; the game's point sets
        // do not change once built.
        private static readonly Dictionary<RailTrack, TrackLine> Lines = new Dictionary<RailTrack, TrackLine>();

        private sealed class TrackLine : IRoadTrack
        {
            private readonly double[] spans;
            private readonly float[] heights;

            public TrackLine(EquiPointSet set)
            {
                var points = set?.points ?? new EquiPointSet.Point[0];
                spans = points.Select(p => p.span).ToArray();
                heights = points.Select(p => (float)p.position.y).ToArray();
            }

            public int Count => spans.Length;
            public double Span(int index) => spans[index];
            public float Height(int index) => heights[index];
        }

        private static IRoadTrack Line(RailTrack track)
        {
            if (track == null)
                return null;
            if (!Lines.TryGetValue(track, out var line))
                Lines[track] = line = new TrackLine(track.GetKinkedPointSet());
            return line;
        }

        // The track beyond an end, and whether the road enters it at its start.
        private static (RailTrack Track, bool AtStart)? Beyond(RailTrack track, bool atEnd)
        {
            var branch = atEnd ? track.GetOutBranch() : track.GetInBranch();
            return branch != null && branch.track != null ? (branch.track, branch.first) : ((RailTrack, bool)?)null;
        }

        /// <summary>
        /// The road ahead of <paramref name="car"/> for <paramref name="distance"/>
        /// m, going forward (along the car) or not; empty where the car is not
        /// on the track.
        /// </summary>
        public static List<RoadPoint> Ahead(TrainCar car, bool forward, float distance, float spacing)
        {
            var bogies = car != null ? car.Bogies : null;
            if (bogies == null || bogies.Length == 0)
                return new List<RoadPoint>();
            var bogie = forward ? car.FrontBogie : car.RearBogie;
            if (bogie == null || bogie.track == null || bogie.traveller == null || bogie.HasDerailed)
                return new List<RoadPoint>();
            // The bogie faces along the track's span when its direction is positive.
            var alongSpan = (forward ? 1f : -1f) * bogie.TrackDirectionSign > 0f;
            return RoadAhead.Walk(bogie.track, bogie.traveller.Span, alongSpan, distance, spacing, Line, Beyond);
        }

        /// <summary>The weight of the train <paramref name="car"/> is in, in kg.</summary>
        public static float TrainMass(TrainCar car)
        {
            var cars = car != null && car.trainset != null ? car.trainset.cars : null;
            if (cars == null)
                return car != null && car.massController != null ? car.massController.TotalMass : 0f;
            var mass = 0f;
            foreach (var each in cars)
                if (each != null && each.massController != null)
                    mass += each.massController.TotalMass;
            return mass;
        }
    }
}
