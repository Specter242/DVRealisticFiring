using System;
using System.Linq;
using DV.JObjectExtstensions;
using HarmonyLib;
using LocoSim.Definitions;
using LocoSim.Implementations;
using Newtonsoft.Json.Linq;
using RealisticFiring.Core;
using RealisticFiring.Diagnostics;
using UnityEngine;

namespace RealisticFiring.Sim
{
    /// <summary>
    /// Drop-in replacement for the stock LocoSim Firebox, driving a
    /// <see cref="FireModel"/>. Keeps the definition's component ID, every
    /// stock port and port reference with its default value, the stock save
    /// keys, the add-and-reset coal delivery port, ignition and extinguish
    /// requests, the boiler-broken shutdown, and the environmental-damage
    /// meter, so the rest of the loco cannot tell the difference.
    /// </summary>
    internal sealed class RealisticFirebox : SimComponent
    {
        public const string FireOnSaveKey = "fireOn";
        public const string CoalMassSaveKey = "coalMass";
        public const string CoalEnvDamageSaveKey = "coalEnvDmg";
        public const string ZonesSaveKey = "rfZones";
        public const string AshpanSaveKey = "rfAshpan";

        private const float StartupMultiplierBase = 25f;

        // Clicking to throw ends the hover, and the throw animation takes
        // under a second before the coal arrives.
        internal const float AimHoldTime = 3f;

        // Below this the door counts as shut; the leak path stays open anyway.
        private const float DoorOpenThreshold = 0.05f;

        // A stoker feeds a little every tick, far less than any shovelful;
        // deliveries smaller than this are continuous feed, in kg.
        private const float ContinuousFeedPiece = 0.5f;

        private static readonly AccessTools.FieldRef<PortReference, Port> ReferencedPort =
            AccessTools.FieldRefAccess<PortReference, Port>("port");

        private readonly float startupMaxPressure;
        private readonly ShovelDelivery shovel;
        private readonly GrateLever grateLever = new GrateLever();

        private readonly PortReference coalDumpControl;
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
        private readonly Port coalDumpFlowNormalizedReadOut;
        private readonly Port smokeDensityReadOut;
        private readonly Port combustionRateNormalizedReadOut;
        private readonly Port temperatureReadOut;
        private readonly Port oxygenAvailabilityReadOut;

        private Port doorPort;
        private float coalDumpFlow;
        private float coalDumpFlowVelocity;
        private bool reportedBadStartupParameter;

        private bool doorOpen;
        private float doorOpenTime;
        private float doorMaxOpening;
        private float doorMaxAirFlow;
        private float heatAtDoorOpen;
        private float temperatureAtDoorOpen;

