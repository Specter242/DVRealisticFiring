using System;
using System.Collections.Generic;
using System.Linq;

namespace RealisticFiring.Core
{
    /// <summary>How pressing a reading or a next step is: shown with a mark and a color.</summary>
    public enum AdviceLevel
    {
        /// <summary>In hand; nothing to do but keep at it.</summary>
        Ok,

        /// <summary>Wants doing soon.</summary>
        Watch,

        /// <summary>Wants doing now.</summary>
        Act,
    }

    /// <summary>The one next step, in a fireman's words.</summary>
    public readonly struct Advice
    {
        public Advice(AdviceLevel level, string text, int lightingStep = 0)
        {
            Level = level;
            Text = text;
            LightingStep = lightingStep;
        }

        public AdviceLevel Level { get; }
        public string Text { get; }

        /// <summary>
        /// The step of firing up an oil engine it gives: 1 to
        /// <see cref="FiringAdvice.LightingSteps"/> while lighting, one more
        /// once the shed's steam can come off, otherwise 0.
        /// </summary>
        public int LightingStep { get; }
    }

    /// <summary>One injector, as the next step reads it.</summary>
    public readonly struct InjectorReading
    {
        public InjectorReading(InjectorState state, float waterValve, bool steamValve, bool tenderValveShut = false)
        {
            State = state;
            WaterValve = waterValve;
            SteamValve = steamValve;
            TenderValveShut = tenderValveShut;
        }

        public InjectorState State { get; }

        /// <summary>The water valve (or the one lever), 0 to 1.</summary>
        public float WaterValve { get; }

        /// <summary>Whether it has its own steam valve (Off, Prime, Feed).</summary>
        public bool SteamValve { get; }

        /// <summary>Whether the tender's water valve to it is shut.</summary>
        public bool TenderValveShut { get; }
    }

    /// <summary>An oil fire and its engine, as the next step reads them.</summary>
    public struct OilReading
    {
        public bool FireOn;
        public float Vapor;
        public float PooledOil;
        public bool OnSteamLine;
        public float SteamLineOpening;
        /// <summary>Boiler pressure, absolute bar.</summary>
        public float BoilerPressure;
        public float OilTemperature;
        public float Torch;
        public float BrickTemperature;
        public float StackSmoke;
        public float FlueSoot;
        public bool BlowerOn;
        public bool AtomizerOpen;
        /// <summary>Whether a shed stand's blower line reaches the engine: the shed's steam comes only by plugging one in.</summary>
        public bool StandNearby;
        public float GlassLevel;
        public IReadOnlyList<InjectorReading> Injectors;
    }

    /// <summary>One step of lighting an oil fire, for the overlay's checklist.</summary>
    public readonly struct LightingStep
    {
        public LightingStep(string title, string how, bool done, bool current)
        {
            Title = title;
            How = how;
            Done = done;
            Current = current;
        }

        public string Title { get; }
        public string How { get; }
        public bool Done { get; }
        public bool Current { get; }
    }

 public static class FiringAdvice
    {
        /// <summary>The glass this low wants the injector on, 0 to 1.</summary>
        public const float LowGlass = 0.3f;

        /// <summary>Smoke this thick with the door shut wants the door cracked.</summary>
        public const float Smoking = 0.4f;

        /// <summary>The door open this far already burns the smoke off.</summary>
        public const float CrackedDoor = 0.2f;

        /// <summary>A zone with less than this share of the middle zone's coal is burning thin.</summary>
        public const float ThinShare = 0.35f;

        /// <summary>The ashpan this full wants emptying, as the fireman says.</summary>
        public const float FullAshpan = 0.5f;

        /// <summary>Absolute bar below which a dead engine has no steam for the burner.</summary>
        public const float DeadBoiler = 2f;

        /// <summary>Absolute bar from which the boiler's own steam can take over from the shed's.</summary>
        public const float OwnSteam = 7.9f;

        /// <summary>Oil this warm runs: a little below where it runs freely.</summary>
        public const float RunningOil = OilFire.WarmOil - 5f;

        /// <summary>Stack smoke from this is black, below the light haze is clear.</summary>
        public const float BlackStack = 0.6f;
        public const float HazeStack = 0.08f;

        /// <summary>Flues this sooted want sanding.</summary>
        public const float SootedFlues = 0.3f;

        public const int LightingSteps = 5;

