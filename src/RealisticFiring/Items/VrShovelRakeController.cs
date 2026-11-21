using DV.CabControls;
using DV.VRTK_Extensions;
using HarmonyLib;
using RealisticFiring.Core;
using RealisticFiring.Patches;
using RealisticFiring.Sim;
using RealisticFiring.Visuals;
using UnityEngine;
using VRTK;

namespace RealisticFiring.Items
{
    /// <summary>
    /// Empty-shovel raking in VR: reaching or sweeping an empty shovel blade
    /// into an open firebox levels the targeted grate zone, highlighting the
    /// zone on the coal bed and giving audio and haptic feedback.
    ///
    /// Uses the same <see cref="TrainCar"/> / <see cref="NonPhysicsCoalTarget"/> /
    /// <see cref="Fire"/> target colliders and <see cref="VrShovelRake.DoorOpening"/>
    /// threshold as stock VR coal shoveling, with a post-unload lockout
    /// (<see cref="VrShovelRake"/>) so throwing coal never accidentally rakes.
    /// </summary>
    internal sealed class VrShovelRakeController : MonoBehaviour
    {
        // Slightly wider than stock VR shovel's 2.5 cm cast so raking feels reliable at the firehole.
        private const float TipRadius = 0.04f;
        private const float RakeSoundVolume = 0.35f;

        private readonly VrShovelRake state = new VrShovelRake();
        private readonly Collider[] overlaps = new Collider[8];

        private ShovelNonPhysicalCoal shovel;
        private ItemBase item;
        private Collider shovelTip;
        private ShovelAudio shovelAudio;
        private VRTK_InteractableObject_DV interactable;
        private TrainCar lastCar;
        private int interiorMask;

        private void Start()
        {
            shovel = GetComponent<ShovelNonPhysicalCoal>();
            item = GetComponent<ItemBase>();
            var stockShovel = GetComponent<Shovel>();
            shovelTip = stockShovel != null ? stockShovel.shovelTip : null;
            shovelAudio = GetComponent<ShovelAudio>();
            interactable = GetComponent<VRTK_InteractableObject_DV>();
            interiorMask = LayerMask.GetMask("Train_Interior");
        }

        private void OnDisable()
        {
            lastCar = null;
            state.Reset();
        }

        private void Update()
        {
            if (shovel == null || item == null)
                return;
            if (interactable == null)
                interactable = GetComponent<VRTK_InteractableObject_DV>();

            var grabbed = item.IsGrabbed();
            if (!grabbed)
                lastCar = null;
            var loaded = shovel.IsLoaded;
            var tipTransform = shovelTip != null ? shovelTip.transform : (shovel.InteractionPoint != null ? shovel.InteractionPoint : transform);
            var tipPos = tipTransform.position;

            TrainCar car = null;
            RealisticFirebox firebox = null;
            Collider hitCollider = null;
            var inOpenFirebox = grabbed && TryFindOpenFirebox(tipPos, out car, out firebox, out hitCollider);
            var zone = GrateZone.Center;
            var across = 0.5f;
            var along = 0.5f;
            var localTip = Vector3.zero;

            if (inOpenFirebox)
            {
                lastCar = car;
                var bladeDir = tipPos - transform.position;
                bladeDir = bladeDir.sqrMagnitude > 1e-4f ? bladeDir.normalized : transform.forward;
                zone = AimZone(car, firebox, hitCollider, tipPos, bladeDir, out across, out along);
                localTip = FiringFrame.Of(car).InverseTransformPoint(tipPos);
            }
            else if (lastCar != null)
            {
                var frame = FiringFrame.Of(lastCar);
                if (frame != null)
                    localTip = frame.InverseTransformPoint(tipPos);
            }

            var now = Time.time;
            var shouldRake = state.Step(grabbed, loaded, inOpenFirebox, zone, localTip.x, localTip.y, localTip.z, now);

            if (inOpenFirebox && Main.Settings != null && Main.Settings.AimShovelThroughDoor && !HudPolicy.Hidden
                && state.CanHighlight(grabbed, loaded, inOpenFirebox, now))
                firebox.NoteAim(new DoorAimSample(zone, across, along, DoorAimKind.Rake, now));

            if (!shouldRake || !inOpenFirebox)
                return;

            firebox.Rake(zone);
            if (shovelAudio != null && shovelAudio.coalDrop != null)
                shovelAudio.PlayDebounced(shovelAudio.coalDrop, tipPos, RakeSoundVolume);
            PulseHands();
        }