        public RealisticFirebox(FireboxDefinition fDef, TrainCar car)
            : base(fDef.ID)
        {
            Car = car;
            startupMaxPressure = fDef.startupMaxPressure;
            Model = new FireModel(new FireboxParameters(
                fDef.maxCoalCapacity,
                fDef.burnTime,
                fDef.efficiencyAtMaxCombustion,
                fDef.coalDumpRate,
                fDef.combustionRateSmoothTime,
                fDef.temperatureSmoothTime));
            shovel = new ShovelDelivery(Model.Parameters);
            if (car != null && car.carLivery != null && GrateSlopes.TryGetValue(car.carLivery.id, out var slope))
                Model.GrateSlope = slope;
            if (car != null && car.carLivery != null && GrateAreas.TryGetValue(car.carLivery.id, out var area))
                Model.GrateArea = area;

            // Same ports, IDs, and default values as the stock Firebox.
            intakeWaterContent = AddPortReference(fDef.intakeWaterContent);
            coalDumpControl = AddPortReference(fDef.coalDumpControl, 0.5f);
            AddPort(fDef.coalCapacityReadOut, fDef.maxCoalCapacity);
            coalLevelReadOut = AddPort(fDef.coalLevelReadOut);
            coalLevelNormalizedReadOut = AddPort(fDef.coalLevelNormalizedReadOut);
            coalControlExtIn = AddPort(fDef.coalControlExtIn);
            coalEnvDamage = AddPort(fDef.coalEnvDamage);
            ignitionExtIn = AddPort(fDef.ignitionExtIn);
            extinguishExtIn = AddPort(fDef.extinguishExtIn);
            airFlow = AddPortReference(fDef.airFlow);
            heatReadOut = AddPort(fDef.heatReadOut);
            coalDumpFlowNormalizedReadOut = AddPort(fDef.coalDumpFlowNormalizedReadOut);
            fireOnReadOut = AddPort(fDef.fireOnReadOut);
            smokeDensityReadOut = AddPort(fDef.smokeDensityReadOut);
            combustionRateNormalizedReadOut = AddPort(fDef.combustionRateNormalizedReadOut);
            temperatureReadOut = AddPort(fDef.temperatureReadOut, 100f);
            forwardSpeed = AddPortReference(fDef.forwardSpeed);
            boilerPressure = AddPortReference(fDef.boilerPressure);
            boilerTemperature = AddPortReference(fDef.boilerTemperature, 100f);
            boilerBrokenState = AddPortReference(fDef.boilerBrokenState);
            oxygenAvailabilityReadOut = AddPort(fDef.oxygenAvailabilityReadOut);

            coalControlExtIn.ValueUpdatedInternally += OnCoalDelivered;
        }

        public TrainCar Car { get; }

        /// <summary>
        /// Each livery's grate slope, in degrees, as measured from its coal
        /// pile when a cab first loads; later fireboxes of that livery start
        /// with it.
        /// </summary>
        public static readonly System.Collections.Generic.Dictionary<string, float> GrateSlopes =
            new System.Collections.Generic.Dictionary<string, float>();

        /// <summary>Records the grate's measured slope for this firebox and its livery.</summary>
        public void SetGrateSlope(float degrees)
        {
            Model.GrateSlope = degrees;
            if (Car != null && Car.carLivery != null)
                GrateSlopes[Car.carLivery.id] = degrees;
        }

        /// <summary>
        /// Each livery's grate area, in m², as measured from its coal pile when
        /// a cab first loads; later fireboxes of that livery start with it.
        /// </summary>
        public static readonly System.Collections.Generic.Dictionary<string, float> GrateAreas =
            new System.Collections.Generic.Dictionary<string, float>();

        /// <summary>
        /// Each livery's grate, as a box in the car's frame (from the grate up
        /// to the coal pile's top), measured when a cab first loads.
        /// </summary>
        public static readonly System.Collections.Generic.Dictionary<string, Bounds> GrateBoxes =
            new System.Collections.Generic.Dictionary<string, Bounds>();

        /// <summary>This loco's grate in its car frame, once a cab of its livery has loaded.</summary>
        public Bounds? GrateBox =>
            Car != null && Car.carLivery != null && GrateBoxes.TryGetValue(Car.carLivery.id, out var box) ? box : (Bounds?)null;

        /// <summary>Records where the grate lies in the car, for its livery.</summary>
        public void SetGrateBox(Bounds carFrame)
        {
            if (Car != null && Car.carLivery != null)
                GrateBoxes[Car.carLivery.id] = carFrame;
        }

        /// <summary>Records the grate's measured area for this firebox and its livery.</summary>
        public void SetGrateArea(float squareMetres)
        {
            Model.GrateArea = squareMetres;
            if (Car != null && Car.carLivery != null)
                GrateAreas[Car.carLivery.id] = squareMetres;
        }

        public FireModel Model { get; }

        public float AirFlowIn => airFlow.Value;

        public float DoorOpening => doorPort != null ? Mathf.Clamp01(doorPort.Value) : 0f;

        public bool HasDoorPort => doorPort != null;

