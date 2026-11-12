using System.Collections.Generic;
using System.Linq;
using RealisticFiring.Visuals;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RealisticFiring.Items
{
    /// <summary>
    /// The starting fuel's look. The effects bundle carries authored bodies
    /// (fx/textures/make_starting_fuel.py builds their meshes and textures,
    /// PlumeBuilder their materials and the StartingFuelFX prefab): three
    /// splits of wood, three rags, the crate and the rag tin with their
    /// contents' places. Their shader chars, glows, scorches, and oils each
    /// renderer by its property block; the firebox's copies are lit by the
    /// fire alone, as the coal is. Without the bundle, simple bodies.
    /// </summary>
    internal static class StartingFuelBody
    {
        internal const string BodyName = "starting fuel body";
        internal const string ContentsName = "[rf contents]";
        internal const int WoodVariants = 3, RagPoses = 3;
        internal static string SlotName(int i) => "slot " + i;
        private static readonly int CharId = Shader.PropertyToID("_Char");
        private static readonly int GlowId = Shader.PropertyToID("_Glow");
        private static readonly int ScorchId = Shader.PropertyToID("_Scorch");
        private static readonly int OilId = Shader.PropertyToID("_Oil");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        internal static bool Authored => PlumeAssets.StartingFuelPrefab != null;

        internal static Bounds Bounds(StartingFuelKind kind) => new Bounds(Vector3.zero,
            kind == StartingFuelKind.Wood ? new Vector3(0.10f, 0.09f, 0.34f)
            : kind == StartingFuelKind.Rag ? new Vector3(0.16f, 0.07f, 0.15f)
            : kind == StartingFuelKind.WoodCrate ? new Vector3(0.50f, 0.30f, 0.38f) : new Vector3(0.42f, 0.28f, 0.30f));

        private static string Source(StartingFuelKind kind, int variant) =>
            kind == StartingFuelKind.Wood ? "wood " + variant % WoodVariants
            : kind == StartingFuelKind.Rag ? "rag " + variant % RagPoses
            : kind == StartingFuelKind.WoodCrate ? "crate" : "rag tin";

        /// <summary>A body under <paramref name="parent"/>; in the firebox, lit by its fire.</summary>
        internal static Transform Build(Transform parent, StartingFuelKind kind, int variant, bool inFirebox = false)
        {
            var prefab = PlumeAssets.StartingFuelPrefab;
            var source = prefab != null ? prefab.transform.Find(Source(kind, variant)) : null;
            if (source == null) return Fallback.Build(parent, kind);
            var root = Object.Instantiate(source.gameObject, parent, false).transform;
            root.name = BodyName;
            root.localPosition = Vector3.zero; root.localRotation = Quaternion.identity; root.localScale = Vector3.one;
            root.gameObject.SetActive(true);
            if (inFirebox)
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(m => FireLit(m)).ToArray();
            // A container's slots take its contents.
            if (FireStartingItems.IsContainer(kind))
            {
                var content = FireStartingItems.Content(kind);
                for (var i = 0; i < FireStartingItems.ContainerCapacity; i++)
                {
                    var slot = root.Find(SlotName(i));
                    if (slot == null) continue;
                    var piece = Build(slot, content, i * 7 % (content == StartingFuelKind.Wood ? WoodVariants : RagPoses));
                    piece.name = "piece";
                }
            }
            return root;
        }

        private static Material FireLit(Material material)
        {
            if (material == null) return null;
            var lit = PlumeAssets.StartingFuelFireMaterial(material.name);
            return lit != null ? lit : material;
        }

        /// <summary>Gives a loose piece one of the other splits or rag poses.</summary>
        internal static void Vary(Transform body, MeshCollider hull, StartingFuelKind kind, int variant)
        {
            var source = PlumeAssets.StartingFuelPrefab?.transform.Find(Source(kind, variant))?.GetComponent<MeshFilter>()?.sharedMesh;
            var filter = body != null ? body.GetComponent<MeshFilter>() : null;
            if (source == null || filter == null) return;
            filter.sharedMesh = source;
            if (hull != null) hull.sharedMesh = source;
        }

        internal static Collider[] AddColliders(GameObject item, StartingFuelKind kind)
        {
            var bounds = Bounds(kind);
            if (kind == StartingFuelKind.Wood)
            {
                var mesh = PlumeAssets.StartingFuelPrefab?.transform.Find("wood 0")?.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh != null)
                {
                    var hull = item.AddComponent<MeshCollider>();
                    hull.sharedMesh = mesh;
                    hull.convex = true;
                    return new Collider[] { hull };
                }
                var box = item.AddComponent<BoxCollider>(); box.size = bounds.size;
                return new Collider[] { box };
            }
            if (kind == StartingFuelKind.Rag)
            {
                var box = item.AddComponent<BoxCollider>(); box.size = new Vector3(0.14f, 0.05f, 0.13f); box.center = new Vector3(0f, 0.005f, 0f);
                return new Collider[] { box };
            }
            // The walls and floor; over the open top, the contents to reach into.
            var walls = item.AddComponent<BoxCollider>();
            walls.size = new Vector3(bounds.size.x, bounds.size.y - 0.03f, bounds.size.z);
            walls.center = new Vector3(0f, -0.015f, 0f);
            var contents = new GameObject(ContentsName);
            contents.transform.SetParent(item.transform, false);
            contents.transform.localPosition = new Vector3(0f, bounds.extents.y - 0.02f, 0f);
            var top = contents.AddComponent<BoxCollider>();
            top.size = new Vector3(bounds.size.x - 0.06f, 0.05f, bounds.size.z - 0.06f);
            return new Collider[] { walls, top };
        }

        /// <summary>Shows a body's state: surface char, ember glow, scorch, oil, 0 to 1, and its own pattern.</summary>
        internal static void SetState(Renderer[] renderers, MaterialPropertyBlock block, float charred, float glow, float scorch, float oil, float seed, bool own = false)
        {
            if (renderers == null) return;
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                // A loose item's renderers are the game's to set property
                // blocks on (highlighting): its state goes on its own materials.
                if (own && Authored)
                {
                    foreach (var material in renderer.materials)
                    {
                        material.SetFloat(CharId, charred);
                        material.SetFloat(GlowId, glow);
                        material.SetFloat(ScorchId, scorch);
                        material.SetFloat(OilId, oil);
                        material.SetFloat(SeedId, seed);
                    }
                    continue;
                }
                renderer.GetPropertyBlock(block);
                if (Authored)
                {
                    block.SetFloat(CharId, charred);
                    block.SetFloat(GlowId, glow);
                    block.SetFloat(ScorchId, scorch);
                    block.SetFloat(OilId, oil);
                    block.SetFloat(SeedId, seed);
                }
                else
                {
                    var tint = Color.Lerp(Color.white, new Color(0.35f, 0.3f, 0.25f), oil);
                    block.SetColor(ColorId, Color.Lerp(tint, new Color(0.06f, 0.05f, 0.045f), charred));
                }
                renderer.SetPropertyBlock(block);
            }
        }

        // --- Sounds: knocks synthesized here, no third-party audio ---

        private static readonly Dictionary<StartingFuelKind, AudioClip> knocks = new Dictionary<StartingFuelKind, AudioClip>();

        /// <summary>A dry wood knock, a hollow crate's thud, a tin's clank; a rag is silent.</summary>
        internal static AudioClip Knock(StartingFuelKind kind)
        {
            if (kind == StartingFuelKind.Rag) return null;
            if (knocks.TryGetValue(kind, out var clip) && clip != null) return clip;
            const int rate = 22050;
            var (modes, length) = kind == StartingFuelKind.Wood ? (new[] { (430f, 0.035f, 1f), (790f, 0.022f, 0.6f), (1370f, 0.012f, 0.35f) }, 0.16f)
                : kind == StartingFuelKind.WoodCrate ? (new[] { (165f, 0.07f, 1f), (310f, 0.05f, 0.55f), (620f, 0.025f, 0.3f) }, 0.3f)
                : (new[] { (980f, 0.18f, 0.7f), (2230f, 0.12f, 0.5f), (3650f, 0.07f, 0.3f), (420f, 0.06f, 0.5f) }, 0.45f);
            var samples = new float[(int)(rate * length)];
            var random = new System.Random(kind.GetHashCode() * 977 + 13);
            for (var i = 0; i < samples.Length; i++)
            {
                var t = i / (float)rate;
                var v = 0f;
                foreach (var (f, decay, gain) in modes) v += gain * Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-t / decay);
                v += 0.5f * (float)(random.NextDouble() * 2 - 1) * Mathf.Exp(-t / 0.006f);
                samples[i] = Mathf.Clamp(v * 0.35f, -1f, 1f);
            }
            clip = AudioClip.Create("RF " + kind + " knock", samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            knocks[kind] = clip;
            return clip;
        }

        // --- Flames ---

        /// <summary>
        /// A flame emitter: a copy of the firebox's own flame system (its
        /// flipbook, fades, and growth), emitting itself. The caller sets its
        /// rate and sizes; it scales with its parent.
        /// </summary>
        internal static ParticleSystem FlameSystem(Transform parent, string name, bool world = false)
        {
            var source = PlumeAssets.FireboxPrefab?.transform.Find("Flames")?.GetComponent<ParticleSystem>();
            ParticleSystem ps;
            if (source != null)
            {
                var copy = Object.Instantiate(source.gameObject, parent, false);
                copy.name = name;
                ps = copy.GetComponent<ParticleSystem>();
            }
            else
            {
                ps = Fallback.Flame(parent, name);
            }
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.simulationSpace = world ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.14f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
            // The flame shader brightens its tint about fourfold: a wood
            // flame's orange, not white; tongues upright, not spun.
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.9f, 0.36f, 0.07f, 0.5f), new Color(1f, 0.5f, 0.12f, 0.65f));
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
            var spin = ps.rotationOverLifetime; spin.enabled = false;
            var drawn = ps.GetComponent<ParticleSystemRenderer>();
            if (drawn != null) drawn.renderMode = ParticleSystemRenderMode.Billboard;
            main.maxParticles = 160;
            main.playOnAwake = true;
            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(0.1f, 0.1f, 0.01f);
            ps.Play();
            return ps;
        }

        /// <summary>A small steady flame over a loose rag: <paramref name="width"/> by <paramref name="height"/> m.</summary>
        internal static GameObject AddFlame(Transform parent, float width, float height)
        {
            var ps = FlameSystem(parent, "small flame", world: true);
            ps.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var main = ps.main;
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(width * 0.7f, width * 1.2f);
            main.startSizeY = new ParticleSystem.MinMaxCurve(height * 0.7f, height * 1.2f);
            main.startSizeZ = main.startSizeX;
            var emission = ps.emission; emission.rateOverTime = 35f;
            var shape = ps.shape; shape.scale = new Vector3(width, width, 0.01f);
            return ps.gameObject;
        }

        // --- Icons ---

        internal static void SetIcon(InventoryItemSpec spec, StartingFuelKind kind)
        {
            var texture = PlumeAssets.StartingFuelIcon(kind == StartingFuelKind.Wood ? "starting_wood_icon" : kind == StartingFuelKind.Rag ? "starting_rag_icon"
                : kind == StartingFuelKind.WoodCrate ? "starting_crate_icon" : "starting_tin_icon") ?? Fallback.Icon(kind);
            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100);
            spec.ItemIconSprite = spec.ItemIconSpriteSimple = spec.ItemIconSpriteDropped = sprite;
            var dropped = PlumeAssets.StartingFuelIcon(texture.name + "_dropped");
            if (dropped != null)
                HarmonyLib.AccessTools.Field(typeof(InventoryItemSpec), "itemIconSpriteDropped")
                    .SetValue(spec, Sprite.Create(dropped, new Rect(0, 0, dropped.width, dropped.height), new Vector2(0.5f, 0.5f), 100));
        }

        /// <summary>Plain bodies for a game without the effects bundle.</summary>
        private static class Fallback
        {
            private static Material wood, cloth, metal;

            private static Material Plain(ref Material cached, Color color)
            {
                if (cached == null)
                {
                    var source = Resources.Load<GameObject>("shovel")?.transform.Find("model")?.GetComponent<Renderer>()?.sharedMaterial;
                    cached = source != null ? new Material(source) : new Material(Shader.Find("Standard"));
                    cached.color = color;
                    cached.mainTexture = Texture2D.whiteTexture;
                    cached.DisableKeyword("_NORMALMAP");
                    cached.DisableKeyword("_METALLICGLOSSMAP");
                    cached.SetFloat("_Metallic", 0f);
                    cached.SetFloat("_Glossiness", 0.15f);
                }
                return cached;
            }

            internal static Transform Build(Transform parent, StartingFuelKind kind)
            {
                var root = new GameObject(BodyName).transform; root.SetParent(parent, false);
                var size = Bounds(kind).size;
                if (kind == StartingFuelKind.Wood)
                    Part(root, "split", Vector3.zero, new Vector3(0.085f, 0.075f, 0.33f), Plain(ref wood, new Color(0.55f, 0.40f, 0.24f)));
                else if (kind == StartingFuelKind.Rag)
                    Part(root, "rag", Vector3.zero, new Vector3(0.13f, 0.04f, 0.12f), Plain(ref cloth, new Color(0.55f, 0.52f, 0.45f)));
                else
                {
                    var mat = kind == StartingFuelKind.WoodCrate ? Plain(ref wood, new Color(0.55f, 0.40f, 0.24f)) : Plain(ref metal, new Color(0.2f, 0.27f, 0.2f));
                    Part(root, "floor", new Vector3(0f, -size.y / 2f + 0.01f, 0f), new Vector3(size.x, 0.02f, size.z), mat);
                    for (var s = -1; s <= 1; s += 2)
                    {
                        Part(root, "side", new Vector3(s * (size.x / 2f - 0.01f), 0f, 0f), new Vector3(0.02f, size.y, size.z), mat);
                        Part(root, "end", new Vector3(0f, 0f, s * (size.z / 2f - 0.01f)), new Vector3(size.x, size.y, 0.02f), mat);
                    }
                    var content = FireStartingItems.Content(kind);
                    for (var i = 0; i < FireStartingItems.ContainerCapacity; i++)
                    {
                        var slot = new GameObject(SlotName(i)).transform; slot.SetParent(root, false);
                        slot.localPosition = new Vector3((i % 4 - 1.5f) * size.x / 4.5f, -size.y / 2f + 0.06f + i / 4 * 0.07f, 0f);
                        if (content == StartingFuelKind.Wood) slot.localRotation = Quaternion.Euler(0f, 90f, 0f);
                        Build(slot, content).name = "piece";
                    }
                }
                return root;
            }

            private static void Part(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
            {
                var obj = GameObject.CreatePrimitive(PrimitiveType.Cube); obj.name = name;
                Object.DestroyImmediate(obj.GetComponent<Collider>());
                obj.transform.SetParent(parent, false); obj.transform.localPosition = position; obj.transform.localScale = scale;
                obj.GetComponent<Renderer>().sharedMaterial = material;
            }

            private static Material flame;
            internal static ParticleSystem Flame(Transform parent, string name)
            {
                var go = new GameObject(name); go.transform.SetParent(parent, false);
                var ps = go.AddComponent<ParticleSystem>();
                var main = ps.main; main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
                main.startColor = new Color(1f, 0.45f, 0.08f, 0.8f);
                var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 0));
                if (flame == null)
                {
                    flame = new Material(Shader.Find("Legacy Shaders/Particles/Additive"));
                    var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                    var pixels = new Color[1024];
                    for (var y = 0; y < 32; y++) for (var x = 0; x < 32; x++)
                    {
                        var r = new Vector2((x - 15.5f) / 15.5f, (y - 15.5f) / 15.5f).sqrMagnitude;
                        pixels[y * 32 + x] = new Color(1, 1, 1, Mathf.Pow(Mathf.Clamp01(1 - r), 2));
                    }
                    texture.SetPixels(pixels); texture.Apply(); flame.mainTexture = texture;
                }
                ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = flame;
                return ps;
            }

            internal static Texture2D Icon(StartingFuelKind kind)
            {
                var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false) { name = "RF " + kind + " icon" };
                var pixels = new Color[64 * 64];
                for (var y = 0; y < 64; y++) for (var x = 0; x < 64; x++)
                {
                    var box = FireStartingItems.IsContainer(kind);
                    var inside = box ? x > 7 && x < 57 && y > 12 && y < 51
                        : kind == StartingFuelKind.Wood ? Mathf.Abs(x - y) < 9 && x + y > 25 && x + y < 101
                        : (new Vector2(x - 32, y - 30)).magnitude < 20 + 3 * Mathf.Sin(Mathf.Atan2(y - 30, x - 32) * 5);
                    if (!inside) continue;
                    var color = kind == StartingFuelKind.Rag || kind == StartingFuelKind.RagBox ? new Color(0.72f, 0.69f, 0.57f) : new Color(0.70f, 0.45f, 0.22f);
                    color.a = 1; pixels[y * 64 + x] = color;
                }
                texture.SetPixels(pixels); texture.Apply();
                return texture;
            }
        }
    }
}
