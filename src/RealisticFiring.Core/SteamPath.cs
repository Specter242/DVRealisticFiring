using System;

namespace RealisticFiring.Core
{
    public enum RegulatorLocation { BeforeSuperheater, AfterSuperheater }

    /// <summary>The circuit and delivery/chest volumes in liters, and the regulator between their appropriate boundaries.</summary>
    public sealed class SteamPathSpec
    {
        public RegulatorLocation Regulator { get; }
        public double PipeVolume { get; }
        public double ChestVolume { get; }
        public double MaxFlow { get; }
        public double TotalVolume => PipeVolume + ChestVolume;
        public double RegulatedVolume => Regulator == RegulatorLocation.BeforeSuperheater ? TotalVolume : ChestVolume;

        public SteamPathSpec(RegulatorLocation regulator, double pipeVolume, double chestVolume, double maxFlow)
        {
            if (!Finite(pipeVolume) || pipeVolume < 0 || !Finite(chestVolume) || chestVolume <= 0
                || !Finite(maxFlow) || maxFlow <= 0 || !Finite(pipeVolume + chestVolume))
                throw new ArgumentOutOfRangeException(nameof(chestVolume), "Steam volumes and capacity must be finite and positive.");
            if (regulator != RegulatorLocation.BeforeSuperheater && regulator != RegulatorLocation.AfterSuperheater)
                throw new ArgumentOutOfRangeException(nameof(regulator));
            Regulator = regulator; PipeVolume = pipeVolume; ChestVolume = chestVolume; MaxFlow = maxFlow;
        }

        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public struct SteamPathState
    {
        public double PipeMass, ChestMass, PipeHeat, ChestHeat;
    }

    public struct SteamPathFlow
    {
        /// <summary>Signed steam taken from the boiler. Negative means returned through an open regulator.</summary>
        public double InletMass, InletHeat;
        /// <summary>Signed flow across the regulator; a boiler-side header can fill without crossing it.</summary>
        public double RegulatorMass;
        public double DeliveredMass, DeliveredHeat;
        /// <summary>Heat exchanged with the existing superheater boundary; negative means cooling.</summary>
        public double HeaterHeat;
        public double HeaterMass;
    }

    public struct SteamPathLoss
    {
        public double LeakedMass, LeakedHeat, CondensedMass, CondensedHeat, CoolingHeat;
    }

    /// <summary>
    /// Conserved steam and transported sensible heat in the superheater circuit
    /// and delivery chest. Regulator placement determines which inventory can
    /// supply the cylinders after shut-off.
    /// Pressure retains the game's saturated-equivalent mass/volume closure so
    /// the existing cylinder density relation remains the same.
    /// Heat is an advected tracer (J, 0 K reference), not a new boiler/condensate model.
    /// </summary>
    public sealed class SteamPath
    {
        /// <summary>Pressure equality tolerance, in absolute bar.</summary>
        public const double PressureTolerance = 0.001;
        private const double HeatCapacity = 2400; // J/kg K, matching the existing superheater approximation.
        private const double StepLimit = 0.01;
        // Inferred valve conductance for private tuning, not measured prototype dimensions.
        private const double ValveDrop = 0.01;
        private const double ConnectionDrop = 0.002;
        private SteamPathState state;

        public SteamPathSpec Spec { get; }
        public SteamPathState State => state;
        public double Mass => state.PipeMass + state.ChestMass;
        public double RegulatedMass => Spec.Regulator == RegulatorLocation.BeforeSuperheater ? Mass : state.ChestMass;
        public double Heat => state.PipeHeat + state.ChestHeat;
        public double ChestPressure => Pressure(state.ChestMass, Spec.ChestVolume);
        public double PipePressure => Spec.PipeVolume > 0 ? Pressure(state.PipeMass, Spec.PipeVolume) : ChestPressure;
        public double ChestTemperature => Temperature(state.ChestMass, state.ChestHeat);

