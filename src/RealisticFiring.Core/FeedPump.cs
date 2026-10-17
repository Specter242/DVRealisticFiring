using System;

namespace RealisticFiring.Core
{
    public enum FeedHeaterType
    {
        /// <summary>
        /// Worthington open type (SA): exhaust steam mixes with the water in the
        /// heater, a hot-water pump forces the mix in, and a drifting control
        /// valve halves the pumping with the throttle shut.
        /// </summary>
        Open,

        /// <summary>
        /// Elesco closed type: water through tubes in exhaust steam, pushed by a
        /// centrifugal turbine pump that trips on overspeed when it loses water.
        /// </summary>
        Closed,
    }

    public enum FeedPumpState
    {
        /// <summary>Its operating valve shut.</summary>
        Off,

        /// <summary>Pumping, at full rate.</summary>
        Feeding,

        /// <summary>Pumping at half rate: the throttle is shut and the drifting valve has closed.</summary>
        Choked,

        /// <summary>The heater's water flashing after a hook-up or shut-off: the pump races and delivers little.</summary>
        Racing,

        /// <summary>No water at the suction: the cold pump shrieks and nothing is fed until it is shut, refilled, and primed.</summary>
        LostSuction,

        /// <summary>The turbine's overspeed trip has fallen: shut the valve a few seconds to reset it.</summary>
        Tripped,
    }

    /// <summary>A feedwater heater and pump's design figures. Pressures absolute bar.</summary>
    public sealed class FeedPumpSpec
    {
        public FeedPumpSpec(FeedHeaterType type, float capacity)
        {
            if (!(capacity > 0f))
                throw new ArgumentOutOfRangeException(nameof(capacity));
            Type = type;
            Capacity = capacity;
        }

        public FeedHeaterType Type { get; }

        /// <summary>Water into the boiler with the operating valve wide open, kg/s.</summary>
        public float Capacity { get; }

        /// <summary>Boiler pressure below which it slows, absolute bar (an estimate).</summary>
        public float FullPressure { get; set; } = 3f;

        /// <summary>The pump's own steam, as a share of the water it feeds: 1.5–2.7 % on test.</summary>
        public float PumpSteamShare { get; set; } = 0.025f;

        /// <summary>Its steam a kg rises this much with the drifting valve choking it (an estimate).</summary>
        public float ChokedSteamFactor { get; set; } = 2f;

        /// <summary>Steam-pipe pressure where the drifting valve starts to open, absolute bar: 50 psi.</summary>
        public float DriftingOpen { get; set; } = 1f + 50f / 14.5038f;

        /// <summary>Where it is fully open, absolute bar: over 90 psi.</summary>
        public float DriftingFull { get; set; } = 1f + 90f / 14.5038f;

        /// <summary>Pumping left with the drifting valve shut: one half.</summary>
        public float DriftingShare { get; set; } = 0.5f;

        /// <summary>Heater pressure at the engine's full exhaust, absolute bar: 15 psi.</summary>
        public float HeaterPressure { get; set; } = 1f + 15f / 14.5038f;

        /// <summary>How far under exhaust saturation the feed comes out, K: 10 °F.</summary>
        public float Approach { get; set; } = 5.5f;

        /// <summary>Most of the exhaust the heater can condense: 1/7 to 15 % on test, matched feed.</summary>
        public float CondensableShare { get; set; } = 0.18f;

        /// <summary>Time constant of the heater warming, s: about seven minutes to hot.</summary>
        public float WarmTime { get; set; } = 140f;

        /// <summary>Time constant of the heater cooling with no exhaust, s.</summary>
        public float CoolTime { get; set; } = 300f;

        /// <summary>Time constant of an open heater's over-hot water flashing off, s.</summary>
        public float FlashTime { get; set; } = 4f;

        /// <summary>Delivery left while it races.</summary>
        public float RacingDelivery { get; set; } = 0.2f;

        /// <summary>Open wider than this through a hook-up or shut-off and it races.</summary>
        public float RacingValve { get; set; } = 0.3f;

        /// <summary>Tender water below which the suction is lost, kg (an estimate).</summary>
        public float LowSuctionWater { get; set; } = 300f;

        /// <summary>Seconds the valve must stay shut to reset a tripped turbine.</summary>
        public float TripResetTime { get; set; } = 5f;

        /// <summary>The operating valve's opening at and below which a pump counts as shut, unless its spec says otherwise.</summary>
        public const float ShutOpening = 0.05f;

        /// <summary>At and below this the operating valve counts as shut.</summary>
        public float OpenThreshold { get; set; } = ShutOpening;

        /// <summary>Time constant of the pump coming up to speed or running down, s.</summary>
        public float SpinTime { get; set; } = 3f;
    }

    /// <summary>What the boiler, tender, engine, and cab give the pump each tick.</summary>
    public struct FeedPumpInputs
    {
        /// <summary>The pump's operating valve, 0 shut to 1 wide open.</summary>
        public float Valve;

        /// <summary>Boiler pressure, absolute bar.</summary>
        public float BoilerPressure;

