using System;

namespace RealisticFiring.Core
{
 public sealed class ValveGear
    {
        /// <summary>Steam lap: the valve's overlap of the steam port in mid position.</summary>
        public float Lap { get; set; } = 1.5f;

        /// <summary>Lead: the steam port's opening at dead center, the same in every notch.</summary>
        public float Lead { get; set; } = 0.25f;

        /// <summary>Exhaust lap (negative for exhaust clearance); 0 is line and line.</summary>
        public float ExhaustLap { get; set; }

        /// <summary>The steam port's width: an opening past it passes no more steam.</summary>
        public float PortWidth { get; set; } = 2f;

        /// <summary>
        /// The events at <paramref name="cutoff"/> (a share of the stroke). The
        /// valve's displacement is A·sin θ + B·cos θ of the crank angle from
        /// dead center, B the lap and lead (fixed), A set by the reverser;
        /// the piston stands at (1 − cos θ)/2 of its stroke, as in the game.
        /// </summary>
        public ValveEvents Events(float cutoff)
        {
            var c = Math.Min(Math.Max(cutoff, 0.001), 0.999);
            var cutoffAngle = Math.Acos(1.0 - 2.0 * c);
            var b = Lap + Lead;
            var a = (Lap - b * Math.Cos(cutoffAngle)) / Math.Sin(cutoffAngle);
            var travel = Math.Sqrt(a * a + b * b);
            var phase = Math.Atan2(b, a);
            var admissionAngle = Math.Asin(Math.Min(1.0, Lap / travel)) - phase;
            var exhaust = Math.Asin(Math.Max(-1.0, Math.Min(1.0, ExhaustLap / travel)));
            var releaseAngle = Math.PI + exhaust - phase;
            var compressionAngle = 2.0 * Math.PI - exhaust - phase;
            var release = Math.Max(c, Math.Min(1.0, (1.0 - Math.Cos(releaseAngle)) / 2.0));
            var compression = Math.Max(0.0, Math.Min(1.0, (1.0 - Math.Cos(compressionAngle)) / 2.0));
            var events = new ValveEvents((float)c, (float)release, (float)compression, (float)admissionAngle, (float)cutoffAngle,
                (float)travel, (float)phase, Lap, PortWidth, 0f);
            return events.WithPortWindow();
        }
    }

    /// <summary>One face of a cylinder's valve events in a notch.</summary>
    public readonly struct ValveEvents
    {
        private const int WindowSteps = 24;

        private readonly float travel;
        private readonly float phase;
        private readonly float lap;
        private readonly float portWidth;

        public ValveEvents(float cutoff, float release, float compression, float admissionAngle, float cutoffAngle,
            float travel, float phase, float lap, float portWidth, float portWindow)
        {
            PortWindow = portWindow;
            Cutoff = cutoff;
            Release = release;
            Compression = compression;
            AdmissionAngle = admissionAngle;
            CutoffAngle = cutoffAngle;
            this.travel = travel;
            this.phase = phase;
            this.lap = lap;
            this.portWidth = portWidth;
        }

        /// <summary>Where steam is cut off, a share of the stroke.</summary>
        public float Cutoff { get; }

        /// <summary>Where the exhaust opens (release), a share of the stroke.</summary>
        public float Release { get; }

        /// <summary>The share of the return stroke left when the exhaust closes, compressing the cushion.</summary>
        public float Compression { get; }

        /// <summary>Crank angle where the steam port opens, radians from dead center (negative: before it, the lead).</summary>
        public float AdmissionAngle { get; }

        /// <summary>Crank angle at cut-off, radians from dead center.</summary>
        public float CutoffAngle { get; }

        /// <summary>Half the valve's travel in this notch, in the gear's units.</summary>
        public float HalfTravel => travel;

        /// <summary>Crank turns the steam port is open a stroke, lead included.</summary>
        public float AdmissionTurns => Math.Max(0f, CutoffAngle - AdmissionAngle) / (2f * (float)Math.PI);

        /// <summary>
        /// The admission in crank turns weighed by the port's opening: turns
        /// the port would be wide open to pass the same steam. Steam admitted
        /// a stroke is the open port's flow times this, over the turns a second.
        /// </summary>
        public float PortWindow { get; }

        /// <summary>The steam port's opening at a crank angle, a share of its width.</summary>
        public float Opening(float crankAngle)
        {
            var open = travel * Math.Sin(crankAngle + phase) - lap;
            return (float)Math.Min(1.0, Math.Max(0.0, open / portWidth));
        }

        internal ValveEvents WithPortWindow()
        {
            var span = CutoffAngle - AdmissionAngle;
            if (!(span > 0f))
                return this;
            var step = span / WindowSteps;
            var sum = 0.0;
            for (var i = 0; i < WindowSteps; i++)
                sum += Opening(AdmissionAngle + (i + 0.5f) * step);
            return new ValveEvents(Cutoff, Release, Compression, AdmissionAngle, CutoffAngle, travel, phase, lap, portWidth,
                (float)(sum * step / (2.0 * Math.PI)));
        }
    }