        public float StartupMultiplier { get; private set; } = 1f;

        /// <summary>Coal the last delivery put on the grate, in kg; 0 when none since the fire was laid.</summary>
        public float LastDeliveryMass { get; private set; }

        /// <summary>Where the last delivery was aimed; null when it followed the fire.</summary>
        public DoorAimSample? LastDeliveryAim { get; private set; }

        /// <summary>Where a shovel last pointed in this firebox's door.</summary>
        public DoorAimSample? HoverAim { get; private set; }

        private float continuousFeed;

        public override bool HasSaveData => true;

        /// <summary>
        /// Connects the firebox door port that FireboxSimController names, so
        /// door opening can feed air above the fire. Unconnected means closed.
        /// </summary>
        public void AttachDoorPort(Port port) => doorPort = port;

        /// <summary>
        /// The drop grate's cab lever, a saved control the mod adds to each
        /// supported loco: 0 shut, 1 open. Open, whatever lies on the drop
        /// grate's zones falls into the ashpan.
        /// </summary>
        public static readonly StokerJetControls.Valve DropGrateControl = new StokerJetControls.Valve("rfDropGrate", "Drop grate", null, 0f);

        public void AttachDropGratePort(Port port) => dropGratePort = port;

        /// <summary>
        /// The ashpan's hopper lever, outside the loco, a control the mod adds
        /// to each supported loco: held open (1), the ashes fall out; it is not
        /// saved, as it springs shut when let go.
        /// </summary>
        public static readonly StokerJetControls.Valve AshpanControl = new StokerJetControls.Valve("rfAshpan", "Ashpan hopper", null, 0f);

        public void AttachAshpanPort(Port port) => ashpanPort = port;

        private Port ashpanPort;
        private bool ashpanWasOpen;
        private float ashpanEmptiedBefore;

        private Port dropGratePort;
        private bool dropGrateWasOpen;
        private float dropGrateCoalBefore;
        private float dropGrateAshBefore;

        public void ShakeGrate() => Model.ShakeGrate();

        /// <summary>Records where the player points a shovel in the door.</summary>
        public void NoteAim(DoorAimSample aim) => HoverAim = aim;

        /// <summary>Runs the slice bar under <paramref name="zone"/>'s fire, as with the fire iron.</summary>
        public void Slice(GrateZone zone)
        {
            var before = Model.Zones[(int)zone].Clinker;
            Model.Slice(zone);
            if (Main.Settings.LogCoalDeliveries)
                Main.Mod.Logger.Log($"{Car?.ID}: sliced {zone}, clinker {before:F2} -> {Model.Zones[(int)zone].Clinker:F2} kg, " +
                                    $"stirred {Model.Zones[(int)zone].Stirred:P0}");
        }

        /// <summary>Works <paramref name="zone"/> with the fire iron as a hoe toward <paramref name="direction"/>.</summary>
        public void Push(GrateZone zone, GrateDirection direction)
        {
            var before = Model.Zones[(int)zone].Coal;
            var hookedBefore = Model.HookedOut;
            Model.Push(zone, direction);
            if (Main.Settings.LogCoalDeliveries)
                Main.Mod.Logger.Log($"{Car?.ID}: worked {zone} {direction.ToString().ToLowerInvariant()}, coal there {before:F1} -> {Model.Zones[(int)zone].Coal:F1} kg" +
                                    (Model.HookedOut > hookedBefore ? $", {Model.HookedOut - hookedBefore:F2} kg of ash and clinker hooked out" : ""));
        }

        /// <summary>
        /// Levels the fire around <paramref name="zone"/>, as raked with an
        /// empty shovel through the door.
        /// </summary>
        public void Rake(GrateZone zone)
        {
            var before = Model.Zones[(int)zone].Coal;
            Model.Rake(zone);
            if (Main.Settings.LogCoalDeliveries)
                Main.Mod.Logger.Log($"{Car?.ID}: raked {zone}, {before:F1} -> {Model.Zones[(int)zone].Coal:F1} kg there");
        }

