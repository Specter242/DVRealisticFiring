using DV.Interaction;
using DV.Player;
using DV.Utils;
using RealisticFiring.Core;
using RealisticFiring.Patches;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Items
{
    /// <summary>
    /// Working the fire with the fire iron through the open firebox door,
    /// aimed at a zone as the shovel is: a click runs the slice bar under
    /// that zone (breaking clinker loose, stirring ash into the fire); the
    /// mouse wheel works it as a hoe, forward pushing the zone's fire and
    /// loose clinker toward the front, back pulling it toward the door (and
    /// from the back row out through it), with Shift sideways. The working
    /// end heats in the fire and glows, cooling over half a minute.
    /// </summary>
    internal sealed class FireIronUse : MonoBehaviour, IItemUseAnimated, IInventoryItemLocalizer
    {
        public const string TipName = "[iron tip]";

        // An aim taken while hovering is good for this long, in s.
        private const float AimHoldTime = 0.5f;
        private const float CoolingTime = 25f;

        private static readonly int HeatId = Shader.PropertyToID("_Heat");

        private Transform tip;
        private GrabHandlerItem grab;
        private ItemScrolling scrolling;
        private Renderer model;
        private MaterialPropertyBlock properties;
        private RealisticFirebox aimedFirebox;
        private GrateZone aimedZone;
        private float aimTime = float.NegativeInfinity;
        private float heat;

        public Transform InteractionPoint => tip != null ? tip : transform;

        private void Awake()
        {
            tip = transform.Find(TipName);
            var spec = GetComponent<InventoryItemSpec>();
            if (spec != null)
                spec.NameOverride = FireIron.DisplayName;
            var modelHolder = transform.Find("model");
            model = modelHolder != null ? modelHolder.GetComponent<Renderer>() : null;
            properties = new MaterialPropertyBlock();
        }

        private void Start()
        {
            grab = GetComponent<GrabHandlerItem>();
            scrolling = GetComponent<ItemScrolling>();
            if (scrolling != null)
                scrolling.Scrolled += OnScrolled;
        }

        private void OnDestroy()
        {
            if (scrolling != null)
                scrolling.Scrolled -= OnScrolled;
        }

        private void Update()
        {
            if (heat <= 0f || model == null)
                return;
            heat = Mathf.Max(0f, heat - heat * Time.deltaTime / CoolingTime - 0.002f * Time.deltaTime);
            model.GetPropertyBlock(properties);
            properties.SetFloat(HeatId, heat);
            model.SetPropertyBlock(properties);
        }

        public bool IsHoverCompatible(ItemUseTarget target) => ShovelTargets.TryGetFirebox(target, out _, out _);

        public bool IsUseCompatible(ItemUseTarget target) =>
            ShovelTargets.TryGetFirebox(target, out _, out var firebox) && DoorOpen(firebox);

        public bool HandleHover(ItemUseTarget target)
        {
            if (!ShovelTargets.TryGetFirebox(target, out var car, out var firebox))
                return false;
            var text = SingletonBehaviour<InteractionTextControllerNonVr>.Instance;
            if (!DoorOpen(firebox))
            {
                if (text != null)
                    text.DisplayText("Open the firebox door to work the fire");
                return true;
            }
            var grabber = grab != null ? grab.GetGrabber() : null;
            if (grabber != null && grabber.Cursor != null
                && ShovelHoverPatch.TryMeasure(grabber.Cursor.GetRay(), target, car.transform, out var across, out var height))
            {
                aimedFirebox = firebox;
                aimedZone = DoorAim.ZoneFor(across, height);
                aimTime = Time.time;
                // Highlights the zone on the bed as the shovel's aim does; as a
                // rake's, so a shovelful thrown next ignores it.
                firebox.NoteAim(new DoorAimSample(aimedZone, across, height, raking: true, Time.time));
            }
            if (text != null && AimIsLive(firebox))
                text.DisplayText($"Fire iron at {GrateLayout.Describe(aimedZone)}: click to slice; wheel to push forward or pull back (Shift: sideways)");
            return true;
        }

        public bool HandleUse(ItemUseTarget target)
        {
            if (!ShovelTargets.TryGetFirebox(target, out _, out var firebox) || !DoorOpen(firebox))
                return false;
            var zone = AimIsLive(firebox) ? aimedZone : GrateZone.Center;
            firebox.Slice(zone);
            Heat(firebox);
            return true;
        }

        public (Vector3 pos, Quaternion rot) TargetPoint(ItemUseTarget target)
        {
            // The blade goes to the door, the iron lying along the player's aim.
            var pose = target.transform.Find("[animation_pose_target]");
            var anchor = SingletonBehaviour<ItemPositionController>.Instance;
            if (pose != null && anchor != null && anchor.itemAnchor != null)
                return (pose.position, Quaternion.Slerp(pose.rotation, InteractionPoint.rotation, 0.95f));
            return (target.transform.position, target.transform.rotation);
        }

        private void OnScrolled(ScrollAction action)
        {
            if (aimedFirebox == null || !AimIsLive(aimedFirebox) || !DoorOpen(aimedFirebox))
                return;
            var up = action == ScrollAction.ScrollUp;
            if (!up && action != ScrollAction.ScrollDown)
                return;
            var sideways = UnityModManagerNet.KeyBinding.Shift();
            var direction = sideways
                ? (up ? GrateDirection.Right : GrateDirection.Left)
                : (up ? GrateDirection.Front : GrateDirection.Back);
            aimedFirebox.Push(aimedZone, direction);
            Heat(aimedFirebox);
        }

        private bool AimIsLive(RealisticFirebox firebox) =>
            aimedFirebox == firebox && Time.time - aimTime <= AimHoldTime;

        private static bool DoorOpen(RealisticFirebox firebox) =>
            firebox.DoorOpening >= ShovelNonPhysicalCoal.REQUIRED_DOOR_OPENED_PERCENTAGE_NON_VR;

        // In the fire the working end heats toward the fire's own glow.
        private void Heat(RealisticFirebox firebox)
        {
            var fire = Mathf.Clamp01((firebox.Model.Temperature - 450f) / 750f);
            heat = Mathf.Max(heat, Mathf.Min(fire, heat + 0.35f * fire));
        }

        public string GetNameParam() => FireIron.DisplayName;

        public string GetCustomDescription() =>
            "A fireman's slice bar. Through the open firebox door, point it at part of the fire: " +
            "click to run it under the fire there and break clinker loose for the grate or the drop grate to take; " +
            "mouse wheel forward to push the fire toward the front, back to pull it toward the door " +
            "(from the back row, loose clinker comes out through the door); hold Shift to push it sideways. " +
            "Work the fire no more than it needs: stirring brings ash up into the fire, where it makes clinker.";
    }
}
