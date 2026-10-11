using System;
using System.Linq;
using System.Collections.Generic;

namespace RealisticFiring.Core
{
    /// <summary>
    /// A coal fire on a zoned grate. Each zone holds fresh coal, burning coal,
    /// ash, and clinker. The exhaust draft pulls air through the zones in
    /// proportion to their conductance, so holes and ash redistribute it; door
    /// air enters above the fire, where it burns smoke but cools the firebox.
    ///
    /// A well-managed fire (even, clean, door shut, coal matched to draft)
    /// reproduces the stock Firebox: burning coal is limited to
    /// min(fuel, 2 × air) with the stock oxygen yield and efficiency curves.
    /// </summary>
    public sealed class FireModel
    {
        /// <summary>Air per kg of coal in the stock model: stoichiometric air × 1.2 excess.</summary>
        public const float AirPerCoal = 13.899433f;

        /// <summary>Excess-air factor folded into <see cref="AirPerCoal"/>.</summary>
        public const float DesignExcessAir = 1.2f;

        public const float StoichiometricAirPerCoal = AirPerCoal / DesignExcessAir;

        public const float MinFireTemperature = 500f;
        public const float MaxFireTemperature = 1400f;

        private const float MinCombustionRate = 1e-9f;
        private const float NegligibleMass = 1e-5f;

        // Nearly empty zones burn out at this rate instead of decaying forever.
        private const float EmberMass = 0.05f;
        private const float EmberBurnRate = 0.0005f;

        private readonly FireboxParameters parameters;
        private readonly FireTuning tuning;
        private readonly ZoneState[] zones = new ZoneState[GrateLayout.ZoneCount];
        private readonly float zoneArea = 1f / GrateLayout.ZoneCount;
        private readonly float referenceZoneMass;
        private readonly float ignitionTime;
        private readonly float carbonBurnTime;

        // Per-tick scratch, kept to avoid allocation in the game loop.
        private readonly float[] conductance = new float[GrateLayout.ZoneCount];
        private readonly float[] draftWeights = new float[GrateLayout.ZoneCount];
        private readonly float[] volatileRate = new float[GrateLayout.ZoneCount];
        private readonly float[] oxygen = new float[GrateLayout.ZoneCount];
        private readonly float[] target = new float[GrateLayout.ZoneCount];

        private float smokeVelocity;
        private float sootVelocity;
        private float temperatureVelocity;

        public FireModel(FireboxParameters parameters, FireTuning tuning = null)
        {
            this.parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
            this.tuning = tuning ?? FireTuning.Default;
            for (var i = 0; i < zones.Length; i++)
                zones[i] = new ZoneState();

            referenceZoneMass = parameters.MaxCoalCapacity * zoneArea * this.tuning.ReferenceDepthFraction;

            // Coal spends the ignition time as fresh coal, then burns. Shorten
            // the burning stage so a box of coal still lasts the stock burn
            // time: (1 - volatiles) × carbonBurnTime + ignitionTime = burnTime.
            ignitionTime = Math.Min(this.tuning.IgnitionTime, 0.5f * parameters.BurnTime);
            carbonBurnTime = (parameters.BurnTime - ignitionTime) / (1f - this.tuning.VolatileFraction);

            Temperature = 100f;
            WeighDraft();
        }

        public FireboxParameters Parameters => parameters;

        /// <summary>A zone's reference bed, in kg: what its ash, clinker, and coal are weighed against.</summary>
        public float ReferenceZoneMass => referenceZoneMass;

        private GrateCharacter character = GrateCharacter.Typical;

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

        // The front and sides draw more air than the back, by the firebox's
        // character; the weights average 1, so an even bed draws the stock total.
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

        /// <summary>
        /// Slope of the grate in degrees; negative when it falls toward the
        /// front, as most do. A moving loco shakes coal down it.
        /// </summary>
        public float GrateSlope { get; set; }

        /// <summary>
        /// Area of the grate in m², as measured from the loco's coal pile; 0
        /// until then, when a forced fire is taken as a share of the stock maximum.
        /// </summary>
        public float GrateArea { get; set; }

        /// <summary>Combustion rate of one zone of a forced fire, its bed white-hot, in kg/s.</summary>
        public float ForcedZoneRate => GrateArea > 0f
            ? tuning.ForcedFiringRate * GrateArea * zoneArea / 3600f
            : tuning.ForcedShareOfStock * parameters.MaxCombustionRate * zoneArea;

        /// <summary>How hard <paramref name="zone"/> burns for its share of the grate: 0 idle, 1 forced.</summary>
        public float ZoneForcing(GrateZone zone) => Smoothing.Clamp01(zones[(int)zone].CombustionRate / ForcedZoneRate);

        /// <summary>How hard the whole fire burns for its grate: 0 idle, 1 forced.</summary>
        public float FireForcing => Smoothing.Clamp01(ConsumptionRate / (ForcedZoneRate * GrateLayout.ZoneCount));

