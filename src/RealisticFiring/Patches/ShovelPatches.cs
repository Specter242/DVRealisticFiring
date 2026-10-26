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
    /// loco with a realistic firebox (the S282's tender or the S060's bunker).
    /// Coal piles elsewhere keep the stock size.
    /// </summary>
    [HarmonyPatch(typeof(ShovelNonPhysicalCoal), "ShovelCapacityForPile")]
    internal static class ShovelCapacityPatch
    {
        private static readonly HashSet<string> Reported = new HashSet<string>();

        private static void Postfix(ShovelNonPhysicalCoal __instance, ICoalPile coalPile, ref float __result)
        {
            var shovelfulMass = Main.Settings.Shovelful;
            if (!(coalPile is Component pile) || pile == null)
                return;
            var car = TrainCar.Resolve(pile.gameObject);
            if (!FireboxRegistry.TryGetTended(car, out var firebox))
                return;

            var controller = firebox.Car.SimController != null ? firebox.Car.SimController.firebox : null;
            var multiplier = controller != null && controller.coalConsumptionMultiplier > 0f
                ? controller.coalConsumptionMultiplier
                : 1f;
            var stock = __result;
            __result = ShovelSize.PileCapacity(shovelfulMass, __instance.shovelChunksCapacity, multiplier);

            if (Main.Settings.LogCoalDeliveries && Reported.Add($"{__instance.name}|{car.carLivery.id}|{shovelfulMass}"))
            {
                Main.Mod.Logger.Log($"Shovel '{__instance.name}' ({__instance.shovelChunksCapacity} chunks of " +
                                    $"{coalPile.CoalChunkMass():F2} kg) at {car.carLivery.id}'s coal: stock {stock:F2} kg " +
                                    $"from the pile ({stock / multiplier:F2} kg into the firebox), now {__result:F2} kg " +
                                    $"({__result / multiplier:F2} kg)");
            }
        }
    }

    /// <summary>
    /// The keyboard and HUD "shovel coal" action moves whole tender chunks into
    /// the firebox with no shovel (12 kg each on the S282). On a realistic
    /// firebox each chunk becomes one standard shovelful instead, which follows
    /// the fire (where it needs coal most) because nothing aims it.
    /// </summary>
    [HarmonyPatch(typeof(CoalPileSimController), nameof(CoalPileSimController.TransferToFirebox))]
    internal static class MagicShovelPatch
    {
        private static bool Prefix(CoalPileSimController __instance, FireboxSimController fireboxController, byte chunkCount)
        {
            var shovelfulMass = Main.Settings.Shovelful;
            if (fireboxController == null
                || !FireboxRegistry.TryGet(TrainCar.Resolve(fireboxController.gameObject), out _))
                return true;

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
    /// keypad or the grate (<see cref="FireboxAim"/>). Clicking
    /// ends the hover, so the last sample is the aim: of the throw with a
    /// loaded shovel, or of the rake with an empty one.
    /// </summary>
    [HarmonyPatch(typeof(ShovelNonPhysicalCoal), nameof(ShovelNonPhysicalCoal.HandleHover))]
    internal static class ShovelHoverPatch
    {
        private const float MaxReach = 5f;

        private static void Postfix(ShovelNonPhysicalCoal __instance, ItemUseTarget target)
        {
            if (!Main.Settings.AimShovelThroughDoor || !ShovelTargets.TryGetFirebox(target, out var car, out var firebox))
                return;
            var handler = __instance.GetComponent<GrabHandlerItem>();
            var grabber = handler != null ? handler.GetGrabber() : null;
            if (grabber == null || grabber.Cursor == null)
                return;

            if (FireboxAim.TryAim(grabber.Cursor.GetRay(), target, car, firebox, out var across, out var depth, out var box))
                firebox.NoteAim(new DoorAimSample(DoorAim.ZoneFor(across, depth), across, depth, !__instance.IsLoaded, Time.time, car.transform, box));
        }

        // Places the nearest hit on the target's colliders within that
        // collider's extent in the car's frame. Facing the firebox means
        // facing the car's front (+Z), so -X is the fireman's left.
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

        /// <summary>As <see cref="TryAim(Ray, ItemUseTarget, TrainCar, RealisticFirebox, out float, out float)"/>, with the firehole's rectangle in the car's frame, or none when aiming at the grate.</summary>
        public static bool TryAim(Ray ray, ItemUseTarget target, TrainCar car, RealisticFirebox firebox, out float across, out float depth, out Bounds hole)
        {
            hole = default;
            var grate = Main.Settings.Aim == ShovelAim.Grate;
            if (grate && Visuals.ZonedCoalBed.TryGet(firebox, out var bed) && bed.TryAim(ray, out across, out depth))
                return true;
            if (!ShovelHoverPatch.TryMeasure(ray, target, car.transform, out across, out depth, out var box))
                return false;
            var opening = FireholeOpening.For(car, box);
            if (!grate)
                hole = opening.Bounds;
            if (opening.TryPlace(ray, car.transform, out var holeAcross, out var holeUp))
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

        /// <summary>An empty shovel can rake through the door once it is open as far as for shoveling.</summary>
        public static bool CanRake(ShovelNonPhysicalCoal shovel, RealisticFirebox firebox) =>
            !shovel.IsLoaded && firebox.DoorOpening >= ShovelNonPhysicalCoal.REQUIRED_DOOR_OPENED_PERCENTAGE_NON_VR;
    }

    /// <summary>
    /// Lets a click with an empty shovel on a realistic firebox's open door
    /// count as a use, so the game plays the shovel's thrust into the firebox;
    /// and says why a loaded one is refused at a full firebox.
    /// </summary>
    [HarmonyPatch(typeof(ShovelNonPhysicalCoal), nameof(ShovelNonPhysicalCoal.IsUseCompatible))]
    internal static class ShovelRakeCompatiblePatch
    {
        private static void Postfix(ShovelNonPhysicalCoal __instance, ItemUseTarget target, ref bool __result)
        {
            if (__result || !ShovelTargets.TryGetFirebox(target, out var car, out var firebox))
                return;
            if (ShovelTargets.CanRake(__instance, firebox))
            {
                __result = true;
                return;
            }
            // The game asks this as the player clicks (in VR, as the shovel
            // goes in), and refuses a shovel with no room at all.
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
    /// At the end of that thrust, rakes the zone the empty shovel was aimed
    /// at (the center if no aim was recorded).
    /// </summary>
    [HarmonyPatch(typeof(ShovelNonPhysicalCoal), nameof(ShovelNonPhysicalCoal.HandleUse))]
    internal static class ShovelRakePatch
    {
        private static bool Prefix(ShovelNonPhysicalCoal __instance, ItemUseTarget target, ref bool __result)
        {
            if (!ShovelTargets.TryGetFirebox(target, out var car, out var firebox))
                return true;
            if (!ShovelTargets.CanRake(__instance, firebox))
            {
                // A throw: the game puts in what fits and keeps the rest.
                FullFirebox.SayIfFull(__instance, car);
                return true;
            }

            var aim = firebox.HoverAim;
            firebox.Rake(aim.HasValue && aim.Value.Raking && Time.time - aim.Value.Time <= RealisticFirebox.AimHoldTime ? aim.Value.Zone : GrateZone.Center);
            __result = true;
            return false;
        }
    }
}
