using System.Linq;
using System.Runtime.CompilerServices;
using DV.JObjectExtstensions;
using DV.Simulation.Cars;
using LocoSim.Definitions;
using LocoSim.Implementations;
using Newtonsoft.Json.Linq;
using RealisticFiring.Core;
using UnityEngine;

namespace RealisticFiring.Sim
{
 internal sealed class EngineSuperheater
    {
        // Heat a kg of steam takes from the feed, J: about 2.5 MJ at working pressure.
        private const float EvaporationHeat = 2.5e6f;
        private const string WearSaveKey = "rfSuperheaterWear";

        // Any other steam engine: a Type A, the commonest.
        private static readonly SuperheaterSpec Default = Superheat("Type A", 136f, 0.72f);

        private static readonly ConditionalWeakTable<ReciprocatingSteamEngine, EngineSuperheater> Engines =
            new ConditionalWeakTable<ReciprocatingSteamEngine, EngineSuperheater>();
        private static readonly ConditionalWeakTable<TrainCar, EngineSuperheater> ByCar =
            new ConditionalWeakTable<TrainCar, EngineSuperheater>();
        private static readonly ConditionalWeakTable<ReciprocatingSteamEngine, JObject> PendingSaves =
            new ConditionalWeakTable<ReciprocatingSteamEngine, JObject>();

        private readonly TrainCar car;
        private readonly Port heat;
        private readonly float fullHeat;
        private bool? mirroredCareActive;

        private EngineSuperheater(TrainCar car, SuperheaterSpec spec, Port heat, float fullHeat)
        {
            this.car = car;
            Model = new Superheater(spec);
            this.heat = heat;
            this.fullHeat = fullHeat;
        }

        public Superheater Model { get; }

        /// <summary>True on a multiplayer guest once host-authoritative superheater state has been mirrored.</summary>
        internal bool Mirrored { get; set; }

        /// <summary>
        /// Whether superheater element care is active for this engine: the
        /// host's mirrored setting on a multiplayer guest, otherwise local settings.
        /// </summary>
        public bool SuperheaterCareActive =>
            mirroredCareActive ?? (Main.Settings == null || Main.Settings.SuperheaterCare);

        /// <summary>Applies host-authoritative superheater state on a multiplayer guest.</summary>
        internal void Mirror(float elements, float superheat, float elementTemperature, float wear, bool overheating, bool approaching, bool careActive)
        {
            mirroredCareActive = careActive;
            Model.Mirror(elements, superheat, elementTemperature, wear, overheating, approaching);
            Model.SetCareEnabled(careActive);
            Mirrored = true;
        }

        // Standing with a bright fire and the throttle shut, the elements went
        // "above red heat" (Santa Fe, 1921). They take minutes to come up; their
        // steel's heat over the steam's at the full rate is about 75 s.
        private static SuperheaterSpec Superheat(string kind, float full, float halfShare) =>
            new SuperheaterSpec { Kind = kind, FullSuperheat = full, HalfSuperheat = full * halfShare, StandingSuperheat = 400f, TimeConstant = 75f };

        internal static bool IsExempt(string livery) =>
            LocomotiveProfiles.For(livery)?.SuperheaterExempt == true || MakerFitRegistry.IsSuperheaterExempt(livery);

        internal static SuperheaterSpec SpecFor(string livery) =>
            LocomotiveProfiles.For(livery)?.Superheater
            ?? (MakerFitRegistry.TryGetSuperheater(livery, out var maker) ? maker : Default);

        /// <summary>Fits <paramref name="car"/>'s engine with its superheater. Returns a line for the log, or null.</summary>
        public static string Attach(TrainCar car, SimController controller)
        {
            var flow = controller != null ? controller.simFlow : null;
            var engine = flow != null ? flow.OrderedSimComps.OfType<ReciprocatingSteamEngine>().FirstOrDefault() : null;
            var firebox = controller.connectionsDefinition != null && controller.connectionsDefinition.executionOrder != null
                ? controller.connectionsDefinition.executionOrder.OfType<FireboxDefinition>().FirstOrDefault()
                : null;
            if (engine == null || firebox == null || firebox.burnTime <= 0f)
                return null;
            flow.TryGetPort($"{firebox.ID}.{firebox.heatReadOut.ID}", out var heat, canBeNullOrEmpty: true);
            if (heat == null)
                return null;
            var livery = car != null && car.carLivery != null ? car.carLivery.id : null;
            if (IsExempt(livery))
            {
                EngineCylinders.Of(engine).Model.WallCondensation = false;
                return "steam as it was: no superheater, no wet walls";
            }
            var spec = SpecFor(livery);
            var fullHeat = firebox.maxCoalCapacity / firebox.burnTime * FireboxParameters.CoalHeatContent * firebox.efficiencyAtMaxCombustion;
            Attach(car, engine, spec, heat, fullHeat);
            return spec.Saturated
                ? "saturated: no superheater"
                : $"{spec.Kind} superheater: {spec.FullSuperheat:F0} °C at the full rate of {fullHeat / 1e6f:F1} MW, {spec.HalfSuperheat:F0} °C at half";
        }

