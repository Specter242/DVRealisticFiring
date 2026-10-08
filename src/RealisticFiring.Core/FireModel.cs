using System;
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

            // The front and sides draw more air than the back; the weights
            // average 1, so an even bed draws the stock total.
            var sum = 0f;
            for (var i = 0; i < draftWeights.Length; i++)
            {
                var zone = (GrateZone)i;
                var row = GrateLayout.Row(zone);
                var rowDraft = row == 0 ? this.tuning.BackRowDraft : row == GrateLayout.Rows - 1 ? this.tuning.FrontRowDraft : 1f;
                var column = GrateLayout.Column(zone);
                var sideDraft = column == 0 || column == GrateLayout.Columns - 1 ? this.tuning.SideDraft : 1f;
                draftWeights[i] = rowDraft * sideDraft;
                sum += draftWeights[i];
            }
            for (var i = 0; i < draftWeights.Length; i++)
                draftWeights[i] *= draftWeights.Length / sum;
        }

        public FireboxParameters Parameters => parameters;

        /// <summary>
        /// Slope of the grate in degrees; negative when it falls toward the
        /// front, as most do. A moving loco shakes coal down it.
        /// </summary>
        public float GrateSlope { get; set; }

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
        /// Rocks the grate: clears most loose ash, breaks up a little clinker,
        /// and drops some live coal into the ashpan.
        /// </summary>
        public void ShakeGrate()
        {
            foreach (var zone in zones)
            {
                zone.Ash *= 1f - tuning.ShakeAshRemoval;
                zone.Clinker *= 1f - tuning.ShakeClinkerRemoval;
                var dropped = zone.Burning * tuning.ShakeBurningLoss;
                zone.Burning -= dropped;
                CoalDroppedThroughGrate += dropped;
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

            CoalDumped += RemoveCoal(flow * parameters.CoalDumpRate * dt);
            var keep = (float)Math.Exp(-flow * dt / tuning.DumpClearTime);
            foreach (var zone in zones)
            {
                zone.Ash *= keep;
                zone.Clinker *= keep;
            }
        }

        /// <summary>Whether anything is left for the dump to remove.</summary>
        public bool HasDumpableContents => CoalMass > 0f || AshMass + ClinkerMass > NegligibleMass;

        /// <summary>
        /// Restores from a stock save, which only knows the total coal. A lit
        /// fire comes back evenly alight; an unlit one as fresh coal.
        /// </summary>
        public void Restore(bool fireOn, float coalMass)
        {
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
            OverFireBurnout = overFireDemand > 0f ? Math.Min(1f, (freePrimaryAir + mixedDoorAir) / overFireDemand) : 1f;
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
                conductance[i] = zoneArea * draftWeights[i] / (float)Math.Sqrt(resistance);
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
        // zones burning hard, fuses into clinker.
        private void FormAsh(ZoneState zone, float burned, float rate, float dt)
        {
            zone.Ash += tuning.AshFraction * burned;
            zone.Ash -= zone.Ash * Math.Min(1f, dt / tuning.AshSiftTime);

            var zoneLoad = rate / (parameters.MaxCombustionRate * zoneArea);
            var heat = Smoothing.Clamp01((zoneLoad - tuning.ClinkerHeatThreshold) / (1f - tuning.ClinkerHeatThreshold));
            if (heat <= 0f)
                return;
            var fused = zone.Ash * Math.Min(1f, heat * dt / tuning.ClinkerFormationTime);
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
