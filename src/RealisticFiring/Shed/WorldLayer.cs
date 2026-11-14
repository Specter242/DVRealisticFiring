using UnityEngine;

namespace RealisticFiring.Shed
{
 internal sealed class WorldLayer : MonoBehaviour
    {
        private Renderer[] renderers;

        private void LateUpdate()
        {
            var start = Diagnostics.PerfMeter.Start();
            try { TimedLateUpdate(); }
            finally { Diagnostics.PerfMeter.Stop(Diagnostics.PerfSection.ShedHoses, start); }
        }

        private void TimedLateUpdate()
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
