using System;

namespace RealisticStackEffects.Core
{
    /// <summary>
    /// Counts exhaust beats from the crank angle. The stock engine reports a
    /// beat by changing a port's value, so beats in the same frame merge and a
    /// repeated index is missed; counting the beat boundaries the crank has
    /// crossed since the last reading catches every one, in either direction.
    /// </summary>
    public sealed class ExhaustBeatCounter
    {
        private readonly int beatsPerRevolution;
        private bool started;
        private float lastAngle;
        private int revolutions;

        /// <param name="beatsPerRevolution">Two per cylinder for a double-acting engine.</param>
        public ExhaustBeatCounter(int beatsPerRevolution)
        {
            if (beatsPerRevolution < 1)
                throw new ArgumentOutOfRangeException(nameof(beatsPerRevolution));
            this.beatsPerRevolution = beatsPerRevolution;
        }

        /// <summary>
        /// Takes the crank angle in revolutions (stock CRANK_ROTATION, 0 to 1)
        /// and returns the beats since the last reading. The crank must turn
        /// less than half a revolution between readings.
        /// </summary>
        public int Advance(float crankRotation)
        {
            var angle = crankRotation - (float)Math.Floor(crankRotation);
            if (!started)
            {
                started = true;
                lastAngle = angle;
                return 0;
            }

            var before = Math.Floor((revolutions + (double)lastAngle) * beatsPerRevolution);
            if (angle - lastAngle < -0.5f)
                revolutions++;
            else if (angle - lastAngle > 0.5f)
                revolutions--;
            lastAngle = angle;
            var after = Math.Floor((revolutions + (double)angle) * beatsPerRevolution);
            return (int)Math.Abs(after - before);
        }
    }
}
