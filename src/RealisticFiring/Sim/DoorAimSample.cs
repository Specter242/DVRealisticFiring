using RealisticFiring.Core;
using UnityEngine;

namespace RealisticFiring.Sim
{
    /// <summary>
    /// Where the player pointed a shovel into a firebox: the zone, the
    /// point's place across (0 left, 1 right) and up the door's coal target
    /// (0 bottom, 1 top, a higher throw going deeper), whether the shovel was
    /// empty (a rake rather than a throw), when, in Unity time, and the
    /// target's box in the car's frame, for drawing its keypad.
    /// </summary>
    internal readonly struct DoorAimSample
    {
        /// <summary>Seconds a sample counts as the shovel still pointing into the door; hovering samples every frame.</summary>
        public const float LiveAge = 0.25f;

        public DoorAimSample(GrateZone zone, float across, float depth, bool raking, float time, Transform car = null, Bounds box = default)
        {
            Zone = zone;
            Across = across;
            Depth = depth;
            Raking = raking;
            Time = time;
            Car = car;
            Box = box;
        }

        /// <summary>The car whose frame <see cref="Box"/> is in, or null.</summary>
        public Transform Car { get; }

        /// <summary>The door's coal target, in the car's frame.</summary>
        public Bounds Box { get; }

        public GrateZone Zone { get; }

        public float Across { get; }

        public float Depth { get; }

        /// <summary>True when the shovel was empty, so a click rakes the zone.</summary>
        public bool Raking { get; }

        public float Time { get; }

        public bool IsLiveAt(float now) => now - Time <= LiveAge;

        public override string ToString() => $"{Zone} (across {Across:F2}, deep {Depth:F2})";
    }
}
