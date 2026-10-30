using UnityEngine;

namespace RealisticFiring.Visuals
{
    /// <summary>Reuse the active camera's frustum for nearby firebox surface work.</summary>
    internal static class FireboxVisibility
    {
        private static readonly Plane[] planes = new Plane[6];
        private static Camera lastCamera;
        private static int frame = -1;

        internal static bool InView(Camera camera, Renderer renderer, bool surfaceReady = true)
        {
            // Stereo keeps its existing conservative behavior until a
            // combined-eye frustum has been verified in a headset.
            // A newly created bed has collapsed bounds until its first
            // surface upload. It must get that upload before frustum culling.
            if (!surfaceReady || camera == null || renderer == null || camera.stereoEnabled)
                return true;
            if (frame != Time.frameCount || lastCamera != camera)
            {
                GeometryUtility.CalculateFrustumPlanes(camera, planes);
                frame = Time.frameCount;
                lastCamera = camera;
            }
            return GeometryUtility.TestPlanesAABB(planes, renderer.bounds);
        }
    }
}