    /// <summary>
    /// A cylinder face's indicator diagram (absolute bar over the stroke's
    /// share): steam at the admission pressure to cut-off, expanding as
    /// p·Vⁿ to release, falling to the back pressure by the stroke's end; on
    /// the return, exhaust at the back pressure until the exhaust closes, then
    /// the cushion compressed into the clearance. Volumes include the
    /// clearance, a share of the swept volume.
    /// </summary>
    public readonly struct CylinderCycle
    {
        public CylinderCycle(ValveEvents events, float clearance, float exponent, float admission, float back)
        {
            Events = events;
            Clearance = clearance;
            Exponent = exponent;
            Admission = admission;
            Back = back;
        }

        public ValveEvents Events { get; }
        public float Clearance { get; }
        public float Exponent { get; }
        public float Admission { get; }
        public float Back { get; }

        /// <summary>The pressure in the cylinder as the exhaust opens: what each beat carries up the stack.</summary>
        public float ReleasePressure => Expanded(Events.Release);

        /// <summary>Pressure on the working face at <paramref name="s"/> of its stroke.</summary>
        public float Working(float s)
        {
            if (s <= Events.Cutoff)
                return Admission;
            if (s <= Events.Release)
                return Expanded(s);
            var toe = Events.Release < 1f ? (s - Events.Release) / (1f - Events.Release) : 1f;
            return ReleasePressure + (Back - ReleasePressure) * Math.Min(1f, toe);
        }

        /// <summary>Pressure on a face in its return stroke, <paramref name="s"/> of the stroke from its own dead center.</summary>
        public float Returning(float s)
        {
            var k = Events.Compression;
            return s >= k ? Back : Back * (float)Math.Pow((k + Clearance) / (Math.Max(0f, s) + Clearance), Exponent);
        }

        /// <summary>
        /// Mean effective pressure: the working face's mean less the returning
        /// face's, in bar. The atmosphere acts on both and drops out.
        /// </summary>
        public float MeanEffectivePressure
        {
            get
            {
                var c = Events.Cutoff;
                var r = Events.Release;
                var k = Events.Compression;
                var cl = Clearance;
                var n = Exponent;
                var working = Admission * c
                              + Admission * Math.Pow(c + cl, n) * Integral(c, r)
                              + (ReleasePressure + Back) / 2.0 * (1.0 - r);
                var returning = Back * (1.0 - k) + Back * Math.Pow(k + cl, n) * Integral(0f, k);
                return (float)(working - returning);
            }
        }

        private float Expanded(float s) =>
            Admission * (float)Math.Pow((Events.Cutoff + Clearance) / (s + Clearance), Exponent);

        // ∫ (s + clearance)^-n ds from a to b.
        private double Integral(float a, float b)
        {
            var low = a + Clearance;
            var high = b + Clearance;
            if (Math.Abs(Exponent - 1f) < 1e-4f)
                return Math.Log(high / low);
            var m = 1.0 - Exponent;
            return (Math.Pow(high, m) - Math.Pow(low, m)) / m;
        }
    }

    /// <summary>Steam admitted to a cylinder in a stroke.</summary>
    public struct CylinderAdmission
    {
        /// <summary>Steam taken from the steam chest, in kg.</summary>
        public float Mass;

        /// <summary>The cylinder's pressure to cut-off, absolute bar.</summary>
        public float Pressure;

        /// <summary>Exhaust steam kept as cushion, in kg.</summary>
        public float Cushion;

        /// <summary>Of <see cref="Mass"/>, steam the walls condensed, in kg: it leaves with the exhaust having done no work.</summary>
        public float Condensed;
    }

 public sealed class CylinderSpec
    {
        public ValveGear Gear { get; set; } = new ValveGear();

        /// <summary>Clearance volume, a share of the swept volume: "around 10%" on later locomotives.</summary>
        public float Clearance { get; set; } = 0.1f;

        /// <summary>The expansion and compression exponent of dry saturated steam, and of well superheated steam.</summary>
        public float SaturatedExponent { get; set; } = 1.135f;
        public float SuperheatedExponent { get; set; } = 1.3f;

