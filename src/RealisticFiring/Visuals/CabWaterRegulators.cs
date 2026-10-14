using System.Collections.Generic;
using System.Linq;
using DV.CabControls.Spec;
using DV.Simulation.Ports;
using RealisticFiring.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace RealisticFiring.Visuals
{
 internal static class CabWaterRegulators
    {
        // The cab mesh carrying each engine's regulators, and how far each
        // turns, in degrees: the handwheel nearly a whole turn, the handle as
        // far as the cab side lets it.
        private static readonly Dictionary<string, (string Mesh, float RightTurn, float LeftTurn)> Fits = new Dictionary<string, (string, float, float)>
        {
            ["em_lms_8f_loc_black"] = ("CAB.113", 354f, 150f),
            ["em_lms_8f_loc_grey"] = ("CAB.113", 354f, 150f),
        };

        // A spindle is a part at least this tall and no wider, in m; the parts
        // it turns sit on its top within this reach of its axis.
        private const float SpindleHeight = 0.3f;
        private const float SpindleWidth = 0.05f;
        private const float TurningReach = 0.12f;
        private const float OnTop = 0.01f;

        // A grip at least this thick, in m, so a slim handle can be taken.
        private const float GripLeast = 0.03f;

        // The hinge turns no further either way.
        private const float HingeLimit = 177f;

        private const int Steps = 40;
        private const float ScrollSteps = 2f;

        // Meshes cut once per source mesh and kept: interiors load often.
        private static readonly Dictionary<(Mesh, string), Mesh> Cuts = new Dictionary<(Mesh, string), Mesh>();
        private static readonly Dictionary<Mesh, Mesh> Originals = new Dictionary<Mesh, Mesh>();

        /// <summary>A cab mesh and the parts it is made of.</summary>
        internal sealed class Cab
        {
            public string Livery;
            public MeshFilter Filter;
            public Mesh Readable;
            public List<Part> Parts;
            public readonly List<Part> Turning = new List<Part>();
            public readonly List<Part> Spindles = new List<Part>();
        }

        /// <summary>Triangles joined by their corners, and where they are drawn in the car's frame.</summary>
        internal sealed class Part
        {
            public readonly List<int>[] Triangles;
            public Bounds Box;

            public Part(int submeshes)
            {
                Triangles = Enumerable.Range(0, submeshes).Select(_ => new List<int>()).ToArray();
            }
        }

        /// <summary>The cab's regulators to work, or null where the engine has none modeled or the mesh cannot be read.</summary>
        public static Cab Find(TrainCar car, Transform interior)
        {
            if (car == null || interior == null || car.carLivery == null || !Fits.TryGetValue(car.carLivery.id, out var fit))
                return null;
            var filter = interior.GetComponentsInChildren<MeshFilter>(true)
                .FirstOrDefault(f => f.sharedMesh != null && (f.sharedMesh.name == fit.Mesh || Originals.ContainsKey(f.sharedMesh)));
            if (filter == null)
                return null;
            // A cab rebuilt: its own mesh back before it is cut again.
            if (Originals.TryGetValue(filter.sharedMesh, out var original))
                filter.sharedMesh = original;
            original = filter.sharedMesh;
            var readable = original.isReadable ? original : car.GetComponentsInChildren<MeshFilter>(true)
                .Select(f => f.sharedMesh)
                .FirstOrDefault(m => m != null && m.isReadable && m.name == original.name && m.vertexCount == original.vertexCount
                                     && (m.bounds.center - original.bounds.center).sqrMagnitude < 1e-8f && (m.bounds.size - original.bounds.size).sqrMagnitude < 1e-8f);
            if (readable == null)
                return null;
            var toCar = car.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            return new Cab { Livery = car.carLivery.id, Filter = filter, Readable = readable, Parts = Split(readable, toCar) };
        }

        /// <summary>
        /// Makes the regulator on <paramref name="feeder"/>'s side work
        /// <paramref name="circuit"/>'s water valve. Returns a note for the
        /// log, or null where that side has none to work.
        /// </summary>
        public static string Build(Cab cab, TrainCar car, Transform interior, Transform root, InteractablePortFeeder feeder, InjectorCircuit circuit,
            StokerJetControls.Valve valve)
        {
            if (cab == null)
                return null;
            var stock = feeder.transform;
            var side = car.transform.InverseTransformPoint(stock.position).x < 0f ? -1f : 1f;
            var spindle = cab.Parts
                .Where(p => p.Box.center.x * side > 0f && p.Box.size.y >= SpindleHeight && p.Box.size.x <= SpindleWidth && p.Box.size.z <= SpindleWidth)
                .OrderByDescending(p => p.Box.size.y)
                .FirstOrDefault();
            if (spindle == null)
                return null;
            if (cab.Spindles.Contains(spindle))
                return $"'{valve.Id}' already works the regulator on the {(side < 0f ? "left" : "right")}";
            var axis = new Vector2(spindle.Box.center.x, spindle.Box.center.z);
            var turning = cab.Parts
                .Where(p => p != spindle && p.Box.min.y >= spindle.Box.max.y - OnTop
                            && Vector2.Distance(new Vector2(p.Box.center.x, p.Box.center.z), axis) <= TurningReach)
                .ToList();
            var source = InjectorSteamCab.PrefabHolder(car, interior, stock);
            var sourceControl = source != null ? source.Find(stock.name) : null;
            if (turning.Count == 0 || sourceControl == null || sourceControl.GetComponent<Rotary>() == null)
                return null;

            // Copied switched off and set in place before it wakes.
            var frame = new GameObject(valve.Id).transform;
            frame.gameObject.SetActive(false);
            frame.SetParent(root, false);
            var holder = Object.Instantiate(source.gameObject, frame, false).transform;
            holder.SetPositionAndRotation(stock.parent.position, stock.parent.rotation);
            var control = holder.Find(stock.name);
            var spec = control.GetComponent<Rotary>();
            var grips = spec.colliderGameObjects != null ? spec.colliderGameObjects.FirstOrDefault(g => g != null) : null;
            var layer = grips != null ? grips.layer : control.gameObject.layer;
            // Its own handle and grips go: the cab's parts are the handle. Gone,
            // not hidden: a control's outline takes its first renderer.
            foreach (var renderer in holder.GetComponentsInChildren<Renderer>(true))
                Object.DestroyImmediate(renderer);
            foreach (var collider in holder.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            if (spec.nonVrStaticInteractionArea != null)
            {
                Object.DestroyImmediate(spec.nonVrStaticInteractionArea.gameObject);
                spec.nonVrStaticInteractionArea = null;
            }

            // On the spindle's axis, turning about the cab's up: positive
            // anticlockwise seen from above, opening as a screw valve does.
            var pivot = car.transform.TransformPoint(new Vector3(axis.x, spindle.Box.max.y, axis.y));
            control.position = pivot;
            spec.jointAxis = control.InverseTransformDirection(-car.transform.up).normalized;
            var turn = side > 0f ? Fits[cab.Livery].RightTurn : Fits[cab.Livery].LeftTurn;
            var min = turn > HingeLimit ? -turn / 2f : 0f;
            spec.useSteppedJoint = true;
            spec.notches = Steps + 1;
            spec.useLimits = true;
            spec.jointLimitMin = min;
            spec.jointLimitMax = min + turn;
            spec.jointStartingPos = min;
            spec.invertDirection = false;
            spec.scrollWheelHoverScroll = ScrollSteps;

            // The parts, cut from the cab's mesh, drawn where they were when shut.
            var mesh = Cut(cab.Readable, turning, "turning " + spindle.Box.center.x.ToString("F2"));
            var piece = new GameObject("regulator").transform;
            piece.SetParent(cab.Filter.transform, false);
            piece.SetParent(control, true);
            piece.gameObject.layer = layer;
            piece.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var drawn = piece.gameObject.AddComponent<MeshRenderer>();
            drawn.sharedMaterials = cab.Filter.GetComponent<MeshRenderer>().sharedMaterials;
            (control.GetComponent<HighlightTag>() ?? control.gameObject.AddComponent<HighlightTag>()).renderers = new List<Renderer> { drawn };
            var grip = piece.gameObject.AddComponent<BoxCollider>();
            var scale = piece.lossyScale;
            grip.center = mesh.bounds.center;
            grip.size = new Vector3(
                Mathf.Max(mesh.bounds.size.x, GripLeast / Mathf.Max(Mathf.Abs(scale.x), 1e-6f)),
                Mathf.Max(mesh.bounds.size.y, GripLeast / Mathf.Max(Mathf.Abs(scale.y), 1e-6f)),
                Mathf.Max(mesh.bounds.size.z, GripLeast / Mathf.Max(Mathf.Abs(scale.z), 1e-6f)));
            spec.colliderGameObjects = new[] { piece.gameObject };
            // Shut at the hinge's least: turned back by it, it sits as modeled.
            piece.RotateAround(pivot, control.TransformDirection(spec.jointAxis), -min);

            foreach (var input in control.GetComponents<MonoBehaviour>()
                         .Where(c => StokerCab.InputComponents.Contains(c.GetType().Name)
                                     || c.GetType().Name == "PercentageControlNames" || c.GetType().Name == "NotchedControlCustomNames")
                         .ToList())
                Object.DestroyImmediate(input);
            var cloneFeeder = control.GetComponent<InteractablePortFeeder>();
            if (cloneFeeder == null)
            {
                Object.Destroy(frame.gameObject);
                return null;
            }
            cloneFeeder.portId = valve.PortId;
            control.gameObject.AddComponent<SimControlBinder>().Bind(cloneFeeder, circuit.Added);
            control.gameObject.AddComponent<HoverName>().Source = () => InjectorSteamCab.WaterHover(circuit);
            frame.gameObject.SetActive(true);
            cab.Spindles.Add(spindle);
            cab.Turning.AddRange(turning);

            var at = car.transform.InverseTransformPoint(pivot);
            return $"'{valve.Id}' is the cab's own regulator on the {(side < 0f ? "left" : "right")}, {turning.Count} parts of '{cab.Filter.name}' " +
                   $"turning {turn:F0} degrees in {Steps} steps on the spindle at ({at.x:F2}, {at.y:F2}, {at.z:F2}) m";
        }

        /// <summary>Cuts the parts that now turn out of the cab's own mesh.</summary>
        public static void Finish(Cab cab)
        {
            if (cab == null || cab.Turning.Count == 0)
                return;
            var key = "rest " + string.Join(",", cab.Turning.Select(p => cab.Parts.IndexOf(p)).OrderBy(i => i));
            var rest = Cut(cab.Readable, cab.Parts.Except(cab.Turning).ToList(), key);
            Originals[rest] = cab.Filter.sharedMesh;
            cab.Filter.sharedMesh = rest;
        }

        // The mesh's parts: triangles joined by shared corners, or corners at
        // the same place (a part's hard edges and seams split its corners).
        private static List<Part> Split(Mesh mesh, Matrix4x4 toCar)
        {
            var vertices = mesh.vertices;
            var parent = Enumerable.Range(0, vertices.Length).ToArray();
            int Root(int i)
            {
                while (parent[i] != i)
                    i = parent[i] = parent[parent[i]];
                return i;
            }
            void Join(int a, int b) => parent[Root(a)] = Root(b);

            var triangles = Enumerable.Range(0, mesh.subMeshCount).Select(mesh.GetTriangles).ToArray();
            foreach (var list in triangles)
                for (var t = 0; t < list.Length; t += 3)
                {
                    Join(list[t], list[t + 1]);
                    Join(list[t + 1], list[t + 2]);
                }
            var at = new Dictionary<Vector3, int>();
            for (var i = 0; i < vertices.Length; i++)
            {
                if (at.TryGetValue(vertices[i], out var same))
                    Join(i, same);
                else
                    at[vertices[i]] = i;
            }

            var parts = new Dictionary<int, Part>();
            for (var s = 0; s < triangles.Length; s++)
                for (var t = 0; t < triangles[s].Length; t += 3)
                {
                    var key = Root(triangles[s][t]);
                    if (!parts.TryGetValue(key, out var part))
                    {
                        part = new Part(triangles.Length) { Box = new Bounds(toCar.MultiplyPoint3x4(vertices[triangles[s][t]]), Vector3.zero) };
                        parts[key] = part;
                    }
                    for (var k = 0; k < 3; k++)
                    {
                        part.Triangles[s].Add(triangles[s][t + k]);
                        part.Box.Encapsulate(toCar.MultiplyPoint3x4(vertices[triangles[s][t + k]]));
                    }
                }
            return parts.Values.ToList();
        }

        // A mesh of some of a readable mesh's parts, with only their corners.
        private static Mesh Cut(Mesh source, List<Part> parts, string key)
        {
            if (Cuts.TryGetValue((source, key), out var cut) && cut != null)
                return cut;
            var vertices = source.vertices;
            var normals = source.normals;
            var tangents = source.tangents;
            var uv = source.uv;
            var uv2 = source.uv2;
            var colors = source.colors32;
            var index = new Dictionary<int, int>();
            var used = new List<int>();
            var submeshes = new List<int>[source.subMeshCount];
            for (var s = 0; s < submeshes.Length; s++)
            {
                submeshes[s] = new List<int>();
                foreach (var corner in parts.SelectMany(p => p.Triangles[s]))
                {
                    if (!index.TryGetValue(corner, out var mapped))
                    {
                        mapped = used.Count;
                        index[corner] = mapped;
                        used.Add(corner);
                    }
                    submeshes[s].Add(mapped);
                }
            }
            cut = new Mesh { name = source.name + " (" + key + ")" };
            if (used.Count > ushort.MaxValue)
                cut.indexFormat = IndexFormat.UInt32;
            cut.SetVertices(used.Select(i => vertices[i]).ToList());
            if (normals.Length == vertices.Length)
                cut.SetNormals(used.Select(i => normals[i]).ToList());
            if (tangents.Length == vertices.Length)
                cut.SetTangents(used.Select(i => tangents[i]).ToList());
            if (uv.Length == vertices.Length)
                cut.SetUVs(0, used.Select(i => uv[i]).ToList());
            if (uv2.Length == vertices.Length)
                cut.SetUVs(1, used.Select(i => uv2[i]).ToList());
            if (colors.Length == vertices.Length)
                cut.SetColors(used.Select(i => colors[i]).ToList());
            cut.subMeshCount = submeshes.Length;
            for (var s = 0; s < submeshes.Length; s++)
                cut.SetTriangles(submeshes[s], s);
            cut.RecalculateBounds();
            Cuts[(source, key)] = cut;
            return cut;
        }
    }
}
