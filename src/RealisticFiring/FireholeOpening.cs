using System.Collections.Generic;
using DV.Interaction;
using UnityEngine;

namespace RealisticFiring
{
 internal readonly struct FireholeOpening
    {
        /// <summary>A firehole's usual size, and its foot above its target's, m: the S282's as fitted.</summary>
        private const float UsualWidth = 0.45f;
        private const float UsualHeight = 0.456f;
        private const float UsualFootAbove = 0.09f;

        // Firehole geometry per livery lives in Sim.LocomotiveProfiles (in the
        // firing frame: the car's, turned half about on an engine fired from
        // the front).
        private static bool TryProfileHole(string livery, out (float Middle, float Foot, float Width, float Height, float Plane) hole)
        {
            if (Sim.LocomotiveProfiles.TryGet(livery, out var profile) && profile.Firehole is Sim.FireholeSeat seat)
            {
                hole = seat.AsTuple();
                return true;
            }
            hole = default;
            return false;
        }

        // Holes set this session on the seating bench (Visuals.SeatingBench, a
        // development tool), by livery: they come first and are never saved.
        // And the hole last worked out for a cab not fitted, kept only while
        // the bench is open, for it to start from.
#if !PUBLIC_RELEASE
        internal static readonly Dictionary<string, (float Middle, float Foot, float Width, float Height, float Plane)> DevHoles =
            new Dictionary<string, (float, float, float, float, float)>();
        private static readonly Dictionary<string, (float Middle, float Foot, float Width, float Height, float Plane)> LastUnfitted =
            new Dictionary<string, (float, float, float, float, float)>();
#endif
        private static readonly Dictionary<string, (float Middle, float Foot, float Width, float Height, float Plane)> NativeHoles =
            new Dictionary<string, (float, float, float, float, float)>();

        /// <summary>
        /// <paramref name="livery"/>'s hole: the bench's, the profile's, or the
        /// one last worked out while aiming with the bench open; false before
        /// one is known.
        /// </summary>
        internal static bool TryHole(string livery, out (float Middle, float Foot, float Width, float Height, float Plane) hole) =>
#if PUBLIC_RELEASE
            TryProfileHole(livery, out hole) || Sim.MakerFitRegistry.TryGetFirehole(livery, out hole) || NativeHoles.TryGetValue(livery, out hole);
#else
            DevHoles.TryGetValue(livery, out hole) || TryProfileHole(livery, out hole) || Sim.MakerFitRegistry.TryGetFirehole(livery, out hole) || NativeHoles.TryGetValue(livery, out hole) || LastUnfitted.TryGetValue(livery, out hole);
#endif

        // The bench's hole, then the profile's.
        private static bool TryFitted(string livery, out (float Middle, float Foot, float Width, float Height, float Plane) hole) =>
#if PUBLIC_RELEASE
            TryProfileHole(livery, out hole) || Sim.MakerFitRegistry.TryGetFirehole(livery, out hole) || NativeHoles.TryGetValue(livery, out hole);
#else
            DevHoles.Count > 0 && DevHoles.TryGetValue(livery, out hole) || TryProfileHole(livery, out hole) || Sim.MakerFitRegistry.TryGetFirehole(livery, out hole) || NativeHoles.TryGetValue(livery, out hole);
#endif

        // The QJ's shovel target reaches into the box. Its two closed door
        // leaves in the prefab are a better starting rectangle, independent
        // of the live door's opening angle. The bench can refine this fit.
        private static bool SeedNative(TrainCar car)
        {
            if (car == null || car.carLivery == null || car.carLivery.id != Visuals.QjStokerCab.Livery)
                return false;
            if (TryFitted(car.carLivery.id, out _)) return true;
            var prefab = car.carLivery.interiorPrefab != null ? car.carLivery.interiorPrefab.transform : null;
            var door = prefab != null ? prefab.Find("fireboxdoor ") : null;
            if (door == null) return false;
            Bounds? bounds = null;
            foreach (var renderer in door.GetComponentsInChildren<MeshRenderer>(true))
            {
                var part = CarSpace.LocalBounds(prefab, renderer);
                if (bounds.HasValue) { var all = bounds.Value; all.Encapsulate(part); bounds = all; }
                else bounds = part;
            }
            if (!bounds.HasValue || bounds.Value.size.x < 0.1f || bounds.Value.size.y < 0.1f) return false;
            var b = bounds.Value;
            NativeHoles[car.carLivery.id] = (b.center.x, b.min.y, b.size.x, b.size.y, b.min.z);
            return true;
        }

 internal static bool SeedForBench(TrainCar car)
        {
            if (car == null || car.carLivery == null) return false;
            if (TryHole(car.carLivery.id, out _) || SeedNative(car)) return true;
            if (!Sim.FireboxRegistry.TryGet(car, out var firebox) || !Visuals.ZonedCoalBed.TryGet(firebox, out var bed)) return false;
            var target = bed.GetComponent<ItemUseTarget>();
            if (target == null || target.targetColliders == null) return false;
            Bounds? bounds = null;
            foreach (var collider in target.targetColliders)
            {
                if (collider == null) continue;
                var part = CarSpace.LocalBounds(FiringFrame.Of(car), collider);
                if (bounds.HasValue) { var all = bounds.Value; all.Encapsulate(part); bounds = all; }
                else bounds = part;
            }
            if (!bounds.HasValue) return false;
            For(car, bounds.Value);
            return TryHole(car.carLivery.id, out _);
        }

        private FireholeOpening(Vector3 min, Vector3 max)
        {
            Min = min;
            Max = max;
        }

        /// <summary>The hole's lower left and upper right, in the firing frame, in the door's plane.</summary>
        public Vector3 Min { get; }
        public Vector3 Max { get; }

        public Bounds Bounds
        {
            get
            {
                var bounds = new Bounds();
                bounds.SetMinMax(Min, Max);
                return bounds;
            }
        }

        /// <summary>
        /// Where <paramref name="car"/>'s backhead is, from its fitted firehole:
        /// the door's plane along the car and the hole's foot, m in the car's
        /// frame. False for a cab not fitted.
        /// </summary>
        public static bool Backhead(TrainCar car, out float plane, out float foot)
        {
            plane = foot = 0f;
            var livery = car != null && car.carLivery != null ? car.carLivery.id : null;
            if (livery == null || !TryFitted(livery, out var fitted))
                return false;
            plane = fitted.Plane;
            foot = fitted.Foot;
            return true;
        }

        /// <summary>The hole of <paramref name="car"/>'s cab, where <paramref name="target"/> is its shovel target, moved and sized by the calibration settings.</summary>
        public static FireholeOpening For(TrainCar car, Bounds target)
        {
            SeedNative(car);
            var livery = car != null && car.carLivery != null ? car.carLivery.id : null;
            if (livery != null && TryFitted(livery, out var fitted))
                return new FireholeOpening(new Vector3(fitted.Middle - fitted.Width / 2f, fitted.Foot, fitted.Plane),
                    new Vector3(fitted.Middle + fitted.Width / 2f, fitted.Foot + fitted.Height, fitted.Plane));
            // Not yet fitted: the S282's hole at the foot and middle of the target.
            var middle = target.center.x;
            var foot = Mathf.Min(target.min.y + UsualFootAbove, target.max.y);
            var width = Mathf.Min(UsualWidth, target.size.x);
            var height = UsualHeight;
            var face = target.max.z;
#if !PUBLIC_RELEASE
            if (livery != null && Visuals.SeatingBench.Open)
                LastUnfitted[livery] = (middle, foot, width, height, face);
#endif
            return new FireholeOpening(new Vector3(middle - width / 2f, foot, face), new Vector3(middle + width / 2f, foot + height, face));
        }

        /// <summary>
        /// Where <paramref name="ray"/> crosses the hole's plane, across (0
        /// left, 1 right) and up (0 its foot, 1 its top), held to the hole; false
        /// when it runs along or away from the plane.
        /// </summary>
        public bool TryPlace(Ray ray, Transform car, out float across, out float up)
        {
            across = up = 0f;
            var origin = car.InverseTransformPoint(ray.origin);
            var direction = car.InverseTransformDirection(ray.direction);
            if (direction.z <= 1e-4f)
                return false;
            var t = (Min.z - origin.z) / direction.z;
            if (t < 0f)
                return false;
            var point = origin + direction * t;
            across = Mathf.Clamp01((point.x - Min.x) / Mathf.Max(1e-4f, Max.x - Min.x));
            up = Mathf.Clamp01((point.y - Min.y) / Mathf.Max(1e-4f, Max.y - Min.y));
            return true;
        }

        public override string ToString() =>
            $"middle across {(Min.x + Max.x) / 2f:F3} m, foot {Min.y:F3} m, {Max.x - Min.x:F3} by {Max.y - Min.y:F3} m, in the plane z {Min.z:F3} m";
    }
}