        /// <summary>
        /// How hard the draft pulls through the grate against a forced fire's
        /// air: 0 still, 1 working hard.
        /// </summary>
        public float Drawing => Smoothing.Clamp01(PrimaryAirFlow / (ForcedZoneRate * GrateLayout.ZoneCount * AirPerCoal));

        /// <summary>Live coal shaking has thrown up the stack as cinders, in kg, since the fire was made.</summary>
        public float ShakeCinders { get; private set; }

        /// <summary>
        /// The zones the drop grate covers: the front row unless the loco says
        /// otherwise ("the 'drop-grate'—the forward section of the grates", Baker).
        /// </summary>
        public IReadOnlyList<GrateZone> DropGrate { get; set; } = new[] { GrateZone.FrontLeft, GrateZone.Front, GrateZone.FrontRight };

        /// <summary>How far the drop grate stood open last tick, 0 to 1.</summary>
        public float DropGrateOpening { get; private set; }

        /// <summary>Ash and clinker the drop grate has dropped, in kg, since the fire was made.</summary>
        public float DroppedAshAndClinker { get; private set; }

        /// <summary>Loose ash and broken clinker hooked out through the door, in kg, since the fire was made.</summary>
        public float HookedOut { get; private set; }

        /// <summary>Ash, clinker, and burnt-out coal in the ashpan, in kg.</summary>
        public float Ashpan { get; private set; }

        /// <summary>What the ashpan holds when full, in kg: by grate area once measured, else by coal capacity.</summary>
        public float AshpanCapacity => GrateArea > 0f
            ? tuning.AshpanPerGrateArea * GrateArea
            : tuning.AshpanPerCoalCapacity * parameters.MaxCoalCapacity;

        /// <summary>How full the ashpan is, 0 to 1.</summary>
        public float AshpanFill => Smoothing.Clamp01(Ashpan / AshpanCapacity);

        /// <summary>Ashes let out of the ashpan's hopper doors, in kg, since the fire was made.</summary>
        public float AshpanEmptied { get; private set; }

        // How far past starting to choke the ashpan is, 0 to 1.
        private float AshpanOverfill => Smoothing.Clamp01((AshpanFill - tuning.AshpanChokeStart) / (1f - tuning.AshpanChokeStart));

        // Ash and clinker sifted or shaken through go into the ashpan as far as
        // it has room; returns the share of <paramref name="mass"/> that went.
        private float IntoAshpan(float mass)
        {
            if (!(mass > 0f))
                return 1f;
            var room = Math.Max(0f, AshpanCapacity - Ashpan);
            var taken = Math.Min(mass, room);
            Ashpan += taken;
            return taken / mass;
        }

        // Fire falls into the ashpan regardless and burns down to its ash there.
        private void FireIntoAshpan(float coal) => Ashpan += Math.Max(0f, coal) * AshFraction;

        // The coal's ash, by its road's coal.
        private float AshFraction => tuning.AshFraction * character.AshFactor;

        /// <summary>
        /// Holds the ashpan's hopper doors open, 0 to 1, for one tick: the
        /// ashes fall out. Returns the kg let out.
        /// </summary>
        public float OpenAshpan(float opening, float dt)
        {
            opening = Smoothing.Clamp01(opening);
            if (opening <= 0f || !(dt > 0f) || Ashpan <= 0f)
                return 0f;
            var emptied = Ashpan * (1f - (float)Math.Exp(-opening * dt / tuning.AshpanEmptyTime));
            if (Ashpan - emptied < NegligibleMass)
                emptied = Ashpan;
            Ashpan -= emptied;
            AshpanEmptied += emptied;
            return emptied;
        }

        /// <summary>Restores the ashpan's contents from a save.</summary>
        public void RestoreAshpan(float mass) => Ashpan = mass >= 0f && !float.IsInfinity(mass) ? mass : 0f;

        /// <summary>How much air <paramref name="zone"/> draws against the grate's average, on an even bed.</summary>
        public float DraftWeight(GrateZone zone) => draftWeights[(int)zone];

        /// <summary>
        /// Where a fireman following the fire would put the next shovelful:
        /// the zone furthest below its share of the coal. Shares go with the
        /// square of each zone's draft, which is what evens out the air, so
        /// the high-draft front corners carry more coal than the back.
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

        public IReadOnlyList<ZoneState> Zones => zones;

        public bool FireOn { get; private set; }

        public float CoalMass
        {
            get
            {
                var total = 0f;
                foreach (var zone in zones)
                    total += zone.Coal;
                return total;
            }
        }

        public float AshMass => Sum(z => z.Ash);

        public float ClinkerMass => Sum(z => z.Clinker);

        /// <summary>Live coal lost into the ashpan by shaking, in kg since creation.</summary>
        public float CoalDroppedThroughGrate { get; private set; }

        /// <summary>Coal removed by the fire dump, in kg since creation.</summary>
        public float CoalDumped { get; private set; }

        // Outputs of the last Step.

        /// <summary>Heat delivered to the boiler, in W.</summary>
        public float Heat { get; private set; }

