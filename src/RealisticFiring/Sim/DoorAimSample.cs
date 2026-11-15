using RealisticFiring.Core;
using UnityEngine;

namespace RealisticFiring.Sim
{
    /// <summary>
    /// Where the player pointed into a firebox: the zone, the point's place
    /// across (0 left, 1 right) and up the door's coal target (0 bottom, 1
    /// top, a higher throw going deeper), what was aimed (a shovelful, a
    /// rake, wood or a rag laid; <see cref="DoorAimRules"/> says what each
    /// steers), when, in Unity time, and the target's box in the car's
    /// frame, for drawing its keypad.
    /// </summary>
    internal readonly struct DoorAimSample
    {
        /// <summary>Seconds a sample counts as the hand still pointing into the door; hovering samples every frame.</summary>
        public const float LiveAge = 0.25f;

        public DoorAimSample(GrateZone zone, float across, float depth, DoorAimKind kind, float time, Transform car = null, Bounds box = default)
        {
            Zone = zone;
            Across = across;
            Depth = depth;
            Kind = kind;
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

        /// <summary>What was aimed: a shovelful, a rake (an empty shovel or the fire iron), or wood or a rag laid by hand.</summary>
        public DoorAimKind Kind { get; }

        public float Time { get; }

        public bool IsLiveAt(float now) => now - Time <= LiveAge;

        /// <summary>How long ago it was taken, s, at <paramref name="now"/>.</summary>
        public float AgeAt(float now) => now - Time;

        public override string ToString() => $"{Zone} (across {Across:F2}, deep {Depth:F2})";
    }
}
