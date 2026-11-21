using System;

namespace RealisticFiring.Core
{
    public sealed partial class FireModel
    {
        /// <summary>The firebox's draft at the box's most air, inches of water (inferred: a working firebox's 4 to 6).</summary>
        public const float FullDraftInches = 6f;

        /// <summary>How much more room a kg of the jets' steam takes than a kg of air, at the same pressure and temperature.</summary>
        public const float SteamVolumePerAir = 1.6f;

        /// <summary>
        /// Penetration a kg of jet air buys against a kg of gas rising, sized so
        /// the S282's six large jets reach about halfway across at its full
        /// work: 1.64 kg/s against about 17 kg/s of gas.
        /// </summary>
        public const float JetReach = 5.3f;

        /// <summary>
        /// The share of the jets' air, where it reaches, that meets the gas in
        /// time to burn it, for each unit of the jets' drive against the rising
        /// gas (<see cref="JetReach"/>), at most all of it: sized to the tests'
        /// smoke (the MMA's 1913 Altoona runs: running smoke 20 to 7.5 per cent,
        /// standing 30 to 4; Collins's NYC test plant: firing, 50 to 15 per cent
        /// at 200 psi, standby 50-60 to 0-10).
        /// </summary>
        public const float JetMixing = 0.45f;

        /// <summary>Heat the jets' steam takes from the fire, heated on to the flue's temperature, J/kg (about 150 °C at 2 kJ/kg K).</summary>
        public const float JetSteamHeating = 0.3e6f;

        private readonly float[] conductance = new float[GrateLayout.ZoneCount];
        private readonly float[] draftWeights = new float[GrateLayout.ZoneCount];
        private GrateCharacter character = GrateCharacter.Typical;

        /// <summary>
        /// The boiler's efficiency line for this firebox: the stock figures'
        /// (<see cref="FireboxParameters.Efficiency"/>), less what the box's
        /// fittings lose (<see cref="GrateCharacter.EfficiencyKeptLight"/>).
        /// </summary>
        public EfficiencyLine Efficiency => parameters.Efficiency.Kept(character.EfficiencyKeptLight, character.EfficiencyKeptForced);

        /// <summary>This firebox's own character: its draft pattern, how it burns its smoke, and its road's coal.</summary>
        public GrateCharacter Character
        {
            get => character;
            set
            {
                character = value ?? GrateCharacter.Typical;
                WeighDraft();
            }
        }

        /// <summary>Air through the grate, in kg/s.</summary>
        public float PrimaryAirFlow { get; private set; }

        /// <summary>Air through the door and its air holes, in kg/s.</summary>
        public float DoorAirFlow { get; private set; }

        /// <summary>Air that passes through without burning anything, in kg/s.</summary>
        public float ExcessAirFlow { get; private set; }

        /// <summary>Share of smoke and gas leaving the bed that burns above it, 0 to 1.</summary>
        public float OverFireBurnout { get; private set; } = 1f;

        /// <summary>The box's over-fire jets, or null for none (<see cref="OverFireJets"/>).</summary>
        public OverFireJets Jets { get; set; }

        /// <summary>Steam the jets take from the boiler, in kg/s.</summary>
        public float JetSteamFlow { get; private set; }

        /// <summary>Air the jets drive in over the fire, in kg/s.</summary>
        public float JetAirFlow { get; private set; }

        /// <summary>
        /// How far across the box the jets reach into the rising gas, 0 to 1
        /// (Benton and Engdahl, Fig. 11: penetration falls as the burning rate
        /// rises; their closure puts Collins's poor results at working rates down
        /// to coverage and penetration): across standing, about halfway at a
        /// road engine's full work (council, 2026-11-14; inferred).
        /// </summary>
        public float JetPenetration { get; private set; }

        /// <summary>
        /// The share of the gas leaving the bed that burns over the fire only
        /// because of the jets, 0 to 1: the box's smoke they clear, for drawing
        /// it (the fire volume).
        /// </summary>
        public float JetBurnout { get; private set; }

        /// <summary>
        /// Gas the jets drive in beyond what the draft takes up the tubes, in
        /// kg/s: the box at a positive pressure, its gas and flame forced out at
        /// an open door (Canetta, Railway Age, 13 July 1946: flames "may surge from
        /// the firebox"; Benton and Engdahl p.41: the trip valve that feeds the
        /// blower against "smoke and gases being blown out of the fire door").
        /// </summary>
        public float Blowback { get; private set; }

