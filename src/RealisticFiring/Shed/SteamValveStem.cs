using UnityEngine;

namespace RealisticFiring.Shed
{
 internal sealed class SteamValveStem : MonoBehaviour
    {
        private const float RiserRadius = 0.071f;
        private const float RiserZ = -0.905f;
        private const float RiserFrom = 0.96f;
        private const float RiserTo = 2.42f;

        // Sizes, m: the boss on the riser, the bonnet's hex, the spindle.
        private const float BossRadius = 0.028f;
        private const float BossOut = 0.03f;
        private const float BonnetAcross = 0.036f;
        private const float BonnetLength = 0.022f;
        private const float SpindleRadius = 0.007f;

        // Built this long after the stand, the wheel drawn by then, s.
        private const float After = 1f;

        /// <summary>The wheel's control, its drawn parts under it.</summary>
        public Transform Wheel;

        /// <summary>The layer the stand's own mesh draws on.</summary>
        public int Layer;

        private float buildAt;

        private void Start() => buildAt = Time.time + After;

        private void Update()
        {
            if (Time.time < buildAt)
                return;
            enabled = false;
            Build();
        }

        private void Build()
        {
            if (Wheel == null)
                return;
            var renderers = Wheel.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return;
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
                bounds.Encapsulate(renderer.bounds);
            var stand = transform;
            var hub = stand.InverseTransformPoint(bounds.center);
            var height = Mathf.Clamp(hub.y, RiserFrom + BossRadius, RiserTo - BossRadius);
            var axis = new Vector3(0f, height, RiserZ);
            var out_ = new Vector3(hub.x, 0f, hub.z - RiserZ);
            var reach = out_.magnitude;
            if (Main.Settings.LogFireboxCreation)
                Main.Mod.Logger.Log(System.FormattableString.Invariant(
                    $"{name}: direct steam handwheel drawn at ({hub.x:F3}, {hub.y:F3}, {hub.z:F3}) m in the stand's frame, {reach - RiserRadius:F3} m off the riser"));
            if (reach < 1e-3f)
                return;
            var along = out_ / reach;
            var mesh = new FittingMesh();
            // Short of the hub by this much, the bonnet and boss clear the
            // wheel; where the gap is too short for both, the boss alone.
            const float Clear = 0.012f;
            var room = reach - Clear - RiserRadius;
            var bossTo = room > BossOut + BonnetLength ? RiserRadius + BossOut : RiserRadius + Mathf.Max(0f, room);
            if (bossTo > RiserRadius)
                mesh.Turned(Metal.Iron, new[]
                {
                    new Vector2(0f, RiserRadius - 0.02f), new Vector2(BossRadius, RiserRadius - 0.02f),
                    new Vector2(BossRadius, bossTo - 0.004f), new Vector2(BossRadius - 0.004f, bossTo), new Vector2(0f, bossTo),
                }, axis, along);
            if (room > BossOut + BonnetLength)
                mesh.Hex(Metal.Brass, BonnetAcross, reach - Clear - BonnetLength, reach - Clear, axis, along);
            mesh.Rod(Metal.Steel, SpindleRadius, RiserRadius - 0.01f, reach, axis, along);
            mesh.Build(stand, "RealisticFiring direct steam valve", Layer);
        }
    }
}
