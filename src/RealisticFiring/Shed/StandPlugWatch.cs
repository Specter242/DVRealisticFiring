using DV.CabControls;
using UnityEngine;

namespace RealisticFiring.Shed
{
 internal sealed class StandPlugWatch : MonoBehaviour
    {
        // Carried this far from its stand's socket, it may be hung back.
        private const float Clear = 0.75f;

        private const float BuriedUnder = 0.3f;
        private const float LookEvery = 1f;
        private float lookAt;

        private PluggableObject plug;
        private PlugSocket stand;
        private Collider[] standSense = new Collider[0];
        private ControlImplBase control;
        private bool held;
        private bool kept;

        public static void Watch(PropHose hose)
        {
            var watch = hose.gameObject.AddComponent<StandPlugWatch>();
            watch.plug = hose.plug;
            // The coupling is small and falls fast enough to pass through a
            // shed's thin floor between physics steps: it is swept.
            foreach (var body in hose.plug.GetComponentsInChildren<Rigidbody>(true))
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            watch.stand = hose.stationSocket;
            watch.standSense = hose.stationSocket.GetComponents<Collider>();
            hose.plug.Unplugged += (p, s) => watch.OnUnplugged(s);
        }

        private void Update()
        {
            if (control == null && plug != null)
            {
                control = plug.GetComponent<ControlImplBase>();
                if (control != null)
                {
                    control.Grabbed += _ => held = true;
                    control.Ungrabbed += _ => { held = false; Restore(); };
                }
            }
            if (kept && plug != null && stand != null && Vector3.Distance(plug.transform.position, stand.transform.position) > Clear)
                Restore();
            if (Time.time >= lookAt)
            {
                lookAt = Time.time + LookEvery;
                RecoverIfBuried();
            }
        }

        private void RecoverIfBuried()
        {
            // In a session the host's plug decides, and one following
            // another machine's hand is where that player has it.
            if (plug == null || stand == null || held || plug.IsHeldInHand || plug.State != PluggableObject.PluggableState.Free
                || Net.IsClient || ShedNet.IsFollowing(plug))
                return;
            var at = plug.transform.position;
            var buried = false;
            foreach (var hit in Physics.RaycastAll(at + Vector3.up * BuriedUnder, Vector3.down, BuriedUnder, HoseGround.GroundLayers, QueryTriggerInteraction.Ignore))
            {
                if (HoseGround.IsGround(hit.collider, plug.transform) && hit.collider.GetComponentInParent<RailTrack>() == null)
                {
                    buried = true;
                    break;
                }
            }
            if (!buried)
                return;
            Restore();
            var back = ShedNet.Snap(plug, stand);
            Say(back ? "found under the floor, put back on its stand" : "found under the floor, but its stand would not take it back");
        }

        private void OnUnplugged(PlugSocket socket)
        {
            if (socket != stand || !plug.IsHeldInHand)
                return;
            foreach (var sense in standSense)
                sense.enabled = false;
            kept = true;
        }

        private void Restore()
        {
            if (!kept)
                return;
            kept = false;
            foreach (var sense in standSense)
            {
                if (sense != null)
                    sense.enabled = true;
            }
        }

        private void Say(string what)
        {
            if (Main.Settings.LogFireboxCreation && plug != null)
                Main.Mod.Logger.Log($"Shed hose plug: {what}");
        }
    }
}
