using System;

namespace RealisticFiring.Core
{
    public static class Smoothing
    {
        /// <summary>
        /// Critically damped approach toward a target, with the same algorithm
        /// as UnityEngine.Mathf.SmoothDamp (no speed limit), so the model's
        /// dynamics match the stock firebox it replaces.
        /// </summary>
        public static float SmoothDamp(float current, float target, ref float velocity, float smoothTime, float deltaTime)
        {
            if (!(deltaTime > 0f))
                return current;

            smoothTime = Math.Max(0.0001f, smoothTime);
            var omega = 2f / smoothTime;
            var x = omega * deltaTime;
            var decay = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
            var change = current - target;
            var temp = (velocity + omega * change) * deltaTime;
            velocity = (velocity - omega * temp) * decay;
            var output = target + (change + temp) * decay;

            // Never overshoot the target.
            if (target - current > 0f == output > target)
            {
                output = target;
                velocity = (output - target) / deltaTime;
            }
            return output;
        }

        /// <summary>
        /// <see cref="SmoothDamp"/> that never moves away from the target.
        /// SmoothDamp keeps its momentum when the target reverses or the
        /// smoothing time grows (the startup speedup ending), which can carry a
        /// reading far past anything it was heading for; here that momentum is
        /// dropped, so the value stays between where it was and the target.
        /// </summary>
        public static float SmoothApproach(float current, float target, ref float velocity, float smoothTime, float deltaTime)
        {
            var output = SmoothDamp(current, target, ref velocity, smoothTime, deltaTime);
            if ((output - current) * (target - current) < 0f)
            {
                velocity = 0f;
                return current;
            }
            return output;
        }

        public static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;

        public static float Lerp(float from, float to, float t) => from + (to - from) * Clamp01(t);
    }
}