        /// <summary>Coal consumed per second, including the startup speedup, in kg/s.</summary>
        public float ConsumptionRate { get; private set; }

        /// <summary>Heat delivered per kg of coal consumed, in J/kg; 0 when nothing burns.</summary>
        public float HeatPerCoal => ConsumptionRate > 0f ? Heat / ConsumptionRate : 0f;

        /// <summary>
        /// Stock-compatible smoke density, 0 to 1: unburned gas of any kind
        /// leaving the fire, carbon monoxide included.
        /// </summary>
        public float SmokeDensity { get; private set; }

        /// <summary>
        /// Visible smoke, 0 to 1: the tarry gas fresh coal gives off that
        /// leaves without burning. Carbon monoxide from an air-starved bed is
        /// invisible, so it does not count; door air burns soot off.
        /// </summary>
        public float SootDensity { get; private set; }

        /// <summary>Firebox temperature, in °C.</summary>
        public float Temperature { get; private set; }

        /// <summary>Stock-compatible oxygen availability: air over fuel demand, capped at 1.</summary>
        public float OxygenAvailability { get; private set; } = 1f;

        /// <summary>Combustion rate over the stock maximum, before the startup speedup.</summary>
        public float CombustionRateNormalized { get; private set; }

        /// <summary>Air through the grate, in kg/s.</summary>
        public float PrimaryAirFlow { get; private set; }

        /// <summary>Air through the door and its air holes, in kg/s.</summary>
        public float DoorAirFlow { get; private set; }

        /// <summary>Air that passes through without burning anything, in kg/s.</summary>
        public float ExcessAirFlow { get; private set; }

        /// <summary>Share of smoke and gas leaving the bed that burns above it, 0 to 1.</summary>
        public float OverFireBurnout { get; private set; } = 1f;

        public void Ignite() => FireOn = true;

        public void Extinguish() => FireOn = false;

        /// <summary>
        /// Adds fresh coal aimed at one zone; some spills onto its neighbors.
        /// Returns the mass accepted, which the capacity may limit.
        /// </summary>
        public float AddCoal(float mass, GrateZone target)
        {
            var accepted = Accept(mass);
            if (accepted <= 0f)
                return 0f;

            // Each of the four sides takes a quarter of the spill; against a
            // wall it stays in the aimed zone. That way a round that aims at
            // every zone once covers the grate evenly.
            var spillPerSide = accepted * tuning.ShovelSpill / 4f;
            var neighbors = GrateLayout.Neighbors(target);
            zones[(int)target].Fresh += accepted - spillPerSide * neighbors.Count;
            foreach (var neighbor in neighbors)
                zones[(int)neighbor].Fresh += spillPerSide;
            return accepted;
        }

        /// <summary>Adds fresh coal spread evenly. Returns the mass accepted.</summary>
        public float AddCoalEvenly(float mass)
        {
            var accepted = Accept(mass);
            foreach (var zone in zones)
                zone.Fresh += accepted * zoneArea;
            return accepted;
        }

        /// <summary>
        /// Adds stoked coal by zone: lumps land as fresh coal, and fines catch
        /// at once, landing as burning coal. If the box can't take it all,
        /// every zone takes the same share. Returns the mass accepted.
        /// </summary>
        public float AddStoked(float[] lumps, float[] fines)
        {
            if (lumps == null || fines == null || lumps.Length != zones.Length || fines.Length != zones.Length)
                throw new ArgumentException("Need one lump and one fines mass per zone.");

            var offered = 0f;
            for (var i = 0; i < zones.Length; i++)
                offered += Math.Max(0f, lumps[i]) + Math.Max(0f, fines[i]);
            var accepted = Accept(offered);
            if (accepted <= 0f)
                return 0f;

            var share = accepted / offered;
            for (var i = 0; i < zones.Length; i++)
            {
                zones[i].Fresh += Math.Max(0f, lumps[i]) * share;
                zones[i].Burning += FireOn ? Math.Max(0f, fines[i]) * share : 0f;
                if (!FireOn)
                    zones[i].Fresh += Math.Max(0f, fines[i]) * share;
            }
            return accepted;
        }

        /// <summary>Takes coal from every zone in proportion to what it holds. Returns the mass removed.</summary>
        public float RemoveCoal(float mass)
        {
            var coal = CoalMass;
            if (!(mass > 0f) || coal <= 0f)
                return 0f;

            var removed = Math.Min(mass, coal);
            var keep = 1f - removed / coal;
            foreach (var zone in zones)
            {
                zone.Fresh *= keep;
                zone.Burning *= keep;
            }
            return removed;
        }

