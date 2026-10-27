using UnityEngine;
using UnityEngine.XR;

namespace RealisticStackEffects
{
    /// <summary>
    /// The mod's shaders in VR (a player, 2026-10-11: the exhaust, the sand,
    /// and the empty grate doubled in the right eye and followed the head).
    /// The game draws both eyes in one pass (single-pass stereo); a shader
    /// without its stereo variant draws both with the left eye's view, and
    /// Unity strips that variant from a bundle built without VR. The mod's
    /// shaders carry it on their own keyword instead, switched on here for
    /// each camera that renders single-pass stereo and off for any other.
    /// Realistic Firing and Realistic Stack Effects each install it; both
    /// switch the one global keyword the same way for the same camera.
    /// </summary>
    internal static class StereoKeyword
    {
        public const string Keyword = "RF_SINGLE_PASS_STEREO";

        private static bool installed;
        private static bool on;

        public static void Install()
        {
            if (installed)
                return;
            installed = true;
            Camera.onPreRender += Set;
        }

        private static void Set(Camera camera)
        {
            var stereo = camera != null && camera.stereoEnabled && XRSettings.enabled
                         && XRSettings.stereoRenderingMode == XRSettings.StereoRenderingMode.SinglePass;
            if (stereo == on)
                return;
            on = stereo;
            if (stereo)
                Shader.EnableKeyword(Keyword);
            else
                Shader.DisableKeyword(Keyword);
        }
    }
}
