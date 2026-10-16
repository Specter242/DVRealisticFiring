using System.Collections.Generic;
using System.Linq;
using DV.CabControls.Spec;
using UnityEngine;

namespace RealisticFiring.Visuals
{
    /// <summary>
    /// The look of a cab's own valve handwheel, so valves the mod adds can
    /// wear it: the meshes and materials of a turning control (a wheel or a
    /// rotary) on the backhead, a flat disc a hand's span across, taken from
    /// the one nearest a point. A valve dressed in it keeps its own size,
    /// control, and collider; only its wheel is drawn anew.
    /// </summary>
    internal sealed class HandwheelLook
    {
        private const string LookName = "[RealisticFiring handwheel]";

        // A handwheel is this wide, m, no thicker than this share of its width,
        // and faces the cab: its axis this close to the loco's length.
        private const float MinDiameter = 0.06f;
        private const float MaxDiameter = 0.35f;
        private const float MaxThickness = 0.6f;
        private const float MinFacing = 0.7f;

        // Each mesh in metres in its control's turned frame (rotation only).
        private readonly List<(Mesh Mesh, Material[] Materials, Vector3 Position, Quaternion Rotation, Vector3 Scale)> parts;

        // The wheel's axis in its control's frame, pointing out into the cab, and its width, m.
        private readonly Vector3 outward;
        private readonly float diameter;

        private HandwheelLook(string name, Vector3 outward, float diameter,
            List<(Mesh, Material[], Vector3, Quaternion, Vector3)> parts)
        {
            Name = name;
            this.outward = outward;
            this.diameter = diameter;
            this.parts = parts;
        }

        /// <summary>The handwheel's object in its cab, for the log.</summary>
        public string Name { get; }

        /// <summary>The wheel's width, m.</summary>
        public float Diameter => diameter;

        /// <summary>The cab's own handwheel nearest <paramref name="near"/>, or null when it has none.</summary>
        public static HandwheelLook Find(Transform interior, Transform car, Vector3 near)
        {
            HandwheelLook best = null;
            var bestDistance = float.MaxValue;
            foreach (var spec in interior.GetComponentsInChildren<ControlSpec>(true))
            {
                var axis = Axis(spec);
                if (axis.sqrMagnitude < 1e-6f || IsOurs(spec.transform))
                    continue;
                var distance = (spec.transform.position - near).sqrMagnitude;
                if (distance >= bestDistance)
                    continue;
                var look = Measure(spec.transform, axis, car);
                if (look == null)
                    continue;
                best = look;
                bestDistance = distance;
            }
            return best;
        }

        /// <summary>
        /// The look of a loose handwheel on a backhead, a mesh named for it
        /// with no control behind it, turning about the loco's length (its
        /// mesh's bounds say nothing: the 2901's are cubes), or null without
        /// a mesh. Being named, it is taken as it is.
        /// </summary>
        public static HandwheelLook OfLoose(Transform wheel, Transform car)
        {
            var filter = wheel != null ? wheel.GetComponent<MeshFilter>() : null;
            if (filter == null || filter.sharedMesh == null)
                return null;
            return Measure(wheel, car.forward, car, turned: true, checks: false);
        }

        /// <summary>The wheel's center and its axis into the backhead, in the world, for putting a working valve in its place.</summary>
        public static bool TryPlaceOf(Transform wheel, Transform car, out Vector3 center, out Vector3 intoBackhead)
        {
            center = wheel.position;
            intoBackhead = car.forward;
            var filter = wheel.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
                return false;
            center = wheel.TransformPoint(filter.sharedMesh.bounds.center);
            return true;
        }

        /// <summary>Draws <paramref name="control"/>'s wheel in this look, at the wheel's own size; false when it has no wheel.</summary>
        public bool Dress(Transform control, Transform car) => Dress(control, car, false);

        /// <summary>Draws <paramref name="control"/>'s wheel in this look, at the look's size when <paramref name="lookSize"/>.</summary>
        public bool Dress(Transform control, Transform car, bool lookSize)
        {
            var spec = control.GetComponent<ControlSpec>();
            var axis = spec != null ? Axis(spec) : Vector3.zero;
            var renderers = Renderers(control).ToList();
            if (axis.sqrMagnitude < 1e-6f || renderers.Count == 0)
                return false;
            var size = lookSize ? diameter : Measure(control, renderers, Outward(control, axis, car), out _);
            if (!(size > 0f))
                return false;
            foreach (var renderer in renderers)
                renderer.enabled = false;

            var look = new GameObject(LookName).transform;
            look.SetParent(control, false);
            look.localRotation = Quaternion.FromToRotation(outward, Outward(control, axis, car));
            // Metres in the look; the control's own scale taken out (taken as even).
            var lossy = control.lossyScale;
            var even = (Mathf.Abs(lossy.x) + Mathf.Abs(lossy.y) + Mathf.Abs(lossy.z)) / 3f;
            look.localScale = Vector3.one * (size / diameter / Mathf.Max(1e-6f, even));
            foreach (var (mesh, materials, position, rotation, scale) in parts)
            {
                var part = new GameObject(mesh.name).transform;
                part.SetParent(look, false);
                part.localPosition = position;
                part.localRotation = rotation;
                part.localScale = scale;
                part.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                part.gameObject.AddComponent<MeshRenderer>().sharedMaterials = materials;
            }
            Outline(control, look);
            return true;
        }