        /// <summary>
        /// Rocks the grate. A shake (half stroke) clears most loose ash,
        /// breaks up a little clinker, and drops some live coal into the
        /// ashpan; a wiggle clears less ash and loses almost no fire. Shaken
        /// while the draft pulls hard, more live coal falls and some goes up
        /// the stack as cinders.
        /// </summary>
        public void ShakeGrate(ShakeStroke stroke = ShakeStroke.Shake)
        {
            var shake = stroke == ShakeStroke.Shake;
            var drawing = Drawing;
            var working = 1f + (tuning.WorkingShakeLoss - 1f) * drawing * drawing;
            var lossShare = (shake ? tuning.ShakeBurningLoss : tuning.WiggleBurningLoss) * working;
            foreach (var zone in zones)
            {
                // Into the ashpan as far as it has room; a full pan takes none.
                var ash = zone.Ash * (shake ? tuning.ShakeAshRemoval : tuning.WiggleAshRemoval);
                var clinker = shake ? zone.Clinker * tuning.ShakeClinkerRemoval : 0f;
                var went = IntoAshpan(ash + clinker);
                zone.Ash -= ash * went;
                zone.Clinker -= clinker * went;
                var lost = zone.Burning * lossShare;
                zone.Burning -= lost;
                // The loss beyond a still fire's goes up the stack, half of it as cinders.
                var blown = 0.5f * lost * (1f - 1f / working);
                ShakeCinders += blown;
                CoalDroppedThroughGrate += lost - blown;
                FireIntoAshpan(lost - blown);
            }
        }

        /// <summary>
        /// Levels the fire around <paramref name="target"/> with a rake or an
        /// empty shovel: the zone and its neighbors each move part of the way
        /// toward their mean coal, coal going from piled zones to thin ones
        /// with its fresh and burning shares, and some of the raked zone's
        /// clinker breaks into loose ash. Total coal is unchanged.
        /// </summary>
        public void Rake(GrateZone target)
        {
            var group = new List<ZoneState> { zones[(int)target] };
            foreach (var neighbor in GrateLayout.Neighbors(target))
                group.Add(zones[(int)neighbor]);

            var mean = 0f;
            foreach (var zone in group)
                mean += zone.Coal;
            mean /= group.Count;

            var surplus = 0f;
            foreach (var zone in group)
                surplus += Math.Max(0f, zone.Coal - mean);

            if (surplus > NegligibleMass)
            {
                var moving = tuning.RakeShare * surplus;
                float fresh = 0f, burning = 0f;
                foreach (var zone in group)
                {
                    var excess = zone.Coal - mean;
                    if (excess <= 0f)
                        continue;
                    var share = moving * excess / surplus / zone.Coal;
                    fresh += zone.Fresh * share;
                    burning += zone.Burning * share;
                    zone.Fresh -= zone.Fresh * share;
                    zone.Burning -= zone.Burning * share;
                }

                // The mean splits surplus and deficit evenly, so the deficits sum to the surplus.
                foreach (var zone in group)
                {
                    var shortfall = mean - zone.Coal;
                    if (shortfall <= 0f)
                        continue;
                    var share = shortfall / surplus;
                    zone.Fresh += fresh * share;
                    zone.Burning += burning * share;
                }
            }

            var raked = zones[(int)target];
            var broken = raked.Clinker * tuning.RakeClinkerBreak;
            raked.Clinker -= broken;
            raked.Ash += broken;
            Stir(raked, tuning.RakeStir);
        }

        // Fire irons raise ash from the grate into the hot fire, where it fuses.
        private static void Stir(ZoneState zone, float amount) => zone.Stirred = Math.Min(1f, zone.Stirred + amount);

        /// <summary>
        /// Runs the slice bar under <paramref name="target"/>'s fire: half its
        /// clinker breaks loose, for a shake or the drop grate to take, and
        /// ash is stirred up into the fire, where it fuses again.
        /// </summary>
        public void Slice(GrateZone target)
        {
            var zone = zones[(int)target];
            var broken = zone.Clinker * tuning.SliceClinkerBreak;
            zone.Clinker -= broken;
            zone.Ash += broken;
            Stir(zone, tuning.SliceStir);
        }

        /// <summary>
        /// Works <paramref name="target"/> with the hoe toward
        /// <paramref name="direction"/>: part of its coal and loose ash (broken
        /// clinker with it) moves to the next zone. Pulled toward the door from
        /// the back row, the loose ash and clinker come out through it and the
        /// fire stays. Against a side wall nothing moves. Clinker that has not
        /// been broken stays where it is.
        /// </summary>
        public void Push(GrateZone target, GrateDirection direction)
        {
            var from = zones[(int)target];
            var row = GrateLayout.Row(target);
            var column = GrateLayout.Column(target);
            switch (direction)
            {
                case GrateDirection.Front: row++; break;
                case GrateDirection.Back: row--; break;
                case GrateDirection.Left: column--; break;
                default: column++; break;
            }
            Stir(from, tuning.PushStir);
            if (row < 0)
            {
                var hooked = from.Ash * tuning.PushShare;
                from.Ash -= hooked;
                HookedOut += hooked;
                return;
            }
            if (row >= GrateLayout.Rows || column < 0 || column >= GrateLayout.Columns)
                return;

            var to = zones[(int)GrateLayout.At(row, column)];
            var fresh = from.Fresh * tuning.PushShare;
            var burning = from.Burning * tuning.PushShare;
            var ash = from.Ash * tuning.PushShare;
            from.Fresh -= fresh;
            from.Burning -= burning;
            from.Ash -= ash;
            to.Fresh += fresh;
            to.Burning += burning;
            to.Ash += ash;
            Stir(to, tuning.PushStir);
        }

