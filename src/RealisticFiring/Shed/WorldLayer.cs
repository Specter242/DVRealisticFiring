using UnityEngine;

namespace RealisticFiring.Shed
{
 internal sealed class WorldLayer : MonoBehaviour
    {
        private Renderer[] renderers;

        private void LateUpdate()
        {
            if (renderers == null || renderers.Length == 0)
                renderers = GetComponentsInChildren<Renderer>(true);
            foreach (var shown in renderers)
            {
                if (shown != null && shown.gameObject.layer != 0)
                    shown.gameObject.layer = 0;
            }
        }
    }
}
