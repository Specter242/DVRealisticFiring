using System;

namespace RealisticFiring.Core
{
    /// <summary>What the boiler thermal-shock model reads each step.</summary>
    public struct BoilerThermalShockInputs
    {
        /// <summary>The boiler's water/metal temperature, °C.</summary>
        public float BoilerTemperature;

        /// <summary>
        /// The game's Steam Startup speedup on the fire (1 = real time, up to
        /// 25 at default 100 %). The measured temperature rise rate is divided
        /// by this and wear integrates over effective thermal time so normal
        /// cold starts are never unfairly punished at any Steam Startup setting.
        /// </summary>
        public float StartupMultiplier;

        /// <summary>
        /// Whether the engine is actively working / drawing significant cylinder
        /// steam. Thermal shock only applies to cold warm-up, never to a working
        /// engine whose boiler pressure has sagged.
        /// </summary>
        public bool Working;

        /// <summary>Whether direct steaming from a repair stand is heating the boiler (safe controlled ramp).</summary>
        public bool DirectSteaming;

        /// <summary>Whether boiler thermal shock is enabled (off in Relaxed preset).</summary>
        public bool Enabled;
    }

    /// <summary>
    /// Thermal stress on boiler tubes and stays when raising steam too quickly
    /// from a cold boiler. Uneven expansion between firebox sheets, tubes, and
    /// cold barrel shell strains tube ends and staybolts, causing gradual
    /// tube/stay leak wear that slightly lowers steaming efficiency, weeps a
    /// slow water leak under pressure, and contributes to engine wear until
    /// repaired at a service station.
    /// </summary>
    public sealed class BoilerThermalShock
    {
        /// <summary>
        /// Normalized boiler heating rate that a cold boiler tolerates without
        /// thermal stress, K/s (7.2 °C/min unboosted: natural draft and gentle
        /// warm-up stay safely below this).
        /// </summary>
        public const float SafeHeatingRate = 0.12f;

        /// <summary>Normalized heating rate above <see cref="SafeHeatingRate"/> corresponding to full forcing, K/s.</summary>
        public const float ReferenceExcessRate = 0.18f;

        /// <summary>Below this temperature (°C) the cold boiler is fully vulnerable to rapid heating.</summary>
        public const float FullVulnerabilityTemperature = 120f;

        /// <summary>At or above this temperature (°C) the boiler is warmed through and immune to cold-start shock.</summary>
        public const float WarmTemperature = 165f;

        /// <summary>Smoothing time constant for the normalized heating rate, in effective seconds.</summary>
        public const float RateSmoothTime = 5f;

        /// <summary>Time constants for thermal stress buildup and relaxation, in effective seconds.</summary>
        public const float StressBuildTime = 30f;
        public const float StressCoolTime = 40f;

        /// <summary>Transient stress above which the crew is warned to ease the fire.</summary>
        public const float WarningStress = 0.15f;

        /// <summary>Transient stress above which tube/stay leak wear begins accumulating.</summary>
        public const float WearStressThreshold = 0.05f;

        /// <summary>
        /// Tube/stay wear gained per effective second at full forcing excess (1.0)
        /// on a cold boiler: one reckless full-blast cold start from 25 °C to
        /// working steam accumulates roughly 20–25 % leak wear.
        /// </summary>
        public const float WearRate = 0.0006f;

        /// <summary>Maximum boiler steaming efficiency lost at full tube/stay leak wear (1.0).</summary>
        public const float MaxEfficiencyLoss = 0.10f;

        /// <summary>Subtle water/steam leak rate at full wear (1.0) and <see cref="ReferenceGaugePressure"/>, kg/s.</summary>
        public const float MaxLeakRate = 0.12f;

        /// <summary>Reference gauge pressure for tube/stay leak scaling, bar.</summary>
        public const float ReferenceGaugePressure = 14f;

        /// <summary>Engine mechanical wear share represented by full tube/stay leak wear (1.0).</summary>
        public const float EngineWearWeight = 0.12f;

        private bool active = true;
        private bool hasLastTemperature;
        private float lastTemperature;

        /// <summary>Smoothed heating rate normalized by Steam Startup speedup, K/s.</summary>
        public float NormalizedRate { get; private set; }

        /// <summary>Current transient thermal stress on tubes and stays, 0 to 1.</summary>
        public float Stress { get; private set; }

        /// <summary>Accumulated tube/stay leak wear, 0 (sound) to 1 (severely leaking).</summary>
        public float Wear { get; private set; }

        /// <summary>Whether the cold boiler is currently being warmed too fast and needs the fire eased.</summary>
        public bool WarmingTooFast { get; private set; }

        /// <summary>Share of fire heat lost through leaking tubes/stays, 0 to <see cref="MaxEfficiencyLoss"/> (0 when disabled).</summary>
        public float EfficiencyLoss => active ? MaxEfficiencyLoss * Wear : 0f;