        /// <summary>
        /// Holds the drop grate open, 0 to 1, for one tick: whatever lies on its
        /// zones (fire, ash, clinker) falls into the ashpan, and cold air comes
        /// straight in through the opening.
        /// </summary>
        public void OpenDropGrate(float opening, float dt)
        {
            DropGrateOpening = Smoothing.Clamp01(opening);
            if (DropGrateOpening <= 0f || !(dt > 0f))
                return;
            var keep = (float)Math.Exp(-DropGrateOpening * dt / tuning.DropGrateTime);
            foreach (var target in DropGrate)
            {
                var zone = zones[(int)target];
                CoalDumped += (zone.Fresh + zone.Burning) * (1f - keep);
                DroppedAshAndClinker += (zone.Ash + zone.Clinker) * (1f - keep);
                Ashpan += (zone.Ash + zone.Clinker) * (1f - keep);
                FireIntoAshpan((zone.Fresh + zone.Burning) * (1f - keep));
                zone.Fresh *= keep;
                zone.Burning *= keep;
                zone.Ash *= keep;
                zone.Clinker *= keep;
            }
        }

        /// <summary>
        /// Runs the fire dump at a normalized flow for one tick: coal falls out
        /// at the stock dump rate, and ash and clinker go with it.
        /// </summary>
        public void Dump(float flow, float dt)
        {
            flow = Smoothing.Clamp01(flow);
            if (flow <= 0f || !(dt > 0f))
                return;

            var dumped = RemoveCoal(flow * parameters.CoalDumpRate * dt);
            CoalDumped += dumped;
            FireIntoAshpan(dumped);
            var keep = (float)Math.Exp(-flow * dt / tuning.DumpClearTime);
            foreach (var zone in zones)
            {
                Ashpan += (zone.Ash + zone.Clinker) * (1f - keep);
                zone.Ash *= keep;
                zone.Clinker *= keep;
            }
        }

        /// <summary>Whether anything is left for the dump to remove.</summary>
        public bool HasDumpableContents => CoalMass > 0f || AshMass + ClinkerMass > NegligibleMass;

        /// <summary>
        /// Restores from a stock save, which only knows the total coal. A lit
        /// fire comes back evenly alight; an unlit one as fresh coal. The
        /// ashpan comes back empty.
        /// </summary>
        public void Restore(bool fireOn, float coalMass)
        {
            Ashpan = 0f;
            coalMass = Math.Max(0f, Math.Min(coalMass, parameters.MaxCoalCapacity));
            foreach (var zone in zones)
            {
                zone.Clear();
                if (fireOn)
                    zone.Burning = coalMass * zoneArea;
                else
                    zone.Fresh = coalMass * zoneArea;
            }
            FireOn = fireOn;
        }

        /// <summary>
        /// Brings the coal to <paramref name="coalMass"/> keeping the bed's
        /// shape: each zone's fresh and burning coal scaled alike, or spread
        /// evenly (burning if the fire is on) onto an empty grate. For a fire
        /// that follows another's total, as a multiplayer client's does the host's.
        /// </summary>
        public void MatchCoal(float coalMass)
        {
            coalMass = Math.Max(0f, Math.Min(coalMass, parameters.MaxCoalCapacity));
            var current = CoalMass;
            if (current < NegligibleMass)
            {
                foreach (var zone in zones)
                {
                    if (FireOn)
                        zone.Burning += coalMass * zoneArea;
                    else
                        zone.Fresh += coalMass * zoneArea;
                }
                return;
            }
            var scale = coalMass / current;
            foreach (var zone in zones)
            {
                zone.Fresh *= scale;
                zone.Burning *= scale;
            }
        }

        /// <summary>Zone masses as a flat array: fresh, burning, ash, clinker per zone.</summary>
        public float[] SaveZones()
        {
            var data = new float[GrateLayout.ZoneCount * 4];
            for (var i = 0; i < zones.Length; i++)
            {
                data[i * 4] = zones[i].Fresh;
                data[i * 4 + 1] = zones[i].Burning;
                data[i * 4 + 2] = zones[i].Ash;
                data[i * 4 + 3] = zones[i].Clinker;
            }
            return data;
        }

        /// <summary>Coal (fresh plus burning) in data written by <see cref="SaveZones"/>.</summary>
        public static float SavedCoalMass(float[] data)
        {
            var coal = 0f;
            if (data == null)
                return coal;
            for (var i = 0; i + 1 < data.Length; i += 4)
                coal += data[i] + data[i + 1];
            return coal;
        }

