using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using VerletRope;

namespace RealisticFiring.Shed
{
 internal static class HoseGround
    {
        internal static readonly int GroundLayers = LayerMask.GetMask("Terrain", "Default");
        private const float SearchFrom = 0.5f;
        private const float SearchDepth = 6f;
        private const float Square = 0.15f;
        private const int SearchesPerFrame = 32;
        private const float SearchAgainAfter = 5f;
        private const float LayWithin = 60f;
        private const float Reach = 0.2f;

        /// <summary>The hose's centerline rests this far over the ground beyond its radius, m.</summary>
        public const float Clearance = 0.01f;

        private sealed class Lying
        {
            public RopeBehaviour Rope;
            public Transform Stand;
            public float Lift;
            public readonly Dictionary<long, Surfaces> Squares = new Dictionary<long, Surfaces>();
        }

        // A square's surfaces' heights in the rope's frame, highest first,
        // and when they are to be looked for again.
        private sealed class Surfaces
        {
            public float[] Tops;
            public float Until;
        }

        /// <summary>Whether <paramref name="collider"/> is ground a shed's hose or stand rests on: not a train, the player, or <paramref name="own"/>.</summary>
        internal static bool IsGround(Collider collider, Transform own)
        {
            var t = collider.transform;
            return !(own != null && t.IsChildOf(own)) && TrainCar.Resolve(collider.gameObject) == null
                   && !(PlayerManager.PlayerTransform != null && t.IsChildOf(PlayerManager.PlayerTransform));
        }

        private static readonly Dictionary<RopeMeshGenerator, Lying> Hoses = new Dictionary<RopeMeshGenerator, Lying>();

        private static int searchFrame;
        private static int searchesLeft;

        /// <summary>The height of a hose's centerline over the ground it rests on, m: its radius and a little.</summary>
        public static float Lift(RopeMeshGenerator mesh) => (mesh != null ? 0.5f * mesh.thickness : 0.03f) + Clearance;

        /// <summary>Lays <paramref name="hose"/> on the ground under it, its own <paramref name="stand"/> not taken as ground.</summary>
        public static void Follow(PropHose hose, Transform stand)
        {
            foreach (var gone in Hoses.Keys.Where(k => k == null).ToList())
                Hoses.Remove(gone);
            var rope = hose != null ? hose.GetComponent<RopeBehaviour>() : null;
            var mesh = rope != null ? rope.meshGenerator : null;
            if (mesh == null)
                return;
            Hoses[mesh] = new Lying { Rope = rope, Stand = stand, Lift = Lift(mesh) };
        }

        // The rope's mesh is made from its points in a job that follows the
        // rope's own; for a shed hose, the rope's job is finished first and
        // its points set on the ground.
        [HarmonyPatch(typeof(RopeMeshGenerator), "Schedule")]
        private static class SchedulePatch
        {
            private static void Prefix(RopeMeshGenerator __instance, JobHandle dependsOn)
            {
                if (Hoses.Count == 0 || !Hoses.TryGetValue(__instance, out var lying) || lying.Rope == null)
                    return;
                // Only a hose near the player, its rope's step finished here
                // for it: further off the rope's own floor holds it.
                if (PlayerManager.PlayerTransform != null
                    && (lying.Rope.transform.position - PlayerManager.PlayerTransform.position).sqrMagnitude > LayWithin * LayWithin)
                    return;
                dependsOn.Complete();
                var rope = lying.Rope.GetRope();
                if (rope == null || !rope.points.IsCreated)
                    return;
                Lay(lying, rope);
            }
        }

        // A point rests on the ground within this of it, m.
        private const float Resting = 0.005f;

        private static void Lay(Lying lying, Rope rope)
        {
            if (searchFrame != Time.frameCount)
            {
                searchFrame = Time.frameCount;
                searchesLeft = SearchesPerFrame;
            }
            var parameters = lying.Rope.ropeParams;
            var space = lying.Rope.transform;
            var friction = Mathf.Max(1e-3f, parameters.friction);
            var grip = Mathf.Clamp01(parameters.floorFriction / friction);
            var speedup = lying.Rope.solver != null ? lying.Rope.solver.simulationSpeedup : 1f;
            var fall = space.InverseTransformVector(parameters.gravity).y * Time.deltaTime * speedup;
            var relaxed = parameters.bendingCorrectionFactor * (parameters.floorBendingScale - 1f);
            var points = rope.points;
            for (var i = 0; i < points.Length; i++)
            {
                var point = points[i];
                if (point.pinned)
                    continue;
                var held = TryGround(lying, space, point.curPos, out var ground)
                           && ground + lying.Lift > parameters.floorLevel + Clearance
                           && point.curPos.y <= ground + lying.Lift + Resting;
                if (held)
                {
                    var moving = point.curPos - point.oldPos;
                    point.curPos.y = Mathf.Max(point.curPos.y, ground + lying.Lift);
                    point.oldPos = point.curPos - new float3(moving.x * grip, -fall / friction, moving.z * grip);
                    point.addedBendingCorrection = relaxed;
                }
                else if (point.addedBendingCorrection != 0f)
                {
                    point.addedBendingCorrection = 0f;
                }
                else
                {
                    continue;
                }
                points[i] = point;
            }
        }

        // The ground under a point in the rope's frame, its height in that frame.
        private static bool TryGround(Lying lying, Transform space, float3 at, out float height)
        {
            height = 0f;
            var key = ((long)Mathf.FloorToInt(at.x / Square) << 32) ^ (uint)Mathf.FloorToInt(at.z / Square);
            if ((!lying.Squares.TryGetValue(key, out var surfaces) || Time.time >= surfaces.Until) && searchesLeft > 0)
            {
                searchesLeft--;
                lying.Squares[key] = surfaces = Search(lying, space, at.x, at.z);
            }
            if (surfaces == null)
                return false;
            foreach (var top in surfaces.Tops)
            {
                if (top <= at.y + Reach)
                {
                    height = top;
                    return true;
                }
            }
            return false;
        }

        private static Surfaces Search(Lying lying, Transform space, float x, float z)
        {
            var from = space.TransformPoint(new Vector3(x, SearchFrom, z));
            var tops = Physics.RaycastAll(from, Vector3.down, SearchDepth, GroundLayers, QueryTriggerInteraction.Ignore)
                .Where(hit => IsGround(hit.collider, lying.Stand))
                .Select(hit => space.InverseTransformPoint(new Vector3(from.x, hit.point.y, from.z)).y)
                .OrderByDescending(y => y)
                .ToArray();
            return new Surfaces { Tops = tops, Until = Time.time + SearchAgainAfter };
        }
    }
}
