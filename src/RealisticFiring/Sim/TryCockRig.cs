using System.Collections.Generic;
using System.Linq;
using LocoSim.Implementations;
using RealisticFiring.Core;

namespace RealisticFiring.Sim
{
 internal sealed class TryCockRig
    {
        /// <summary>The cocks' controls: the try cocks top to bottom, then the glass's top and bottom cocks.</summary>
        public static readonly IReadOnlyList<StokerJetControls.Valve> Valves = new[]
        {
            new StokerJetControls.Valve("rfTryCockTop", "Top try cock", null, 0f),
            new StokerJetControls.Valve("rfTryCockMiddle", "Middle try cock", null, 0f),
            new StokerJetControls.Valve("rfTryCockBottom", "Bottom try cock", null, 0f),
            new StokerJetControls.Valve("rfGlassCockTop", "Glass top cock", null, 0f),
            new StokerJetControls.Valve("rfGlassCockBottom", "Glass bottom cock", null, 0f),
        };

        /// <summary>How many of <see cref="Valves"/> are try cocks; the rest are the glass's.</summary>
        public const int TryCocks = 3;

        /// <summary>The glass's top cock's index in <see cref="Valves"/>.</summary>
        public const int GlassTop = 3;

        /// <summary>The glass's bottom cock's index in <see cref="Valves"/>.</summary>
        public const int GlassBottom = 4;

        /// <summary>An engine's cocks: the cab's valve meshes and their heights in the boiler, top to bottom.</summary>
        internal readonly struct Fit
        {
            public Fit(string[] meshes, float[] levels)
            {
                Meshes = meshes;
                Levels = levels;
            }

            /// <summary>The cab's own loose valve wheel for each cock.</summary>
            public string[] Meshes { get; }

            /// <summary>
            /// Each cock's height on the boiler's level scale (the one the
            /// glass reads, 0 to 1): a try cock's from its wheel's center on
            /// the glass, the glass's drawn length spanning its indicator's
            /// range; the glass's own at its range's top and foot, where its
            /// connections are.
            /// </summary>
            public float[] Levels { get; }
        }

        // From the stock cabs (read from the game's resources, 2026-11-14):
        // the S282's three cocks on a diagonal left of its glass, which spans
        // 0.63 to 0.96 of the boiler over 2.948 to 3.126 m, at 3.094, 3.018,
        // and 2.944 m (the bottom one at the glass's foot); the S060's left of
        // its glass, 0.68 to 0.84 over 2.590 to 2.768 m, at 2.733, 2.679, and
        // 2.626 m. Each cab's other two loose wheels are its glass's own cocks,
        // above and below it (the S282's at 3.217 and 2.857 m, the S060's at
        // 2.860 and 2.499 m).
        private static readonly Dictionary<string, Fit> ByLivery = new Dictionary<string, Fit>
        {
            ["LocoS282A"] = new Fit(new[] { "s282_valve_s_03", "s282_valve_s_02", "s282_valve_s_01", "s282_valve_w_01", "s282_valve_w_02" },
                new[] { 0.900f, 0.761f, 0.621f, 0.96f, 0.63f }),
            ["LocoS060"] = new Fit(new[] { "s060_valve_05", "s060_valve_06", "s060_valve_07", "s060_valve_03", "s060_valve_04" },
                new[] { 0.808f, 0.760f, 0.712f, 0.84f, 0.68f }),
        };

        private static readonly Dictionary<TrainCar, TryCockRig> ByCar = new Dictionary<TrainCar, TryCockRig>();

        private readonly Port[] ports;
        private readonly BoilerWaterRig water;
        private readonly Boiler boiler;

        private TryCockRig(TrainCar car, BoilerWaterRig water, Boiler boiler, Fit fit, Port[] ports)
        {
            Car = car;
            this.water = water;
            this.boiler = boiler;
            Levels = fit.Levels;
            Meshes = fit.Meshes;
            this.ports = ports;
        }

        public TrainCar Car { get; }

        /// <summary>Each cock's height on the boiler's level scale, top to bottom.</summary>
        public float[] Levels { get; }

        /// <summary>The cab's valve wheel for each cock, top to bottom.</summary>
        public string[] Meshes { get; }

        /// <summary>Whether engines of <paramref name="livery"/> have try cocks to work.</summary>
        public static bool Fits(string livery) => livery != null && ByLivery.ContainsKey(livery);

        public static bool TryGet(TrainCar car, out TryCockRig rig)
        {
            rig = null;
            return car != null && ByCar.TryGetValue(car, out rig);
        }

        /// <summary>Sets up <paramref name="car"/>'s try cocks on its boiler. Returns a line for the log, or null for an engine without them.</summary>
        public static string Attach(TrainCar car, SimulationFlow flow)
        {
            foreach (var gone in ByCar.Keys.Where(c => c == null).ToList())
                ByCar.Remove(gone);
            var livery = car != null && car.carLivery != null ? car.carLivery.id : null;
            if (!ByLivery.TryGetValue(livery ?? "", out var fit) || flow == null)
                return null;
            var boiler = flow.OrderedSimComps.OfType<Boiler>().FirstOrDefault();
            if (boiler == null || !BoilerWaterRig.TryGet(boiler, out var water))
                return "no boiler water to put try cocks on";
            var ports = Valves.Select(v => flow.TryGetPort(v.PortId, out var port, canBeNullOrEmpty: true) ? port : null).ToArray();
            if (ports.Any(p => p == null))
                return "try cocks' controls missing from the sim; none fitted";
            var rig = new TryCockRig(car, water, boiler, fit, ports);
            ByCar[car] = rig;
            water.TryCocks = rig;
            return $"try cocks at {string.Join(", ", fit.Levels.Take(TryCocks).Select(l => l.ToString("F2")))} of the boiler, the glass's cocks at " +
                   $"{fit.Levels[GlassTop]:F2} and {fit.Levels[GlassBottom]:F2} (crown sheet at {water.CrownSheetLevelNormalized:F2})";
        }

        /// <summary>The boiler readout the glass shows.</summary>
        public string WaterLevelPortId => boiler.waterLevelReadOut.id;

        /// <summary>Cock <paramref name="index"/>'s control port.</summary>
        public Port PortOf(int index) => ports[index];

        /// <summary>How far cock <paramref name="index"/> is open, 0 to 1.</summary>
        public float Opening(int index) => Smoothing.Clamp01(Smoothing.Finite(ports[index].Value));

        /// <summary>Whether any cock is open.</summary>
        public bool AnyOpen => ports.Any(p => p.Value > 0f);

        /// <summary>
        /// What cock <paramref name="index"/> lets out now, from the boiler's
        /// readouts (a guest's too, which mirror the host's): the level the
        /// glass shows, the pressure (absolute), and the water's temperature;
        /// the foam where the boiler water is kept (the host's).
        /// </summary>
        public TryCockFlow Flow(int index)
        {
            var foam = Main.Settings.BoilerWaterImpurities && !Net.IsClient ? water.Water.Foam : 0f;
            return TryCock.Flow(Opening(index), boiler.waterLevelReadOut.Value - Levels[index], foam,
                boiler.pressureReadOut.Value, boiler.temperatureReadOut.Value);
        }

        /// <summary>Everything the cocks let out now, kg/s: boiler water and steam.</summary>
        public (float Water, float Steam) Total()
        {
            var water = 0f;
            var steam = 0f;
            for (var i = 0; i < ports.Length; i++)
            {
                if (!(ports[i].Value > 0f))
                    continue;
                var flow = Flow(i);
                water += flow.Water;
                steam += flow.Steam;
            }
            return (water, steam);
        }
    }
}
