using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using DV;
using DV.Utils;
using HarmonyLib;
using LocoSim.Implementations;
using Newtonsoft.Json;
using RealisticFiring.Core;
using RealisticFiring.Sim;
using RealisticStackEffects;
using UnityEngine;

namespace RealisticFiring.Visuals
{
 internal sealed class FireboxCapture : MonoBehaviour
    {
        private const string RequestFile = "firebox-capture-request.json";
        private const string StopFile = "capture-stop";
        private const float PollEvery = 1f;

        // Frames the camera is held at a view before its shot, so the
        // antialiasing's history is the view's own.
        private const int HoldFrames = 12;

        public static bool Running { get; private set; }
        internal static bool ShowOverlay { get; private set; }
        internal static bool KeepsVisible => Running && !respectVisibility;
        private static bool respectVisibility;

        internal sealed class State
        {
            public string Name;
            public float? Door;
            public FireboxQuality? Quality;

            /// <summary>The flames' draft, 0 to 1, on the test bed; the fire's own when not given.</summary>
            public float? Draft;

            /// <summary>Exhaust beats a second laid over the flames; none when 0.</summary>
            public float Beats;

            public float BeatStrength = 1f;

            /// <summary>The cab's blower, 0 to 1, for the state; as it stands when not given.</summary>
            public float? Blower;
            public float? Stoker;
            public float? StokerMaster;

            /// <summary>The air through the grate, kg/s, on the test bed; the engine's draft when not given.</summary>
            public float? Air;

            /// <summary>Coal laid evenly over the grate as the state starts, kg, before any shovelfuls.</summary>
            public float Bed;

            /// <summary>Coal laid evenly over the grate already burning, kg, in place of the fire there was.</summary>
            public float Burning;

            /// <summary>The fire put out as the state starts, its coal left cold (a cold box lit through its door).</summary>
            public bool Out;

            /// <summary>Shovelfuls landed as the state starts, each "Zone:kg" (a <see cref="GrateZone"/> name).</summary>
            public string[] Shovelfuls = new string[0];

            /// <summary>Seconds the state runs before its shots.</summary>
            public float Settle = 6f;

            /// <summary>Lights the fire first if it is out (with coal on the grate).</summary>
            public bool Light;

            /// <summary>The box's surfaces as drawn (0), their albedo alone (1), or the fire's light on white (2).</summary>
            public int LightView;

            /// <summary>The box's light (<see cref="FireboxLighting"/>) for the state, each as it stands when not given.</summary>
            public float? WallLight;
            public float? CoalLight;
            public float? BedLight;
            public float? FlameLight;

            /// <summary>The volume as drawn (0), its flames alone (1), or its smoke alone (2).</summary>
            public int VolumeView;

            /// <summary>The look set by name for the state, as FireboxTestBed.Tune takes it ("Glow=2.5;WallLight=0.1").</summary>
            public string Tuning;

            /// <summary>The box's smoke and the flame cards hidden, to see the box behind them.</summary>
            public bool HideSmoke;
            public bool HideFlames;
        }

        internal sealed class Request
        {
            public string Name = "firebox";

            /// <summary>The engine's car ID; the fire nearest the camera when not given.</summary>
            public string Car;

            /// <summary>Spawn a missing stock coal locomotive for an unsaved capture session.</summary>
            public bool SpawnStockCar;

            /// <summary>Keep the normal fire overlay visible when it is enabled in settings.</summary>
            public bool ShowOverlay;

            /// <summary>The camera's vertical field of view for the shots, degrees; the player's when not given.</summary>
            public float? Fov;

            /// <summary>How far the fire door is held open, 0 to 1.</summary>
            public float Door = 1f;

            /// <summary>
            /// Puts the player in the engine's cab first, so the game loads its
            /// interior (the fire bed and flames are drawn in it); the exterior
            /// stand-in has a shut door and a bare box.
            /// </summary>
            public bool Board = true;
            public bool RespectVisibility;

            public string[] Views = { "door", "inside" };
            public State[] States = { new State() };

            /// <summary>Seconds of game time each view is filmed for, in a folder of frames; 0 for one still.</summary>
            public float Clip;

            /// <summary>A clip's frames a second of game time.</summary>
            public int ClipRate = 30;

            /// <summary>A clip's frames' width, pixels (the screen's height kept to it); 0 for the screen's.</summary>
            public int ClipWidth = 1280;

