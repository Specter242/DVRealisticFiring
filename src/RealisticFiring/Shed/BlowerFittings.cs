using System.Linq;
using UnityEngine;

namespace RealisticFiring.Shed
{
 internal static class BlowerFittings
    {
        /// <summary>¾-in. steam hose's outside diameter, m.</summary>
        public const float HoseDiameter = 0.038f;

        private const float HoseRadius = 0.5f * HoseDiameter;

        // The engine's fitting stands this far out from the smokebox to its
        // spud's mouth, where the hose's coupling seats.
        public const float Proud = 0.12f;

 private const float RopeOriginAt = 0.044f;

        private static readonly Vector3 Up = Vector3.up;
        private static readonly Vector3 Along = Vector3.forward;
        private static readonly Vector3 Across = Vector3.right;

        private static Vector2 P(float r, float h) => new Vector2(r, h);

        /// <summary>
        /// Builds the Barco fitting under <paramref name="socket"/> (its +y
        /// outboard, its origin the spud's mouth) on <paramref name="layer"/>.
        /// Returns the fitting, to be turned to its engine by <see cref="Orient"/>.
        /// </summary>
        public static Transform Barco(Transform socket, int layer)
        {
            var mesh = new FittingMesh();
            var face = -Proud;
            const float flange = 0.016f;
            const float flangeLength = 0.148f, flangeWidth = 0.083f;
            var straight = flangeLength - flangeWidth;
            // The flange: oblong, along the smokebox.
            mesh.Box(Metal.Iron, new Vector3(0f, face + 0.5f * flange, 0f), new Vector3(flangeWidth, flange, straight), Quaternion.identity);
            mesh.Turned(Metal.Iron, new[] { P(0f, face), P(0.5f * flangeWidth, face), P(0.5f * flangeWidth, face + flange), P(0f, face + flange) },
                new Vector3(0f, 0f, 0.5f * straight), Up, from: 0f, to: 180f);
            mesh.Turned(Metal.Iron, new[] { P(0f, face), P(0.5f * flangeWidth, face), P(0.5f * flangeWidth, face + flange), P(0f, face + flange) },
                new Vector3(0f, 0f, -0.5f * straight), Up, from: 180f, to: 360f);
            // Its two cap screws, at 4⅛-in. centres.
            foreach (var z in new[] { -0.0525f, 0.0525f })
            {
                var at = new Vector3(0f, 0f, z);
                mesh.Rod(Metal.Iron, 0.0165f, face + flange, face + flange + 0.003f, at, Up);
                mesh.Hex(Metal.Iron, 0.027f, face + flange + 0.003f, face + flange + 0.015f, at, Up, 30f);
            }
            // The body, standing out from the flange.
            var top = -0.042f;
            mesh.Turned(Metal.Iron, new[]
            {
                P(0f, face + flange), P(0.046f, face + flange), P(0.040f, face + flange + 0.006f), P(0.036f, face + flange + 0.012f),
                P(0.036f, top - 0.004f), P(0.032f, top), P(0f, top),
            }, Vector3.zero, Up);
            // The hose spud: its hex, then its coarse thread to the mouth,
            // and its bore.
            mesh.Hex(Metal.Steel, 0.041f, top, -0.026f, Vector3.zero, Up);
            var spud = new System.Collections.Generic.List<Vector2>();
            for (var i = 0; i < 8; i++)
            {
                var h = -0.026f + 0.003f * i;
                spud.Add(P(0.0162f, h));
                spud.Add(P(0.0178f, h + 0.0015f));
            }
            spud.AddRange(new[] { P(0.0162f, -0.002f), P(0.0150f, 0f), P(0.0115f, 0f), P(0.0115f, -0.018f), P(0f, -0.018f) });
            mesh.Turned(Metal.Steel, spud, Vector3.zero, Up);
            // The cab's blower pipe: out of the body toward the cab, through
            // a union and an elbow, and into the smokebox.
            var branch = new Vector3(0f, -0.07f, 0f);
            mesh.Rod(Metal.Iron, 0.024f, 0.03f, 0.044f, branch, Along);
            mesh.Hex(Metal.Brass, 0.046f, 0.044f, 0.062f, branch, Along, 30f);
            mesh.Rod(Metal.Iron, 0.021f, 0.062f, 0.105f, branch, Along);
            var elbow = branch + Along * 0.115f;
            mesh.Turned(Metal.Iron, Enumerable.Range(0, 9).Select(i =>
            {
                var a = Mathf.PI * i / 8f;
                return P(0.026f * Mathf.Sin(a), -0.026f * Mathf.Cos(a));
            }).ToList(), elbow, Up);
            mesh.Rod(Metal.Iron, 0.021f, 0f, 0.16f, elbow, Vector3.down);
            var fitting = mesh.Build(socket, "RealisticFiring Barco fitting", layer);

            // The check valve under its hex cap, on the side turned up.
            var check = new FittingMesh();
            var valve = new Vector3(0f, -0.067f, 0f);
            check.Rod(Metal.Iron, 0.023f, 0.03f, 0.046f, valve, Across);
            check.Hex(Metal.Brass, 0.040f, 0.046f, 0.062f, valve, Across);
            check.Turned(Metal.Brass, new[] { P(0.012f, 0.062f), P(0.008f, 0.067f), P(0f, 0.068f) }, valve, Across);
            check.Build(fitting, "check valve", layer);
            return fitting;
        }

