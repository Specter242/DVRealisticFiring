using System.Collections.Generic;
using System.Linq;
using DV.JObjectExtstensions;
using HarmonyLib;
using LocoSim.Definitions;
using LocoSim.Implementations;
using Newtonsoft.Json.Linq;
using RealisticFiring.Core;
using UnityEngine;

namespace RealisticFiring.Sim
{
    /// <summary>
    /// Drop-in replacement for the stock firebox on an oil burner (see
    /// <see cref="OilBurnerFit"/>), driving an <see cref="OilFire"/>. Keeps
    /// the stock component ID, ports, port references, and save keys. The
    /// loco's own stoker control is the firing valve, read through the
    /// <see cref="OilFeed"/> that stands in for its stoker; the atomizer and
    /// oil heater are the mod's valves; the draft is the stock exhaust's, its
    /// dampers included. The stock ignite request (the game's lighter held at
    /// the open fire door) stands for the lit waste a crew threw in.
    /// </summary>
    internal sealed class RealisticOilFirebox : SimComponent
    {
        private const string BrickSaveKey = "rfBrick";
        private const string OilTemperatureSaveKey = "rfOilTemp";
        private const string FlueSootSaveKey = "rfFlueSoot";
        private const string VaporSaveKey = "rfVapor";

        private const float StartupMultiplierBase = 25f;

        // A flareback's black cloud clears over this time, in s.
        private const float FlarebackSmokeTime = 3f;

        /// <summary>A shed's steam line, from its stationary boiler: 100 psi, in absolute bar.</summary>
        public const float SteamLinePressure = 1f + 100f / 14.5038f;

        private static readonly AccessTools.FieldRef<PortReference, Port> ReferencedPort =
            AccessTools.FieldRefAccess<PortReference, Port>("port");

        private static readonly Dictionary<TrainCar, RealisticOilFirebox> ByCar = new Dictionary<TrainCar, RealisticOilFirebox>();

        private readonly float startupMaxPressure;
        private readonly PortReference intakeWaterContent;
        private readonly PortReference airFlow;
        private readonly PortReference forwardSpeed;
        private readonly PortReference boilerPressure;
        private readonly PortReference boilerTemperature;
        private readonly PortReference boilerBrokenState;

        private readonly Port coalLevelReadOut;
        private readonly Port coalLevelNormalizedReadOut;
        private readonly Port coalControlExtIn;
        private readonly Port coalEnvDamage;
        private readonly Port ignitionExtIn;
        private readonly Port extinguishExtIn;
        private readonly Port heatReadOut;
        private readonly Port fireOnReadOut;
        private readonly Port smokeDensityReadOut;
        private readonly Port combustionRateNormalizedReadOut;
        private readonly Port temperatureReadOut;
        private readonly Port oxygenAvailabilityReadOut;

        private Port doorPort;
        private Port atomizerPort;
        private Port heaterPort;
        private Port shopSteamPort;
        private Port peepHolePort;
        private float standCheck;

        /// <summary>
        /// How far a shed stand's blower line in the engine's connection is
        /// open, 0 to 1 (checks outside the game set it).
        /// </summary>
        internal static System.Func<TrainCar, SteamExhaust, float> HouseLineOf =
            (car, exhaust) => exhaust != null ? Shed.ShedConnections.BlowerLine(exhaust) : 0f;
        private ReciprocatingSteamEngine engine;
        private SteamExhaust exhaustSim;
        private bool resolved;
        private bool restored;

