namespace RealisticFiring.Core
{
    /// <summary>What the rest of the locomotive feeds the fire each tick.</summary>
    public struct FireInputs
    {
        /// <summary>Draft from the exhaust (stock AIR_FLOW), in kg/s of air through a clean, even bed.</summary>
        public float AirFlow;

        /// <summary>Water fraction carried into the firebox (stock INTAKE_WATER_CONTENT), 0 to 1.</summary>
        public float IntakeWater;

        /// <summary>Firebox door opening, 0 closed to 1 fully open.</summary>
        public float DoorOpening;

        /// <summary>Stock cold-start speedup; 1 in normal running.</summary>
        public float StartupMultiplier;

        /// <summary>Boiler temperature the cold firebox settles to, in °C.</summary>
        public float BoilerTemperature;

        /// <summary>Train speed, in m/s; its shaking works coal down a sloping grate.</summary>
        public float Speed;
    }
}
