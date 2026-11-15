using System;

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

        /// <summary>
        /// The share of the boiler's efficiency kept fired lightly, against
        /// the same box with its brick arch: 1 for a box with its arch, or one
        /// the arch changes nothing in.
        /// </summary>
        public float EfficiencyKeptLight { get; set; } = 1f;

        /// <summary>The share of the boiler's efficiency kept at the most the fire burns, against the same box with its arch.</summary>
        public float EfficiencyKeptForced { get; set; } = 1f;

        /// <summary>How readily the coal's ash fuses into clinker, against typical coal.</summary>
        public float ClinkerFactor { get; set; } = 1f;

        /// <summary>How much ash the coal leaves, against typical coal.</summary>
        public float AshFactor { get; set; } = 1f;

        /// <summary>
        /// The coal an arch saves fired lightly, as a share of the efficiency
        /// (PRR Bulletin 30, Brick Arch Tests, 1916, pp.28-29: "for ordinary
        /// rates of working ... an economy in coal of from 6 to 8 per cent";
        /// the Chicago smoke report, 1915, §701.48: about 7 per cent).
        /// </summary>
        public const float ArchEconomyLight = 0.07f;

        /// <summary>
        /// The share at the most the fire burns: Bulletin 30's "maximum
        /// evaporation ... was increased 15.5 per cent, by the use of the
        /// arch", so the box without it makes 1 / 1.155 of its most steam
        /// (Bulletin 6, 1912: an economy of 12 to 13.5 per cent).
        /// </summary>
        public const float ArchEconomyForced = 1f - 1f / 1.155f;

        /// <summary>
        /// How much worse the gas burns over the fire without the arch, as a
        /// share of the box's mixing: sized so a quarter of the box fired at
        /// once at half load smokes about half as much again without it, and a
        /// lighter round, or one on a harder fire, that the arched box burns
        /// clean smokes (Bulletin 30, p.28: lower smoke density with the arch;
        /// the Chicago report, 1915: a third less over its tests; Bulletin 6,
        /// 1912: black smoke 46 against 18 per cent; the boiler makers'
        /// committee, 1912: 40 per cent less).
        /// </summary>
        public const float ArchMixing = 0.35f;

        /// <summary>
        /// This box with its brick arch taken out: its draft no longer drawn to
        /// the back over a sealed arch, so the front, nearest the tubes, draws
        /// as hard as the back did, a short arch's pattern turned part way (Acker, RME Apr. 1940: with no arch fuel
        /// "would burn much more rapidly at the front"); the gas burned off
        /// over the fire less well; and the boiler's efficiency down by the
        /// coal the railways' tests found an arch saved, more the harder it is
        /// fired. <paramref name="archLength"/> is the arch's reach against a
        /// full arch's, 1 for one that covers about half the grate as the
        /// tested engines' did; a short arch saves less (inferred, in
        /// proportion). The coal and the ash stay the road's.
        /// </summary>
        public GrateCharacter WithoutArch(float archLength = 1f)
        {
            var reach = Math.Max(0f, Math.Min(1f, archLength));
            return new GrateCharacter
            {
                BackRowDraft = BackRowDraft + (FrontRowDraft - BackRowDraft) * reach,
                FrontRowDraft = FrontRowDraft + (BackRowDraft - FrontRowDraft) * reach,
                SideDraft = SideDraft,
                OverFireMixing = OverFireMixing * (1f - ArchMixing * reach),
                EfficiencyKeptLight = EfficiencyKeptLight * (1f - ArchEconomyLight * reach),
                EfficiencyKeptForced = EfficiencyKeptForced * (1f - ArchEconomyForced * reach),
                ClinkerFactor = ClinkerFactor,
                AshFactor = AshFactor,
            };
        }
    }
}
