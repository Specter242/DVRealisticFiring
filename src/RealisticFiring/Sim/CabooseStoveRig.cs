using System;
using System.Collections.Generic;
using System.Linq;
using DV.CabControls;
using DV.Simulation.Cars;
using LocoSim.Implementations;
using Newtonsoft.Json.Linq;
using RealisticFiring.Core;
using RealisticFiring.Items;
using RealisticFiring.Visuals;
using UnityEngine;

namespace RealisticFiring.Sim
{
    /// <summary>
    /// Runs the caboose's wood stove on each caboose <see cref="TrainCar"/>:
    /// reads the caboose's stove door controls/ports, ticks
    /// <see cref="CabooseStoveModel"/> on the host or in single player, mirrors
    /// the host's snapshot on multiplayer guests, and owns the cab interior
    /// interaction/visuals (<see cref="CabooseStoveCab"/>) and roof stovepipe
    /// smoke (<see cref="CabooseStoveSmoke"/>).
    /// </summary>
    internal sealed class CabooseStoveRig : MonoBehaviour
    {
        private static readonly List<CabooseStoveRig> rigs = new List<CabooseStoveRig>();

        // Stock CarCabooseRed stove door external control port IDs.
        private static readonly string[] DoorPortIds =
        {
            "stoveDoor1.EXT_IN",
            "stoveDoor2.EXT_IN",
            "stoveDoor3.EXT_IN",
        };

        internal TrainCar Car { get; private set; }
        internal readonly CabooseStoveModel Model = new CabooseStoveModel();

        private readonly List<Port> doorPorts = new List<Port>();
        private bool portsScanned;
        private CabooseStoveCab cab;
        private CabooseStoveSmoke smoke;
        private bool wasEnabled;
        private bool wasMirrorActive;

        /// <summary>Whether the caboose stove feature is enabled in settings.</summary>
        internal bool FeatureEnabled => Main.Settings == null || Main.Settings.CabooseStove;

        /// <summary>Whether any stock sim door ports were found on this caboose.</summary>
        internal bool HasDoorPorts => doorPorts.Count > 0;

        /// <summary>Saved JSON preserved while <see cref="FeatureEnabled"/> is temporarily off.</summary>
        internal JObject PreservedSave { get; set; }

        /// <summary>Fallback toggle door state for custom cabooses without stock stove door levers.</summary>
        internal bool FallbackDoorOpen { get; set; }

        /// <summary>Whether this stove currently has active fire, fuel, warmth, or an open door worth mirroring.</summary>
        internal bool IsMirrorActive =>
            FeatureEnabled && (Model.HasActiveState || Model.DoorOpening > 0.01f || FallbackDoorOpen);

        /// <summary>
        /// True while the stove is active or needs a final reliable cold/closed
        /// transition snapshot after going out or shutting, avoiding idle traffic
        /// on cold cabooses.
        /// </summary>
        internal bool ShouldMirror
        {
            get
            {
                if (IsMirrorActive)
                {
                    wasMirrorActive = true;
                    return true;
                }
                return wasMirrorActive;
            }
        }

        /// <summary>
        /// True when the stove transitioned from active to cold and closed, so
        /// the final snapshot must be sent reliably before idle ticks stop.
        /// </summary>
        internal bool NeedsReliableMirror => wasMirrorActive && !IsMirrorActive;

        internal void MarkMirrorSent()
        {
            wasMirrorActive = IsMirrorActive;
        }

        /// <summary>Live stove rigs across all spawned cabooses.</summary>
        internal static IEnumerable<CabooseStoveRig> All
        {
            get
            {
                rigs.RemoveAll(r => r == null || r.Car == null);
                return rigs;
            }
        }

        internal static bool TryGet(TrainCar car, out CabooseStoveRig rig)
        {
            rig = null;
            if (car == null) return false;
            rig = car.GetComponent<CabooseStoveRig>();
            return rig != null;
        }

        internal static CabooseStoveRig Attach(TrainCar car)
        {
            if (car == null) return null;
            var existing = car.GetComponent<CabooseStoveRig>();
            if (existing != null) return existing;
            var rig = car.gameObject.AddComponent<CabooseStoveRig>();
            rig.Car = car;
            rig.Model.Clear(Ambient.Air);
            return rig;
        }

        private void Awake()
        {
            if (Car == null)
                Car = GetComponent<TrainCar>();
        }

        private void OnEnable()
        {
            if (!rigs.Contains(this))
                rigs.Add(this);
        }

        private void OnDisable()
        {
            rigs.Remove(this);
        }

        private void OnDestroy()
        {
            rigs.Remove(this);
            TearDownVisuals();
        }

