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

        // Each cab's hole: its middle across, its foot, width, height, and its
        // plane's place along the car, m, in the car's frame.
        private static readonly Dictionary<string, (float Middle, float Foot, float Width, float Height, float Plane)> Holes =
            new Dictionary<string, (float, float, float, float, float)>
            {
                ["LocoS282A"] = (0.010f, 1.720f, 0.450f, 0.456f, 0.054f),
                ["LocoS060"] = (0.009f, 1.752f, 0.450f, 0.456f, -2.002f),
                ["Jk3A"] = (0.000f, 1.448f, 0.450f, 0.365f, -2.351f),
                ["FEF-3"] = (0.000f, 2.332f, 0.585f, 0.365f, -8.305f),
                ["S484-840A"] = (0.000f, 2.422f, 0.585f, 0.365f, -8.274f),
                ["S464-730"] = (0.000f, 2.344f, 0.630f, 0.365f, 0.109f),
                // The 8F: fitted on the black livery; the grey is the same cab.
                ["em_lms_8f_loc_black"] = (0.000f, 2.018f, 0.540f, 0.410f, -4.379f),
                ["em_lms_8f_loc_grey"] = (0.000f, 2.018f, 0.540f, 0.410f, -4.379f),
            };

        // Holes set this session on the seating bench (Visuals.SeatingBench, a
        // development tool), by livery, as Holes: they come first and are
        // never saved. And the hole last worked out for a cab not fitted,
        // kept only while the bench is open, for it to start from.
        internal static readonly Dictionary<string, (float Middle, float Foot, float Width, float Height, float Plane)> DevHoles =
            new Dictionary<string, (float, float, float, float, float)>();
        private static readonly Dictionary<string, (float Middle, float Foot, float Width, float Height, float Plane)> LastUnfitted =
            new Dictionary<string, (float, float, float, float, float)>();

        /// <summary>
        /// <paramref name="livery"/>'s hole: the bench's, the table's, or the
        /// one last worked out while aiming with the bench open; false before
        /// one is known.
        /// </summary>
        internal static bool TryHole(string livery, out (float Middle, float Foot, float Width, float Height, float Plane) hole) =>
            DevHoles.TryGetValue(livery, out hole) || Holes.TryGetValue(livery, out hole) || LastUnfitted.TryGetValue(livery, out hole);

        // The bench's hole, then the table's.
        private static bool TryFitted(string livery, out (float Middle, float Foot, float Width, float Height, float Plane) hole) =>
            DevHoles.Count > 0 && DevHoles.TryGetValue(livery, out hole) || Holes.TryGetValue(livery, out hole);

        private FireholeOpening(Vector3 min, Vector3 max)
        {
            Min = min;
            Max = max;
        }

        /// <summary>The hole's lower left and upper right, in the car's frame, in the door's plane.</summary>
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
            if (livery != null && Visuals.SeatingBench.Open)
                LastUnfitted[livery] = (middle, foot, width, height, face);
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