        public SteamPath(SteamPathSpec spec) { Spec = spec ?? throw new ArgumentNullException(nameof(spec)); }

        public static double VolumePerKg(double pressure) => 1720 * Math.Pow(Math.Max(1, pressure), -0.951);
        public static double Pressure(double mass, double volume) => mass > 0
            ? Math.Max(1, Math.Pow(volume / mass / 1720, -1.0515247108307046)) : 1;
        public static double SpecificHeat(double temperature) => HeatCapacity * (Math.Max(-273.15, temperature) + 273.15);

        /// <summary>
        /// Equivalent ring/valve leakage: two cylinder circumferences with a
        /// 20 micrometer effective gap, discharge coefficient .65. This is a
        /// physical estimate, not a measured clearance or a regulator leak.
        /// The exponential tail approximates blowdown into atmospheric exhaust.
        /// </summary>
        public static double LeakSeconds(double volumeLiters, double boreMeters, double temperature)
        {
            var area = 2 * Math.PI * Math.Max(.1, boreMeters) * .000020;
            var sound = Math.Sqrt(1.3 * 461.5 * (SafeTemperature(temperature) + 273.15));
            return volumeLiters / 1000 / (.65 * area * sound * .585);
        }

        public static double RemainingMass(double initialPressure, double volume, double seconds, double leakSeconds)
        {
            var ambient = volume / VolumePerKg(1);
            return ambient + Math.Max(0, volume / VolumePerKg(initialPressure) - ambient)
                * Math.Exp(-Math.Max(0, seconds) / leakSeconds);
        }

        /// <summary>
        /// Loss from the circuit downstream of a shut regulator. Never vents
        /// the boiler-side header through a front-end throttle. Condensate
        /// leaves the available vapor inventory; no water returns to the boiler.
        /// </summary>
        public SteamPathLoss Bleed(double seconds, double wallTemperature, double leakSeconds)
        {
            var loss = new SteamPathLoss();
            if (!SteamPathSpec.Finite(seconds) || seconds < 0 || !SteamPathSpec.Finite(wallTemperature)
                || !SteamPathSpec.Finite(leakSeconds) || leakSeconds <= 0) return loss;
            var upstream = Spec.Regulator == RegulatorLocation.BeforeSuperheater;
            if (upstream) BleedNode(ref state.PipeMass, ref state.PipeHeat, Spec.PipeVolume, seconds, wallTemperature, leakSeconds, ref loss);
            BleedNode(ref state.ChestMass, ref state.ChestHeat, Spec.ChestVolume, seconds, wallTemperature, leakSeconds, ref loss);
            return loss;
        }

        private static void BleedNode(ref double mass, ref double heat, double volume, double seconds,
            double wallTemperature, double leakSeconds, ref SteamPathLoss loss)
        {
            if (mass <= 0 || volume <= 0) return;
            var ambient = volume / VolumePerKg(1);
            var retained = ambient + Math.Max(0, mass - ambient) * Math.Exp(-seconds / leakSeconds);
            Move(ref mass, ref heat, Math.Max(0, mass - retained), out var leaked, out var leakedHeat);
            loss.LeakedMass += leaked; loss.LeakedHeat += leakedHeat;
            var previous = heat;
            var temperature = Temperature(mass, heat);
            // Sensible cooling is bounded by the cooler metal. The pressure
            // relation remains the game's saturated-equivalent relation.
            temperature = Math.Min(temperature, wallTemperature + (temperature - wallTemperature) * Math.Exp(-seconds / 20));
            heat = mass * SpecificHeat(Math.Max(0, temperature));
            loss.CoolingHeat += previous - heat;
            var saturationPressure = Math.Max(1, Math.Pow(Math.Max(0, temperature) / 99.6, 4));
            var vaporLimit = volume / VolumePerKg(saturationPressure);
            Move(ref mass, ref heat, Math.Max(0, mass - vaporLimit), out var condensed, out var condensedHeat);
            loss.CondensedMass += condensed; loss.CondensedHeat += condensedHeat;
            // Finish the asymptotic leak below 0.005 bar gauge. At atmospheric
            // pressure this inventory cannot do positive cylinder work.
            if (Pressure(mass, volume) <= 1.005)
            {
                Move(ref mass, ref heat, Math.Max(0, mass - ambient), out leaked, out leakedHeat);
                loss.LeakedMass += leaked; loss.LeakedHeat += leakedHeat;
            }
        }
        private static double Temperature(double mass, double heat) => mass > 1e-12 ? heat / mass / HeatCapacity - 273.15 : 0;

