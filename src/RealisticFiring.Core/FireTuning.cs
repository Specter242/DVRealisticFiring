namespace RealisticFiring.Core
{
 public sealed class FireTuning
    {
        public static readonly FireTuning Default = new FireTuning();

        /// <summary>Share of a shovelful that spills from the aimed zone onto its neighbors.</summary>
        public float ShovelSpill { get; set; } = 0.4f;

        // Ignition of fresh coal.

        /// <summary>Seconds for fresh coal to catch on a well-lit zone.</summary>
        public float IgnitionTime { get; set; } = 30f;

        /// <summary>Burning coal in a zone (kg) that fully supports ignition.</summary>
        public float IgnitionBurningMass { get; set; } = 0.5f;

        /// <summary>Weight of neighboring zones' burning coal in ignition support.</summary>
        public float NeighborIgnitionWeight { get; set; } = 0.25f;

        /// <summary>Ignition support a lit fire always has, from embers and kindling.</summary>
        public float EmberIgnitionSupport { get; set; } = 0.05f;

        /// <summary>Share of igniting coal released as volatile gas that must burn above the bed.</summary>
        public float VolatileFraction { get; set; } = 0.1f;

        /// <summary>Heat stored in each kg of coal as it warms to ignition, in J/kg.</summary>
        public float WarmUpEnergy { get; set; } = 1.0e6f;

        // Draft. Resistances are relative to a clean bed of reference depth (1).

        /// <summary>Resistance of the tubes, smokebox, and spark arrestor shared by every path.</summary>
        public float TubeResistance { get; set; } = 3f;

        /// <summary>Resistance of a bare grate.</summary>
        public float GrateResistance { get; set; } = 0.1f;

        /// <summary>Resistance a clean coal bed of reference depth adds to the grate.</summary>
        public float BedResistance { get; set; } = 0.9f;

        /// <summary>Reference bed depth as a fraction of a zone's share of capacity.</summary>
        public float ReferenceDepthFraction { get; set; } = 0.5f;

        /// <summary>Resistance added by ash equal in mass to the reference bed.</summary>
        public float AshResistance { get; set; } = 1.0f;

        /// <summary>Resistance added by clinker equal in mass to the reference bed.</summary>
        public float ClinkerResistance { get; set; } = 4.0f;

        /// <summary>Conductance of the closed door's air holes, relative to the whole reference bed.</summary>
        public float DoorLeakConductance { get; set; } = 0.02f;

        /// <summary>Conductance the fully open door adds, relative to the whole reference bed.</summary>
        public float DoorConductance { get; set; } = 0.6f;

        /// <summary>Share of door air that mixes with the gases above the fire.</summary>
        public float DoorAirMixing { get; set; } = 0.8f;

        // Losses.

        /// <summary>Heat lost per kg of grate air beyond what combustion uses, in J/kg.</summary>
        public float ExcessAirHeatLoss { get; set; } = 0.5e6f;

        /// <summary>Heat lost per kg of door air beyond what combustion uses, in J/kg.</summary>
        public float DoorAirHeatLoss { get; set; } = 1.2e6f;

        /// <summary>Smoke density per unit of unburned gas fraction.</summary>
        public float SmokeGain { get; set; } = 6f;

        /// <summary>Soot density per unit of unburned fresh-coal gas fraction.</summary>
        public float SootGain { get; set; } = 4f;

        // Grate character.

        /// <summary>Draft at the back row of the grate, under the door, before normalizing.</summary>
        public float BackRowDraft { get; set; } = 0.9f;

        /// <summary>Draft at the front row, near the tube plate and the arch's throat, before normalizing.</summary>
        public float FrontRowDraft { get; set; } = 1.12f;

        /// <summary>Draft along the sides, where air slips up beside the firebox walls, against the center column.</summary>
        public float SideDraft { get; set; } = 1.07f;

        /// <summary>
        /// Share of a zone's coal a moving loco shakes down a sloping grate to
        /// the next row, per second, per unit of the slope's sine, at full shaking.
        /// </summary>
        public float SlopeShakeRate { get; set; } = 0.0027f;

        /// <summary>Train speed at which the shaking is full, in m/s.</summary>
        public float FullShakeSpeed { get; set; } = 20f;

        // Raking.

        /// <summary>Share of the way a rake moves a zone and its neighbors toward their mean coal.</summary>
        public float RakeShare { get; set; } = 0.5f;

        /// <summary>Share of the raked zone's clinker that a rake breaks into loose ash.</summary>
        public float RakeClinkerBreak { get; set; } = 0.2f;

        // Ash and clinker.

        /// <summary>Ash left per kg of coal burned on the grate.</summary>
        public float AshFraction { get; set; } = 0.06f;

        /// <summary>Time constant of ash sifting through an unshaken grate, in s.</summary>
        public float AshSiftTime { get; set; } = 1200f;

        /// <summary>Time constant of ash fusing to clinker in a zone burning flat out, in s.</summary>
        public float ClinkerFormationTime { get; set; } = 7200f;

        /// <summary>Zone burn rate, as a share of its maximum, above which ash starts to fuse.</summary>
        public float ClinkerHeatThreshold { get; set; } = 0.6f;

        /// <summary>Share of ash one grate shake removes.</summary>
        public float ShakeAshRemoval { get; set; } = 0.6f;

        /// <summary>Share of clinker one grate shake breaks up and removes.</summary>
        public float ShakeClinkerRemoval { get; set; } = 0.1f;

        /// <summary>Share of burning coal one grate shake drops into the ashpan.</summary>
        public float ShakeBurningLoss { get; set; } = 0.02f;

        /// <summary>Time constant of clearing ash and clinker with the fire dump fully open, in s.</summary>
        public float DumpClearTime { get; set; } = 10f;
    }
}