        public RealisticOilFirebox(FireboxDefinition fDef, TrainCar car)
            : base(fDef.ID)
        {
            Car = car;
            startupMaxPressure = fDef.startupMaxPressure;
            Model = new OilFire(new FireboxParameters(
                fDef.maxCoalCapacity,
                fDef.burnTime,
                fDef.efficiencyAtMaxCombustion,
                fDef.coalDumpRate,
                fDef.combustionRateSmoothTime,
                fDef.temperatureSmoothTime));

            // Same ports, IDs, and default values as the stock Firebox.
            intakeWaterContent = AddPortReference(fDef.intakeWaterContent);
            AddPortReference(fDef.coalDumpControl, 0.5f);
            AddPort(fDef.coalCapacityReadOut, fDef.maxCoalCapacity);
            coalLevelReadOut = AddPort(fDef.coalLevelReadOut);
            coalLevelNormalizedReadOut = AddPort(fDef.coalLevelNormalizedReadOut);
            coalControlExtIn = AddPort(fDef.coalControlExtIn);
            coalEnvDamage = AddPort(fDef.coalEnvDamage);
            ignitionExtIn = AddPort(fDef.ignitionExtIn);
            extinguishExtIn = AddPort(fDef.extinguishExtIn);
            airFlow = AddPortReference(fDef.airFlow);
            heatReadOut = AddPort(fDef.heatReadOut);
            AddPort(fDef.coalDumpFlowNormalizedReadOut);
            fireOnReadOut = AddPort(fDef.fireOnReadOut);
            smokeDensityReadOut = AddPort(fDef.smokeDensityReadOut);
            combustionRateNormalizedReadOut = AddPort(fDef.combustionRateNormalizedReadOut);
            temperatureReadOut = AddPort(fDef.temperatureReadOut, 100f);
            forwardSpeed = AddPortReference(fDef.forwardSpeed);
            boilerPressure = AddPortReference(fDef.boilerPressure);
            boilerTemperature = AddPortReference(fDef.boilerTemperature, 100f);
            boilerBrokenState = AddPortReference(fDef.boilerBrokenState);
            oxygenAvailabilityReadOut = AddPort(fDef.oxygenAvailabilityReadOut);

            coalControlExtIn.ValueUpdatedInternally += OnShovelDelivered;

            if (car != null)
            {
                foreach (var stale in ByCar.Keys.Where(c => c == null).ToList())
                    ByCar.Remove(stale);
                ByCar[car] = this;
            }
        }

        public TrainCar Car { get; }

        public override bool HasSaveData => true;

        public OilFire Model { get; }

        /// <summary>The feed standing in for the loco's stoker, once it has made itself known.</summary>
        public OilFeed Feed { get; set; }

        /// <summary>A flareback's cloud at the stack, 0 to 1.</summary>
        public float FlarebackSmoke { get; private set; }

        /// <summary>Shows the host's flareback cloud, on a multiplayer client.</summary>
        public void MirrorFlarebackSmoke(float smoke) => FlarebackSmoke = smoke;

        /// <summary>
        /// How dark the stack is, 0 clear to 1 black: the fire's smoke, or a
        /// sand burst's or flareback's black cloud. The smoke density readout
        /// and the stack's look both take it.
        /// </summary>
        public float StackDarkness => Mathf.Clamp01(Mathf.Max(Model.StackSmoke, 3f * Mathf.Max(Model.SandBurst, FlarebackSmoke)));

        /// <summary>The flareback on the last tick, 0 none to 1 the worst; the effects take it.</summary>
        public float PendingFlareback { get; set; }

        /// <summary>The fire door's opening, 0 to 1.</summary>
        public float DoorOpening => doorPort != null ? Mathf.Clamp01(doorPort.Value) : 0f;

        public float AtomizerValve => atomizerPort != null ? Mathf.Clamp01(atomizerPort.Value) : OilBurnerFit.Atomizer.Default;

        public float HeaterValve => heaterPort != null ? Mathf.Clamp01(heaterPort.Value) : 0f;

        /// <summary>The cab's shop steam valve, 0 shut to 1 wide open; 0 without one. The shed's steam comes by a stand's line, not by it.</summary>
        public float ShopSteamValve => shopSteamPort != null ? Mathf.Clamp01(shopSteamPort.Value) : 0f;

        /// <summary>Whether a shed stand's blower line reaches the engine's connection: there the shed's steam comes by it alone (checked once a second).</summary>
        public bool StandReaches => standReaches;
        private bool standReaches;

        /// <summary>The boiler's pressure, absolute bar.</summary>
        internal float BoilerPressure => boilerPressure.Value;

 public float SteamLineOpening => HouseLine;

        /// <summary>How far a roundhouse stand's valve is open to the burner through its blower line, 0 without one in.</summary>
        public float HouseLine => HouseLineOf(Car, exhaustSim);

        /// <summary>Air drawn through the firebox, in kg/s.</summary>
        public float AirFlowIn => airFlow.Value;

