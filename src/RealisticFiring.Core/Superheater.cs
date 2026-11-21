using System;

namespace RealisticFiring.Core
{
 public sealed class SuperheaterSpec
    {
        /// <summary>The kind, as the overlay names it: "Type A", "Type E", or "saturated".</summary>
        public string Kind { get; set; } = "saturated";

        /// <summary>Superheat with the fire and the steam both at the boiler's full rate.</summary>
        public float FullSuperheat { get; set; }

        /// <summary>Superheat with both at half the full rate.</summary>
        public float HalfSuperheat { get; set; }

        /// <summary>
        /// The flue gas at the elements' ends over saturation with a full
        /// fire: what the elements soak to with the throttle shut.
        /// </summary>
        public float StandingSuperheat { get; set; }

        /// <summary>Seconds the elements take to follow a change at the full rate.</summary>
        public float TimeConstant { get; set; } = 60f;

        public bool Saturated => FullSuperheat <= 0f;

        public static SuperheaterSpec None => new SuperheaterSpec();
    }

    /// <summary>What superheater element care reads each tick besides fire and flow.</summary>
    public struct SuperheaterCareInputs
    {
        /// <summary>Firing rate as a share of the boiler's full rate (raw heat for steam superheat).</summary>
        public float Fire;

        /// <summary>Actual steam flow through the superheater, share of the boiler's full rate.</summary>
        public float Flow;

        /// <summary>Boiler outlet steam quality (1 dry).</summary>
        public float Quality;

        /// <summary>Boiler steam saturation temperature (or water temperature while cold), °C.</summary>
        public float SaturationTemperature;

        /// <summary>Throttle opening, 0 to 1.</summary>
        public float Throttle;

        /// <summary>Drifting valve opening, 0 to 1 (0 if not fitted).</summary>
        public float DriftingValve;

        /// <summary>
        /// The game's Steam Startup speedup on the fire (1 = real time, up to
        /// 25 at default 100 %; 0 treated as 1). Divided out of element metal
        /// heating only, so cold starts don't falsely cook elements while steam
        /// superheat keeps its existing behavior.
        /// </summary>
        public float StartupMultiplier;

        /// <summary>Whether superheater overheat care is enabled (off in Relaxed preset).</summary>
        public bool CareEnabled;
    }

 public sealed class Superheater
    {
        /// <summary>Element temperature (°C) where overheat warning starts when unprotected.</summary>
        public const float WarningElementTemperature = 455f;

        /// <summary>Element temperature (°C) above which scaling/cooking wear accumulates.</summary>
        public const float SafeElementTemperature = 480f;

        /// <summary>Temperature (°C) above <see cref="SafeElementTemperature"/> for full cooking wear rate.</summary>
        public const float ReferenceOverheat = 100f;

        /// <summary>Element wear gained per second at full overheat (100 °C past safe): ~15 min to full wear.</summary>
        public const float WearRate = 1f / 900f;

        /// <summary>Maximum superheat lost at full element wear (1.0): 55 % lower superheat, raising cylinder steam use.</summary>
        public const float MaxSuperheatLoss = 0.55f;

        /// <summary>Engine mechanical wear share represented by full superheater wear (1.0).</summary>
        public const float EngineWearWeight = 0.10f;

        /// <summary>Effective cooling steam flow provided by a cracked throttle or open drifting valve.</summary>
        public const float ProtectiveCoolingFlow = 0.65f;

        /// <summary>Drifting valve opening giving full element-cooling protection.</summary>
        public const float DriftingValveFullProtection = 0.25f;

        /// <summary>Element temperature (°C) at which an automatic fireman begins checking the damper early.</summary>
        public const float FiremanPreWarnTemperature = 435f;

        // Convective heat transfer goes as the gas flow to the 0.8 power.
        private const float GasExponent = 0.8f;

        // Standing element metal temperature scales more steeply with firing
        // rate than a Type E superheater's shallow steam-pickup curve (~0.39),
        // so normal 40–60 % standing fires equilibrate safely around 345–435 °C
        // while shutting off on a white-hot 75–100 % fire climbs past 480 °C.
        private const float MetalGasExponent = 1.2f;

