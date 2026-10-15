namespace RealisticFiring.Core
{
 public sealed class GrateCharacter
    {
        /// <summary>A typical firebox: a short combustion chamber, a brick arch sealed to the tube plate, typical bituminous coal.</summary>
        public static readonly GrateCharacter Typical = new GrateCharacter();

        /// <summary>
        /// Air through the back row of the grate, under the door, before
        /// normalizing. Over a brick arch sealed to the tube plate the gases
        /// from the back take the shortest way to the tubes, so the back draws
        /// hardest: on a Frisco 2-8-2 the front pulled 70 % of the back (Acker,
        /// RME Apr. 1930), about 0.78 of its air.
        /// </summary>
        public float BackRowDraft { get; set; } = 1.12f;

        /// <summary>Air through the front row, under the arch by the tube plate, before normalizing: the gases' longest way round.</summary>
        public float FrontRowDraft { get; set; } = 0.88f;

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