        /// <summary>Every oil burner's firebox, by car.</summary>
        public static IEnumerable<RealisticOilFirebox> All => ByCar.Where(pair => pair.Key != null).Select(pair => pair.Value).ToList();

        public static bool TryGet(TrainCar car, out RealisticOilFirebox firebox)
        {
            firebox = null;
            return car != null && ByCar.TryGetValue(car, out firebox);
        }

        /// <summary>The oil firebox the player tends from <paramref name="car"/>: its own, or a coupled loco's.</summary>
        public static bool TryGetTended(TrainCar car, out RealisticOilFirebox firebox)
        {
            if (TryGet(car, out firebox))
                return true;
            if (car == null)
                return false;
            foreach (var coupler in new[] { car.frontCoupler, car.rearCoupler })
            {
                var neighbor = coupler != null && coupler.coupledTo != null ? coupler.coupledTo.train : null;
                if (TryGet(neighbor, out firebox))
                    return true;
            }
            return false;
        }

        /// <summary>How hard the draft pulls through the firebox, 0 still to 1 the stock fire's full air.</summary>
        public float Drawing => Mathf.Clamp01(airFlow.Value / Mathf.Max(0.1f, Model.Parameters.MaxCombustionRate * FireModel.AirPerCoal));

        /// <summary>Sand thrown in on the last delivery, in kg, for the log and effects.</summary>
        public float LastSand { get; private set; }

        // A shovelful through the door: sand from the flue sand box scours
        // the flues; coal (from another loco's pile, another mod) has nowhere
        // to go in an oil firebox and is lost.
        private void OnShovelDelivered(float delivered)
        {
            if (delivered == 0f)
                return;
            coalControlExtIn.Value = 0f;
            if (delivered < 0f)
                return;
            var controller = Car != null && Car.SimController != null ? Car.SimController.firebox : null;
            var multiplier = controller != null && controller.coalConsumptionMultiplier > 0f ? controller.coalConsumptionMultiplier : 1f;
            var sand = Visuals.FlueSandBox.TakeLoaded(delivered * multiplier);
            if (!(sand > 0f))
                return;
            ThrowSand(sand);
        }

        /// <summary>On a multiplayer client: a shovel's load thrown in, sand to the host, coal lost.</summary>
        public void DeliverFromClient(float pileMass)
        {
            var sand = Visuals.FlueSandBox.TakeLoaded(pileMass);
            if (sand > 0f)
                ThrowSand(sand);
        }

        /// <summary>Throws <paramref name="sand"/> kg of the loco's sand into the firebox, through the host in multiplayer.</summary>
        public void ThrowSand(float sand)
        {
            if (Net.TrySend(Car, FireAction.Sand, value: sand))
                return;
            var before = Model.FlueSoot;
            Model.ThrowSand(sand, Drawing);
            LastSand = sand;
            if (Main.Settings.LogCoalDeliveries)
                Main.Mod?.Logger.Log($"{Car?.ID}: {sand:F1} kg of sand thrown in under {Drawing:P0} of the full pull: flue soot {before:P0} -> {Model.FlueSoot:P0}");
        }

        /// <summary>Gives the firebox its cab ports, once the sim graph is built.</summary>
        public void AttachPorts(Port door, Port atomizer, Port heater, Port shopSteam = null, Port peepHole = null)
        {
            doorPort = door;
            atomizerPort = atomizer;
            heaterPort = heater;
            shopSteamPort = shopSteam;
            peepHolePort = peepHole;
        }

        /// <summary>The fire door's peep hole cover, 0 shut to 1 open; 0 without one.</summary>
        public float PeepHole => peepHolePort != null ? Mathf.Clamp01(peepHolePort.Value) : 0f;

        public override void InitializationAfterConnecting()
        {
            if (boilerBrokenState.IsConnected)
                ReferencedPort(boilerBrokenState).ValueUpdatedInternally += broken =>
                {
                    if (broken == 1f)
                        Model.Extinguish();
                };
        }