        /// <summary>How vulnerable the boiler is to cold-start thermal shock at <paramref name="boilerTemperature"/> °C, 0 to 1.</summary>
        public static float ColdVulnerability(float boilerTemperature)
        {
            if (float.IsNaN(boilerTemperature) || boilerTemperature >= WarmTemperature)
                return 0f;
            if (boilerTemperature <= FullVulnerabilityTemperature)
                return 1f;
            return (WarmTemperature - boilerTemperature) / (WarmTemperature - FullVulnerabilityTemperature);
        }

        /// <summary>Water/steam leaking from strained tubes and stays at <paramref name="absolutePressure"/> bar, kg/s (0 when disabled).</summary>
        public float LeakRate(float absolutePressure)
        {
            if (!active || !(Wear > 0f) || float.IsNaN(absolutePressure) || absolutePressure <= 1f)
                return 0f;
            var gauge = absolutePressure - 1f;
            return MaxLeakRate * Wear * (float)Math.Sqrt(gauge / ReferenceGaugePressure);
        }

        /// <summary>Enables or disables thermal-shock effects without discarding saved wear.</summary>
        public void SetEnabled(bool enabled)
        {
            active = enabled;
            if (!enabled)
                ResetTransient();
        }

        public void Step(float dt, BoilerThermalShockInputs inputs)
        {
            if (!(dt > 0f))
                return;
            if (!inputs.Enabled)
            {
                SetEnabled(false);
                return;
            }
            active = true;

            var temp = inputs.BoilerTemperature;
            if (float.IsNaN(temp) || float.IsInfinity(temp))
            {
                ResetTransient();
                return;
            }

            // Measure the actual positive rate of boiler temperature rise rather
            // than estimating from fire heat: a working engine whose pressure is
            // sagging or steady is not warming its boiler shell.
            var rawRate = 0f;
            if (hasLastTemperature)
            {
                var dT = temp - lastTemperature;
                if (dT > 0f)
                    rawRate = dT / dt;
            }
            lastTemperature = temp;
            hasLastTemperature = true;

            if (inputs.DirectSteaming || inputs.Working)
            {
                NormalizedRate = 0f;
                Stress = 0f;
                WarmingTooFast = false;
                return;
            }

            var speedup = inputs.StartupMultiplier > 1f && !float.IsNaN(inputs.StartupMultiplier) && !float.IsInfinity(inputs.StartupMultiplier)
                ? inputs.StartupMultiplier
                : 1f;
            var normalized = rawRate / speedup;
            var effectiveDt = dt * speedup;

            NormalizedRate += (normalized - NormalizedRate) * (1f - (float)Math.Exp(-effectiveDt / RateSmoothTime));

            var coldness = ColdVulnerability(temp);
            var excess = coldness > 0f && NormalizedRate > SafeHeatingRate
                ? (NormalizedRate - SafeHeatingRate) / ReferenceExcessRate * coldness
                : 0f;
            var targetStress = Math.Min(1f, excess);
            var stressTime = targetStress > Stress ? StressBuildTime : StressCoolTime;
            Stress += (targetStress - Stress) * (1f - (float)Math.Exp(-effectiveDt / stressTime));

            WarmingTooFast = coldness > 0.05f && (NormalizedRate > SafeHeatingRate || Stress >= WarningStress);

            if (excess > 0f && Stress >= WearStressThreshold)
            {
                // Superlinear severity so mild overshoots are forgiving while
                // blasting a cold boiler hard punishes tubes and stays sharply.
                var severity = (float)Math.Pow(excess, 1.5);
                Wear = Math.Min(1f, Wear + WearRate * severity * effectiveDt);
            }
        }

        /// <summary>Repairs <paramref name="share"/> (0 to 1) of accumulated tube/stay leak wear at a service station.</summary>
        public void Repair(float share)
        {
            var kept = 1f - Math.Max(0f, Math.Min(1f, Smoothing.Finite(share)));
            Wear *= kept;
            if (Wear < 1e-5f)
                Wear = 0f;
        }

        /// <summary>Clears transient heating rate and stress (e.g. after load or boiler renewal).</summary>
        public void ResetTransient()
        {
            NormalizedRate = 0f;
            Stress = 0f;
            WarmingTooFast = false;
            hasLastTemperature = false;
        }

        /// <summary>Restores saved tube/stay leak wear.</summary>
        public void Restore(float wear)
        {
            Wear = Math.Max(0f, Math.Min(1f, Smoothing.Finite(wear)));
            ResetTransient();
        }

        /// <summary>Applies host-authoritative state on a multiplayer guest.</summary>
        public void Mirror(float wear, float stress, bool warmingTooFast)
        {
            Wear = Math.Max(0f, Math.Min(1f, Smoothing.Finite(wear)));
            Stress = Math.Max(0f, Math.Min(1f, Smoothing.Finite(stress)));
            WarmingTooFast = warmingTooFast;
        }
    }
}
