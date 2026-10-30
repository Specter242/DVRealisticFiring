using System.Collections.Generic;
using RealisticStackEffects.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace RealisticStackEffects
{
    /// <summary>
    /// The chimney's smoke drawn at half resolution, at a lower smoke quality
    /// (<see cref="SmokeDetail"/>; Nexus, 2026-10-25: two players' frame rate
    /// fell from 60 to 15 with the plume, whose cost is blending its
    /// particles' many overlapping layers, so a quarter as many pixels cost
    /// about a quarter as much). The custom effects' cloud, sooty core, and
    /// jet draw nothing in the camera's own drawing (their shader puts their
    /// triangles beyond the screen: hidden with forceRenderingOff, the game
    /// built none of their particles). Before the scene's transparent things
    /// a command buffer draws them into a target half the screen's size each
    /// way, against the scene's depth laid at that size (PlumeDepth) and with
    /// their coverage kept in alpha. An object of the mod's among the
    /// transparent things, sorted where the plume was, lays that over the
    /// scene (PlumeComposite): laid over from the command buffer, the sky,
    /// drawn among the transparent things, covered it. A second buffer after
    /// them lets the target go. The heat shimmer and the cinders draw as
    /// before. Only for a deferred camera drawing to the screen outside VR,
    /// where the scene's depth is to hand; anywhere else the smoke draws as
    /// before, with fewer particles instead.
    /// </summary>
    internal static class PlumeOffscreen
    {
        // The smoke shader's channels: with its coverage at half resolution, without on the screen.
        private const float AllChannels = 15f;
        private const float ColorChannels = 14f;

        // The laying over sorted this far behind the nearest stack, m: behind
        // its cinders, as the cloud was (ChimneyPlume.DrawnBefore). At the
        // cloud's sorting order it draws after every shimmer, which sorts at
        // order 0 (ChimneyPlume.ShimmerFirst), so the shimmer's copy holds none of it.
        private const float BehindStack = 0.2f;

        private static readonly int ColorMask = Shader.PropertyToID("_ColorMask");
        private static readonly int OffscreenOnly = Shader.PropertyToID("_Offscreen");
        private static readonly int OffscreenPass = Shader.PropertyToID("_PlumeOffscreenPass");
        private static readonly int Ready = Shader.PropertyToID("_PlumeOffscreenReady");
        private static readonly int Low = Shader.PropertyToID("_PlumeLow");
        private static readonly int LowDepth = Shader.PropertyToID("_PlumeLowDepth");
        private static readonly int Full = Shader.PropertyToID("_PlumeFull");
        private static readonly int Half = Shader.PropertyToID("_PlumeHalf");
        private static readonly int Debug = Shader.PropertyToID("_PlumeCompositeDebug");
        private static readonly int LightPosition = Shader.PropertyToID("_WorldSpaceLightPos0");
        private static readonly int LightColor = Shader.PropertyToID("_LightColor0");

        private const CameraEvent Before = CameraEvent.BeforeForwardAlpha;
        private const CameraEvent After = CameraEvent.AfterForwardAlpha;

        private sealed class Buffers
        {
            public CommandBuffer Before;
            public CommandBuffer After;
        }

        private static readonly Dictionary<Camera, Buffers> Attached = new Dictionary<Camera, Buffers>();
        private static readonly List<ParticleSystemRenderer> Renderers = new List<ParticleSystemRenderer>();
        private static readonly List<(ChimneyPlume Plume, float Distance)> Order = new List<(ChimneyPlume, float)>();
        private static readonly List<Camera> Gone = new List<Camera>();
        private static readonly Plane[] Frustum = new Plane[6];
        private static Mesh triangle;
        private static GameObject overlay;
        private static Material overlayMaterial;
        private static bool hooked;
        private static int decided = -1;

        // The first fill since it began drawing so is logged once.
        private static bool described;

        /// <summary>Whether the plumes are drawn at half resolution this frame.</summary>
        public static bool Drawing { get; private set; }

        /// <summary>
        /// Once a frame, before the cameras draw: whether the plumes go off
        /// screen, each plume's renderers set to match. Called by each plume.
        /// </summary>
        public static void Decide()
        {
            if (decided == Time.frameCount)
                return;
            decided = Time.frameCount;
            var viewer = Viewer();
            var drawing = StackHost.Settings.HalfResolutionAllowed && SmokeDetail.HalfResolution(StackHost.Settings.SmokeQuality)
                          && StackHost.Assets.CompositeMaterial != null && StackHost.Assets.DepthMaterial != null && Fits(viewer);
            if (drawing != Drawing)
            {
                Drawing = drawing;
                StackHost.Mod.Logger.Log(drawing
                    ? "Chimney smoke drawn at half resolution"
                    : "Chimney smoke drawn at full resolution" + (SmokeDetail.HalfResolution(StackHost.Settings.SmokeQuality)
                        ? $" (half wants a deferred camera outside VR; {Describe(viewer)})"
                        : ""));
                described = false;
                if (!drawing)
                    DetachAll();
            }
            // A camera gone (a scene unloaded) takes its buffers' release with it.
            Gone.Clear();
            foreach (var pair in Attached)
            {
                if (pair.Key == null)
                    Gone.Add(pair.Key);
            }
            foreach (var camera in Gone)
            {
                Release(Attached[camera]);
                Attached.Remove(camera);
            }
            Renderers.Clear();
            var plumes = ChimneyPlume.Plumes;
            for (var i = 0; i < plumes.Count; i++)
                plumes[i].AddOffscreen(Renderers);
            foreach (var renderer in Renderers)
            {
                var material = renderer.sharedMaterial;
                if (material != null && material.HasProperty(ColorMask) && material.HasProperty(OffscreenOnly))
                {
                    material.SetFloat(ColorMask, drawing ? AllChannels : ColorChannels);
                    material.SetFloat(OffscreenOnly, drawing ? 1f : 0f);
                }
            }
            PlaceOverlay(drawing, viewer);
            if (drawing && !hooked)
            {
                hooked = true;
                Camera.onPreCull += BeforeCull;
                Camera.onPreRender += PreRender;
            }
        }

        private static Camera Viewer() => PlayerManager.ActiveCamera != null ? PlayerManager.ActiveCamera : Camera.main;

        // A camera whose scene depth the smoke can be laid against.
        private static bool Fits(Camera camera) =>
            camera != null && camera.cameraType == CameraType.Game && camera.targetTexture == null
            && !camera.stereoEnabled && camera.actualRenderingPath == RenderingPath.DeferredShading;

        private static string Describe(Camera camera) =>
            camera == null ? "no camera" : $"{camera.name}: {camera.actualRenderingPath}" +
                                           (camera.stereoEnabled ? ", VR" : "") + (camera.targetTexture != null ? ", to a texture" : "");

        // The object that lays the smoke over the scene, sorted among the
        // transparent things as the nearest plume's cloud: its queue, sorting
        // layer, and order, and just behind its stack from the camera.
        private static void PlaceOverlay(bool drawing, Camera viewer)
        {
            ParticleSystemRenderer cloud = null;
            var nearest = float.MaxValue;
            Vector3 stack = default;
            if (drawing && viewer != null)
            {
                var eye = viewer.transform.position;
                var plumes = ChimneyPlume.Plumes;
                for (var i = 0; i < plumes.Count; i++)
                {
                    var plume = plumes[i];
                    Renderers.Clear();
                    plume.AddOffscreen(Renderers);
                    var distance = Renderers.Count > 0 ? Vector3.Distance(eye, plume.StackTop) : float.MaxValue;
                    if (distance < nearest)
                    {
                        nearest = distance;
                        cloud = Renderers[0];
                        stack = plume.StackTop;
                    }
                }
            }
            if (cloud == null)
            {
                if (overlay != null)
                    overlay.SetActive(false);
                return;
            }
            if (overlay == null)
            {
                overlay = new GameObject("Realistic Firing: chimney smoke at half resolution") { hideFlags = HideFlags.HideAndDontSave };
                Object.DontDestroyOnLoad(overlay);
                overlay.AddComponent<MeshFilter>().sharedMesh = Triangle();
                overlayMaterial = new Material(StackHost.Assets.CompositeMaterial) { hideFlags = HideFlags.DontSave };
                var made = overlay.AddComponent<MeshRenderer>();
                made.sharedMaterial = overlayMaterial;
                made.shadowCastingMode = ShadowCastingMode.Off;
                made.receiveShadows = false;
                made.lightProbeUsage = LightProbeUsage.Off;
                made.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            overlay.SetActive(true);
            overlay.layer = cloud.gameObject.layer;
            var away = (stack - viewer.transform.position).normalized;
            // A mesh has no particle sorting fudge. Include the cloud's
            // effective distance, including the stock steam's offset (10 m
            // on the S060), or this overlay always covers nearby steam.
            var sortingDistance = Vector3.Distance(viewer.transform.position, cloud.bounds.center) + cloud.sortingFudge;
            overlay.transform.position = PlumeTestBed.Look.MatchSteamOrder > 0.5f
                ? viewer.transform.position + away * Mathf.Max(0.01f, sortingDistance)
                : stack + away * BehindStack;
            var renderer = overlay.GetComponent<MeshRenderer>();
            renderer.sortingLayerID = cloud.sortingLayerID;
            renderer.sortingOrder = cloud.sortingOrder;
            if (cloud.sharedMaterial != null)
                overlayMaterial.renderQueue = cloud.sharedMaterial.renderQueue;
        }

        // Sort against the camera's final pose, before Unity sorts the scene.
        // Captures call this again after holding their own camera pose.
        internal static void SortForCamera(Camera camera)
        {
            if (Drawing && Fits(camera))
                PlaceOverlay(true, camera);
        }

        private static void BeforeCull(Camera camera) => SortForCamera(camera);

        // Filled once the camera is culled, where it will draw from (a
        // capture holds the camera at its view from its own pre-cull).
        private static void PreRender(Camera camera)
        {
            // A frame the plumes made no decision in (none left) draws none.
            var current = Drawing && decided >= Time.frameCount - 1;
            if (!current || !Fits(camera))
            {
                if (Attached.TryGetValue(camera, out var stale))
                {
                    Detach(camera, stale);
                    Attached.Remove(camera);
                }
                return;
            }
            if (!Attached.TryGetValue(camera, out var buffers))
            {
                buffers = new Buffers
                {
                    Before = new CommandBuffer { name = "Realistic Firing: chimney smoke at half resolution" },
                    After = new CommandBuffer { name = "Realistic Firing: chimney smoke at half resolution, done" },
                };
                camera.AddCommandBuffer(Before, buffers.Before);
                camera.AddCommandBuffer(After, buffers.After);
                Attached.Add(camera, buffers);
            }
            Fill(buffers, camera);
        }

        private static void Fill(Buffers buffers, Camera camera)
        {
            var buffer = buffers.Before;
            buffer.Clear();
            buffers.After.Clear();
            var depth = StackHost.Assets.DepthMaterial;
            if (depth == null)
                return;
            // The plumes in sight, farthest first, each its systems in its own order.
            GeometryUtility.CalculateFrustumPlanes(camera, Frustum);
            var eye = camera.transform.position;
            Order.Clear();
            var plumes = ChimneyPlume.Plumes;
            for (var i = 0; i < plumes.Count; i++)
            {
                var plume = plumes[i];
                Renderers.Clear();
                plume.AddOffscreen(Renderers);
                foreach (var renderer in Renderers)
                {
                    if (Sees(camera, renderer) && GeometryUtility.TestPlanesAABB(Frustum, renderer.bounds))
                    {
                        Order.Add((plume, Vector3.Distance(eye, plume.StackTop)));
                        break;
                    }
                }
            }
            if (!described)
            {
                described = true;
                StackHost.Mod.Logger.Log($"Chimney smoke at half resolution: camera '{camera.name}' {camera.pixelWidth}×{camera.pixelHeight} " +
                                    $"{camera.actualRenderingPath}, HDR {camera.allowHDR}; {Order.Count} of {ChimneyPlume.Plumes.Count} plumes in sight");
            }
            if (Order.Count == 0)
                return;
            Order.Sort((a, b) => b.Distance.CompareTo(a.Distance));

            var width = camera.pixelWidth;
            var height = camera.pixelHeight;
            var halfWidth = Mathf.Max(1, (width + 1) / 2);
            var halfHeight = Mathf.Max(1, (height + 1) / 2);
            buffer.SetGlobalVector(Full, new Vector4(width, height, 1f / width, 1f / height));
            buffer.SetGlobalVector(Half, new Vector4(halfWidth, halfHeight, 1f / halfWidth, 1f / halfHeight));
            // The scene's depth as values, then in the smoke's own depth buffer.
            buffer.GetTemporaryRT(LowDepth, halfWidth, halfHeight, 0, FilterMode.Point, RenderTextureFormat.RFloat);
            buffer.SetRenderTarget(LowDepth);
            buffer.DrawMesh(Triangle(), Matrix4x4.identity, depth, 0, 1);
            buffer.GetTemporaryRT(Low, halfWidth, halfHeight, 24, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);
            buffer.SetRenderTarget(Low);
            buffer.ClearRenderTarget(true, true, Color.clear);
            buffer.DrawMesh(Triangle(), Matrix4x4.identity, depth, 0, 0);
            buffer.SetGlobalFloat(OffscreenPass, 1f);
            SetSun(buffer);
            foreach (var (plume, _) in Order)
            {
                Renderers.Clear();
                plume.AddOffscreen(Renderers);
                foreach (var renderer in Renderers)
                {
                    if (Sees(camera, renderer))
                        buffer.DrawRenderer(renderer, renderer.sharedMaterial, 0, 0);
                }
            }
            buffer.SetGlobalFloat(OffscreenPass, 0f);
            buffer.SetGlobalFloat(Debug, PlumeTestBed.Look.OffscreenDebug);
            buffer.SetGlobalFloat(Ready, 1f);
            buffer.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);

            buffers.After.SetGlobalFloat(Ready, 0f);
            buffers.After.ReleaseTemporaryRT(Low);
            buffers.After.ReleaseTemporaryRT(LowDepth);
        }

        // The sun as the transparent things are lit by it: drawn from the
        // command buffer, the smoke had none of it in game (dull and blue
        // beside the full-resolution plume), though it had in the editor.
        private static void SetSun(CommandBuffer buffer)
        {
            var sun = ChimneyPlume.Sun();
            if (sun == null || !sun.isActiveAndEnabled)
            {
                buffer.SetGlobalVector(LightPosition, new Vector4(0f, 1f, 0f, 0f));
                buffer.SetGlobalVector(LightColor, Vector4.zero);
                return;
            }
            Color color;
            if (QualitySettings.activeColorSpace != ColorSpace.Linear)
                color = sun.color * sun.intensity;
            else if (GraphicsSettings.lightsUseLinearIntensity)
                color = sun.color.linear * sun.intensity;
            else
                color = (sun.color * sun.intensity).linear;
            var toSun = -sun.transform.forward;
            buffer.SetGlobalVector(LightPosition, new Vector4(toSun.x, toSun.y, toSun.z, 0f));
            buffer.SetGlobalVector(LightColor, new Vector4(color.r, color.g, color.b, color.a));
        }

        // A camera that draws the renderer's layer (an overlay drawing only
        // the interface would lay the smoke over it again).
        private static bool Sees(Camera camera, Renderer renderer) => (camera.cullingMask & (1 << renderer.gameObject.layer)) != 0;

        private static void Detach(Camera camera, Buffers buffers)
        {
            if (camera != null)
            {
                camera.RemoveCommandBuffer(Before, buffers.Before);
                camera.RemoveCommandBuffer(After, buffers.After);
            }
            Release(buffers);
        }

        private static void Release(Buffers buffers)
        {
            buffers.Before.Release();
            buffers.After.Release();
        }

        private static void DetachAll()
        {
            foreach (var pair in Attached)
                Detach(pair.Key, pair.Value);
            Attached.Clear();
            Shader.SetGlobalFloat(Ready, 0f);
        }

        // One triangle over the whole target in clip space (the shaders take
        // its corners as they are), never culled.
        private static Mesh Triangle()
        {
            if (triangle != null)
                return triangle;
            triangle = new Mesh { name = "Realistic Firing: whole screen" };
            triangle.vertices = new[] { new Vector3(-1f, -1f, 0f), new Vector3(3f, -1f, 0f), new Vector3(-1f, 3f, 0f) };
            triangle.triangles = new[] { 0, 1, 2 };
            triangle.bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f);
            triangle.hideFlags = HideFlags.DontSave;
            return triangle;
        }
    }
}