        // What cools the elements with the fire dead: the draft through it and
        // the header's conduction, as a share of the full rate's gas.
        private const float DeadGas = 0.1f;

        // Hotter than the fire holds them, the elements cool within this many
        // seconds at the least: by the steam standing in them and the header's
        // conduction, which don't fall with the fire. Cooling only through the
        // gas, a fire eased, damped or dropped on cooking elements left them
        // over 480 °C for 3½ to 6 minutes, the dropped fire the longest (game,
        // job 3116), so the advice's "close the damper / ease the firing" and
        // the fireman's damper did little; only the drifting valve or a cracked
        // throttle cooled them. Judged against the fire over FireSmoothTime,
        // not the fire this tick: against the fire this tick, a few dark seconds
        // in each half minute (a bed re-laid, a heavy round) cooled them faster
        // than the fire heats them, and they passed 480 °C at 541 s instead of
        // 326 (game, job 3123). Heating is unchanged.
        private const float MetalCoolTime = 90f;
        private const float FireSmoothTime = 20f;

        // Superheat a share of moisture costs: its latent heat (about 1,950 kJ/kg at
        // 14 bar) over superheated steam's specific heat (about 2.4 kJ/kg·K).
        private const float DryingPerWetness = 810f;

        private readonly float conductance;
        private readonly float gasExponent;
        private bool started;
        private float metalOverSat;
        private float smoothFire;
        private bool metalStarted;
        private bool careActive = true;

        public Superheater(SuperheaterSpec spec)
        {
            Spec = spec ?? SuperheaterSpec.None;
            if (Spec.Saturated)
                return;
            if (!(Spec.StandingSuperheat > Spec.FullSuperheat))
                throw new ArgumentException("The elements must soak hotter standing than they give at the full rate.", nameof(spec));
            if (!(Spec.HalfSuperheat > 0f && Spec.HalfSuperheat <= Spec.FullSuperheat))
                throw new ArgumentException("Half the rate must give some superheat, and no more than the full rate.", nameof(spec));
            // At the full rate, gas and steam conductances share the soak: s = G·r/(r + 1).
            conductance = Spec.FullSuperheat / (Spec.StandingSuperheat - Spec.FullSuperheat);
            // At half, the gas temperature's power makes up the rest: s = G·½ᵃ·r·½ᵇ/(r·½ᵇ + ½).
            var half = (float)Math.Pow(0.5, GasExponent);
            var share = Spec.HalfSuperheat * (conductance * half + 0.5f) / (Spec.StandingSuperheat * conductance * half);
            gasExponent = Math.Max(0f, (float)(Math.Log(share) / Math.Log(0.5)));
        }

        public SuperheaterSpec Spec { get; }

        /// <summary>The elements' superheat: what steam leaving the header has, dry and clean, in °C.</summary>
        public float Elements { get; private set; }

        /// <summary>Superheat of the steam reaching the cylinders, in °C: degraded by element wear and wet steam.</summary>
        public float Superheat { get; private set; }

        /// <summary>Estimated element metal temperature, in °C (0 on a saturated engine).</summary>
        public float ElementTemperature { get; private set; }

        /// <summary>Accumulated superheater element wear from overheating, 0 (new) to 1 (badly cooked/scaled).</summary>
        public float Wear { get; private set; }

        /// <summary>Remaining superheater element health, 1 (new) to 0 (badly cooked).</summary>
        public float Health => 1f - Wear;

        /// <summary>Share of clean superheat transferred to the steam after element wear, 0 to 1 (1 when care is off).</summary>
        public float SuperheatEfficiency => careActive ? 1f - MaxSuperheatLoss * Wear : 1f;

        /// <summary>Whether the elements are overheating with no cooling protection and need immediate crew action.</summary>
        public bool Overheating { get; private set; }

        /// <summary>Whether element temperature is approaching the overheat threshold without cooling protection.</summary>
        public bool ApproachingOverheat { get; private set; }

        /// <summary>Whether the elements are warm enough that an automatic fireman should close the damper when shut off.</summary>
        public bool WantsDamperCheck => careActive && !Spec.Saturated && ElementTemperature >= FiremanPreWarnTemperature;