        public override void Tick(float delta)
        {
            // On a multiplayer client the host simulates; its ports carry the host's values.
            if (Net.IsClient)
                return;
            if (!resolved)
                Resolve();
            // Whether a stand's line reaches, for the lighting steps, looked for once a second.
            standCheck -= delta;
            if (standCheck <= 0f)
            {
                standCheck = 1f;
                standReaches = Shed.ShedConnections.StandReaches(Car);
            }
            if (ignitionExtIn.Value == 1f)
            {
                if (intakeWaterContent.Value <= 0.5f)
                    Model.Ignite();
                ignitionExtIn.Value = 0f;
            }
            else if (extinguishExtIn.Value == 1f)
            {
                Model.Extinguish();
                extinguishExtIn.Value = 0f;
            }

            var maxFlow = engine != null ? engine.maxFlowReadOut.Value : 0f;
            var exhaust = engine != null && maxFlow > 0f ? Mathf.Clamp01(engine.exhaustFlowReadOut.Value / maxFlow) : 0f;
            var drawn = Model.Step(delta, new OilFireInputs
            {
                FiringValve = Feed != null ? Feed.FiringValve : 0f,
                Atomizer = AtomizerValve,
                Heater = HeaterValve,
                AirFlow = airFlow.Value,
                DoorOpening = DoorOpening,
                Exhaust = exhaust,
                BoilerPressure = boilerPressure.Value,
                BoilerTemperature = boilerTemperature.Value,
                TenderFuel = Feed != null ? Feed.TenderFuel : 0f,
                IntakeWater = intakeWaterContent.Value,
                StartupMultiplier = StartupMultiplier(),
                SteamLinePressure = SteamLineOpening > 0f ? 1f + (SteamLinePressure - 1f) * SteamLineOpening : 0f,
            });
            // On the shed's line, the burner's steam is not the boiler's.
            Feed?.Draw(drawn, Model.OnSteamLine ? 0f : Model.AtomizerSteam + Model.HeaterSteamUsed, delta);
            if (Model.Flareback > 0f)
            {
                PendingFlareback = Mathf.Max(PendingFlareback, Model.Flareback);
                Main.Mod?.Logger.Log($"{Car?.ID}: oil fire flared back ({Model.Flareback:P0}): lit into gathered vapor and pooled oil");
            }
            if (drawn > 0f)
                coalEnvDamage.Value += drawn;

            fireOnReadOut.Value = Model.FireOn ? 1f : 0f;
            heatReadOut.Value = Model.Heat;
            // The stock smoke shows the oil's black smoke, a sanding's cloud, and a flareback's.
            FlarebackSmoke *= Mathf.Exp(-delta / FlarebackSmokeTime);
            if (Model.Flareback > 0f)
                FlarebackSmoke = Mathf.Max(FlarebackSmoke, Model.Flareback);
            // The cab's own smoke shades from white to black by this port, and
            // puffs thick black from 0.3 (the 2901's color readers): the
            // stack's darkness, not the stock's six times the unburned share.
            smokeDensityReadOut.Value = StackDarkness;
            combustionRateNormalizedReadOut.Value = Model.CombustionRateNormalized;
            temperatureReadOut.Value = Model.Temperature;
            oxygenAvailabilityReadOut.Value = Model.FireOn ? Mathf.Clamp01(Model.ExcessAir / OilFire.BestExcessAir) : 1f;
            // The firepan holds no fuel bed; oil pooled in it is what lies there.
            coalLevelReadOut.Value = Model.PooledOil;
            coalLevelNormalizedReadOut.Value = Model.PooledOil / Model.Parameters.MaxCoalCapacity;
            Fireman?.Step(delta);
        }

        /// <summary>The automatic fireman on this loco, once its cab is set up.</summary>
        internal FiremanRig Fireman { get; set; }

        /// <summary>The fireman's mode in the save, for a fireman not yet set up when it loaded.</summary>
        internal FiremanMode SavedFiremanMode { get; private set; }

        /// <summary>The atomizer's and oil heater's cab ports, for the fireman's hands.</summary>
        internal Port AtomizerPort => atomizerPort;
        internal Port HeaterPort => heaterPort;

        /// <summary>Readies the fire as the game's start-up does: lit, the brick hot, the oil warm.</summary>
        public void PrepareFire()
        {
            if (intakeWaterContent.Value <= 0.5f)
                Model.PrepareFire();
            fireOnReadOut.Value = Model.FireOn ? 1f : 0f;
        }

