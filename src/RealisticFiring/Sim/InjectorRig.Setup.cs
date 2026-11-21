using System.Collections.Generic;
using System.Linq;
using LocoSim.Definitions;
using LocoSim.Implementations;
using RealisticFiring.Core;

namespace RealisticFiring.Sim
{
    internal sealed partial class InjectorRig
    {
        /// <summary>Seconds after an engine is set up in which its injectors find their saved levers.</summary>
        private const float ResumeSeconds = 5f;

        // Engines whose one cab lever stands for the prototype's two
        // injectors turn down as far as one of a pair of half its size run
        // alone at its least (LocomotiveProfiles.Injector.OneLeverForTwo).
        private const float OneOfTwoLeast = 0.5f;

        /// <summary>
        /// Sets up a realistic injector on each boiler of <paramref name="car"/>
        /// whose injector is worked by a cab control. A boiler whose injector
        /// port is driven by anything else keeps the stock behavior, except
        /// verified two-valve averaging graphs (JK3, LMS 8F and 4F). Returns a line for
        /// the log, or null when the car has no boiler.
        /// </summary>
        public static string Attach(TrainCar car, SimulationFlow flow)
        {
            var boilers = flow.OrderedSimComps.OfType<Boiler>().ToList();
            if (boilers.Count == 0)
                return null;

            var notes = new List<string>();
            foreach (var boiler in boilers)
            {
                if (TryCreate(car, boiler, car.carLivery.id, flow.OrderedSimComps, out var note)
                    && TryGet(boiler, out var rig) && rig.FitPump(flow) is string pump)
                    note += "; " + pump;
                notes.Add(note);
            }
            return string.Join("; ", notes);
        }

        /// <summary>
        /// Gives <paramref name="boiler"/> a realistic injector if a cab control
        /// works it, and says what was decided.
        /// </summary>
        internal static bool TryCreate(TrainCar car, Boiler boiler, out string note)
            => TryCreate(car, boiler, car != null ? car.carLivery.id : null, null, out note);

        internal static bool TryCreate(TrainCar car, Boiler boiler, string livery, IEnumerable<SimComponent> components, out string note)
        {
            var control = boiler.injectorControl.IsConnected ? ReferencedPort(boiler.injectorControl) : null;
            // A verified two-valve graph first: the 8F's calculated feed is typed as a cab control.
            var controls = TwoValveControls(livery, components, control)
                ?? (control != null && control.type == PortType.EXTERNAL_IN ? new[] { control } : null);
            if (controls == null)
            {
                note = $"boiler '{boiler.id}' keeps the stock injector (driven by {(control != null ? $"'{control.id}'" : "nothing")})";
                return false;
            }

            var rated = SafetyValveOpeningPressure(boiler);
            var stockRate = MaxInjectorRate(boiler);
            var profile = LocomotiveProfiles.For(livery);
            var delivery = profile?.Injector.PeriodDelivery
                ?? (MakerFitRegistry.TryGetInjectorMaxFlow(livery, out var makerPeriod) ? makerPeriod : (float?)null);
            var resized = delivery.HasValue;
            var maxRate = delivery ?? stockRate;
            if (maxRate <= 0f || rated <= InjectorSpec.DefaultStartPressure)
            {
                note = $"boiler '{boiler.id}' keeps the stock injector (rate {maxRate}, safety valve {rated} bar)";
                return false;
            }

            var ports = controls.Length > 1 ? components.SelectMany(c => c.GetAllPorts()).ToList() : null;
            var circuits = controls.Select((port, i) =>
            {
                var spec = new InjectorSpec(maxRate / controls.Length, rated)
                {
                    LeastScale = (profile?.Injector.OneLeverForTwo == true || MakerFitRegistry.IsOneLeverForTwo(livery)) ? OneOfTwoLeast : 1f,
                    Lifting = InjectorValves.Lifting(livery),
                };
                if (profile?.Injector.CrackOpening is float crack
                    && boiler.id == "boiler" && controls.Length == 1 && port.id == "injector.EXT_IN")
                    spec.CrackOpening = crack;
                else if (MakerFitRegistry.TryGetInjectorCrackOpening(livery, out var makerCrack))
                    spec.CrackOpening = makerCrack;
                return new InjectorCircuit(port, new Injector(spec),
                    controls.Length == 1 ? "" : i == 0 ? "right" : "left",
                    ports?.SingleOrDefault(p => p.id == $"injectorParticles.INJECTOR_{(i == 0 ? "R" : "L")}_PARTICLES_CLAMP"));
            })
                .ToList().AsReadOnly();
            // Each injector's own steam valve, where the engine has them: the stock lever is then its water valve.
            var steamValves = 0;
            if (components != null && InjectorValves.Fits(livery))
            {
                var all = components.SelectMany(c => c.GetAllPorts()).ToList();
                for (var i = 0; i < circuits.Count; i++)
                {
                    circuits[i].Added = all.FirstOrDefault(p => p.id == InjectorValves.For(i, livery).PortId);
                    if (circuits[i].Added == null)
                        continue;
                    circuits[i].OwnLeverIsSteam = InjectorValves.OwnLeverIsSteam(livery);
                    steamValves++;
                }
            }
            // A loaded engine's injectors pick up as they were saved, feeding or not.
            foreach (var circuit in circuits)
                circuit.Injector.Resume(ResumeSeconds);
            var rig = new InjectorRig(car, boiler, circuits);
            PruneDestroyedCars();
            ByBoiler[boiler] = rig;
            if (car != null)
                ByCar[car] = rig;
            note = $"realistic injector{(controls.Length > 1 ? "s" : "")} on '{boiler.id}' worked by " +
                   string.Join(" and ", controls.Select(p => $"'{p.id}'")) +
                   $": {maxRate / controls.Length:F1} kg/s{(controls.Length > 1 ? " each" : "")} at {rated:F1} bar" +
                   (resized ? $" (a period size; the game's is {stockRate:F1})" : "") +
                   $", picks up from {InjectorSpec.DefaultStartPressure:F1} bar" +
                   (steamValves > 0 ? $"; {(InjectorValves.Lifting(livery) ? "lifting" : "non-lifting")}, its own steam valve{(steamValves > 1 ? "s" : "")}, the lever its water valve" : "");
            return true;
        }