        /// <summary>Attaches a superheater directly to <paramref name="engine"/> (also used by offline adapter checks).</summary>
        internal static EngineSuperheater Attach(TrainCar car, ReciprocatingSteamEngine engine, SuperheaterSpec spec, Port heat, float fullHeat)
        {
            var superheater = new EngineSuperheater(car, spec, heat, fullHeat);
            Engines.Remove(engine);
            Engines.Add(engine, superheater);
            if (car != null)
            {
                ByCar.Remove(car);
                ByCar.Add(car, superheater);
            }
            if (PendingSaves.TryGetValue(engine, out var saved))
            {
                PendingSaves.Remove(engine);
                superheater.LoadData(saved);
            }
            return superheater;
        }

        public static bool TryGet(ReciprocatingSteamEngine engine, out EngineSuperheater superheater)
        {
            superheater = null;
            return engine != null && Engines.TryGetValue(engine, out superheater);
        }

        public static bool TryGet(TrainCar car, out EngineSuperheater superheater)
        {
            superheater = null;
            return car != null && ByCar.TryGetValue(car, out superheater);
        }

        /// <summary>A tick: the fire's heat, steam flow, throttle, and drifting valve protection.</summary>
        public void Step(ReciprocatingSteamEngine engine, float delta)
        {
            if (Net.IsClient && Mirrored)
                return;
            var careEnabled = !Net.IsClient && (Main.Settings == null || Main.Settings.SuperheaterCare);
            var fire = fullHeat > 0f ? Mathf.Max(0f, heat.Value) / fullHeat : 0f;
            var startup = careEnabled ? StartupBoost() : 1f;
            var rate = EngineSteamPath.TryGet(engine, out var path) && path.Active ? path.HeaterRate : engine.intakeFlowReadOut.Value;
            var steam = fullHeat > 0f ? Mathf.Max(0f, rate) * EvaporationHeat / fullHeat : 0f;
            var quality = engine.intakeQuality.IsConnected ? engine.intakeQuality.Value : 1f;
            var intakePressure = engine.intakePressure.IsConnected ? Mathf.Max(1f, engine.intakePressure.Value) : 1f;
            var satTemp = BoilerWaterRig.TryGet(car, out var water) && water.BelowBoilingNow
                ? water.WaterTemperature
                : SteamCylinders.SaturationTemperature(intakePressure);
            var throttle = engine.throttleControl.IsConnected ? engine.throttleControl.Value : 0f;
            var driftingValve = car != null ? EngineDrift.ValveOpening(car, engine) : 0f;
            Model.Step(delta, new SuperheaterCareInputs
            {
                Fire = fire,
                Flow = steam,
                Quality = quality,
                SaturationTemperature = satTemp,
                Throttle = throttle,
                DriftingValve = driftingValve,
                StartupMultiplier = startup,
                CareEnabled = careEnabled,
            });
        }

        private float StartupBoost()
        {
            var boost = car != null && FireboxRegistry.TryGet(car, out var coal) ? coal.StartupMultiplier
                : car != null && RealisticOilFirebox.TryGet(car, out var oil) ? oil.StartupBoost
                : 1f;
            return boost > 1f ? boost : 1f;
        }

        internal static void Save(ReciprocatingSteamEngine engine, JObject data)
        {
            if (engine == null || data == null)
                return;
            if (TryGet(engine, out var superheater) && superheater.Model.Wear > 0f)
                data.SetFloat(WearSaveKey, superheater.Model.Wear);
            else if (PendingSaves.TryGetValue(engine, out var pending))
            {
                var wear = pending.GetFloat(WearSaveKey);
                if (wear.HasValue && wear.Value > 0f)
                    data.SetFloat(WearSaveKey, wear.Value);
            }
        }

        internal static void Load(ReciprocatingSteamEngine engine, JObject data)
        {
            if (engine == null)
                return;
            PendingSaves.Remove(engine);
            if (data == null)
            {
                if (TryGet(engine, out var clear))
                    clear.Model.Restore(0f);
                return;
            }
            if (TryGet(engine, out var superheater))
                superheater.LoadData(data);
            else
                PendingSaves.Add(engine, data);
        }

        private void LoadData(JObject data)
        {
            Model.Restore(data.GetFloat(WearSaveKey) ?? 0f);
            Model.SetCareEnabled(SuperheaterCareActive);
        }

        /// <summary>The steam's temperature at <paramref name="pressure"/> absolute bar, in °C.</summary>
        public float SteamTemperature(float pressure) => SteamCylinders.SaturationTemperature(pressure) + Model.Superheat;
    }
}