        // The stock steam-raising speedup, as the coal fire takes it.
        private float StartupMultiplier()
        {
            var pressure = Mathf.InverseLerp(startupMaxPressure, 1f, boilerPressure.Value);
            var standing = Mathf.InverseLerp(1f, 0f, Mathf.Abs(forwardSpeed.Value));
            var parameter = gameParams != null && gameParams.SteamStartupMultiplier > 0f ? gameParams.SteamStartupMultiplier : 1f;
            return Mathf.Lerp(1f, StartupMultiplierBase / parameter, pressure * standing);
        }

        private void Resolve()
        {
            resolved = true;
            var flow = Car != null && Car.SimController != null ? Car.SimController.SimulationFlow : null;
            engine = flow?.OrderedSimComps.OfType<ReciprocatingSteamEngine>().FirstOrDefault();
            exhaustSim = flow?.OrderedSimComps.OfType<SteamExhaust>().FirstOrDefault();
            // A loco spawned cold has cold oil and brick.
            if (!restored && !Model.FireOn && boilerPressure.Value < 2f)
                Model.Restore(false, Mathf.Max(OilFire.ColdBrick, boilerTemperature.Value), OilFire.AmbientTemperature, Model.FlueSoot);
            if (Main.Settings.LogFireboxCreation)
                Main.Mod?.Logger.Log($"{Car?.ID}: realistic oil burner '{id}': up to {Model.MaxFeed:F2} kg/s of oil (firebox kg), " +
                                     $"firing valve from {(Feed != null ? $"'{Feed.ControlName}'" : "nothing: no feed found")}, " +
                                     $"atomizer {(atomizerPort != null ? "from its cab valve" : "fixed")}, heater {(heaterPort != null ? "from its cab valve" : "off")}, " +
                                     $"fire door {(doorPort != null ? "found" : "not found")}");
        }

        public override JObject GetSaveStateData()
        {
            var data = new JObject();
            if (Model.FireOn)
                data.SetBool(RealisticFirebox.FireOnSaveKey, true);
            data.SetFloat(RealisticFirebox.CoalMassSaveKey, Model.PooledOil);
            var envDamage = coalEnvDamage.Value;
            if (envDamage > 0f)
                data.SetFloat(RealisticFirebox.CoalEnvDamageSaveKey, envDamage);
            data.SetFloat(BrickSaveKey, Model.BrickTemperature);
            data.SetFloat(OilTemperatureSaveKey, Model.OilTemperature);
            data.SetFloat(FlueSootSaveKey, Model.FlueSoot);
            if (Model.Vapor > 0f)
                data.SetFloat(VaporSaveKey, Model.Vapor);
            var mode = Fireman != null ? Fireman.Fireman.Mode : SavedFiremanMode;
            if (mode != FiremanMode.Off)
                data[RealisticFirebox.FiremanSaveKey] = (int)mode;
            return data;
        }

        public override void SetSaveStateData(JObject savedData)
        {
            var fireOn = savedData.GetBool(RealisticFirebox.FireOnSaveKey) == true;
            // The stock coal-mass key carries the pooled oil; with the vapor,
            // it keeps the flareback a reload would otherwise clear.
            Model.Restore(fireOn,
                savedData.GetFloat(BrickSaveKey) ?? (fireOn ? OilFire.HotBrick : OilFire.ColdBrick),
                savedData.GetFloat(OilTemperatureSaveKey) ?? OilFire.WarmOil,
                savedData.GetFloat(FlueSootSaveKey) ?? 0f,
                savedData.GetFloat(RealisticFirebox.CoalMassSaveKey) ?? 0f,
                savedData.GetFloat(VaporSaveKey) ?? 0f);
            var envDamage = savedData.GetFloat(RealisticFirebox.CoalEnvDamageSaveKey);
            if (envDamage.HasValue)
                coalEnvDamage.Value = envDamage.Value;
            var saved = savedData[RealisticFirebox.FiremanSaveKey]?.Type == JTokenType.Integer ? (int)savedData[RealisticFirebox.FiremanSaveKey] : 0;
            SavedFiremanMode = System.Enum.IsDefined(typeof(FiremanMode), saved) ? (FiremanMode)saved : FiremanMode.Off;
            Fireman?.SetMode(SavedFiremanMode);
            restored = true;
            fireOnReadOut.Value = Model.FireOn ? 1f : 0f;
        }
    }
}