        /// <summary>Whether <paramref name="livery"/>'s cab works two injectors through a verified calculated feed.</summary>
        internal static bool HasTwoValveGraph(string livery) =>
            LocomotiveProfiles.For(livery)?.Injector.TwoValveGraph != null || MakerFitRegistry.TryGetTwoValveGraph(livery, out _);

        private static bool TryGetTwoValveGraph(string livery, out TwoValveInjectorGraph graph)
        {
            graph = LocomotiveProfiles.For(livery)?.Injector.TwoValveGraph;
            if (graph != null)
                return true;
            if (!MakerFitRegistry.TryGetTwoValveGraph(livery, out var maker))
                return false;
            graph = new TwoValveInjectorGraph
            {
                Add = maker.Add, AddA = maker.AddA, AddB = maker.AddB,
                Multiplier = maker.Multiplier, MultiplierOut = maker.MultiplierOut,
                Factor = maker.Factor, FactorPort = maker.FactorPort,
                Right = maker.Right, Left = maker.Left,
            };
            return true;
        }

        /// <summary>
        /// The fireman still needs the cab valve when realistic injectors are
        /// disabled. A two-injector boiler's calculated average is read-only;
        /// use its verified right-hand valve rather than trying to write it.
        /// </summary>
        internal static Port FiremanControl(Boiler boiler, string livery, IEnumerable<SimComponent> components)
        {
            var control = boiler.injectorControl.IsConnected ? ReferencedPort(boiler.injectorControl) : null;
            // The 8F's calculated output is itself typed EXTERNAL_IN, so
            // validate the graph before accepting an ordinary control port.
            if (HasTwoValveGraph(livery))
                return TwoValveControls(livery, components, control)?.FirstOrDefault();
            if (control != null && control.type == PortType.EXTERNAL_IN && control.valueType == PortValueType.CONTROL)
                return control;
            return null;
        }

        private static Port[] TwoValveControls(string livery, IEnumerable<SimComponent> components, Port control)
        {
            if (!TryGetTwoValveGraph(livery, out var graph) || components == null || control == null)
                return null;
            var all = components.ToList();
            var multiplier = all.OfType<ConfigurableMultiplier>().SingleOrDefault(c => c.id == graph.Multiplier);
            var add = all.OfType<ConfigurableAdd>().SingleOrDefault(c => c.id == graph.Add);
            var factor = all.SingleOrDefault(c => c.id == graph.Factor)?.GetAllPorts().SingleOrDefault(p => p.id == graph.FactorPort);
            Port Valve(string id) => all.OfType<ExternalControl>().SingleOrDefault(c => c.id == id)?.controlExtIn;
            var right = Valve(graph.Right);
            var left = Valve(graph.Left);
            Port Source(PortReference r) => r.IsConnected ? ReferencedPort(r) : null;
            bool CabValve(Port p, string id) => p != null && p.id == id + ".EXT_IN" && p.type == PortType.EXTERNAL_IN
                && p.valueType == PortValueType.CONTROL;
            if (multiplier == null || add == null || factor == null
                || multiplier.invertA || multiplier.invertB || add.negativeA || add.negativeB
                || multiplier.mulReadOut != control || control.id != graph.MultiplierOut
                || Source(multiplier.aReader) != add.addReadOut || Source(multiplier.bReader) != factor
                || (factor.type != PortType.IN && factor.type != PortType.EXTERNAL_IN) || factor.IsConnectedPort || factor.Value != 0.5f
                || !CabValve(right, graph.Right) || !CabValve(left, graph.Left)
                || Source(add.aReader) != Valve(graph.AddA) || Source(add.bReader) != Valve(graph.AddB))
                return null;
            return new[] { right, left };
        }
    }
}
