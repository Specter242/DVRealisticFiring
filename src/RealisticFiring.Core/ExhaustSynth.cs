using System;

namespace RealisticFiring.Core
{
 public sealed class ExhaustSpec
    {
        public int Cylinders { get; set; } = 2;
        public float Bore { get; set; } = 0.66f;
        public float Stroke { get; set; } = 0.762f;

        /// <summary>The clearance, a share of the swept volume.</summary>
        public float Clearance { get; set; } = 0.1f;

        public ValveGear Gear { get; set; } = new ValveGear();

        /// <summary>The piston valve's diameter, a share of the bore (as SteamCylinders takes it).</summary>
        public float ValveToBore { get; set; } = 0.5f;

        /// <summary>The port's width, a share of the bore, and the share of the valve's ring open between its bridges.</summary>
        public float PortWidthToBore { get; set; } = 0.08f;
        public float PortOpenShare { get; set; } = 0.85f;

        /// <summary>The blast nozzle's bore, where the exhaust leaves the blast pipe.</summary>
        public float NozzleDiameter { get; set; } = 0.15f;

        /// <summary>The chimney's length from its petticoat to its top, and its bore.</summary>
        public float ChimneyLength { get; set; } = 1.6f;
        public float ChimneyDiameter { get; set; } = 0.48f;

        /// <summary>Superheated steam expands as p·V^1.3 and stays hot; saturated as p·V^1.135 at its boiling point.</summary>
        public bool Superheated { get; set; } = true;

        /// <summary>The steam's temperature at admission when superheated, °C.</summary>
        public float SteamTemperature { get; set; } = 330f;

        internal float PistonArea => (float)(Math.PI / 4.0 * Bore * Bore);

        internal float PortArea => (float)(Math.PI * ValveToBore * Bore * PortOpenShare * PortWidthToBore * Bore);

        internal float NozzleArea => (float)(Math.PI / 4.0 * NozzleDiameter * NozzleDiameter);

        internal float ChimneyArea => (float)(Math.PI / 4.0 * ChimneyDiameter * ChimneyDiameter);

        /// <summary>Each cylinder's front dead center, turns: two cylinders quartered, three at 120°.</summary>
        internal double Quartering(int cylinder) => Cylinders == 2 ? cylinder * 0.25 : (double)cylinder / Math.Max(1, Cylinders);
    }

 public sealed class ExhaustTuning
    {
        /// <summary>The port and nozzle's discharge coefficient.</summary>
        public float Discharge = 0.8f;

        /// <summary>The speed of sound in the chimney's hot mix of steam and smokebox gas, m/s.</summary>
        public float ChimneySoundSpeed = 470f;

        /// <summary>A jet's noise peaks at this Strouhal number: f = St·u/d.</summary>
        public float Strouhal = 0.2f;

        /// <summary>The nozzle jet's noise: its level, how it grows with the jet's speed, and its band's Q.</summary>
        public float JetLevel = 0.768f;
        public float JetSpeedPower = 1.2f;
        public float JetQ = 1.32919f;

        /// <summary>
        /// A source's amplitude grows as the flow feeding it to this power.
        /// A choked jet's speed stands still while its flow falls with the
        /// cylinder's pressure, and its sound power goes with the flow, so
        /// its amplitude goes with the square root: the beat holds a flat
        /// top through the blowdown, as real slow beats do, where an
        /// amplitude in step with the flow tapers from its peak.
        /// </summary>
        public float FlowPower = 0.5f;

        /// <summary>The pressure pulse of each surge of flow (its rate of change radiates as a monopole).</summary>
        public float ThumpLevel = 0.10827f;

        /// <summary>The thump's top, Hz: what the chimney passes of the surge's onset.</summary>
        public float ThumpTop = 221f;

        /// <summary>
        /// The exhaust passages and blast pipe, s: the steam reaches the
        /// nozzle through them, each surge spread a little in time.
        /// </summary>
        public float PassageTime = 0.00614f;

        /// <summary>
        /// The chimney's column of gas takes up the jet's flow in this
        /// time, s (both ways; what the sound does after the flow falls is
        /// the ring's).
        /// </summary>
        public float RoarLag = 0.006f;

