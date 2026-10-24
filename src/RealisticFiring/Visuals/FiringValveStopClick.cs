using DV.CabControls;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Visuals
{
 internal sealed class FiringValveStopClick : MonoBehaviour
    {
        // A grab let go within this, in s, having moved the lever less than this share of its travel, is a click.
        private const float ClickTime = 0.35f;
        private const float ClickTravel = 0.02f;

        // A lever this far under its stop is put back (under half of a 2.5 % notch), at most this often, in s.
        private const float UnderStop = 0.01f;
        private const float PushEvery = 0.5f;

        private ControlImplBase control;
        private RealisticOilFirebox firebox;
        private bool held;
        private float grabbedAt = float.NegativeInfinity;
        private float valueAtGrab;
        private float nextPush;

        /// <summary>Watches <paramref name="lever"/>, the firing valve of <paramref name="oil"/>, for a click.</summary>
        public static void Attach(ControlImplBase lever, RealisticOilFirebox oil)
        {
            if (lever == null || oil == null)
                return;
            var click = lever.GetComponent<FiringValveStopClick>();
            if (click == null)
                click = lever.gameObject.AddComponent<FiringValveStopClick>();
            click.firebox = oil;
        }

        private void OnEnable()
        {
            control = GetComponent<ControlImplBase>();
            if (control == null)
                return;
            control.Grabbed += OnGrabbed;
            control.Ungrabbed += OnUngrabbed;
        }

        private void OnDisable()
        {
            held = false;
            if (control == null)
                return;
            control.Grabbed -= OnGrabbed;
            control.Ungrabbed -= OnUngrabbed;
        }

        private void OnGrabbed(ControlImplBase lever)
        {
            held = true;
            grabbedAt = Time.time;
            valueAtGrab = lever.Value;
        }

        private void OnUngrabbed(ControlImplBase lever)
        {
            held = false;
            var click = Time.time - grabbedAt <= ClickTime && Mathf.Abs(lever.Value - valueAtGrab) < ClickTravel;
            grabbedAt = float.NegativeInfinity;
            if (!click || firebox == null)
                return;
            var stop = firebox.Stop;
            var wasIn = stop.In;
            if (stop.Toggle(lever.Value))
                FireController.Announce($"Firing valve stop set at {stop.At:P0}: the valve won't close past it. Click the valve again to take it out");
            else
                FireController.Announce(wasIn
                    ? "Firing valve stop out: the valve closes all the way"
                    : "Open the firing valve to where the stop should go, then click it");
        }

        private void Update()
        {
            if (held || control == null || firebox == null || !firebox.Stop.In || Time.time < nextPush)
                return;
            var stop = firebox.Stop.At;
            var port = firebox.Feed != null ? firebox.Feed.ControlPort : null;
            if (port == null || !(control.Value < stop - UnderStop))
                return;
            nextPush = Time.time + PushEvery;
            port.ExternalValueUpdate(stop);
        }
    }
}
