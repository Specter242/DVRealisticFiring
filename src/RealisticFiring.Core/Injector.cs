using System;

namespace RealisticFiring.Core
{
    /// <summary>
    /// Water and steam properties, in kJ/kg, °C, and absolute bar. The game
    /// supplies its own steam tables so the injector agrees with its boiler.
    /// </summary>
    public interface ISteamProperties
    {
        /// <summary>Specific enthalpy of saturated steam at <paramref name="pressure"/>.</summary>
        float SteamEnthalpy(float pressure);

        /// <summary>Specific enthalpy of water at <paramref name="temperature"/>.</summary>
        float WaterEnthalpy(float temperature);

        /// <summary>Temperature of water with specific <paramref name="enthalpy"/>.</summary>
        float WaterTemperature(float enthalpy);
    }

    public enum InjectorState
    {
        /// <summary>Steam valve shut.</summary>
        Off,

        /// <summary>Steam on, spilling from the overflow until the jet takes hold.</summary>
        PickingUp,

        /// <summary>Singing: feeding the boiler.</summary>
        Working,

        /// <summary>Failed: spilling until the steam valve is shut and reopened.</summary>
        KnockedOff,
    }

    /// <summary>A live-steam injector's design figures.</summary>
    public sealed class InjectorSpec
    {
        /// <summary>Lowest boiler pressure it picks up at, absolute bar (about 2.5 bar gauge).</summary>
        public const float DefaultStartPressure = 3.5f;

        /// <summary>Once working, it holds on down to this, absolute bar.</summary>
        public const float DefaultHoldPressure = 2.5f;

        /// <summary>Below this the steam valve counts as shut.</summary>
        public const float OpenThreshold = 0.05f;

        public InjectorSpec(float maxDelivery, float ratedPressure)
        {
            if (maxDelivery <= 0f)
                throw new ArgumentOutOfRangeException(nameof(maxDelivery));
            if (ratedPressure <= DefaultStartPressure)
                throw new ArgumentOutOfRangeException(nameof(ratedPressure));
            MaxDelivery = maxDelivery;
            RatedPressure = ratedPressure;
        }

        /// <summary>Water delivered to the boiler at rated pressure with the valve wide open, in kg/s.</summary>
        public float MaxDelivery { get; }

        /// <summary>Boiler pressure it delivers its full rate at, absolute bar.</summary>
        public float RatedPressure { get; }

        public float StartPressure { get; set; } = DefaultStartPressure;

        public float HoldPressure { get; set; } = DefaultHoldPressure;

        /// <summary>Seconds of spilling before the jet takes hold.</summary>
        public float PickUpTime { get; set; } = 1.2f;

        /// <summary>Tender water lifted per kg of live steam.</summary>
        public float WaterPerSteam { get; set; } = 12f;

        /// <summary>Share of the water it would lift that spills while it is not feeding.</summary>
        public float SpillShare { get; set; } = 0.6f;
    }

    /// <summary>What the boiler, tender, and cab give the injector each tick.</summary>
    public struct InjectorInputs
    {
        /// <summary>Steam valve opening, 0 shut to 1 wide open (the cab's injector lever).</summary>
        public float SteamValve;

        /// <summary>Boiler pressure, absolute bar.</summary>
        public float BoilerPressure;

        /// <summary>Water left in the tender, in kg.</summary>
        public float TenderWater;

        /// <summary>Water the boiler can still take, in kg.</summary>
        public float BoilerSpace;

        /// <summary>Temperature of the tender water, in °C.</summary>
        public float TenderWaterTemperature;
    }

    /// <summary>What the injector does this tick; rates in kg/s.</summary>
    public struct InjectorFlow
    {
        public InjectorState State;

        /// <summary>Feed water entering the boiler, condensed steam included.</summary>
        public float WaterIn;

        /// <summary>Live steam drawn from the boiler.</summary>
        public float SteamUsed;

        /// <summary>Water taken from the tender, fed and spilled.</summary>
        public float TenderDraw;

        /// <summary>Water spilling from the overflow.</summary>
        public float Overflow;

        /// <summary>Steam blowing out of the overflow.</summary>
        public float OverflowSteam;