        // The hover outline draws the renderers of the first highlight tag
        // under a control, hidden or not (GrabberHoverHighlight): the tags on
        // the control, under it, and on the copied valve's root (or a new one)
        // now outline the look instead of the hidden wheel. Awake adds the
        // tag's target's own renderer, so the target is the look too.
        private static void Outline(Transform control, Transform look)
        {
            var drawn = look.GetComponentsInChildren<MeshRenderer>(true).Cast<Renderer>().ToList();
            if (drawn.Count == 0)
                return;
            var tags = control.GetComponentsInChildren<HighlightTag>(true).ToList();
            if (control.parent != null && control.parent.GetComponent<HighlightTag>() is HighlightTag holder)
                tags.Add(holder);
            // Without a tag the hover outlines the first renderer under the control: the hidden wheel.
            if (tags.Count == 0)
                tags.Add(control.gameObject.AddComponent<HighlightTag>());
            foreach (var tag in tags.Distinct())
            {
                tag.renderers = new List<Renderer>(drawn);
                tag.targetObject = drawn[0].gameObject;
            }
        }

        private static Vector3 Axis(ControlSpec spec) =>
            (spec is Wheel wheel ? wheel.jointAxis : spec is Rotary rotary ? rotary.jointAxis : Vector3.zero).normalized;

        private static bool IsOurs(Transform transform)
        {
            for (var t = transform; t != null; t = t.parent)
            {
                if (t.name.StartsWith("[RealisticFiring", System.StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        // A control's wheel, its axis in the control's local frame; or, turned,
        // an axis already in the control's turned (rotation-only) frame.
        private static HandwheelLook Measure(Transform control, Vector3 axis, Transform car, bool turned = false, bool checks = true)
        {
            var renderers = Renderers(control).ToList();
            if (renderers.Count == 0)
                return null;
            if (turned)
                axis = (Quaternion.Inverse(control.rotation) * axis).normalized;
            var outward = Outward(control, axis, car);
            if (checks && Mathf.Abs(Vector3.Dot(control.rotation * outward, car.forward)) < MinFacing)
                return null;
            var size = Measure(control, renderers, outward, out var thickness);
            if (!(size > 0f) || checks && (size < MinDiameter || size > MaxDiameter || thickness > MaxThickness * size))
                return null;
            var turn = Quaternion.Inverse(control.rotation);
            var parts = renderers.Select(r => (r.GetComponent<MeshFilter>().sharedMesh, r.sharedMaterials,
                turn * (r.transform.position - control.position), turn * r.transform.rotation, r.transform.lossyScale)).ToList();
            return new HandwheelLook(control.name, outward, size, parts);
        }

        // The wheel's meshes: everything drawn under the control but a look already put on it.
        private static IEnumerable<MeshRenderer> Renderers(Transform control)
        {
            var look = control.Find(LookName);
            foreach (var renderer in control.GetComponentsInChildren<MeshRenderer>(true))
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null && (look == null || !renderer.transform.IsChildOf(look)))
                    yield return renderer;
            }
        }

        // The axis turned to point away from the backhead, into the cab.
        private static Vector3 Outward(Transform control, Vector3 axis, Transform car) =>
            Vector3.Dot(control.rotation * axis, car.forward) > 0f ? -axis : axis;

        // The wheel's width across its axis and its thickness along it, in
        // metres, from its meshes' bounds.
        private static float Measure(Transform control, List<MeshRenderer> renderers, Vector3 axis, out float thickness)
        {
            var toAxis = Quaternion.FromToRotation(axis, Vector3.forward) * Quaternion.Inverse(control.rotation);
            var box = new Bounds();
            var first = true;
            foreach (var renderer in renderers)
            {
                var bounds = renderer.GetComponent<MeshFilter>().sharedMesh.bounds;
                for (var i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        (i & 1) == 0 ? bounds.min.x : bounds.max.x,
                        (i & 2) == 0 ? bounds.min.y : bounds.max.y,
                        (i & 4) == 0 ? bounds.min.z : bounds.max.z);
                    var point = toAxis * (renderer.transform.TransformPoint(corner) - control.position);
                    if (first)
                        box = new Bounds(point, Vector3.zero);
                    else
                        box.Encapsulate(point);
                    first = false;
                }
            }
            thickness = box.size.z;
            return Mathf.Max(box.size.x, box.size.y);
        }
    }
}
