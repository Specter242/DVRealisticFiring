using System.Collections;
using System.Collections.Generic;
using DV.VRTK_Extensions;
using RealisticFiring.Sim;
using UnityEngine;
using VRTK;

namespace RealisticFiring.Items
{
    /// <summary>
    /// VR scrolling for two-handed items such as the shovel.
    ///
    /// Stock <see cref="ItemScrollingVR"/> assumes a single-handed item: a
    /// second hand grabbing a <see cref="VRTK.GrabAttachMechanics.VRTK_TwoHandedPoleGrab"/>
    /// fires <c>InteractableObjectGrabbed</c> a second time, doubling
    /// subscriptions and throwing a <see cref="System.NullReferenceException"/>
    /// when both hands ungrab. This component keeps one binding per grabbing
    /// hand (primary and secondary), sets <c>interactable.isScrollable = true</c>
    /// so Derail Valley's own locomotion and rotation suppress movement while
    /// Trigger + Thumbstick is held, accepts all four stick/touchpad directions
    /// (<see cref="ScrollAction.ScrollUp"/>/<see cref="ScrollAction.ScrollRight"/>
    /// larger, <see cref="ScrollAction.ScrollDown"/>/<see cref="ScrollAction.ScrollLeft"/>
    /// smaller), and remembers which hand scrolled for haptics and tooltips.
    /// </summary>
    internal sealed class ShovelScrollingVR : ItemScrolling
    {
        private const float ContinuousDelay = 0.4f;
        private const float ContinuousStandardInterval = 0.25f;
        private const float ContinuousFastInterval = 0.1f;
        private const int ContinuousFastThreshold = 5;

        private sealed class HandBinding
        {
            public GameObject Hand;
            public VRTK_InteractUse_DV Use;
            public TouchpadInputInterpreter Touchpad;
            public SDK_BaseController.ControllerHand ControllerHand;
            public TouchpadInputInterpreter.TouchpadPressedChangedDelegate PressedHandler;
        }

        private readonly List<HandBinding> hands = new List<HandBinding>(2);
        private VRTK_InteractableObject_DV interactable;
        private Coroutine continuousCoro;
        private GameObject activeScrollHand;
        private SDK_BaseController.ControllerHand activeScrollControllerHand = SDK_BaseController.ControllerHand.None;

        /// <summary>The VR controller that last triggered a scroll on this shovel.</summary>
        internal VRTK_ControllerReference LastScrollController { get; private set; }

        private void Start() => TryBind();

        private void Update()
        {
            if (!initialized)
                TryBind();
        }

        private void OnDisable() => StopContinuous();

        protected override void OnDestroy()
        {
            StopContinuous();
            base.OnDestroy();
            if (!UnloadWatcher.isUnloading && initialized && interactable != null)
                interactable.isScrollable = false;
        }

        private void TryBind()
        {
            interactable = GetComponent<VRTK_InteractableObject_DV>();
            if (interactable == null)
                return;
            interactable.isScrollable = true;
            initialized = true;
            SetupListeners(on: true);
            SyncGrabbingHands();
        }

        protected override void SetupListeners(bool on)
        {
            if (interactable == null)
                return;
            if (on)
            {
                interactable.InteractableObjectGrabbed += OnGrabStateChanged;
                interactable.InteractableObjectUngrabbed += OnGrabStateChanged;
                return;
            }
            interactable.InteractableObjectGrabbed -= OnGrabStateChanged;
            interactable.InteractableObjectUngrabbed -= OnGrabStateChanged;
            ClearHands();
        }

        private void OnGrabStateChanged(object sender, InteractableObjectEventArgs e) => SyncGrabbingHands();

        private void SyncGrabbingHands()
        {
            if (interactable == null)
            {
                StopContinuous();
                ClearHands();
                return;
            }
            var primary = interactable.GetGrabbingObject();
            var secondary = interactable.GetSecondaryGrabbingObject();

            for (var i = hands.Count - 1; i >= 0; i--)
            {
                var h = hands[i];
                if (h.Hand == null || (h.Hand != primary && h.Hand != secondary))
                {
                    if (ShouldStopContinuous(activeScrollHand, activeScrollControllerHand, h.Hand, h.ControllerHand))
                        StopContinuous();
                    Unsubscribe(h);
                    hands.RemoveAt(i);
                }
            }

            TryAddHand(primary);
            if (secondary != null && secondary != primary)
                TryAddHand(secondary);
        }

        private void TryAddHand(GameObject hand)
        {
            if (hand == null)
                return;
            for (var i = 0; i < hands.Count; i++)
                if (hands[i].Hand == hand)
                    return;

            var use = hand.GetComponent<VRTK_InteractUse_DV>();
            var touchpad = use != null ? use.GetComponent<TouchpadInputInterpreter>() : hand.GetComponent<TouchpadInputInterpreter>();
            if (use == null || touchpad == null)
                return;

            var ctrlRef = VRTK_ControllerReference.GetControllerReference(hand);
            var isWand = ctrlRef != null && ctrlRef.IsValid() && ctrlRef.IsWandOrUndefined();
            var controllerHand = ctrlRef != null ? ctrlRef.hand : SDK_BaseController.ControllerHand.None;
            var binding = new HandBinding
            {
                Hand = hand,
                Use = use,
                Touchpad = touchpad,
                ControllerHand = controllerHand
            };
            touchpad.DelayedDirectionalInputGiven += OnScrollInput;
            touchpad.DirectionalInputNeutral += OnScrollInputNeutral;
            if (isWand)
            {
                binding.PressedHandler = pressed => OnPressedChanged(binding, pressed);
                touchpad.PressedChanged += binding.PressedHandler;
            }
            hands.Add(binding);
        }