        /// <summary>How much air <paramref name="zone"/> draws against the grate's average, on an even bed.</summary>
        public float DraftWeight(GrateZone zone) => draftWeights[(int)zone];

        /// <summary>
        /// Where a fireman following the fire would put the next shovelful:
        /// the zone furthest below its share of the coal. Shares go with the
        /// square of each zone's draft, which is what evens out the air, so
        /// the high-draft back corners carry more coal than the front.
        /// </summary>
        public GrateZone NeediestZone()
        {
            var total = CoalMass + 1f;
            var weightSum = 0f;
            foreach (var weight in draftWeights)
                weightSum += weight * weight;
            var best = 0;
            var bestNeed = float.NegativeInfinity;
            for (var i = 0; i < zones.Length; i++)
            {
                var need = total * draftWeights[i] * draftWeights[i] / weightSum - zones[i].Coal;
                if (need > bestNeed)
                {
                    bestNeed = need;
                    best = i;
                }
            }
            return (GrateZone)best;
        }

        // Where the grate draws its air, by the firebox's character (under a
        // sealed arch the back and the sides hardest); the weights average 1,
        // so an even bed draws the stock total.
        private void WeighDraft()
        {
            var sum = 0f;
            for (var i = 0; i < draftWeights.Length; i++)
            {
                var zone = (GrateZone)i;
                var row = GrateLayout.Row(zone);
                var rowDraft = row == 0 ? character.BackRowDraft : row == GrateLayout.Rows - 1 ? character.FrontRowDraft : 1f;
                var column = GrateLayout.Column(zone);
                var sideDraft = column == 0 || column == GrateLayout.Columns - 1 ? character.SideDraft : 1f;
                draftWeights[i] = rowDraft * sideDraft;
                sum += draftWeights[i];
            }
            for (var i = 0; i < draftWeights.Length; i++)
                draftWeights[i] *= draftWeights.Length / sum;
        }

        // The jets' steam and air this tick, from their valve and the boiler's
        // pressure, with the last tick's draft (its share of the box's most
        // air, squared, of a working box's draft).
        private void BlowJets(FireInputs inputs)
        {
            if (Jets == null || !(inputs.JetOpening > 0f) || !(inputs.BoilerPressure > 1f))
            {
                JetSteamFlow = JetAirFlow = 0f;
                return;
            }
            var most = parameters.MaxCombustionRate * AirPerCoal;
            var share = most > 0f ? (PrimaryAirFlow + DoorAirFlow) / most : 0f;
            var (steam, air) = Jets.Flow(inputs.JetOpening, inputs.BoilerPressure - 1f, FullDraftInches * share * share);
            JetSteamFlow = steam;
            JetAirFlow = air;
        }

        // Zones and the door are parallel paths in series with the tubes. The
        // blast is calibrated to pull the stock AIR_FLOW through an even,
        // clean bed holding the same coal, so only unevenness, ash, clinker,
        // and the door change the total; they also change how it splits.
        private void DistributeAir(float stockAirFlow, float door)
        {
            var depthPerZone = CoalMass * zoneArea / referenceZoneMass;
            var evenBed = 1f / (float)Math.Sqrt(tuning.GrateResistance + tuning.BedResistance * depthPerZone);
            var reference = evenBed + tuning.DoorLeakConductance;

            var grate = 0f;
            for (var i = 0; i < zones.Length; i++)
            {
                var zone = zones[i];
                var resistance = tuning.GrateResistance
                                 + (tuning.BedResistance * zone.Coal
                                    + tuning.AshResistance * zone.Ash
                                    + tuning.ClinkerResistance * zone.Clinker) / referenceZoneMass;
                conductance[i] = zoneArea * draftWeights[i] / (float)Math.Sqrt(resistance) * (1f - tuning.AshpanChoke * AshpanOverfill);
                if (DropGrateOpening > 0f && IsDropGrate((GrateZone)i))
                    conductance[i] *= 1f + (tuning.DropGrateDraft - 1f) * DropGrateOpening;
                grate += conductance[i];
            }
            var doorPath = tuning.DoorLeakConductance + tuning.DoorConductance * door;
            var total = grate + doorPath;

            var ratio = reference / total;
            var tubes = tuning.TubeResistance;
            // The draft's head is what pulls the stock flow through the
            // reference bed and the tubes: (T + 1) S^2. The jets push J in
            // above the bed, which goes up the tubes with the grate's and the
            // door's air Q: ratio^2 Q^2 + T (Q + J)^2 = (T + 1) S^2. With
            // T J^2 at or over the head, the box stands at a positive
            // pressure: no air through the grate, and what the tubes can't
            // take blows back (council round 1, 2026-11-14).
            var injected = JetAirFlow + SteamVolumePerAir * JetSteamFlow;
            var head = (tubes + 1f) * stockAirFlow * stockAirFlow;
            float air;
            if (!(injected > 0f))
            {
                air = stockAirFlow * (float)Math.Sqrt((tubes + 1f) / (tubes + ratio * ratio));
                Blowback = 0f;
            }
            else if (tubes * injected * injected >= head)
            {
                air = 0f;
                Blowback = injected - (float)Math.Sqrt(head / tubes);
            }
            else
            {
                var a = ratio * ratio + tubes;
                var b = 2f * tubes * injected;
                var c = tubes * injected * injected - head;
                air = (-b + (float)Math.Sqrt(b * b - 4f * a * c)) / (2f * a);
                Blowback = 0f;
            }

            for (var i = 0; i < zones.Length; i++)
                zones[i].AirFlow = air * conductance[i] / total;
            PrimaryAirFlow = air * grate / total;
            DoorAirFlow = air * doorPath / total;
        }