        private void Update()
        {
            if (Car == null) return;
            var enabledNow = FeatureEnabled;
            if (!enabledNow)
            {
                if (wasEnabled)
                {
                    wasEnabled = false;
                    TearDownVisuals();
                }
                return;
            }
            if (!wasEnabled)
            {
                wasEnabled = true;
                PreservedSave = null;
                EnsureVisuals();
            }

            EnsureDoorPorts();
            var doorOpening = ReadDoorOpening();
            if (!Net.IsClient)
            {
                Model.SetDoorOpening(doorOpening);
                var drowned = LevelInfo.IsUnderWater(transform.position + Vector3.up * 1.4f);
                Model.Step(Time.deltaTime, Ambient.Air, drowned);
            }

            cab?.Tick(this);
            smoke?.Tick(this);
        }

        private void EnsureVisuals()
        {
            if (cab == null)
            {
                cab = gameObject.GetComponent<CabooseStoveCab>();
                if (cab == null) cab = gameObject.AddComponent<CabooseStoveCab>();
                cab.Bind(this);
            }
            if (smoke == null)
            {
                smoke = gameObject.GetComponent<CabooseStoveSmoke>();
                if (smoke == null) smoke = gameObject.AddComponent<CabooseStoveSmoke>();
                smoke.Bind(this);
            }
        }

        private void TearDownVisuals()
        {
            if (cab != null)
            {
                cab.Clear();
                Destroy(cab);
                cab = null;
            }
            if (smoke != null)
            {
                smoke.Clear();
                Destroy(smoke);
                smoke = null;
            }
        }

        private void EnsureDoorPorts()
        {
            if (portsScanned) return;
            var flow = Car.SimController != null ? Car.SimController.SimulationFlow : null;
            if (flow == null) return;
            portsScanned = true;
            doorPorts.Clear();
            RegisterDiscoveredDoorPorts(DoorPortIds);
        }

        internal void RegisterDiscoveredDoorPorts(IEnumerable<string> portIds)
        {
            if (portIds == null || Car == null) return;
            var flow = Car.SimController != null ? Car.SimController.SimulationFlow : null;
            if (flow == null) return;
            foreach (var id in portIds)
            {
                if (string.IsNullOrEmpty(id)) continue;
                if (flow.TryGetPort(id, out var port, canBeNullOrEmpty: true) && port != null && !doorPorts.Contains(port))
                    doorPorts.Add(port);
            }
        }

        /// <summary>Toggles a custom caboose's fallback stove door from the local player (routing to host on MP clients).</summary>
        internal void ToggleFallbackDoorFromPlayer()
        {
            if (!FeatureEnabled)
                return;
            if (Net.IsClient)
            {
                Net.TrySend(Car, FireAction.StoveDoorToggle);
                return;
            }
            if (ToggleFallbackDoorFromHost())
                FireController.Show(FallbackDoorOpen ? "Caboose stove door opened" : "Caboose stove door shut", 1.5f);
        }

        /// <summary>Authoritative host/SP toggle for a custom caboose's fallback stove door.</summary>
        internal bool ToggleFallbackDoorFromHost()
        {
            if (!FeatureEnabled || HasDoorPorts || (cab != null && cab.HasStockDoors))
                return false;
            FallbackDoorOpen = !FallbackDoorOpen;
            return true;
        }

        /// <summary>
        /// How far a stock stove door stands open, 0 to 1, from its lever or
        /// port. The stock caboose's levers read 1 shut and 0 open (the door
        /// swung out at 0 in game, job 3112), and a new caboose spawns at 1.
        /// </summary>
        internal static float StockDoorOpening(float value) => 1f - Mathf.Clamp01(value);

        private float ReadDoorOpening()
        {
            var max = 0f;
            for (var i = 0; i < doorPorts.Count; i++)
            {
                var p = doorPorts[i];
                if (p != null)
                    max = Mathf.Max(max, StockDoorOpening(p.Value));
            }
            if (cab != null)
                max = Mathf.Max(max, cab.ReadDoorControlOpening());
            if (doorPorts.Count == 0 && (cab == null || !cab.HasStockDoors))
                max = Mathf.Max(max, FallbackDoorOpen ? 1f : 0f);
            return max;
        }

        /// <summary>Whether the stove can accept <paramref name="kind"/> right now.</summary>
        internal bool CanTake(StartingFuelKind kind) => RefusalFor(kind) == null;

        /// <summary>Why the stove refuses <paramref name="kind"/> right now, or null if accepted.</summary>
        internal string RefusalFor(StartingFuelKind kind)
        {
            if (!FeatureEnabled)
                return "The caboose stove is turned off in settings";
            if (kind == StartingFuelKind.Wood)
                return Model.CanAddWood(CabooseStoveModel.PieceMass) ? null : "The stove is full";
            if (kind == StartingFuelKind.Rag)
                return Model.RagCount < 4 ? null : "The stove already has enough rags";
            return "That does not go in the stove";
        }

