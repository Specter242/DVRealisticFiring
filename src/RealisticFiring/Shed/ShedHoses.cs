using System.Collections.Generic;
using System.Linq;
using DV.CabControls;
using DV.Interaction;
using DV.ThingTypes;
using Unity.Mathematics;
using UnityEngine;
using VerletRope;

namespace RealisticFiring.Shed
{
 internal sealed class HoseKind
    {
        private HoseKind(string name, string tag, float length, float diameter, float fittings)
        {
            Name = name;
            Tag = tag;
            Length = length;
            Diameter = diameter;
            Fittings = fittings;
        }

 public static readonly HoseKind BlowerLine = new HoseKind("blower line", "rf-blower-line", 12f, BlowerFittings.HoseDiameter, 0.6f);

        public string Name { get; }

        /// <summary>The plug's and its sockets' connection tag.</summary>
        public string Tag { get; }

        public float Length { get; }

        /// <summary>The hose's outside diameter, m.</summary>
        public float Diameter { get; }

        /// <summary>How near its plug and the engines' sockets find each other, against the station's diesel ones.</summary>
        public float Fittings { get; }
    }

    /// <summary>
    /// Shed hoses made from the game's own: a service station's diesel hose
    /// (its <c>…HoseAddon</c>: the rope from <c>HoseSource</c>, the plug, and
    /// its stand's socket), copied while inactive, retagged, and lengthened,
    /// thinned, and set on its own floor before it wakes.
    /// </summary>
    internal static class ShedHoses
    {
 public const float PointSpacing = 0.15f;

        private static GameObject template;

        /// <summary>The station hose to copy, or null before the stations load.</summary>
        public static GameObject Template()
        {
            if (template != null)
                return template;
            var module = Object.FindObjectsOfType<LocoResourceModule>()
                .FirstOrDefault(m => m != null && m.resourceType == ResourceType.Fuel && m.resourceHose != null && m.resourceHose.transform.parent != null);
            template = module != null ? module.resourceHose.transform.parent.gameObject : null;
            return template;
        }

        /// <summary>The station's socket on the copied hose's stand, for its sounds.</summary>
        public static PlugSocket TemplateSocket() =>
            Template() != null ? Template().GetComponentInChildren<PropHose>(true)?.stationSocket : null;

        /// <summary>
        /// Builds a stand with its hose of <paramref name="kind"/> under
        /// <paramref name="parent"/>: the station's whole diesel stand (its
        /// standpipe and gooseneck, the hose from the gooseneck, the plug
        /// resting in its socket on the riser) set down at <paramref name="root"/>,
        /// its hose <paramref name="length"/> m (the kind's when null), lying
        /// on a floor at <paramref name="floorY"/> (world height). Null without
        /// a station hose to copy.
        /// </summary>
        public static PropHose Build(HoseKind kind, Transform parent, Pose root, float floorY, float? length = null)
        {
            var hoseLength = length ?? kind.Length;
            var original = Template();
            if (original == null)
                return null;
            var holder = new GameObject("RealisticFiring hose (being built)");
            holder.SetActive(false);
            var copy = Object.Instantiate(original, holder.transform, false);
            copy.name = $"RealisticFiring {kind.Name}";

            StripRuntimeParts(copy);

            var hose = copy.GetComponentInChildren<PropHose>(true);
            if (hose == null || hose.plug == null || hose.stationSocket == null)
            {
                Object.Destroy(holder);
                return null;
            }
            hose.plug.connectionTag = kind.Tag;
            hose.stationSocket.connectionTag = kind.Tag;
            hose.floorTouchingCollider = null;

            var filter = hose.GetComponent<MeshFilter>();
            if (filter != null)
                filter.sharedMesh = new Mesh { name = $"RealisticFiring {kind.Name} rope" };
            copy.transform.SetPositionAndRotation(root.position, root.rotation);

            var rope = hose.GetComponent<RopeBehaviour>();
            var mesh = hose.GetComponent<RopeMeshGenerator>();
            if (rope != null)
            {
                var parameters = rope.ropeParams;
                var lastBefore = parameters.numPoints - 1;
                // The rope's texture runs its scale's times over its whole
                // length: kept as dense as on the station's hose.
                if (mesh != null && parameters.ropeLength > 0f)
                    mesh.uvScale *= hoseLength / parameters.ropeLength;
                parameters.ropeLength = hoseLength;
                parameters.numPoints = Mathf.Max(parameters.numPoints, Mathf.CeilToInt(hoseLength / PointSpacing) + 1);
                var lastNow = parameters.numPoints - 1;
                for (var i = 0; i < rope.pins.Count; i++)
                {
                    var pin = rope.pins[i];
                    if (pin.pointIndex > lastBefore / 2)
                    {
                        pin.pointIndex = lastNow - (lastBefore - pin.pointIndex);
                        rope.pins[i] = pin;
                    }
                }
                if (!pinsLogged && Main.Settings.LogFireboxCreation)
                {
                    pinsLogged = true;
                    Main.Mod.Logger.Log($"Shed hose rope: {parameters.numPoints} points over {hoseLength:F0} m, pinned " +
                                        string.Join(", ", rope.pins.Select(p => $"{p.pointIndex} to '{(p.pinnedToTransform != null ? p.pinnedToTransform.name : "nothing")}'" +
                                                                                (p.pinnedToTransform != null && !p.pinnedToTransform.IsChildOf(copy.transform) ? " (not this hose's!)" : ""))));
                }
                if (mesh != null)
                    mesh.thickness = kind.Diameter;
                parameters.floorLevel = FloorLevel(rope, floorY);
            }

            ScaleSenses(hose.plug.transform, kind.Fittings);
            BlowerFittings.HoseEnd(hose.plug, hose.GetComponent<MeshRenderer>()?.sharedMaterial,
                mesh != null ? mesh.uvScale / hoseLength : 1f);
            BlowerFittings.GooseneckEnd(hose.transform, hose.transform.Find("HoseLead"), hose.gameObject.layer);
            copy.transform.SetParent(parent, true);
            Object.Destroy(holder);
            if (rope != null)
                CoilAtFoot(rope, hose, floorY, hoseLength);
            HoseGround.Follow(hose, parent);
            StandPlugWatch.Watch(hose);
            return hose;
        }

