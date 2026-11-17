using System.Collections.Generic;
using System.Runtime.CompilerServices;
using LocoSim.Implementations;
using RealisticFiring.Core;
using UnityEngine;

namespace RealisticFiring.Sim
{
 internal static class DriftingFittings
    {
        /// <summary>A prototype's drifting fittings, fitted always.</summary>
        internal readonly struct Equipment
        {
            public Equipment(bool snifting, bool valve, bool byPass)
            {
                Snifting = snifting;
                Valve = valve;
                ByPass = byPass;
            }

            public bool Snifting { get; }
            public bool Valve { get; }
            public bool ByPass { get; }
        }

        /// <summary>The engines that buy them: the stock S282 and S060.</summary>
        private static readonly HashSet<string> Upgraded = new HashSet<string> { "LocoS282A", "LocoS060" };

        /// <summary>
        /// What the other supported engines' prototypes carried, by livery,
        /// from the research of 2026-11-15
        /// (development/drifting-fittings-20261115/research.md): the verified
        /// and the likely; no fittings found on the NYC J-3a, the JK3 (no
        /// prototype), or BorING's narrow-gauge engines (slide valves and
        /// saturated steam; at most a relief valve).
        /// </summary>
        private static readonly Dictionary<string, Equipment> Prototype = new Dictionary<string, Equipment>
        {
            // The Santa Fe 2900 class's Wagner by-pass valves (called drifting
            // valves, but admitting no steam), verified for the class.
            ["A.T.&S.F. 2901-A"] = new Equipment(snifting: false, valve: false, byPass: true),
            // The DR class 95's automatic pressure equalizers (Druckausgleich),
            // verified; their type unknown.
            ["S2102-720"] = new Equipment(snifting: false, valve: false, byPass: true),
            ["S2102-870"] = new Equipment(snifting: false, valve: false, byPass: true),
            // The LMS 8F's snifting valves by the cylinders and the 4F's at the
            // smokebox's foot, likely (BR later took many off; when, unknown).
            ["em_lms_8f_loc_black"] = new Equipment(snifting: true, valve: false, byPass: false),
            ["em_lms_8f_loc_grey"] = new Equipment(snifting: true, valve: false, byPass: false),
            ["em_fowler_4f_44422"] = new Equipment(snifting: true, valve: false, byPass: false),
            ["em_fowler_4f_4027"] = new Equipment(snifting: true, valve: false, byPass: false),
        };

        /// <summary>The drifting valve's control in the cab: saved, so a valve left open stays open.</summary>
        public static readonly StokerJetControls.Valve ValveControl = new StokerJetControls.Valve("rfDriftingValve", "Drifting valve", null, 0f);

        /// <summary>Whether <paramref name="livery"/> buys the fittings as upgrades.</summary>
        public static bool Upgradable(string livery) => livery != null && Upgraded.Contains(livery);

        private static string Livery(TrainCar car) => car != null && car.carLivery != null ? car.carLivery.id : null;

        /// <summary>Whether <paramref name="car"/> has its snifting valves now.</summary>
        public static bool Snifting(TrainCar car)
        {
            var livery = Livery(car);
            return Upgradable(livery) ? Upgrades.Fittings.Snifting : livery != null && Prototype.TryGetValue(livery, out var e) && e.Snifting;
        }

        /// <summary>Whether <paramref name="car"/> has its drifting valve now.</summary>
        public static bool Valve(TrainCar car)
        {
            var livery = Livery(car);
            return Upgradable(livery) ? Upgrades.Fittings.DriftingValve : livery != null && Prototype.TryGetValue(livery, out var e) && e.Valve;
        }

        /// <summary>Whether <paramref name="car"/> has its by-pass valves now.</summary>
        public static bool ByPass(TrainCar car)
        {
            var livery = Livery(car);
            return Upgradable(livery) ? Upgrades.Fittings.ByPass : livery != null && Prototype.TryGetValue(livery, out var e) && e.ByPass;
        }

        /// <summary>Whether <paramref name="livery"/> may carry any drifting fitting, bought or its prototype's.</summary>
        public static bool MayHave(string livery) => Upgradable(livery) || livery != null && Prototype.ContainsKey(livery);

        /// <summary>Whether <paramref name="livery"/> can ever have a drifting valve: its sim graph then carries the valve's control.</summary>
        public static bool CanHaveValve(string livery) =>
            Upgradable(livery) || livery != null && Prototype.TryGetValue(livery, out var e) && e.Valve;
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