        /// <summary>Superheat that makes steam expand fully as superheated, in °C.</summary>
        public float FullSuperheat { get; set; } = 100f;

        /// <summary>
        /// Saturated steam's initial condensation: the share of the steam
        /// admitted that the cylinder walls condense, at a quarter cut-off,
        /// <see cref="CondensationTurns"/>, and <see cref="CondensationPressure"/>
        /// in <see cref="CondensationBore"/> by <see cref="CondensationStroke"/>
        /// cylinders. Purdue's Schenectady No. 1 showed 72 to 80 % of its
        /// steam on the indicator cards at 25 % cut-off (Goss, 1907, pp.
        /// 112–114). Heck's formula scales it (The Steam Engine and Turbine,
        /// 1911, eq. 126): as the root of the walls' surface over the volume
        /// and of one over the cut-off and clearance, the cube root of one
        /// over the speed, and about the cube root of one over the pressure.
        /// </summary>
        public float QuarterCondensation { get; set; } = 0.25f;

        public float CondensationTurns { get; set; } = 2f;

        /// <summary>Absolute bar: Goss's engine ran at about 130 psi.</summary>
        public float CondensationPressure { get; set; } = 10f;

        /// <summary>Goss's cylinders, 17 by 24 in., in m.</summary>
        public float CondensationBore { get; set; } = 0.432f;
        public float CondensationStroke { get; set; } = 0.61f;

        /// <summary>
        /// Condensation a °C of superheat prevents, a share of the steam
        /// admitted: 7.5 °F of superheat for each percent (Ripper).
        /// </summary>
        public float CondensationPerSuperheat { get; set; } = 0.0024f;

        /// <summary>The blast nozzle's back pressure at the engine's full steam flow, bar over the atmosphere; it goes as the flow squared.</summary>
        public float BackPressureAtFullFlow { get; set; } = 0.7f;

        /// <summary>Piston valve diameter over cylinder bore (12-in. valves on 25-in. cylinders).</summary>
        public float ValveToBore { get; set; } = 0.5f;

        /// <summary>Steam port width over cylinder bore (2 in. on 25 in.), matching <see cref="ValveGear.PortWidth"/>.</summary>
        public float PortWidthToBore { get; set; } = 0.08f;

        /// <summary>The share of the valve's circumference open, between the bridges across the port.</summary>
        public float PortOpenShare { get; set; } = 0.85f;

        /// <summary>The port's discharge coefficient, on Napier's flow of steam.</summary>
        public float DischargeCoefficient { get; set; } = 0.8f;
    }

    /// <summary>
    /// A locomotive's cylinders as a steam engine sees them each tick: the
    /// notch's valve events, the steam's exponent, and the back pressure, and
    /// from them the steam each stroke admits and the indicator diagram.
    /// </summary>
    public sealed class SteamCylinders
    {
        // Napier's rule, w (lb/s) = A (in²) · P (psia) / 70, in kg/s per m² per bar.
        private const float NapierPerSquareMetreBar = 145.7f;

        private readonly float maxFlow;

        // These cylinders' walls against Goss's, for the condensation.
        private readonly float walls;

        public SteamCylinders(CylinderSpec spec, float bore, float stroke, float maxFlow)
        {
            Spec = spec ?? new CylinderSpec();
            this.maxFlow = maxFlow > 0f ? maxFlow : 1f;
            // Heck's surface over volume, ends and barrel: 2/L + 4/D.
            float Walls(float d, float l) => 2f / Math.Max(0.1f, l) + 4f / Math.Max(0.1f, d);
            walls = (float)Math.Sqrt(Walls(bore, stroke) / Walls(Spec.CondensationBore, Spec.CondensationStroke));
            var portArea = (float)Math.PI * Spec.ValveToBore * bore * Spec.PortOpenShare * Spec.PortWidthToBore * bore;
            PortFlowPerBar = Spec.DischargeCoefficient * NapierPerSquareMetreBar * portArea;
            Events = Spec.Gear.Events(0.5f);
            Exponent = Spec.SaturatedExponent;
            BackPressure = 1f;
        }

        public CylinderSpec Spec { get; }

        /// <summary>Steam the wide-open port passes per bar in the steam chest, in kg/s.</summary>
        public float PortFlowPerBar { get; }

        public ValveEvents Events { get; private set; }
        public float Exponent { get; private set; }
        public float BackPressure { get; private set; }