        /// <summary>Restores zones written by <see cref="SaveZones"/>; false if the data does not fit.</summary>
        /// <summary>
        /// Shows another fire's zones (fresh, burning, ash, clinker each, as
        /// <see cref="SaveZones"/> gives them), keeping this fire's own air
        /// and burn rates, so a mirror (a multiplayer client's) does not dim
        /// between updates. Returns false and changes nothing on bad data.
        /// </summary>
        public bool MirrorZones(bool fireOn, float[] data)
        {
            if (data == null || data.Length != GrateLayout.ZoneCount * 4 || data.Any(value => !(value >= 0f) || float.IsInfinity(value)))
                return false;
            for (var i = 0; i < zones.Length; i++)
            {
                zones[i].Fresh = data[i * 4];
                zones[i].Burning = data[i * 4 + 1];
                zones[i].Ash = data[i * 4 + 2];
                zones[i].Clinker = data[i * 4 + 3];
            }
            FireOn = fireOn;
            return true;
        }

        public bool RestoreZones(bool fireOn, float[] data)
        {
            if (data == null || data.Length != GrateLayout.ZoneCount * 4)
                return false;

            foreach (var value in data)
            {
                if (!(value >= 0f) || float.IsInfinity(value))
                    return false;
            }
            if (SavedCoalMass(data) > parameters.MaxCoalCapacity * 1.001f)
                return false;

            for (var i = 0; i < zones.Length; i++)
            {
                zones[i].Clear();
                zones[i].Fresh = data[i * 4];
                zones[i].Burning = data[i * 4 + 1];
                zones[i].Ash = data[i * 4 + 2];
                zones[i].Clinker = data[i * 4 + 3];
            }
            FireOn = fireOn;
            return true;
        }

