using System.Globalization;

namespace RealisticFiring.Core
{
    /// <summary>What stops the boiler hand pump from putting water in while its key is held.</summary>
    public enum HandPumpStop
    {
        /// <summary>Nothing: it pumps.</summary>
        None,

        /// <summary>The player is on no engine with a hand pump, nor its tender.</summary>
        NotAboard,

        /// <summary>The boiler blew up; the engine wants repairing.</summary>
        BoilerBroken,

        /// <summary>A shed stand's water hose is filling the boiler.</summary>
        HoseFilling,

        /// <summary>Too much pressure in the boiler for a hand pump to force water against.</summary>
        Pressure,

        /// <summary>An engine that takes its water from a tender, with none coupled.</summary>
        NoTender,

        /// <summary>No water to lift: the tender or tanks are empty, or the tender's outlet is uncovered.</summary>
        NoWater,

        /// <summary>No room left in the boiler.</summary>
        BoilerFull,
    }

    /// <summary>
    /// The boiler hand pump's rules, and what the player is told when it does
    /// nothing (a Nexus bug report in the 2026-11-21 audit: "the Hand Pump
    /// just.. Doesn't wanna cooperate"). Held, it lifts water from the tender
    /// into a boiler that is not broken, has room, and stands at no more than
    /// its pressure, unless a shed stand's hose is filling it already.
    /// </summary>
    public static class HandPump
    {
        /// <summary>
        /// What stops it, the first that applies, in the order the boiler's
        /// step takes them; <see cref="HandPumpStop.None"/> if it pumps.
        /// </summary>
        /// <param name="broken">The boiler is broken.</param>
        /// <param name="hoseFilling">A shed hose fills it this tick, in the pump's place.</param>
        /// <param name="pressure">Boiler pressure, absolute bar.</param>
        /// <param name="mostPressure">The most it pumps against, absolute bar.</param>
        /// <param name="noTender">The engine takes its water from a tender and has none coupled.</param>
        /// <param name="water">Water it can lift, kg (none when the tender's outlet is uncovered).</param>
        /// <param name="room">Room left in the boiler, kg.</param>
        public static HandPumpStop Stop(bool broken, bool hoseFilling, float pressure, float mostPressure, bool noTender, float water, float room)
        {
            if (broken)
                return HandPumpStop.BoilerBroken;
            if (hoseFilling)
                return HandPumpStop.HoseFilling;
            if (pressure > mostPressure)
                return HandPumpStop.Pressure;
            if (noTender)
                return HandPumpStop.NoTender;
            if (!(water > 0f))
                return HandPumpStop.NoWater;
            if (!(room > 0f))
                return HandPumpStop.BoilerFull;
            return HandPumpStop.None;
        }

        /// <summary>
        /// The notice for a press that pumps nothing; null if it pumps.
        /// </summary>
        /// <param name="mostGauge">The most it pumps against, gauge bar.</param>
        /// <param name="ownTanks">A tank engine, carrying its own water.</param>
        public static string Notice(HandPumpStop stop, float mostGauge, bool ownTanks = false)
        {
            switch (stop)
            {
                case HandPumpStop.NotAboard:
                    return "No hand pump here: board a steam loco or its tender";
                case HandPumpStop.BoilerBroken:
                    return "The hand pump can't fill a broken boiler: have the loco repaired";
                case HandPumpStop.HoseFilling:
                    return "The shed's water hose is already filling the boiler";
                case HandPumpStop.Pressure:
                    return $"The hand pump cannot force water in above {Bar(mostGauge)} bar";
                case HandPumpStop.NoTender:
                    return "The hand pump has no water: couple the tender";
                case HandPumpStop.NoWater:
                    return ownTanks ? "The hand pump has no water: fill the tanks" : "The hand pump has no water: fill the tender";
                case HandPumpStop.BoilerFull:
                    return "The boiler is full: the hand pump can't put in more";
                default:
                    return null;
            }
        }

        /// <summary>The Water page's row while it is worked: its rate, or what stops it.</summary>
        /// <param name="rate">Water it put in on the last tick, kg/s.</param>
        public static string Row(HandPumpStop stop, float rate, float mostGauge)
        {
            switch (stop)
            {
                case HandPumpStop.BoilerBroken:
                    return "boiler broken";
                case HandPumpStop.HoseFilling:
                    return "hose filling";
                case HandPumpStop.Pressure:
                    return $"over {Bar(mostGauge)} bar";
                case HandPumpStop.NoTender:
                    return "no tender";
                case HandPumpStop.NoWater:
                    return "no water";
                case HandPumpStop.BoilerFull:
                    return "boiler full";
                default:
                    return rate.ToString("F1", CultureInfo.CurrentCulture) + " kg/s";
            }
        }

        private static string Bar(float gauge) => gauge.ToString("F0", CultureInfo.CurrentCulture);
    }
}
