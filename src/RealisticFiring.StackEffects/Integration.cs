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
            // The steam around the engine came with the contract's second
            // version: an older Realistic Stack Effects has no such member,
            // and a method that names it must not be compiled against it.
            if (StackSources.ApiVersion >= 2)
                LinkEngineSteam();
            // Optional stack geometry hints came with the contract's third
            // version: guarded and NoInlining so Realistic Firing stays
            // backwards-compatible with Realistic Stack Effects v1 and v2.
            if (StackSources.ApiVersion >= 3)
                LinkStackGeometry(mod.Info.Id);
            return true;
        }

        // The mod's own steam sources (a booster's, an injector's overflow)
        // drawn with Realistic Stack Effects' steam around the engine.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void LinkEngineSteam()
        {
            Visuals.EngineVents.External = car =>
            {
                var steam = StackSources.EngineSteam(car);
                if (steam == null)
                    return null;
                return (steam, (name, kind, at, facing, strength) => steam.Add(name, kind.ToString(), at, facing, strength));
            };
        }

        // Optional chimney geometry hints from built-in locomotive profiles
        // and loaded maker fits, registered with Realistic Stack Effects v3+.
        // The delegate made here, not by a method group: the compiler caches a
        // method group's delegate in a static field of a hidden class, typed with
        // the v3 StackGeometry, and with Realistic Stack Effects 1.2.2 that class
        // can't load, so every scan of this assembly's types failed (the game's
        // console at start, with a crash report; job 3117).
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void LinkStackGeometry(string modId)
        {
            StackSources.RegisterGeometry(modId, new System.Func<TrainCar, StackGeometry?>(GeometryFor));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static StackGeometry? GeometryFor(TrainCar car)
        {
            var hint = StackEffectsLink.StackGeometryFor(car);
            if (!hint.HasValue)
                return null;
            return new StackGeometry
            {
                LipHeight = hint.Value.LipHeight,
                BoreRadius = hint.Value.BoreRadius,
                OutsideRadius = hint.Value.OutsideRadius,
            };
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