        /// <summary>The next step on a coal fire; <paramref name="driftingHookedUp"/>, the engine drifts with its throttle shut and reverser hooked up.</summary>
        public static Advice ForCoal(FireModel fire, float door, float glass, IReadOnlyList<InjectorReading> injectors, bool driftingHookedUp = false)
        {
            if (fire == null)
                throw new ArgumentNullException(nameof(fire));
            if (Injector(injectors) is Advice injector)
                return injector;
            if (glass < LowGlass)
                return new Advice(AdviceLevel.Act, "Water's low in the glass: get the injector on.");
            if (!fire.FireOn)
                return new Advice(AdviceLevel.Watch, "The fire's out: put some coal in and light it.");
            if (driftingHookedUp)
                return new Advice(AdviceLevel.Watch, "Drifting hooked up with the throttle shut: drop the reverser to full gear, or crack the throttle.");
            if (fire.SmokeDensity >= Smoking && door < CrackedDoor)
                return new Advice(AdviceLevel.Watch, "She's smoking: crack the fire door to burn it off.");
            var thin = ThinZone(fire.Zones);
            if (thin >= 0)
                return new Advice(AdviceLevel.Watch, $"Fire the {GrateLayout.Describe((GrateZone)thin)}: it's burning thin ({fire.Zones[thin].Coal:F1} kg).");
            var clean = WantsCleaning(fire);
            var pan = fire.AshpanFill >= FullAshpan;
            if (clean && pan)
                return new Advice(AdviceLevel.Watch, "Clinker's building and the ashpan's half full: clean the fire and empty the pan.");
            if (clean)
                return new Advice(AdviceLevel.Watch, "Clinker's building: clean the fire.");
            if (pan)
                return new Advice(AdviceLevel.Watch, "Ashpan's half full: empty it.");
            return new Advice(AdviceLevel.Ok, "Steady fire. Keep firing light and often.");
        }

        /// <summary>
        /// A boiler so damaged it would burst under its safety valve, at
        /// <paramref name="burst"/> absolute bar: a warning before any other
        /// advice, urgent within <see cref="AutoFiremanBase.BurstMargin"/> of
        /// it; else null.
        /// </summary>
        public static Advice? Bursting(float pressure, float safetyValve, float burst)
        {
            if (!(burst < safetyValve))
                return null;
            return new Advice(pressure > burst - AutoFiremanBase.BurstMargin ? AdviceLevel.Act : AdviceLevel.Watch,
                $"The boiler's damaged: it would burst at {burst - 1f:F1} bar, under the safety valve. Keep the pressure down and get it repaired.");
        }

        /// <summary>The zone burning thinnest against the middle of the bed, or -1 when none is thin.</summary>
        public static int ThinZone(IReadOnlyList<ZoneState> zones)
        {
            if (zones == null || zones.Count == 0)
                return -1;
            var sorted = zones.Select(z => z.Coal).OrderBy(c => c).ToList();
            var middle = sorted[sorted.Count / 2];
            var thinnest = 0;
            for (var i = 1; i < zones.Count; i++)
                if (zones[i].Coal < zones[thinnest].Coal)
                    thinnest = i;
            return zones[thinnest].Coal < ThinShare * middle ? thinnest : -1;
        }

        /// <summary>Whether any zone holds enough clinker that the fireman would clean the fire.</summary>
        public static bool WantsCleaning(FireModel fire) =>
            fire.Zones.Any(z => z.Clinker > AutoFireman.CleanClinkerShare * fire.ReferenceZoneMass);

        /// <summary>The next step on an oil fire: lighting it up, then reading the burner.</summary>
        public static Advice ForOil(OilReading oil)
        {
            if (!oil.FireOn)
                return Lighting(oil);
            if (Injector(oil.Injectors) is Advice injector)
                return injector;
            if (oil.GlassLevel < LowGlass)
                return new Advice(AdviceLevel.Act, "Water's low in the glass: get the injector on.");
            if (oil.OnSteamLine && oil.SteamLineOpening > 0f && oil.BoilerPressure >= OwnSteam)
                return new Advice(AdviceLevel.Watch, $"She makes her own steam ({oil.BoilerPressure - 1f:F1} bar): shut the stand's valve and take its line off before you leave.", LightingSteps + 1);
            if (oil.StackSmoke >= BlackStack)
                return new Advice(AdviceLevel.Act, "Black at the stack: too much oil for the air. Ease the firing valve or open the damper.");
            if (oil.StackSmoke < HazeStack)
                return new Advice(AdviceLevel.Watch, "Clear stack: too much air, wasting heat. Ease the damper or blower, or give her more oil.");
            if (oil.FlueSoot > SootedFlues)
                return new Advice(AdviceLevel.Watch, "Flues are sooting: at speed, load the shovel at the sand box and throw it through the peep hole.");
            return new Advice(AdviceLevel.Ok, "Light haze at the stack: she's burning right.");
        }