        /// <summary>
        /// Advances the fire by <paramref name="dt"/> seconds. Returns the coal
        /// consumed by combustion this tick, in kg.
        /// </summary>
        public float Step(float dt, FireInputs inputs)
        {
            if (!(dt > 0f))
            {
                Heat = 0f;
                return 0f;
            }

            var water = Smoothing.Clamp01(inputs.IntakeWater);
            if (water > 0.5f)
                FireOn = false;
            var speedup = inputs.StartupMultiplier > 0f ? inputs.StartupMultiplier : 1f;
            var dryAir = 1f - water;

            ShakeDownSlope(inputs.Speed, dt);
            DistributeAir(Math.Max(0f, inputs.AirFlow), Smoothing.Clamp01(inputs.DoorOpening));

            // Fresh coal catches from burning coal beneath and beside it. Part
            // of it leaves at once as volatile gas that has to burn above the bed.
            var ignitedToBed = 0f;
            for (var i = 0; i < zones.Length; i++)
            {
                var zone = zones[i];
                volatileRate[i] = 0f;
                if (!FireOn || zone.Fresh <= 0f)
                    continue;

                var ignited = Math.Min(zone.Fresh, speedup * zone.Fresh * IgnitionSupport((GrateZone)i) / ignitionTime * dt);
                if (zone.Fresh - ignited < NegligibleMass)
                    ignited = zone.Fresh;
                zone.Fresh -= ignited;
                zone.Burning += ignited * (1f - tuning.VolatileFraction);
                ignitedToBed += ignited * (1f - tuning.VolatileFraction);
                volatileRate[i] = ignited * tuning.VolatileFraction / (dt * speedup);
            }

            // Burning coal: the stock rule per zone, rate = min(fuel, 2 × air).
            // The stock total of what the coal can burn is shared out by draft:
            // high-draft zones burn a larger share, so an evenly fired grate
            // burns through at the front corners first, while the total stays
            // the stock figure for any spread of coal.
            var burningTotal = 0f;
            var draftedTotal = 0f;
            for (var i = 0; i < zones.Length; i++)
            {
                burningTotal += zones[i].Burning;
                draftedTotal += zones[i].Burning * draftWeights[i];
            }
            var draftShare = draftedTotal > 0f ? burningTotal / draftedTotal : 1f;

            var fuelTotal = 0f;
            var airTotal = 0f;
            var targetTotal = 0f;
            for (var i = 0; i < zones.Length; i++)
            {
                var zone = zones[i];
                var fuel = zone.Burning * draftWeights[i] * draftShare / carbonBurnTime;
                if (zone.Burning > 0f && zone.Burning < EmberMass)
                    fuel = Math.Max(fuel, EmberBurnRate);
                var air = zone.AirFlow / AirPerCoal * dryAir;
                fuelTotal += fuel;
                airTotal += air;
                oxygen[i] = fuel > 0f ? Math.Min(1f, air / fuel) : 1f;
                target[i] = FireOn ? Math.Max(MinCombustionRate, Math.Min(fuel, 2f * air)) : 0f;
                targetTotal += target[i];
            }
            OxygenAvailability = fuelTotal > 0f ? Math.Min(1f, airTotal / fuelTotal) : 1f;

            // The grate as a whole never burns faster than the stock maximum.
            var cap = targetTotal > parameters.MaxCombustionRate ? parameters.MaxCombustionRate / targetTotal : 1f;

            var consumed = 0f;
            var burnedToGas = 0f;
            var completeRate = 0f;   // kg/s burned completely, before over-fire burnout
            var unburnedRate = 0f;   // kg/s worth of CO and volatiles leaving the bed
            var overFireDemand = 0f; // kg/s of air they need
            var volatileTotal = 0f;  // kg/s of tarry gas from fresh coal
            var freePrimaryAir = 0f;
            var baseRateTotal = 0f;
            var smoothTime = parameters.CombustionRateSmoothTime / speedup;
            for (var i = 0; i < zones.Length; i++)
            {
                var zone = zones[i];
                var rateTarget = target[i] * cap;
                var velocity = zone.RateVelocity;
                var rate = Smoothing.SmoothApproach(zone.CombustionRate, rateTarget, ref velocity, smoothTime, dt) * dryAir;
                if (rateTarget == 0f && rate < 1e-6f)
                {
                    rate = 0f;
                    velocity = 0f;
                }

                var burned = speedup * rate * dt;
                if (burned >= zone.Burning)
                {
                    burned = zone.Burning;
                    zone.Burning = 0f;
                    rate = burned / (speedup * dt);
                    zone.CombustionRate = 0f;
                    zone.RateVelocity = 0f;
                }
                else
                {
                    zone.Burning -= burned;
                    zone.CombustionRate = rate;
                    zone.RateVelocity = velocity;
                }

                consumed += burned;
                burnedToGas += burned;
                var gas = volatileRate[i];
                consumed += gas * speedup * dt;
                baseRateTotal += rate + gas;

                // Stock oxygen yield: short air leaves CO in the gas.
                var yield = 0.5f + 0.5f * (float)Math.Pow(oxygen[i], 0.4);
                completeRate += rate * yield;
                volatileTotal += gas;
                unburnedRate += rate * (1f - yield) + gas;
                overFireDemand += StoichiometricAirPerCoal * (rate * (1f - yield) + gas);
                freePrimaryAir += Math.Max(0f, zone.AirFlow * dryAir - AirPerCoal * rate);

                FormAsh(zone, burned, rate, dt);
            }

            // Gas leaving the bed burns with the primary air left over and the
            // door air that mixes in. Air beyond that only cools the firebox;
            // cold door air cools it more than air warmed by the bed.
            var doorAir = DoorAirFlow * dryAir;
            var mixedDoorAir = tuning.DoorAirMixing * doorAir;
            OverFireBurnout = overFireDemand > 0f ? Math.Min(1f, character.OverFireMixing * (freePrimaryAir + mixedDoorAir) / overFireDemand) : 1f;
            var overFireNeed = DesignExcessAir * overFireDemand;
            var primaryUsed = Math.Min(freePrimaryAir, overFireNeed);
            var doorUsed = Math.Min(mixedDoorAir, overFireNeed - primaryUsed);
            var excessPrimary = freePrimaryAir - primaryUsed;
            var excessDoor = doorAir - doorUsed;
            ExcessAirFlow = excessPrimary + excessDoor;

            var burnedRate = completeRate + OverFireBurnout * unburnedRate;
            var yieldAverage = baseRateTotal > 0f ? burnedRate / baseRateTotal : 1f;
            var efficiency = Smoothing.Lerp(1f, parameters.EfficiencyAtMaxCombustion,
                yieldAverage * baseRateTotal / parameters.MaxCombustionRate);
            var grossHeat = speedup * burnedRate * FireboxParameters.CoalHeatContent * efficiency;
            var coldAirLoss = excessPrimary * tuning.ExcessAirHeatLoss + excessDoor * tuning.DoorAirHeatLoss;
            // Warming fresh coal stores heat that comes back as it burns away.
            var warmUp = tuning.WarmUpEnergy * (ignitedToBed - burnedToGas) / dt;
            Heat = Math.Max(0f, grossHeat - coldAirLoss - warmUp);

            ConsumptionRate = consumed / dt;
            CombustionRateNormalized = baseRateTotal / parameters.MaxCombustionRate;

            if (CoalMass <= 0f && Temperature < MinFireTemperature)
                FireOn = false;

            var unburnedShare = baseRateTotal > 0f ? (1f - OverFireBurnout) * unburnedRate / baseRateTotal : 0f;
            var smokeTarget = FireOn ? Smoothing.Clamp01(tuning.SmokeGain * unburnedShare) : 0f;
            SmokeDensity = Smoothing.SmoothApproach(SmokeDensity, smokeTarget, ref smokeVelocity, smoothTime, dt);
            var sootShare = baseRateTotal > 0f ? (1f - OverFireBurnout) * volatileTotal / baseRateTotal : 0f;
            var sootTarget = FireOn ? Smoothing.Clamp01(tuning.SootGain * sootShare) : 0f;
            SootDensity = Smoothing.SmoothApproach(SootDensity, sootTarget, ref sootVelocity, smoothTime, dt);

            var temperatureTarget = baseRateTotal < MinCombustionRate
                ? inputs.BoilerTemperature
                : Smoothing.Lerp(MinFireTemperature, MaxFireTemperature, Heat / parameters.MaxHeatOutput);
            Temperature = Smoothing.SmoothDamp(Temperature, temperatureTarget, ref temperatureVelocity,
                parameters.TemperatureSmoothTime, dt);

            return consumed;
        }

