using DV.CabControls;
using RealisticFiring.Sim;
using UnityEngine;
using VRTK;

namespace RealisticFiring.Visuals
{
 internal sealed class IndependentBailClick : MonoBehaviour
    {
        // A grab let go within this, in s, having moved the lever less than this share of its travel, is a click.
        private const float ClickTime = 0.35f;
        private const float ClickTravel = 0.02f;

        private ControlImplBase control;
        private VRTK_InteractableObject vrLever;
        private float grabbedAt = float.NegativeInfinity;
        private float valueAtGrab;

        public TrainCar Car { get; set; }

        /// <summary>Watches <paramref name="lever"/>, the independent brake of <paramref name="car"/>, for a click.</summary>
        public static void Attach(ControlImplBase lever, TrainCar car)
        {
            if (lever == null || car == null)
                return;
            var click = lever.GetComponent<IndependentBailClick>();
            if (click == null)
                click = lever.gameObject.AddComponent<IndependentBailClick>();
            click.Car = car;
        }

        private void OnEnable()
        {
            control = GetComponent<ControlImplBase>();
            if (control != null)
            {
                control.Grabbed += OnGrabbed;
                control.Ungrabbed += OnUngrabbed;
            }
            // The VR lever's interactable, added in its Awake; none on a desktop lever.
            vrLever = GetComponent<VRTK_InteractableObject>();
            if (vrLever != null)
            {
                vrLever.isUsable = true;
                vrLever.useOnlyIfGrabbed = true;
                vrLever.InteractableObjectUsed += OnUsed;
            }
        }

        private void OnDisable()
        {
            if (control != null)
            {
                control.Grabbed -= OnGrabbed;
                control.Ungrabbed -= OnUngrabbed;
            }
            if (vrLever != null)
                vrLever.InteractableObjectUsed -= OnUsed;
        }

        private void OnGrabbed(ControlImplBase lever)
        {
            grabbedAt = Time.time;
            valueAtGrab = lever.Value;
        }

        private void OnUngrabbed(ControlImplBase lever)
        {
            var click = Time.time - grabbedAt <= ClickTime && Mathf.Abs(lever.Value - valueAtGrab) < ClickTravel;
            grabbedAt = float.NegativeInfinity;
            if (click && Main.Settings.ClickToBailOff)
                BailOffs.Bail(Car);
        }

        // The use button pressed and let go while this lever is held, in VR.
        private void OnUsed(object sender, InteractableObjectEventArgs e)
        {
            if (Main.Settings.ClickToBailOff)
                BailOffs.Bail(Car);
        }
    }
}
