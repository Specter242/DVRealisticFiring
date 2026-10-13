using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DV.Simulation.Cars;
using LocoSim.Definitions;
using LocoSim.Implementations;
using RealisticFiring.Core;
using UnityEngine;

namespace RealisticFiring.Sim
{
 internal sealed class EngineSuperheater
    {
        // Heat a kg of steam takes from the feed, J: about 2.5 MJ at working pressure.
        private const float EvaporationHeat = 2.5e6f;

        private static readonly Dictionary<string, SuperheaterSpec> Fits = new Dictionary<string, SuperheaterSpec>
        {
            // USRA light Mikado: Type A, 882 sq ft on 3,783 (RME Aug 1918).
            ["LocoS282A"] = Superheat("Type A", 136f, 0.72f),
            // The JK3's 1897–1912 shunters were saturated.
            ["Jk3A"] = SuperheaterSpec.None,
            // NYC J-3a: Type E, 1,745 sq ft on 4,187 (RME May 1938).
            ["S464-730"] = Superheat("Type E", 175f, 0.82f),
            // UP FEF-3: a large Type A, 1,400 sq ft on 4,224 (UP diagram book).
            ["FEF-3"] = Superheat("Type A", 158f, 0.75f),
            ["S484-840A"] = Superheat("Type A", 158f, 0.75f),
            // AT&SF 2900: 220 Type E flues, 2,366 sq ft on 5,312 (Locobase).
            ["A.T.&S.F. 2901-A"] = Superheat("Type E", 180f, 0.82f),
            // SP GS-4: Elesco Type E, its valves rated for 750 °F (SP spec, 1940).
            ["S484-841A"] = Superheat("Type E", 180f, 0.82f),
        };

        private static readonly HashSet<string> Exempt = new HashSet<string> { "LocoS060" };

        // Any other steam engine: a Type A, the commonest.
        private static readonly SuperheaterSpec Default = Superheat("Type A", 136f, 0.72f);

        private static readonly ConditionalWeakTable<ReciprocatingSteamEngine, EngineSuperheater> Engines =
            new ConditionalWeakTable<ReciprocatingSteamEngine, EngineSuperheater>();

        private readonly Port heat;
        private readonly float fullHeat;

        private EngineSuperheater(SuperheaterSpec spec, Port heat, float fullHeat)
        {
            Model = new Superheater(spec);
            this.heat = heat;
            this.fullHeat = fullHeat;
        }

        public Superheater Model { get; }

        // Standing with a bright fire and the throttle shut, the elements went
        // "above red heat" (Santa Fe, 1921). They take minutes to come up; their
        // steel's heat over the steam's at the full rate is about 75 s.
        private static SuperheaterSpec Superheat(string kind, float full, float halfShare) =>
            new SuperheaterSpec { Kind = kind, FullSuperheat = full, HalfSuperheat = full * halfShare, StandingSuperheat = 400f, TimeConstant = 75f };

        /// <summary>Fits <paramref name="car"/>'s engine with its superheater. Returns a line for the log, or null.</summary>
        public static string Attach(TrainCar car, SimController controller)
        {
            var flow = controller != null ? controller.simFlow : null;
            var engine = flow != null ? flow.OrderedSimComps.OfType<ReciprocatingSteamEngine>().FirstOrDefault() : null;
            var firebox = controller.connectionsDefinition != null && controller.connectionsDefinition.executionOrder != null
                ? controller.connectionsDefinition.executionOrder.OfType<FireboxDefinition>().FirstOrDefault()
                : null;
            if (engine == null || firebox == null || firebox.burnTime <= 0f)
                return null;
            flow.TryGetPort($"{firebox.ID}.{firebox.heatReadOut.ID}", out var heat, canBeNullOrEmpty: true);
            if (heat == null)
                return null;
            var livery = car != null && car.carLivery != null ? car.carLivery.id : null;
            if (livery != null && Exempt.Contains(livery))
            {
                EngineCylinders.Of(engine).Model.WallCondensation = false;
                return "steam as it was: no superheater, no wet walls";
            }
            var spec = livery != null && Fits.TryGetValue(livery, out var fit) ? fit : Default;
            var fullHeat = firebox.maxCoalCapacity / firebox.burnTime * FireboxParameters.CoalHeatContent * firebox.efficiencyAtMaxCombustion;
            Engines.Remove(engine);
            Engines.Add(engine, new EngineSuperheater(spec, heat, fullHeat));
            return spec.Saturated
                ? "saturated: no superheater"
                : $"{spec.Kind} superheater: {spec.FullSuperheat:F0} °C at the full rate of {fullHeat / 1e6f:F1} MW, {spec.HalfSuperheat:F0} °C at half";
        }

        public static bool TryGet(ReciprocatingSteamEngine engine, out EngineSuperheater superheater)
        {
            superheater = null;
            return engine != null && Engines.TryGetValue(engine, out superheater);
        }

        /// <summary>A tick: the fire's heat and the steam into the steam chest, each against the boiler's full rate.</summary>
        public void Step(ReciprocatingSteamEngine engine, float delta)
        {
            var fire = fullHeat > 0f ? Mathf.Max(0f, heat.Value) / fullHeat : 0f;
            var steam = fullHeat > 0f ? Mathf.Max(0f, engine.intakeFlowReadOut.Value) * EvaporationHeat / fullHeat : 0f;
            var quality = engine.intakeQuality.IsConnected ? engine.intakeQuality.Value : 1f;
            Model.Step(fire, steam, quality, delta);
        }

        /// <summary>The steam's temperature at <paramref name="pressure"/> absolute bar, in °C.</summary>
        public float SteamTemperature(float pressure) => SteamCylinders.SaturationTemperature(pressure) + Model.Superheat;
    }
}