        /// <summary>The steam's superheat at the chest this tick, in °C.</summary>
        public float Superheat { get; private set; }

        /// <summary>Whether wet steam condenses on these walls; an engine left as it was has none.</summary>
        public bool WallCondensation { get; set; } = true;

        /// <summary>
        /// Steam the cylinder walls take on admission at
        /// <paramref name="turnsPerSecond"/> from a chest at
        /// <paramref name="chestPressure"/> absolute bar, over the steam that
        /// works: saturated steam's condensation at this tick's cut-off, less
        /// what the superheat prevents.
        /// </summary>
        public float Condensation(float turnsPerSecond, float chestPressure)
        {
            if (!WallCondensation || Spec.QuarterCondensation <= 0f)
                return 0f;
            var cutoff = Math.Max(0.05f, Events.Cutoff);
            var turns = Math.Max(0.25f, Math.Abs(turnsPerSecond));
            var pressure = Math.Max(1f, chestPressure);
            var saturated = Spec.QuarterCondensation * walls * (float)(
                Math.Sqrt((0.25 + Spec.Clearance) / (cutoff + Spec.Clearance))
                * Math.Pow(Spec.CondensationTurns / turns, 1.0 / 3.0)
                * Math.Pow(Spec.CondensationPressure / pressure, 1.0 / 3.0));
            var condensed = Math.Min(0.6f, saturated) - Superheat * Spec.CondensationPerSuperheat;
            return condensed > 0f ? condensed / (1f - condensed) : 0f;
        }

        /// <summary>Sets this tick's notch, steam, and exhaust.</summary>
        public void Set(float cutoff, float exhaustFlow, float intakePressure, float intakeTemperature)
        {
            Events = Spec.Gear.Events(cutoff);
            Superheat = Math.Max(0f, intakeTemperature - SaturationTemperature(intakePressure));
            var share = Math.Min(1f, Superheat / Spec.FullSuperheat);
            Exponent = Spec.SaturatedExponent + (Spec.SuperheatedExponent - Spec.SaturatedExponent) * share;
            var flow = Math.Min(1.5f, Math.Max(0f, exhaustFlow) / maxFlow);
            BackPressure = 1f + Spec.BackPressureAtFullFlow * flow * flow;
        }

        /// <summary>The indicator diagram at this tick's events for <paramref name="admission"/> bar.</summary>
        public CylinderCycle Cycle(float admission) =>
            new CylinderCycle(Events, Spec.Clearance, Exponent, admission, BackPressure);

        /// <summary>
        /// Steam a stroke admits from the steam chest (its pressure, steam, and
        /// volume in L) into a cylinder of <paramref name="cylinderVolume"/> L
        /// at <paramref name="turnsPerSecond"/>. It is the lesser of what the
        /// chest gives as the two fill to one pressure (the cushion already in
        /// the cylinder counted), and what the port passes while it opens and
        /// closes. The throttle refills the chest between strokes, so its limit
        /// shows as the chest's pressure. The walls' condensation takes steam
        /// as a larger cylinder would: drawn from the chest and through the
        /// port, but doing no work. Steam volumes are L/kg.
        /// </summary>
        public CylinderAdmission Admit(float chestPressure, float chestSteam, float chestVolume, float cylinderVolume,
            float turnsPerSecond, Func<float, float> steamVolume, Func<float, float> steamPressure)
        {
            var condensation = Condensation(turnsPerSecond, chestPressure);
            var filled = (Events.Cutoff + Spec.Clearance) * cylinderVolume * (1f + condensation);
            var cushion = (Events.Compression + Spec.Clearance) * cylinderVolume / steamVolume(BackPressure);
            var shared = filled / (filled + chestVolume) * (chestSteam + cushion) - cushion;
            var timed = turnsPerSecond > 0f ? PortFlowPerBar * Math.Max(1f, chestPressure) * Events.PortWindow / turnsPerSecond : shared;
            var mass = Math.Max(0f, Math.Min(shared, timed));
            var pressure = mass > 0f ? Math.Min(chestPressure, steamPressure(filled / (mass + cushion))) : 1f;
            return new CylinderAdmission
            {
                Mass = mass, Pressure = Math.Max(1f, pressure), Cushion = cushion,
                Condensed = mass * condensation / (1f + condensation),
            };
        }

        /// <summary>Saturated steam's temperature at an absolute pressure in bar, in °C (within 3 °C from 1 to 20 bar).</summary>
        public static float SaturationTemperature(float pressure) =>
            99.6f * (float)Math.Pow(Math.Max(0.05f, pressure), 0.25);
    }
}