        /// <summary>Water the boiler can still take, kg.</summary>
        public float BoilerSpace;

        /// <summary>Water in the tender, kg.</summary>
        public float TenderWater;

        /// <summary>Tender water temperature, °C.</summary>
        public float TenderWaterTemperature;

        /// <summary>The main engine's exhaust steam, kg/s.</summary>
        public float ExhaustFlow;

        /// <summary>The main engine's exhaust at full throttle, kg/s, for the heater's pressure.</summary>
        public float FullExhaustFlow;

        /// <summary>The main engine's steam-chest pressure, absolute bar, for the drifting valve.</summary>
        public float ChestPressure;
    }

    /// <summary>What the pump and heater do this tick; rates in kg/s.</summary>
    public struct FeedPumpFlow
    {
        public FeedPumpState State;

        /// <summary>Water into the boiler, condensed exhaust included on an open heater.</summary>
        public float WaterIn;

        /// <summary>Temperature of the feed, °C.</summary>
        public float FeedTemperature;

        /// <summary>The pump's live steam from the boiler.</summary>
        public float SteamUsed;

        /// <summary>Net water taken from the tender.</summary>
        public float TenderDraw;

        /// <summary>Exhaust steam the heater condenses into the feed.</summary>
        public float Condensed;

        /// <summary>Steam blowing from the heater's vent while its water flashes.</summary>
        public float VentSteam;

        /// <summary>The heater's temperature, °C.</summary>
        public float HeaterTemperature;

        /// <summary>Pump speed as a share of its speed at full capacity.</summary>
        public float Speed;

        /// <summary>Its cab gauge, bar gauge: the cold pump's discharge (open), or delivery (closed).</summary>
        public float Gauge;
    }

    /// <summary>
    /// A feedwater heater and its pump, the second feed on big engines: the
    /// fireman opens its operating valve and sets it to the boiler's
    /// evaporation, and it feeds evenly with water heated by exhaust steam,
    /// for its own small steam. The heater takes minutes to warm and cools
    /// when the engine drifts; an open heater's drifting valve halves the
    /// pumping then, and its water flashes off when the exhaust drops, so
    /// the pump races unless eased first. Low tender water loses the
    /// suction; a closed heater's turbine trips on it.
    /// </summary>
    public sealed class FeedPump
    {
        private readonly FeedPumpSpec spec;
        private float shutTimer;
        private bool suctionLost;
        private bool tripped;

        // The pump's speed as a share of its speed at full capacity.
        private float speed;

        public FeedPump(FeedPumpSpec spec, float startTemperature = 15f)
        {
            this.spec = spec ?? throw new ArgumentNullException(nameof(spec));
            HeaterTemperature = startTemperature;
        }

        public FeedPumpSpec Spec => spec;

        /// <summary>The heater's temperature, °C.</summary>
        public float HeaterTemperature { get; private set; }

        public FeedPumpFlow Step(float dt, FeedPumpInputs inputs, ISteamProperties steam)
        {
            if (steam == null)
                throw new ArgumentNullException(nameof(steam));
            var flow = new FeedPumpFlow { FeedTemperature = HeaterTemperature, HeaterTemperature = HeaterTemperature };
            if (!(dt > 0f))
                return flow;

            var valve = Math.Max(0f, Math.Min(1f, inputs.Valve));
            var open = valve > spec.OpenThreshold;
            var exhaustShare = inputs.FullExhaustFlow > 0f ? Clamp01(inputs.ExhaustFlow / inputs.FullExhaustFlow) : 0f;
            var heaterPressure = 1f + (spec.HeaterPressure - 1f) * exhaustShare;
            var saturation = steam.SaturationTemperature(heaterPressure);

            // Shut, the suction can be primed again and a trip reset.
            shutTimer = open ? 0f : shutTimer + dt;
            if (!open && inputs.TenderWater >= spec.LowSuctionWater)
                suctionLost = false;
            if (tripped && shutTimer >= spec.TripResetTime)
                tripped = false;
            if (open && inputs.TenderWater < spec.LowSuctionWater)
            {
                suctionLost = true;
                if (spec.Type == FeedHeaterType.Closed)
                    tripped = true;
            }

            // An open heater's water above its new saturation flashes off out of the vent.
            var flashing = spec.Type == FeedHeaterType.Open && HeaterTemperature > saturation + 2f;
            if (flashing)
            {
                var before = HeaterTemperature;
                HeaterTemperature = Relax(HeaterTemperature, saturation, spec.FlashTime, dt);
                flow.VentSteam = (before - HeaterTemperature) / dt * 0.05f;
            }

            if (!open || tripped)
            {
                flow.State = open ? FeedPumpState.Tripped : FeedPumpState.Off;
                Spin(0f, dt);
                flow.Speed = speed;
                Warm(dt, inputs, saturation, exhaustShare, 0f, steam);
                return Report(flow, inputs, heaterPressure, 0f);
            }
            if (suctionLost)
            {
                // The cold pump spins dry, shrieking; the hot pump pounds.
                flow.State = FeedPumpState.LostSuction;
                Spin(2.5f * valve, dt);
                flow.SteamUsed = spec.PumpSteamShare * spec.Capacity * speed * 0.8f;
                flow.Speed = speed;
                Warm(dt, inputs, saturation, exhaustShare, 0f, steam);
                return Report(flow, inputs, heaterPressure, 0f);
            }

            var pressure = Clamp01((inputs.BoilerPressure - 1f) / (spec.FullPressure - 1f));
            var drifting = spec.Type == FeedHeaterType.Open
                ? spec.DriftingShare + (1f - spec.DriftingShare) * Clamp01((inputs.ChestPressure - spec.DriftingOpen) / (spec.DriftingFull - spec.DriftingOpen))
                : 1f;
            var racing = flashing && valve > spec.RacingValve;
            // It comes up to its setting over a few seconds; racing, it runs fast and delivers little.
            var setting = valve * pressure * drifting;
            Spin(racing ? 1.8f * setting : setting, dt);
            var pumped = spec.Capacity * (racing ? Math.Min(speed / 1.8f, setting) * spec.RacingDelivery : Math.Min(speed, setting));
            var delivered = Math.Min(pumped, Math.Max(0f, inputs.BoilerSpace) / dt);
            var steamShare = spec.PumpSteamShare * (drifting < 0.99f ? spec.ChokedSteamFactor : 1f) * (racing ? 1.8f : 1f);
            var pumpSteam = steamShare * pumped;

            var condensed = Warm(dt, inputs, saturation, exhaustShare, delivered, steam, pumpSteam);
            flow.State = racing ? FeedPumpState.Racing : drifting < 0.99f ? FeedPumpState.Choked : FeedPumpState.Feeding;
            flow.WaterIn = delivered;
            flow.SteamUsed = pumpSteam;
            flow.Condensed = Math.Min(condensed, delivered);
            flow.TenderDraw = Math.Max(0f, delivered - flow.Condensed);
            flow.Speed = speed;
            return Report(flow, inputs, heaterPressure, delivered);
        }

