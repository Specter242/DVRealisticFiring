using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RealisticFiring.Visuals
{
    /// <summary>Build the supplied additions and trim an exact stock fitting; no game or simulation state.</summary>
    internal static class InjectorModelMesh
    {
        internal static Mesh Create(S282InjectorData.Piece p)
        {
            var mesh = new Mesh { name = "Lilla S282 injector " + p.Name };
            mesh.vertices = p.Vertices.Select(V).ToArray(); mesh.normals = p.Normals.Select(V).ToArray();
            mesh.uv = p.Uv.Select(v => new Vector2(v[0], v[1])).ToArray();
            mesh.uv2 = p.Uv2.Select(v => new Vector2(v[0], v[1])).ToArray();
            mesh.triangles = p.Triangles; mesh.RecalculateBounds(); mesh.RecalculateTangents();
            return mesh;
        }

        internal static Mesh Cut(Mesh source, S282InjectorData.Mask mask)
        {
            if (source.vertexCount != mask.VertexCount || source.subMeshCount != mask.TriangleCounts.Length
                || GeometryHash(source) != mask.GeometryHash)
                throw new InvalidDataException("Stock fitting geometry differs; keeping its original cab");
            var kept = new int[source.subMeshCount][];
            for (var s = 0; s < kept.Length; s++)
            {
                var triangles = source.GetTriangles(s);
                if (triangles.Length / 3 != mask.TriangleCounts[s]) throw new InvalidDataException("Stock fitting topology changed");
                var remove = new HashSet<int>(mask.RemovedTriangles[s]);
                kept[s] = Enumerable.Range(0, triangles.Length / 3).Where(t => !remove.Contains(t))
                    .SelectMany(t => new[] { triangles[t * 3], triangles[t * 3 + 1], triangles[t * 3 + 2] }).ToArray();
            }
            var cut = Object.Instantiate(source);
            cut.name = source.name + " (injector prototype)";
            for (var s = 0; s < kept.Length; s++) cut.SetTriangles(kept[s], s);
            return cut;
        }

        internal static string GeometryHash(Mesh mesh)
        {
            using (var bytes = new MemoryStream())
            using (var writer = new BinaryWriter(bytes))
            using (var hash = SHA256.Create())
            {
                writer.Write(mesh.vertexCount);
                foreach (var v in mesh.vertices) { writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); }
                writer.Write(mesh.subMeshCount);
                for (var s = 0; s < mesh.subMeshCount; s++)
                {
                    var indices = mesh.GetTriangles(s); writer.Write(indices.Length);
                    foreach (var i in indices) writer.Write(i);
                }
                writer.Flush();
                return BitConverter.ToString(hash.ComputeHash(bytes.ToArray())).Replace("-", "").ToLowerInvariant();
            }
        }

        private static Vector3 V(float[] v) => new Vector3(v[0], v[1], v[2]);
    }

    internal sealed class S282InjectorLifetime : MonoBehaviour
    {
        internal readonly List<Mesh> Meshes = new List<Mesh>();
        internal readonly List<(MeshFilter Filter, Mesh Original, Mesh Replacement)> Cuts = new List<(MeshFilter, Mesh, Mesh)>();
        internal GameObject StockControl;
        internal bool StockControlWasActive;
        internal bool RestoreOriginalState = true;
        internal Action Detach;
        internal Action RestoreInputs;
        internal readonly List<(GameObject Object, bool Active)> Hidden = new List<(GameObject, bool)>();
        internal readonly List<(Renderer Renderer, bool Enabled)> StockRenderers = new List<(Renderer, bool)>();
        internal readonly List<(Collider Collider, bool Enabled)> StockColliders = new List<(Collider, bool)>();
        internal void ConcealControl(GameObject value, params GameObject[] otherInteractionAreas)
        {
            StockControl = value; StockControlWasActive = value.activeSelf;
            // Hide it immediately; retire its physics once its keyboard/HUD
            // references have been transferred to the replacement wheel.
            var areas = new[] { value }.Concat(otherInteractionAreas ?? Array.Empty<GameObject>()).Where(a => a != null).Distinct().ToArray();
            foreach (var renderer in areas.SelectMany(a => a.GetComponentsInChildren<Renderer>(true)).Distinct())
            { StockRenderers.Add((renderer, renderer.enabled)); renderer.enabled = false; }
            foreach (var collider in areas.SelectMany(a => a.GetComponentsInChildren<Collider>(true)).Distinct())
            { StockColliders.Add((collider, collider.enabled)); collider.enabled = false; }
        }
        internal void Hide(GameObject value)
        {
            if (value == null) return;
            Hidden.Add((value, value.activeSelf)); value.SetActive(false);
        }
        internal void ApplyCuts() { foreach (var cut in Cuts) cut.Filter.sharedMesh = cut.Replacement; }
        internal void Clean()
        {
            Detach?.Invoke(); Detach = null;
            if (RestoreOriginalState)
            {
                RestoreInputs?.Invoke();
                foreach (var cut in Cuts)
                    if (cut.Filter != null && cut.Filter.sharedMesh == cut.Replacement) cut.Filter.sharedMesh = cut.Original;
                if (StockControl != null && StockControl.activeSelf != StockControlWasActive) StockControl.SetActive(StockControlWasActive);
                foreach (var item in StockRenderers) if (item.Renderer != null) item.Renderer.enabled = item.Enabled;
                foreach (var item in StockColliders) if (item.Collider != null) item.Collider.enabled = item.Enabled;
                foreach (var hidden in Hidden)
                    if (hidden.Object != null && hidden.Object.activeSelf != hidden.Active) hidden.Object.SetActive(hidden.Active);
            }
            RestoreInputs = null;
            StockControl = null;
            foreach (var mesh in Meshes)
                if (mesh != null)
                {
                    if (Application.isPlaying) Object.Destroy(mesh); else Object.DestroyImmediate(mesh);
                }
            Meshes.Clear(); Cuts.Clear(); Hidden.Clear(); StockRenderers.Clear(); StockColliders.Clear();
        }
        private void OnDestroy() => Clean();
    }

    internal sealed class S282InjectorLinkage : MonoBehaviour
    {
        internal Transform Lever;
        internal Transform Handle;
        internal Transform Carrier;
        internal Transform Stem;
        internal S282InjectorData Model;

        internal static void Pose(S282InjectorData model, Quaternion opening, out Vector3 pin,
            out Vector3 handlePivot, out Quaternion carrierRotation)
        {
            var pivot = V(model.SteamPivot); var rest = V(model.StemPin);
            var support = V(model.CarrierPivot); var axis = V(model.StemAxis);
            var arm = opening * (rest - pivot);
            // Slider-crank constraint: the rod pin stays on its original
            // shaft line; the suspended handle hinge stays one link length
            // from the fixed collar pin. Pick the branch containing closed.
            var offset = rest - arm - support;
            var along = Vector3.Dot(offset, axis);
            var length2 = (pivot - support).sqrMagnitude;
            var discriminant = along * along - offset.sqrMagnitude + length2;
            if (discriminant < -1e-6f) throw new InvalidDataException("Injector linkage exceeds its suspension travel");
            var branch = Vector3.Dot(pivot - support, axis) < 0f ? -1f : 1f;
            var travel = -along + branch * Mathf.Sqrt(Mathf.Max(0f, discriminant));
            pin = rest + axis * travel;
            handlePivot = pin - arm;
            carrierRotation = Quaternion.FromToRotation(pivot - support, handlePivot - support);
        }

        private void LateUpdate()
        {
            if (Lever == null || Model == null) return;
            Pose(Model, Quaternion.Inverse(transform.rotation) * Lever.rotation, out var pin, out var pivot, out var rotation);
            Stem.localPosition = pin; Stem.localRotation = Quaternion.identity;
            Carrier.localRotation = rotation;
            Handle.position = transform.TransformPoint(pivot);
        }

        private static Vector3 V(float[] v) => new Vector3(v[0], v[1], v[2]);
    }
}