        /// <summary>
        /// The front end rings. A real engine's beat dies away at its own
        /// steady rate in time, the same from a start to speed, not in
        /// crank angle: 40 to 115 dB a second from the lineside in six
        /// engines, 40 to 135 in three cabs, the lows slower than the
        /// highs, while the blowdown itself is over in about a tenth of a
        /// second. Part of that is the smokebox and chimney, a steel room
        /// around the jet that keeps sounding, and part the place the
        /// recording was made; the game adds neither. Each source's level
        /// falls no faster than this: the time its amplitude takes to fall
        /// to 1/e, s, for each metre of the chimney's bore (a bigger front
        /// end rings longer: 80 dB a second for the S282's 0.48 m), with
        /// the jet's highs at <see cref="RingHighShare"/> of it and the low
        /// body at <see cref="RingLowShare"/>.
        /// </summary>
        public float RingPerBore = 0.23f;
        public float RingHighShare = 0.62f;
        public float RingLowShare = 1.6f;

        /// <summary>
        /// The ring keeps the color of the beat's loud part: the sources'
        /// peaks follow the jet's speed up at once and down only over this
        /// time, s, so a dying beat doesn't sweep down in pitch.
        /// </summary>
        public float ColorHold = 1.5f;

        /// <summary>
        /// The beat's color with the engine's pace: the core's speed share
        /// stands at this share of itself starting, rising to the whole by
        /// this many turns a second (inferred from real engines' starts and
        /// climbs within one recording, which brighten by about 11 dB in the
        /// 2 to 4 kHz band against 125 to 250 Hz; council round 3).
        /// </summary>
        public float StartingColor = 0.30075f;
        public float ColorAt = 1.5f;

        /// <summary>The radiated top (<see cref="RadiationHigh"/>) starting, as a share of itself: a slow jet's noise has little top; it opens to the whole by <see cref="ColorAt"/>.</summary>
        public float StartingTop = 0.24f;

        /// <summary>The chimney exit's roar: the mixed jet of steam and drawn gas leaving the top; its level, and how it grows with the core's speed.</summary>
        public float RoarLevel = 1.2f;
        public float RoarSpeedPower = 2f;
        public float RoarQ = 0.49813f;

        /// <summary>
        /// The steam jet's core as it leaves the chimney top, not yet mixed
        /// with the gas it draws: its speed, a share of the jet's at the
        /// nozzle, and its bore, a share of the chimney's. The roar peaks at
        /// the Strouhal frequency of these.
        /// </summary>
        public float CoreSpeedShare = 1.1f;
        public float CoreDiameterShare = 0.90226f;

 public float ChimneyReflection = 0.45113f;
        public float MouthCorner = 1f;

        /// <summary>The jet fills the chimney as the pace builds and damps its standing waves: the round trip keeps this share less at <see cref="ColorAt"/> and above.</summary>
        public float PipeDamping = 0.399f;

        /// <summary>
        /// The beat's low body, apart from its upper texture (council round
        /// 5: one envelope and one color through the whole spectrum is what
        /// sounds like noise). Its level, its top (Hz), and the time it
        /// takes to follow the flow, s; it dies away with the ring.
        /// </summary>
        public float BodyLevel = 0.5f;
        public float BodyTop = 164.474f;
        public float BodyRise = 0.004f;

        /// <summary>The nozzle jet's share of its level starting, reaching the whole by <see cref="ColorAt"/>.</summary>
        public float StartingJet = 1f;

 public float BandVariation = 0.7f;
        public float BandWander = 0.5f;
        public float BandWanderRate = 3f;

        /// <summary>
        /// The boiler, cab and running boards the engine carries with it send
        /// back a few short, irregular reflections, so its sound has fine
        /// structure from every side: their gain, a share of the set.
        /// </summary>
        public float EchoGain = 1f;

        /// <summary>
        /// What leaves the chimney top: a small opening radiates the lows
        /// poorly (one pole below this, Hz), and the smokebox and the
        /// chimney's turbulent mixing take the nozzle jet's highs (two poles
        /// above this, Hz).
        /// </summary>
        public float RadiationLow = 122.88f;
        public float RadiationHigh = 6000f;

        /// <summary>
        /// The smokebox's pressure, bar absolute, where the nozzle's jet
        /// leaves: a little under the atmosphere with the draft. The
        /// cylinders' back pressure is not set here; it is the blast pipe's
        /// pressure the series solution finds (council round 2).
        /// </summary>
        public float Smokebox = 0.99f;

        /// <summary>The blast pipe's volume smooths the flow the thump follows, s: the surge can't run ahead of the body (council round 2).</summary>
        public float ThumpSmooth = 0.00132f;

        /// <summary>The flow, kg/s, counted as full flow for the back pressure and the levels.</summary>
        public float FullFlow = 20f;

