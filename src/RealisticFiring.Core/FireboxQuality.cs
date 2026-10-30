using System;

namespace RealisticFiring.Core
{
    /// <summary>Detail of the flames and smoke inside the firebox, independent of chimney smoke.</summary>
    public enum FireboxQuality
    {
        High,
        Medium,
        Low,
    }

    /// <summary>Coal geometry and visible surface-upload budgets, independent of firelight.</summary>
    public readonly struct FireboxDetail
    {
        public int LumpStride { get; }
        public float UploadInterval { get; }

        private FireboxDetail(int lumpStride, float uploadInterval)
        {
            LumpStride = lumpStride;
            UploadInterval = uploadInterval;
        }

        public static FireboxDetail For(FireboxQuality quality) =>
            quality == FireboxQuality.Low ? new FireboxDetail(4, 0.1f)
            : quality == FireboxQuality.Medium ? new FireboxDetail(2, 0.05f)
            : new FireboxDetail(1, 0f);
    }

    /// <summary>
    /// A ray's sample limit and target spacing in meters. The shader always
    /// covers the whole visible ray, spreading its samples farther apart
    /// when it reaches the limit. Density and emission stay per meter.
    /// </summary>
    public readonly struct FireboxSampling
    {
        public float MaxSteps { get; }
        public float StepLength { get; }

        private FireboxSampling(float maxSteps, float stepLength)
        {
            MaxSteps = maxSteps;
            StepLength = stepLength;
        }

        /// <summary>
        /// High preserves the original open-door, in-box, and shut-door
        /// budgets. Medium and Low halve and quarter the open fire's
        /// samples, including short rays. Shut doors keep their existing
        /// small budget so the fire seen through their holes stays intact.
        /// The final two arguments retain development test-bed tuning.
        /// </summary>
        public static FireboxSampling For(FireboxQuality quality, bool doorClosed, bool cameraInside,
            float highSteps = 128f, float highStepLength = 0.02f)
        {
            if (doorClosed)
                return new FireboxSampling(24f, Math.Max(highStepLength, 0.06f));

            var divisor = quality == FireboxQuality.Medium ? 2f : quality == FireboxQuality.Low ? 4f : 1f;
            var steps = Math.Max(4f, Math.Min(128f, cameraInside ? 0.5f * highSteps : highSteps));
            return new FireboxSampling(Math.Max(4f, steps / divisor), highStepLength * divisor);
        }
    }
}
