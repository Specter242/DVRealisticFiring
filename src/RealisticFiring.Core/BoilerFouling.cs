using System;

namespace RealisticFiring.Core
{
    /// <summary>What fouls a boiler each step.</summary>
    public struct BoilerFoulingInputs
    {
        /// <summary>Water boiled off this step, kg.</summary>
        public float Boiled;

        /// <summary>The feed water's dissolved solids, ppm.</summary>
        public float FeedSolids;

        /// <summary>The boiler's heating surface, m².</summary>
        public float HeatingSurface;

        /// <summary>The fire's soot, 0 to 1 (a coal fire's soot density; none for oil, whose flues are its own).</summary>
        public float Soot;

        /// <summary>The flue gas's flow against the fire's full, 0 to 1.</summary>
        public float GasFlow;
    }

 public sealed class BoilerFouling
    {
        /// <summary>The share of the feed's solids that bakes on as hard scale rather than settling as sludge (inferred).</summary>
        public const float ScaleShare = 0.2f;

        /// <summary>Scale's density, kg/m³.</summary>
        public const float ScaleDensity = 2500f;

        /// <summary>The heat lost for each mm of scale (inferred: period tables give about a tenth for 1/16 in, modern ones less).</summary>
        public const float LossPerScaleMm = 0.06f;

        /// <summary>The most heat soot, or scale, keeps from the water.</summary>
        public const float MostLoss = 0.15f;

        /// <summary>
        /// Soot's loss gained per second at full soot and full gas flow, and the
        /// share scoured out per second at full flow, going as the flow's
        /// square: black smoke at half flow loses about 5 % in an hour; at full
        /// flow the tubes scour clean over half an hour (inferred).
        /// </summary>
        public const float SootDeposit = 5.6e-5f;
        public const float SootScour = 5.6e-4f;

        /// <summary>The share of the scale a washout loosens and washes out.</summary>
        public const float WashoutScale = 0.5f;

        /// <summary>Scale on the heating surface, kg.</summary>
        public float ScaleMass { get; private set; }

        /// <summary>The heat soot in the tubes keeps from the water, 0 to <see cref="MostLoss"/>.</summary>
        public float SootLoss { get; private set; }

        /// <summary>The heat the scale keeps from the water over <paramref name="heatingSurface"/> m².</summary>
        public static float ScaleLossOf(float scaleMass, float heatingSurface) =>
            heatingSurface > 0f ? Math.Min(MostLoss, scaleMass / (heatingSurface * ScaleDensity) * 1000f * LossPerScaleMm) : 0f;

        /// <summary>The heat lost to soot and scale together, 0 to 1.</summary>
        public float Loss(float heatingSurface) => Math.Min(1f, SootLoss + ScaleLossOf(ScaleMass, heatingSurface));

        public void Step(float dt, BoilerFoulingInputs inputs)
        {
            if (!(dt > 0f))
                return;
            ScaleMass += Math.Max(0f, inputs.Boiled) * Math.Max(0f, inputs.FeedSolids) * 1e-6f * ScaleShare;
            var flow = Math.Max(0f, Math.Min(1f, inputs.GasFlow));
            var soot = Math.Max(0f, Math.Min(1f, inputs.Soot));
            var change = SootDeposit * soot * flow - SootScour * flow * flow * SootLoss;
            SootLoss = Math.Max(0f, Math.Min(MostLoss, SootLoss + change * dt));
        }

        /// <summary>A washout: the loose scale washed out.</summary>
        public void Washout() => ScaleMass *= 1f - WashoutScale;

        /// <summary>The shed's work on <paramref name="share"/> of it: tubes cleaned and scale chipped out.</summary>
        public void Clean(float share)
        {
            var kept = 1f - Math.Max(0f, Math.Min(1f, share));
            ScaleMass *= kept;
            SootLoss *= kept;
        }

        public void Restore(float scaleMass, float sootLoss)
        {
            ScaleMass = Math.Max(0f, scaleMass);
            SootLoss = Math.Max(0f, Math.Min(MostLoss, sootLoss));
        }
    }

 public static class Drifting
    {
        /// <summary>The reverser's travel from mid-gear at which an engine drifts freely: about three-quarter stroke.</summary>
        public const float FreeReverser = 0.8f;

        /// <summary>A throttle open this far keeps steam in the cylinders.</summary>
        public const float CrackedThrottle = 0.02f;

        /// <summary>Below this speed, m/s, it does no harm worth counting.</summary>
        public const float HarmlessSpeed = 5.5f;

        /// <summary>The engine's health lost per hour drifting in mid-gear at <see cref="FullHarmSpeed"/> m/s, throttle shut (inferred).</summary>
        public const float WearPerHour = 0.05f;
        public const float FullHarmSpeed = 20f;

        /// <summary>Whether drifting this way draws smokebox gas into the cylinders.</summary>
        public static bool HookedUp(float throttle, float reverser, float speed) =>
            throttle < CrackedThrottle && Math.Abs(speed) > HarmlessSpeed && Math.Abs(reverser) < FreeReverser;

        /// <summary>The engine's health lost per second drifting so, 0 to 1.</summary>
        public static float Wear(float throttle, float reverser, float speed) =>
            HookedUp(throttle, reverser, speed)
                ? WearPerHour / 3600f * Math.Abs(speed) / FullHarmSpeed * (FreeReverser - Math.Abs(reverser)) / FreeReverser
                : 0f;
    }
}