        /// <summary>A face's release, ± turns, and its port, ± share, set slightly out at random per engine.</summary>
        public float ReleaseSpread = 0.004f;
        public float PortSpread = 0.1f;

        /// <summary>
        /// No two beats alike: each release's pressure varies by this share
        /// (standard deviation), its moment by this many turns (the motion's
        /// slack; the gear itself repeats), and the steam's supply drifts
        /// slowly by this share. Small, as the bands' gains are: at short
        /// cut-off the jet is no longer choked, and its sound follows the
        /// release pressure steeply.
        /// </summary>
        public float BeatVariation = 0.035f;
        public float ReleaseJitter = 0.002f;
        public float Drift = 0.035f;

        /// <summary>The thump is held from growing past this many turns a second (the surge sharpens with speed; the ear hears a roar).</summary>
        public float ThumpKnee = 3.2f;

        /// <summary>The overall level before the soft limit.</summary>
        public float Output = 0.8f;
    }

    /// <summary>The engine as driven, at one moment.</summary>
    public struct ExhaustState
    {
        /// <summary>The crank's turns a second (its sign is ignored).</summary>
        public float TurnsPerSecond;

        /// <summary>Cut-off, a share of the stroke.</summary>
        public float Cutoff;

        /// <summary>The steam chest's pressure at admission, bar absolute.</summary>
        public float Chest;

        public static ExhaustState Lerp(ExhaustState a, ExhaustState b, float t) => new ExhaustState
        {
            TurnsPerSecond = a.TurnsPerSecond + (b.TurnsPerSecond - a.TurnsPerSecond) * t,
            Cutoff = a.Cutoff + (b.Cutoff - a.Cutoff) * t,
            Chest = a.Chest + (b.Chest - a.Chest) * t,
        };
    }

 public sealed class ExhaustSynth
    {
        private const double GasConstant = 461.5;
        private const double Atmosphere = 1e5;
        // The gas is stepped, and the sources' levels and filters set, every this many samples (6 kHz at 48 kHz: the
        // flow moves in milliseconds); the bands' gains every this many of those.
        private const int ControlEvery = 8;
        private const int ColorEvery = 4;

        private readonly ExhaustSpec spec;
        private readonly ExhaustTuning tuning;
        private readonly double rate;
        private readonly Face[] faces;
        private readonly Svf jet = new Svf();
        private readonly Svf roar = new Svf();
        private readonly Svf thumpTop = new Svf();
        private readonly Svf top = new Svf();
        private double lowIn, lowOut;
        private readonly double lowCoefficient;
        private readonly Pipe chimney;
        private readonly Svf bodyTop = new Svf();
        private double body;

        // The beat's color: four bands' gains, per beat and wandering, and the engine's own reflections.
        private static readonly double[] ColorBands = { 250.0, 700.0, 1800.0, 4500.0 };
        private static readonly double[] EchoTimes = { 0.00137, 0.00171, 0.00240, 0.00342, 0.00460 };
        private static readonly double[] EchoShares = { 0.11, -0.09, 0.10, 0.075, -0.06 };
        private readonly Svf[] colorFilters = new Svf[ColorBands.Length];
        private readonly double[] beatColor = new double[ColorBands.Length];
        private readonly double[] colorNow = new double[ColorBands.Length];
        private readonly double[] wanderA = new double[ColorBands.Length];
        private readonly double[] wanderB = new double[ColorBands.Length];
        private readonly double[] colorGain = new double[ColorBands.Length];
        private readonly Random texture;
        private readonly double[] echoLine = new double[1024];
        private readonly int[] echoLags = new int[EchoTimes.Length];
        private int echoAt;
        private uint noise;
        private readonly Random random;
        private double lastOutflow, drift, blastPipe;
        private double outflow, outflowStep, jetSpeed, jetSpeedStep;
        private double jetLevel, jetLevelStep, roarLevel, roarLevelStep, bodyLevel, bodyLevelStep;
        private double coreShare, thumpGain, pipeKeeps = 1.0;
        private readonly double passage, lag, bodyLag, thumpLag;
        private int colorControl;
        private double dcIn, dcOut;
        private int control;
        private double pace;

        // The flow and jet speed at the nozzle, after the passages, and the chimney's lagging flow and core speed.
        private double flow, speed, column, columnSpeed;

        // The ring: each source's level, falling no faster than its share of the ring's time, and the held color.
        private double jetSounding, roarSounding, bodySounding, tone;
        private readonly double jetRing, roarRing, bodyRing, toneHold;