        /// <summary>
        /// Turns the Barco fitting on its socket's outboard axis so its pipe
        /// leaves toward <paramref name="car"/>'s cab and its check valve is
        /// on the side nearer up.
        /// </summary>
        public static void Orient(Transform fitting, Transform car)
        {
            if (fitting == null || car == null || fitting.parent == null)
                return;
            var outward = fitting.parent.up;
            var back = Vector3.ProjectOnPlane(-car.forward, outward);
            if (back.sqrMagnitude < 1e-4f)
                back = Vector3.ProjectOnPlane(-car.up, outward);
            fitting.rotation = Quaternion.LookRotation(back.normalized, outward);
            var check = fitting.Find("check valve");
            if (check != null)
                check.localRotation = Vector3.Dot(fitting.right, car.up) >= 0f ? Quaternion.identity : Quaternion.AngleAxis(180f, Vector3.up);
        }

        /// <summary>
        /// Makes the station plug <paramref name="plug"/> the hose's Boss
        /// coupling: the diesel plug's model hidden, the coupling built along
        /// its +y from its mouth, its grip colliders and the rope's ends set
        /// to it; <paramref name="scale"/> times the blower line's, for a
        /// bigger hose.
        /// </summary>
        public static void HoseEnd(PluggableObject plug, float scale = 1f)
        {
            var root = plug.transform;
            var model = root.Find("model");
            var layer = model != null ? model.gameObject.layer : plug.gameObject.layer;
            if (model != null)
            {
                foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
                    renderer.enabled = false;
            }

            // Built at the blower line's size and scaled.
            var mesh = new FittingMesh();
            // The winged nut, over the spud's thread when coupled.
            mesh.Turned(Metal.Brass, new[]
            {
                P(0.0235f, -0.019f), P(0.0255f, -0.017f), P(0.0255f, 0.002f), P(0.0235f, 0.004f),
                P(0.0182f, 0.004f), P(0.0182f, -0.019f), P(0.0235f, -0.019f),
            }, Vector3.zero, Up);
            foreach (var side in new[] { -1f, 1f })
                mesh.Box(Metal.Brass, new Vector3(side * 0.0315f, -0.0075f, 0f), new Vector3(0.016f, 0.018f, 0.007f), Quaternion.identity);
            mesh.Turned(Metal.Steel, new[]
            {
                P(0f, 0.03f), P(0.009f, 0.03f), P(0.009f, 0f), P(0.0175f, 0f), P(0.0175f, 0.012f),
                P(0.0135f, 0.014f), P(0.0135f, 0.04f), P(0f, 0.04f),
            }, Vector3.zero, Up);
            // The clamp: its band round the hose, its fingers hooking the
            // stem's collar, and its two bolted ears.
            mesh.Turned(Metal.Steel, new[]
            {
                P(0.0225f, 0.02f), P(0.0225f, 0.048f), P(HoseRadius, 0.048f), P(HoseRadius, 0.02f), P(0.0225f, 0.02f),
            }, Vector3.zero, Up);
            foreach (var degrees in new[] { 45f, 135f, 225f, 315f })
            {
                var a = degrees * Mathf.Deg2Rad;
                var radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                mesh.Box(Metal.Steel, radial * 0.0195f + Up * 0.016f, new Vector3(0.004f, 0.01f, 0.007f), Quaternion.LookRotation(radial, Up));
            }
            foreach (var side in new[] { -1f, 1f })
            {
                var ear = new Vector3(0f, 0.034f, side * 0.0275f);
                mesh.Box(Metal.Steel, ear, new Vector3(0.012f, 0.024f, 0.01f), Quaternion.identity);
                mesh.Rod(Metal.Steel, 0.0035f, -0.012f, 0.012f, ear, Across);
                mesh.Hex(Metal.Steel, 0.01f, 0.006f, 0.011f, ear, Across);
                mesh.Hex(Metal.Steel, 0.01f, -0.011f, -0.006f, ear, Across);
            }
            var coupling = mesh.Build(root, "RealisticFiring Boss coupling", layer);
            coupling.localScale = Vector3.one * scale;
            Outline(root, coupling);

            var colliders = root.Find("Colliders");
            if (colliders != null)
            {
                var capsules = 0;
                foreach (var collider in colliders.GetComponentsInChildren<Collider>(true))
                {
                    var t = collider.transform;
                    t.localPosition = Vector3.zero;
                    t.localRotation = Quaternion.identity;
                    t.localScale = Vector3.one;
                    collider.enabled = true;
                    if (collider is CapsuleCollider capsule && capsules++ == 0)
                    {
                        capsule.direction = 1;
                        capsule.center = new Vector3(0f, 0.03f, 0f) * scale;
                        capsule.radius = 0.026f * scale;
                        capsule.height = 0.15f * scale;
                    }
                    else if (collider is CapsuleCollider ears)
                    {
                        ears.direction = 2;
                        ears.center = new Vector3(0f, 0.034f, 0f) * scale;
                        ears.radius = 0.012f * scale;
                        ears.height = 0.075f * scale;
                    }
                    else if (collider is BoxCollider box)
                    {
                        box.center = new Vector3(0f, -0.0075f, 0f) * scale;
                        box.size = new Vector3(0.078f, 0.018f, 0.012f) * scale;
                    }
                }
            }

            // The hose leaves straight out of the coupling, its last two
            // points a rope's spacing apart whatever its size.
            SetLocal(root.Find("RopeOrigin"), Up * RopeOriginAt * scale);
            SetLocal(root.Find("RopeLead"), Up * (RopeOriginAt * scale + ShedHoses.PointSpacing));
            SetLocal(root.Find("[left anchor]"), new Vector3(-0.03f, 0.05f, 0f) * scale);
            SetLocal(root.Find("[right anchor]"), new Vector3(0.03f, 0.05f, 0f) * scale);
        }

