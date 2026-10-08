namespace RealisticFiring.Core
{
    /// <summary>One grate zone. Masses are in kg and never negative.</summary>
    public sealed class ZoneState
    {
        internal float RateVelocity;

        /// <summary>Coal that has not caught yet.</summary>
        public float Fresh { get; internal set; }

        /// <summary>Coal that is alight.</summary>
        public float Burning { get; internal set; }

        /// <summary>Loose ash that shaking can clear.</summary>
        public float Ash { get; internal set; }

        /// <summary>Fused ash that only dumping the fire clears.</summary>
        public float Clinker { get; internal set; }

        public float Coal => Fresh + Burning;

        /// <summary>Primary air through this zone last tick, in kg/s.</summary>
        public float AirFlow { get; internal set; }

        /// <summary>Burning-coal combustion rate before any startup speedup, in kg/s.</summary>
        public float CombustionRate { get; internal set; }

        internal void Clear()
        {
            Fresh = Burning = Ash = Clinker = 0f;
            AirFlow = CombustionRate = RateVelocity = 0f;
        }
    }
}