        private static bool pinsLogged;

        // How near the plug finds a socket: its sensing spheres, made smaller
        // with the coupling the plug now is.
        private static void ScaleSenses(Transform plug, float scale)
        {
            foreach (var sphere in plug.GetComponents<SphereCollider>())
            {
                sphere.radius *= scale;
                sphere.center *= scale;
            }
        }

        private static void StripRuntimeParts(GameObject copy)
        {
            bool Added(Component c) =>
                c is ControlImplBase || c is AGrabHandler || c is CollisionSound
                || c.GetType().Name == "PluggableObjectKinematicVRFix"
                || c.GetType().Name.StartsWith("Networked")
                || (c.GetType().Namespace ?? "").StartsWith("VRTK");
            // Dependents first: a grab handler before the control that needs it.
            for (var pass = 0; pass < 3; pass++)
            {
                foreach (var part in copy.GetComponentsInChildren<MonoBehaviour>(true).Where(c => c != null && Added(c)).ToList())
                    Object.DestroyImmediate(part);
            }
        }

        private const float CoilInner = 0.25f;
        private const float CoilOuter = 0.6f;
        internal static readonly Vector3 CoilAt = new Vector3(0.9f, 0f, -0.9f);

        /// <summary>Lays the hose's floor at <paramref name="floorY"/>, world height.</summary>
        public static void SetFloor(PropHose hose, float floorY)
        {
            var rope = hose != null ? hose.GetComponent<RopeBehaviour>() : null;
            if (rope != null)
                rope.ropeParams.floorLevel = FloorLevel(rope, floorY);
        }

        private static float FloorLevel(RopeBehaviour rope, float floorY) =>
            (floorY + HoseGround.Lift(rope.meshGenerator) - rope.transform.position.y) / Mathf.Max(1e-4f, rope.transform.lossyScale.y);

        private static void CoilAtFoot(RopeBehaviour behaviour, PropHose hose, float floorY, float length)
        {
            var rope = behaviour.GetRope();
            if (rope == null || !rope.points.IsCreated || rope.points.Length < 3)
                return;
            var frame = hose.transform.parent != null ? hose.transform.parent : hose.transform;
            var start = hose.hoseStationOrigin.position;
            var end = hose.plug.transform.position;
            var lift = Vector3.up * HoseGround.Lift(behaviour.meshGenerator);
            var centre = frame.TransformPoint(CoilAt);
            centre.y = floorY;

            var path = new List<Vector3> { start, new Vector3(start.x, floorY, start.z) + lift };
            var fixedLength = (start.y - floorY) + (end.y - floorY);
            var spiralLength = length - fixedLength - Vector3.Distance(path[1], centre) - Vector3.Distance(centre, new Vector3(end.x, floorY, end.z));
            if (spiralLength > 1f)
            {
                var meanRadius = 0.5f * (CoilInner + CoilOuter);
                var turns = spiralLength / (2f * Mathf.PI * meanRadius);
                var steps = Mathf.Max(8, Mathf.CeilToInt(turns * 24f));
                for (var i = 0; i <= steps; i++)
                {
                    var f = (float)i / steps;
                    var angle = f * turns * 2f * Mathf.PI;
                    var radius = Mathf.Lerp(CoilOuter, CoilInner, f);
                    path.Add(centre + lift + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius);
                }
            }
            path.Add(new Vector3(end.x, floorY, end.z) + lift);
            path.Add(end);

            // Spread the rope's points evenly along the path.
            var total = 0f;
            var lengths = new float[path.Count];
            for (var i = 1; i < path.Count; i++)
                lengths[i] = total += Vector3.Distance(path[i - 1], path[i]);
            var points = rope.points;
            var count = points.Length;
            var space = behaviour.transform;
            var segment = 1;
            for (var i = 0; i < count; i++)
            {
                var point = points[i];
                if (point.pinned)
                    continue;
                var at = total * i / (count - 1);
                while (segment < path.Count - 1 && lengths[segment] < at)
                    segment++;
                var span = Mathf.Max(1e-5f, lengths[segment] - lengths[segment - 1]);
                var world = Vector3.Lerp(path[segment - 1], path[segment], (at - lengths[segment - 1]) / span);
                var local = (float3)space.InverseTransformPoint(world);
                point.curPos = local;
                point.oldPos = local;
                points[i] = point;
            }
        }
    }
}
