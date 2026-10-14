using RealisticFiring.Core;

namespace RealisticFiring.Sim
{
    /// <summary>
    /// Where the player pointed a shovel into a firebox: the zone, the
    /// point's place across (0 left, 1 right) and deep (0 the door, 1 the
    /// front) on the drawn bed (or up the door's coal target, 0 bottom to 1
    /// top, where there is no bed), whether the shovel was empty (a rake
    /// rather than a throw), and when, in Unity time.
    /// </summary>
    internal readonly struct DoorAimSample
    {
        /// <summary>Seconds a sample counts as the shovel still pointing into the door; hovering samples every frame.</summary>
        public const float LiveAge = 0.25f;

        public DoorAimSample(GrateZone zone, float across, float depth, bool raking, float time)
        {
            Zone = zone;
            Across = across;
            Depth = depth;
            Raking = raking;
            Time = time;
        }

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