        public ExhaustSynth(ExhaustSpec spec, ExhaustTuning tuning, int sampleRate, int seed)
        {
            this.spec = spec ?? throw new ArgumentNullException(nameof(spec));
            this.tuning = tuning ?? new ExhaustTuning();
            rate = sampleRate;
            noise = (uint)seed | 1u;
            random = new Random(seed);
            texture = new Random(seed ^ 0x5bd1e995);
            faces = new Face[Math.Max(1, spec.Cylinders) * 2];
            for (var c = 0; c < Math.Max(1, spec.Cylinders); c++)
            {
                for (var end = 0; end < 2; end++)
                {
                    faces[c * 2 + end] = new Face
                    {
                        DeadCenter = spec.Quartering(c) + 0.5 * end,
                        ReleaseShift = (random.NextDouble() * 2.0 - 1.0) * this.tuning.ReleaseSpread,
                        PortScale = 1.0 + (random.NextDouble() * 2.0 - 1.0) * this.tuning.PortSpread,
                    };
                }
            }
            var ring = Math.Max(1e-3, this.tuning.RingPerBore * spec.ChimneyDiameter);
            jetRing = Math.Exp(-1.0 / (rate * ring * Math.Max(0.05, this.tuning.RingHighShare)));
            roarRing = Math.Exp(-1.0 / (rate * ring));
            bodyRing = Math.Exp(-1.0 / (rate * ring * Math.Max(0.05, this.tuning.RingLowShare)));
            toneHold = Math.Exp(-1.0 / (rate * Math.Max(1e-3, this.tuning.ColorHold)));
            passage = 1.0 - Math.Exp(-1.0 / (rate * Math.Max(1e-4, this.tuning.PassageTime)));
            lag = 1.0 - Math.Exp(-1.0 / (rate * Math.Max(1e-4, this.tuning.RoarLag)));
            bodyLag = 1.0 - Math.Exp(-1.0 / (rate * Math.Max(1e-4, this.tuning.BodyRise)));
            thumpLag = 1.0 - Math.Exp(-1.0 / (rate * Math.Max(1e-4, this.tuning.ThumpSmooth)));
            thumpTop.Set(this.tuning.ThumpTop, 0.707, rate);
            bodyTop.Set(this.tuning.BodyTop, 0.707, rate);
            var rc = 1.0 / (2.0 * Math.PI * Math.Max(1.0, this.tuning.RadiationLow));
            lowCoefficient = rc / (rc + 1.0 / rate);
            var length = spec.ChimneyLength + 0.6 * spec.ChimneyDiameter;
            var mouth = this.tuning.MouthCorner * this.tuning.ChimneySoundSpeed / (Math.PI * Math.Max(0.05, spec.ChimneyDiameter));
            chimney = new Pipe((int)Math.Round(length / this.tuning.ChimneySoundSpeed * rate), mouth, this.tuning.ChimneyReflection, rate);
            for (var i = 0; i < ColorBands.Length; i++)
            {
                colorFilters[i] = new Svf();
                colorFilters[i].Set(ColorBands[i], 0.8, rate);
            }
            // The reflections' paths scale with the engine.
            var scale = spec.ChimneyLength / 1.6;
            for (var i = 0; i < EchoTimes.Length; i++)
                echoLags[i] = Math.Min(echoLine.Length - 1, Math.Max(1, (int)Math.Round(EchoTimes[i] * scale * rate)));
        }

        /// <summary>
        /// The engine's own reflections as heard from where the listener is, a share of
        /// <see cref="ExhaustTuning.EchoGain"/>. Read on the audio thread.
        /// </summary>
        public double EchoScale { get; set; } = 1.0;

        /// <summary>The crank's position, turns.</summary>
        public double Crank { get; set; }

        /// <summary>The steam leaving the nozzle now, kg/s.</summary>
        public float Flow => (float)flow;

        /// <summary>The jet's speed at the nozzle now, m/s.</summary>
        public float JetSpeed => (float)speed;

        /// <summary>How far past a face's dead center its exhaust opens at <paramref name="cutoff"/>, turns.</summary>
        public double ReleaseTurns(float cutoff)
        {
            var events = spec.Gear.Events(Math.Max(0.05f, Math.Min(0.9f, cutoff)));
            return Math.Acos(1.0 - 2.0 * events.Release) / (2.0 * Math.PI);
        }

