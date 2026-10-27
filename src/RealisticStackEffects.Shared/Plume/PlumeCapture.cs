using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DV;
using DV.Utils;
using LocoSim.Implementations;
using Newtonsoft.Json;
using RealisticStackEffects.Core;
using UnityEngine;

namespace RealisticStackEffects
{
 internal sealed class PlumeCapture : MonoBehaviour
    {
        private const string RequestFile = "capture-request.json";
        private const string StopFile = "capture-stop";
        private const float PollEvery = 1f;

        // Frames the camera is held at a view before its shot, so the
        // antialiasing's history is the view's own.
        private const int HoldFrames = 12;

        private const int JpegQuality = 92;

        public static bool Running { get; private set; }

        internal sealed class Scene
        {
            public string Name;

            /// <summary>The game's hour, 0 to 24; the game's own when not given.</summary>
            public float? Hour;

            /// <summary>The game's rain, 0 to 1; the game's own when not given.</summary>
            public float? Rain;

            /// <summary>A test bed weather preset (its name or the start of it), or null for the game's air.</summary>
            public string Air;

            public float? Temperature;
            public float? Humidity;

            /// <summary>The test bed's wind with its air set, m/s and the bearing it blows toward.</summary>
            public float Wind;
            public float WindToward = 90f;
        }

        /// <summary>A set of test bed tuning and look values to shoot with, as name=value pairs (<see cref="PlumeTestBed.Restore"/>).</summary>
        internal sealed class Variant
        {
            public string Name;
            public string Tuning;

            /// <summary>The player's smoke quality for it (High, Medium, Low); the player's own when not given.</summary>
            public string SmokeQuality;
        }

        internal sealed class Request
        {
            public string Name = "capture";

            /// <summary>Each run of the scenes and stacks, its tuning set; one as the test bed has it when not given.</summary>
            public Variant[] Variants = { new Variant() };

            /// <summary>The camera's vertical field of view for the shots, degrees; the player's when not given.</summary>
            public float? Fov;

            /// <summary>Seconds each stack runs before its shots.</summary>
            public float Settle = 8f;

            /// <summary>
            /// Coal delivered to the engine's firebox as each stack starts, kg,
            /// through its stock coal input as the shovel sends it (a
            /// shovelful landing, for its smoke-out); 0 none.
            /// </summary>
            public float Charge;

            public string[] Views = { "side", "front", "rear" };
            public Scene[] Scenes = { new Scene() };

            /// <summary>
            /// Test bed stack presets (their names or the start of them), or
            /// "engine" for the engine's own; a preset's values may follow a
            /// '|' as name=value pairs ("Well fired|Soot=0;Clear=1").
            /// </summary>
            public string[] Stacks = { "engine" };

            /// <summary>The engine's car ID; the plume nearest the camera when not given.</summary>
            public string Car;

            /// <summary>
            /// Clears the game's hour and rain if pinned, before anything else
            /// (a run's pins saved by an autosave come back with the save).
            /// </summary>
            public bool ClearWeather;

            /// <summary>
            /// The game's weather point pinned for the run, 0 to 1: its fog
            /// (x) and its cloud cover (y), as photo mode's weather sets them;
            /// the game's own when not given (runs in fresh games drew
            /// different clouds behind the plume).
            /// </summary>
            public float? Fog;
            public float? CloudCover;

            /// <summary>Seconds of game time each view is filmed for, in a folder of frames; 0 for one still.</summary>
            public float Clip;

            /// <summary>A clip's frames a second of game time.</summary>
            public int ClipRate = 30;

            /// <summary>A clip's frames' width, pixels (the screen's height kept to it); 0 for the screen's.</summary>
            public int ClipWidth = 1280;
            public int RenderWidth;
            public int RenderHeight;
            public bool DynamoOn;

            /// <summary>
            /// Frames timed before each still, their mean and slowest tenth
            /// put before its readout (0: none), for the plume's cost against
            /// a variant that hides it (Look.HidePlume).
            /// </summary>
            public int TimeFrames;
        }