            /// <summary>Optional native render size, before any clip resizing; both dimensions must be positive.</summary>
            public int RenderWidth;
            public int RenderHeight;

            /// <summary>Writes the volume's zones each frame of a clip to trace.tsv.</summary>
            public bool Trace;

            /// <summary>
            /// Frames timed before each still, uncapped, their mean and slowest
            /// tenth put in its line (0: none), for the volume's cost against a
            /// state that hides it (HideFlames).
            /// </summary>
            public int TimeFrames;
        }

        private static readonly AccessTools.FieldRef<PortReference, Port> ReferencedPort =
            AccessTools.FieldRefAccess<PortReference, Port>("port");

        private float nextPoll;

        private void Update()
        {
            if (Running || PlumeCapture.Running || Time.unscaledTime < nextPoll || !FireboxTestBed.Enabled)
                return;
            nextPoll = Time.unscaledTime + PollEvery;
            var path = Path.Combine(Main.Mod.Path, RequestFile);
            if (!File.Exists(path))
                return;
            Request request;
            try
            {
                request = JsonConvert.DeserializeObject<Request>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                request = null;
                Main.Mod.Logger.Warning($"Firebox capture: the request can't be read: {e.Message}");
            }
            File.Delete(path);
            if (request != null)
                StartCoroutine(Run(request));
        }