        /// <summary>Enables or disables superheater care effects without discarding saved wear.</summary>
        public void SetCareEnabled(bool enabled)
        {
            careActive = enabled;
            if (!enabled)
            {
                Overheating = false;
                ApproachingOverheat = false;
            }
        }

        /// <summary>
        /// Where the elements settle with the fire at <paramref name="fire"/>
        /// and the steam at <paramref name="flow"/>, each a share of the
        /// boiler's full rate.
        /// </summary>
        public float Steady(float fire, float flow)
        {
            if (Spec.Saturated)
                return 0f;
            Rates(fire, flow, out var gas, out var steam, out var soak);
            return gas * soak / (gas + steam);
        }

        /// <summary>
        /// A tick of <paramref name="dt"/> s: the fire and the steam through
        /// the throttle as shares of the boiler's full rate, and the boiler's
        /// steam quality (1 dry).
        /// </summary>
        public void Step(float fire, float flow, float quality, float dt)
        {
            if (Spec.Saturated)
                return;
            StepThermal(fire, flow, dt);
            ApplyOutput(quality);
        }

        /// <summary>
        /// A tick of <paramref name="dt"/> s including element metal
        /// temperature, drifting-valve / cracked-throttle protection, and
        /// overheat wear when <see cref="SuperheaterCareInputs.CareEnabled"/> is on.
        /// </summary>
        public void Step(float dt, SuperheaterCareInputs inputs)
        {
            if (Spec.Saturated)
                return;
            careActive = inputs.CareEnabled;
            StepThermal(inputs.Fire, inputs.Flow, dt);

            var speedup = inputs.StartupMultiplier > 1f && !float.IsNaN(inputs.StartupMultiplier) && !float.IsInfinity(inputs.StartupMultiplier)
                ? inputs.StartupMultiplier
                : 1f;
            var metalFire = inputs.Fire / speedup;

            var throttleCracked = !float.IsNaN(inputs.Throttle) && inputs.Throttle >= Drifting.CrackedThrottle;
            var valve = float.IsNaN(inputs.DriftingValve) ? 0f : Smoothing.Clamp01(inputs.DriftingValve);
            var protection = Math.Max(throttleCracked ? 1f : 0f, Smoothing.Clamp01(valve / DriftingValveFullProtection));
            var coolingFlow = Math.Max(Math.Max(0f, inputs.Flow), protection * ProtectiveCoolingFlow);
            StepMetal(metalFire, coolingFlow, dt);

            var sat = float.IsNaN(inputs.SaturationTemperature) ? 100f : Math.Max(0f, inputs.SaturationTemperature);
            ElementTemperature = sat + metalOverSat;

            var coolingActive = protection >= 0.5f || inputs.Flow >= 0.08f;
            if (inputs.CareEnabled)
            {
                Overheating = ElementTemperature >= SafeElementTemperature && !coolingActive;
                ApproachingOverheat = ElementTemperature >= WarningElementTemperature && !coolingActive;
                if (ElementTemperature > SafeElementTemperature && !coolingActive && dt > 0f)
                {
                    var excess = (ElementTemperature - SafeElementTemperature) / ReferenceOverheat;
                    Wear = Math.Min(1f, Wear + WearRate * excess * dt);
                }
            }
            else
            {
                Overheating = false;
                ApproachingOverheat = false;
            }

            ApplyOutput(inputs.Quality);
        }

        /// <summary>
        /// Repairs <paramref name="share"/> (0 to 1) of accumulated superheater
        /// element wear. The renewed elements come in cool, as much nearer the
        /// steam's temperature: repaired hot with no steam through them, they
        /// went on wearing at once (game, job 3116: 0 to 0.0019 in 2 s).
        /// </summary>
        public void Repair(float share)
        {
            var kept = 1f - Math.Max(0f, Math.Min(1f, Smoothing.Finite(share)));
            Wear *= kept;
            metalOverSat *= kept;
            if (Wear < 1e-5f)
                Wear = 0f;
        }

        /// <summary>Restores saved superheater element wear.</summary>
        public void Restore(float wear)
        {
            Wear = Math.Max(0f, Math.Min(1f, Smoothing.Finite(wear)));
        }