        /// <summary>Writes <paramref name="count"/> samples, the engine going from <paramref name="from"/> to <paramref name="to"/> over them.</summary>
        public void Render(float[] buffer, int offset, int count, ExhaustState from, ExhaustState to)
        {
            for (var i = 0; i < count; i++)
            {
                // The engine is read only at the gas's steps: on Unity's Mono, a state made for every sample costs more than it should.
                if (control-- <= 0)
                {
                    control = ControlEvery - 1;
                    Advance(count > 1 ? ExhaustState.Lerp(from, to, (float)i / count) : from);
                }
                buffer[offset + i] = Next();
            }
        }

        private float Next()
        {
            // Between the gas's steps the cylinders' outflow and the jet's speed run straight to the new values.
            outflow += outflowStep;
            jetSpeed += jetSpeedStep;
            flow += (outflow - flow) * passage;
            speed += (jetSpeed - speed) * passage;
            column += (flow - column) * lag;
            columnSpeed += (speed * coreShare - columnSpeed) * lag;
            body += (flow - body) * bodyLag;
            // The color the ring keeps: the jet's speed, up at once and down slowly.
            tone = speed > tone ? speed : speed + (tone - speed) * toneHold;

            // The surge's thump: the cylinders' outflow's rate of change as the port opens, rounded off by the
            // chimney, held from growing with speed past the knee.
            var smoothed = lastOutflow + (outflow - lastOutflow) * thumpLag;
            var thump = thumpGain * thumpTop.LowPass(smoothed - lastOutflow);
            lastOutflow = smoothed;

            // Each source sounds at its level, or what the ring still holds of it.
            jetLevel += jetLevelStep;
            roarLevel += roarLevelStep;
            bodyLevel += bodyLevelStep;
            jetSounding = Math.Max(jetLevel, jetSounding * jetRing);
            roarSounding = Math.Max(roarLevel, roarSounding * roarRing);
            bodySounding = Math.Max(bodyLevel, bodySounding * bodyRing);
            var sources = thump + jetSounding * jet.BandPass(Noise()) + roarSounding * roar.BandPass(Noise())
                + bodySounding * bodyTop.LowPass(Noise());

            // The jet mixes with the drawn gas mostly inside the chimney, so all of it sounds through its standing waves.
            var coloured = Color(chimney.Step(sources, pipeKeeps));

            // Radiated from the top: a pole up from the lows, two down from the highs.
            var high = lowCoefficient * (lowOut + coloured - lowIn);
            lowIn = coloured;
            lowOut = high;
            var radiated = top.LowPass(high);

            // A DC blocker, then a soft limit.
            var y = radiated - dcIn + 0.995 * dcOut;
            dcIn = radiated;
            dcOut = y;
            return (float)Math.Tanh(tuning.Output * y);
        }

