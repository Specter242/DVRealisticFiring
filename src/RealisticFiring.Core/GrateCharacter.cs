namespace RealisticFiring.Core
{
 public sealed class GrateCharacter
    {
        /// <summary>A typical firebox: a short combustion chamber, a brick arch, typical bituminous coal.</summary>
        public static readonly GrateCharacter Typical = new GrateCharacter();

        /// <summary>Draft at the back row of the grate, under the door, before normalizing.</summary>
        public float BackRowDraft { get; set; } = 0.9f;

        /// <summary>Draft at the front row, near the tube plate and the arch's throat, before normalizing.</summary>
        public float FrontRowDraft { get; set; } = 1.12f;

        /// <summary>Draft along the sides, where air slips up beside the firebox walls, against the center column.</summary>
        public float SideDraft { get; set; } = 1.07f;

        /// <summary>How well the air over the fire burns its gas off, against a typical firebox.</summary>
        public float OverFireMixing { get; set; } = 1f;

        /// <summary>How readily the coal's ash fuses into clinker, against typical coal.</summary>
        public float ClinkerFactor { get; set; } = 1f;

        /// <summary>How much ash the coal leaves, against typical coal.</summary>
        public float AshFactor { get; set; } = 1f;
    }
}
