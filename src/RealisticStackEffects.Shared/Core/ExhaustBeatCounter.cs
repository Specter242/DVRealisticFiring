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

        /// <summary>Revolutions the crank turned between the last two readings, either way.</summary>
        public float Turned { get; private set; }

        /// <summary>How far the crank is past the last beat it crossed, 0 to 1 of a beat, the way it turns.</summary>
        public float SinceBeat { get; private set; }

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
            var change = angle - lastAngle;
            if (change < -0.5f)
            {
                revolutions++;
                change += 1f;
            }
            else if (change > 0.5f)
            {
                revolutions--;
                change -= 1f;
            }
            lastAngle = angle;
            var place = (revolutions + (double)angle) * beatsPerRevolution;
            var after = Math.Floor(place);
            Turned = Math.Abs(change);
            SinceBeat = (float)(change >= 0f ? place - after : 1.0 - (place - after));
            return (int)Math.Abs(after - before);
        }
    }
}