        private float nextPoll;
        private Dynamo testDynamo;
        private Port testDynamoPressure;
        private ChimneyPlume testDynamoPlume;
        private ParticleSystem[] testDynamoSteam = Array.Empty<ParticleSystem>();
        private readonly ParticleSystem.Particle[] steamParticles = new ParticleSystem.Particle[1024];

        // Exercise the stock dynamo effects even when the loaded boiler is
        // cold. This fixture is scoped to a capture and never saves its ports.
        private void DriveDynamo()
        {
            if (!Running || testDynamo == null)
                return;
            if (testDynamoPressure != null)
                testDynamoPressure.Value = 9f;
            testDynamo.dynamoFlowNormalizedReadOut.Value = 1f;
            testDynamo.steamConsumptionReadOut.Value = testDynamo.steamConsumption;
        }

        private void LateUpdate()
        {
            DriveDynamo();
            var stack = PlumeTestBed.StackFor(testDynamoPlume);
            if (!Running || stack == null)
                return;
            // The stack test moves the air past a parked engine. Apply the
            // same moving reference frame to the stock dynamo particles.
            var move = -Vector3.ProjectOnPlane(testDynamoPlume.Car.transform.forward, Vector3.up).normalized
                       * (stack.Speed / 3.6f) * Time.deltaTime;
            foreach (var system in testDynamoSteam)
            {
                var main = system.main;
                var space = main.simulationSpace == ParticleSystemSimulationSpace.World ? null
                    : main.simulationSpace == ParticleSystemSimulationSpace.Custom ? main.customSimulationSpace : system.transform;
                var delta = space != null ? space.InverseTransformVector(move) : move;
                var count = system.GetParticles(steamParticles);
                for (var i = 0; i < count; i++)
                    steamParticles[i].position += delta;
                system.SetParticles(steamParticles, count);
            }
        }