        /// <summary>Lighting a cold oil fire, step by step: done steps ticked, the current one lit.</summary>
        public static IReadOnlyList<LightingStep> Steps(OilReading oil)
        {
            var done = new[]
            {
                oil.OnSteamLine || oil.BoilerPressure >= DeadBoiler,
                oil.OilTemperature >= RunningOil,
                oil.Vapor + oil.PooledOil <= OilFire.SafeToLight,
                oil.BlowerOn && oil.AtomizerOpen,
                false,
            };
            var titles = new[]
            {
                ("Shed's steam on", "a roundhouse or engine shed stand's blower line in the smokebox's fitting, its valve open"),
                ("Oil warm", $"OIL HEATER open; runs from {OilFire.WarmOil:F0} °C (now {oil.OilTemperature:F0} °C)"),
                ("Box clear", $"under {OilFire.SafeToLight:F2} kg of vapor and pooled oil (now {oil.Vapor + oil.PooledOil:F2}); the blower clears vapor, pooled oil drains from the pan"),
                ("Blower on, atomizer cracked", "air through the box, steam at the burner"),
                ("Light it", $"lighter in the open door, crack the firing valve within {OilFire.TorchTime:F0} s"),
            };
            var current = Array.IndexOf(done, false);
            return titles.Select((t, i) => new LightingStep(t.Item1, t.Item2, done[i] && i < current, i == current)).ToList();
        }

        private static Advice Lighting(OilReading oil)
        {
            var steps = Steps(oil);
            var step = steps.Select((s, i) => (s, i)).First(x => x.s.Current).i + 1;
            var of = $"Step {step} of {LightingSteps}";
            switch (step)
            {
                case 1:
                    return new Advice(AdviceLevel.Act, oil.StandNearby
                        ? $"{of}, no steam for the burner: plug the stand's blower line into the fitting on the smokebox and open the stand's valve."
                        : $"{of}, no steam for the burner: tow her to a roundhouse stall or engine shed and plug a stand's blower line into the fitting on the smokebox.", step);
                case 2:
                    return new Advice(AdviceLevel.Watch, $"{of}, warm the oil: OIL HEATER open. It's {oil.OilTemperature:F0} °C and runs from {OilFire.WarmOil:F0} °C.", step);
                case 3:
                    return new Advice(AdviceLevel.Act, oil.Vapor >= oil.PooledOil
                        ? $"{of}, vapor in the box: blow it out with the blower before you light."
                        : $"{of}, oil pooled in the pan: let it drain out a minute or two, damper open, before you light.", step);
                case 4:
                    return new Advice(AdviceLevel.Watch, $"{of}: blower on and the atomizer cracked.", step);
                default:
                    if (oil.Torch > 0f)
                        return new Advice(AdviceLevel.Act, $"Lit: crack the firing valve now, atomizer on ({oil.Torch:F0} s).", step);
                    return new Advice(AdviceLevel.Watch, oil.BrickTemperature > OilFire.BrickRelights
                        ? $"{of}, the brick's hot: crack the firing valve and she relights."
                        : $"{of}: your lighter in the open door, then crack the firing valve within {OilFire.TorchTime:F0} s.", step);
            }
        }

        // The worst injector trouble, if any, and what to do about it.
        private static Advice? Injector(IReadOnlyList<InjectorReading> injectors)
        {
            if (injectors == null)
                return null;
            foreach (var injector in injectors)
            {
                switch (injector.State)
                {
                    case InjectorState.Hot:
                        return new Advice(AdviceLevel.Act, injector.SteamValve
                            ? "Injector's too hot to take: hold the steam at Prime to cool it."
                            : "Injector's too hot to take: hold it cracked to cool it.");
                    case InjectorState.NoWater:
                        return new Advice(AdviceLevel.Act, injector.TenderValveShut
                            ? "No water at the injector: open the tender water valve."
                            : injector.WaterValve <= InjectorSpec.OpenThreshold
                                ? "No water at the injector: open the water valve."
                                : "Tender's dry: take water.");
                    case InjectorState.Broken:
                        return new Advice(AdviceLevel.Act, injector.SteamValve
                            ? "Injector's jet broke: steam Off, then Prime till it picks up."
                            : "Injector's jet broke: shut it, then crack it till it picks up.");
                }
            }
            return null;
        }
    }
}
