using UnityEngine;

namespace RealisticStackEffects
{
    /// <summary>The mod's developer tools in game: the plume test bed's key and window.</summary>
    internal sealed class StackController : MonoBehaviour
    {
        private void Update()
        {
            var settings = Main.Settings;
            if (settings != null && settings.PlumeTestBedEnabled && settings.DevPlumeTestBedKey.Down())
                PlumeTestBed.Toggle();
        }

        private void OnGUI()
        {
            if (!PlumeCapture.Running)
                PlumeTestBed.Draw();
        }
    }
}
