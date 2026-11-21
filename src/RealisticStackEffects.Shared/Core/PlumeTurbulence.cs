using System;

namespace RealisticStackEffects.Core
{
    /// <summary>
    /// Divergence-free 3D turbulent eddy field and slow horizontal meander for
    /// the chimney plume. The eddy velocity is the analytical curl of a smooth
    /// height-enveloped vector potential v = curl(A(y) * Psi(x, y, z, t)) with
    /// fixed spatial wavenumbers, so div(v) == 0 analytically everywhere
    /// (including through the stack-lip ramp and height decay) with zero
    /// coordinate-dependent phase jitter, zero heap allocations, and no
    /// particle pinching or clumping.
    /// </summary>
    public static class PlumeTurbulence
    {
        /// <summary>Height over which curl eddies ramp smoothly in above the stack lip, in m.</summary>
        public const float EddyLipRamp = 6f;

        /// <summary>Reference height scale for buoyant rise-speed decay in the eddy envelope, in m.</summary>
        public const float EddyHeightScale = 3f;

        /// <summary>Eddy velocity scale as a share of the plume's lip rise speed.</summary>
        public const float EddyStir = 0.6f;

        /// <summary>Time drift scale of the eddy modes.</summary>
        public const float EddyDrift = 0.2f;

        /// <summary>Height over which slow crosswind meander ramps in above the lip, in m.</summary>
        public const float MeanderRampHeight = 8f;

        /// <summary>Gentle horizontal meander speed in calm air, in m/s.</summary>
        public const float CalmMeander = 0.18f;

        /// <summary>Crosswind meander amplitude as a share of wind speed.</summary>
        public const float WindMeanderShare = 0.14f;

        /// <summary>Wind speed above which meander amplitude stops growing, in m/s.</summary>
        public const float MaxMeanderWind = 6f;

        private readonly struct Mode
        {
            public readonly double Kx, Ky, Kz;
            public readonly double Px, Py, Pz;
            public readonly double Ax, Ay, Az;
            public readonly double Omega;
            public readonly double Phase;

            public Mode(double kx, double ky, double kz, double bx, double by, double bz, double weight, double omega, double phase)
            {
                Kx = kx;
                Ky = ky;
                Kz = kz;
                // Vector potential mode Psi = p * cos(k · x + omega * t + phase)
                // has curl(Psi) = (p x k) * sin(phi) = a * sin(phi), with k · a == 0.
                // Scale p so |a| == weight.
                var cx = by * kz - bz * ky;
                var cy = bz * kx - bx * kz;
                var cz = bx * ky - by * kx;
                var scale = weight / Math.Sqrt(cx * cx + cy * cy + cz * cz);
                Px = bx * scale;
                Py = by * scale;
                Pz = bz * scale;
                Ax = cx * scale;
                Ay = cy * scale;
                Az = cz * scale;
                Omega = omega;
                Phase = phase;
            }
        }

        // Four oblique, incommensurate fixed-wavelength 3D modes spanning broad
        // aloft eddies (~12 m, ~7 m) down to finer near-stack eddies (~4 m, ~2.5 m).
        // The two broad modes use purely vertical potentials (Px = Pz = 0),
        // keeping eddies horizontal-dominant and vertical overturning gentle while
        // keeping the envelope-derivative term A'(y) * (Psi_z, 0, -Psi_x) small.
        private static readonly Mode Mode0 = new Mode(0.11, 0.08, -0.09, 0.0, 1.0, 0.0, 0.30, 0.18, 0.4);
        private static readonly Mode Mode1 = new Mode(-0.17, 0.13, 0.16, 0.0, 1.0, 0.0, 0.28, -0.25, 2.1);
        private static readonly Mode Mode2 = new Mode(0.31, 0.19, 0.28, 0.16, 0.96, -0.22, 0.20, 0.34, 4.3);
        private static readonly Mode Mode3 = new Mode(0.43, -0.48, -0.44, 0.38, 0.80, 0.46, 0.14, -0.43, 1.2);

        /// <summary>
        /// Smooth C² height amplitude envelope A(<paramref name="height"/>) and
        /// its exact derivative A'(<paramref name="height"/>) with respect to
        /// height above the stack lip, in m: zero with zero slope at and below
        /// the lip, ramping smoothly over <see cref="EddyLipRamp"/> m, and
        /// decaying gently aloft as (1 + height / <see cref="EddyHeightScale"/>)^(-1/3).
        /// </summary>
        public static void Envelope(float height, out double amplitude, out double derivative)
        {
            if (!(height > 0f) || float.IsInfinity(height))
            {
                amplitude = 0.0;
                derivative = 0.0;
                return;
            }
            var h = (double)height;
            var decay = Math.Pow(1.0 + h / EddyHeightScale, -1.0 / 3.0);
            var decayDeriv = -decay / (3.0 * (EddyHeightScale + h));
            if (h >= EddyLipRamp)
            {
                amplitude = decay;
                derivative = decayDeriv;
                return;
            }
            var u = h / EddyLipRamp;
            var u2 = u * u;
            var oneMinusU = 1.0 - u;
            // Quintic smootherstep: C² continuous at both u = 0 and u = 1.
            var ramp = u2 * u * (u * (6.0 * u - 15.0) + 10.0);
            var rampDeriv = 30.0 * u2 * oneMinusU * oneMinusU / EddyLipRamp;
            amplitude = ramp * decay;
            derivative = rampDeriv * decay + ramp * decayDeriv;
        }

