using System;
using System.Collections.Generic;
using System.Diagnostics;
using RealisticFiring.Core;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace RealisticFiring.Items
{
 internal static class ItemIcons
    {
        /// <summary>A layer no game camera or light uses (DV.Layers names 0 to 29); the rig is there only during a render.</summary>
        internal const int Layer = 31;

        /// <summary>Where the log goes: the one line to change in another mod.</summary>
        internal static Action<string> Log = text => Main.Mod?.Logger.Log(text);

        /// <summary>The square the game rendered its icons in, px, and how many times over we render it.</summary>
        internal const int Canvas = 128, Supersample = 4;

        /// <summary>The longest side the item is brought to, px of <see cref="Canvas"/>: the stock icons' 100 to 108.</summary>
        internal const float LongSide = 104f;

        /// <summary>The camera's field of view, degrees: a gentle perspective, as most stock icons show.</summary>
        internal const float FieldOfView = 20f;

        // Far below the world, where nothing else is drawn.
        private static readonly Vector3 Away = new Vector3(0f, -20000f, 0f);

        /// <summary>
        /// The light the icons are rendered in. Directions are the way the
        /// light travels, as Euler angles in the camera's frame (it looks
        /// along +z, y up).
        /// </summary>
        /// <remarks>
        /// Matched in game (job 3389) by rendering ten stock items from their
        /// previews and comparing colors with their own icons where both are
        /// solid: 32 settings of key, ambient, and reflection; this one's mean
        /// difference is 0.063 of full scale, its luminance 0.015 brighter and
        /// saturation 0.007 less than the game's.
        /// </remarks>
        internal sealed class Lighting
        {
            public Vector3 KeyAngles = new Vector3(40f, 30f, 0f);
            public float KeyIntensity = 1f;
            public Color KeyColor = new Color(1f, 0.97f, 0.92f);
            public Vector3 FillAngles = new Vector3(10f, -150f, 0f);
            public float FillIntensity = 0.35f;
            public Color FillColor = new Color(0.85f, 0.9f, 1f);
            public Color Ambient = new Color(0.55f, 0.55f, 0.572f);
            public Color Reflection = new Color(0.45f, 0.46f, 0.48f);
            public float ReflectionIntensity = 1f;
        }

        /// <summary>The lighting every icon is rendered in.</summary>
        internal static Lighting Light = new Lighting();

        /// <summary>
        /// The game's icon view before an item's own turns: the stock icon
        /// renderer's pivot (a scene setting, not in its code), fitted in game
        /// (job 3389) by rendering ten stock items from their previews at each
        /// pivot on a 15 degree grid, refined to 2, against their own icons'
        /// silhouettes: a mean overlap of 0.84, 0.90 to 0.96 for the devices
        /// and lamps. The shovel's (0.16) differs: its icon was rendered with
        /// its pivot pulled 1.4 m toward the camera, a close perspective.
        /// </summary>
        internal static Vector3 StockPivotAngles = new Vector3(12f, 335f, 0f);

        /// <summary>The pivot as a rotation; made when asked, so the type's initializer never calls into the engine.</summary>
        internal static Quaternion StockPivot => Quaternion.Euler(StockPivotAngles);

        /// <summary>An item's normal and dropped icons.</summary>
        internal sealed class Icons
        {
            public Sprite Icon, Dropped;
            public float Milliseconds;
        }

        private static readonly Dictionary<string, Icons> Made = new Dictionary<string, Icons>();
        private static readonly HashSet<string> Failed = new HashSet<string>();
        private static Cubemap reflection;
        private static Color reflectionColor;

        /// <summary>
        /// The view the game's icon renderer gave an item with
        /// <paramref name="spec"/>'s icon settings, its own turn and offset
        /// applied to the renderer's pivot; for an item copied from a stock
        /// one, the stock item's view.
        /// </summary>
        internal static Quaternion StockView(InventoryItemSpec spec) => spec == null ? StockPivot
            : Quaternion.Euler(spec.iconRenderAngleOffset) * StockPivot * Quaternion.Euler(spec.PreviewRotation);

        /// <summary>
        /// Sets <paramref name="spec"/>'s icons from <paramref name="model"/>
        /// seen at <paramref name="view"/>: rendered once a session under
        /// <paramref name="key"/>, then reused. On failure the item keeps the
        /// icons it has, and the failure is logged once. True when set.
        /// </summary>
        internal static bool Apply(InventoryItemSpec spec, GameObject model, Quaternion view, string key, float fieldOfView = FieldOfView)
        {
            if (spec == null)
                return false;
            var icons = Render(model, view, key, fieldOfView);
            if (icons == null)
                return false;
            spec.ItemIconSpriteSimple = icons.Icon;
            spec.ItemIconSprite = icons.Icon;
            spec.ItemIconSpriteDropped = icons.Dropped;
            return true;
        }

        /// <summary>
        /// The icons for <paramref name="model"/> (any object, active or not:
        /// the enabled renderers on objects active under it count) turned by
        /// <paramref name="view"/>, made once a session under
        /// <paramref name="key"/>; null if it could not be rendered (logged
        /// once). A wider <paramref name="fieldOfView"/> brings the camera
        /// close, as the stock shovel's icon was drawn, its blade large.
        /// </summary>
        internal static Icons Render(GameObject model, Quaternion view, string key, float fieldOfView = FieldOfView)
        {
            if (Made.TryGetValue(key, out var made) && made.Icon != null && made.Dropped != null)
                return made;
            if (Failed.Contains(key))
                return null;
            try
            {
                var icons = RenderNow(model, view, key, Light, Canvas, Supersample, LongSide, fieldOfView);
                Made[key] = icons;
                Log?.Invoke($"Icon for {key} rendered from its model: {icons.Icon.rect.width:F0} by {icons.Icon.rect.height:F0} px in {icons.Milliseconds:F0} ms");
                return icons;
            }
            catch (Exception error)
            {
                Failed.Add(key);
                Log?.Invoke($"Icon for {key} could not be rendered ({error.Message}); it keeps its drawn icon");
                return null;
            }
        }

        /// <summary>
        /// Renders now, without the session's cache or log: for comparing
        /// views and lighting. Throws when nothing could be rendered.
        /// <paramref name="longSide"/> of 0 keeps the framing's own size.
        /// </summary>
        internal static Icons RenderNow(GameObject model, Quaternion view, string name, Lighting lighting, int canvas, int supersample, float longSide,
            float fieldOfView = FieldOfView)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));
            var clock = Stopwatch.StartNew();
            var linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            var size = canvas * supersample;
            GameObject rig = null;
            RenderTexture target = null;
            Texture2D read = null;
            var environment = new Environment();
            try
            {
                rig = new GameObject("[item icon rig]") { hideFlags = HideFlags.HideAndDontSave };
                rig.transform.position = Away;
                var corners = new List<float>();
                if (Mirror(model, view, rig.transform, corners) == 0)
                    throw new InvalidOperationException("no meshes to render");
                var camera = MakeCamera(rig.transform, fieldOfView);
                var key = MakeLight(rig.transform, "key", lighting.KeyAngles, lighting.KeyIntensity, lighting.KeyColor);
                var fill = MakeLight(rig.transform, "fill", lighting.FillAngles, lighting.FillIntensity, lighting.FillColor);
                target = RenderTexture.GetTemporary(size, size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default, 1);
                read = new Texture2D(size, size, TextureFormat.RGBA32, false);
                camera.targetTexture = target;
                environment.Set(lighting, key, fill);

                var fillShare = 0.9f;
                float[] image = null;
                int x = 0, y = 0, w = 0, h = 0;
                // The framing fits the meshes' boxes; once more if the item itself came out much larger or smaller than wanted.
                for (var pass = 0; pass < 2; pass++)
                {
                    var tan = Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);
                    if (!ItemIconRaster.Frame(corners.ToArray(), tan, fillShare, out var cx, out var cy, out var cz, out var near, out var far))
                        throw new InvalidOperationException("nothing to frame");
                    camera.transform.localPosition = new Vector3(cx, cy, cz);
                    camera.nearClipPlane = near;
                    camera.farClipPlane = far;
                    var black = Shoot(camera, target, read, Color.clear);
                    var white = Shoot(camera, target, read, new Color(1f, 1f, 1f, 0f));
                    image = ItemIconRaster.Downsample(ItemIconRaster.Unblend(black, white, linear), size, size, supersample);
                    if (!ItemIconRaster.Bounds(image, canvas, canvas, out x, out y, out w, out h))
                        throw new InvalidOperationException("the render came out empty (its shaders drew nothing offscreen)");
                    var longest = Mathf.Max(w, h);
                    if (longSide <= 0f || Mathf.Abs(longest - longSide) <= 3f || pass == 1)
                        break;
                    fillShare = Mathf.Min(0.98f, fillShare * longSide / longest);
                }
                var iconBytes = ItemIconRaster.Encode(image, canvas, canvas, x, y, w, h, linear, out var iw, out var ih);
                var droppedBytes = ItemIconRaster.Dropped(image, canvas, canvas, x, y, w, h, out var dw, out var dh);
                var icons = new Icons
                {
                    Icon = MakeSprite(name + "_Icon", iconBytes, iw, ih),
                    Dropped = MakeSprite(name + "_Icon_Dropped", droppedBytes, dw, dh),
                };
                icons.Milliseconds = (float)clock.Elapsed.TotalMilliseconds;
                return icons;
            }
            finally
            {
                environment.Restore();
                if (target != null)
                    RenderTexture.ReleaseTemporary(target);
                if (read != null)
                    Object.DestroyImmediate(read);
                if (rig != null)
                    Object.DestroyImmediate(rig);
            }
        }

        // Copies the model's visible meshes under the rig, turned to the view, and collects their boxes' corners in the rig's frame.
        private static int Mirror(GameObject model, Quaternion view, Transform rig, List<float> corners)
        {
            var skipped = new HashSet<Renderer>();
            foreach (var lods in model.GetComponentsInChildren<LODGroup>(true))
            {
                var levels = lods.GetLODs();
                for (var i = 1; i < levels.Length; i++)
                    foreach (var r in levels[i].renderers)
                        if (r != null)
                            skipped.Add(r);
            }
            var toModel = model.transform.worldToLocalMatrix;
            var turn = Matrix4x4.Rotate(view);
            var block = new MaterialPropertyBlock();
            var count = 0;
            foreach (var source in model.GetComponentsInChildren<Renderer>(true))
            {
                if (!source.enabled || skipped.Contains(source) || !ShownUnder(source.transform, model.transform))
                    continue;
                Mesh mesh = null;
                var text = source.GetComponent<TextMesh>();
                if (source is SkinnedMeshRenderer skinned)
                    mesh = skinned.sharedMesh;
                else if (source is MeshRenderer)
                {
                    var filter = source.GetComponent<MeshFilter>();
                    mesh = filter != null ? filter.sharedMesh : null;
                }
                if (mesh == null && text == null)
                    continue;
                var copy = new GameObject(source.name) { layer = Layer, hideFlags = HideFlags.HideAndDontSave };
                copy.transform.SetParent(rig, false);
                var local = turn * toModel * source.transform.localToWorldMatrix;
                copy.transform.localPosition = local.GetColumn(3);
                copy.transform.localRotation = local.rotation;
                copy.transform.localScale = local.lossyScale;
                if (text != null)
                {
                    var words = copy.AddComponent<TextMesh>();
                    words.text = text.text;
                    words.font = text.font;
                    words.fontSize = text.fontSize;
                    words.fontStyle = text.fontStyle;
                    words.characterSize = text.characterSize;
                    words.lineSpacing = text.lineSpacing;
                    words.anchor = text.anchor;
                    words.alignment = text.alignment;
                    words.offsetZ = text.offsetZ;
                    words.tabSize = text.tabSize;
                    words.richText = text.richText;
                    words.color = text.color;
                }
                else
                    copy.AddComponent<MeshFilter>().sharedMesh = mesh;
                // A text mesh brings its own renderer.
                var renderer = text != null ? copy.GetComponent<MeshRenderer>() : copy.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = source.sharedMaterials;
                source.GetPropertyBlock(block);
                renderer.SetPropertyBlock(block);
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                renderer.allowOcclusionWhenDynamic = false;
                if (mesh != null)
                {
                    var b = mesh.bounds;
                    var m = copy.transform.localToWorldMatrix;
                    var toRig = rig.worldToLocalMatrix * m;
                    for (var i = 0; i < 8; i++)
                    {
                        var corner = toRig.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents,
                            new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f)));
                        corners.Add(corner.x);
                        corners.Add(corner.y);
                        corners.Add(corner.z);
                    }
                }
                count++;
            }
            return count;
        }

        // Shown when it and each object up to the model's root are active themselves (the root may sit in an inactive holder).
        private static bool ShownUnder(Transform t, Transform root)
        {
            for (; t != null; t = t.parent)
            {
                if (!t.gameObject.activeSelf)
                    return false;
                if (t == root)
                    return true;
            }
            return false;
        }

        private static Camera MakeCamera(Transform rig, float fieldOfView)
        {
            var holder = new GameObject("camera") { hideFlags = HideFlags.HideAndDontSave };
            holder.transform.SetParent(rig, false);
            var camera = holder.AddComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.cullingMask = 1 << Layer;
            camera.orthographic = false;
            camera.fieldOfView = fieldOfView;
            camera.aspect = 1f;
            camera.renderingPath = RenderingPath.Forward;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.useOcclusionCulling = false;
            camera.stereoTargetEye = StereoTargetEyeMask.None;
            camera.depthTextureMode = DepthTextureMode.None;
            return camera;
        }

        private static Light MakeLight(Transform rig, string name, Vector3 angles, float intensity, Color color)
        {
            var holder = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            holder.transform.SetParent(rig, false);
            holder.transform.localRotation = Quaternion.Euler(angles);
            var light = holder.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.color = color;
            light.shadows = LightShadows.None;
            light.cullingMask = 1 << Layer;
            light.renderMode = LightRenderMode.ForcePixel;
            light.enabled = intensity > 0f;
            return light;
        }

        // Renders over the background and reads it back, RGBA32 bytes.
        private static byte[] Shoot(Camera camera, RenderTexture target, Texture2D read, Color background)
        {
            camera.backgroundColor = background;
            var previous = RenderTexture.active;
            try
            {
                camera.Render();
                RenderTexture.active = target;
                read.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false);
                return read.GetRawTextureData();
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }

        private static Sprite MakeSprite(string name, byte[] rgba, int width, int height)
        {
            var colors = new Color32[width * height];
            for (var i = 0; i < colors.Length; i++)
                colors[i] = new Color32(rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3]);
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, true)
            {
                name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 0,
            };
            texture.SetPixels32(colors);
            texture.Apply(true, true);
            texture.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.name = name;
            sprite.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            return sprite;
        }

        private static Cubemap ReflectionOf(Color color)
        {
            if (reflection != null && reflectionColor == color)
                return reflection;
            if (reflection == null)
                reflection = new Cubemap(8, TextureFormat.RGBA32, false) { name = "[item icon reflection]", hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color[8 * 8];
            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = color;
            foreach (CubemapFace face in Enum.GetValues(typeof(CubemapFace)))
                if (face != CubemapFace.Unknown)
                    reflection.SetPixels(pixels, face);
            reflection.Apply(false, false);
            reflectionColor = color;
            return reflection;
        }

        /// <summary>The scene's lighting that a render changes, and every other light kept off the icon layer, put back after.</summary>
        private sealed class Environment
        {
            private bool set;
            private AmbientMode ambientMode;
            private Color ambientLight;
            private float ambientIntensity;
            private SphericalHarmonicsL2 ambientProbe;
            private DefaultReflectionMode reflectionMode;
            private Cubemap customReflection;
            private float reflectionIntensity;
            private bool fog;
            private readonly List<(Light light, int mask)> lights = new List<(Light, int)>();

            internal void Set(Lighting lighting, Light key, Light fill)
            {
                ambientMode = RenderSettings.ambientMode;
                ambientLight = RenderSettings.ambientLight;
                ambientIntensity = RenderSettings.ambientIntensity;
                ambientProbe = RenderSettings.ambientProbe;
                reflectionMode = RenderSettings.defaultReflectionMode;
                customReflection = RenderSettings.customReflection;
                reflectionIntensity = RenderSettings.reflectionIntensity;
                fog = RenderSettings.fog;
                set = true;
                var bit = 1 << Layer;
                foreach (var light in Object.FindObjectsOfType<Light>())
                {
                    if (light == key || light == fill || (light.cullingMask & bit) == 0)
                        continue;
                    lights.Add((light, light.cullingMask));
                    light.cullingMask &= ~bit;
                }
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = lighting.Ambient;
                RenderSettings.ambientIntensity = 1f;
                var probe = new SphericalHarmonicsL2();
                probe.AddAmbientLight(QualitySettings.activeColorSpace == ColorSpace.Linear ? lighting.Ambient.linear : lighting.Ambient);
                RenderSettings.ambientProbe = probe;
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.customReflection = ReflectionOf(lighting.Reflection);
                RenderSettings.reflectionIntensity = lighting.ReflectionIntensity;
                RenderSettings.fog = false;
            }

            internal void Restore()
            {
                foreach (var (light, mask) in lights)
                    if (light != null)
                        light.cullingMask = mask;
                lights.Clear();
                if (!set)
                    return;
                set = false;
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientLight = ambientLight;
                RenderSettings.ambientIntensity = ambientIntensity;
                RenderSettings.ambientProbe = ambientProbe;
                RenderSettings.defaultReflectionMode = reflectionMode;
                RenderSettings.customReflection = customReflection;
                RenderSettings.reflectionIntensity = reflectionIntensity;
                RenderSettings.fog = fog;
            }
        }
    }
}
