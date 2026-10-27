using System.Runtime.CompilerServices;
using LocoSim.Implementations;
using RealisticStackEffects;
using RealisticStackEffects.Api;
using UnityModManagerNet;

[assembly: InternalsVisibleTo("RealisticFiring.AdapterCheck")]

namespace RealisticFiring.StackEffects
{
 public static class Integration
    {
        /// <summary>Registers the realistic fires; false if the installed Realistic Stack Effects is too old.</summary>
        public static bool Start(UnityModManager.ModEntry mod)
        {
            if (StackSources.ApiVersion < 1)
                return false;
            StackSources.Register(mod.Info.Id, car => StackEffectsLink.FireFor(car) is IStackFire fire ? new Source(fire) : null);
            return true;
        }
    }

    /// <summary>A realistic fire, read for Realistic Stack Effects.</summary>
    internal sealed class Source : IStackFireSource
    {
        private readonly IStackFire fire;

        public Source(IStackFire fire) => this.fire = fire;

        public bool Tended => fire.Tended;

        public void Read(float dt, SteamExhaust exhaust, ref StackFireReading reading)
        {
            var from = default(StackReading);
            fire.Read(dt, exhaust, ref from);
            reading.FireOn = from.FireOn;
            reading.Heat = from.Heat;
            reading.MostHeat = from.MostHeat;
            reading.Soot = from.Soot;
            reading.FireboxGas = from.FireboxGas;
            reading.FireboxSmokeOut = from.FireboxSmokeOut;
            reading.ClearStack = from.ClearStack;
            reading.AirThrough = from.AirThrough;
            reading.Cinders = from.Cinders;
            reading.Fines = from.Fines;
            reading.ShakenCoal = from.ShakenCoal;
            reading.PlugSteam = from.PlugSteam;
            reading.ExhaustWetness = from.ExhaustWetness;
            reading.StackSteam = from.StackSteam;
            reading.BlowerSteam = from.BlowerSteam;
            reading.NaturalFlow = from.NaturalFlow;
        }
    }
}
