using UnityEngine;

namespace RealisticFiring.Visuals
{
    /// <summary>
    /// The frame the mod places its own parts in the cab by: the game's
    /// interior holder (<see cref="TrainCar.interior"/>), which the cab's
    /// interior is built under.
    /// <list type="bullet">
    /// <item>The holder is not the car's child. The game copies the car's
    /// pose to it once a frame, in <c>LateUpdate</c>
    /// (<c>TrainCarInteriorPhysics.SyncPosition</c>).</item>
    /// <item>The mod builds its parts when the cab loads, from
    /// <c>Update</c>. By then the car has moved on for this frame while the
    /// holder hasn't.</item>
    /// <item>A part placed by the car's own pose there landed a frame's
    /// travel off the cab and stayed off until the cab next loaded: 12 to
    /// 37 cm at 40 to 60 km/h. The cab reloads whenever the player steps
    /// off the engine onto the tender and back.</item>
    /// <item>AdensWorld (Nexus, 2026-11-14, in VR) saw the S282's injector
    /// come off its spot, and an invisible wall stand in front of the
    /// firehole.</item>
    /// </list>
    /// Placed by the holder's frame, a part is where the car's frame puts it
    /// whenever the two agree (every frame after the sync) and moves with
    /// the cab.
    /// </summary>
    internal static class CabFrame
    {
        /// <summary>
        /// The frame for <paramref name="car"/>'s cab parts, in the car's own
        /// coordinates: the interior holder, or the car itself where it has
        /// none.
        /// </summary>
        internal static Transform Of(TrainCar car) =>
            car == null ? null : car.interior != null ? car.interior : car.transform;
    }
}
