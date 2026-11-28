using System.Collections.Generic;
using DV.Interaction;
using DV.Simulation.Cars;
using DV.Simulation.Controllers;
using HarmonyLib;
using RealisticFiring.Core;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Patches
{
    /// <summary>
    /// Sizes the shovel as a real shovelful when it loads from the coal of a
    /// loco with a realistic firebox (the S282's tender or the S060's bunker):
    /// the player's own, as the mouse wheel picked it (<see cref="Items.ShovelfulWheel"/>).
    /// Coal piles elsewhere keep the stock size.
    /// </summary>
    [HarmonyPatch(typeof(ShovelNonPhysicalCoal), "ShovelCapacityForPile")]
    internal static class ShovelCapacityPatch
    {
        private static readonly HashSet<string> Reported = new HashSet<string>();

        private static void Postfix(ShovelNonPhysicalCoal __instance, ICoalPile coalPile, ref float __result)
        {
            if (!(coalPile is Component pile) || pile == null)
                return;
            var car = TrainCar.Resolve(pile.gameObject);
            if (!FireboxRegistry.TryGetTended(car, out var firebox))
                return;
            // Bigger during the accelerated startup, fixed as it is scooped: the pile gives its weight times the coal
            // multiplier as for any shovelful, so a career pays the same for each kilogram.
            var scoop = Main.Settings.Scoop;
            var shovelfulMass = firebox.StartupScoop(scoop);
            StartupLoad.Note(__instance, shovelfulMass, scoop > 0f ? shovelfulMass / scoop : 1f);

            var controller = firebox.Car.SimController != null ? firebox.Car.SimController.firebox : null;
            var multiplier = controller != null && controller.coalConsumptionMultiplier > 0f
                ? controller.coalConsumptionMultiplier
                : 1f;
            var stock = __result;
            __result = ShovelSize.PileCapacity(shovelfulMass, __instance.shovelChunksCapacity, multiplier);

#if !PUBLIC_RELEASE
            if (Main.Settings.LogCoalDeliveries && Reported.Add($"{__instance.name}|{car.carLivery.id}|{shovelfulMass}"))
            {
                Main.Mod.Logger.Log($"Shovel '{__instance.name}' ({__instance.shovelChunksCapacity} chunks of " +
                                    $"{coalPile.CoalChunkMass():F2} kg) at {car.carLivery.id}'s coal: stock {stock:F2} kg " +
                                    $"from the pile ({stock / multiplier:F2} kg into the firebox), now {__result:F2} kg " +
                                    $"({__result / multiplier:F2} kg)");
            }
#endif
        }
    }

    /// <summary>
    /// What each shovel took at its last scoop (kg into the firebox, and the
    /// accelerated startup's boost on it), for the aim label while it is loaded.
    /// </summary>
    internal static class StartupLoad
    {
        private sealed class Load
        {
            public float Kg, Boost;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ShovelNonPhysicalCoal, Load> Loads =
            new System.Runtime.CompilerServices.ConditionalWeakTable<ShovelNonPhysicalCoal, Load>();

        /// <summary>The note the aim label adds while a loaded, boosted shovel aims into a firebox ("12 kg (startup ×4)"), or null.</summary>
        internal static string Hover;
        internal static float HoverAt = float.NegativeInfinity;

        internal static void Note(ShovelNonPhysicalCoal shovel, float kg, float boost)
        {
            var load = Loads.GetOrCreateValue(shovel);
            load.Kg = kg;
            load.Boost = boost;
        }

        internal static bool TryGet(ShovelNonPhysicalCoal shovel, out float kg, out float boost)
        {
            kg = 0f;
            boost = 1f;
            if (shovel == null || !Loads.TryGetValue(shovel, out var load))
                return false;
            kg = load.Kg;
            boost = load.Boost;
            return true;
        }
    }

    /// <summary>
    /// The keyboard and HUD "shovel coal" action moves whole tender chunks into
    /// the firebox with no shovel (12 kg each on the S282). On a realistic
    /// firebox each chunk becomes one of the player's shovelfuls instead, which follows
    /// the fire (where it needs coal most) because nothing aims it.
    /// </summary>
    [HarmonyPatch(typeof(CoalPileSimController), nameof(CoalPileSimController.TransferToFirebox))]
    internal static class MagicShovelPatch
    {
        private static bool Prefix(CoalPileSimController __instance, FireboxSimController fireboxController, byte chunkCount)
        {
            if (fireboxController == null
                || !FireboxRegistry.TryGet(TrainCar.Resolve(fireboxController.gameObject), out var firebox))
                return true;
            var shovelfulMass = firebox.StartupScoop(Main.Settings.Scoop);

            var multiplier = fireboxController.coalConsumptionMultiplier > 0f ? fireboxController.coalConsumptionMultiplier : 1f;
            var mass = chunkCount * ShovelSize.PileCapacity(shovelfulMass, ShovelSize.StandardShovelChunks, multiplier);
            // Like the stock transfer: nothing moves unless it all fits.
            if (mass > 0f && fireboxController.SpaceForCoal() >= mass)
                fireboxController.TransferCoal(__instance.TryRemoveCoal(mass));
            return false;
        }
    }

    /// <summary>
    /// Records where the player points a shovel into a realistic firebox,
    /// along the same cursor ray the game uses to pick the target, by the
    /// keypad or the grate (<see cref="FireboxAim"/>), for the label, the
    /// keypad, the bed and the overlay. A throw or rake takes its aim at its
    /// click (<see cref="ShovelUseAim"/>); this last sample is only its
    /// fallback.
    /// </summary>
    [HarmonyPatch(typeof(ShovelNonPhysicalCoal), nameof(ShovelNonPhysicalCoal.HandleHover))]
    internal static class ShovelHoverPatch
    {
        private const float MaxReach = 5f;

        private static void Postfix(ShovelNonPhysicalCoal __instance, ItemUseTarget target)
        {
            if (!Main.Settings.AimShovelThroughDoor || !ShovelTargets.TryGetFirebox(target, out var car, out var firebox))
                return;
            if (ShovelUseAim.TryMeasure(__instance, target, car, firebox, out var aim))
            {
                firebox.NoteAim(aim);
                // A boosted load says so beside the aim (FireController's aim label).
                StartupLoad.Hover = __instance.IsLoaded && StartupLoad.TryGet(__instance, out var kg, out var boost) && boost > 1.01f
                    ? $" {Items.ShovelfulWheel.Kg(kg)}{ShovelSize.BoostNote(boost)}" : null;
                StartupLoad.HoverAt = Time.time;
            }
        }

        // Places the nearest hit on the target's colliders within that
        // collider's extent in the firing frame (FiringFrame). Facing the
        // firebox means facing its +Z, so -X is the fireman's left.
        internal static bool TryMeasure(Ray ray, ItemUseTarget target, Transform car, out float across, out float height) =>
            TryMeasure(ray, target, car, out across, out height, out _);

        internal static bool TryMeasure(Ray ray, ItemUseTarget target, Transform car, out float across, out float height, out Bounds box)
        {
            across = height = 0f;
            box = default;
            Collider nearest = null;
            var nearestHit = default(RaycastHit);
            foreach (var collider in target.targetColliders)
            {
                if (collider != null && collider.Raycast(ray, out var hit, MaxReach)
                    && (nearest == null || hit.distance < nearestHit.distance))
                {
                    nearest = collider;
                    nearestHit = hit;
                }
            }
            if (nearest == null)
                return false;

            var bounds = CarSpace.LocalBounds(car, nearest);
            box = bounds;
            var point = car.InverseTransformPoint(nearestHit.point);
            across = Fraction(bounds.min.x, bounds.max.x, point.x);
            height = Fraction(bounds.min.y, bounds.max.y, point.y);
            return true;
        }

        private static float Fraction(float min, float max, float value) =>
            max - min > 1e-4f ? (value - min) / (max - min) : 0.5f;
    }

 internal static class FireboxAim
    {
        public static bool TryAim(Ray ray, ItemUseTarget target, TrainCar car, RealisticFirebox firebox, out float across, out float depth) =>
            TryAim(ray, target, car, firebox, out across, out depth, out _);

        /// <summary>As <see cref="TryAim(Ray, ItemUseTarget, TrainCar, RealisticFirebox, out float, out float)"/>, with the firehole's rectangle in the firing frame, or none when aiming at the grate.</summary>
        public static bool TryAim(Ray ray, ItemUseTarget target, TrainCar car, RealisticFirebox firebox, out float across, out float depth, out Bounds hole)
        {
            hole = default;
            var grate = Main.Settings.Aim == ShovelAim.Grate;
            if (grate && Visuals.ZonedCoalBed.TryGet(firebox, out var bed) && bed.TryAim(ray, out across, out depth))
                return true;
            var firing = FiringFrame.Of(car);
            if (!ShovelHoverPatch.TryMeasure(ray, target, firing, out across, out depth, out var box))
                return false;
            var opening = FireholeOpening.For(car, box);
            if (!grate)
                hole = opening.Bounds;
            if (opening.TryPlace(ray, firing, out var holeAcross, out var holeUp))
            {
                across = holeAcross;
                depth = holeUp;
            }
            return true;
        }
    }

    /// <summary>Finds the realistic firebox behind a shovel's use target.</summary>
    internal static class ShovelTargets
    {
        public static bool TryGetFirebox(ItemUseTarget target, out TrainCar car, out RealisticFirebox firebox)
        {
            car = null;
            firebox = null;
            if (target == null || (target.GetComponent<NonPhysicsCoalTarget>() == null && target.GetComponent<Fire>() == null))
                return false;
            car = TrainCar.Resolve(target.gameObject);
            return FireboxRegistry.TryGet(car, out firebox);
        }

        /// <summary>An empty desktop shovel can rake through the door once it is open as far as for shoveling; VR rakes via <see cref="Items.VrShovelRakeController"/>.</summary>
        public static bool CanRake(ShovelNonPhysicalCoal shovel, RealisticFirebox firebox) =>
            !VRManager.IsVREnabled() && !shovel.IsLoaded && firebox.DoorOpening >= ShovelNonPhysicalCoal.REQUIRED_DOOR_OPENED_PERCENTAGE_NON_VR;
    }

    /// <summary>
    /// The aim of a desktop shovel's throw or rake, taken as the player
    /// clicks: the game asks <c>IsUseCompatible</c> on the click's frame
    /// (<c>InsertItemIntoTargetHandler</c>), plays the thrust, and only at its
    /// end calls <c>HandleUse</c>, which puts the coal in. Firing fast, the
    /// look has turned back to the coal by then (#52, a player, 2026-11-22:
    /// "sometimes it bunches up at the side, sometimes at the front or
    /// back"), and the last hover was where it crossed the door's edge on the
    /// way out, or an empty shovel's. So the click's aim is held
    /// (<see cref="ClickedAim{T}"/>) and given to the firebox just before the
    /// coal goes in, which a multiplayer guest's shovelful sends to the host.
    /// Without a click's aim (a use another mod makes), the cursor at the end
    /// of the thrust, then the last hover, as before.
    /// </summary>
    internal static class ShovelUseAim
    {
        private static readonly ClickedAim<DoorAimSample> Clicked = new ClickedAim<DoorAimSample>();

        /// <summary>What a shovel aims in its state: a loaded one throws, an empty one rakes.</summary>
        public static DoorAimKind KindOf(ShovelNonPhysicalCoal shovel) => shovel.IsLoaded ? DoorAimKind.Shovel : DoorAimKind.Rake;

        /// <summary>Where the desktop cursor points <paramref name="shovel"/> into <paramref name="firebox"/>'s door now, if on its target.</summary>
        public static bool TryMeasure(ShovelNonPhysicalCoal shovel, ItemUseTarget target, TrainCar car, RealisticFirebox firebox, out DoorAimSample aim)
        {
            aim = default;
            var handler = shovel.GetComponent<GrabHandlerItem>();
            var grabber = handler != null ? handler.GetGrabber() : null;
            if (grabber == null || grabber.Cursor == null
                || !FireboxAim.TryAim(grabber.Cursor.GetRay(), target, car, firebox, out var across, out var depth, out var box))
                return false;
            aim = new DoorAimSample(DoorAim.ZoneFor(across, depth), across, depth, KindOf(shovel), Time.time, FiringFrame.Of(car), box);
            return true;
        }

        /// <summary>At the click that starts a throw or rake: holds where it points.</summary>
        public static void Click(ShovelNonPhysicalCoal shovel, ItemUseTarget target, TrainCar car, RealisticFirebox firebox)
        {
            if (!Main.Settings.AimShovelThroughDoor || VRManager.IsVREnabled())
                return;
            if (TryMeasure(shovel, target, car, firebox, out var aim))
            {
                Clicked.Click(shovel, target, aim, aim.Kind, aim.Time);
                firebox.NoteAim(aim);
            }
            else
                Clicked.Clear();
        }

        /// <summary>
        /// At the end of the thrust: the aim of the use <paramref name="shovel"/>
        /// makes on <paramref name="target"/>, of its kind, from its click, else
        /// the cursor now. Null leaves the last hover to decide.
        /// </summary>
        public static DoorAimSample? ForUse(ShovelNonPhysicalCoal shovel, ItemUseTarget target, TrainCar car, RealisticFirebox firebox)
        {
            if (!Main.Settings.AimShovelThroughDoor)
                return null;
            if (Clicked.TryTake(shovel, target, KindOf(shovel), Time.time, RealisticFirebox.AimHoldTime, out var clicked))
                return clicked;
            return TryMeasure(shovel, target, car, firebox, out var now) ? now : (DoorAimSample?)null;
        }
    }

    /// <summary>
    /// Lets a click with an empty shovel on a realistic firebox's open door
    /// count as a use, so the game plays the shovel's thrust into the firebox;
    /// holds the aim of each click that starts a throw or rake
    /// (<see cref="ShovelUseAim"/>); and says why a loaded one is refused at a
    /// full firebox.
    /// </summary>
    [HarmonyPatch(typeof(ShovelNonPhysicalCoal), nameof(ShovelNonPhysicalCoal.IsUseCompatible))]
    internal static class ShovelRakeCompatiblePatch
    {
        private static void Postfix(ShovelNonPhysicalCoal __instance, ItemUseTarget target, ref bool __result)
        {
            if (!ShovelTargets.TryGetFirebox(target, out var car, out var firebox))
                return;
            if (!__result && ShovelTargets.CanRake(__instance, firebox))
                __result = true;
            // The game asks this as the player clicks (in VR, as the shovel
            // goes in): the use starts its thrust now, or is refused.
            if (__result)
            {
                ShovelUseAim.Click(__instance, target, car, firebox);
                return;
            }
            // A shovel with no room at all is refused.
            FullFirebox.SayIfFull(__instance, car);
        }
    }

 internal static class FullFirebox
    {
        // The shovel's load, in the pile's kg; null should a game update rename it.
        private static readonly System.Reflection.FieldInfo LoadedField = AccessTools.Field(typeof(ShovelNonPhysicalCoal), "_coalMassLoaded");

        // Said at most this often, in s.
        private const float SayAgain = 1.5f;

        // The door open enough for a shovel in VR; with the mouse it wants 60 %.
        private const float OpenEnough = 0.25f;

        // Room this much short of the shovelful, in the pile's kg, still takes it.
        private const float Slack = 0.01f;

        private static float lastSaid = float.NegativeInfinity;

        public static void SayIfFull(ShovelNonPhysicalCoal shovel, TrainCar car)
        {
            var controller = car != null && car.SimController != null ? car.SimController.firebox : null;
            if (!shovel.IsLoaded || controller == null || LoadedField == null || controller.FireboxDoorOpening < OpenEnough
                || controller.SpaceForCoal() >= (float)LoadedField.GetValue(shovel) - Slack || Time.unscaledTime - lastSaid < SayAgain)
                return;
            lastSaid = Time.unscaledTime;
            FireController.Announce($"The firebox is full ({controller.FireboxContents:F0} of {controller.FireboxCapacity:F0} kg): let the fire burn down before firing more");
        }
    }

    /// <summary>
    /// At the end of that thrust: a throw lands where its click aimed, and an
    /// empty shovel rakes the zone its click aimed at (<see cref="ShovelUseAim"/>;
    /// without one, the last hover's rake aim, else the center: wood's or a
    /// shovelful's aim is not a rake's).
    /// </summary>
    [HarmonyPatch(typeof(ShovelNonPhysicalCoal), nameof(ShovelNonPhysicalCoal.HandleUse))]
    internal static class ShovelRakePatch
    {
        private static bool Prefix(ShovelNonPhysicalCoal __instance, ItemUseTarget target, ref bool __result)
        {
            if (!ShovelTargets.TryGetFirebox(target, out var car, out var firebox))
                return true;
            var aim = ShovelUseAim.ForUse(__instance, target, car, firebox);
            if (!ShovelTargets.CanRake(__instance, firebox))
            {
                // A throw: the game puts in what fits, at once, and keeps the
                // rest; the firebox takes this aim for it (TakeFreshAim; a
                // multiplayer guest's DeliverFromClient sends it to the host).
                if (aim.HasValue && aim.Value.Kind == DoorAimKind.Shovel)
                    firebox.NoteAim(aim.Value);
                FullFirebox.SayIfFull(__instance, car);
                return true;
            }

            if (!aim.HasValue)
            {
                var hover = firebox.HoverAim;
                if (hover.HasValue && DoorAimRules.AimsRake(hover.Value.Kind, hover.Value.AgeAt(Time.time), RealisticFirebox.AimHoldTime))
                    aim = hover;
            }
            firebox.Rake(aim.HasValue && aim.Value.Kind == DoorAimKind.Rake ? aim.Value.Zone : GrateZone.Center);
            __result = true;
            return false;
        }
    }
}
