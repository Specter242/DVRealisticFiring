using System.Collections.Generic;
using RealisticFiring.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace RealisticFiring.Visuals
{
 internal static class FireboxOffscreen
    {
        private static readonly int Low = Shader.PropertyToID("_FireLow");
        private static readonly int LowSize = Shader.PropertyToID("_FireLowSize");
        private static readonly int LowPass = Shader.PropertyToID("_FireLowPass");
        private static readonly int Ready = Shader.PropertyToID("_FireLowReady");
        private static readonly int UseLow = Shader.PropertyToID("_UseLow");

        private const CameraEvent Before = CameraEvent.BeforeForwardAlpha;
        private const CameraEvent After = CameraEvent.AfterForwardAlpha;

        /// <summary>The test bed's choice: under 0 by the quality, 0 never, 1 always.</summary>
        internal static float Force = -1f;

        private sealed class Buffers
        {
            public CommandBuffer Before;
            public CommandBuffer After;
        }

        private static readonly List<FireboxVolume> Volumes = new List<FireboxVolume>();
        private static readonly Dictionary<Camera, Buffers> Attached = new Dictionary<Camera, Buffers>();
        private static readonly List<Camera> Gone = new List<Camera>();
        private static bool hooked;

        /// <summary>Whether the last game camera drew a volume at half resolution, for the capture's log.</summary>
        internal static bool Drawing { get; private set; }

        public static void Add(FireboxVolume volume)
        {
            if (!Volumes.Contains(volume))
                Volumes.Add(volume);
            if (hooked)
                return;
            hooked = true;
            Camera.onPreRender += PreRender;
        }

        public static void Remove(FireboxVolume volume)
        {
            Volumes.Remove(volume);
            if (Volumes.Count > 0 || !hooked)
                return;
            hooked = false;
            Camera.onPreRender -= PreRender;
            foreach (var pair in Attached)
                Detach(pair.Key, pair.Value);
            Attached.Clear();
            Shader.SetGlobalFloat(Ready, 0f);
        }

        // A camera whose scene depth the fire can be laid against.
        private static bool Fits(Camera camera) =>
            camera != null && camera.cameraType == CameraType.Game && camera.targetTexture == null
            && !camera.stereoEnabled && camera.actualRenderingPath == RenderingPath.DeferredShading;

        // Once the camera is culled, where it will draw from (a capture holds
        // the camera at its view from its own pre-cull).
        private static void PreRender(Camera camera)
        {
            Gone.Clear();
            foreach (var pair in Attached)
            {
                if (pair.Key == null)
                    Gone.Add(pair.Key);
            }
            foreach (var gone in Gone)
            {
                Release(Attached[gone]);
                Attached.Remove(gone);
            }
            var chosen = Fits(camera) ? Choose(camera) : null;
            // Each volume's own draw lays the half-size target over the scene
            // or marches every pixel, as its material says.
            for (var i = 0; i < Volumes.Count; i++)
            {
                var material = Volumes[i] != null ? Volumes[i].VolumeMaterial : null;
                if (material != null)
                    material.SetFloat(UseLow, Volumes[i] == chosen ? 1f : 0f);
            }
            if (camera.cameraType == CameraType.Game)
                Drawing = chosen != null;
            if (chosen == null)
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
                    Before = new CommandBuffer { name = "Realistic Firing: firebox fire at half resolution" },
                    After = new CommandBuffer { name = "Realistic Firing: firebox fire at half resolution, done" },
                };
                camera.AddCommandBuffer(Before, buffers.Before);
                camera.AddCommandBuffer(After, buffers.After);
                Attached.Add(camera, buffers);
            }
            Fill(buffers, camera, chosen);
        }

        // The nearest volume the camera draws that wants half resolution.
        private static FireboxVolume Choose(Camera camera)
        {
            var settings = Main.Settings;
            if (settings == null || Force > -0.5f && Force < 0.5f)
                return null;
            FireboxVolume nearest = null;
            var least = float.MaxValue;
            var eye = camera.transform.position;
            for (var i = 0; i < Volumes.Count; i++)
            {
                var volume = Volumes[i];
                var renderer = volume != null ? volume.VolumeRenderer : null;
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy
                    || (camera.cullingMask & (1 << renderer.gameObject.layer)) == 0)
                    continue;
                var distance = Mathf.Sqrt(renderer.bounds.SqrDistance(eye));
                if (distance < least)
                {
                    least = distance;
                    nearest = volume;
                }
            }
            if (nearest == null)
                return null;
            return Force > 0.5f || settings.FireboxQuality != FireboxQuality.High ? nearest : null;
        }

        private static void Fill(Buffers buffers, Camera camera, FireboxVolume volume)
        {
            var buffer = buffers.Before;
            buffer.Clear();
            buffers.After.Clear();
            var halfWidth = Mathf.Max(1, (camera.pixelWidth + 1) / 2);
            var halfHeight = Mathf.Max(1, (camera.pixelHeight + 1) / 2);
            buffer.SetGlobalVector(LowSize, new Vector4(halfWidth, halfHeight, 1f / halfWidth, 1f / halfHeight));
            buffer.GetTemporaryRT(Low, halfWidth, halfHeight, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);
            buffer.SetRenderTarget(Low);
            // No light, and all of the scene behind coming through.
            buffer.ClearRenderTarget(false, true, new Color(0f, 0f, 0f, 1f));
            buffer.SetGlobalFloat(LowPass, 1f);
            buffer.DrawRenderer(volume.VolumeRenderer, volume.VolumeMaterial, 0, 0);
            buffer.SetGlobalFloat(LowPass, 0f);
            buffer.SetGlobalFloat(Ready, 1f);
            buffer.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);

            buffers.After.SetGlobalFloat(Ready, 0f);
            buffers.After.ReleaseTemporaryRT(Low);
        }

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
    }
}