        /// <summary>Adopt the original chest inventory once; the volume split never duplicates its steam.</summary>
        public void Reset(double mass, double temperature)
        {
            mass = SafeMass(mass);
            var pipe = mass * Spec.PipeVolume / Spec.TotalVolume;
            var h = SpecificHeat(SafeTemperature(temperature));
            state = new SteamPathState { PipeMass = pipe, ChestMass = mass - pipe, PipeHeat = pipe * h, ChestHeat = (mass - pipe) * h };
        }

        public bool Restore(SteamPathState value)
        {
            if (!ValidNode(value.PipeMass, value.PipeHeat) || !ValidNode(value.ChestMass, value.ChestHeat)
                || (Spec.PipeVolume == 0 && (value.PipeMass != 0 || value.PipeHeat != 0))
                || Pressure(value.ChestMass, Spec.ChestVolume) > 100
                || (Spec.PipeVolume > 0 && Pressure(value.PipeMass, Spec.PipeVolume) > 100)) return false;
            state = value;
            return true;
        }

        private static bool ValidNode(double mass, double heat) => SteamPathSpec.Finite(mass) && mass >= 0
            && mass <= 1e5 && SteamPathSpec.Finite(heat) && heat >= 0
            && (mass > 0 ? heat >= mass * SpecificHeat(0) - 1e-6 && heat <= mass * SpecificHeat(2000) : heat == 0);
        private static double SafeMass(double value) => SteamPathSpec.Finite(value) ? Math.Max(0, value) : 0;
        private static double SafeTemperature(double value) => SteamPathSpec.Finite(value) ? Math.Max(0, Math.Min(1000, value)) : 0;

        /// <summary>
        /// Pay the game's discrete stroke debit from the connected effective
        /// inventory, once. The cylinder admission model already integrates
        /// that valve opening; spreading its debit over a second interval
        /// would smooth the original power curve a second time.
        /// </summary>
        public SteamPathFlow Take(double requested)
        {
            var flow = new SteamPathFlow();
            var amount = Math.Min(RegulatedMass, SafeMass(requested));
            var pipe = Spec.Regulator == RegulatorLocation.BeforeSuperheater && Mass > 0
                ? amount * state.PipeMass / Mass : 0;
            Move(ref state.PipeMass, ref state.PipeHeat, pipe, out var pipeMass, out var pipeHeat);
            Move(ref state.ChestMass, ref state.ChestHeat, amount - pipeMass, out var chestMass, out var chestHeat);
            flow.DeliveredMass = pipeMass + chestMass;
            flow.DeliveredHeat = pipeHeat + chestHeat;
            flow.HeaterMass = pipeMass;
            return flow;
        }

