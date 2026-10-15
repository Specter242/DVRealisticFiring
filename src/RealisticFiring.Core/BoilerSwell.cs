using System;

namespace RealisticFiring.Core
{
    /// <summary>What the boiler's swell reads each step.</summary>
    public struct BoilerSwellInputs
    {
        /// <summary>The fire's heat into the water, W.</summary>
        public float Heat;

        /// <summary>The heat that boils a kg at the boiler's pressure, J/kg.</summary>
        public float LatentHeat;

        /// <summary>The engine's full steam, kg/s: the steaming the swell is measured against; none, no swell from steaming.</summary>
        public float FullSteam;

        /// <summary>Water fed this step, kg.</summary>
        public float Fed;

        /// <summary>The heat the feed takes to reach boiling, J/kg.</summary>
        public float FeedSubcooling;

        /// <summary>The boiler's pressure, bar.</summary>
        public float Pressure;

        /// <summary>Whether the water is boiling; below boiling it holds no steam.</summary>
        public bool Boiling;
    }

 public sealed class BoilerSwell
    {
        /// <summary>The glass's rise steaming at the engine's full steam, in boiler fractions (inferred: a few inches).</summary>
        public const float FullSwell = 0.05f;

        /// <summary>The glass's rise for each bar a second the pressure falls, in boiler fractions; it shrinks as fast as it rises.</summary>
        public const float FlashPerBarPerSecond = 0.2f;

        /// <summary>The most a change of pressure moves the glass, in boiler fractions.</summary>
        public const float MostFlash = 0.04f;

        /// <summary>How fast the steam in the water follows, s, and the pressure's change is read over, s.</summary>
        public const float FollowTime = 3f;
        public const float RateTime = 2f;

        private float lastPressure = float.NaN;

        /// <summary>How far the glass stands above the water alone, in boiler fractions.</summary>
        public float Rise { get; private set; }

        /// <summary>The pressure's change, bar/s, smoothed.</summary>
        public float PressureRate { get; private set; }

        public void Step(float dt, BoilerSwellInputs inputs)
        {
            if (!(dt > 0f))
                return;
            var rate = float.IsNaN(lastPressure) ? 0f : (inputs.Pressure - lastPressure) / dt;
            lastPressure = inputs.Pressure;
            PressureRate += (rate - PressureRate) * Math.Min(1f, dt / RateTime);

            var target = 0f;
            if (inputs.Boiling && inputs.LatentHeat > 0f)
            {
                // The steam made in the water, less what cold feed condenses,
                // against the engine's full steam.
                var made = Math.Max(0f, inputs.Heat) / inputs.LatentHeat;
                var condensed = Math.Max(0f, inputs.Fed) / dt * Math.Max(0f, inputs.FeedSubcooling) / inputs.LatentHeat;
                var steaming = inputs.FullSteam > 0f ? Smoothing.Clamp01((made - condensed) / inputs.FullSteam) : 0f;
                var flash = Math.Max(-MostFlash, Math.Min(MostFlash, -PressureRate * FlashPerBarPerSecond));
                target = Math.Max(0f, FullSwell * steaming + flash);
            }
            Rise += (target - Rise) * Math.Min(1f, dt / FollowTime);
        }

        /// <summary>A boiler made anew: no steam in the water, the pressure read afresh.</summary>
        public void Reset()
        {
            Rise = 0f;
            PressureRate = 0f;
            lastPressure = float.NaN;
        }
    }
}