        /// <summary>
        /// Readies the fire as the game's loco start-up does: where stock fills
        /// the firebox and lights it, this lays a clean bed burning evenly
        /// across the grate and filling the firebox, as shed staff would have
        /// built it, rather than a box of green coal that smothers the fire.
        /// A flooded firebox gets the coal but stays out, as ignition does.
        /// </summary>
        public void PrepareFire()
        {
            var lit = intakeWaterContent.Value <= 0.5f;
            Model.Restore(lit, Model.Parameters.MaxCoalCapacity);
            fireOnReadOut.Value = Model.FireOn ? 1f : 0f;
            LastDeliveryMass = 0f;
            LastDeliveryAim = null;
            UpdateCoalLevel();
            if (Main.Settings.LogCoalDeliveries)
                Main.Mod.Logger.Log($"{Car?.ID}: start-up laid a {(lit ? "burning" : "flooded, unlit")} fire of {Model.CoalMass:F1} kg");
        }

        public override void InitializationAfterConnecting()
        {
            // The stock firebox subscribes to the port behind the reference;
            // that field is internal to DV.Simulation.
            if (boilerBrokenState.IsConnected)
                ReferencedPort(boilerBrokenState).ValueUpdatedInternally += OnBoilerBrokenStateChanged;
        }

        private void OnBoilerBrokenStateChanged(float brokenState)
        {
            if (brokenState == 1f)
            {
                Model.Extinguish();
                fireOnReadOut.Value = 0f;
            }
        }

        private void OnCoalDelivered(float coalMassDelta)
        {
            if (coalMassDelta == 0f)
                return;

            if (coalMassDelta > 0f && coalMassDelta < ContinuousFeedPiece)
            {
                // A stoker's trickle: it goes where the fire needs it, leaves
                // the player's shovel aim alone, and is logged as a total.
                continuousFeed += shovel.Deliver(Model, coalMassDelta, null);
            }
            else if (coalMassDelta > 0f)
            {
                var aim = TakeFreshAim();
                var accepted = shovel.Deliver(Model, coalMassDelta, aim?.Zone);
                LastDeliveryMass = accepted;
                LastDeliveryAim = aim;
                if (Main.Settings.LogCoalDeliveries)
                {
                    Main.Mod.Logger.Log($"{Car?.ID}: {coalMassDelta:F2} kg delivered " +
                                        $"({(aim.HasValue ? "aimed at " + aim.Value : "following the fire")}), {accepted:F2} kg accepted, " +
                                        $"door {DoorOpening:P0}, door air {Model.DoorAirFlow:F2} kg/s");
                }
            }
            else
            {
                Model.RemoveCoal(-coalMassDelta);
            }

            UpdateCoalLevel();
            coalControlExtIn.Value = 0f;
        }

        /// <summary>The last tick's stoker reading, if a stoker feeds this firebox.</summary>
        public StokerFlow? LastStoker { get; private set; }

        /// <summary>Stoker engine speed at the last tick, as a share of full speed.</summary>
        public float StokerEngineSpeed { get; private set; }

        /// <summary>The stoker feeding this firebox, once it has run.</summary>
        public Stoker StokerModel { get; private set; }

        /// <summary>
        /// Runs <paramref name="stoker"/> for a tick, its jets throwing coal
        /// straight onto this firebox's zones.
        /// </summary>
        internal StokerFlow Stoke(Stoker stoker, StokerInputs inputs, float dt)
        {
            inputs.AirFlow = AirFlowIn;
            var flow = stoker.Step(dt, inputs, Model);
            continuousFeed += flow.Fed;
            LastStoker = flow;
            StokerModel = stoker;
            StokerEngineSpeed = stoker.EngineSpeed;
            UpdateCoalLevel();
            return flow;
        }

