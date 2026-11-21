using System.Runtime.CompilerServices;
using LocoSim.Implementations;
using RealisticFiring.Core;
using UnityEngine;

namespace RealisticFiring.Sim
{
 internal static class DriftingFittings
    {
        /// <summary>The drifting valve's control in the cab: saved, so a valve left open stays open.</summary>
        public static readonly StokerJetControls.Valve ValveControl = new StokerJetControls.Valve("rfDriftingValve", "Drifting valve", null, 0f);

        private static DriftingProfile ProfileOf(string livery) =>
            LocomotiveProfiles.TryGet(livery, out var profile) ? profile.Drifting
            : MakerFitRegistry.TryGetDriftingFittings(livery, out var maker) ? maker
            : DriftingProfile.None;

        /// <summary>Whether <paramref name="livery"/> buys the fittings as upgrades.</summary>
        public static bool Upgradable(string livery) => ProfileOf(livery).IsUpgradable;

        private static string Livery(TrainCar car) => car != null && car.carLivery != null ? car.carLivery.id : null;

        /// <summary>Whether <paramref name="car"/> has its snifting valves now.</summary>
        public static bool Snifting(TrainCar car)
        {
            var d = ProfileOf(Livery(car));
            return d.IsUpgradable ? Upgrades.Fittings.HasSnifting(car) : d.PrototypeSnifting;
        }

        /// <summary>Whether <paramref name="car"/> has its drifting valve now.</summary>
        public static bool Valve(TrainCar car)
        {
            var d = ProfileOf(Livery(car));
            return d.IsUpgradable ? Upgrades.Fittings.HasDriftingValve(car) : d.PrototypeValve;
        }

        /// <summary>Whether <paramref name="car"/> has its by-pass valves now.</summary>
        public static bool ByPass(TrainCar car)
        {
            var d = ProfileOf(Livery(car));
            return d.IsUpgradable ? Upgrades.Fittings.HasByPass(car) : d.PrototypeByPass;
        }

        /// <summary>Whether <paramref name="livery"/> may carry any drifting fitting, bought or its prototype's.</summary>
        public static bool MayHave(string livery)
        {
            var d = ProfileOf(livery);
            return d.IsUpgradable || d.HasPrototype;
        }

        /// <summary>Whether <paramref name="livery"/> can ever have a drifting valve: its sim graph then carries the valve's control.</summary>
        public static bool CanHaveValve(string livery)
        {
            var d = ProfileOf(livery);
            return d.IsUpgradable || d.PrototypeValve;
        }
    }

    /// <summary>
    /// A steam engine's drifting as its ports show it, on the host or a
    /// guest alike: its throttle, reverser, and speed, its cylinders' draw in
    /// this notch, and its drifting fittings and valve (<see cref="Drifting.State"/>).
    /// </summary>
    internal static class EngineDrift
    {
        private sealed class Links
        {
            public TrainCar Car;
            public Port Valve;
            public bool Looked;
        }

        private static readonly ConditionalWeakTable<ReciprocatingSteamEngine, Links> ByEngine = new ConditionalWeakTable<ReciprocatingSteamEngine, Links>();
        private static readonly ConditionalWeakTable<TrainCar, ReciprocatingSteamEngine> ByCar = new ConditionalWeakTable<TrainCar, ReciprocatingSteamEngine>();

        /// <summary>Ties <paramref name="engine"/> to its car, as the sim is built.</summary>
        public static void Register(TrainCar car, ReciprocatingSteamEngine engine)
        {
            if (car == null || engine == null)
                return;
            var links = ByEngine.GetValue(engine, _ => new Links());
            links.Car = car;
            links.Looked = false;
            ByCar.Remove(car);
            ByCar.Add(car, engine);
        }

        /// <summary>The steam engine of <paramref name="car"/>, if registered.</summary>
        public static ReciprocatingSteamEngine EngineOf(TrainCar car) => car != null && ByCar.TryGetValue(car, out var engine) ? engine : null;

        /// <summary>The car of <paramref name="engine"/>, if registered.</summary>
        public static TrainCar CarOf(ReciprocatingSteamEngine engine) => engine != null && ByEngine.TryGetValue(engine, out var links) ? links.Car : null;

        /// <summary>The drifting valve's opening, 0 to 1: 0 where it isn't fitted.</summary>
        public static float ValveOpening(TrainCar car, ReciprocatingSteamEngine engine)
        {
            if (car == null || engine == null || !DriftingFittings.Valve(car) || !ByEngine.TryGetValue(engine, out var links))
                return 0f;
            if (!links.Looked)
            {
                links.Looked = true;
                var flow = car.SimController != null ? car.SimController.simFlow : null;
                links.Valve = flow != null && flow.TryGetPort(DriftingFittings.ValveControl.PortId, out var port, canBeNullOrEmpty: true) ? port : null;
            }
            return links.Valve != null ? Mathf.Clamp01(links.Valve.Value) : 0f;
        }

        public static DriftFittings Fittings(TrainCar car, ReciprocatingSteamEngine engine) =>
            new DriftFittings(DriftingFittings.Snifting(car), DriftingFittings.ByPass(car), ValveOpening(car, engine));

        /// <summary>The engine's cut-off in its reverser's notch, as the game takes it.</summary>
        public static float Cutoff(ReciprocatingSteamEngine engine) =>
            Mathf.Lerp(engine.minCutoff, engine.maxCutoff, Mathf.Abs(engine.reverserControl.Value));

        /// <summary>The cylinders' draw in this notch at this speed, kg/s at the atmosphere.</summary>
        public static float Draw(ReciprocatingSteamEngine engine)
        {
            var volume = Mathf.PI / 4f * engine.cylinderBore * engine.cylinderBore * engine.pistonStroke * 1000f;
            return Drifting.Draw(Cutoff(engine), EngineCylinders.Spec.Clearance, volume, engine.numCylinders, engine.crankRpm.Value / 60f);
        }

        /// <summary>The drifting valve's steam now, kg/s, from the boiler's pressure at the throttle.</summary>
        public static float ValveSteam(TrainCar car, ReciprocatingSteamEngine engine) =>
            Drifting.ValveSteam(ValveOpening(car, engine), engine.intakePressure.Value);

        /// <summary><paramref name="car"/>'s drifting now.</summary>
        public static DriftState Read(TrainCar car, ReciprocatingSteamEngine engine)
        {
            if (car == null || engine == null)
                return DriftState.NotDrifting;
            return Drifting.State(engine.throttleControl.Value, engine.reverserControl.Value, car.GetForwardSpeed(),
                Draw(engine), ValveSteam(car, engine), Fittings(car, engine));
        }

        /// <summary>
        /// The steam the drifting valve takes from the boiler now, kg/s: what
        /// the cylinders draw through it while drifting, no more than fills
        /// the chests to <see cref="Drifting.MaxChest"/>; none standing, where
        /// the chests fill and it stops.
        /// </summary>
        public static float SteamUsed(TrainCar car)
        {
            var engine = EngineOf(car);
            if (engine == null)
                return 0f;
            var steam = ValveSteam(car, engine);
            if (!(steam > 0f) || !Drifting.State(engine.throttleControl.Value, engine.reverserControl.Value, car.GetForwardSpeed(),
                    1f, steam, DriftFittings.None).Drifting)
                return 0f;
            var draw = Draw(engine) * (DriftingFittings.ByPass(car) ? Drifting.ByPassDraw : 1f);
            return Mathf.Min(steam, draw * Drifting.MaxChest);
        }
    }
}
