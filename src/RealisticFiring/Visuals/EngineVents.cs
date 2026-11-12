using System;
using RealisticStackEffects;
using RealisticStackEffects.Core;
using UnityEngine;

namespace RealisticFiring.Visuals
{
 internal static class EngineVents
    {
        /// <summary>Adds a source of a kind at a place facing a way, in the car's frame, by a strength each frame.</summary>
        public delegate bool Adder(string name, VentKind kind, Vector3 at, Vector3 facing, Func<float> strength);

        /// <summary>
        /// Realistic Stack Effects' steam around a car, set by the add-on
        /// when that mod's contract has it: what it is now (the same object
        /// while it is the same steam) and how to add to it; null for none.
        /// </summary>
        public static Func<TrainCar, (object Identity, Adder Add)?> External;

        /// <summary>
        /// The steam around <paramref name="car"/> now: its
        /// <paramref name="identity"/>, which changes when the engine's steam
        /// is made again (its sources are then to be added again), and how
        /// to add to it. False where neither mod draws it.
        /// </summary>
        public static bool TryGet(TrainCar car, out object identity, out Adder add)
        {
            identity = null;
            add = null;
            if (car == null)
                return false;
            if (ChimneyPlume.TryGet(car, out var plume) && plume.Vents != null)
            {
                var vents = plume.Vents;
                identity = vents;
                add = (name, kind, at, facing, strength) =>
                {
                    vents.Add(name, kind, vents.Place(at, facing), strength);
                    return true;
                };
                return true;
            }
            var external = External;
            if (external == null)
                return false;
            (object Identity, Adder Add)? found;
            try
            {
                found = external(car);
            }
            catch (Exception e)
            {
                // A contract that changed under the add-on: left alone from here on.
                External = null;
                Faults.Report("Realistic Stack Effects' steam around the engine", car, e);
                return false;
            }
            if (!(found is (object Identity, Adder Add) there) || there.Identity == null || there.Add == null)
                return false;
            identity = there.Identity;
            add = there.Add;
            return true;
        }
    }
}