        // The gas's step, every ControlEvery samples: the crank, the faces, the blast pipe, and from them the
        // sources' levels and colors the samples between run toward.
        private void Advance(ExhaustState state)
        {
            var seconds = ControlEvery / rate;
            var turnsPerSecond = Math.Abs(state.TurnsPerSecond);
            var turns = turnsPerSecond * seconds;
            var previous = Crank;
            Crank += turns;
            var events = spec.Gear.Events(Math.Max(0.05f, Math.Min(0.9f, state.Cutoff)));
            var release = Math.Acos(1.0 - 2.0 * events.Release) / (2.0 * Math.PI);
            var closes = 1.0 - Math.Acos(1.0 - 2.0 * events.Compression) / (2.0 * Math.PI);
            var backPa = Math.Max(0.5, tuning.Smokebox) * Atmosphere;

            // The open faces, their pressures and their ports' areas.
            var open = 0;
            var highest = backPa;
            foreach (var face in faces)
            {
                var at = Turn(Crank - face.DeadCenter);
                var was = Turn(previous - face.DeadCenter);
                var opensAt = release + face.ReleaseShift + face.Jitter;
                if (!face.Open && turns > 0.0 && was < opensAt && at >= opensAt && at < closes)
                    Open(face, events, state, at);
                if (!face.Open)
                    continue;
                if (at - face.ReleaseShift - face.Jitter >= closes || at < was)
                {
                    face.Open = false;
                    // The next beat's jitter, drawn now so its opening test and its port agree (council round 3).
                    face.Jitter = tuning.ReleaseJitter * Gaussian();
                    continue;
                }
                Gauge(face, events, at);
                face.Counted = face.Area > 0.0;
                if (face.Counted)
                {
                    open++;
                    highest = Math.Max(highest, face.Pressure);
                }
            }

            // The port and the blast nozzle are two orifices in series: the blast pipe stands at the pressure where
            // what the ports pass is what the nozzle passes. Found by halving.
            if (open == 0)
                blastPipe = backPa;
            else
            {
                double lo = backPa, hi = highest;
                for (var i = 0; i < 12; i++)
                {
                    var mid = 0.5 * (lo + hi);
                    if (PortFlow(mid) > NozzleFlow(mid, backPa, out _))
                        lo = mid;
                    else
                        hi = mid;
                }
                blastPipe = 0.5 * (lo + hi);
            }

            var total = 0.0;
            foreach (var face in faces)
            {
                if (!face.Open || !face.Counted)
                    continue;
                var mass = Orifice(face.Area, face.Pressure, blastPipe, face.Temperature, face.Gamma, out _);
                face.Mass = Math.Max(1e-6, face.Mass - mass * seconds);
                total += mass;
            }
            NozzleFlow(blastPipe, backPa, out var velocity);
            outflowStep = (total - outflow) / ControlEvery;
            jetSpeedStep = ((total > 1e-6 ? velocity : 0.0) - jetSpeed) / ControlEvery;

            // The sources' levels from the flow and the jet's speed as they stand.
            pace = Math.Min(1.0, turnsPerSecond / Math.Max(0.05, tuning.ColorAt));
            coreShare = tuning.CoreSpeedShare * (tuning.StartingColor + (1.0 - tuning.StartingColor) * pace);
            thumpGain = tuning.ThumpLevel * rate / (tuning.FullFlow * 50.0) / (1.0 + turnsPerSecond / Math.Max(0.1, tuning.ThumpKnee));
            pipeKeeps = Math.Sqrt(Math.Max(0.0, 1.0 - tuning.PipeDamping * pace));
            var jetNow = tuning.JetLevel * (tuning.StartingJet + (1.0 - tuning.StartingJet) * pace)
                * Strength(flow) * Math.Pow(Math.Max(0.0, speed) / 450.0, tuning.JetSpeedPower);
            var roarNow = tuning.RoarLevel * Strength(column) * Math.Pow(Math.Max(0.0, columnSpeed) / 250.0, tuning.RoarSpeedPower);
            var bodyNow = tuning.BodyLevel * Strength(body);
            jetLevelStep = (jetNow - jetLevel) / ControlEvery;
            roarLevelStep = (roarNow - roarLevel) / ControlEvery;
            bodyLevelStep = (bodyNow - bodyLevel) / ControlEvery;

            var nozzle = Math.Max(1e-3, spec.NozzleDiameter);
            jet.Set(Clamp(tuning.Strouhal * tone / nozzle, 60.0, 9000.0), tuning.JetQ, rate);
            var core = Math.Max(0.03, spec.ChimneyDiameter * tuning.CoreDiameterShare);
            roar.Set(Clamp(tuning.Strouhal * tone * coreShare / core, 25.0, 2500.0), tuning.RoarQ, rate);
            top.Set(tuning.RadiationHigh * (tuning.StartingTop + (1.0 - tuning.StartingTop) * pace), 0.707, rate);

            if (colorControl-- <= 0)
            {
                colorControl = ColorEvery - 1;
                Colors();
            }
        }

        // A source's strength from the flow feeding it, kg/s.
        private double Strength(double feed) => Math.Pow(Math.Max(0.0, feed) / tuning.FullFlow, tuning.FlowPower);

        // The bands' gains: each beat's drawn color, eased in, and the slow wander.
        private void Colors()
        {
            var step = ControlEvery * ColorEvery / rate;
            var ease = 1.0 - Math.Exp(-step / 0.008);
            var wander = 1.0 - Math.Exp(-2.0 * Math.PI * Math.Max(0.1, tuning.BandWanderRate) * step);
            var unit = Math.Sqrt(4.0 / wander);
            for (var i = 0; i < ColorBands.Length; i++)
            {
                colorNow[i] += (beatColor[i] - colorNow[i]) * ease;
                wanderA[i] += (TextureGaussian() - wanderA[i]) * wander;
                wanderB[i] += (wanderA[i] - wanderB[i]) * wander;
                colorGain[i] = Math.Pow(10.0, (colorNow[i] + tuning.BandWander * wanderB[i] * unit) / 20.0) - 1.0;
            }
        }

