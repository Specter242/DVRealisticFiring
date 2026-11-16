using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace RealisticFiring
{
 internal static class FiringFrame
    {
        // Engines fired from the front: BorING's cab-forward, its door at the
        // forward end of its box, the fireman facing the rear.
        private static readonly HashSet<string> FromTheFront = new HashSet<string> { "S440NCF-400A" };

        // Half a turn about the vertical, written out: Quaternion.Euler is the
        // engine's native call, which the adapter check can't make.
        private static readonly Quaternion HalfTurn = new Quaternion(0f, 1f, 0f, 0f);

        private static readonly ConditionalWeakTable<TrainCar, Holder> Frames = new ConditionalWeakTable<TrainCar, Holder>();

        private sealed class Holder
        {
            public Transform Frame;
        }

        /// <summary>Whether <paramref name="livery"/> is fired from the front, its door facing the car's front.</summary>
        internal static bool Reversed(string livery) => livery != null && FromTheFront.Contains(livery);

        /// <summary>Whether <paramref name="car"/> is fired from the front.</summary>
        internal static bool Reversed(TrainCar car) => car != null && car.carLivery != null && Reversed(car.carLivery.id);

        /// <summary>The turn from the car's frame to <paramref name="car"/>'s firing frame.</summary>
        internal static Quaternion Turn(TrainCar car) => Reversed(car) ? HalfTurn : Quaternion.identity;

        /// <summary>
        /// <paramref name="car"/>'s firing frame: its own transform, or an
        /// empty child of it turned half about.
        /// </summary>
        internal static Transform Of(TrainCar car)
        {
            if (car == null)
                return null;
            if (!Reversed(car))
                return car.transform;
            var holder = Frames.GetValue(car, _ => new Holder());
            if (holder.Frame == null)
            {
                var frame = new GameObject("RealisticFiring firing frame").transform;
                frame.SetParent(car.transform, false);
                frame.localPosition = Vector3.zero;
                frame.localRotation = HalfTurn;
                holder.Frame = frame;
            }
            return holder.Frame;
        }

        /// <summary>A point in the car's frame, in the firing frame of an engine fired as <paramref name="reversed"/> says.</summary>
        internal static Vector3 FromCar(bool reversed, Vector3 point) => reversed ? new Vector3(-point.x, point.y, -point.z) : point;

        /// <summary>A box in the car's frame, in the firing frame of an engine fired as <paramref name="reversed"/> says.</summary>
        internal static Bounds FromCar(bool reversed, Bounds box)
        {
            if (!reversed)
                return box;
            var turned = new Bounds();
            turned.SetMinMax(new Vector3(-box.max.x, box.min.y, -box.max.z), new Vector3(-box.min.x, box.max.y, -box.min.z));
            return turned;
        }

        /// <summary>A box in <paramref name="car"/>'s frame, in its firing frame.</summary>
        internal static Bounds FromCar(TrainCar car, Bounds box) => FromCar(Reversed(car), box);
    }
}
