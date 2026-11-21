using System;

namespace RealisticFiring.Core
{
    public sealed partial class FireModel
    {
        // Per-tick scratch, kept to avoid allocation in the game loop.
        private readonly float[] volatileRate = new float[GrateLayout.ZoneCount];
        private readonly float[] overFireGas = new float[GrateLayout.ZoneCount];
        private readonly float[] oxygen = new float[GrateLayout.ZoneCount];
        private readonly float[] target = new float[GrateLayout.ZoneCount];
        private readonly float[] zoneSpeedup = new float[GrateLayout.ZoneCount];

        private float smokeVelocity;
        private float sootVelocity;
        private float temperatureVelocity;

        // The share of the burning last tick's intake water left, 0 to 1:
        // the zones' stored rates are what burned, this much of the dry rate.
        private float wetted = 1f;

        /// <summary>
        /// Advances the fire by <paramref name="dt"/> seconds. Returns the coal
        /// consumed by combustion this tick, in kg.
        /// </summary>
        public float Step(float dt, FireInputs inputs)
        {
            if (!(dt > 0f) || float.IsInfinity(dt))
            {
                Heat = 0f;
                ConsumptionRate = 0f;
                return 0f;
            }
            // One NaN from the game must not stay in the fire for good.
            inputs.AirFlow = Smoothing.Finite(inputs.AirFlow);
            inputs.IntakeWater = Smoothing.Finite(inputs.IntakeWater);
            inputs.DoorOpening = Smoothing.Finite(inputs.DoorOpening);
            inputs.StartupMultiplier = Smoothing.Finite(inputs.StartupMultiplier, 1f);
            inputs.BoilerTemperature = Smoothing.Finite(inputs.BoilerTemperature, Temperature);
            inputs.Speed = Smoothing.Finite(inputs.Speed);
            inputs.JetOpening = Smoothing.Finite(inputs.JetOpening);
            inputs.BoilerPressure = Smoothing.Finite(inputs.BoilerPressure);

            var water = Smoothing.Clamp01(inputs.IntakeWater);
            if (water > 0.5f)
                FireOn = false;
            var speedup = inputs.StartupMultiplier > 0f ? inputs.StartupMultiplier : 1f;
            var dryAir = 1f - water;

            ShakeDownSlope(inputs.Speed, dt);
            BlowJets(inputs);
            DistributeAir(Math.Max(0f, inputs.AirFlow), Smoothing.Clamp01(inputs.DoorOpening));
            StepStarters(dt, dryAir, inputs.BoilerTemperature, water > 0.5f, out var starterHeat, out var starterSmoke);

            // Fresh coal catches from burning coal beneath and beside it. Part
            // of it leaves at once as volatile gas that has to burn above the bed.
            // Wet coal dries first: it catches slower by the heat its water
            // takes beside the coal's own warming ("the dry piece caught flame
            // first", Power Plant Engineering, 15 Feb 1923, pp.215-217).
            CountWater();
            var waterPerFresh = wettedFresh > NegligibleMass ? FreshWater / wettedFresh : 0f;
            var drying = tuning.WarmUpEnergy / (tuning.WarmUpEnergy + waterPerFresh * DryingEnergy);
            var ignitedToBed = 0f;
            var caught = 0f;
            // The stock startup speedup is for a coal fire. Where wood lies in
            // a zone or beside it, the zone's coal gets it only as its own
            // burning bed grows to full ignition support, so coal just
            // catching from wood is not burned away 25 times faster than the
            // wood can light it. A coal fire with no wood near keeps the
            // whole speedup at any size, as before the wood fire existed.
            for (var i = 0; i < zones.Length; i++)
                zoneSpeedup[i] = WoodNear(i)
                    ? 1f + (speedup - 1f) * Smoothing.Clamp01(zones[i].Burning / tuning.IgnitionBurningMass)
                    : speedup;
            for (var i = 0; i < zones.Length; i++)
            {
                var zone = zones[i];
                volatileRate[i] = 0f;
                if (!FireOn || zone.Fresh <= 0f)
                    continue;

                // The stock startup speedup hurries coal catching from coal, not
                // from the wood: the wood fire burns in real time, and coal laid
                // on it catches only as fast as that fire can light it.
                // The wood can warm coal only with the heat it gives: about a
                // third of its flame and ember heat reaches the coal over it.
                var fromWood = Math.Min(zone.Fresh * woodIgnition[i] / ignitionTime, WoodHeatToCoal * woodHeat[i] / tuning.WarmUpEnergy);
                var ignited = Math.Min(zone.Fresh, (zone.Fresh * speedup * IgnitionSupport((GrateZone)i) / ignitionTime + fromWood) * drying * dt);
                if (zone.Fresh - ignited < NegligibleMass)
                    ignited = zone.Fresh;
                zone.Fresh -= ignited;
                caught += ignited;
                zone.Burning += ignited * (1f - tuning.VolatileFraction);
                ignitedToBed += ignited * (1f - tuning.VolatileFraction);
                volatileRate[i] = ignited * tuning.VolatileFraction / (dt * zoneSpeedup[i]);
            }
            lighter = Math.Max(0f, lighter - speedup * dt);
            if (FireOn && lighter <= 0f && BurningMass <= NegligibleMass && !StarterAlight)
                FireOn = false;

            // The water on the coal that caught has dried off.
            var dried = Math.Min(FreshWater, caught * waterPerFresh) + flashWater;
            FreshWater = Math.Max(0f, FreshWater - caught * waterPerFresh);
            wettedFresh = FreshMass;
            flashWater = 0f;

            // Burning coal: the stock rule per zone, rate = min(fuel, 2 × air).
            // The stock total of what the coal can burn is shared out by draft:
            // high-draft zones burn a larger share, so an evenly fired grate
            // burns through at its hardest-drawing corners first, while the total stays
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
            AirRatio = fuelTotal > 0f ? airTotal / fuelTotal : airTotal > 0f ? MaxAirRatio : 1f;

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
            var spedComplete = 0f;
            var spedUnburned = 0f;
            var smoothTime = parameters.CombustionRateSmoothTime / speedup;
            for (var i = 0; i < zones.Length; i++)
            {
                var zone = zones[i];
                var rateTarget = target[i] * cap;
                var velocity = zone.RateVelocity;
                // Water in the intake cuts the burning by its share, once:
                // taken from the stored rate each tick it compounded, so a
                // tenth of water left a fiftieth of the fire at 100 ticks a
                // second and a fifth at 10 (audit 2026-11-09). The stock
                // firebox has the same line, but only ever sees 0 or 1; a
                // melted plug's jet gives this one the shares between.
                var dry = wetted > 0f ? zone.CombustionRate / wetted : 0f;
                var rate = Smoothing.SmoothApproach(dry, rateTarget, ref velocity, smoothTime, dt) * dryAir;
                if (rateTarget == 0f && rate < 1e-6f)
                {
                    rate = 0f;
                    velocity = 0f;
                }

                var zs = zoneSpeedup[i];
                var burned = zs * rate * dt;
                if (burned >= zone.Burning)
                {
                    burned = zone.Burning;
                    zone.Burning = 0f;
                    rate = burned / (zs * dt);
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
                consumed += gas * zs * dt;
                baseRateTotal += rate + gas;

                // Stock oxygen yield: short air leaves CO in the gas.
                var yield = 0.5f + 0.5f * (float)Math.Pow(oxygen[i], 0.4);
                completeRate += rate * yield;
                spedComplete += zs * rate * yield;
                spedUnburned += zs * (rate * (1f - yield) + gas);
                volatileTotal += gas;
                unburnedRate += rate * (1f - yield) + gas;
                overFireGas[i] = rate * (1f - yield) + gas;
                overFireDemand += StoichiometricAirPerCoal * (rate * (1f - yield) + gas);
                freePrimaryAir += Math.Max(0f, zone.AirFlow * dryAir - AirPerCoal * rate);

                FormAsh(zone, burned, rate, dt);
            }

            var coldAirLoss = ResolveOverFireAir(dryAir, freePrimaryAir, overFireDemand);

            var burnedRate = completeRate + OverFireBurnout * unburnedRate;
            var yieldAverage = baseRateTotal > 0f ? burnedRate / baseRateTotal : 1f;
            var efficiency = Efficiency.At(yieldAverage * baseRateTotal);
            var grossHeat = (spedComplete + OverFireBurnout * spedUnburned) * FireboxParameters.CoalHeatContent * efficiency;
            // Warming fresh coal stores heat that comes back as it burns away.
            var warmUp = tuning.WarmUpEnergy * (ignitedToBed - burnedToGas) / dt;
            // Drying the wet coal's water takes its heat up the stack as steam:
            // about 1 per cent of the heat for 10 per cent of moisture
            // (Kreisinger, Bureau of Mines Technical Paper 80, 1915, p.63).
            DryingHeat = dried * DryingEnergy / dt;
            Heat = Math.Max(0f, grossHeat - coldAirLoss - warmUp - DryingHeat) + starterHeat * Efficiency.At(0f);

            ConsumptionRate = consumed / dt;
            wetted = dryAir;
            CombustionRateNormalized = (baseRateTotal + starterHeat / FireboxParameters.CoalHeatContent) / parameters.MaxCombustionRate;

            if (CoalMass <= 0f && !StarterAlight && Temperature < MinFireTemperature)
                FireOn = false;

            var unburnedShare = baseRateTotal > 0f ? (1f - OverFireBurnout) * unburnedRate / baseRateTotal : 0f;
            var smokeTarget = FireOn ? Smoothing.Clamp01(Math.Max(starterSmoke, tuning.SmokeGain * unburnedShare)) : 0f;
            SmokeDensity = Smoothing.SmoothApproach(SmokeDensity, smokeTarget, ref smokeVelocity, smoothTime, dt);
            var sootShare = baseRateTotal > 0f ? (1f - OverFireBurnout) * volatileTotal / baseRateTotal : 0f;
            var sootTarget = FireOn ? Smoothing.Clamp01(tuning.SootGain * sootShare) : 0f;
            SootDensity = Smoothing.SmoothApproach(SootDensity, sootTarget, ref sootVelocity, smoothTime, dt);

            var temperatureTarget = baseRateTotal < MinCombustionRate && starterHeat <= 0f
                ? inputs.BoilerTemperature
                : Smoothing.Lerp(MinFireTemperature, MaxFireTemperature, Heat / parameters.MaxHeatOutput);
            Temperature = Smoothing.SmoothDamp(Temperature, temperatureTarget, ref temperatureVelocity,
                parameters.TemperatureSmoothTime, dt);

            return consumed;
        }

        private float IgnitionSupport(GrateZone zone)
        {
            // Indexed: enumerating the neighbors boxed an enumerator each time,
            // 360 bytes a step with fresh coal in all nine zones.
            var neighbors = GrateLayout.Neighbors(zone);
            var neighborBurning = 0f;
            for (var k = 0; k < neighbors.Count; k++)
                neighborBurning += zones[(int)neighbors[k]].Burning;
            neighborBurning /= neighbors.Count;

            var support = (zones[(int)zone].Burning + tuning.NeighborIgnitionWeight * neighborBurning)
                          / tuning.IgnitionBurningMass;
            if (lighter > 0f)
                return Math.Max(tuning.EmberIgnitionSupport, Smoothing.Clamp01(support));
            return BurningMass < tuning.DyingEmbers ? 0f : Smoothing.Clamp01(support);
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
    }
}