        // The engine's own short reflections, then each band's gain.
        private double Color(double x)
        {
            echoLine[echoAt] = x;
            var y = x;
            for (var i = 0; i < echoLags.Length; i++)
                y += tuning.EchoGain * EchoScale * EchoShares[i] * echoLine[(echoAt - echoLags[i] + echoLine.Length) % echoLine.Length];
            echoAt = echoAt + 1 < echoLine.Length ? echoAt + 1 : 0;
            var sum = y;
            for (var i = 0; i < colorFilters.Length; i++)
                sum += colorGain[i] * colorFilters[i].BandPass(y);
            return sum;
        }

        private void Open(Face face, ValveEvents events, ExhaustState state, double at)
        {
            for (var i = 0; i < beatColor.Length; i++)
                beatColor[i] = tuning.BandVariation * TextureGaussian();
            var exponent = spec.Superheated ? 1.3 : 1.135;
            var cycle = new CylinderCycle(events, spec.Clearance, (float)exponent, Math.Max(1.01f, state.Chest), 1f);
            drift = 0.85 * drift + 0.15 * Gaussian();
            var vary = Math.Exp(tuning.BeatVariation * Gaussian() + tuning.Drift * drift * 2.5);
            var pressure = Math.Max(1.0, cycle.ReleasePressure * vary) * Atmosphere;
            var temperature = spec.Superheated
                ? (spec.SteamTemperature + 273.15) * Math.Pow(pressure / (Math.Max(1.01, state.Chest) * Atmosphere), (exponent - 1.0) / exponent)
                : Boiling(pressure / Atmosphere);
            face.Open = true;
            face.Gamma = exponent;
            face.Pressure0 = pressure;
            face.Density0 = pressure / (GasConstant * temperature);
            face.Mass = face.Density0 * Volume(at);
        }

        // A face's state this sample: its pressure, temperature and port.
        private void Gauge(Face face, ValveEvents events, double at)
        {
            var density = face.Mass / Volume(at);
            face.Pressure = face.Pressure0 * Math.Pow(density / face.Density0, face.Gamma);
            face.Temperature = face.Pressure / (density * GasConstant);
            // The port opens from the face's own release: its setting and this beat's jitter shift the whole opening.
            var own = at - face.ReleaseShift - face.Jitter;
            face.Area = tuning.Discharge * spec.PortArea * face.PortScale * events.ExhaustOpening((float)(own * 2.0 * Math.PI));
        }

