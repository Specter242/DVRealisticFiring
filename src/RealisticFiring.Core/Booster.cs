using System;

namespace RealisticFiring.Core
{
    public enum BoosterState
    {
        /// <summary>Out of gear, the engine still (or coasting to a stop).</summary>
        CutOut,

        /// <summary>Out of gear, turned slowly by preliminary steam to warm and drain it.</summary>
        Idling,

        /// <summary>In gear, its cylinder cocks still open: little pressure in the cylinders.</summary>
        CocksOpen,

        /// <summary>In gear and pulling.</summary>
        Working,
    }

    /// <summary>Something the crew would hear or feel, reported on the tick it happens.</summary>
    public enum BoosterEvent
    {
        None,

        /// <summary>The latch was raised too fast or with the engine turning: the gears clashed and did not mesh.</summary>
        Clash,

        /// <summary>Cut in cold: water in the cylinders.</summary>
        Priming,
    }

    /// <summary>
    /// A Franklin Type C-2 locomotive booster's design figures, as on the NYC
    /// Hudsons: a two-cylinder, double-acting, non-reversing engine geared to
    /// the trailer axle. Lengths in m, pressures in bar gauge, speeds in m/s.
    /// </summary>
    public sealed class BoosterSpec
    {
        /// <summary>10 in.</summary>
        public float CylinderBore { get; set; } = 0.254f;

        /// <summary>12 in.</summary>
        public float PistonStroke { get; set; } = 0.3048f;

        /// <summary>Crank turns per trailer wheel turn: 36:14.</summary>
        public float GearRatio { get; set; } = 2.571f;

        /// <summary>The trailer wheels it drives: 51 in on the J-3a.</summary>
        public float WheelDiameter { get; set; } = 1.2954f;

        /// <summary>Its rating pressure, 250 psi: the J-3a's 12,100 lb is "@ 250 lbs press.".</summary>
        public float MaxInletPressure { get; set; } = 17.24f;

        /// <summary>Cylinder pressure while the cocks are open: 30–40 psi.</summary>
        public float CockPressure { get; set; } = 2.4f;

        /// <summary>Seconds before the cocks close after cutting in (Franklin's recommendation).</summary>
        public float CockDelay { get; set; } = 20f;

        /// <summary>Steam blowing from the open cocks, kg/s (an estimate).</summary>
        public float CockBlow { get; set; } = 0.05f;

        /// <summary>Fastest it may be cut in: 12 mph (C-2).</summary>
        public float CutInSpeed { get; set; } = 5.364f;

        /// <summary>Fastest it may run in gear: 21 mph (C-2).</summary>
        public float RunningLimit { get; set; } = 9.388f;

        /// <summary>The reverser's cut-off must be at least this, forward, to raise the latch.</summary>
        public float LatchCutoff { get; set; } = 0.66f;

        /// <summary>Its own fixed cut-off (C-2-L).</summary>
        public float Cutoff { get; set; } = 0.75f;

        /// <summary>
        /// Steam per swept volume admitted, for clearance and condensation:
        /// fitted to the C-1's 42 lb per brake hp-hour at 17.8 mph.
        /// </summary>
        public float SteamPerAdmission { get; set; } = 1.2f;

        /// <summary>Crank speed at which the pull falls to half, rpm: fitted to 64 % at 380 and 35 % at 600.</summary>
        public float TaperRpm { get; set; } = 473.5f;

        public float TaperExponent { get; set; } = 2.615f;

        /// <summary>Crank speed while idling out of gear, rpm (an estimate).</summary>
        public float IdleRpm { get; set; } = 40f;

        /// <summary>Preliminary steam pressure through its choke, bar gauge (an estimate).</summary>
        public float IdlePressure { get; set; } = 2.4f;

        /// <summary>Seconds for an idling or working booster to warm: 2–3 minutes of idling.</summary>
        public float WarmTime { get; set; } = 65f;

        /// <summary>Seconds for a cut-out booster to go cold.</summary>
        public float CoolTime { get; set; } = 1800f;

        /// <summary>Seconds a cold one primes after cutting in.</summary>
        public float PrimingTime { get; set; } = 10f;

        /// <summary>Share of its pull left while priming.</summary>
        public float PrimingPull { get; set; } = 0.3f;

        /// <summary>Seconds for the engine to stop once its steam is off.</summary>
        public float CoastTime { get; set; } = 3f;

        /// <summary>Share of the trailer axle's adhesion it keeps while its wheels slip.</summary>
        public float SlipPull { get; set; } = 0.6f;

