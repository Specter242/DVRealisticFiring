namespace RealisticFiring.Core
{
    /// <summary>A way to work the fire across the grate, as seen from the door.</summary>
    public enum GrateDirection
    {
        /// <summary>Toward the tube plate.</summary>
        Front,

        /// <summary>Toward the door; from the back row, out through it.</summary>
        Back,

        Left,

        Right,
    }
}