        /// <summary>Applies host-authoritative state on a multiplayer guest.</summary>
        public void Mirror(float elements, float superheat, float elementTemperature, float wear, bool overheating, bool approaching)
        {
            if (Spec.Saturated)
                return;
            Elements = Math.Max(0f, Smoothing.Finite(elements));
            Superheat = Math.Max(0f, Smoothing.Finite(superheat));
            ElementTemperature = Math.Max(0f, Smoothing.Finite(elementTemperature));
            Wear = Math.Max(0f, Math.Min(1f, Smoothing.Finite(wear)));
            Overheating = overheating;
            ApproachingOverheat = approaching;
            started = true;
        }

        private void StepThermal(float fire, float flow, float dt)
        {
            Rates(fire, flow, out var gas, out var steam, out var soak);
            var settled = gas * soak / (gas + steam);
            if (!started)
            {
                Elements = settled;
                started = true;
            }
            else
            {
                // Exactly, so a long tick cannot overshoot.
                var heatCapacity = Spec.TimeConstant * (conductance + 1f);
                Elements = settled + (Elements - settled) * (float)Math.Exp(-Math.Max(0f, dt) * (gas + steam) / heatCapacity);
            }
        }

        private void StepMetal(float fire, float coolingFlow, float dt)
        {
            var f = Math.Min(1.5f, Math.Max(0f, fire));
            var settled = MetalSettled(f, coolingFlow, out var gas, out var steam);
            if (!metalStarted)
            {
                metalOverSat = settled;
                smoothFire = f;
                metalStarted = true;
                return;
            }
            var step = Math.Max(0f, dt);
            smoothFire += (f - smoothFire) * (1f - (float)Math.Exp(-step / FireSmoothTime));
            var heatCapacity = Spec.TimeConstant * (conductance + 1f);
            var rate = (gas + steam) / heatCapacity;
            metalOverSat = settled + (metalOverSat - settled) * (float)Math.Exp(-step * rate);
            // The floor (MetalCoolTime), toward where the fire has held them lately, never below where it holds them now.
            var held = Math.Max(settled, MetalSettled(Math.Min(1.5f, smoothFire), coolingFlow, out _, out _));
            var extra = 1f / MetalCoolTime - rate;
            if (metalOverSat > held && extra > 0f)
                metalOverSat = held + (metalOverSat - held) * (float)Math.Exp(-step * extra);
        }

        // Where the fire at <paramref name="f"/> and the cooling flow hold the elements, over saturation.
        private float MetalSettled(float f, float coolingFlow, out float gas, out float steam)
        {
            Rates(f, coolingFlow, out gas, out steam, out var soak);
            var metalSoak = Spec.StandingSuperheat * (float)Math.Pow(f, MetalGasExponent);
            var steamSettled = gas * soak / (gas + steam);
            var metalSettled = gas * metalSoak / (gas + steam);
            // Standing with no flow, elements equilibrate at metalSoak so normal
            // 40–60 % fires stay safely under 480 °C while a white-hot forced
            // fire overheats them; flowing steam cools them without ever reading
            // cooler than the steam they heat.
            return Math.Max(metalSettled, Math.Min(metalSoak, steamSettled));
        }

        private void ApplyOutput(float quality)
        {
            var wetness = 1f - Math.Min(1f, Math.Max(0f, quality));
            Superheat = Math.Max(0f, Elements * SuperheatEfficiency - wetness * DryingPerWetness);
        }

        // The gas's and the steam's conductances, as shares of the steam's at the full rate, and the gas's superheat.
        private void Rates(float fire, float flow, out float gas, out float steam, out float soak)
        {
            var f = Math.Min(1.5f, Math.Max(0f, fire));
            gas = conductance * (float)Math.Pow(Math.Max(DeadGas, f), GasExponent);
            // Stroke-driven delivery can exceed the average rated rate for a
            // short pulse. Every kg must cool the elements; clipping the pulse
            // makes the same mean flow leave them hotter at some frame rates.
            steam = Math.Max(0f, flow);
            soak = Spec.StandingSuperheat * (float)Math.Pow(f, gasExponent);
        }
    }
}