        public float PistonArea => (float)(Math.PI / 4.0) * CylinderBore * CylinderBore;

        /// <summary>
        /// Starting pull at an inlet pressure, in N: the ARA formula
        /// 0.80·P·d²·S·r/D, 12,100 lb at 250 psi on the J-3a.
        /// </summary>
        public float StartingPull(float inletPressure) =>
            0.8f * Math.Max(0f, inletPressure) * 1e5f * CylinderBore * CylinderBore * PistonStroke * GearRatio / WheelDiameter;

        /// <summary>The share of the starting pull left at a crank speed.</summary>
        public float Taper(float crankRpm) =>
            1f / (1f + (float)Math.Pow(Math.Max(0f, crankRpm) / TaperRpm, TaperExponent));

        /// <summary>Crank speed at a forward speed, rpm.</summary>
        public float CrankRpm(float speed) => speed / ((float)Math.PI * WheelDiameter) * 60f * GearRatio;
    }

    /// <summary>What the cab and the loco give the booster each tick.</summary>
    public struct BoosterInputs
    {
        /// <summary>The booster latch raised.</summary>
        public bool Latch;

        /// <summary>The idling valve at Idling (which locks the latch out).</summary>
        public bool Idling;

        /// <summary>The main engine's steam chest pressure, absolute bar: its main steam.</summary>
        public float ChestPressure;

        /// <summary>Boiler pressure, absolute bar: its preliminary steam.</summary>
        public float BoilerPressure;

        /// <summary>The main engine's cut-off, positive forward.</summary>
        public float Cutoff;

        /// <summary>Speed along the track, m/s, positive forward.</summary>
        public float Speed;

        /// <summary>Most the trailer wheels can pull before slipping, N; 0 for no limit.</summary>
        public float AdhesionLimit;
    }

    /// <summary>What the booster does this tick.</summary>
    public struct BoosterFlow
    {
        public BoosterState State;

        /// <summary>Its pull at the rail, N, positive forward.</summary>
        public float Force;

        /// <summary>Steam drawn from the boiler, kg/s.</summary>
        public float SteamUsed;

        /// <summary>Its steam gauge, bar gauge.</summary>
        public float InletPressure;

        public float CrankRpm;

        /// <summary>Exhaust beats a second: four a crank turn.</summary>
        public float BeatRate;

        public bool CocksOpen;

        public bool Slipping;

        /// <summary>Running in gear above its limit.</summary>
        public bool Overspeed;

        /// <summary>Moving backward in gear: it is being dragged.</summary>
        public bool Backing;

        /// <summary>Priming: water in the cylinders after cutting in cold.</summary>
        public bool Priming;

        /// <summary>The latch fell (hooked up past its cut-off, or knocked out by a clash): the cab's latch drops.</summary>
        public bool LatchDropped;

        public BoosterEvent Event;
    }

    /// <summary>
    /// A Franklin C-2 locomotive booster. Idled out of gear it warms; with the
    /// main engine's reverser in the forward corner and the latch raised it
    /// meshes, slowly at first with its cylinder cocks open, then pulls on
    /// the main engine's steam-chest steam, less the faster it turns. Hooking
    /// the reverser back drops the latch and cuts it out. Raised too fast or
    /// with the engine still turning, the gears clash. A cold one primes.
    /// </summary>
    public sealed class Booster
    {
        private const float StillRpm = 2f;
        private const float StoppedSpeed = 0.1f;

        private readonly BoosterSpec spec;
        private float cockTimer;
        private float primingTimer;
        private float crankRpm;

        public Booster(BoosterSpec spec)
        {
            this.spec = spec ?? throw new ArgumentNullException(nameof(spec));
        }

        public BoosterSpec Spec => spec;

        public BoosterState State { get; private set; }

        /// <summary>How warm its cylinders are, 0 cold to 1 warm.</summary>
        public float Warmth { get; private set; }

        public bool InGear => State == BoosterState.CocksOpen || State == BoosterState.Working;

