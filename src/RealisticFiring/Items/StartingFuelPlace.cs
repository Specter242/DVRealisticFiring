using DV.Interaction;
using DV.Player;
using DV.Utils;
using RealisticFiring.Core;
using RealisticFiring.Patches;
using UnityEngine;

namespace RealisticFiring.Items
{
    /// <summary>
    /// Laying a held piece of wood or rag through the open fire door, as the
    /// stock shovel lays its coal: aim at the firehole and use. The held item
    /// moves in to the door (the game's working animation), then goes onto
    /// the grate at the zone aimed at. The hover records the aim as the
    /// shovel's does, as a lay (<see cref="DoorAimKind"/>), so the label, the
    /// keypad, and the zone on the bed show it, and no coal follows it.
    /// Throwing stays for anyone who prefers it, and for VR.
    /// </summary>
    internal sealed class StartingFuelPlace : MonoBehaviour, IItemUseAnimated, IItemUse, IInteractionPointProvider
    {
        internal const string TipName = "[rf place point]";
        private StartingFuelItem fuel;
        private Transform tip;
        private GrabHandlerItem handle;

        // The game's animation asks after the use too, when the piece is already in the fire.
        public Transform InteractionPoint => this == null ? null : tip != null ? tip : transform;

        private void Awake()
        {
            fuel = GetComponent<StartingFuelItem>();
            tip = transform.Find(TipName);
        }

        private bool Firebox(ItemUseTarget target, out FuelFireboxIntake intake) =>
            FuelFireboxIntake.ForTarget(target, out intake);

        private DoorAimKind AimKind => fuel.Kind == StartingFuelKind.Rag ? DoorAimKind.LayRag : DoorAimKind.LayWood;

        // The game's grab handler, once it has made the item's.
        private GrabHandlerItem Handle => handle != null ? handle : handle = GetComponent<GrabHandlerItem>();

        public bool IsHoverCompatible(ItemUseTarget target) => fuel != null && Firebox(target, out _);

        public bool IsUseCompatible(ItemUseTarget target) =>
            fuel != null && !fuel.Consumed && Firebox(target, out var intake) && intake.DoorOpenFor(VRManager.IsVREnabled()) && intake.CanTake(fuel);

        public bool HandleHover(ItemUseTarget target)
        {
            if (fuel == null || !Firebox(target, out var intake)) return false;
            if (intake.TryAim(target, Handle, AimKind, out var firebox, out var aim)) firebox.NoteAim(aim);
            var text = !intake.DoorOpenFor(false) ? "Open the fire door wider to lay it in"
                : !intake.CanTake(fuel) ? intake.RefusalFor(fuel)
                : fuel.Kind == StartingFuelKind.Wood ? "Lay the wood on the grate" : fuel.Rag.Lit ? "Lay the burning rag in" : fuel.Rag.Oil > 0f ? "The rag isn't lit: light it first, or it does nothing" : "Put the rag in";
            SingletonBehaviour<InteractionTextControllerNonVr>.Instance?.DisplayText(text);
            return true;
        }

        public bool HandleUse(ItemUseTarget target)
        {
            if (!IsUseCompatible(target) || !Firebox(target, out var intake)) return false;
            var zone = intake.AimedZone(target, Handle, AimKind);
            return intake.Place(fuel, zone);
        }

        public (Vector3 pos, Quaternion rot) TargetPoint(ItemUseTarget target)
        {
            var pose = target != null ? target.transform.Find(InsertItemIntoTargetHandler.ANIMATION_POSE_TARGET) : null;
            if (pose == null)
                return target != null ? (target.transform.position, transform.rotation) : default;
            var anchor = SingletonBehaviour<ItemPositionController>.Instance?.itemAnchor;
            return (anchor != null ? Vector3.Lerp(pose.position, anchor.position, 0.35f) : pose.position, Quaternion.Slerp(pose.rotation, transform.rotation, 0.8f));
        }
    }
}