        private IEnumerator Run(Request request)
        {
            var saves = SingletonBehaviour<SaveGameManager>.Instance;
            if (saves != null && !saves.disableAutosave)
            {
                saves.disableAutosave = true;
                Main.Mod.Logger.Log("Firebox capture: the game's autosave is off for the rest of the session");
            }
            var firebox = Find(request.Car);
            if (firebox == null && request.SpawnStockCar)
            {
                // Only the two stock coal engines; never substitute a custom
                // livery in a screenshot meant to represent the base game.
                if (request.Car != "LocoS282A" && request.Car != "LocoS060")
                {
                    Main.Mod.Logger.Warning("Firebox capture: stock spawning requires LocoS282A or LocoS060");
                    yield break;
                }
                var ids = request.Car == "LocoS282A" ? new[] { "LocoS282A", "LocoS282B" } : new[] { "LocoS060" };
                var liveries = ids.Select(id => Globals.G.Types.Liveries.FirstOrDefault(l => l.id == id)).ToList();
                if (liveries.Any(l => l == null))
                {
                    Main.Mod.Logger.Warning("Firebox capture: stock livery unavailable");
                    yield break;
                }
                var spawned = SingletonBehaviour<CarSpawner>.Instance.SpawnCarTypesOnClosestTrack(
                    liveries, PlayerManager.PlayerTransform.position, ids.Select(_ => false).ToList(),
                    preventAutoCoupleOnLastCars: true, applyHandbrakeOnLastCars: true, playerSpawnedCars: true);
                if (spawned == null || spawned.Count == 0)
                {
                    Main.Mod.Logger.Warning("Firebox capture: no room to spawn the stock locomotive");
                    yield break;
                }
                yield return null;
                firebox = Find(spawned[0].ID);
            }
            if (firebox == null)
            {
                Main.Mod.Logger.Warning($"Firebox capture: no fire {(string.IsNullOrEmpty(request.Car) ? "near" : $"on {request.Car}")}; available: " +
                    string.Join(", ", FireboxRegistry.All.Select(f => $"{f.Car?.ID}/{f.Car?.carLivery?.id}")));
                yield break;
            }
            var car = firebox.Car;
            var livery = car.carLivery != null ? car.carLivery.id : null;
            if (livery == null || !FireholeOpening.TryHole(livery, out var hole))
            {
                Main.Mod.Logger.Warning($"Firebox capture: no firehole known for {livery ?? car.ID} to look through");
                yield break;
            }
            if (request.Board && !car.IsInteriorLoaded)
            {
                PlayerManager.TeleportPlayerToCar(car);
                Main.Mod.Logger.Log($"Firebox capture: boarded {car.ID} to load its cab");
                for (var waited = 0f; waited < 30f && !car.IsInteriorLoaded; waited += Time.unscaledDeltaTime)
                    yield return null;
                if (!car.IsInteriorLoaded)
                {
                    Main.Mod.Logger.Warning($"Firebox capture: {car.ID}'s cab did not load");
                    yield break;
                }
                yield return new WaitForSecondsRealtime(3f);
            }
            var flow = car.SimController != null ? car.SimController.SimulationFlow : null;
            var doorId = car.SimController != null && car.SimController.firebox != null ? car.SimController.firebox.fireboxDoorPortId : null;
            var doorPort = flow != null && flow.TryGetPort(doorId, out var found, canBeNullOrEmpty: true) ? found : null;
            var door = new Door(car, doorPort);
            var exhaust = flow?.OrderedSimComps.OfType<SteamExhaust>().FirstOrDefault();
            var blower = exhaust != null && exhaust.blowerControl != null && exhaust.blowerControl.IsConnected ? ReferencedPort(exhaust.blowerControl) : null;
            var doorBefore = door.Value;
            var blowerBefore = blower != null ? blower.Value : 0f;
            var stoker = flow?.OrderedSimComps.OfType<RealisticStoker>().FirstOrDefault();
            var stokerControl = stoker != null ? AccessTools.Field(typeof(RealisticStoker), "control")?.GetValue(stoker) as PortReference : null;
            var stokerPort = stokerControl != null && stokerControl.IsConnected ? ReferencedPort(stokerControl) : null;
            var masterId = StokerJetControls.Valves.First(v => v.Jet == null).PortId;
            var masterPort = stoker != null && flow != null && flow.TryGetPort(masterId, out var master, canBeNullOrEmpty: true) ? master : null;
            var stokerBefore = stokerPort?.Value ?? 0f;
            var masterBefore = masterPort?.Value ?? 0f;

            var folder = Path.Combine(Main.Mod.Path, "captures", $"{PlumeCapture.Safe(request.Name)}-{DateTime.Now:yyyyMMdd-HHmmss}");
            Directory.CreateDirectory(folder);
            var stop = Path.Combine(Main.Mod.Path, StopFile);
            var shots = new StringBuilder("shot\tstate\tview\tdraft\tbeats\tfire\n");
            var timing = "";
            var count = 0;
            Main.Mod.Logger.Log($"Firebox capture '{request.Name}' on {car.ID} ({livery}): {request.States.Length} states, " +
                                $"{request.Views.Length} views, into {folder}");
            Running = true;
            ShowOverlay = request.ShowOverlay;
            fov = request.Fov;
            var vSyncBefore = QualitySettings.vSyncCount;
            var rateBefore = Application.targetFrameRate;
            var qualityBefore = Main.Settings.FireboxQuality;
            var widthBefore = Screen.width;
            var heightBefore = Screen.height;
            var modeBefore = Screen.fullScreenMode;
            var resize = request.RenderWidth > 0 && request.RenderHeight > 0;
            if (request.TimeFrames > 0)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = -1;
            }
            Camera.onPreCull += Hold;
            Camera.onPostRender += Release;
            try
            {
                if (resize)
                {
                    Screen.SetResolution(request.RenderWidth, request.RenderHeight, modeBefore);
                    for (var waited = 0f; waited < 5f && (Screen.width != request.RenderWidth || Screen.height != request.RenderHeight); waited += Time.unscaledDeltaTime)
                        yield return null;
                }
                Main.Mod.Logger.Log($"Firebox capture: rendering at {Screen.width} x {Screen.height}");
                foreach (var state in request.States)
                {
                    Main.Settings.FireboxQuality = state.Quality ?? qualityBefore;
                    respectVisibility = request.RespectVisibility;
                    var opening = state.Door ?? request.Door;
                    if (File.Exists(stop))
                    {
                        File.Delete(stop);
                        Main.Mod.Logger.Log("Firebox capture stopped");
                        yield break;
                    }
                    if (firebox.Car == null)
                    {
                        Main.Mod.Logger.Warning("Firebox capture: the engine went away");
                        yield break;
                    }
                    FireboxTestBed.Draft = state.Draft;
                    FireboxTestBed.Air = state.Air;
                    FireboxTestBed.LightView = state.LightView;
                    FireboxTestBed.HideSmoke = state.HideSmoke;
                    FireboxTestBed.VolumeView = state.VolumeView;
                    var untuned = FireboxTestBed.Tune(state.Tuning);
                    if (untuned != null)
                        Main.Mod.Logger.Warning($"Firebox capture: no tunable '{untuned}'");
                    FireboxTestBed.HideFlames = state.HideFlames;
                    FireboxLighting.WallLight = state.WallLight ?? FireboxLighting.WallLight;
                    FireboxLighting.CoalLight = state.CoalLight ?? FireboxLighting.CoalLight;
                    FireboxLighting.BedLight = state.BedLight ?? FireboxLighting.BedLight;
                    FireboxLighting.FlameLight = state.FlameLight ?? FireboxLighting.FlameLight;
                    if (state.Out)
                    {
                        firebox.Model.Extinguish();
                        Main.Mod.Logger.Log($"Firebox capture: put out the fire on {car.ID}");
                    }
                    if (state.Burning > 0f)
                    {
                        firebox.LayBurningBed(state.Burning);
                        Main.Mod.Logger.Log($"Firebox capture: laid {state.Burning:F0} kg of burning coal on {car.ID}");
                    }
                    for (var z = 0; z < GrateLayout.ZoneCount && state.Bed > 0f; z++)
                        firebox.DeliverShovelful(state.Bed / GrateLayout.ZoneCount, (GrateZone)z);
                    FireboxTestBed.BeatRate = Mathf.Max(0f, state.Beats);
                    FireboxTestBed.BeatStrength = state.BeatStrength;
                    if (blower != null && state.Blower is float b)
                        blower.ExternalValueUpdate(Mathf.Clamp01(b));
                    if (stokerPort != null && state.Stoker is float s)
                        stokerPort.ExternalValueUpdate(Mathf.Clamp01(s));
                    if (masterPort != null && state.StokerMaster is float m)
                        masterPort.ExternalValueUpdate(Mathf.Clamp01(m));
                    foreach (var shovelful in state.Shovelfuls ?? new string[0])
                        Land(firebox, shovelful);
                    if (state.Light && !firebox.Model.FireOn && firebox.Model.CoalMass > 0f)
                    {
                        firebox.Model.Ignite();
                        Main.Mod.Logger.Log($"Firebox capture: lit the fire on {car.ID} ({firebox.Model.CoalMass:F0} kg of coal)");
                    }
                    var stateName = state.Name ?? $"draft {(state.Draft is float d ? d.ToString("0.##") : "fire's")} beats {state.Beats:0.#}";
                    held = View(car, hole, request.Views.FirstOrDefault());
                    for (var waited = 0f; waited < state.Settle; waited += Time.unscaledDeltaTime)
                    {
                        door.Set(opening);
                        yield return null;
                    }
                    foreach (var view in request.Views)
                    {
                        held = View(car, hole, view);
                        if (held == null)
                        {
                            Main.Mod.Logger.Warning($"Firebox capture: no view '{view}'");
                            continue;
                        }
                        for (var i = 0; i < HoldFrames; i++)
                        {
                            door.Set(opening);
                            yield return null;
                        }
                        var file = $"{++count:000}-{PlumeCapture.Safe(stateName)}-{view}";
                        if (request.Clip > 0f)
                        {
                            var clip = Path.Combine(folder, file);
                            Directory.CreateDirectory(clip);
                            var rate = Mathf.Clamp(request.ClipRate, 1, 240);
                            var frames = Mathf.CeilToInt(request.Clip * rate);
                            Time.captureFramerate = rate;
                            var trace = request.Trace ? new StringBuilder() : null;
                            for (var frame = 0; frame < frames; frame++)
                            {
                                door.Set(opening);
                                yield return new WaitForEndOfFrame();
                                File.WriteAllBytes(Path.Combine(clip, $"{frame:0000}.jpg"), PlumeCapture.Frame(request.ClipWidth));
                                var traced = trace != null && car.loadedInterior != null ? car.loadedInterior.GetComponentInChildren<FireboxVolume>(true) : null;
                                if (traced != null)
                                    trace.Append($"{frame}\t{Time.time:F3}\t{traced.Describe()}\n");
                            }
                            if (trace != null)
                                File.WriteAllText(Path.Combine(clip, "trace.tsv"), trace.ToString());
                            Time.captureFramerate = 0;
                        }
                        else
                        {
                            if (request.TimeFrames > 0)
                            {
                                var times = new System.Collections.Generic.List<float>(request.TimeFrames);
                                for (var i = 0; i < request.TimeFrames; i++)
                                {
                                    door.Set(opening);
                                    yield return null;
                                    times.Add(Time.unscaledDeltaTime * 1000f);
                                }
                                times.Sort();
                                timing = $"frame {times.Average():F2} ms, slowest tenth from {times[times.Count * 9 / 10]:F2} ms; ";
                            }
                            yield return new WaitForEndOfFrame();
                            file += ".jpg";
                            File.WriteAllBytes(Path.Combine(folder, file), PlumeCapture.Frame(0));
                        }
                        var model = firebox.Model;
                        var volume = car.loadedInterior != null ? car.loadedInterior.GetComponentInChildren<FireboxVolume>(true) : null;
                        shots.Append($"{file}\t{stateName}\t{view}\t{(state.Draft is float sd ? sd.ToString("0.##") : "fire's")}\t{state.Beats:0.##}\t{timing}" +
                                     $"coal {model.CoalMass:F0} kg, heat {model.Heat / 1e6f:F1} MW, air {model.PrimaryAirFlow:F2} kg/s, smoke {model.SmokeDensity:P0}" +
                                     (volume != null ? "; " + volume.Describe() : "") +
                                     "; " + car.loadedInterior?.GetComponentInChildren<FireboxLighting>(true)?.Describe() +
                                     "; " + car.loadedInterior?.GetComponentInChildren<FireboxFlames>(true)?.DescribeParticles() +
                                     "; " + car.loadedInterior?.GetComponentInChildren<StokerSpray>(true)?.Describe() + "\n");
                    }
                }
            }
            finally
            {
                Main.Settings.FireboxQuality = qualityBefore;
                Time.captureFramerate = 0;
                if (resize)
                    Screen.SetResolution(widthBefore, heightBefore, modeBefore);
                if (request.TimeFrames > 0)
                {
                    QualitySettings.vSyncCount = vSyncBefore;
                    Application.targetFrameRate = rateBefore;
                }
                held = null;
                fov = null;
                Camera.onPreCull -= Hold;
                Camera.onPostRender -= Release;
                FireboxTestBed.Clear();
                door.Set(doorBefore);
                if (blower != null)
                    blower.ExternalValueUpdate(blowerBefore);
                stokerPort?.ExternalValueUpdate(stokerBefore);
                masterPort?.ExternalValueUpdate(masterBefore);
                Running = false;
                ShowOverlay = false;
                File.WriteAllText(Path.Combine(folder, "shots.tsv"), shots.ToString());
                File.WriteAllText(Path.Combine(folder, "done.txt"), $"{count} shots\n");
                Main.Mod.Logger.Log($"Firebox capture '{request.Name}' done: {count} shots in {folder}");
            }
        }

