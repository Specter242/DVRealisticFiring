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
    public sealed partial class FireModel
    {
        /// <summary>Air per kg of coal in the stock model: stoichiometric air × 1.2 excess.</summary>
        public const float AirPerCoal = 13.899433f;

        /// <summary>Excess-air factor folded into <see cref="AirPerCoal"/>.</summary>
        public const float DesignExcessAir = 1.2f;

        public const float StoichiometricAirPerCoal = AirPerCoal / DesignExcessAir;

        public const float MinFireTemperature = 500f;
        public const float MaxFireTemperature = 1400f;

        public const float MaxAirRatio = 10f;

        /// <summary>
        /// Heat to dry a kg of the coal's water and carry it off as steam, in
        /// J: about 1,200 BTU a pound, from 70 to 400 °F (ICS Reference Library,
        /// 1904, p.20).
        /// </summary>
        public const float DryingEnergy = 2.79e6f;

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

        // Seconds left of the lighter's flame keeping a just-lit fire alight
        // while nothing burns yet.
        private float lighter;

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

        /// <summary>
        /// The gas leaving <paramref name="zone"/>'s bed to burn over it, in kg/s
        /// before any startup speedup: its fresh coal's volatiles as it catches,
        /// and the carbon monoxide of its coal burning short of air. A zone's
        /// tall flames are this gas burning; its coke alone burns short.
        /// </summary>
        public float ZoneOverFireGas(GrateZone zone) => overFireGas[(int)zone];

        /// <summary>How hard the whole fire burns for its grate: 0 idle, 1 forced.</summary>
        public float FireForcing => Smoothing.Clamp01(ConsumptionRate / (ForcedZoneRate * GrateLayout.ZoneCount));

        /// <summary>
        /// How hard the draft pulls through the grate against a forced fire's
        /// air: 0 still, 1 working hard.
        /// </summary>
        public float Drawing => Smoothing.Clamp01(PrimaryAirFlow / (ForcedZoneRate * GrateLayout.ZoneCount * AirPerCoal));

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

        /// <summary>Coal not yet caught, in kg.</summary>
        public float FreshMass => Sum(z => z.Fresh);

        /// <summary>The coal burning on the grate, kg: none, and the fire is out once its lighter has gone.</summary>
        public float BurningMass => Sum(z => z.Burning);

        public float AshMass => Sum(z => z.Ash);

        public float ClinkerMass => Sum(z => z.Clinker);

        /// <summary>
        /// The water's share of the coal now going on the fire, 0 to 1 (wet
        /// coal; <see cref="CoalMoisture"/>): each kg fired carries the water
        /// the rain left on it beyond the coal as delivered (the stock coal),
        /// which dries off as the coal catches, taking its heat and slowing the
        /// catching.
        /// </summary>
        public float FeedMoisture { get; set; }

        /// <summary>Rain water on the fresh coal, in kg.</summary>
        public float FreshWater { get; private set; }

        /// <summary>Heat spent drying wet coal last tick, in W, already taken from <see cref="Heat"/>.</summary>
        public float DryingHeat { get; private set; }

        // The fresh coal the water was last counted against, and water on
        // coal that caught at once (stoked fines), dried this tick.
        private float wettedFresh;
        private float flashWater;

        // Fresh coal gone other than by catching (dumped, dropped, scaled)
        // takes its share of the water; the water spreads evenly over the
        // fresh coal wherever it is moved on the grate.
        private void CountWater()
        {
            var fresh = FreshMass;
            if (fresh <= NegligibleMass)
                FreshWater = 0f;
            else if (fresh < wettedFresh)
                FreshWater *= fresh / wettedFresh;
            wettedFresh = fresh;
        }

        // Fresh coal put on, wet as the coal it comes from.
        private void Wet(float fresh) => Wet(fresh, FeedMoisture);

        private void Wet(float fresh, float moisture)
        {
            CountWater();
            FreshWater += Math.Max(0f, fresh) * RainWater(moisture);
            wettedFresh = FreshMass;
        }

        // The rain's water on a kg of coal: beyond the coal as delivered, which the stock fire's heat already allows for.
        private static float RainWater(float moisture) =>
            Math.Max(0f, CoalMoisture.WaterPerKg(moisture) - CoalMoisture.WaterPerKg(CoalMoisture.Delivered));

        // Coal restored or mirrored comes dry: its water is not saved.
        private void DryWater()
        {
            FreshWater = flashWater = 0f;
            wettedFresh = FreshMass;
        }

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

        /// <summary>
        /// The air through the grate against what the burning coal on it wants
        /// (1 is the design's 20 % excess), not capped: under 1 the bed is
        /// choked, too much coal for the draft; well over it the air rushes
        /// through a thin fire. <see cref="MaxAirRatio"/> with air and no fire.
        /// </summary>
        public float AirRatio { get; private set; } = 1f;

        /// <summary>Combustion rate over the stock maximum, before the startup speedup.</summary>
        public float CombustionRateNormalized { get; private set; }

        public void Ignite()
        {
            FireOn = true;
            lighter = tuning.LighterTime;
        }

        public void Extinguish()
        {
            FireOn = false;
            lighter = 0f;
            foreach (var starter in Starters) starter.Extinguish();
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