        /// <summary>
        /// Pay losses within the tick. Front-end chests retain the native discrete
        /// debit/refill sequence; upstream circuits integrate their connected flow.
        /// The inlet has finite capacity and internal transfer cannot cross equilibrium.
        /// All returned amounts are integrals in kg/J, not rates.
        /// </summary>
        public SteamPathFlow Step(double pressure, double boilerTemperature, double hotTemperature,
            double opening, double demand, double delta)
        {
            var flow = new SteamPathFlow();
            if (!SteamPathSpec.Finite(delta) || delta <= 0 || !SteamPathSpec.Finite(pressure) || pressure < 1) return flow;
            boilerTemperature = SafeTemperature(boilerTemperature);
            hotTemperature = SafeTemperature(hotTemperature);
            opening = SteamPathSpec.Finite(opening) ? Math.Max(0, Math.Min(1, opening)) : 0;
            demand = SafeMass(demand);
            if (Spec.Regulator == RegulatorLocation.AfterSuperheater)
                return FrontStep(pressure, boilerTemperature, hotTemperature, opening, demand, delta);
            var count = (int)Math.Min(1024, Math.Max(1, Math.Ceiling(delta / StepLimit)));
            var dt = delta / count;
            for (var i = 0; i < count; i++)
            {
                Inlet(pressure, boilerTemperature, hotTemperature, opening, dt * 0.5, ref flow);
                HeatPipe(hotTemperature, ref flow);
                Transfer(pressure, opening, dt * 0.5, ref flow);
                Move(ref state.ChestMass, ref state.ChestHeat, demand * dt, out var mass, out var heat);
                flow.DeliveredMass += mass; flow.DeliveredHeat += heat;
                Transfer(pressure, opening, dt * 0.5, ref flow);
                Inlet(pressure, boilerTemperature, hotTemperature, opening, dt * 0.5, ref flow);
                HeatPipe(hotTemperature, ref flow);
                // Publish a connected circuit, rather than a newly topped-up
                // pipe beside a chest still one half-substep behind. With a
                // small physical pipe that sampling lag biases admission.
                Transfer(pressure, opening, dt * 0.5, ref flow);
            }
            return flow;
        }

        private SteamPathFlow FrontStep(double pressure, double cold, double hot, double opening, double demand, double delta)
        {
            // Match the maker's discrete chest: pay the whole tick's losses,
            // then refill once with its linear valve cap. Substepping this
            // sequence smooths the pressure ripple and changes steady output.
            // The binding supplies the stock pre-debit pressure-equality gate.
            var flow = Take(demand * delta);
            var supplyBudget = Spec.MaxFlow * (Spec.PipeVolume > 0 ? 10 : 1) * delta;
            if (Spec.PipeVolume > 0)
            {
                Inlet(pressure, cold, hot, opening, delta, ref flow);
                HeatPipe(hot, ref flow);
                supplyBudget = Math.Max(0, supplyBudget - Math.Abs(flow.InletMass));
            }
            var cap = Spec.MaxFlow * opening * delta;
            var amount = Math.Max(-cap, Math.Min(cap, Spec.ChestVolume / VolumePerKg(pressure) - state.ChestMass));
            var connected = Spec.PipeVolume == 0 || Math.Abs(PipePressure - pressure) < PressureTolerance;
            if (connected && Math.Abs(amount) <= supplyBudget)
            {
                // A charged header is a conduit connected to the boiler.
                // Through-flow leaves its inventory intact and is charged to
                // the boiler once; filling the header already used its share
                // of this tick's finite supply budget above.
                if (amount >= 0)
                {
                    var heat = amount * SpecificHeat(hot);
                    state.ChestMass += amount; state.ChestHeat += heat;
                    flow.InletMass += amount; flow.InletHeat += heat;
                    flow.HeaterMass += amount; flow.RegulatorMass += amount;
                }
                else
                {
                    Move(ref state.ChestMass, ref state.ChestHeat, -amount, out var mass, out var heat);
                    flow.InletMass -= mass; flow.InletHeat -= heat;
                    flow.RegulatorMass -= mass;
                }
            }
            else
            {
                // Until the header is charged, only its actual pressure and
                // inventory can move steam through the valve. In particular,
                // a weak header cannot feed a higher-pressure chest.
                Transfer(pressure, opening, delta, ref flow);
                HeatPipe(hot, ref flow);
            }
            return flow;
        }