        // Gas leaving the bed burns with the primary air left over, the door
        // air that mixes in, and the over-fire jets. Returns the excess-air
        // cooling and jet-steam heating loss, in W.
        private float ResolveOverFireAir(float dryAir, float freePrimaryAir, float overFireDemand)
        {
            var doorAir = DoorAirFlow * dryAir;
            var mixedDoorAir = tuning.DoorAirMixing * doorAir;
            var effective = character.OverFireMixing * (freePrimaryAir + mixedDoorAir);
            // The jets: their air where it reaches into the gas, and their
            // stirring, which mixes the door's air in as well as their own; never
            // more than the oxygen there is (council round 1, 2026-11-14).
            var jetAir = JetAirFlow;
            var gasUp = PrimaryAirFlow + DoorAirFlow + jetAir;
            // How strongly the jets drive into the gas: over 1, across the box
            // with momentum to spare, which stirs it (a standing box's gas is
            // mostly the jets' own air); under, part way.
            var drive = jetAir > 0f && gasUp > 0f ? JetReach * jetAir / gasUp : 0f;
            JetPenetration = Math.Min(1f, drive);
            var burnoutWithout = overFireDemand > 0f ? Math.Min(1f, effective / overFireDemand) : 1f;
            if (jetAir > 0f)
            {
                var stirred = overFireDemand > 0f ? JetPenetration * Math.Min(1f, jetAir / overFireDemand) : 0f;
                var withJets = character.OverFireMixing * (freePrimaryAir + doorAir * (tuning.DoorAirMixing + (1f - tuning.DoorAirMixing) * stirred))
                               + Math.Min(1f, JetMixing * drive) * JetPenetration * jetAir;
                effective = Math.Max(effective, Math.Min(withJets, freePrimaryAir + doorAir + jetAir));
            }
            OverFireBurnout = overFireDemand > 0f ? Math.Min(1f, effective / overFireDemand) : 1f;
            JetBurnout = Math.Max(0f, OverFireBurnout - burnoutWithout);
            var overFireNeed = DesignExcessAir * overFireDemand;
            var primaryUsed = Math.Min(freePrimaryAir, overFireNeed);
            var doorUsed = Math.Min(mixedDoorAir, overFireNeed - primaryUsed);
            var jetUsed = Math.Min(jetAir, Math.Max(0f, overFireNeed - primaryUsed - doorUsed));
            var excessPrimary = freePrimaryAir - primaryUsed;
            var excessDoor = doorAir - doorUsed;
            var excessJet = jetAir - jetUsed;
            ExcessAirFlow = excessPrimary + excessDoor + excessJet;
            // The jets' air beyond what burns the gas is excess air, mixed in
            // before the tubes; their steam is heated on to the flue's
            // temperature (the steam itself is the boiler's, charged there).
            return excessPrimary * tuning.ExcessAirHeatLoss + excessDoor * tuning.DoorAirHeatLoss
                   + excessJet * tuning.ExcessAirHeatLoss + JetSteamFlow * JetSteamHeating;
        }
    }
}
