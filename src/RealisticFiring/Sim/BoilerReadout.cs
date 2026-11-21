using HarmonyLib;
using LocoSim.Implementations;

namespace RealisticFiring.Sim
{
    /// <summary>
    /// A boiler's pressure and water as this machine knows them. The host
    /// simulates the boiler and reads its stock vessel. On a multiplayer
    /// guest that vessel is a shadow never ticked (the Multiplayer mod skips
    /// a guest's stock ticks; <see cref="Patches.BoilerTickPatch"/>), so the
    /// host's figures are read from the boiler's read-out ports, which the
    /// Multiplayer mod copies from the host. The game sets those ports from
    /// the vessel every tick (<c>Boiler.SimulateEvaporation</c>,
    /// <c>SimulateWaterLevel</c>), in the vessel's own units: absolute bar,
    /// °C, and kg. Read from the vessel on a guest, the F6 and tablet boiler
    /// gauge and its water temperature stood still (DV Discord, 2026-11-21).
    /// </summary>
    internal static class BoilerReadout
    {
        private static readonly AccessTools.FieldRef<Boiler, WaterPressureVessel> Vessel =
            AccessTools.FieldRefAccess<Boiler, WaterPressureVessel>("vessel");

        /// <summary>The boiler's pressure, absolute bar; the atmosphere's with no vessel, or on a guest before the host's first reading.</summary>
        public static float Pressure(Boiler boiler)
        {
            if (Net.IsClient)
            {
                // An absolute pressure is never nothing: a port at 0 has not
                // heard from the host yet.
                var port = boiler.pressureReadOut.Value;
                return port > 0f ? port : 1f;
            }
            return Vessel(boiler)?.pressure ?? 1f;
        }

        /// <summary>The boiler's water, °C; <paramref name="none"/> with no vessel.</summary>
        public static float WaterTemperature(Boiler boiler, float none) =>
            Net.IsClient ? boiler.temperatureReadOut.Value : Vessel(boiler)?.waterTemp ?? none;

        /// <summary>The water and steam in the boiler, kg; nothing with no vessel.</summary>
        public static float Mass(Boiler boiler) =>
            Net.IsClient ? boiler.waterMassReadOut.Value : Vessel(boiler)?.mass ?? 0f;
    }
}
