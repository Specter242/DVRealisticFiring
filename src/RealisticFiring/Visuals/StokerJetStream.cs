using UnityEngine;
using UnityEngine.Rendering;

namespace RealisticFiring.Visuals
{
    /// <summary>Five continuous steam jets in one small mesh, rooted at the distributor plate.</summary>
    internal sealed class StokerJetStream
    {
        private readonly GameObject gameObject;
        private readonly Mesh mesh;
        private readonly MeshRenderer renderer;
        private readonly Vector3[] vertices;
        private readonly Color[] colors;
        private readonly Vector2[] flow;
        private int active;

        public StokerJetStream(Transform parent, Transform effects, int count, Material material)
        {
            gameObject = new GameObject("stoker jet streams");
            gameObject.transform.SetParent(parent, false);
            // Preserve the spray's coordinate frame, but keep this renderer
            // under the effects root so wall lighting cannot replace its shader.
            if (effects != null)
                gameObject.transform.SetParent(effects, true);
            vertices = new Vector3[count * 8];
            colors = new Color[vertices.Length];
            flow = new Vector2[vertices.Length];
            var uv = new Vector2[vertices.Length];
            var indices = new int[count * 12];
            for (var face = 0; face < count * 2; face++)
            {
                var v = face * 4;
                uv[v] = new Vector2(0f, 0f);
                uv[v + 1] = new Vector2(1f, 0f);
                uv[v + 2] = new Vector2(0f, 1f);
                uv[v + 3] = new Vector2(1f, 1f);
                var t = face * 6;
                indices[t] = v; indices[t + 1] = v + 2; indices[t + 2] = v + 1;
                indices[t + 3] = v + 1; indices[t + 4] = v + 2; indices[t + 5] = v + 3;
            }
            mesh = new Mesh { name = "RealisticFiring stoker jets", vertices = vertices, uv = uv, triangles = indices };
            mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = false;
        }

        public void Begin() => active = 0;

        public void Set(int jet, Vector3 from, Vector3 heading, Vector3 up, float pressure, float heat)
        {
            var forcing = Mathf.Sqrt(Mathf.Clamp01(pressure));
            var length = Mathf.Lerp(0.18f, 0.65f, forcing);
            var axis = heading.normalized;
            var across = Vector3.Cross(axis, up).normalized;
            if (across.sqrMagnitude < 0.1f)
                across = Vector3.Cross(axis, Vector3.right).normalized;
            var other = Vector3.Cross(axis, across).normalized;
            var end = from + axis * length;
            // Steam takes a warm glint in the fire, without becoming a flame.
            var color = Color.Lerp(new Color(0.45f, 0.48f, 0.5f), new Color(0.5f, 0.32f, 0.17f), heat);
            color.a = pressure > 0.001f ? Mathf.Lerp(0.3f, 0.2f, heat) * forcing : 0f;
            if (color.a > 0f)
                active++;
            for (var face = 0; face < 2; face++)
            {
                var v = jet * 8 + face * 4;
                var side = face == 0 ? across : other;
                var root = side * Mathf.Lerp(0.002f, 0.005f, forcing);
                var tip = side * Mathf.Lerp(0.012f, 0.035f, forcing);
                vertices[v] = from - root;
                vertices[v + 1] = from + root;
                vertices[v + 2] = end - tip;
                vertices[v + 3] = end + tip;
                for (var i = v; i < v + 4; i++)
                {
                    colors[i] = color;
                    flow[i] = new Vector2(0.5f + forcing, jet * 0.173f);
                }
            }
        }

        public void End()
        {
            renderer.enabled = active > 0;
            if (active == 0)
                return;
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.uv2 = flow;
            mesh.RecalculateBounds();
        }

        public void Hide() => renderer.enabled = false;
        public string Describe() => $"jets {active}, drawn {renderer.enabled}, shader {renderer.sharedMaterial?.shader.name}";

        public void Destroy()
        {
            Object.Destroy(gameObject);
            Object.Destroy(mesh);
        }
    }
}
