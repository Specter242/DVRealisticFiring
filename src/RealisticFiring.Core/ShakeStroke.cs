namespace RealisticFiring.Core
{
    /// <summary>
    /// How far the grate is rocked: "wiggling" or "shaking – half stroke"
    /// (1944 NY firing course pp.88-89). Dumping, the full stroke held, is
    /// <see cref="FireModel.Dump"/>.
    /// </summary>
    public enum ShakeStroke
    {
        /// <summary>A short rock: loose ash only, almost no fire lost.</summary>
        Wiggle,

        /// <summary>A half stroke or more: most loose ash, a little clinker, some fire.</summary>
        Shake,
    }
}
