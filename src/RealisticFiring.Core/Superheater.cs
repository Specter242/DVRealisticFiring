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

 public sealed class Superheater
    {
        // Convective heat transfer goes as the gas flow to the 0.8 power.
        private const float GasExponent = 0.8f;

        // What cools the elements with the fire dead: the draft through it and
        // the header's conduction, as a share of the full rate's gas.
        private const float DeadGas = 0.1f;

        // Superheat a share of moisture costs: its latent heat (about 1,950 kJ/kg at
        // 14 bar) over superheated steam's specific heat (about 2.4 kJ/kg·K).
        private const float DryingPerWetness = 810f;

        private readonly float conductance;
        private readonly float gasExponent;
        private bool started;

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

        /// <summary>The elements' superheat: what steam leaving the header has, dry, in °C.</summary>
        public float Elements { get; private set; }

        /// <summary>Superheat of the steam reaching the cylinders, in °C: the elements' less what drying the steam took.</summary>
        public float Superheat { get; private set; }

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
            var wetness = 1f - Math.Min(1f, Math.Max(0f, quality));
            Superheat = Math.Max(0f, Elements - wetness * DryingPerWetness);
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