        /// <param name="steamVolume">Specific volume of saturated steam at an absolute pressure, m³/kg.</param>
        public BoosterFlow Step(float dt, BoosterInputs inputs, Func<float, float> steamVolume)
        {
            if (steamVolume == null)
                throw new ArgumentNullException(nameof(steamVolume));
            var flow = new BoosterFlow();
            if (!(dt > 0f))
                return Report(flow);

            var latchAllowed = inputs.Cutoff >= spec.LatchCutoff && !inputs.Idling;
            if (InGear && (!inputs.Latch || !latchAllowed))
            {
                // The latch drops: the air goes and the idler springs out.
                State = BoosterState.CutOut;
                flow.LatchDropped = inputs.Latch;
            }
            else if (!InGear && inputs.Latch)
            {
                flow.LatchDropped = true;
                if (!latchAllowed)
                {
                    // It will not stay up: the reverser is hooked up, or it is idling.
                }
                else if (inputs.Speed > spec.CutInSpeed || inputs.Speed < -StoppedSpeed || crankRpm > StillRpm)
                {
                    flow.Event = BoosterEvent.Clash;
                }
                else
                {
                    flow.LatchDropped = false;
                    State = BoosterState.CocksOpen;
                    cockTimer = 0f;
                    if (Warmth < 0.5f)
                    {
                        primingTimer = spec.PrimingTime;
                        flow.Event = BoosterEvent.Priming;
                    }
                }
            }

            if (InGear)
                Pull(dt, inputs, steamVolume, ref flow);
            else
                Rest(dt, inputs, steamVolume, ref flow);
            return Report(flow);
        }

        private void Pull(float dt, BoosterInputs inputs, Func<float, float> steamVolume, ref BoosterFlow flow)
        {
            if ((cockTimer += dt) >= spec.CockDelay)
                State = BoosterState.Working;
            var chest = Math.Max(0f, inputs.ChestPressure - 1f);
            var inlet = Math.Min(chest, State == BoosterState.CocksOpen ? spec.CockPressure : spec.MaxInletPressure);

            crankRpm = Math.Max(0f, spec.CrankRpm(inputs.Speed));
            var force = spec.StartingPull(inlet) * spec.Taper(crankRpm);
            if (primingTimer > 0f)
            {
                primingTimer = Math.Max(0f, primingTimer - dt);
                force *= spec.PrimingPull;
                flow.Priming = true;
            }
            if (inputs.Speed < -StoppedSpeed)
            {
                // Never backing up: it is dragged, not driven.
                force = 0f;
                flow.Backing = true;
            }
            if (inputs.AdhesionLimit > 0f && force > inputs.AdhesionLimit)
            {
                force = spec.SlipPull * inputs.AdhesionLimit;
                crankRpm *= 1.5f;
                flow.Slipping = true;
            }

            flow.Force = force;
            flow.InletPressure = inlet;
            flow.CocksOpen = State == BoosterState.CocksOpen;
            flow.Overspeed = inputs.Speed > spec.RunningLimit;
            flow.SteamUsed = SteamPerTurn(inlet, steamVolume) * spec.Taper(crankRpm) * crankRpm / 60f
                             + (flow.CocksOpen && inlet > 0f ? spec.CockBlow : 0f);
            Warm(dt, true);
        }

        private void Rest(float dt, BoosterInputs inputs, Func<float, float> steamVolume, ref BoosterFlow flow)
        {
            var idling = inputs.Idling && inputs.BoilerPressure - 1f > spec.IdlePressure;
            State = idling ? BoosterState.Idling : BoosterState.CutOut;
            if (idling)
            {
                crankRpm = spec.IdleRpm;
                flow.InletPressure = spec.IdlePressure;
                flow.SteamUsed = SteamPerTurn(spec.IdlePressure, steamVolume) * crankRpm / 60f;
            }
            else
            {
                crankRpm *= (float)Math.Exp(-dt / spec.CoastTime);
                if (crankRpm < 0.5f)
                    crankRpm = 0f;
            }
            Warm(dt, idling);
        }

        // Steam admitted a crank turn: four admissions of its cut-off's share
        // of a cylinder, with clearance and condensation.
        private float SteamPerTurn(float inletGauge, Func<float, float> steamVolume)
        {
            if (!(inletGauge > 0f))
                return 0f;
            var admitted = spec.SteamPerAdmission * 4f * spec.PistonArea * spec.PistonStroke * spec.Cutoff;
            return admitted / steamVolume(inletGauge + 1f);
        }

        private void Warm(float dt, bool steaming)
        {
            Warmth = steaming
                ? 1f - (1f - Warmth) * (float)Math.Exp(-dt / spec.WarmTime)
                : Warmth * (float)Math.Exp(-dt / spec.CoolTime);
        }

        private BoosterFlow Report(BoosterFlow flow)
        {
            flow.State = State;
            flow.CrankRpm = crankRpm;
            flow.BeatRate = crankRpm / 60f * 4f;
            return flow;
        }
    }
}