        private void HeatPipe(double hotTemperature, ref SteamPathFlow flow)
        {
            if (state.PipeMass <= 0) return;
            // The existing heater supplies the outlet boundary temperature before
            // steam leaves the elements. Book the exchange, including cooling.
            var heated = state.PipeMass * SpecificHeat(hotTemperature);
            flow.HeaterHeat += heated - state.PipeHeat;
            state.PipeHeat = heated;
        }

        private void Inlet(double pressure, double cold, double hot, double opening, double dt, ref SteamPathFlow flow)
        {
            var pipe = Spec.PipeVolume > 0;
            // A front-end throttle is between the hot header and the chest.
            // Its boiler-side circuit charges even with that throttle shut.
            var unregulated = pipe && Spec.Regulator == RegulatorLocation.AfterSuperheater;
            var inletOpening = unregulated ? 1 : opening;
            if (inletOpening <= 0) return;
            var mass = pipe ? state.PipeMass : state.ChestMass;
            var volume = pipe ? Spec.PipeVolume : Spec.ChestVolume;
            var difference = volume / VolumePerKg(pressure) - mass;
            var head = Math.Abs(pressure - Pressure(mass, volume));
            var rate = Spec.MaxFlow * (unregulated ? 10 : 1) * inletOpening
                * Math.Min(1, Math.Sqrt(head / ((unregulated ? ConnectionDrop : ValveDrop) * pressure)));
            var moved = Math.Min(Math.Abs(difference), rate * dt);
            if (difference >= 0)
            {
                var heat = moved * SpecificHeat(Spec.Regulator == RegulatorLocation.BeforeSuperheater ? cold : hot);
                if (pipe) { state.PipeMass += moved; state.PipeHeat += heat; }
                else { state.ChestMass += moved; state.ChestHeat += heat; }
                flow.InletMass += moved; flow.InletHeat += heat;
                if (!unregulated) flow.RegulatorMass += moved;
                if (Spec.Regulator == RegulatorLocation.AfterSuperheater) flow.HeaterMass += moved;
            }
            else
            {
                double removed, heat;
                if (pipe) Move(ref state.PipeMass, ref state.PipeHeat, moved, out removed, out heat);
                else Move(ref state.ChestMass, ref state.ChestHeat, moved, out removed, out heat);
                flow.InletMass -= removed; flow.InletHeat -= heat;
                if (!unregulated) flow.RegulatorMass -= removed;
            }
        }

        private void Transfer(double pressure, double opening, double dt, ref SteamPathFlow flow)
        {
            if (Spec.PipeVolume == 0) return;
            var regulated = Spec.Regulator == RegulatorLocation.AfterSuperheater;
            if (regulated && opening <= 0) return;
            var difference = state.PipeMass - Mass * Spec.PipeVolume / Spec.TotalVolume;
            var head = Math.Abs(PipePressure - ChestPressure);
            var capacity = Spec.MaxFlow * (regulated ? opening : 10)
                * Math.Min(1, Math.Sqrt(head / ((regulated ? ValveDrop : ConnectionDrop) * pressure)));
            var moved = Math.Min(Math.Abs(difference), capacity * dt);
            double mass, heat;
            if (difference >= 0)
            {
                Move(ref state.PipeMass, ref state.PipeHeat, moved, out mass, out heat);
                state.ChestMass += mass; state.ChestHeat += heat;
                if (!regulated) flow.HeaterMass += mass;
                else flow.RegulatorMass += mass;
            }
            else
            {
                Move(ref state.ChestMass, ref state.ChestHeat, moved, out mass, out heat);
                state.PipeMass += mass; state.PipeHeat += heat;
                if (regulated) flow.RegulatorMass -= mass;
            }
        }

        private static void Move(ref double mass, ref double heat, double requested, out double moved, out double carriedHeat)
        {
            moved = Math.Min(mass, Math.Max(0, requested));
            carriedHeat = mass > 0 ? heat * (moved / mass) : 0;
            mass -= moved; heat -= carriedHeat;
            if (mass <= 0) { mass = 0; heat = 0; }
        }
    }
}
