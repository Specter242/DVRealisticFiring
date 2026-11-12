using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DV.CabControls;
using DV.CabControls.Spec;
using DV.HUD;
using DV.KeyboardInput;
using DV.Simulation.Ports;
using LocoSim.Implementations;
using Newtonsoft.Json;
using RealisticFiring.Sim;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RealisticFiring.Visuals
{
    /// <summary>S282 cab injector. Missing or incompatible assets keep the existing controls.</summary>
    internal static class S282InjectorCab
    {
        internal const string ModelFile = "S282Injector.json";
        internal const string WaterSourcePath = StokerCab.SourceValve + "/" + StokerCab.SourceControl;
        private static S282InjectorData data;
        private static bool attempted;

        internal static bool TryAttach(TrainCar car, Transform interior, InjectorRig rig, out string note)
        {
            note = null;
            if (car.carLivery?.id != "LocoS282A" || rig.Circuits.Count != 1 || rig.Circuits[0].Added == null)
                return false;
            var path = Path.Combine(Main.Mod.Path, ModelFile);
            if (!attempted)
            {
                attempted = true;
                if (!File.Exists(path)) return false;
                try
                {
                    if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("Model file is too large");
                    var loaded = JsonConvert.DeserializeObject<S282InjectorData>(File.ReadAllText(path));
                    if (loaded == null) throw new InvalidDataException("Empty injector model");
                    loaded.Validate();
                    data = loaded;
                }
                catch (Exception e) { Main.Mod.Logger.Warning("S282 injector left stock: " + e.Message); }
            }
            if (data == null) return false;
            GameObject root = null;
            S282InjectorLifetime lifetime = null;
            try
            {
                var circuit = rig.Circuits[0];
                var stock = interior.GetComponentsInChildren<InteractablePortFeeder>(true)
                    .FirstOrDefault(f => f.portId == circuit.Control.id);
                var prefab = car.carLivery.interiorPrefab.transform;
                var waterSource = prefab.Find(WaterSourcePath);
                var steamSource = prefab.Find("Right/Sand/C_Sand");
                if (stock == null || waterSource?.GetComponent<Rotary>() == null || steamSource?.GetComponent<Lever>() == null)
                    throw new InvalidDataException("Stock blower/sander control template changed");
                var materials = car.GetComponentsInChildren<MeshRenderer>(true).SelectMany(r => r.sharedMaterials)
                    .Where(m => m != null).Distinct().ToList();
                var chosen = data.Meshes.Select(p => p.Material).Distinct().ToDictionary(name => name,
                    name => materials.FirstOrDefault(m => m.name == name || m.name.StartsWith(name + " (", StringComparison.Ordinal)));
                if (chosen.Values.Any(m => m == null)) throw new InvalidDataException("Missing stock S282 materials");
                // Everything is prepared inactive. A failed fit never disables the old controls.
                root = new GameObject(InjectorSteamCab.RootName);
                lifetime = root.AddComponent<S282InjectorLifetime>();
                Action<GameObject> unloading = loaded =>
                {
                    if (loaded == interior.gameObject) lifetime.RestoreOriginalState = false;
                };
                car.InteriorAboutToBeUnloaded += unloading;
                lifetime.Detach = () => { if (car != null) car.InteriorAboutToBeUnloaded -= unloading; };
                root.SetActive(false);
                root.transform.SetParent(interior, false);
                root.transform.SetPositionAndRotation(car.transform.position, car.transform.rotation);
                foreach (var mask in data.Masks) PrepareCut(car, interior, mask, lifetime);
                var water = Control(waterSource, root.transform, car, V(data.WaterPivot), circuit.WaterValve,
                    () => InjectorSteamCab.WaterHover(circuit));
                var steam = Control(steamSource, root.transform, car, V(data.SteamPivot), circuit.SteamValve,
                    () => InjectorSteamCab.SteamHover(circuit, InjectorValves.Steam));
                Configure(water.GetComponent<Rotary>(), data);
                var waterInput = water.gameObject.AddComponent<S282ContinuousWaterWheel>();
                waterInput.Original = stock;
                waterInput.Car = car;
                waterInput.Lifetime = lifetime;
                Configure(steam.GetComponent<Lever>(), data);
                steam.gameObject.AddComponent<S282ContinuousValve>();
                steam.gameObject.AddComponent<SpringFollowsHand>();
                var scrolling = steam.gameObject.AddComponent<Patches.NotchScroll>();
                scrolling.DegreesPerSecond = 140f; scrolling.ContinuousValue = true; scrolling.DirectionalPresets = true;
                steam.gameObject.AddComponent<DV.HUD.NotchedControlCustomNames>().customNames = InjectorValves.Notches;
                var stem = new GameObject("steam valve rod").transform;
                stem.SetParent(root.transform, false); stem.localPosition = V(data.StemPin);
                var handle = new GameObject("steam handle motion").transform;
                handle.SetParent(steam, false);
                var carrier = new GameObject("upper suspension link").transform;
                carrier.SetParent(root.transform, false); carrier.localPosition = V(data.CarrierPivot);
                var linkage = root.AddComponent<S282InjectorLinkage>();
                linkage.Lever = steam; linkage.Handle = handle; linkage.Carrier = carrier; linkage.Stem = stem; linkage.Model = data;
                var highlights = new Dictionary<Transform, List<Renderer>> {
                    [water] = new List<Renderer>(), [steam] = new List<Renderer>()
                };
                foreach (var p in data.Meshes)
                {
                    var parent = p.Role == "water" ? water : p.Role == "steam" ? handle
                        : p.Role == "stem" ? stem : p.Role == "carrier" ? carrier : root.transform;
                    var part = new GameObject(p.Name);
                    part.transform.SetParent(parent, false);
                    part.transform.position = car.transform.TransformPoint(V(p.Pivot));
                    part.transform.rotation = car.transform.rotation;
                    var mesh = InjectorModelMesh.Create(p);
                    lifetime.Meshes.Add(mesh);
                    part.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = part.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = chosen[p.Material];
                    if (p.Role == "water" || p.Role == "steam")
                    {
                        part.layer = water.gameObject.layer;
                        highlights[p.Role == "water" ? water : steam].Add(renderer);
                    }
                }
                // Keep the working valve's sphere and rotational inertia.
                // A box around the replacement mesh made the same torque scroll
                // accelerate much more slowly than the blower/stoker wheels.
                var waterGrip = waterSource.GetComponent<Rotary>().colliderGameObjects
                    .Where(g => g != null).SelectMany(g => g.GetComponentsInChildren<SphereCollider>(true)).FirstOrDefault();
                if (waterGrip == null) throw new InvalidDataException("Stock blower sphere grip changed");
                Grip(water, highlights[water], .08f, sphereRadius: waterGrip.radius);
                Grip(steam, highlights[steam], .035f, handle);
                // Preserve the blower's native range and direction. Offset
                // only the model/grip so zero still has the modeled pose.
                foreach (Transform p in water)
                    p.RotateAround(water.position, water.TransformDirection(V(data.WaterAxis)),
                        -WaterClosedAngle(water.GetComponent<Rotary>()));
                var oldSpec = stock.GetComponent<ControlSpec>();
                var oldRotary = stock.GetComponent<Rotary>();
                var oldLever = stock.GetComponent<Lever>();
                var oldArea = oldRotary != null ? oldRotary.nonVrStaticInteractionArea : oldLever?.nonVrStaticInteractionArea;
                lifetime.ConcealControl(stock.gameObject, (oldSpec.colliderGameObjects ?? Array.Empty<GameObject>())
                    .Concat(new[] { oldArea != null ? oldArea.gameObject : null }).ToArray());
                lifetime.Hide(interior.Find("LocoS282A_Interior_Labels/Injector")?.gameObject);
                lifetime.ApplyCuts();
                root.SetActive(true);
                note = "S282 injector: Lilla's model; working water wheel and steam lever";
                return true;
            }
            catch (Exception e)
            {
                if (lifetime != null) lifetime.Clean();
                if (root != null) Object.DestroyImmediate(root);
                Main.Mod.Logger.Warning("S282 injector left stock: " + e.Message);
                return false;
            }
        }

        internal static void Configure(Rotary spec, S282InjectorData model)
        {
            // Keep the working blower/stoker wheel's native input, but no
            // detents: water delivery must retain arbitrary intermediate values.
            spec.jointAxis = V(model.WaterAxis);
            spec.useSteppedJoint = false;
        }

        internal static float WaterClosedAngle(Rotary spec) => !spec.useLimits ? 0f
            : Mathf.Clamp(spec.invertDirection ? spec.jointLimitMax : spec.jointLimitMin, -177f, 177f);

        internal static void Configure(Lever spec, S282InjectorData model)
        {
            spec.jointAxis = V(model.SteamAxis); spec.invertDirection = false;
            spec.useLimits = true; spec.jointLimitMin = 0f; spec.jointLimitMax = model.SteamTravel;
            spec.useSteppedJoint = false; spec.steppedValueUpdate = false; spec.notches = InjectorValves.Notches.Length;
            spec.scrollWheelHoverScroll = 1f;
        }

        private static Transform Control(Transform source, Transform parent, TrainCar car, Vector3 pivot, Port port, Func<string> hover)
        {
            if (port == null) throw new InvalidDataException("Missing existing injector valve port");
            var control = Object.Instantiate(source.gameObject, parent, false).transform;
            control.name = port.id;
            foreach (var input in control.GetComponents<MonoBehaviour>().Where(c =>
                StokerCab.InputComponents.Contains(c.GetType().Name) || c.GetType().Name == "PercentageControlNames"
                || c.GetType().Name == "NotchedControlCustomNames").ToList()) Object.DestroyImmediate(input);
            foreach (var r in control.GetComponentsInChildren<Renderer>(true)) Object.DestroyImmediate(r);
            foreach (var f in control.GetComponentsInChildren<MeshFilter>(true)) Object.DestroyImmediate(f);
            foreach (var c in control.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            var rotary = control.GetComponent<Rotary>();
            if (rotary != null) rotary.nonVrStaticInteractionArea = null;
            var lever = control.GetComponent<Lever>();
            if (lever != null) { lever.nonVrStaticInteractionArea = null; lever.interactionPoint = null; }
            control.SetPositionAndRotation(car.transform.TransformPoint(pivot), car.transform.rotation);
            var feeder = control.GetComponent<InteractablePortFeeder>();
            if (feeder == null) throw new InvalidDataException("Cloned control has no port feeder");
            feeder.portId = port.id;
            control.gameObject.AddComponent<SimControlBinder>().Bind(feeder, port);
            control.gameObject.AddComponent<HoverName>().Source = hover;
            return control;
        }

        private static void Grip(Transform control, List<Renderer> renderers, float minimum, Transform movingFrame = null, float? sphereRadius = null)
        {
            if (renderers.Count == 0) throw new InvalidDataException("Missing moving valve geometry");
            var bounds = new Bounds(); var first = true;
            foreach (var renderer in renderers)
            {
                var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                for (var i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? mesh.bounds.min.x : mesh.bounds.max.x,
                        (i & 2) == 0 ? mesh.bounds.min.y : mesh.bounds.max.y,
                        (i & 4) == 0 ? mesh.bounds.min.z : mesh.bounds.max.z);
                    var p = control.InverseTransformPoint(renderer.transform.TransformPoint(corner));
                    if (first) { bounds = new Bounds(p, Vector3.zero); first = false; } else bounds.Encapsulate(p);
                }
            }
            var grip = new GameObject("valve grip");
            grip.transform.SetParent(movingFrame != null ? movingFrame : control, false); grip.layer = control.gameObject.layer;
            if (sphereRadius.HasValue)
            {
                var sphere = grip.AddComponent<SphereCollider>();
                sphere.center = bounds.center;
                sphere.radius = sphereRadius.Value;
            }
            else
            {
                var box = grip.AddComponent<BoxCollider>(); box.center = bounds.center;
                box.size = Vector3.Max(bounds.size, Vector3.one * minimum);
            }
            control.GetComponent<ControlSpec>().colliderGameObjects = new[] { grip };
            var highlight = control.GetComponent<HighlightTag>() ?? control.gameObject.AddComponent<HighlightTag>();
            highlight.renderers = renderers;
        }

        private static void PrepareCut(TrainCar car, Transform interior, S282InjectorData.Mask mask, S282InjectorLifetime lifetime)
        {
            var filter = interior.Find(mask.Path)?.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null || filter.sharedMesh.name != mask.MeshName)
                throw new InvalidDataException("Stock injector fitting mesh changed: " + mask.Path);
            var readable = filter.sharedMesh.isReadable ? filter.sharedMesh : car.GetComponentsInChildren<MeshFilter>(true)
                .Select(f => f.sharedMesh).FirstOrDefault(m => m != null && m.isReadable && m.name == mask.MeshName
                    && m.vertexCount == mask.VertexCount && InjectorModelMesh.GeometryHash(m) == mask.GeometryHash);
            if (readable == null || InjectorModelMesh.GeometryHash(readable) != mask.GeometryHash)
                throw new InvalidDataException("Stock fitting geometry differs; keeping its original cab");
            var cut = InjectorModelMesh.Cut(readable, mask);
            lifetime.Meshes.Add(cut);
            lifetime.Cuts.Add((filter, filter.sharedMesh, cut));
        }

        private static Vector3 V(float[] v) => new Vector3(v[0], v[1], v[2]);
    }

    /// <summary>Retains a continuous water wheel after native scrolling or dragging.</summary>
    internal sealed class S282ContinuousWaterWheel : MonoBehaviour
    {
        internal InteractablePortFeeder Original;
        internal TrainCar Car;
        internal S282InjectorLifetime Lifetime;
        private Rotary spec;
        private HingeJointAngleFix angle;
        private DV.Interaction.GrabHandlerHingeJoint grab;

        private IEnumerator Start()
        {
            var binder = GetComponent<SimControlBinder>();
            var manager = Original.GetComponentInParent<InteriorControlsManager>();
            while (!binder.Bound || manager != null && !manager.Initialized)
                yield return null;
            // The native feeder and keyboard implementations also finish Start.
            yield return null;
            yield return null;
            var keyboard = Original.GetComponentInParent<InteractablesKeyboardControl>();
            var reader = Original.GetComponentInParent<LocoControlsReader>();
            var copies = new Dictionary<AKeyboardInput, AKeyboardInput>();
            foreach (var input in Original.GetComponents<AKeyboardInput>())
            {
                var copy = (AKeyboardInput)gameObject.AddComponent(input.GetType());
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(input), copy);
                copies.Add(input, copy);
            }
            yield return null;
            try
            {
                TransferInput(manager, keyboard, reader, copies);
            }
            catch (Exception e)
            {
                Main.Mod.Logger.Warning("S282 injector control transfer failed; keeping stock: " + e.Message);
                Lifetime.Clean();
                Object.Destroy(Lifetime.gameObject);
            }
        }

        private void TransferInput(InteriorControlsManager manager, InteractablesKeyboardControl keyboard,
            LocoControlsReader reader, Dictionary<AKeyboardInput, AKeyboardInput> copies)
        {
            if (manager == null || keyboard == null || reader == null || reader.injector != Original.gameObject)
                throw new InvalidDataException("Stock injector input references changed");
            var oldEntries = keyboard.entries;
            if (!Car.SimController.SimulationFlow.TryGetPort(Original.portId, out var port))
                throw new InvalidDataException("Missing injector water port");
            var blocker = Car.SimController.controlsBlocker?.GetBlockDefinition(Original.portId);
            var originalControl = Original.GetComponent<ControlImplBase>();
            var originalBody = Original.GetComponent<Rigidbody>();
            var originalSteps = Original.GetComponent<SteppedJoint>();
            if (originalControl == null || originalBody == null)
                throw new InvalidDataException("Stock injector implementation did not initialize");
            var wasEnabled = originalControl.enabled;
            var wasKinematic = originalBody.isKinematic;
            var stepsEnabled = originalSteps != null && originalSteps.enabled;
            // Only one physical control owns the water port. Keeping a hidden
            // stepped control alive lets its detents feed back into this wheel.
            Lifetime.RestoreInputs = () =>
            {
                keyboard.entries = oldEntries;
                reader.injector = Original.gameObject;
                manager.SetupControl(Original.gameObject, InteriorControlsManager.ControlType.Injector);
                originalBody.isKinematic = wasKinematic;
                if (originalSteps != null) originalSteps.enabled = stepsEnabled;
                originalControl.enabled = wasEnabled;
            };
            foreach (var copy in copies.Values) copy.SetupActions(manager);
            keyboard.entries = oldEntries.Select(i => copies.TryGetValue(i, out var copy) ? copy : i).ToArray();
            reader.injector = gameObject;
            manager.SetupControl(gameObject, InteriorControlsManager.ControlType.Injector);
            var feeder = GetComponent<InteractablePortFeeder>();
            feeder.Deinit();
            feeder.Init(port, blocker);
            feeder.SetupControlChangedListeners();
            // Keep its existing feeder for cached AI/network SetValue references,
            // but stop its physical updates from sending detents back to the port.
            // Do not disable the whole object: its hinge angle reference resets
            // on reactivation and would break restoration after a cab rebuild.
            originalControl.ForceEndInteraction();
            originalControl.enabled = false;
            if (originalSteps != null) originalSteps.enabled = false;
            originalBody.angularVelocity = Vector3.zero;
            originalBody.isKinematic = true;
        }

        internal void RememberPosition()
        {
            if (spec == null) spec = GetComponent<Rotary>();
            if (angle == null) angle = GetComponent<HingeJointAngleFix>();
            // Torque scrolling moves the wheel, not its spring target. The
            // native release must hold the actual angle rather than the old target.
            if (spec != null && angle != null)
                spec.jointStartingPos = angle.Angle;
        }

        private void LateUpdate()
        {
            if (grab == null) grab = GetComponent<DV.Interaction.GrabHandlerHingeJoint>();
            if (grab != null) grab.resetTargetPositionOnUngrab = false;
        }
    }

    // An unstepped stock rotary otherwise resets its spring target to the
    // initial angle on mouse-wheel release. Remember the live target instead,
    // preserving any intermediate setting without percentage quantization.
    internal sealed class S282ContinuousValve : MonoBehaviour
    {
        private HingeJoint joint;
        private Rotary spec;
        private DV.Interaction.GrabHandlerHingeJoint grab;
        private void Awake() => spec = GetComponent<Rotary>();
        private void LateUpdate()
        {
            if (joint == null) joint = GetComponent<HingeJoint>();
            if (grab == null) grab = GetComponent<DV.Interaction.GrabHandlerHingeJoint>();
            if (grab != null) grab.resetTargetPositionOnUngrab = false;
            if (joint != null && spec != null) spec.jointStartingPos = joint.spring.targetPosition;
        }
    }

}
