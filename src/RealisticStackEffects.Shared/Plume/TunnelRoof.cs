using System.Collections.Generic;
using RealisticStackEffects.Core;
using UnityEngine;

namespace RealisticStackEffects
{
 internal sealed class TunnelRoof
    {
        // A sample every this far the stack travels, m; found from this far
        // along it and this far across.
        private const float Spacing = 2f;
        private const float AlongReach = 0.75f * Spacing;
        private const float AcrossReach = TunnelSmoke.HalfWidth + 3f;
        // The samples are filed by this size of square, m, at least the reach across.
        private const float Cell = 6f;
        private const int MostSamples = 4096;
        // A ceiling higher than this over the rail is no tunnel's, m.
        private const float HighestRoof = 30f;

        public struct Sample
        {
            /// <summary>Over the track under the stack, at the roof's height, in the smoke's space.</summary>
            public Vector3 Point;
            /// <summary>Along the track and across it, level, in the smoke's space.</summary>
            public Vector3 Along;
            public Vector3 Across;
            /// <summary>The roof's height above the rail, m.</summary>
            public float Height;
            public float Time;
        }

        private readonly Sample[] ring = new Sample[MostSamples];
        private int oldest;
        private int count;
        private readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();
        private Vector3 lastPoint;
        private bool sampled;

        /// <summary>The roof over the stack now, or null in the open.</summary>
        public Sample? OverStack { get; private set; }

        /// <summary>
        /// Samples the roof over the stack at <paramref name="stack"/> (world)
        /// when it has moved on, in <paramref name="space"/>'s simulation
        /// space, and forgets samples older than <paramref name="keep"/> s.
        /// </summary>
        public void Follow(TrainCar car, Vector3 stack, ParticleSystem space, float keep)
        {
            var now = Time.time;
            while (count > 0 && now - ring[oldest].Time > keep)
                Drop();
            var point = ChimneyPlume.SimulationPoint(space, stack);
            if (sampled && Flat(point - lastPoint).sqrMagnitude < Spacing * Spacing)
                return;
            sampled = true;
            lastPoint = point;
            OverStack = null;

            var bogie = car != null && car.Bogies != null && car.Bogies.Length > 0 ? car.FrontBogie : null;
            if (bogie == null || bogie.track == null || bogie.traveller == null)
                return;
            // The ceiling at the stack's own place on the track, ahead of or
            // behind the bogie, so the roof starts and ends at the portals.
            var rail = bogie.transform.position;
            var forward = bogie.traveller.worldForward;
            var span = (float)bogie.traveller.Span + Vector3.Dot(stack - rail, forward);
            var height = bogie.track.SampleCeilingHeight(span);
            if (!(height > 0f && height < HighestRoof))
                return;

            var along = Flat(Direction(space, forward)).normalized;
            if (along.sqrMagnitude < 0.5f)
                return;
            var roof = ChimneyPlume.SimulationPoint(space, new Vector3(stack.x, rail.y + height, stack.z));
            var sample = new Sample
            {
                Point = roof,
                Along = along,
                Across = Vector3.Cross(Vector3.up, along),
                Height = height,
                Time = now,
            };
            Add(sample);
            OverStack = sample;
        }

        /// <summary>The roof over <paramref name="position"/>, in the smoke's space; false in the open.</summary>
        public bool TryFind(Vector3 position, out Sample roof)
        {
            roof = default;
            if (count == 0)
                return false;
            var x = Mathf.FloorToInt(position.x / Cell);
            var z = Mathf.FloorToInt(position.z / Cell);
            var best = float.PositiveInfinity;
            for (var dx = -1; dx <= 1; dx++)
            for (var dz = -1; dz <= 1; dz++)
            {
                if (!cells.TryGetValue(Key(x + dx, z + dz), out var list))
                    continue;
                foreach (var index in list)
                {
                    var sample = ring[index];
                    var offset = position - sample.Point;
                    var along = Mathf.Abs(Vector3.Dot(offset, sample.Along));
                    if (along > AlongReach || along >= best || Mathf.Abs(Vector3.Dot(offset, sample.Across)) > AcrossReach)
                        continue;
                    best = along;
                    roof = sample;
                }
            }
            return best < float.PositiveInfinity;
        }

        private void Add(Sample sample)
        {
            if (count == MostSamples)
                Drop();
            var index = (oldest + count) % MostSamples;
            ring[index] = sample;
            count++;
            var key = KeyOf(sample.Point);
            if (!cells.TryGetValue(key, out var list))
                cells[key] = list = new List<int>(4);
            list.Add(index);
        }

        private void Drop()
        {
            var key = KeyOf(ring[oldest].Point);
            if (cells.TryGetValue(key, out var list))
            {
                list.Remove(oldest);
                if (list.Count == 0)
                    cells.Remove(key);
            }
            oldest = (oldest + 1) % MostSamples;
            count--;
        }

        private static long KeyOf(Vector3 point) => Key(Mathf.FloorToInt(point.x / Cell), Mathf.FloorToInt(point.z / Cell));

        private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private static Vector3 Direction(ParticleSystem system, Vector3 world)
        {
            var main = system.main;
            switch (main.simulationSpace)
            {
                case ParticleSystemSimulationSpace.Local:
                    return system.transform.InverseTransformDirection(world);
                case ParticleSystemSimulationSpace.Custom when main.customSimulationSpace != null:
                    return main.customSimulationSpace.InverseTransformDirection(world);
                default:
                    return world;
            }
        }
    }
}