        // Moves the heater toward what the exhaust and pump steam can hold the
        // feed at, and returns the steam condensed into the feed, kg/s.
        private float Warm(float dt, FeedPumpInputs inputs, float saturation, float exhaustShare, float water, ISteamProperties steam, float pumpSteam = 0f)
        {
            var available = spec.CondensableShare * Math.Max(0f, inputs.ExhaustFlow) + pumpSteam;
            var cold = inputs.TenderWaterTemperature;
            var target = Math.Max(cold, saturation - spec.Approach);
            float steady;
            var condensed = 0f;
            if (available <= 0f)
            {
                steady = cold;
            }
            else if (water <= 0f)
            {
                steady = target;
            }
            else
            {
                var heaterPressure = 1f + (spec.HeaterPressure - 1f) * exhaustShare;
                var steamHeat = steam.SteamEnthalpy(heaterPressure) - steam.WaterEnthalpy(target);
                var needed = water * (steam.WaterEnthalpy(target) - steam.WaterEnthalpy(cold)) / Math.Max(1f, steamHeat);
                if (available >= needed)
                {
                    steady = target;
                    condensed = needed;
                }
                else
                {
                    var mixed = (water * steam.WaterEnthalpy(cold) + available * steam.SteamEnthalpy(heaterPressure)) / (water + available);
                    steady = Math.Min(target, steam.WaterTemperature(mixed));
                    condensed = available;
                }
            }
            HeaterTemperature = Relax(HeaterTemperature, steady, steady > HeaterTemperature ? spec.WarmTime : spec.CoolTime, dt);
            return condensed;
        }

        private FeedPumpFlow Report(FeedPumpFlow flow, FeedPumpInputs inputs, float heaterPressure, float delivered)
        {
            flow.FeedTemperature = HeaterTemperature;
            flow.HeaterTemperature = HeaterTemperature;
            var share = delivered / spec.Capacity;
            if (spec.Type == FeedHeaterType.Open)
            {
                // The cold pump's discharge: 4–12 psi over the heater's back pressure while it pumps.
                flow.Gauge = flow.State == FeedPumpState.LostSuction ? 0.05f
                    : flow.State == FeedPumpState.Off ? heaterPressure - 1f
                    : heaterPressure - 1f + 0.28f + 0.55f * share;
            }
            else
            {
                // Delivery: over boiler pressure while it feeds.
                var boiler = Math.Max(0f, inputs.BoilerPressure - 1f);
                flow.Gauge = flow.State == FeedPumpState.Feeding ? boiler + 1f * Math.Max(0.2f, share)
                    : flow.State == FeedPumpState.LostSuction || flow.State == FeedPumpState.Tripped ? 0f
                    : 0f;
            }
            return flow;
        }

        private void Spin(float toward, float dt) => speed = Relax(speed, toward, spec.SpinTime, dt);

        private static float Relax(float value, float toward, float timeConstant, float dt) =>
            toward + (value - toward) * (float)Math.Exp(-dt / timeConstant);

        private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));
    }
}
