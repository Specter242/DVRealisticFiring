using System;

namespace RealisticFiring.Core
{
    /// <summary>
    /// The visible smoke the fire makes, zone by zone, which both the firebox
    /// shows and the stack lets out: green coal's gas as it heats and the
    /// fire's own smoke, both burned off by air over the fire; and a
    /// shovelful's smoke-out, a charge of thick, dark gas from coal landing
    /// on a hot fire, filling the box until it catches and the draft clears
    /// it. Rates are in firebox smoke puffs a second. Stepped with the fire,
    /// so the stack has it whether or not the cab (and its firebox view) is
    /// loaded.
    /// </summary>
    public sealed class FireboxSmoke
    {
        /// <summary>Puffs a second per zone of all-green coal giving off its gas.</summary>
        public const float SmokePerZone = 26f;

        /// <summary>Puffs a second per zone at the fire's full smoke density.</summary>
        public const float SmokeFromFire = 18f;

        /// <summary>A jump of at least this much green coal in a zone at once is a shovelful landing, in kg (a stoker's trickle is grams).</summary>
        public const float LandingMass = 0.1f;

        /// <summary>Smoke-out puffs a second per kg of charge left.</summary>
        public const float SmokeOutPerKg = 8f;

        // The charge clears over these times, in s, from a dull fire under a
        // weak draft to a forced one, and this much sooner with the door open.
        private const float SmokeOutSlow = 7f;
        private const float SmokeOutFast = 3f;
        private const float DoorClearing = 0.4f;

        // A zone with less coal than this, in kg, smokes nothing.
        private const float MinCoal = 0.2f;

        // Landed coal waiting for the firebox view to throw its dust, at most this much, in kg.
        private const float MaxPendingDust = 10f;

        private readonly float[] charge = new float[GrateLayout.ZoneCount];
        private readonly float[] lastFresh = new float[GrateLayout.ZoneCount];
        private readonly float[] gasRate = new float[GrateLayout.ZoneCount];
        private readonly float[] smokeOutRate = new float[GrateLayout.ZoneCount];
        private readonly float[] landed = new float[GrateLayout.ZoneCount];
        private bool freshKnown;

        /// <summary>Gas and the fire's own smoke over a zone, in puffs a second.</summary>
        public float GasRate(GrateZone zone) => gasRate[(int)zone];

        /// <summary>A shovelful's smoke-out over a zone, in puffs a second.</summary>
        public float SmokeOutRate(GrateZone zone) => smokeOutRate[(int)zone];

        /// <summary>A zone's smoke-out charge left, in kg of coal.</summary>
        public float Charge(GrateZone zone) => charge[(int)zone];

        /// <summary>Gas and the fire's own smoke over the whole fire, in puffs a second.</summary>
        public float TotalGasRate { get; private set; }

        /// <summary>Smoke-out over the whole fire, in puffs a second.</summary>
        public float TotalSmokeOutRate { get; private set; }

        public float TotalCharge { get; private set; }

        /// <summary>Takes the coal that has landed on a zone since last asked, in kg, for its dust.</summary>
        public float TakeLanded(GrateZone zone)
        {
            var mass = landed[(int)zone];
            landed[(int)zone] = 0f;
            return mass;
        }

        public void Step(float dt, FireModel model)
        {
            if (!(dt > 0f) || model == null)
                return;
            if (!model.FireOn)
            {
                // Coal laid on a dead fire smokes nothing when it is lit.
                Clear();
                return;
            }

            var warming = Smoothing.Clamp01(0.4f + 0.6f * model.Temperature / 900f);
            var burnOff = 1f - 0.6f * model.OverFireBurnout * Smoothing.Clamp01(model.DoorAirFlow * 10f);
            var clearing = Smoothing.Lerp(SmokeOutSlow, SmokeOutFast, model.FireForcing)
                           * (1f - DoorClearing * Smoothing.Clamp01(model.DoorAirFlow * 2f));
            var gasTotal = 0f;
            var smokeOutTotal = 0f;
            var chargeTotal = 0f;
            for (var i = 0; i < GrateLayout.ZoneCount; i++)
            {
                var zone = model.Zones[i];
                var jump = zone.Fresh - lastFresh[i];
                lastFresh[i] = zone.Fresh;
                if (freshKnown && jump >= LandingMass)
                {
                    charge[i] += jump;
                    landed[i] = Math.Min(MaxPendingDust, landed[i] + jump);
                }
                if (zone.Coal < MinCoal)
                {
                    charge[i] = gasRate[i] = smokeOutRate[i] = 0f;
                    continue;
                }

                var green = zone.Fresh / zone.Coal;
                gasRate[i] = (SmokePerZone * green * warming + SmokeFromFire * model.SmokeDensity) * burnOff;
                smokeOutRate[i] = SmokeOutPerKg * charge[i] * warming;
                charge[i] *= (float)Math.Exp(-dt / clearing);
                gasTotal += gasRate[i];
                smokeOutTotal += smokeOutRate[i];
                chargeTotal += charge[i];
            }
            freshKnown = true;
            TotalGasRate = gasTotal;
            TotalSmokeOutRate = smokeOutTotal;
            TotalCharge = chargeTotal;
        }

        private void Clear()
        {
            freshKnown = false;
            Array.Clear(charge, 0, charge.Length);
            Array.Clear(gasRate, 0, gasRate.Length);
            Array.Clear(smokeOutRate, 0, smokeOutRate.Length);
            Array.Clear(landed, 0, landed.Length);
            TotalGasRate = TotalSmokeOutRate = TotalCharge = 0f;
        }
    }
}
