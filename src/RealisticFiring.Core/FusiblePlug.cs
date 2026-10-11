using System;

namespace RealisticFiring.Core
{
 public sealed class FusiblePlug
    {
        /// <summary>Pure tin's melting point, in °C.</summary>
        public const float MeltingPoint = 232f;

        /// <summary>The plug's tin core follows the crown sheet, or the water over it, with this time constant, in s.</summary>
        public const float CoreTime = 10f;

        /// <summary>Steam and water blown through the bore per bar over atmospheric, in kg/s (a ½-inch bore).</summary>
        public const float VentPerBar = 0.08f;

        /// <summary>A blow of this much, in kg/s, drowns the fire outright.</summary>
        public const float DrowningVent = 0.15f;

        public FusiblePlug(float temperature)
        {
            Renew(temperature);
        }

        /// <summary>The tin core's temperature, in °C.</summary>
        public float CoreTemperature { get; private set; }

        public bool Melted { get; private set; }

        /// <summary>Fits a new plug, at <paramref name="temperature"/>.</summary>
        public void Renew(float temperature)
        {
            CoreTemperature = temperature;
            Melted = false;
        }

        /// <summary>Restores a saved plug.</summary>
        public void Restore(bool melted, float temperature)
        {
            CoreTemperature = temperature;
            Melted = melted;
        }

        /// <summary>
        /// Heats the core toward the crown sheet while <paramref name="bared"/>,
        /// else cools it toward the water. Returns true on the tick it melts.
        /// </summary>
        public bool Step(float dt, bool bared, float crownSheetTemperature, float waterTemperature)
        {
            if (Melted || !(dt > 0f))
                return false;
            var target = bared ? Math.Max(crownSheetTemperature, waterTemperature) : waterTemperature;
            CoreTemperature += (target - CoreTemperature) * Math.Min(1f, dt / CoreTime);
            if (CoreTemperature < MeltingPoint)
                return false;
            Melted = true;
            return true;
        }

        /// <summary>What blows through the melted plug at <paramref name="pressure"/> (absolute bar), in kg/s.</summary>
        public float Vent(float pressure) => Melted ? VentPerBar * Math.Max(0f, pressure - 1f) : 0f;

        /// <summary>How the blow drowns the fire, 0 to 1, as the firebox's intake water: over 0.5 puts it out.</summary>
        public float Drowning(float pressure) => Smoothing.Clamp01(Vent(pressure) / DrowningVent);
    }

    /// <summary>
    /// A low-water alarm: a whistle that blows while less than an inch of
    /// water shows in the glass (NYSED p.54), kept a little above the crown
    /// sheet so a surge does not stop and start it.
    /// </summary>
    public sealed class LowWaterAlarm
    {
        /// <summary>It blows with the water this far above the crown sheet, in boiler fractions.</summary>
        public const float Margin = 0.06f;

        /// <summary>It stops once the water is this much higher again.</summary>
        public const float Hysteresis = 0.01f;

        public bool Sounding { get; private set; }

        /// <summary>Shows another alarm's state, as a multiplayer client shows the host's.</summary>
        public void Mirror(bool sounding) => Sounding = sounding;

        public bool Step(float level, float crownSheetLevel)
        {
            var threshold = crownSheetLevel + Margin;
            Sounding = Sounding ? level < threshold + Hysteresis : level < threshold;
            return Sounding;
        }
    }
}
