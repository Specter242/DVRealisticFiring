namespace RealisticFiring.Core
{
 public sealed class FireTuning
    {
        public static readonly FireTuning Default = new FireTuning();

        /// <summary>Share of a shovelful that spills from the aimed zone onto its neighbors.</summary>
        public float ShovelSpill { get; set; } = 0.4f;

        // Forcing. The stock maximum (a full box burning out in the burn time)
        // is no guide to how hard a fire works: an S282 worked hard burns a
        // quarter of it. A fire is forced, its bed white-hot, at a firing rate
        // per area of grate; until the grate is measured, at a share of stock.

        /// <summary>Firing rate of a forced fire, in kg per m² of grate an hour (about 120 lb/ft²/h, heavy firing).</summary>
        public float ForcedFiringRate { get; set; } = 600f;

        /// <summary>A forced fire's rate as a share of the stock maximum, until the grate is measured.</summary>
        public float ForcedShareOfStock { get; set; } = 0.3f;

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

        // Grate slope (each firebox's own draft pattern is its GrateCharacter).

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

        /// <summary>
        /// Share of the raked zone's clinker that a rake breaks into loose ash:
        /// little, the rake being "only to level off the bank" (1944 NY course
        /// p.95); the slice bar breaks clinker.
        /// </summary>
        public float RakeClinkerBreak { get; set; } = 0.05f;

        /// <summary>How much a rake stirs ash up into the raked zone's fire (stirring runs 0 to 1).</summary>
        public float RakeStir { get; set; } = 0.15f;

        // Ash and clinker.

        /// <summary>Ash left per kg of coal burned on the grate.</summary>
        public float AshFraction { get; set; } = 0.06f;

        /// <summary>Time constant of ash sifting through an unshaken grate, in s.</summary>
        public float AshSiftTime { get; set; } = 3600f;

        // Clinker: ash melting in the hot zone of the fire and cementing ash and
        // coal together (1944 NY firing course pp.75-76, 87; ICS 1920 p.10).
        // It needs a hot fire; a thick ash layer, a thin hot fire, and ash
        // stirred up by the fire irons all speed it; once started it grows
        // fast, the clinker choking the air around it.

        /// <summary>Time constant of loose ash fusing to clinker in a forced zone, other things plain, in s.</summary>
        public float ClinkerFormationTime { get; set; } = 9000f;

        /// <summary>Zone forcing (its rate over a forced zone's) above which ash starts to fuse.</summary>
        public float ClinkerHeatThreshold { get; set; } = 0.4f;

        /// <summary>Ash, as a share of the reference bed, that doubles the fusing: a thick ash layer restricts the air.</summary>
        public float ThickAshShare { get; set; } = 1f;

        /// <summary>How much more a thin hot fire (a stoker's) fuses its ash, at its thinnest.</summary>
        public float ThinFireClinkerBoost { get; set; } = 1f;

        /// <summary>Clinker, as a share of the reference bed, at which a zone is choked and its clinker grows fastest.</summary>
        public float ClinkerChokeShare { get; set; } = 0.4f;

        /// <summary>How much more a zone fuses its ash once its clinker has grown to choke it.</summary>
        public float ClinkerGrowth { get; set; } = 1.5f;

        /// <summary>How much more a fully stirred zone fuses its ash.</summary>
        public float StirClinkerBoost { get; set; } = 2f;

        /// <summary>Time constant of stirred-up ash settling back, in s.</summary>
        public float StirDecayTime { get; set; } = 600f;

        /// <summary>How readily the coal clinkers: 1 for typical coal, 0 never.</summary>
        public float ClinkerTendency { get; set; } = 1f;

        // Shaking: "wiggling" and "shaking – half stroke" (1944 NY course
        // pp.88-90). Clinker barely shakes through until it is broken up.

        /// <summary>Share of ash one grate shake removes.</summary>
        public float ShakeAshRemoval { get; set; } = 0.6f;

        /// <summary>Share of clinker one grate shake breaks up and removes.</summary>
        public float ShakeClinkerRemoval { get; set; } = 0.03f;

        /// <summary>Share of burning coal one grate shake drops into the ashpan.</summary>
        public float ShakeBurningLoss { get; set; } = 0.02f;

        /// <summary>Share of ash one wiggle of the grate removes; a wiggle loses no clinker and little fire.</summary>
        public float WiggleAshRemoval { get; set; } = 0.25f;

        /// <summary>Share of burning coal one wiggle drops into the ashpan.</summary>
        public float WiggleBurningLoss { get; set; } = 0.005f;

        /// <summary>
        /// How many times more live coal a shake loses at full draft: shaken
        /// while working hard, "a cloud of smoke and a hail of sparks are
        /// belched from the stack" (Baker p.63).
        /// </summary>
        public float WorkingShakeLoss { get; set; } = 3f;


        /// <summary>Share of a zone's clinker one pass of the slice bar breaks loose.</summary>
        public float SliceClinkerBreak { get; set; } = 0.5f;

        /// <summary>How much one pass of the slice bar stirs ash up into the zone's fire.</summary>
        public float SliceStir { get; set; } = 0.3f;

        /// <summary>Share of a zone's coal and loose ash one stroke of the hoe moves.</summary>
        public float PushShare { get; set; } = 0.6f;

        /// <summary>How much one stroke of the hoe stirs the zones it works.</summary>
        public float PushStir { get; set; } = 0.1f;

        /// <summary>Time constant of what lies on the open drop grate falling through, in s.</summary>
        public float DropGrateTime { get; set; } = 1.5f;

        /// <summary>How many times its share of the air an open drop grate's zone draws: cold air straight in.</summary>
        public float DropGrateDraft { get; set; } = 4f;

        // The ashpan under the grate: it takes the ash sifted and shaken
        // through, the fire and clinker dropped or dumped, and what is left of
        // live coal lost through the bars. Clean it "when it becomes half
        // full", once or twice on a 100-mile freight trip (Baker p.73); fuller,
        // it shuts off the air at the mud ring (Santa Fe 1921 p.26) and heats
        // the ash toward clinker (ICS 1920 p.30). Full, the grate can shake no
        // more ash into it.

        /// <summary>Ashpan capacity per m² of grate, in kg (until the grate is measured, per kg of coal capacity).</summary>
        public float AshpanPerGrateArea { get; set; } = 95f;

        /// <summary>Ashpan capacity per kg of the firebox's coal capacity, in kg, until the grate is measured.</summary>
        public float AshpanPerCoalCapacity { get; set; } = 1.35f;

        /// <summary>Fill above which the ashpan starts to choke the air and heat the ash.</summary>
        public float AshpanChokeStart { get; set; } = 0.5f;

        /// <summary>Share of the grate's air a full ashpan shuts off.</summary>
        public float AshpanChoke { get; set; } = 0.5f;

        /// <summary>How much faster ash fuses over a full ashpan.</summary>
        public float AshpanClinkerBoost { get; set; } = 1f;

        /// <summary>Time constant of the ashpan emptying with its hopper doors wide open, in s.</summary>
        public float AshpanEmptyTime { get; set; } = 2.5f;

        /// <summary>Time constant of clearing ash and clinker with the fire dump fully open, in s.</summary>
        public float DumpClearTime { get; set; } = 10f;
    }
}