        /// <summary>
        /// Delivers firewood or a rag into the stove from the local player
        /// (routing to the host on a multiplayer client).
        /// </summary>
        internal bool DeliverFromPlayer(StartingFuelKind kind, float oil, bool lit)
        {
            if (!CanTake(kind))
                return false;
            if (Net.IsClient)
            {
                var action = kind == StartingFuelKind.Wood ? FireAction.StoveWood : FireAction.StoveRag;
                if (!Net.TrySend(Car, action, 0, lit ? 1 : 0, oil))
                    return false;
            }
            else if (!TakeFuel(kind, oil, lit))
            {
                return false;
            }
            if (kind == StartingFuelKind.Rag)
                TellRag(oil, lit);
            return true;
        }

        /// <summary>Authoritative host/SP fuel addition.</summary>
        internal bool TakeFuel(StartingFuelKind kind, float oil, bool lit)
        {
            if (!FeatureEnabled)
                return false;
            if (kind == StartingFuelKind.Wood)
                return Model.AddWood(CabooseStoveModel.PieceMass) > 0f;
            if (kind == StartingFuelKind.Rag)
                return Model.AddRag(Mathf.Clamp(oil, 0f, RagFuel.OilCapacity), lit);
            return false;
        }

        /// <summary>Attempts to ignite the stove with a lighter from the local player (or routes to host in MP).</summary>
        internal bool TryIgniteWithLighter()
        {
            if (!FeatureEnabled || !Model.DoorOpen || Model.Lit)
                return false;
            var canLight = Model.RagOil > 0f || (LighterRule.LightsCoal && Model.WoodKg > 0f);
            if (!canLight)
                return false;
            if (Net.IsClient)
                return Net.TrySend(Car, FireAction.StoveIgnite);
            return TryIgniteFromHost();
        }

        /// <summary>Authoritative host/SP lighter ignition.</summary>
        internal bool TryIgniteFromHost()
        {
            if (!FeatureEnabled || Model.Lit)
                return false;
            return Model.TryIgniteWithLighter(LighterRule.LightsCoal);
        }

        private void TellRag(float oil, bool lit)
        {
            if (oil <= 0f)
                FireController.Announce("A dry rag only burns away; oil it and light it first.");
            else if (!lit)
                FireController.Announce("That oiled rag isn't lit yet: touch your lighter to it in the stove, or lay a burning rag.");
            else if (Model.WoodKg < CabooseStoveModel.PieceMass * 0.5f)
                FireController.Announce("The rag has no firewood to light yet: lay wood in the stove while it burns.");
        }
    }

    /// <summary>
    /// JSON persistence for a caboose's stove inside the car's stock save
    /// object (<see cref="DV.Simulation.Cars.SimCarStateSave"/> on the stock
    /// caboose, <see cref="CarStateSave"/> where a car has that instead) under
    /// <see cref="SaveKey"/>. Older saves without the key stay cold; removing
    /// the mod leaves stock loading unaffected because both ignore unknown keys.
    /// </summary>
    internal static class CabooseStoveSave
    {
        internal const string SaveKey = "rfCabooseStove";
        internal const int CurrentVersion = 1;

        internal static JObject WriteForSave(CabooseStoveRig rig)
        {
            if (rig == null)
                return null;
            if (!rig.FeatureEnabled && rig.PreservedSave != null)
                return (JObject)rig.PreservedSave.DeepClone();
            return Write(rig.Model, rig.FallbackDoorOpen);
        }

        internal static bool LoadFromSave(JObject saved, CabooseStoveRig rig, float ambient = CabooseStoveModel.DefaultAmbientC)
        {
            if (rig == null)
                return false;
            if (saved == null)
            {
                rig.PreservedSave = null;
                rig.FallbackDoorOpen = false;
                rig.Model.Clear(ambient);
                return false;
            }
            var ok = Read(saved, rig.Model, ambient, out var fallbackDoorOpen);
            rig.FallbackDoorOpen = ok && fallbackDoorOpen;
            rig.PreservedSave = rig.FeatureEnabled ? null : (JObject)saved.DeepClone();
            return ok;
        }