        /// <summary>Temperature of the feed water, in °C.</summary>
        public float FeedTemperature;
    }

    /// <summary>
    /// A live-steam injector. Opened at enough pressure it spills from the
    /// overflow for a moment, then picks up and sings: live steam condenses
    /// into tender water and drives it into the boiler warm. Delivery rises
    /// with pressure to the full rate at rated pressure. It knocks off when
    /// pressure sags below what it holds on at or the tender runs dry, and
    /// then spills until its steam valve is shut and reopened.
    /// </summary>
    public sealed class Injector
    {
        private readonly InjectorSpec spec;
        private float pickUpTimer;

        public Injector(InjectorSpec spec)
        {
            this.spec = spec ?? throw new ArgumentNullException(nameof(spec));
        }

        public InjectorSpec Spec => spec;

        public InjectorState State { get; private set; }

        public InjectorFlow Step(float dt, InjectorInputs inputs, ISteamProperties steam)
        {
            if (steam == null)
                throw new ArgumentNullException(nameof(steam));
            if (!(dt > 0f) || inputs.SteamValve <= InjectorSpec.OpenThreshold)
            {
                if (inputs.SteamValve <= InjectorSpec.OpenThreshold)
                {
                    State = InjectorState.Off;
                    pickUpTimer = 0f;
                }
                return new InjectorFlow { State = State, FeedTemperature = inputs.TenderWaterTemperature };
            }

            var pressure = inputs.BoilerPressure;
            var hasWater = inputs.TenderWater > 0f;
            switch (State)
            {
                case InjectorState.Off:
                    State = pressure >= spec.StartPressure && hasWater ? InjectorState.PickingUp : InjectorState.KnockedOff;
                    pickUpTimer = 0f;
                    break;
                case InjectorState.PickingUp:
                    if (pressure < spec.StartPressure || !hasWater)
                        State = InjectorState.KnockedOff;
                    else if ((pickUpTimer += dt) >= spec.PickUpTime)
                        State = InjectorState.Working;
                    break;
                case InjectorState.Working:
                    if (pressure < spec.HoldPressure || !hasWater)
                        State = InjectorState.KnockedOff;
                    break;
            }

            // What it lifts with this much steam valve at this pressure.
            var steamShare = 1f / (1f + spec.WaterPerSteam);
            var nominal = spec.MaxDelivery * Math.Min(1f, inputs.SteamValve) * Capacity(pressure);
            // The tender limits the water it can lift.
            nominal = Math.Min(nominal, Math.Max(0f, inputs.TenderWater) / dt / (1f - steamShare));
            var steamUsed = nominal * steamShare;
            var lifted = nominal - steamUsed;

            if (State != InjectorState.Working)
            {
                // The jet has not taken hold: steam and some water blow out of the overflow.
                var spill = spec.SpillShare * lifted;
                return new InjectorFlow
                {
                    State = State,
                    SteamUsed = steamUsed,
                    OverflowSteam = steamUsed,
                    TenderDraw = spill,
                    Overflow = spill,
                    FeedTemperature = inputs.TenderWaterTemperature,
                };
            }

            // What the boiler cannot take goes out of the overflow, warm.
            var delivered = Math.Min(nominal, Math.Max(0f, inputs.BoilerSpace) / dt);
            var enthalpy = nominal > 0f
                ? (steamUsed * steam.SteamEnthalpy(pressure) + lifted * steam.WaterEnthalpy(inputs.TenderWaterTemperature)) / nominal
                : steam.WaterEnthalpy(inputs.TenderWaterTemperature);
            return new InjectorFlow
            {
                State = State,
                WaterIn = delivered,
                SteamUsed = steamUsed,
                TenderDraw = lifted,
                Overflow = nominal - delivered,
                FeedTemperature = steam.WaterTemperature(enthalpy),
            };
        }

        // Delivery grows with the square root of gauge pressure, reaching the
        // full rate at rated pressure.
        private float Capacity(float pressure)
        {
            var gauge = Math.Max(0f, pressure - 1f);
            return Math.Min(1f, (float)Math.Sqrt(gauge / (spec.RatedPressure - 1f)));
        }
    }
}