        private void Update()
        {
            DriveDynamo();
            if (Running || Time.unscaledTime < nextPoll || StackHost.Settings == null || !StackHost.Settings.DeveloperTools)
                return;
            nextPoll = Time.unscaledTime + PollEvery;
            var path = Path.Combine(StackHost.Mod.Path, RequestFile);
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
                StackHost.Mod.Logger.Warning($"Plume capture: the request can't be read: {e.Message}");
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
                StackHost.Mod.Logger.Log("Plume capture: the game's autosave is off for the rest of the session");
            }
            var weather = GameWeather.Driver;
            var hourBefore = Kept(weather?.TimeOfDayHours);
            var rainBefore = Kept(weather?.RainValue);
            var fogBefore = Kept(weather?.WeatherPointX);
            var cloudsBefore = Kept(weather?.WeatherPointY);
            if (weather != null)
                StackHost.Mod.Logger.Log($"Plume capture: the game's hour {(hourBefore is float h ? $"pinned at {h:F2}" : "its own")}, " +
                                    $"its rain {(rainBefore is float r ? $"pinned at {r:F2}" : "its own")}" +
                                    (request.ClearWeather ? "; both cleared" : ""));
            if (request.ClearWeather && weather != null)
            {
                hourBefore = null;
                rainBefore = null;
                Put(weather.TimeOfDayHours, null);
                Put(weather.RainValue, null);
                weather.manager.RefreshTimeOfDay();
            }
            if (request.Scenes.Length == 0 || request.Stacks.Length == 0)
                yield break;
            StackHost.Mod.Logger.Log($"Plume capture: plumes on {ChimneyPlume.ActiveCars()}");
            StackHost.Mod.Logger.Log("Plume capture: cameras " + string.Join(", ", Camera.allCameras.Select(c =>
                $"{c.name} depth {c.depth:F0} clip {c.nearClipPlane:G3}-{c.farClipPlane:G4} mask {c.cullingMask:X8} {(c.targetTexture != null ? "to a texture" : "to the screen")}{(c.enabled ? "" : " off")}")));
            var plume = string.IsNullOrEmpty(request.Car) ? ChimneyPlume.Nearest() : ChimneyPlume.Named(request.Car);
            if (plume == null)
            {
                StackHost.Mod.Logger.Warning($"Plume capture: no engine with a plume {(string.IsNullOrEmpty(request.Car) ? "near" : $"on {request.Car}")}");
                yield break;
            }
            var folder = Path.Combine(StackHost.Mod.Path, "captures", $"{Safe(request.Name)}-{DateTime.Now:yyyyMMdd-HHmmss}");
            Directory.CreateDirectory(folder);
            var stop = Path.Combine(StackHost.Mod.Path, StopFile);
            var shots = new StringBuilder("shot\tscene\tstack\tview\tair\ttime scale\treadout\n");
            var timing = "";
            var count = 0;
            var bed = new TestBedState();
            var tuned = PlumeTestBed.Snapshot();
            StackHost.Mod.Logger.Log($"Plume capture '{request.Name}' on {plume.CarName}: {request.Variants.Length} variants, {request.Scenes.Length} scenes, " +
                                $"{request.Stacks.Length} stacks, {request.Views.Length} views, into {folder}");
            Running = true;
            fov = request.Fov;
            // Timed frames uncapped, so a frame without the plume shows its own time.
            var vSyncBefore = QualitySettings.vSyncCount;
            var rateBefore = Application.targetFrameRate;
            if (request.TimeFrames > 0)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = -1;
            }
            // Pinned inside the run, so it is put back however the run ends.
            if (weather != null && request.Fog is float fog)
                weather.WeatherPointX.EngageOverride(Mathf.Clamp01(fog));
            if (weather != null && request.CloudCover is float clouds)
                weather.WeatherPointY.EngageOverride(Mathf.Clamp01(clouds));
            Camera.onPreCull += Hold;
            Camera.onPostRender += Release;
            var qualityBefore = StackHost.Settings.SmokeQuality;
            var widthBefore = Screen.width;
            var heightBefore = Screen.height;
            var modeBefore = Screen.fullScreenMode;
            var resize = request.RenderWidth > 0 && request.RenderHeight > 0;
            var dynamo = plume.Car.SimController?.SimulationFlow?.OrderedSimComps.OfType<LocoSim.Implementations.Dynamo>().FirstOrDefault();
            var dynamoPort = dynamo != null
                ? HarmonyLib.AccessTools.Field(dynamo.dynamoControl.GetType(), "port")?.GetValue(dynamo.dynamoControl) as Port
                : null;
            var dynamoBefore = dynamoPort != null ? dynamoPort.Value : 0f;
            var flowBefore = dynamo?.dynamoFlowNormalizedReadOut.Value ?? 0f;
            var consumptionBefore = dynamo?.steamConsumptionReadOut.Value ?? 0f;
            var pressurePort = dynamo != null
                ? HarmonyLib.AccessTools.Field(dynamo.steamPressure.GetType(), "port")?.GetValue(dynamo.steamPressure) as Port
                : null;
            var pressureBefore = pressurePort != null ? pressurePort.Value : 0f;
            testDynamo = request.DynamoOn ? dynamo : null;
            testDynamoPressure = request.DynamoOn ? pressurePort : null;
            testDynamoPlume = request.DynamoOn ? plume : null;
            testDynamoSteam = request.DynamoOn ? plume.Car.GetComponentsInChildren<ParticleSystem>(true)
                .Where(p => p.GetComponentsInParent<Transform>(true).Any(t => t.name == "DynamoSteam")).ToArray()
                : Array.Empty<ParticleSystem>();
            try
            {
                if (request.DynamoOn)
                    if (dynamoPort != null)
                        dynamoPort.Value = 1f;
                if (resize)
                {
                    Screen.SetResolution(request.RenderWidth, request.RenderHeight, modeBefore);
                    for (var waited = 0f; waited < 5f && (Screen.width != request.RenderWidth || Screen.height != request.RenderHeight); waited += Time.unscaledDeltaTime)
                        yield return null;
                }
                foreach (var variant in request.Variants)
                foreach (var scene in request.Scenes)
                {
                    PlumeTestBed.Restore(tuned);
                    PlumeTestBed.Restore(variant.Tuning);
                    StackHost.Settings.SmokeQuality = Enum.TryParse(variant.SmokeQuality, true, out SmokeQuality quality) ? quality : qualityBefore;
                    var sceneName = SetScene(scene, weather);
                    if (request.Variants.Length > 1)
                        sceneName = $"{variant.Name ?? variant.Tuning ?? "as tuned"} {sceneName}";
                    foreach (var stackName in request.Stacks)
                    {
                        if (File.Exists(stop))
                        {
                            File.Delete(stop);
                            StackHost.Mod.Logger.Log("Plume capture stopped");
                            yield break;
                        }
                        if (!plume.Active)
                        {
                            StackHost.Mod.Logger.Warning($"Plume capture: {plume.CarName}'s plume went away");
                            yield break;
                        }
                        if (!SetStack(stackName, plume))
                        {
                            StackHost.Mod.Logger.Warning($"Plume capture: no stack preset '{stackName}'");
                            continue;
                        }
                        held = View(plume, request.Views.FirstOrDefault());
                        if (request.Charge > 0f)
                            StackHost.Mod.Logger.Log($"Plume capture: {Deliver(plume.Car, request.Charge)}");
                        yield return new WaitForSecondsRealtime(request.Settle);
                        foreach (var view in request.Views)
                        {
                            held = View(plume, view);
                            // A view ending "-turn" turns about the vertical as it
                            // is filmed, as a player looking around.
                            turning = view.EndsWith("-turn", StringComparison.Ordinal) ? TurnRate : 0f;
                            turnedFrom = Time.time;
                            if (held == null)
                            {
                                StackHost.Mod.Logger.Warning($"Plume capture: no view '{view}'");
                                continue;
                            }
                            for (var i = 0; i < HoldFrames; i++)
                                yield return null;
                            var file = $"{++count:000}-{Safe(sceneName)}-{Safe(stackName)}-{view}";
                            if (request.Clip > 0f)
                            {
                                // Filmed: each frame the same step of game time.
                                var clip = Path.Combine(folder, file);
                                Directory.CreateDirectory(clip);
                                var rate = Mathf.Clamp(request.ClipRate, 1, 120);
                                var frames = Mathf.CeilToInt(request.Clip * rate);
                                Time.captureFramerate = rate;
                                for (var frame = 0; frame < frames; frame++)
                                {
                                    yield return new WaitForEndOfFrame();
                                    File.WriteAllBytes(Path.Combine(clip, $"{frame:0000}.jpg"), Frame(request.ClipWidth));
                                }
                                Time.captureFramerate = 0;
                            }
                            else
                            {
                                if (request.TimeFrames > 0)
                                {
                                    var times = new List<float>(request.TimeFrames);
                                    for (var i = 0; i < request.TimeFrames; i++)
                                    {
                                        yield return null;
                                        times.Add(Time.unscaledDeltaTime * 1000f);
                                    }
                                    times.Sort();
                                    timing = $"Frame: {times.Average():F2} ms, slowest tenth from {times[times.Count * 9 / 10]:F2} ms, over {times.Count} frames. ";
                                }
                                yield return new WaitForEndOfFrame();
                                file += ".jpg";
                                File.WriteAllBytes(Path.Combine(folder, file), Frame(0));
                            }
                            shots.Append($"{file}\t{sceneName}\t{(PlumeTestBed.StackOn ? stackName : "engine")}\t{view}\t{PlumeWeather.Describe()}\t" +
                                         $"{Time.timeScale:0.##}\t{timing}{plume.Readout().Replace('\n', ' ')}; {plume.SortingReadout()}\n");
                            timing = "";
                        }
                    }
                }
            }
            finally
            {
                testDynamo = null;
                testDynamoPressure = null;
                testDynamoPlume = null;
                testDynamoSteam = Array.Empty<ParticleSystem>();
                if (request.DynamoOn)
                {
                    if (dynamoPort != null)
                        dynamoPort.Value = dynamoBefore;
                    if (dynamo != null)
                    {
                        dynamo.dynamoFlowNormalizedReadOut.Value = flowBefore;
                        dynamo.steamConsumptionReadOut.Value = consumptionBefore;
                    }
                    if (pressurePort != null)
                        pressurePort.Value = pressureBefore;
                }
                if (resize)
                    Screen.SetResolution(widthBefore, heightBefore, modeBefore);
                Time.captureFramerate = 0;
                if (request.TimeFrames > 0)
                {
                    QualitySettings.vSyncCount = vSyncBefore;
                    Application.targetFrameRate = rateBefore;
                }
                held = null;
                fov = null;
                Camera.onPreCull -= Hold;
                Camera.onPostRender -= Release;
                StackHost.Settings.SmokeQuality = qualityBefore;
                PlumeTestBed.Restore(tuned);
                bed.Restore();
                if (weather != null)
                {
                    Put(weather.TimeOfDayHours, hourBefore);
                    Put(weather.RainValue, rainBefore);
                    if (request.Fog.HasValue)
                        Put(weather.WeatherPointX, fogBefore);
                    if (request.CloudCover.HasValue)
                        Put(weather.WeatherPointY, cloudsBefore);
                    weather.manager.RefreshTimeOfDay();
                }
                Running = false;
                File.WriteAllText(Path.Combine(folder, "shots.tsv"), shots.ToString());
                File.WriteAllText(Path.Combine(folder, "done.txt"), $"{count} shots\n");
                StackHost.Mod.Logger.Log($"Plume capture '{request.Name}' done: {count} shots in {folder}");
            }
        }

        // Coal into the engine's firebox as a shovelful lands: through the
        // game's coal transfer where the car has one (the shovel's own path),
        // else its stock coal input, which adds what is written and resets.
        private static string Deliver(TrainCar car, float kg)
        {
            var transfer = car != null && car.SimController != null ? car.SimController.firebox : null;
            if (transfer != null)
            {
                transfer.TransferCoal(kg * transfer.coalConsumptionMultiplier);
                return $"{kg:F1} kg of coal thrown into {car.ID}'s firebox";
            }
            var flow = car != null && car.SimController != null ? car.SimController.SimulationFlow : null;
            var input = flow?.OrderedSimComps.SelectMany(c => c.GetAllPorts())
                .FirstOrDefault(p => p.id.EndsWith(".COAL_CONTROL_EXT_IN", StringComparison.Ordinal));
            if (input == null)
                return $"no firebox coal input on {(car != null ? car.ID : "the engine")}";
            input.Value += kg;
            return $"{kg:F1} kg of coal into {input.id}";
        }

        // The screen as a JPEG, scaled down to <paramref name="width"/> (halving
        // first, so the scaling averages the pixels it drops); 0 as it is. The
        // screenshot's bytes are copied as they are (an sRGB target converted
        // them again: the frames came out washed out).
        internal static byte[] Frame(int width)
        {
            var shot = ScreenCapture.CaptureScreenshotAsTexture();
            if (width <= 0 || width >= shot.width)
            {
                var full = shot.EncodeToJPG(JpegQuality);
                Destroy(shot);
                return full;
            }
            var height = Mathf.Max(2, Mathf.RoundToInt(shot.height * (float)width / shot.width / 2f) * 2);
            Texture source = shot;
            RenderTexture half = null;
            if (shot.width >= 2 * width)
            {
                half = RenderTexture.GetTemporary(shot.width / 2, shot.height / 2, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                Graphics.Blit(shot, half);
                source = half;
            }
            var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(source, target);
            var active = RenderTexture.active;
            RenderTexture.active = target;
            var scaled = new Texture2D(width, height, TextureFormat.RGB24, false);
            scaled.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            scaled.Apply(false);
            RenderTexture.active = active;
            RenderTexture.ReleaseTemporary(target);
            if (half != null)
                RenderTexture.ReleaseTemporary(half);
            Destroy(shot);
            var bytes = scaled.EncodeToJPG(JpegQuality);
            Destroy(scaled);
            return bytes;
        }

        // Sets the scene and names it.
        private static string SetScene(Scene scene, DV.WeatherSystem.WeatherDriver weather)
        {
            var parts = new System.Collections.Generic.List<string>();
            if (weather != null)
            {
                if (scene.Hour is float hour)
                {
                    weather.TimeOfDayHours.EngageOverride(Mathf.Repeat(hour, 24f));
                    parts.Add($"{hour:00.0}h");
                }
                if (scene.Rain is float rain)
                {
                    weather.RainValue.EngageOverride(Mathf.Clamp01(rain));
                    parts.Add($"rain {rain:0.##}");
                }
                weather.manager.RefreshTimeOfDay();
            }
            if (scene.Air != null && PlumeTestBed.WeatherNamed(scene.Air, out var temperature, out var humidity))
            {
                SetAir(temperature, humidity, scene);
                parts.Add(scene.Air);
            }
            else if (scene.Temperature is float t && scene.Humidity is float h)
            {
                SetAir(t, h, scene);
                parts.Add($"{t:0.#}° {h * 100f:0}%");
            }
            else
            {
                PlumeTestBed.WeatherOn = false;
                parts.Add("game air");
            }
            return scene.Name ?? string.Join(" ", parts);
        }

        private static void SetAir(float temperature, float humidity, Scene scene)
        {
            PlumeTestBed.WeatherOn = true;
            PlumeTestBed.Temperature = temperature;
            PlumeTestBed.Humidity = humidity;
            PlumeTestBed.WindSpeed = scene.Wind;
            PlumeTestBed.WindToward = scene.WindToward;
        }

        private static bool SetStack(string name, ChimneyPlume plume)
        {
            if (string.Equals(name, "engine", StringComparison.OrdinalIgnoreCase))
            {
                PlumeTestBed.StackOn = false;
                return true;
            }
            var parts = name.Split('|');
            var preset = PlumeTestBed.StackNamed(parts[0].Trim());
            if (preset == null)
                return false;
            if (parts.Length > 1)
                foreach (var pair in parts[1].Split(';'))
                {
                    var value = pair.Split('=');
                    var field = value.Length == 2 ? typeof(StackTest).GetField(value[0].Trim()) : null;
                    if (field != null && field.FieldType == typeof(float)
                        && float.TryParse(value[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                        field.SetValue(preset, number);
                    else if (field != null && field.FieldType == typeof(bool) && bool.TryParse(value[1], out var flag))
                        field.SetValue(preset, flag);
                    else
                        StackHost.Mod.Logger.Warning($"Plume capture: no stack value '{pair}'");
                }
            PlumeTestBed.Stack = preset;
            PlumeTestBed.StackOn = true;
            PlumeTestBed.Aim(plume);
            return true;
        }

        // The views around the engine (close: the left side, nearer, for the
        // first few car lengths; tall: the left side, far enough back for a
        // standing column): the camera's place and where it looks,
        // from the stack along the engine (the test bed's air going past from
        // its front).
        private static Pose? View(ChimneyPlume plume, string view)
        {
            if (view.EndsWith("-turn", StringComparison.Ordinal))
                view = view.Substring(0, view.Length - "-turn".Length);
            var car = plume.Car;
            if (car == null || !plume.Active)
                return null;
            var stack = plume.StackTop;
            var ahead = Vector3.ProjectOnPlane(car.transform.forward, Vector3.up).normalized;
            var right = Vector3.Cross(Vector3.up, ahead);
            var rail = car.transform.position.y;
            Vector3 at, look;
            switch (view)
            {
                case "side":
                    at = stack + right * 14f - ahead * 4f;
                    at.y = rail + 1.7f;
                    look = stack + Vector3.up * 3f - ahead * 7f;
                    break;
                case "front":
                    at = stack + ahead * 18f + right * 9f;
                    at.y = rail + 1.7f;
                    look = stack + Vector3.up * 2.5f - ahead * 3f;
                    break;
                case "front-above":
                    at = stack + ahead * 18f + right * 9f;
                    at.y = rail + 11f;
                    look = stack + Vector3.up * 2f - ahead * 3f;
                    break;
                case "rear":
                    at = stack - ahead * 24f + right * 6f;
                    at.y = rail + 6f;
                    look = stack + Vector3.up * 2f;
                    break;
                case "wide":
                    at = stack + right * 38f - ahead * 12f;
                    at.y = rail + 2f;
                    look = stack + Vector3.up * 6f - ahead * 14f;
                    break;
                case "close":
                    at = stack - right * 10f - ahead * 6f;
                    at.y = rail + 3f;
                    look = stack + Vector3.up * 3f - ahead * 9f;
                    break;
                case "tall":
                    at = stack - right * 30f - ahead * 5f;
                    at.y = rail + 2f;
                    look = stack + Vector3.up * 12f - ahead * 6f;
                    break;
                case "above":
                    // From above and behind the tender, as a player stood on it.
                    at = stack - ahead * 16f + right * 3f;
                    at.y = rail + 11f;
                    look = stack + Vector3.up * 4f + ahead * 4f;
                    break;
                case "under":
                    // From the ground beside the engine, looking up into the plume.
                    at = stack + right * 6f + ahead * 2f;
                    at.y = rail + 1.7f;
                    look = stack + Vector3.up * 14f - ahead * 4f;
                    break;
                case "left":
                    at = stack - right * 14f - ahead * 4f;
                    at.y = rail + 1.7f;
                    look = stack + Vector3.up * 3f - ahead * 7f;
                    break;
                case "trail":
                    // From the left, well back: the plume fraying downwind.
                    at = stack - right * 40f - ahead * 25f;
                    at.y = rail + 2f;
                    look = stack + Vector3.up * 18f - ahead * 40f;
                    break;
                case "trail-under":
                    // From the ground under the trail, looking up into its fraying.
                    at = stack - ahead * 40f + right * 6f;
                    at.y = rail + 1.7f;
                    look = stack + Vector3.up * 30f - ahead * 46f;
                    break;
                case "cab":
                    // From the cab's roof, looking up and back at the plume over the train.
                    at = stack - ahead * 10f - right * 1f;
                    at.y = rail + 3f;
                    look = stack + Vector3.up * 14f - ahead * 22f;
                    break;
                default:
                    return null;
            }
            // Above whatever stands there.
            if (Physics.Raycast(new Vector3(at.x, at.y + 60f, at.z), Vector3.down, out var hit, 120f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                at.y = Mathf.Max(at.y, hit.point.y + 1.5f);
            return new Pose(at, Quaternion.LookRotation(look - at, Vector3.up));
        }

        // The game's camera, held at the view (and the field of view) for each
        // render and put back after.
        private static Pose? held;

        /// <summary>Where a capture holds the camera, if it does: the plume's silhouette is worked out from there.</summary>
        internal static Vector3? HeldAt => held?.position;
        // A turning view's rate about the vertical, degrees a second, and since when.
        private const float TurnRate = 30f;
        private static float turning;
        private static float turnedFrom;
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
            var rotation = turning != 0f ? Quaternion.AngleAxis(turning * (Time.time - turnedFrom), Vector3.up) * pose.rotation : pose.rotation;
            camera.transform.SetPositionAndRotation(pose.position, rotation);
            ChimneyPlume.SortForCamera(camera);
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

        private static float? Kept(OverridableValue<float> value) =>
            value != null && value.IsOverridden ? value.OverriddenValue : (float?)null;

        private static void Put(OverridableValue<float> value, float? kept)
        {
            if (kept is float before)
                value.EngageOverride(before);
            else
                value.ClearOverride();
        }

        internal static string Safe(string text)
        {
            var safe = new string(text.Select(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' ? c : '_').ToArray()).Trim('_');
            return safe.Length > 40 ? safe.Substring(0, 40) : safe;
        }

        // The test bed's weather and stack, put back after the run.
        private sealed class TestBedState
        {
            private readonly bool weatherOn = PlumeTestBed.WeatherOn;
            private readonly float temperature = PlumeTestBed.Temperature;
            private readonly float humidity = PlumeTestBed.Humidity;
            private readonly float windSpeed = PlumeTestBed.WindSpeed;
            private readonly float windToward = PlumeTestBed.WindToward;
            private readonly bool stackOn = PlumeTestBed.StackOn;
            private readonly StackTest stack = PlumeTestBed.Stack.Copy();
            private readonly ChimneyPlume aimed = PlumeTestBed.Aimed;

            public void Restore()
            {
                PlumeTestBed.WeatherOn = weatherOn;
                PlumeTestBed.Temperature = temperature;
                PlumeTestBed.Humidity = humidity;
                PlumeTestBed.WindSpeed = windSpeed;
                PlumeTestBed.WindToward = windToward;
                PlumeTestBed.StackOn = stackOn;
                PlumeTestBed.Stack = stack;
                PlumeTestBed.Aim(aimed);
            }
        }
    }
}