        // The named car's fire, or the one nearest the camera.
        private static RealisticFirebox Find(string carId)
        {
            var all = FireboxRegistry.All.Where(f => f.Car != null).ToList();
            if (!string.IsNullOrEmpty(carId))
                return all.FirstOrDefault(f => f.Car.ID == carId || f.Car.carLivery?.id == carId);
            var camera = PlayerManager.ActiveCamera;
            var from = camera != null ? camera.transform.position : Vector3.zero;
            return all.OrderBy(f => (f.Car.transform.position - from).sqrMagnitude).FirstOrDefault();
        }

        // A shovelful, "Zone:kg", landed as the shovel lands one.
        private static void Land(RealisticFirebox firebox, string shovelful)
        {
            var parts = (shovelful ?? "").Split(':');
            if (parts.Length != 2 || !Enum.TryParse(parts[0].Trim(), true, out GrateZone zone)
                || !float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var kg))
            {
                Main.Mod.Logger.Warning($"Firebox capture: no shovelful '{shovelful}' (Zone:kg)");
                return;
            }
            firebox.DeliverShovelful(kg, zone);
        }

        // The fire door: the cab's own control where the cab is loaded, so its
        // leaves open and its port follows; else the port alone.
        private sealed class Door
        {
            private readonly TrainCar car;
            private readonly Port port;

            public Door(TrainCar car, Port port)
            {
                this.car = car;
                this.port = port;
            }

            private DV.CabControls.ControlImplBase Control()
            {
                var interior = car != null ? car.loadedInterior : null;
                var controls = interior != null ? interior.GetComponentInChildren<DV.HUD.InteriorControlsManager>(true) : null;
                return controls != null && controls.TryGetControl(DV.HUD.InteriorControlsManager.ControlType.Firedoor, out var found)
                    ? found.controlImplBase
                    : null;
            }

            public float Value => Control() is DV.CabControls.ControlImplBase control ? control.Value : port != null ? port.Value : 0f;

            public void Set(float opening)
            {
                opening = Mathf.Clamp01(opening);
                var control = Control();
                if (control != null)
                {
                    if (Mathf.Abs(control.Value - opening) > 0.001f)
                        control.SetValue(opening);
                }
                else if (port != null && Mathf.Abs(port.Value - opening) > 0.001f)
                    port.ExternalValueUpdate(opening);
            }
        }

        // The views through the firehole, from the hole in the car's frame (x
        // right, y up, z forward; the hole's plane across the car): the
        // camera's place and where it looks.
        private static Pose? View(TrainCar car, (float Middle, float Foot, float Width, float Height, float Plane) hole, string view)
        {
            if (car == null)
                return null;
            var middle = hole.Foot + hole.Height / 2f;
            Vector3 at, look;
            switch (view)
            {
                case "backhead":
                    // Include the wall below the firehole and the dump linkage:
                    // close views through the hole miss flames leaking into the cab.
                    at = new Vector3(hole.Middle + 0.2f, middle + 0.25f, hole.Plane - 1.1f);
                    look = new Vector3(hole.Middle, hole.Foot - 0.2f, hole.Plane + 0.1f);
                    break;
                case "crown":
                    // Look above the bed to check actual clearance below the roof.
                    at = new Vector3(hole.Middle, hole.Foot + 0.8f * hole.Height, hole.Plane + 0.15f);
                    look = new Vector3(hole.Middle, hole.Foot + 0.5f, hole.Plane + 2f);
                    break;
                case "door":
                    // A fireman's eye, standing back from the open door.
                    at = new Vector3(hole.Middle, middle + 0.35f, hole.Plane - 0.85f);
                    look = new Vector3(hole.Middle, hole.Foot - 0.2f, hole.Plane + 1.3f);
                    break;
                case "door-near":
                    // Close to the door, looking in and down.
                    at = new Vector3(hole.Middle, middle + 0.15f, hole.Plane - 0.4f);
                    look = new Vector3(hole.Middle, hole.Foot - 0.25f, hole.Plane + 1.4f);
                    break;
                case "door-left":
                    at = new Vector3(hole.Middle - 0.3f, middle + 0.3f, hole.Plane - 0.75f);
                    look = new Vector3(hole.Middle + 0.35f, hole.Foot - 0.2f, hole.Plane + 1.3f);
                    break;
                case "door-right":
                    at = new Vector3(hole.Middle + 0.3f, middle + 0.3f, hole.Plane - 0.75f);
                    look = new Vector3(hole.Middle - 0.35f, hole.Foot - 0.2f, hole.Plane + 1.3f);
                    break;
                case "inside":
                    // Just inside the door, as the footage's camera at the hole.
                    at = new Vector3(hole.Middle, middle, hole.Plane + 0.12f);
                    look = new Vector3(hole.Middle, hole.Foot - 0.1f, hole.Plane + 2f);
                    break;
                case "inside-low":
                    // Low over the bed, looking along it to the tube plate.
                    at = new Vector3(hole.Middle, hole.Foot + 0.05f, hole.Plane + 0.15f);
                    look = new Vector3(hole.Middle, hole.Foot - 0.05f, hole.Plane + 2.2f);
                    break;
                default:
                    return null;
            }
            var t = car.transform;
            var from = t.TransformPoint(at);
            return new Pose(from, Quaternion.LookRotation(t.TransformPoint(look) - from, t.up));
        }

        // The game's camera, held at the view (and the field of view) for each
        // render and put back after.
        private static Pose? held;
        private static float? fov;
        private static Camera holding;
        private static Vector3 heldFrom;
        private static Quaternion heldFacing;
        private static float heldFov;

        private static void Hold(Camera camera)
        {
            if (!(held is Pose pose) || camera != PlayerManager.ActiveCamera)
                return;
            holding = camera;
            heldFrom = camera.transform.position;
            heldFacing = camera.transform.rotation;
            heldFov = camera.fieldOfView;
            camera.transform.SetPositionAndRotation(pose.position, pose.rotation);
            if (fov is float degrees)
                camera.fieldOfView = Mathf.Clamp(degrees, 5f, 120f);
        }

        private static void Release(Camera camera)
        {
            if (camera != holding)
                return;
            camera.transform.SetPositionAndRotation(heldFrom, heldFacing);
            camera.fieldOfView = heldFov;
            holding = null;
        }
    }
}