        /// <summary>Coal fed continuously (by a stoker) since the last call, in kg.</summary>
        public float TakeContinuousFeed()
        {
            var fed = continuousFeed;
            continuousFeed = 0f;
            return fed;
        }

        // Coal from anything but a shovel the player just pointed into this
        // door (the keyboard shovel, a stoker, another mod) goes where the fire needs it.
        private DoorAimSample? TakeFreshAim()
        {
            var aim = HoverAim;
            HoverAim = null;
            return aim.HasValue && !aim.Value.Raking && Time.time - aim.Value.Time <= AimHoldTime ? aim : null;
        }

        public override void Tick(float delta)
        {
            var start = PerfMeter.Start();
            Step(delta);
            PerfMeter.Stop(PerfSection.Firebox, start);
        }

        private void Step(float delta)
        {
            CheckIgnition();
            CoalDump(delta);
            DropGrate(delta);
            AshpanHopper(delta);

            StartupMultiplier = ComputeStartupMultiplier();
            var consumed = Model.Step(delta, new FireInputs
            {
                AirFlow = airFlow.Value,
                IntakeWater = intakeWaterContent.Value,
                DoorOpening = DoorOpening,
                StartupMultiplier = StartupMultiplier,
                BoilerTemperature = boilerTemperature.Value,
                Speed = Mathf.Abs(forwardSpeed.Value),
            });
            if (consumed > 0f)
                coalEnvDamage.Value += consumed;

            fireOnReadOut.Value = Model.FireOn ? 1f : 0f;
            heatReadOut.Value = Model.Heat;
            smokeDensityReadOut.Value = Model.SmokeDensity;
            combustionRateNormalizedReadOut.Value = Model.CombustionRateNormalized;
            temperatureReadOut.Value = Model.Temperature;
            oxygenAvailabilityReadOut.Value = Model.OxygenAvailability;
            UpdateCoalLevel();
            TrackDoor(delta);
        }

        // Logs each door opening with the fire as it was, and each closing with
        // how long and how far it was open and what it did to heat and temperature.
        private void TrackDoor(float delta)
        {
            var opening = DoorOpening;
            var open = opening > DoorOpenThreshold;
            if (open)
            {
                doorOpenTime += delta;
                doorMaxOpening = Mathf.Max(doorMaxOpening, opening);
                doorMaxAirFlow = Mathf.Max(doorMaxAirFlow, Model.DoorAirFlow);
            }
            if (open == doorOpen)
                return;

            doorOpen = open;
            if (open)
            {
                doorOpenTime = 0f;
                doorMaxOpening = opening;
                doorMaxAirFlow = Model.DoorAirFlow;
                heatAtDoorOpen = Model.Heat;
                temperatureAtDoorOpen = Model.Temperature;
                if (Main.Settings.LogCoalDeliveries)
                {
                    Main.Mod.Logger.Log($"{Car?.ID}: door opening: heat {Model.Heat / 1e6f:F2} MW, " +
                                        $"temp {Model.Temperature:F0} C, smoke {Model.SmokeDensity:P0}, " +
                                        $"draft {AirFlowIn:F2} kg/s (grate {Model.PrimaryAirFlow:F2}, door {Model.DoorAirFlow:F2})");
                }
            }
            else if (Main.Settings.LogCoalDeliveries)
            {
                Main.Mod.Logger.Log($"{Car?.ID}: door closed after {doorOpenTime:F1} s, up to {doorMaxOpening:P0} open " +
                                    $"and {doorMaxAirFlow:F2} kg/s of door air: heat {heatAtDoorOpen / 1e6f:F2} -> {Model.Heat / 1e6f:F2} MW, " +
                                    $"temp {temperatureAtDoorOpen:F0} -> {Model.Temperature:F0} C, smoke {Model.SmokeDensity:P0}, " +
                                    $"smoke burnt {Model.OverFireBurnout:P0}");
            }
        }

