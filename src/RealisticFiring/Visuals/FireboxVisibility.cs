using UnityEngine;

namespace RealisticFiring.Visuals
{
    /// <summary>Reuse the active camera's frustum for nearby firebox surface work.</summary>
    internal static class FireboxVisibility
    {
        private static readonly Plane[] planes = new Plane[6];
        private static Camera lastCamera;
        private static int frame = -1;

        // The cab round the firehole, m in the firing frame: its half width beside
        // the hole's middle, how far its floor lies under the hole's foot, and the
        // passage through the door a head may lean into.
        private const float CabHalfWidth = 1.8f;
        private const float FloorUnderFoot = 1.2f;
        private const float Passage = 0.3f;

 internal static bool FromCab(TrainCar car, Camera camera)
        {
#if !PUBLIC_RELEASE
            if (FireboxCapture.KeepsVisible)
                return true;
#endif
            if (car == null || camera == null || car.carLivery == null
                || !FireholeOpening.TryHole(car.carLivery.id, out var hole))
                return true;
            var frame = FiringFrame.Of(car);
            if (frame == null)
                return true;
            var at = frame.InverseTransformPoint(camera.transform.position);
            if (at.z < hole.Plane)
                return Mathf.Abs(at.x - hole.Middle) < CabHalfWidth && at.y > hole.Foot - FloorUnderFoot;
            return at.z < hole.Plane + Passage && Mathf.Abs(at.x - hole.Middle) < hole.Width
                   && at.y > hole.Foot - Passage && at.y < hole.Foot + hole.Height + Passage;
        }

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