        private double TextureGaussian()
        {
            var u = 1.0 - texture.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u)) * Math.Cos(2.0 * Math.PI * texture.NextDouble());
        }

        // What the open ports pass into a blast pipe at <paramref name="pipe"/> Pa, kg/s.
        private double PortFlow(double pipe)
        {
            var sum = 0.0;
            foreach (var face in faces)
                if (face.Open && face.Counted)
                    sum += Orifice(face.Area, face.Pressure, pipe, face.Temperature, face.Gamma, out _);
            return sum;
        }

        // What the nozzle passes from a blast pipe at <paramref name="pipe"/> Pa, kg/s, and the jet's speed; the
        // steam's temperature the open faces' mean (throttling keeps its enthalpy).
        private double NozzleFlow(double pipe, double back, out double velocity)
        {
            double weight = 0.0, temperature = 0.0, gamma = 1.3;
            foreach (var face in faces)
            {
                if (face.Open && face.Counted)
                {
                    weight += face.Area;
                    temperature += face.Area * face.Temperature;
                    gamma = face.Gamma;
                }
            }
            temperature = weight > 0.0 ? temperature / weight : 400.0;
            return Orifice(tuning.Discharge * spec.NozzleArea, pipe, back, temperature, gamma, out velocity);
        }

        private double Gaussian()
        {
            var u = 1.0 - random.NextDouble();
            var v = random.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u)) * Math.Cos(2.0 * Math.PI * v);
        }

        // Gas through an orifice of <paramref name="area"/>, kg/s; choked below the critical pressure ratio.
        private static double Orifice(double area, double pressure, double back, double temperature, double gamma, out double velocity)
        {
            velocity = 0.0;
            if (!(area > 0.0) || !(pressure > back))
                return 0.0;
            var ratio = back / pressure;
            var critical = Math.Pow(2.0 / (gamma + 1.0), gamma / (gamma - 1.0));
            if (ratio <= critical)
            {
                velocity = Math.Sqrt(2.0 * gamma / (gamma + 1.0) * GasConstant * temperature);
                return area * pressure * Math.Sqrt(gamma / (GasConstant * temperature))
                       * Math.Pow(2.0 / (gamma + 1.0), (gamma + 1.0) / (2.0 * (gamma - 1.0)));
            }
            var term = Math.Pow(ratio, 2.0 / gamma) - Math.Pow(ratio, (gamma + 1.0) / gamma);
            velocity = Math.Sqrt(2.0 * gamma / (gamma - 1.0) * GasConstant * temperature * (1.0 - Math.Pow(ratio, (gamma - 1.0) / gamma)));
            return area * pressure * Math.Sqrt(2.0 * gamma / ((gamma - 1.0) * GasConstant * temperature) * Math.Max(0.0, term));
        }

        // The face's cylinder volume at <paramref name="at"/> turns from its dead center, m³.
        private double Volume(double at)
        {
            var piston = (1.0 - Math.Cos(at * 2.0 * Math.PI)) / 2.0;
            return spec.PistonArea * spec.Stroke * (spec.Clearance + piston);
        }

        // Saturated steam's temperature at <paramref name="bar"/> absolute, K (within a few K from 1 to 20 bar).
        private static double Boiling(double bar) => 373.15 + 100.0 * (Math.Pow(Math.Max(0.1, bar), 0.25) - 1.0);

        private static double Turn(double turns) => turns - Math.Floor(turns);

        private static double Clamp(double x, double low, double high) => x < low ? low : x > high ? high : x;

        private double Noise()
        {
            noise ^= noise << 13;
            noise ^= noise >> 17;
            noise ^= noise << 5;
            return noise / 2147483648.0 - 1.0;
        }

        private sealed class Face
        {
            public double DeadCenter;
            public double ReleaseShift;
            public double PortScale;
            public bool Open;
            public double Gamma;
            public double Pressure0;
            public double Density0;
            public double Mass;
            public double Jitter;
            public double Pressure;
            public double Temperature;
            public double Area;
            public bool Counted;
        }

        /// <summary>
        /// A pipe open at both ends as two delay lines, one each way. A wave
        /// reaching an end comes back inverted, its lows by the square root
        /// of the round trip's share and its highs let out (one pole); what
        /// leaves the top is the flow there, the wave arriving less the one
        /// sent back.
        /// </summary>
        private sealed class Pipe
        {
            private readonly double[] up, down;
            private readonly double gain, corner;
            private double topLow, bottomLow;
            private int at;

            public Pipe(int samples, double mouth, double roundTrip, double rate)
            {
                up = new double[Math.Max(1, samples)];
                down = new double[Math.Max(1, samples)];
                gain = Math.Sqrt(Clamp(roundTrip, 0.0, 0.98));
                corner = 1.0 - Math.Exp(-2.0 * Math.PI * Math.Max(1.0, mouth) / rate);
            }

            /// <param name="input">The sound at the pipe's foot.</param>
            /// <param name="keeps">The share of each end's reflection the flow leaves (the square root of the round trip's).</param>
            public double Step(double input, double keeps)
            {
                var top = up[at];
                var bottom = down[at];
                topLow += (top - topLow) * corner;
                bottomLow += (bottom - bottomLow) * corner;
                var each = gain * keeps;
                var back = -each * topLow;
                up[at] = input - each * bottomLow;
                down[at] = back;
                at = at + 1 < up.Length ? at + 1 : 0;
                return top - back;
            }
        }

        /// <summary>A state-variable filter (the trapezoidal form): stable while its frequency moves.</summary>
        private sealed class Svf
        {
            private double a1, a2, a3, k;
            private double ic1, ic2;

            public void Set(double frequency, double q, double sampleRate)
            {
                var g = Math.Tan(Math.PI * Math.Min(frequency, sampleRate * 0.45) / sampleRate);
                k = 1.0 / Math.Max(0.1, q);
                a1 = 1.0 / (1.0 + g * (g + k));
                a2 = g * a1;
                a3 = g * a2;
            }

            private void Step(double x, out double band, out double low)
            {
                var v3 = x - ic2;
                band = a1 * ic1 + a2 * v3;
                low = ic2 + a2 * ic1 + a3 * v3;
                ic1 = 2.0 * band - ic1;
                ic2 = 2.0 * low - ic2;
            }

            /// <summary>Band-pass at unit gain at its center.</summary>
            public double BandPass(double x)
            {
                Step(x, out var band, out _);
                return k * band;
            }

            public double LowPass(double x)
            {
                Step(x, out _, out var low);
                return low;
            }
        }
    }
}