        private void CheckIgnition()
        {
            if (ignitionExtIn.Value == 1f)
            {
                if (intakeWaterContent.Value <= 0.5f)
                {
                    Model.Ignite();
                    fireOnReadOut.Value = 1f;
                }
                ignitionExtIn.Value = 0f;
            }
            else if (extinguishExtIn.Value == 1f)
            {
                Model.Extinguish();
                fireOnReadOut.Value = 0f;
                extinguishExtIn.Value = 0f;
            }
        }

        // The dump lever doubles as the rocking grate lever: a short rock wiggles
        // the grate, a half stroke shakes it, holding it over dumps at the stock rate. The dump keeps
        // running while ash or clinker is left, since dumping is how a
        // clinkered fire gets cleaned.
        private void CoalDump(float delta)
        {
            var lever = grateLever.Update(coalDumpControl.Value, delta);
            for (var i = 0; i < lever.Wiggles + lever.Shakes; i++)
                Shake(i < lever.Wiggles ? ShakeStroke.Wiggle : ShakeStroke.Shake);
            var target = Model.HasDumpableContents ? lever.Dump : 0f;
            coalDumpFlow = Mathf.SmoothDamp(coalDumpFlow, target, ref coalDumpFlowVelocity, 0.5f, float.PositiveInfinity, delta);
            if (target == 0f && coalDumpFlow < 0.01f)
            {
                coalDumpFlow = 0f;
                coalDumpFlowVelocity = 0f;
            }
            coalDumpFlowNormalizedReadOut.Value = coalDumpFlow;
            if (coalDumpFlow > 0f)
                Model.Dump(coalDumpFlow, delta);
        }

        // The drop grate: whatever lies on it falls while the lever stands open.
        // Logs what went each time it is shut again.
        private void DropGrate(float delta)
        {
            var opening = dropGratePort != null ? Mathf.Clamp01(dropGratePort.Value) : 0f;
            var open = opening > 0.05f;
            if (open && !dropGrateWasOpen)
            {
                dropGrateCoalBefore = Model.CoalDumped;
                dropGrateAshBefore = Model.DroppedAshAndClinker;
            }
            else if (!open && dropGrateWasOpen && Main.Settings.LogCoalDeliveries)
            {
                Main.Mod.Logger.Log($"{Car?.ID}: drop grate shut; {Model.DroppedAshAndClinker - dropGrateAshBefore:F1} kg of ash and clinker " +
                                    $"and {Model.CoalDumped - dropGrateCoalBefore:F1} kg of fire went through; clinker left {Model.ClinkerMass:F1} kg");
            }
            dropGrateWasOpen = open;
            Model.OpenDropGrate(open ? opening : 0f, delta);
        }

        // The ashpan's hopper doors: the ashes fall out while the lever is held.
        // Logs what came out each time it is let go.
        private void AshpanHopper(float delta)
        {
            var opening = ashpanPort != null ? Mathf.Clamp01(ashpanPort.Value) : 0f;
            var open = opening > 0.05f;
            if (open && !ashpanWasOpen)
                ashpanEmptiedBefore = Model.AshpanEmptied;
            else if (!open && ashpanWasOpen && Main.Settings.LogCoalDeliveries)
                Main.Mod.Logger.Log($"{Car?.ID}: ashpan hopper shut; {Model.AshpanEmptied - ashpanEmptiedBefore:F0} kg of ashes let out, " +
                                    $"{Model.Ashpan:F0} kg left ({Model.AshpanFill:P0})");
            ashpanWasOpen = open;
            Model.OpenAshpan(open ? opening : 0f, delta);
        }

