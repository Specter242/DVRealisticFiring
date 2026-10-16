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

        /// <summary>Temperature of saturated steam at <paramref name="pressure"/>.</summary>
        float SaturationTemperature(float pressure);
    }

    public enum InjectorState
    {
        /// <summary>Steam valve shut.</summary>
        Off,

        /// <summary>
        /// Cracked, or restarting: water, with the little steam that primes
        /// it, runs from the overflow until the jet takes hold.
        /// </summary>
        Priming,

        /// <summary>Singing: feeding the boiler.</summary>
        Working,

        /// <summary>
        /// Cracked but too hot to take hold: the water flashes, and steam and
        /// hot water spill while the water running through cools it.
        /// </summary>
        Hot,

        /// <summary>
        /// Too little pressure to take hold or hold on: water spills from the
        /// overflow, and it picks up by itself when pressure returns.
        /// </summary>
        LowPressure,

        /// <summary>
        /// The tender is dry: steam blows from the overflow and heats the
        /// injector; it picks up by itself when water returns, if cool enough.
        /// </summary>
        NoWater,

        /// <summary>
        /// The jet broke, opened before it took hold or too hot to restart:
        /// steam blows from the overflow until the lever is shut and the
        /// injector cracked again.
        /// </summary>
        Broken,
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

 public const float SteamPrimeFrom = 0.25f;
        public const float SteamFeedFrom = 0.75f;
        public const float SteamPrime = 0.5f;

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
        public float PickUpTime { get; set; } = 1.5f;

        /// <summary>Tender water lifted per kg of live steam.</summary>
        public float WaterPerSteam { get; set; } = 12f;

        /// <summary>Share of the water it would lift that spills while it is not feeding.</summary>
        public float SpillShare { get; set; } = 0.6f;

        /// <summary>
        /// A lifting injector, above the tender's water, lifts it with its
        /// priming steam (the JK3's, as Finnish engines carried); a non-lifter
        /// (Nathan, Chicago, Hancock, on the American engines) sits below it
        /// and the water runs to it by gravity: with its own water valve open
        /// and the steam shut, the water runs out of the overflow (research,
        /// 2026-10-11: "admit water first; when it appears at the overflow,
        /// the steam valve should be opened").
        /// </summary>
        public bool Lifting { get; set; }

        /// <summary>A non-lifter's gravity flow out of the overflow, steam shut, as a share of its full delivery at the water valve's opening.</summary>
        public float GravityShare { get; set; } = 0.25f;

 public float LeastWaste { get; set; } = 0.3f;

        /// <summary>
        /// Whether starting technique and heat matter: it must be cracked to
        /// prime before it is opened, and a hot injector will not take hold.
        /// Off, it only needs pressure and water.
        /// </summary>
        public bool TechniqueAndHeat { get; set; } = true;

        /// <summary>
        /// The lever opening that primes it: the first movement of a Sellers
        /// non-lifting injector's single lever (PRR, 1949). Opened past this
        /// before the jet takes hold, the jet breaks.
        /// </summary>
        public float CrackOpening { get; set; } = 0.2f;

        // The least it delivers steadily, as a share of its most at that
        // pressure (absolute bar): William Sellers & Co.'s self-acting
        // injectors under test, 37 % from 60 to 120 psi rising to 45 % at
        // 200 and 49 % at 225 (Kneass, Practice and Theory of the Injector,
        // 1910, p.150; Railway Mechanical Engineer, April 1939, p.154). The
        // range narrows at high pressure (Kneass p.153).
        private static readonly (float Pressure, float Share)[] LeastShares =
        {
            (3.1f, 0.40f), (5.2f, 0.37f), (9.4f, 0.366f), (11.4f, 0.39f),
            (13.1f, 0.42f), (14.8f, 0.45f), (16.5f, 0.49f),
        };

        /// <summary>
        /// The least lever it delivers at steadily once it sings, at
        /// <paramref name="pressure"/> absolute bar: turned down further, it
        /// wastes at the overflow. 0 without <see cref="TechniqueAndHeat"/>.
        /// </summary>
        public float LeastShare(float pressure) => TechniqueAndHeat ? LeastScale * SellersLeast(pressure) : 0f;

 public float LeastScale { get; set; } = 1f;

        private static float SellersLeast(float pressure)
        {
            if (pressure <= LeastShares[0].Pressure)
                return LeastShares[0].Share;
            for (var i = 1; i < LeastShares.Length; i++)
            {
                if (pressure <= LeastShares[i].Pressure)
                {
                    var (p0, s0) = LeastShares[i - 1];
                    var (p1, s1) = LeastShares[i];
                    return s0 + (s1 - s0) * (pressure - p0) / (p1 - p0);
                }
            }
            return LeastShares[LeastShares.Length - 1].Share;
        }

        /// <summary>
        /// The hottest the injector can be and still take hold, in °C, with no
        /// steam pressure at the nozzle; it falls by <see cref="RestartLimitDropPerBar"/>.
        /// Through Kneass's tests of a Sellers non-lifter (1910): 57 °C at
        /// 60 psi, 41 °C at 200 psi.
        /// </summary>
        public float RestartLimit { get; set; } = 64.1f;

        /// <summary>Fall in the restart limit per bar of gauge pressure at the nozzle, in °C.</summary>
        public float RestartLimitDropPerBar { get; set; } = 1.67f;

        /// <summary>
        /// How much hotter than its restart limit a working injector can get
        /// before the jet breaks, in °C: a Sellers lifter at 200 psi restarts
        /// up to 40 °C and works up to 52 °C (Kneass).
        /// </summary>
        public float BreakMargin { get; set; } = 12f;

        /// <summary>What a shut injector cools to, under the cab, in °C.</summary>
        public float AmbientTemperature { get; set; } = 25f;

        /// <summary>Time constant of a shut injector cooling, in s: several minutes.</summary>
        public float ShutCoolingTime { get; set; } = 180f;

        /// <summary>Time constant of tender water running through and out of the overflow cooling it, in s.</summary>
        public float SpillCoolingTime { get; set; } = 10f;

        /// <summary>Time constant of feed water cooling a working injector, in s.</summary>
        public float WorkingCoolingTime { get; set; } = 3f;

        /// <summary>Time constant of steam blowing through heating it, in s.</summary>
        public float SteamHeatingTime { get; set; } = 4f;

        /// <summary>Share of the water spilling from a hot injector that flashes to steam.</summary>
        public float HotFlashShare { get; set; } = 0.15f;
    }

    /// <summary>What the boiler, tender, and cab give the injector each tick.</summary>
    public struct InjectorInputs
    {
        /// <summary>
        /// Steam valve opening, 0 shut to 1 wide open (the cab's injector
        /// lever); with a separate <see cref="WaterValve"/>, the three-position
        /// steam valve (shut, prime, wide open).
        /// </summary>
        public float SteamValve;

 public float? WaterValve;

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

        /// <summary>Temperature of the injector's body, in °C.</summary>
        public float InjectorTemperature;

        /// <summary>The hottest it can be and take hold at this pressure and lever, in °C.</summary>
        public float RestartLimit;
    }

    /// <summary>
    /// A live-steam injector worked by one lever, like a Sellers non-lifter.
    /// Cracked at enough pressure it spills from the overflow for a moment,
    /// then picks up and sings: live steam condenses into tender water and
    /// drives it into the boiler warm, and the lever can then be opened as far
    /// as wanted. Delivery rises with pressure to the full rate at rated
    /// pressure. Opened past the crack before it takes hold, the jet breaks
    /// and blows steam until the lever is shut. Steam blowing through heats
    /// it; too hot, it will not take hold until water running through it, or
    /// standing shut, cools it. Once it has taken, it restarts by itself when
    /// sagging pressure recovers or the tender is refilled, if cool enough.
    /// </summary>
    public sealed class Injector
    {
        private readonly InjectorSpec spec;
        private float pickUpTimer;

        // Whether the jet has taken hold since the lever was opened; after
        // that it restarts by itself.
        private bool taken;

        // Seconds left in which a lever found open to feed was feeding already.
        private float resuming;

        public Injector(InjectorSpec spec)
        {
            this.spec = spec ?? throw new ArgumentNullException(nameof(spec));
            Temperature = spec.AmbientTemperature;
        }

        public InjectorSpec Spec => spec;

 public void Resume(float seconds) => resuming = Math.Max(0f, seconds);

        public InjectorState State { get; private set; }

        /// <summary>Temperature of the injector's body, in °C.</summary>
        public float Temperature { get; private set; }

        public InjectorFlow Step(float dt, InjectorInputs inputs, ISteamProperties steam)
        {
            if (steam == null)
                throw new ArgumentNullException(nameof(steam));
            if (dt > 0f && resuming > 0f)
                resuming = Math.Max(0f, resuming - dt);
            var pressure = inputs.BoilerPressure;
            var hasWater = inputs.TenderWater > 0f;
            // The lever sets the jet (its water); the steam valve's opening
            // sets the steam that blows through when the jet is not working.
            float lever, steamOpening;
            bool shut, cracked;
            if (inputs.WaterValve is float waterValve)
            {
                var water = Math.Max(0f, Math.Min(1f, waterValve));
                shut = inputs.SteamValve < InjectorSpec.SteamPrimeFrom;
                cracked = inputs.SteamValve < InjectorSpec.SteamFeedFrom;
                steamOpening = cracked ? spec.CrackOpening : 1f;
                lever = cracked ? Math.Min(spec.CrackOpening, water) : water;
                hasWater &= water > InjectorSpec.OpenThreshold;
                if (shut)
                    return Shut(dt, inputs, pressure, water);
            }
            else
            {
                lever = Math.Min(1f, inputs.SteamValve);
                steamOpening = lever;
                cracked = lever <= spec.CrackOpening;
                if (lever <= InjectorSpec.OpenThreshold)
                    return Shut(dt, inputs, pressure, 0f);
            }
            var limit = RestartLimitAt(pressure, steamOpening);
            if (!(dt > 0f))
                return new InjectorFlow { State = State, FeedTemperature = inputs.TenderWaterTemperature, InjectorTemperature = Temperature, RestartLimit = limit };

            if (resuming > 0f && !taken && !cracked && State != InjectorState.Broken)
            {
                // Feeding when saved: it picks up where it was, cool, without priming.
                taken = true;
                resuming = 0f;
                if (hasWater && pressure >= spec.HoldPressure)
                    State = InjectorState.Working;
            }
            State = Next(dt, cracked, pressure, hasWater, limit);

            // What it lifts with this much jet at this pressure.
            var steamShare = 1f / (1f + spec.WaterPerSteam);
            var capacity = Capacity(pressure);
            var full = spec.MaxDelivery * lever * capacity;
            var steamFlow = full * steamShare;
            var blowing = spec.MaxDelivery * steamOpening * capacity * steamShare;
            // The tender limits the water it can lift.
            var lifted = Math.Min(full - steamFlow, Math.Max(0f, inputs.TenderWater) / dt);

            var flow = Flow(dt, inputs, steam, pressure, steamFlow, blowing, lifted, lever);
            HeatOrCool(inputs.TenderWaterTemperature, steam.SaturationTemperature(pressure), dt);
            flow.InjectorTemperature = Temperature;
            flow.RestartLimit = limit;
            return flow;
        }

        // Steam shut: it cools, and a non-lifter with its water valve open runs
        // the tender's water out of the overflow.
        private InjectorFlow Shut(float dt, InjectorInputs inputs, float pressure, float water)
        {
            var limit = RestartLimitAt(pressure, 0f);
            if (!(dt > 0f))
                return new InjectorFlow { State = State, FeedTemperature = inputs.TenderWaterTemperature, InjectorTemperature = Temperature, RestartLimit = limit };
            State = InjectorState.Off;
            pickUpTimer = 0f;
            taken = false;
            var gravity = !spec.Lifting && water > InjectorSpec.OpenThreshold
                ? Math.Min(spec.GravityShare * spec.MaxDelivery * water, Math.Max(0f, inputs.TenderWater) / dt)
                : 0f;
            if (gravity > 0f)
                Relax(inputs.TenderWaterTemperature, spec.SpillCoolingTime, dt);
            else
                Relax(spec.AmbientTemperature, spec.ShutCoolingTime, dt);
            return new InjectorFlow
            {
                State = State,
                TenderDraw = gravity,
                Overflow = gravity,
                FeedTemperature = inputs.TenderWaterTemperature,
                InjectorTemperature = Temperature,
                RestartLimit = limit,
            };
        }

        /// <summary>What it feeds with the lever wide open at <paramref name="pressure"/>, in kg/s.</summary>
        public float FullDelivery(float pressure) => spec.MaxDelivery * Capacity(pressure);

        /// <summary>
        /// The hottest it can be and take hold, in °C: a part-open lever
        /// throttles the steam at the nozzle and raises it.
        /// </summary>
        public float RestartLimitAt(float pressure, float lever)
        {
            var nozzle = Math.Max(0f, pressure - 1f) * Math.Max(0f, Math.Min(1f, lever));
            return spec.RestartLimit - spec.RestartLimitDropPerBar * nozzle;
        }

        private InjectorState Next(float dt, bool cracked, float pressure, bool hasWater, float limit)
        {
            if (State == InjectorState.Broken)
                return InjectorState.Broken;
            var heat = spec.TechniqueAndHeat;
            // A fresh start must be cracked until it takes hold.
            if (heat && !taken && !cracked)
                return InjectorState.Broken;
            if (!hasWater)
            {
                pickUpTimer = 0f;
                return InjectorState.NoWater;
            }

            if (State == InjectorState.Working)
            {
                if (pressure < spec.HoldPressure)
                    return InjectorState.LowPressure;
                return heat && Temperature > limit + spec.BreakMargin ? InjectorState.Broken : InjectorState.Working;
            }

            if (pressure < spec.StartPressure)
            {
                pickUpTimer = 0f;
                return InjectorState.LowPressure;
            }
            if (heat && Temperature > limit)
            {
                // Cracked, the water running through cools it; opened up, the
                // steam keeps it hot and the jet breaks.
                pickUpTimer = 0f;
                return cracked ? InjectorState.Hot : InjectorState.Broken;
            }
            if ((pickUpTimer += dt) < spec.PickUpTime)
                return InjectorState.Priming;
            taken = true;
            return InjectorState.Working;
        }

        private InjectorFlow Flow(float dt, InjectorInputs inputs, ISteamProperties steam, float pressure, float steamFlow, float blowing, float lifted, float lever)
        {
            var spill = spec.SpillShare * lifted;
            switch (State)
            {
                case InjectorState.Working:
                {
                    // Short of tender water, it condenses steam in proportion.
                    var steamShare = 1f / (1f + spec.WaterPerSteam);
                    var condensed = Math.Min(steamFlow, lifted * steamShare / (1f - steamShare));
                    // Turned down below its least it holds the jet but wastes:
                    // a restarting injector "can be reduced below the actual
                    // minimum capacity ... without breaking the jet", with waste
                    // at the overflow (Kneass p.154, p.145). Part of the jet goes
                    // out there as warm water, more the further it is turned
                    // down; steam from the overflow is a broken jet's or a hot
                    // injector's, not a singing one's.
                    var least = spec.LeastShare(pressure);
                    var wasted = least > 0f && lever < least ? spec.LeastWaste * (least - lever) / least : 0f;
                    // What the boiler cannot take goes out of the overflow, warm.
                    var nominal = (lifted + condensed) * (1f - wasted);
                    var delivered = Math.Min(nominal, Math.Max(0f, inputs.BoilerSpace) / dt);
                    var mixed = lifted + condensed;
                    var enthalpy = mixed > 0f
                        ? (condensed * steam.SteamEnthalpy(pressure) + lifted * steam.WaterEnthalpy(inputs.TenderWaterTemperature)) / mixed
                        : steam.WaterEnthalpy(inputs.TenderWaterTemperature);
                    return new InjectorFlow
                    {
                        State = State,
                        WaterIn = delivered,
                        SteamUsed = condensed,
                        TenderDraw = lifted,
                        Overflow = nominal - delivered + wasted * mixed,
                        FeedTemperature = steam.WaterTemperature(enthalpy),
                    };
                }
                case InjectorState.Priming:
                case InjectorState.LowPressure:
                    // Water only: the little steam condenses in it.
                    return new InjectorFlow { State = State, SteamUsed = steamFlow, TenderDraw = spill, Overflow = spill, FeedTemperature = inputs.TenderWaterTemperature };
                case InjectorState.Hot:
                {
                    // Some of the water flashes off the hot tubes.
                    var flash = spec.HotFlashShare * spill;
                    return new InjectorFlow { State = State, SteamUsed = blowing, TenderDraw = spill, Overflow = spill - flash, OverflowSteam = blowing + flash, FeedTemperature = inputs.TenderWaterTemperature };
                }
                case InjectorState.NoWater:
                    // Dry (the tender, or its water valve shut): the steam valve's steam blows through.
                    return new InjectorFlow { State = State, SteamUsed = blowing, OverflowSteam = blowing, FeedTemperature = inputs.TenderWaterTemperature };
                default:
                    // Broken: the steam blows straight through, with some water.
                    return new InjectorFlow { State = State, SteamUsed = blowing, TenderDraw = spill, Overflow = spill, OverflowSteam = blowing, FeedTemperature = inputs.TenderWaterTemperature };
            }
        }

        private void HeatOrCool(float tenderWater, float steamTemperature, float dt)
        {
            switch (State)
            {
                case InjectorState.Working:
                    Relax(tenderWater, spec.WorkingCoolingTime, dt);
                    break;
                case InjectorState.NoWater:
                case InjectorState.Broken:
                    Relax(steamTemperature, spec.SteamHeatingTime, dt);
                    break;
                default:
                    Relax(tenderWater, spec.SpillCoolingTime, dt);
                    break;
            }
        }

        private void Relax(float toward, float timeConstant, float dt) =>
            Temperature = toward + (Temperature - toward) * (float)Math.Exp(-dt / timeConstant);

        // Delivery grows with the square root of gauge pressure, reaching the
        // full rate at rated pressure.
        private float Capacity(float pressure)
        {
            var gauge = Math.Max(0f, pressure - 1f);
            return Math.Min(1f, (float)Math.Sqrt(gauge / (spec.RatedPressure - 1f)));
        }
    }
}