        private bool TryFindOpenFirebox(Vector3 tipPos, out TrainCar car, out RealisticFirebox firebox, out Collider hitCollider)
        {
            car = null;
            firebox = null;
            hitCollider = null;
            var count = Physics.OverlapSphereNonAlloc(tipPos, TipRadius, overlaps, interiorMask, QueryTriggerInteraction.Collide);
            for (var i = 0; i < count; i++)
            {
                var col = overlaps[i];
                if (col == null)
                    continue;
                if (col.GetComponent<NonPhysicsCoalTarget>() == null && col.GetComponent<Fire>() == null)
                    continue;
                var candidateCar = TrainCar.Resolve(col.gameObject);
                if (!FireboxRegistry.TryGet(candidateCar, out var candidateFirebox))
                    continue;
                if (candidateFirebox.DoorOpening < VrShovelRake.DoorOpening)
                    continue;
                car = candidateCar;
                firebox = candidateFirebox;
                hitCollider = col;
                return true;
            }
            return false;
        }

        // Picks the grate zone targeted by the shovel blade: where the blade
        // lies over the bed, else where the shaft points onto the bed, else
        // where the blade crosses the firehole opening.
        private static GrateZone AimZone(TrainCar car, RealisticFirebox firebox, Collider hitCollider, Vector3 tipPos, Vector3 bladeDir, out float across, out float along)
        {
            if (ZonedCoalBed.TryGet(firebox, out var bed) && bed.Ready)
            {
                var boxPoint = bed.BoxPoint(tipPos);
                if (boxPoint.x >= 0f && boxPoint.x <= 1f && boxPoint.y >= 0f && boxPoint.y <= 1f)
                {
                    across = boxPoint.x;
                    along = boxPoint.y;
                    return DoorAim.ZoneFor(across, along);
                }
                var ray = new Ray(tipPos - bladeDir * 0.25f, bladeDir);
                if (bed.TryAim(ray, out across, out along))
                    return DoorAim.ZoneFor(across, along);
            }

            if (hitCollider != null)
            {
                var firing = FiringFrame.Of(car);
                var bounds = CarSpace.LocalBounds(firing, hitCollider);
                var opening = FireholeOpening.For(car, bounds);
                var ray = new Ray(tipPos - bladeDir * 0.25f, bladeDir);
                if (opening.TryPlace(ray, firing, out across, out along))
                    return DoorAim.ZoneFor(across, along);

                var local = firing.InverseTransformPoint(tipPos);
                var hole = opening.Bounds;
                across = hole.size.x > 1e-4f ? Mathf.Clamp01((local.x - hole.min.x) / hole.size.x) : 0.5f;
                along = hole.size.y > 1e-4f ? Mathf.Clamp01((local.y - hole.min.y) / hole.size.y) : 0.5f;
                return DoorAim.ZoneFor(across, along);
            }

            across = 0.5f;
            along = 0.5f;
            return GrateZone.Center;
        }

        private void PulseHands()
        {
            if (interactable == null)
                return;
            PulseHand(interactable.GetGrabbingObject());
            var secondary = interactable.GetSecondaryGrabbingObject();
            if (secondary != null && secondary != interactable.GetGrabbingObject())
                PulseHand(secondary);
        }

        private static void PulseHand(GameObject hand)
        {
            if (hand == null)
                return;
            var ctrlRef = VRTK_ControllerReference.GetControllerReference(hand);
            if (ctrlRef != null && ctrlRef.IsValid())
                HapticUtils.DoHapticPulse(ctrlRef, HapticIntensityType.Normal);
        }
    }

    /// <summary>Gives each VR shovel physical empty-shovel raking when realistic fireboxes are enabled.</summary>
    [HarmonyPatch(typeof(ShovelNonPhysicalCoal), "Start")]
    internal static class VrShovelRakePatch
    {
        private static void Postfix(ShovelNonPhysicalCoal __instance)
        {
            if (!Main.ReplacementEnabled || !VRManager.IsVREnabled() || __instance == null
                || __instance.GetComponent<ItemBase>() == null || __instance.GetComponent<VrShovelRakeController>() != null)
                return;
            __instance.gameObject.AddComponent<VrShovelRakeController>();
        }
    }
}