        private void Shake(ShakeStroke stroke)
        {
            var ashBefore = Model.AshMass;
            var clinkerBefore = Model.ClinkerMass;
            var droppedBefore = Model.CoalDroppedThroughGrate;
            var cindersBefore = Model.ShakeCinders;
            var drawing = Model.Drawing;
            Model.ShakeGrate(stroke);
            if (Main.Settings.LogCoalDeliveries)
                Main.Mod.Logger.Log($"{Car?.ID}: grate {(stroke == ShakeStroke.Shake ? "shaken" : "wiggled")} at {drawing:P0} draft, " +
                                    $"ash {ashBefore:F2} -> {Model.AshMass:F2} kg, clinker {clinkerBefore:F2} -> {Model.ClinkerMass:F2} kg, " +
                                    $"{Model.CoalDroppedThroughGrate - droppedBefore:F2} kg of fire into the ashpan, " +
                                    $"{Model.ShakeCinders - cindersBefore:F2} kg up the stack");
        }

        // Stock fast startup: up to 25× faster combustion while the boiler is
        // below startupMaxPressure and the loco stands still.
        private float ComputeStartupMultiplier()
        {
            var pressure = Mathf.InverseLerp(startupMaxPressure, 1f, boilerPressure.Value);
            var standing = Mathf.InverseLerp(1f, 0f, Mathf.Abs(forwardSpeed.Value));
            var parameter = gameParams != null ? gameParams.SteamStartupMultiplier : 1f;
            if (parameter <= 0f)
            {
                if (!reportedBadStartupParameter)
                {
                    Main.Mod.Logger.Error($"Unexpected SteamStartupMultiplier {parameter}; using 1");
                    reportedBadStartupParameter = true;
                }
                parameter = 1f;
            }
            return Mathf.Lerp(1f, StartupMultiplierBase / parameter, pressure * standing);
        }

        private void UpdateCoalLevel()
        {
            var coal = Model.CoalMass;
            coalLevelReadOut.Value = coal;
            coalLevelNormalizedReadOut.Value = coal / Model.Parameters.MaxCoalCapacity;
        }

        public override JObject GetSaveStateData()
        {
            var data = new JObject();
            if (Model.FireOn)
                data.SetBool(FireOnSaveKey, true);
            data.SetFloat(CoalMassSaveKey, Model.CoalMass);
            var envDamage = coalEnvDamage.Value;
            if (envDamage > 0f)
                data.SetFloat(CoalEnvDamageSaveKey, envDamage);
            data[ZonesSaveKey] = new JArray(Model.SaveZones());
            if (Model.Ashpan > 0f)
                data.SetFloat(AshpanSaveKey, Model.Ashpan);
            return data;
        }

        public override void SetSaveStateData(JObject savedData)
        {
            var fireOn = savedData.GetBool(FireOnSaveKey) == true;
            var coalMass = savedData.GetFloat(CoalMassSaveKey) ?? 0f;

            // Zones written by this mod win when they agree with the stock
            // total; a stock save, or one another tool edited, is spread evenly.
            var zones = ReadZones(savedData);
            var restored = zones != null
                           && Math.Abs(FireModel.SavedCoalMass(zones) - coalMass) < 0.5f
                           && Model.RestoreZones(fireOn, zones);
            if (!restored)
                Model.Restore(fireOn, coalMass);
            Model.RestoreAshpan(savedData.GetFloat(AshpanSaveKey) ?? 0f);

            var envDamage = savedData.GetFloat(CoalEnvDamageSaveKey);
            if (envDamage.HasValue)
                coalEnvDamage.Value = envDamage.Value;

            fireOnReadOut.Value = Model.FireOn ? 1f : 0f;
            UpdateCoalLevel();
        }

        private static float[] ReadZones(JObject savedData)
        {
            try
            {
                return (savedData[ZonesSaveKey] as JArray)?.Select(token => (float)token).ToArray();
            }
            catch (Exception e) when (e is ArgumentException || e is InvalidCastException || e is FormatException)
            {
                Main.Mod.Logger.Warning($"Ignoring unreadable '{ZonesSaveKey}' save data: {e.Message}");
                return null;
            }
        }
    }
}
