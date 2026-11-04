using System;

namespace RealisticFiring.Core
{
    /// <summary>Sound gain only; never changes injector flow or state.</summary>
    public sealed class InjectorOverflowAudio
    {
        private const float WaterGain = 0.35f, SteamGain = 0.25f;
        private const float Attack = 0.12f, Release = 0.2f;
        private float waterLevel, steamLevel;
        public float Water { get; private set; }
        public float Steam { get; private set; }

        public void Step(float dt, float waterFlow, float steamFlow, float ratedWater, float ratedSteam, float volume)
        {
            waterLevel = Approach(waterLevel, Share(waterFlow, .3f * ratedWater), dt);
            steamLevel = Approach(steamLevel, Share(steamFlow, ratedSteam), dt);
            // Apply the master after smoothing: muting must be immediate.
            var master = Positive(volume);
            Water = WaterGain * waterLevel * master;
            Steam = SteamGain * steamLevel * master;
        }

        private static float Share(float flow, float capacity) =>
            Positive(capacity) > 0f ? Math.Min(1f, Positive(flow) / capacity) : 0f;

        private static float Positive(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Math.Max(0f, value);

        private static float Approach(float current, float target, float dt)
        {
            dt = Positive(dt);
            var value = target + (current - target) * (float)Math.Exp(-dt / (target > current ? Attack : Release));
            return target == 0f && value < .0001f ? 0f : value;
        }
    }
}
