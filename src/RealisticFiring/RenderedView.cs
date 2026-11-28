using UnityEngine;

namespace RealisticFiring
{
 internal static class RenderedView
    {
        private struct Drawn
        {
            public int Frame;
            public Matrix4x4 WorldToCamera;
            public Matrix4x4 Projection;
        }

        private static bool listening;
        // The player's camera, as Camera.main and PlayerManager.ActiveCamera name it (one camera outside VR).
        private static readonly System.Collections.Generic.Dictionary<Camera, Drawn> Cameras = new System.Collections.Generic.Dictionary<Camera, Drawn>();

        /// <summary>
        /// <paramref name="camera"/>'s <see cref="Camera.WorldToScreenPoint(Vector3)"/>
        /// as it drew this frame: x and y in pixels from the bottom left, z the
        /// distance in front of it, m.
        /// </summary>
        public static Vector3 WorldToScreenPoint(Camera camera, Vector3 world)
        {
            Listen();
            if (!TryGet(camera, out var view))
                return camera != null ? camera.WorldToScreenPoint(world) : Vector3.zero;
            return Project(view.WorldToCamera, view.Projection, camera.pixelRect, world);
        }

        /// <summary>The camera's eye as it drew this frame, else its own position.</summary>
        public static Vector3 Eye(Camera camera)
        {
            Listen();
            if (!TryGet(camera, out var view))
                return camera != null ? camera.transform.position : Vector3.zero;
            return view.WorldToCamera.inverse.MultiplyPoint3x4(Vector3.zero);
        }

        /// <summary>
        /// A point through a camera's matrices to the screen, as Unity's
        /// WorldToScreenPoint does: view space looks down −z.
        /// </summary>
        internal static Vector3 Project(Matrix4x4 view, Matrix4x4 proj, Rect pixels, Vector3 world)
        {
            var v = view.MultiplyPoint3x4(world);
            var clip = proj * new Vector4(v.x, v.y, v.z, 1f);
            if (Mathf.Abs(clip.w) < 1e-6f)
                return new Vector3(0f, 0f, -v.z);
            var x = clip.x / clip.w;
            var y = clip.y / clip.w;
            return new Vector3(pixels.x + (x + 1f) * 0.5f * pixels.width, pixels.y + (y + 1f) * 0.5f * pixels.height, -v.z);
        }

        // This frame's matrices for the camera, if it drew this frame.
        private static bool TryGet(Camera camera, out Drawn view)
        {
            view = default;
            return camera != null && Cameras.TryGetValue(camera, out view) && view.Frame == Time.frameCount;
        }

        private static void Listen()
        {
            if (listening)
                return;
            listening = true;
            Camera.onPreRender += Drawing;
        }

        // After every pre-cull handler, so with any shake in place.
        private static void Drawing(Camera camera)
        {
            if (camera == null || (camera != PlayerManager.ActiveCamera && camera != Camera.main))
                return;
            if (Cameras.Count > 4)
                Cameras.Clear();
            Cameras[camera] = new Drawn
            {
                Frame = Time.frameCount,
                WorldToCamera = camera.worldToCameraMatrix,
                Projection = camera.projectionMatrix,
            };
        }
    }
}