 public static void Outline(Transform root, Transform model)
        {
            var renderers = model.GetComponentsInChildren<Renderer>(true).ToList();
            if (renderers.Count == 0)
                return;
            var tags = root.GetComponentsInChildren<HighlightTag>(true);
            if (tags.Length == 0)
                tags = new[] { root.gameObject.AddComponent<HighlightTag>() };
            foreach (var tag in tags)
            {
                tag.renderers = renderers.ToList();
                tag.targetObject = renderers[0].gameObject;
            }
        }

        private static void SetLocal(Transform t, Vector3 position)
        {
            if (t != null)
                t.localPosition = position;
        }

        /// <summary>
        /// Puts a reducing ring and a band clamp where the hose leaves the
        /// gooseneck at <paramref name="source"/> (the rope's start; the hose
        /// leaves it toward <paramref name="lead"/>), <paramref name="scale"/>
        /// times the blower line's.
        /// </summary>
        public static void GooseneckEnd(Transform source, Transform lead, int layer, float scale = 1f)
        {
            if (source == null)
                return;
            var down = lead != null ? source.InverseTransformDirection(lead.position - source.position).normalized : Vector3.down;
            if (down.sqrMagnitude < 0.5f)
                down = Vector3.down;
            var mesh = new FittingMesh();
            mesh.Turned(Metal.Brass, new[] { P(0.028f, 0f), P(0.028f, 0.007f), P(0.017f, 0.007f), P(0.017f, 0f), P(0.028f, 0f) }, Vector3.zero, down);
            mesh.Turned(Metal.Steel, new[]
            {
                P(0.0225f, 0.007f), P(0.0225f, 0.035f), P(HoseRadius, 0.035f), P(HoseRadius, 0.007f), P(0.0225f, 0.007f),
            }, Vector3.zero, down);
            var across = Vector3.Cross(down, Mathf.Abs(Vector3.Dot(down, Vector3.forward)) > 0.9f ? Vector3.right : Vector3.forward).normalized;
            foreach (var side in new[] { -1f, 1f })
            {
                var ear = down * 0.021f + across * side * 0.0275f;
                mesh.Box(Metal.Steel, ear, new Vector3(0.012f, 0.024f, 0.01f), Quaternion.LookRotation(across, down));
                var bolt = Vector3.Cross(down, across).normalized;
                mesh.Rod(Metal.Steel, 0.0035f, -0.012f, 0.012f, ear, bolt);
                mesh.Hex(Metal.Steel, 0.01f, 0.006f, 0.011f, ear, bolt);
                mesh.Hex(Metal.Steel, 0.01f, -0.011f, -0.006f, ear, bolt);
            }
            mesh.Build(source, "RealisticFiring gooseneck clamp", layer).localScale = Vector3.one * scale;
        }
    }
}