        private void ClearHands()
        {
            for (var i = 0; i < hands.Count; i++)
                Unsubscribe(hands[i]);
            hands.Clear();
        }

        private void Unsubscribe(HandBinding binding)
        {
            if (binding.Touchpad == null)
                return;
            binding.Touchpad.DelayedDirectionalInputGiven -= OnScrollInput;
            binding.Touchpad.DirectionalInputNeutral -= OnScrollInputNeutral;
            if (binding.PressedHandler != null)
                binding.Touchpad.PressedChanged -= binding.PressedHandler;
        }

        private void OnPressedChanged(HandBinding binding, bool pressed)
        {
            if (!pressed && binding != null
                && ShouldStopContinuous(activeScrollHand, activeScrollControllerHand, binding.Hand, binding.ControllerHand))
                StopContinuous();
        }

        private void OnScrollInputNeutral(TouchpadInputDirection direction, bool swiped, VRTK_ControllerReference ctrlRef)
        {
            if (ctrlRef == null)
                return;
            var binding = FindBinding(ctrlRef);
            var eventHand = binding != null
                ? binding.Hand
                : (ctrlRef.scriptAlias != null ? ctrlRef.scriptAlias : ctrlRef.actual);
            var eventControllerHand = ctrlRef.hand != SDK_BaseController.ControllerHand.None
                ? ctrlRef.hand
                : (binding != null ? binding.ControllerHand : SDK_BaseController.ControllerHand.None);
            if (ShouldStopContinuous(activeScrollHand, activeScrollControllerHand, eventHand, eventControllerHand))
                StopContinuous();
        }

        private void OnScrollInput(TouchpadInputDirection direction, bool swiped, VRTK_ControllerReference ctrlRef)
        {
            if (ctrlRef == null || AutoFireDoorRig.IsVrDoorHeld)
                return;
            var binding = FindBinding(ctrlRef);
            if (binding == null || binding.Use == null || !binding.Use.UseModified)
                return;
            var action = ToScrollAction(direction);
            if (action == ScrollAction.Release)
                return;

            StopContinuous();
            activeScrollHand = binding.Hand;
            activeScrollControllerHand = ctrlRef.hand != SDK_BaseController.ControllerHand.None
                ? ctrlRef.hand
                : binding.ControllerHand;
            LastScrollController = ctrlRef;
            AdjustForInversionAndFireScrollingEvent(action);
            continuousCoro = StartCoroutine(ScrollContinuously(action, binding.Use, ctrlRef));
        }

        internal static bool ShouldStopContinuous(
            object activeHand,
            SDK_BaseController.ControllerHand activeControllerHand,
            object eventHand,
            SDK_BaseController.ControllerHand eventControllerHand)
        {
            if (activeHand == null && activeControllerHand == SDK_BaseController.ControllerHand.None)
                return false;
            if (activeHand != null && eventHand != null && ReferenceEquals(activeHand, eventHand))
                return true;
            if (activeControllerHand != SDK_BaseController.ControllerHand.None
                && eventControllerHand != SDK_BaseController.ControllerHand.None)
                return eventControllerHand == activeControllerHand;
            return false;
        }

        private HandBinding FindBinding(VRTK_ControllerReference ctrlRef)
        {
            var scriptAlias = ctrlRef.scriptAlias;
            var actual = ctrlRef.actual;
            for (var i = 0; i < hands.Count; i++)
            {
                var hand = hands[i].Hand;
                if (hand == null)
                    continue;
                if (hand == scriptAlias || hand == actual)
                    return hands[i];
                var handRef = VRTK_ControllerReference.GetControllerReference(hand);
                if (handRef != null && handRef.hand == ctrlRef.hand && ctrlRef.hand != SDK_BaseController.ControllerHand.None)
                    return hands[i];
            }
            return null;
        }

        private IEnumerator ScrollContinuously(ScrollAction action, VRTK_InteractUse_DV use, VRTK_ControllerReference ctrlRef)
        {
            yield return WaitFor.Seconds(ContinuousDelay);
            var ticks = 0;
            while (use != null && use.UseModified && !AutoFireDoorRig.IsVrDoorHeld)
            {
                var delay = ticks++ > ContinuousFastThreshold ? ContinuousFastInterval : ContinuousStandardInterval;
                yield return WaitFor.Seconds(delay);
                if (use == null || !use.UseModified || AutoFireDoorRig.IsVrDoorHeld)
                    break;
                LastScrollController = ctrlRef;
                AdjustForInversionAndFireScrollingEvent(action);
            }
            continuousCoro = null;
            activeScrollHand = null;
            activeScrollControllerHand = SDK_BaseController.ControllerHand.None;
        }

        private void StopContinuous()
        {
            if (continuousCoro != null)
                StopCoroutine(continuousCoro);
            continuousCoro = null;
            activeScrollHand = null;
            activeScrollControllerHand = SDK_BaseController.ControllerHand.None;
        }

        internal static ScrollAction ToScrollAction(TouchpadInputDirection direction)
        {
            switch (direction)
            {
                case TouchpadInputDirection.Up: return ScrollAction.ScrollUp;
                case TouchpadInputDirection.Down: return ScrollAction.ScrollDown;
                case TouchpadInputDirection.Left: return ScrollAction.ScrollLeft;
                case TouchpadInputDirection.Right: return ScrollAction.ScrollRight;
                default: return ScrollAction.Release;
            }
        }
    }
}