        internal static JObject Write(CabooseStoveModel model, bool fallbackDoorOpen = false)
        {
            if (model == null || (!model.HasActiveState && !fallbackDoorOpen))
                return null;
            var snap = model.Snapshot();
            var obj = new JObject
            {
                ["v"] = CurrentVersion,
                ["wood"] = snap.Wood,
                ["char"] = snap.Char,
                ["frontEnergy"] = snap.FrontEnergy,
                ["frontTime"] = snap.FrontTime,
                ["ragOil"] = snap.RagOil,
                ["ragCount"] = snap.RagCount,
                ["ragLit"] = snap.RagLit,
                ["glowing"] = snap.Glowing,
                ["involved"] = snap.Involved,
                ["surfaceChar"] = snap.SurfaceChar,
                ["heat"] = snap.Heat01,
                ["smoke"] = snap.Smoke01,
                ["warmth"] = snap.WarmthC,
                ["lighterPilot"] = snap.LighterPilot,
                ["doorOpening"] = snap.DoorOpening,
            };
            if (fallbackDoorOpen)
                obj["fallbackDoorOpen"] = true;
            return obj;
        }

        internal static bool Read(JObject saved, CabooseStoveModel model, float ambient = CabooseStoveModel.DefaultAmbientC) =>
            Read(saved, model, ambient, out _);

        internal static bool Read(JObject saved, CabooseStoveModel model, float ambient, out bool fallbackDoorOpen)
        {
            fallbackDoorOpen = false;
            if (model == null)
                return false;
            if (saved == null)
            {
                model.Clear(ambient);
                return false;
            }
            if (!TryReadInt(saved, "v", out var version) || version != CurrentVersion)
            {
                model.Clear(ambient);
                return false;
            }
            if (!TryReadFloat(saved, "wood", out var wood)
                || !TryReadFloat(saved, "char", out var charKg)
                || !TryReadFloat(saved, "frontEnergy", out var frontEnergy)
                || !TryReadFloat(saved, "frontTime", out var frontTime)
                || !TryReadFloat(saved, "ragOil", out var ragOil)
                || !TryReadInt(saved, "ragCount", out var ragCount)
                || !TryReadBool(saved, "ragLit", out var ragLit)
                || !TryReadBool(saved, "glowing", out var glowing)
                || !TryReadFloat(saved, "involved", out var involved)
                || !TryReadFloat(saved, "surfaceChar", out var surfaceChar)
                || !TryReadFloat(saved, "heat", out var heat)
                || !TryReadFloat(saved, "smoke", out var smoke)
                || !TryReadFloat(saved, "warmth", out var warmth)
                || !TryReadFloat(saved, "lighterPilot", out var lighterPilot)
                || !TryReadFloat(saved, "doorOpening", out var doorOpening)
                || !TryReadOptionalBool(saved, "fallbackDoorOpen", out fallbackDoorOpen))
            {
                fallbackDoorOpen = false;
                model.Clear(ambient);
                return false;
            }

            var snap = new CabooseStoveSnapshot
            {
                Wood = wood,
                Char = charKg,
                FrontEnergy = frontEnergy,
                FrontTime = frontTime,
                RagOil = ragOil,
                RagCount = ragCount,
                RagLit = ragLit,
                Glowing = glowing,
                Involved = involved,
                SurfaceChar = surfaceChar,
                Heat01 = heat,
                Smoke01 = smoke,
                WarmthC = warmth,
                LighterPilot = lighterPilot,
                DoorOpening = doorOpening,
            };
            if (!model.Restore(snap, ambient))
            {
                fallbackDoorOpen = false;
                return false;
            }
            return true;
        }

        private static bool TryReadFloat(JObject obj, string key, out float value)
        {
            value = 0f;
            var token = obj[key];
            if (token == null || (token.Type != JTokenType.Float && token.Type != JTokenType.Integer))
                return false;
            try
            {
                var v = token.Value<float>();
                if (float.IsNaN(v) || float.IsInfinity(v))
                    return false;
                value = v;
                return true;
            }
            catch (Exception e) when (e is OverflowException || e is InvalidCastException || e is FormatException)
            {
                return false;
            }
        }

        private static bool TryReadInt(JObject obj, string key, out int value)
        {
            value = 0;
            var token = obj[key];
            if (token == null || token.Type != JTokenType.Integer)
                return false;
            try
            {
                var raw = token.Value<long>();
                if (raw < int.MinValue || raw > int.MaxValue)
                    return false;
                value = (int)raw;
                return true;
            }
            catch (Exception e) when (e is OverflowException || e is InvalidCastException || e is FormatException)
            {
                return false;
            }
        }

        private static bool TryReadBool(JObject obj, string key, out bool value)
        {
            value = false;
            var token = obj[key];
            if (token == null || token.Type != JTokenType.Boolean)
                return false;
            value = (bool)token;
            return true;
        }

        private static bool TryReadOptionalBool(JObject obj, string key, out bool value)
        {
            value = false;
            var token = obj[key];
            if (token == null)
                return true;
            if (token.Type != JTokenType.Boolean)
                return false;
            value = (bool)token;
            return true;
        }
    }
}