        /// <summary>
        /// Evaluates the bounded, analytically divergence-free 3D curl velocity
        /// field v = curl(A(<paramref name="height"/>) * Psi(x, height, z, t))
        /// at horizontal coordinates (<paramref name="x"/>, <paramref name="z"/>),
        /// <paramref name="height"/> m above the stack lip, and
        /// <paramref name="time"/> s. Zero at and below the stack lip.
        /// </summary>
        public static void Curl(float x, float height, float z, float time, out float vx, out float vy, out float vz)
        {
            if (!(height > 0f) || !IsFinite(x) || !IsFinite(height) || !IsFinite(z) || !IsFinite(time))
            {
                vx = 0f;
                vy = 0f;
                vz = 0f;
                return;
            }

            Envelope(height, out var amp, out var dAmp);

            var px = (double)x;
            var py = (double)height;
            var pz = (double)z;
            var t = (double)time * EddyDrift;

            var p0 = Mode0.Kx * px + Mode0.Ky * py + Mode0.Kz * pz + Mode0.Omega * t + Mode0.Phase;
            var p1 = Mode1.Kx * px + Mode1.Ky * py + Mode1.Kz * pz + Mode1.Omega * t + Mode1.Phase;
            var p2 = Mode2.Kx * px + Mode2.Ky * py + Mode2.Kz * pz + Mode2.Omega * t + Mode2.Phase;
            var p3 = Mode3.Kx * px + Mode3.Ky * py + Mode3.Kz * pz + Mode3.Omega * t + Mode3.Phase;

            var s0 = Math.Sin(p0);
            var s1 = Math.Sin(p1);
            var s2 = Math.Sin(p2);
            var s3 = Math.Sin(p3);

            // Modes 0 and 1 have Px == Pz == 0, so only modes 2 and 3 contribute to Psi_x and Psi_z.
            var c2 = Math.Cos(p2);
            var c3 = Math.Cos(p3);
            var psiX = Mode2.Px * c2 + Mode3.Px * c3;
            var psiZ = Mode2.Pz * c2 + Mode3.Pz * c3;

            var curlX = Mode0.Ax * s0 + Mode1.Ax * s1 + Mode2.Ax * s2 + Mode3.Ax * s3;
            var curlY = Mode0.Ay * s0 + Mode1.Ay * s1 + Mode2.Ay * s2 + Mode3.Ay * s3;
            var curlZ = Mode0.Az * s0 + Mode1.Az * s1 + Mode2.Az * s2 + Mode3.Az * s3;

            vx = (float)(amp * curlX + dAmp * psiZ);
            vy = (float)(amp * curlY);
            vz = (float)(amp * curlZ - dAmp * psiX);
        }

        /// <summary>
        /// Slow horizontal meander velocity (<paramref name="mx"/>,
        /// <paramref name="mz"/>) in m/s at <paramref name="time"/> s and
        /// <paramref name="height"/> m above the stack, for horizontal wind
        /// (<paramref name="windX"/>, <paramref name="windZ"/>) in m/s.
        /// Weaves crosswind when breezy and wanders gently when calm; zero at
        /// the stack lip, independent of horizontal position so its 3D
        /// divergence is zero.
        /// </summary>
        public static void Meander(float time, float height, float windX, float windZ, out float mx, out float mz)
        {
            if (!(height > 0f))
            {
                mx = 0f;
                mz = 0f;
                return;
            }
            var ramp = SmoothStep(0f, MeanderRampHeight, height);
            var t = (double)time;
            var h = (double)height;

            // Gentle 2D wander when calm (periods ~57 s, ~134 s and ~65 s, ~103 s).
            var wanderX = 0.65 * Math.Sin(t * 0.11 + h * 0.012) + 0.35 * Math.Sin(t * 0.047 + 2.3);
            var wanderZ = 0.65 * Math.Cos(t * 0.097 - h * 0.011 + 0.7) + 0.35 * Math.Sin(t * 0.061 + 4.1);
            var calmX = CalmMeander * ramp * (float)wanderX;
            var calmZ = CalmMeander * ramp * (float)wanderZ;

            var windSpeed = (float)Math.Sqrt(windX * windX + windZ * windZ);
            if (!(windSpeed > 1e-4f))
            {
                mx = calmX;
                mz = calmZ;
                return;
            }

            // Crosswind serpentine weave (periods ~48 s and ~110 s).
            var crossWave = 0.65 * Math.Sin(t * 0.13 + h * 0.015) + 0.35 * Math.Sin(t * 0.057 - h * 0.008 + 1.9);
            var cx = -windZ / windSpeed;
            var cz = windX / windSpeed;
            var crossAmp = (CalmMeander + WindMeanderShare * Math.Min(windSpeed, MaxMeanderWind)) * ramp;
            var windMx = cx * (crossAmp * (float)crossWave);
            var windMz = cz * (crossAmp * (float)crossWave);

            var blend = SmoothStep(0.1f, 0.8f, windSpeed);
            mx = calmX + (windMx - calmX) * blend;
            mz = calmZ + (windMz - calmZ) * blend;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static float SmoothStep(float edge0, float edge1, float value)
        {
            var t = Clamp01((value - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
    }
}