        private float Accept(float mass)
        {
            if (!(mass > 0f))
                return 0f;
            return Math.Max(0f, Math.Min(mass, parameters.MaxCoalCapacity - CoalMass));
        }

        private float IgnitionSupport(GrateZone zone)
        {
            var neighbors = GrateLayout.Neighbors(zone);
            var neighborBurning = 0f;
            foreach (var neighbor in neighbors)
                neighborBurning += zones[(int)neighbor].Burning;
            neighborBurning /= neighbors.Count;

            var support = (zones[(int)zone].Burning + tuning.NeighborIgnitionWeight * neighborBurning)
                          / tuning.IgnitionBurningMass;
            return Math.Max(tuning.EmberIgnitionSupport, Smoothing.Clamp01(support));
        }

        private bool IsDropGrate(GrateZone zone)
        {
            foreach (var covered in DropGrate)
                if (covered == zone)
                    return true;
            return false;
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
            var air = stockAirFlow * (float)Math.Sqrt((tubes + 1f) / (tubes + ratio * ratio));

            for (var i = 0; i < zones.Length; i++)
                zones[i].AirFlow = air * conductance[i] / total;
            PrimaryAirFlow = air * grate / total;
            DoorAirFlow = air * doorPath / total;
        }

        // A moving loco's shaking works coal down a sloping grate, row by row,
        // fresh and burning alike; ash stays. Rows are moved from the downhill
        // end first so no coal moves twice in a tick.
        private void ShakeDownSlope(float speed, float dt)
        {
            var downhill = (float)Math.Sin(-GrateSlope * Math.PI / 180.0);
            var share = tuning.SlopeShakeRate * Math.Abs(downhill) * Smoothing.Clamp01(Math.Abs(speed) / tuning.FullShakeSpeed) * dt;
            if (!(share > 0f))
                return;
            var forward = downhill > 0f;
            for (var step = 0; step < GrateLayout.Rows - 1; step++)
            {
                var to = forward ? GrateLayout.Rows - 1 - step : step;
                var from = forward ? to - 1 : to + 1;
                for (var column = 0; column < GrateLayout.Columns; column++)
                {
                    var source = zones[(int)GrateLayout.At(from, column)];
                    var target = zones[(int)GrateLayout.At(to, column)];
                    var fresh = source.Fresh * share;
                    var burning = source.Burning * share;
                    source.Fresh -= fresh;
                    source.Burning -= burning;
                    target.Fresh += fresh;
                    target.Burning += burning;
                }
            }
        }

        // Burned coal leaves ash; ash sifts through the grate slowly and, in
        // zones burning hard, fuses into clinker: faster under a thick ash
        // layer, a thin hot fire, or ash stirred up by the fire irons, and
        // faster again once clinker has started to choke the zone.
        private void FormAsh(ZoneState zone, float burned, float rate, float dt)
        {
            zone.Ash += AshFraction * burned;
            var sifted = zone.Ash * Math.Min(1f, dt / tuning.AshSiftTime);
            zone.Ash -= sifted * IntoAshpan(sifted);
            zone.Stirred -= zone.Stirred * Math.Min(1f, dt / tuning.StirDecayTime);

            var forcing = rate / ForcedZoneRate;
            var heat = Smoothing.Clamp01((forcing - tuning.ClinkerHeatThreshold) / (1f - tuning.ClinkerHeatThreshold));
            var tendency = tuning.ClinkerTendency * character.ClinkerFactor;
            if (heat <= 0f || tendency <= 0f || zone.Ash <= 0f)
                return;
            var thickAsh = 1f + zone.Ash / (tuning.ThickAshShare * referenceZoneMass);
            var thinFire = 1f + tuning.ThinFireClinkerBoost * (1f - Smoothing.Clamp01(zone.Coal / referenceZoneMass));
            var growth = 1f + tuning.ClinkerGrowth * Smoothing.Clamp01(zone.Clinker / (tuning.ClinkerChokeShare * referenceZoneMass));
            var stirred = 1f + tuning.StirClinkerBoost * zone.Stirred;
            var heatedFromBelow = 1f + tuning.AshpanClinkerBoost * AshpanOverfill;
            var speed = tendency * heat * thickAsh * thinFire * growth * stirred * heatedFromBelow;
            var fused = zone.Ash * Math.Min(1f, speed * dt / tuning.ClinkerFormationTime);
            zone.Ash -= fused;
            zone.Clinker += fused;
        }

        private float Sum(Func<ZoneState, float> select)
        {
            var total = 0f;
            foreach (var zone in zones)
                total += select(zone);
            return total;
        }
    }
}
