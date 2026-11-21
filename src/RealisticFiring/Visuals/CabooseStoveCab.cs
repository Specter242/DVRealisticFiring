using System;
using System.Collections.Generic;
using System.Linq;
using DV.CabControls;
using DV.Interaction;
using DV.Simulation.Ports;
using HarmonyLib;
using RealisticFiring.Core;
using RealisticFiring.Items;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Visuals
{
    /// <summary>
    /// In-cab setup and visuals for a caboose's wood stove: discovers the
    /// caboose's <c>Stove</c> and <c>StoveDoor</c> objects with safe fallbacks,
    /// wires hover status and fuel/lighter intake (<see cref="CabooseStoveTarget"/>),
    /// renders laid firewood splits, glowing embers, warm firelight, subtle flames,
    /// and a gentle spatial wood-fire crackle.
    /// </summary>
    internal sealed class CabooseStoveCab : MonoBehaviour
    {
        private const int VisibleLogs = 6;
        private const float CrackleMaxVolume = 0.22f;

        private static readonly AccessTools.FieldRef<ItemUseRedirect, ItemUseTarget> RedirectTarget =
            AccessTools.FieldRefAccess<ItemUseRedirect, ItemUseTarget>("target");

        // Fallback chamber placement in Stove-local coordinates on CarCabooseRed
        // (Stove sits at car-local (-0.629, 1.429, 0.770), doors face +X).
        private static readonly Vector3 DefaultChamberLocal = new Vector3(-0.15f, 0.16f, -0.18f);
        private static readonly Vector3 DefaultChamberSize = new Vector3(0.32f, 0.44f, 0.40f);

        private CabooseStoveRig rig;
        private Transform interior;
        private bool interiorScanned;
        private Transform stoveRoot;
        private Transform visualRoot;
        private CabooseStoveTarget target;
        private readonly List<ControlImplBase> doorControls = new List<ControlImplBase>();
        private readonly List<Transform> fallbackDoorTransforms = new List<Transform>();
        private readonly List<Quaternion> fallbackClosedRotations = new List<Quaternion>();
        private readonly List<GameObject> addedRedirectObjects = new List<GameObject>();

        private Transform[] logs;
        private Renderer[][] logRenderers;
        private Transform ragVisual;
        private Renderer[] ragRenderers;
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();

        private Light fireLight;
        private Renderer emberBed;
        private Material emberMaterial;
        private ParticleSystem flames;
        private AudioSource crackle;
        private float seed;
        private bool loggedMissing;

        private static AudioClip sharedCrackle;

        internal bool HasStockDoors => doorControls.Count > 0;

        /// <summary>Discovered stove root in car-local space, when the interior has loaded.</summary>
        internal Vector3? StoveLocalPosition { get; private set; }

        internal void Bind(CabooseStoveRig owner)
        {
            rig = owner;
            seed = UnityEngine.Random.value * 100f;
        }

        internal float ReadDoorControlOpening()
        {
            var max = 0f;
            for (var i = 0; i < doorControls.Count; i++)
            {
                var ctrl = doorControls[i];
                if (ctrl != null)
                    max = Mathf.Max(max, CabooseStoveRig.StockDoorOpening(ctrl.Value));
            }
            return max;
        }

        internal void Tick(CabooseStoveRig owner)
        {
            rig = owner;
            if (rig == null || rig.Car == null)
                return;
            RefreshInterior();
            if (stoveRoot == null || visualRoot == null)
                return;
            UpdateFallbackDoors();
            UpdateVisuals();
        }

        internal void Clear()
        {
            if (target != null)
            {
                Destroy(target.gameObject);
                target = null;
            }
            if (visualRoot != null)
            {
                Destroy(visualRoot.gameObject);
                visualRoot = null;
            }
            if (emberMaterial != null)
            {
                Destroy(emberMaterial);
                emberMaterial = null;
            }
            foreach (var go in addedRedirectObjects)
                if (go != null) Destroy(go);
            addedRedirectObjects.Clear();
            doorControls.Clear();
            fallbackDoorTransforms.Clear();
            fallbackClosedRotations.Clear();
            logs = null;
            logRenderers = null;
            ragVisual = null;
            ragRenderers = null;
            fireLight = null;
            emberBed = null;
            flames = null;
            crackle = null;
            stoveRoot = null;
            interior = null;
            interiorScanned = false;
        }

        private void OnDestroy() => Clear();

        private void RefreshInterior()
        {
            var loaded = rig.Car.loadedInterior != null ? rig.Car.loadedInterior.transform : null;
            if (ReferenceEquals(loaded, interior) && (loaded != null ? interiorScanned : !interiorScanned))
                return;
            Clear();
            interior = loaded;
            if (loaded == null)
                return;
            interiorScanned = true;

            stoveRoot = FindStoveRoot(loaded);
            if (stoveRoot == null)
            {
                if (!loggedMissing)
                {
                    loggedMissing = true;
                    Main.Mod?.Logger.Log($"{rig.Car.ID}: no Stove mesh found in caboose interior; skipping caboose stove interior setup");
                }
                return;
            }

            StoveLocalPosition = rig.Car.transform.InverseTransformPoint(stoveRoot.position);
            DiscoverDoors(loaded, stoveRoot, out var chamberLocal, out var chamberSize);
            BuildIntake(stoveRoot, chamberLocal, chamberSize);
            BuildVisuals(stoveRoot, chamberLocal);
        }

        private static Transform FindStoveRoot(Transform loaded)
        {
            // Exact stock name first: CarCabooseRed_interior/Stove.
            var exact = loaded.Find("Stove");
            if (exact != null)
                return exact;
            // Case-insensitive search for "Stove" (excluding unrelated items like ElectricStove).
            foreach (var t in loaded.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                if (string.Equals(t.name, "Stove", StringComparison.OrdinalIgnoreCase))
                    return t;
            }
            // Fallback: find any StoveDoor* mesh/control and use its parent.
            foreach (var t in loaded.GetComponentsInChildren<Transform>(true))
            {
                if (t != null && MatchesStoveDoorName(t.name) && t.parent != null)
                    return t.parent;
            }
            return null;
        }

        private static bool MatchesStoveDoorName(string name) =>
            !string.IsNullOrEmpty(name)
            && (name.IndexOf("StoveDoor", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("stove_door", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("stove door", StringComparison.OrdinalIgnoreCase) >= 0);

        private static bool IsStoveDoorControl(ControlImplBase ctrl, Transform stoveRoot)
        {
            if (ctrl == null)
                return false;
            if (stoveRoot != null && ctrl.transform.IsChildOf(stoveRoot))
                return true;
            if (MatchesStoveDoorName(ctrl.name))
                return true;
            if (ctrl.transform.parent != null && MatchesStoveDoorName(ctrl.transform.parent.name))
                return true;
            var feeder = ctrl.GetComponent<InteractablePortFeeder>();
            if (feeder != null && MatchesStoveDoorName(feeder.portId))
                return true;
            return ctrl.GetComponentsInChildren<Transform>(true).Any(t => t != null && MatchesStoveDoorName(t.name));
        }

        internal static bool IsDrivenByStockControl(Transform t) =>
            t != null
            && (t.GetComponentInParent<ControlImplBase>() != null
                || t.GetComponentInChildren<ControlImplBase>(true) != null
                || t.GetComponentInParent<DV.CabControls.Spec.ControlSpec>() != null
                || t.GetComponentInChildren<DV.CabControls.Spec.ControlSpec>(true) != null
                || t.GetComponentInParent<InteractablePortFeeder>() != null
                || t.GetComponentInChildren<InteractablePortFeeder>(true) != null
                || t.GetComponentInParent<AGrabHandler>() != null
                || t.GetComponentInChildren<AGrabHandler>(true) != null);

        private void DiscoverDoors(Transform interiorRoot, Transform root, out Vector3 chamberLocal, out Vector3 chamberSize)
        {
            doorControls.Clear();
            fallbackDoorTransforms.Clear();
            fallbackClosedRotations.Clear();

            var discoveredPorts = new List<string>();
            var searchRoot = interiorRoot != null ? interiorRoot : root;

            // Search the entire loadedInterior for stock stove door controls so levers
            // sitting beside Stove (e.g. under [interactables]) are bound directly instead
            // of spawning a competing fallback door.
            foreach (var ctrl in searchRoot.GetComponentsInChildren<ControlImplBase>(true))
            {
                if (!IsStoveDoorControl(ctrl, root) || doorControls.Contains(ctrl))
                    continue;
                doorControls.Add(ctrl);
                AttachHover(ctrl.gameObject);
                var feeder = ctrl.GetComponent<InteractablePortFeeder>();
                if (feeder != null && !string.IsNullOrEmpty(feeder.portId))
                    discoveredPorts.Add(feeder.portId);
            }

            foreach (var feeder in searchRoot.GetComponentsInChildren<InteractablePortFeeder>(true))
            {
                if (feeder == null || !MatchesStoveDoorName(feeder.portId))
                    continue;
                discoveredPorts.Add(feeder.portId);
                var ctrl = feeder.GetComponent<ControlImplBase>();
                if (ctrl == null) ctrl = feeder.GetComponentInParent<ControlImplBase>();
                if (ctrl == null) ctrl = feeder.GetComponentInChildren<ControlImplBase>(true);
                if (ctrl != null && !doorControls.Contains(ctrl))
                {
                    doorControls.Add(ctrl);
                    AttachHover(ctrl.gameObject);
                }
            }

            if (discoveredPorts.Count > 0 && rig != null)
                rig.RegisterDiscoveredDoorPorts(discoveredPorts);

            // Fallback: static StoveDoor* transforms on a custom caboose without stock levers/ports.
            if (doorControls.Count == 0 && (rig == null || !rig.HasDoorPorts))
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t != null && t != root && MatchesStoveDoorName(t.name) && !IsDrivenByStockControl(t))
                    {
                        fallbackDoorTransforms.Add(t);
                        fallbackClosedRotations.Add(t.localRotation);
                    }
                }
            }

            // Estimate chamber bounds from discovered door meshes when present,
            // falling back cleanly to the CarCabooseRed measurements.
            var hasBounds = false;
            var localBounds = new Bounds();
            void EncapsulateRenderer(MeshRenderer r)
            {
                if (r == null) return;
                var lb = CarSpace.LocalBounds(root, r);
                if (!hasBounds) { localBounds = lb; hasBounds = true; }
                else localBounds.Encapsulate(lb);
            }

            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r != null && MatchesStoveDoorName(r.name))
                    EncapsulateRenderer(r);
            }
            if (!hasBounds)
            {
                foreach (var ctrl in doorControls)
                {
                    if (ctrl == null) continue;
                    foreach (var r in ctrl.GetComponentsInChildren<MeshRenderer>(true))
                        EncapsulateRenderer(r);
                }
            }

            if (hasBounds && localBounds.size.y > 0.05f && localBounds.size.z > 0.05f)
            {
                chamberLocal = new Vector3(localBounds.min.x - 0.14f, localBounds.center.y, localBounds.center.z);
                chamberSize = new Vector3(
                    Mathf.Clamp(0.32f, 0.22f, 0.45f),
                    Mathf.Clamp(localBounds.size.y, 0.25f, 0.65f),
                    Mathf.Clamp(localBounds.size.z, 0.25f, 0.60f));
            }
            else
            {
                chamberLocal = DefaultChamberLocal;
                chamberSize = DefaultChamberSize;
            }
        }

        private void AttachHover(GameObject host)
        {
            if (host == null || host.GetComponent<HoverName>() != null)
                return;
            host.AddComponent<HoverName>().Source = () =>
                rig != null && rig.FeatureEnabled ? rig.Model.StatusText(LighterRule.LightsCoal) : null;
        }

        /// <summary>
        /// Attaches an <see cref="ItemUseRedirect"/> on an inactive GameObject and sets its
        /// target before activation so stock <c>ItemUseRedirect.Awake()</c> never self-destructs.
        /// </summary>
        internal static ItemUseRedirect AttachRedirect(GameObject inactiveObject, ItemUseTarget useTarget)
        {
            var redirect = inactiveObject.GetComponent<ItemUseRedirect>();
            if (redirect == null)
                redirect = inactiveObject.AddComponent<ItemUseRedirect>();
            RedirectTarget(redirect) = useTarget;
            return redirect;
        }

        private GameObject BuildDoorRedirect(ControlImplBase ctrl, int useLayer, ItemUseTarget useTarget, List<Collider> colliders)
        {
            var existing = ctrl.GetComponent<ItemUseRedirect>();
            if (existing != null)
            {
                if (existing.Target == null)
                    RedirectTarget(existing) = useTarget;
                if (existing.Target == useTarget)
                {
                    foreach (var col in ctrl.GetComponentsInChildren<Collider>(true))
                    {
                        if (col != null && !colliders.Contains(col))
                            colliders.Add(col);
                    }
                }
                return null;
            }

            var redirectGo = new GameObject("[RealisticFiring stove door redirect]");
            redirectGo.SetActive(false);
            redirectGo.layer = useLayer;
            redirectGo.transform.SetParent(ctrl.transform, false);
            redirectGo.transform.localPosition = Vector3.zero;
            redirectGo.transform.localRotation = Quaternion.identity;
            redirectGo.transform.localScale = Vector3.one;

            var hasBounds = false;
            var bounds = new Bounds();
            foreach (var col in ctrl.GetComponentsInChildren<Collider>(true))
            {
                if (col == null) continue;
                var local = CarSpace.LocalBounds(ctrl.transform, col);
                if (!hasBounds) { bounds = local; hasBounds = true; }
                else bounds.Encapsulate(local);
            }

            var trigger = redirectGo.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            if (hasBounds && bounds.size.sqrMagnitude > 1e-5f)
            {
                trigger.center = bounds.center;
                trigger.size = bounds.size;
            }
            else
            {
                trigger.center = Vector3.zero;
                trigger.size = new Vector3(0.22f, 0.22f, 0.08f);
            }

            AttachRedirect(redirectGo, useTarget);
            colliders.Add(trigger);
            redirectGo.SetActive(true);
            return redirectGo;
        }

        private void BuildIntake(Transform root, Vector3 chamberLocal, Vector3 chamberSize)
        {
            var pileLayer = LayerMask.NameToLayer("Train_Interior");
            if (pileLayer < 0) pileLayer = root.gameObject.layer;
            var useLayer = LayerMask.NameToLayer("Interactable");
            if (useLayer < 0) useLayer = pileLayer;

            // Build the entire intake hierarchy inactive so ItemUseRedirect.Awake() and
            // ControlSpec.Awake() only run once targets, colliders, and specs are assigned.
            var intakeGo = new GameObject("[RealisticFiring caboose stove intake]");
            intakeGo.SetActive(false);
            intakeGo.layer = pileLayer;
            intakeGo.transform.SetParent(root, false);
            intakeGo.transform.localPosition = chamberLocal;
            intakeGo.transform.localRotation = Quaternion.identity;

            var chamberTrigger = intakeGo.AddComponent<BoxCollider>();
            chamberTrigger.isTrigger = true;
            chamberTrigger.size = chamberSize;

            var ignitionSphere = intakeGo.AddComponent<SphereCollider>();
            ignitionSphere.isTrigger = true;
            ignitionSphere.radius = 0.20f;
            ignitionSphere.center = new Vector3(chamberSize.x * 0.35f, 0f, 0f);

            var useTarget = intakeGo.AddComponent<ItemUseTarget>();
            var colliders = new List<Collider> { chamberTrigger, ignitionSphere };

            // Interactable redirect recessed inside the stove mouth so it only intercepts
            // rays when a stove door is open (closed doors sit in front and stay clickable).
            var interactable = new GameObject("interactable");
            interactable.layer = useLayer;
            interactable.transform.SetParent(intakeGo.transform, false);
            var useBox = interactable.AddComponent<BoxCollider>();
            useBox.isTrigger = true;
            useBox.size = chamberSize;
            AttachRedirect(interactable, useTarget);
            AttachHover(interactable);
            colliders.Add(useBox);

            // Also redirect item-use rays hitting the stock stove doors via an inactive child
            // so hovering a shut stove door with firewood or a rag tells the player to open it.
            foreach (var ctrl in doorControls)
            {
                if (ctrl == null) continue;
                var doorRedirect = BuildDoorRedirect(ctrl, useLayer, useTarget, colliders);
                if (doorRedirect != null)
                    addedRedirectObjects.Add(doorRedirect);
            }

            // Fallback clickable door button when a custom caboose has no stock door levers/ports.
            if (doorControls.Count == 0 && (rig == null || !rig.HasDoorPorts))
            {
                var fallback = new GameObject("[RealisticFiring stove fallback door]");
                fallback.layer = useLayer;
                fallback.transform.SetParent(intakeGo.transform, false);
                var fbCol = fallback.AddComponent<BoxCollider>();
                fbCol.isTrigger = true;
                fbCol.center = new Vector3(chamberSize.x * 0.5f, 0f, 0f);
                fbCol.size = new Vector3(0.08f, chamberSize.y, chamberSize.z);
                AttachRedirect(fallback, useTarget);
                AttachHover(fallback);
                var buttonSpec = fallback.AddComponent<DV.CabControls.Spec.Button>();
                CabooseStoveFallbackDoor.ConfigureButton(buttonSpec, fallback);
                fallback.AddComponent<CabooseStoveFallbackDoor>().Rig = rig;
                colliders.Add(fbCol);
            }

            useTarget.targetColliders = colliders.ToArray();

            var pose = new GameObject(InsertItemIntoTargetHandler.ANIMATION_POSE_TARGET).transform;
            pose.SetParent(intakeGo.transform, false);
            pose.localPosition = new Vector3(chamberSize.x * 0.45f, 0f, 0f);

            target = intakeGo.AddComponent<CabooseStoveTarget>();
            target.Initialize(rig, useTarget, ignitionSphere, chamberSize);
            intakeGo.SetActive(true);
        }

        private void BuildVisuals(Transform root, Vector3 chamberLocal)
        {
            visualRoot = new GameObject("[RealisticFiring caboose stove visuals]").transform;
            visualRoot.SetParent(root, false);
            visualRoot.localPosition = chamberLocal;
            visualRoot.localRotation = Quaternion.identity;

            // Warm firelight just at the stove mouth facing into the caboose (+X).
            var lightGo = new GameObject("stove firelight");
            lightGo.transform.SetParent(visualRoot, false);
            lightGo.transform.localPosition = new Vector3(0.18f, 0.02f, 0f);
            fireLight = lightGo.AddComponent<Light>();
            fireLight.type = LightType.Point;
            fireLight.color = new Color(1f, 0.48f, 0.14f);
            fireLight.range = 3.6f;
            fireLight.intensity = 0f;
            fireLight.shadows = LightShadows.None;
            fireLight.enabled = false;

            // Subtle emissive ember bed resting on the stove firebox floor.
            var bedGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bedGo.name = "stove ember bed";
            var bedCol = bedGo.GetComponent<Collider>();
            if (bedCol != null) Destroy(bedCol);
            bedGo.transform.SetParent(visualRoot, false);
            bedGo.transform.localPosition = new Vector3(-0.02f, -0.10f, 0f);
            bedGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            bedGo.transform.localScale = new Vector3(0.20f, 0.24f, 1f);
            emberBed = bedGo.GetComponent<MeshRenderer>();
            var shader = Shader.Find("Standard");
            if (shader != null)
            {
                emberMaterial = new Material(shader) { name = "RF Caboose Ember Bed", color = new Color(0.08f, 0.06f, 0.05f) };
                emberMaterial.SetFloat("_Metallic", 0f);
                emberMaterial.SetFloat("_Glossiness", 0.05f);
                emberMaterial.EnableKeyword("_EMISSION");
                emberMaterial.SetColor("_EmissionColor", Color.black);
                emberBed.sharedMaterial = emberMaterial;
            }
            emberBed.enabled = false;

            // Subtle flame particle system inside the stove chamber.
            flames = StartingFuelBody.FlameSystem(visualRoot, "caboose stove flames");
            if (flames != null)
            {
                flames.transform.localPosition = new Vector3(0f, -0.04f, 0f);
                flames.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                var main = flames.main;
                main.startSize3D = true;
                main.startSizeX = new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
                main.startSizeY = new ParticleSystem.MinMaxCurve(0.09f, 0.18f);
                main.startSizeZ = main.startSizeX;
                main.maxParticles = 48;
                var shape = flames.shape;
                shape.scale = new Vector3(0.14f, 0.16f, 0.03f);
            }

            // Gentle spatial wood-fire crackle audio.
            var audioGo = new GameObject("stove crackle");
            audioGo.transform.SetParent(visualRoot, false);
            audioGo.transform.localPosition = new Vector3(0.10f, 0f, 0f);
            crackle = audioGo.AddComponent<AudioSource>();
            crackle.clip = CrackleClip();
            crackle.loop = true;
            crackle.playOnAwake = false;
            crackle.spatialBlend = 1f;
            crackle.rolloffMode = AudioRolloffMode.Linear;
            crackle.minDistance = 0.6f;
            crackle.maxDistance = 5.5f;
            crackle.volume = 0f;
        }

        private void UpdateFallbackDoors()
        {
            if (HasStockDoors || (rig != null && rig.HasDoorPorts) || fallbackDoorTransforms.Count == 0)
                return;
            var angle = rig.FallbackDoorOpen ? -85f : 0f;
            for (var i = 0; i < fallbackDoorTransforms.Count; i++)
            {
                var t = fallbackDoorTransforms[i];
                if (t != null && !IsDrivenByStockControl(t))
                    t.localRotation = fallbackClosedRotations[i] * Quaternion.Euler(0f, angle, 0f);
            }
        }

        private void UpdateVisuals()
        {
            var model = rig.Model;
            var starter = model.Fuel;
            var doorOpen = model.DoorOpen;

            // Firelight: gentle ember glow leaking out when shut, cozy bright firelight when open.
            if (fireLight != null)
            {
                var baseGlow = Mathf.Max(model.Heat01, model.Flame01 * 0.85f);
                if (baseGlow > 0.01f)
                {
                    var flicker = 0.88f + 0.24f * Mathf.PerlinNoise(Time.time * 5.2f, seed);
                    var openFactor = Mathf.Lerp(0.28f, 1f, model.DoorOpening);
                    fireLight.intensity = 1.45f * baseGlow * openFactor * flicker;
                    fireLight.range = Mathf.Lerp(2.2f, 4.4f, model.DoorOpening);
                    fireLight.color = Color.Lerp(new Color(0.95f, 0.30f, 0.06f), new Color(1f, 0.56f, 0.18f), model.Flame01);
                    fireLight.enabled = true;
                }
                else
                {
                    fireLight.enabled = false;
                }
            }

            // Emissive ember bed inside the stove.
            if (emberBed != null && emberMaterial != null)
            {
                var ember = Mathf.Max(model.Ember01, model.Heat01 * 0.7f);
                var showBed = ember > 0.02f || model.FuelKg > 0.01f;
                emberBed.enabled = showBed;
                if (showBed)
                {
                    var pulse = 0.90f + 0.20f * Mathf.PerlinNoise(Time.time * 2.1f, seed + 13f);
                    var color = FireColors.At(Mathf.Clamp01(ember * 0.65f)) * (0.45f * ember * pulse);
                    emberMaterial.SetColor("_EmissionColor", color);
                }
            }

            // Subtle flames when burning and door is open.
            if (flames != null)
            {
                var emission = flames.emission;
                var rate = doorOpen && model.Flame01 > 0.02f ? Mathf.Lerp(8f, 26f, model.Flame01) : 0f;
                emission.rateOverTime = rate;
            }

            // Laid firewood splits & rag inside the stove chamber.
            UpdateFuelMeshes(model, starter);

            // Subtle spatial wood-fire crackle.
            if (crackle != null)
            {
                var audible = model.Lit && (model.Heat01 > 0.03f || model.Flame01 > 0.05f);
                if (audible)
                {
                    var openBoost = Mathf.Lerp(0.55f, 1f, model.DoorOpening);
                    crackle.volume = CrackleMaxVolume * Mathf.Max(model.Flame01, model.Heat01 * 0.6f) * openBoost;
                    if (!crackle.isPlaying) crackle.Play();
                }
                else if (crackle.isPlaying)
                {
                    crackle.Stop();
                }
            }
        }

        private void UpdateFuelMeshes(CabooseStoveModel model, StarterFuel starter)
        {
            var pieces = (starter.Wood + starter.Char * 2.2f) / StarterFuel.PieceMass;
            if (pieces > 0.02f && logs == null)
            {
                logs = new Transform[VisibleLogs];
                logRenderers = new Renderer[VisibleLogs][];
            }
            if (logs != null)
            {
                for (var i = 0; i < VisibleLogs; i++)
                {
                    var share = Mathf.Clamp01(pieces - i);
                    if (share <= 0.02f)
                    {
                        if (logs[i] != null) logs[i].gameObject.SetActive(false);
                        continue;
                    }
                    if (logs[i] == null)
                    {
                        logs[i] = StartingFuelBody.Build(visualRoot, StartingFuelKind.Wood, i % StartingFuelBody.WoodVariants, inFirebox: false);
                        logRenderers[i] = logs[i].GetComponentsInChildren<Renderer>(true);
                    }
                    var log = logs[i];
                    log.gameObject.SetActive(true);
                    var level = i / 2;
                    var side = i % 2 == 0 ? -1f : 1f;
                    log.localPosition = new Vector3(-0.02f + (level % 2 == 0 ? 0f : side * 0.035f), -0.07f + level * 0.042f, level % 2 == 0 ? side * 0.055f : 0f);
                    log.localRotation = Quaternion.Euler(0f, level % 2 == 0 ? 90f : 12f * side, i * 27f % 45f - 22f);
                    var scale = 0.62f * Mathf.Lerp(0.5f, 1f, Mathf.Sqrt(share) * (1f - 0.35f * starter.CharFraction));
                    log.localScale = Vector3.one * scale;
                    var charred = Mathf.Clamp01(starter.SurfaceChar + 0.6f * starter.CharFraction);
                    var glow = Mathf.Clamp01(model.Ember01 * 1.4f + 0.15f * model.Flame01) * (starter.Burning ? 1f : 0f);
                    var scorch = Mathf.Clamp01((starter.FrontTemperature - 120f) / 250f);
                    StartingFuelBody.SetState(logRenderers[i], block, charred, glow, scorch, 0f, (i * 0.618f) % 1f);
                }
            }

            var showRag = starter.RagOil > 0f || starter.RagCount > 0;
            if (showRag)
            {
                if (ragVisual == null)
                {
                    ragVisual = StartingFuelBody.Build(visualRoot, StartingFuelKind.Rag, 0, inFirebox: false);
                    ragRenderers = ragVisual.GetComponentsInChildren<Renderer>(true);
                }
                ragVisual.gameObject.SetActive(true);
                ragVisual.localPosition = new Vector3(0.01f, -0.02f + Mathf.Min(2f, Mathf.Ceil(pieces / 2f)) * 0.04f, 0f);
                ragVisual.localScale = Vector3.one * 0.72f;
                var burnt = 1f - starter.RagOil / Mathf.Max(1e-4f, starter.RagCount * RagFuel.OilCapacity);
                StartingFuelBody.SetState(ragRenderers, block, Mathf.Clamp01(burnt * 1.2f), starter.RagLit ? 0.5f : 0f, starter.RagLit ? 1f : 0f, starter.RagOil > 0f ? 1f : 0f, 0.37f);
            }
            else if (ragVisual != null)
            {
                ragVisual.gameObject.SetActive(false);
            }
        }

        // Synthesized cozy wood-fire crackle loop (no external audio assets).
        private static AudioClip CrackleClip()
        {
            if (sharedCrackle != null)
                return sharedCrackle;
            const int rate = 22050;
            const float seconds = 3f;
            var samples = new float[(int)(rate * seconds)];
            var rng = new System.Random(1755);
            var low = 0f;
            var pop = 0f;
            for (var i = 0; i < samples.Length; i++)
            {
                var white = (float)(rng.NextDouble() * 2.0 - 1.0);
                // Soft low hearth rumble.
                low = low * 0.96f + white * 0.04f;
                // Occasional gentle wood pops.
                if (rng.NextDouble() < 0.00055)
                    pop = (float)(0.25 + 0.55 * rng.NextDouble()) * (rng.Next(2) == 0 ? 1f : -1f);
                pop *= 0.92f;
                var v = low * 0.55f + pop * 0.45f;
                // Seamless fade at loop edges.
                var edge = Mathf.Min(1f, Mathf.Min(i, samples.Length - 1 - i) / 400f);
                samples[i] = Mathf.Clamp(v * edge * 0.45f, -1f, 1f);
            }
            sharedCrackle = AudioClip.Create("RF Caboose Stove Crackle", samples.Length, 1, rate, false);
            sharedCrackle.SetData(samples, 0);
            return sharedCrackle;
        }
    }

    /// <summary>
    /// Fallback clickable door control for custom cabooses whose stove has no
    /// stock <see cref="ControlImplBase"/> door levers. Uses a stock
    /// <see cref="DV.CabControls.Spec.Button"/> so desktop clicks (via
    /// <see cref="GrabHandlerButton"/>) and VR presses respect all stock input gates.
    /// </summary>
    internal sealed class CabooseStoveFallbackDoor : MonoBehaviour
    {
        internal CabooseStoveRig Rig;
        private ControlImplBase control;

        internal static void ConfigureButton(DV.CabControls.Spec.Button spec, GameObject collider)
        {
            spec.handPosesOverride = new InteractionHandPoses();
            spec.colliderGameObjects = new[] { collider };
            spec.createRigidbody = false;
            spec.useJoints = false;
            spec.isToggle = false;
            spec.isTogglingBack = false;
        }

        private void Start()
        {
            BindControl(GetComponent<ControlImplBase>());
        }

        internal void BindControl(ControlImplBase ctrl)
        {
            if (ReferenceEquals(control, ctrl))
                return;
            if (control != null)
                control.Used -= Toggle;
            control = ctrl;
            if (control != null)
                control.Used += Toggle;
        }

        private void OnDestroy()
        {
            if (control != null)
            {
                control.Used -= Toggle;
                control = null;
            }
        }

        internal void Toggle()
        {
            if (Rig == null || !Rig.FeatureEnabled) return;
            Rig.ToggleFallbackDoorFromPlayer();
        }
    }

    /// <summary>
    /// The caboose stove's fuel intake and lighter ignition target.
    /// Handles laying wood/rags (<see cref="StartingFuelPlace"/>), throwing
    /// wood/rags (<see cref="StartingFuelItem"/> / <see cref="StartingFuelThrow"/>),
    /// and direct lighter ignition (<see cref="IIgnitable"/>).
    /// </summary>
    internal sealed class CabooseStoveTarget : MonoBehaviour, IIgnitable, IInteractionPointProvider
    {
        private static readonly List<CabooseStoveTarget> active = new List<CabooseStoveTarget>();
        private const float Reach = 8f;

        private CabooseStoveRig rig;
        private ItemUseTarget useTarget;
        private SphereCollider ignitionCollider;
        private Vector3 chamberSize;

        internal CabooseStoveRig Rig => rig;
        internal bool DoorOpen => rig != null && rig.FeatureEnabled && rig.Model.DoorOpen;

        public bool Ignited => rig != null && rig.Model.Lit;

        public bool IgnitionAllowed =>
            rig != null && rig.FeatureEnabled && rig.Model.DoorOpen && !rig.Model.Lit
            && (rig.Model.RagOil > 0f || (LighterRule.LightsCoal && rig.Model.WoodKg > 0f))
            && !LevelInfo.IsUnderWater(transform.position);

        public SphereCollider OverlapInteractionCollider => ignitionCollider;
        public Transform InteractionPoint => transform;
        public Transform GetTransform() => transform;

        public bool Ignite(float ignitionStrength)
        {
            if (!IgnitionAllowed)
                return false;
            return rig.TryIgniteWithLighter();
        }

        internal void Initialize(CabooseStoveRig owner, ItemUseTarget target, SphereCollider sphere, Vector3 size)
        {
            rig = owner;
            useTarget = target;
            ignitionCollider = sphere;
            chamberSize = size;
        }

        private void OnEnable()
        {
            if (!active.Contains(this))
                active.Add(this);
        }

        private void OnDisable() => active.Remove(this);

        internal static bool ForTarget(ItemUseTarget candidate, out CabooseStoveTarget stove)
        {
            stove = null;
            if (candidate == null) return false;
            for (var i = 0; i < active.Count; i++)
            {
                var t = active[i];
                if (t != null && t.rig != null && t.rig.FeatureEnabled && t.useTarget == candidate)
                {
                    stove = t;
                    return true;
                }
            }
            return false;
        }

        internal bool CanTake(StartingFuelItem item) =>
            item != null && rig != null && rig.CanTake(item.Kind);

        internal string RefusalFor(StartingFuelItem item) =>
            item == null || rig == null ? "No stove here" : rig.RefusalFor(item.Kind);

        internal bool Place(StartingFuelItem item)
        {
            if (item == null || item.Consumed || rig == null || !rig.FeatureEnabled || !DoorOpen || !CanTake(item))
                return false;
            if (Net.ItemsRemote)
            {
                // Shared world item on a multiplayer client: release it inside the
                // open stove chamber so the host's CabooseStoveTarget.Track takes it.
                var inside = transform.position;
                var handler = PlayerManager.PlayerTransform != null
                    ? PlayerManager.PlayerTransform.GetComponentInChildren<GrabberInteractionHandlerDV>()
                    : null;
                handler?.RequestDrop();
                item.transform.position = inside;
                var body = item.Body;
                if (body != null)
                    body.velocity = rig.Car != null && rig.Car.rb != null ? rig.Car.rb.GetPointVelocity(inside) : Vector3.zero;
                return true;
            }
            var oil = item.Kind == StartingFuelKind.Rag ? item.Rag.Oil : 0f;
            var lit = item.Kind == StartingFuelKind.Rag && item.Rag.Lit;
            if (!rig.DeliverFromPlayer(item.Kind, oil, lit))
                return false;
            if (item.Item != null && item.Item.IsGrabbed())
                PlayerManager.PlayerTransform?.GetComponentInChildren<GrabberInteractionHandlerDV>()?.RequestDrop();
            Consume(item);
            return true;
        }

        /// <summary>Checks whether a loose wood split or rag entered an open caboose stove.</summary>
        internal static bool Track(StartingFuelItem item)
        {
            if (item == null || item.Consumed || item.Held || Net.ItemsRemote || !item.isActiveAndEnabled)
                return false;
            var now = item.transform.position;
            for (var i = 0; i < active.Count; i++)
            {
                var stove = active[i];
                if (stove == null || stove.rig == null || !stove.rig.FeatureEnabled || stove.rig.Car == null)
                    continue;
                if ((stove.transform.position - now).sqrMagnitude > Reach * Reach)
                    continue;
                if (stove.TryTakeLoose(item, now))
                    return true;
            }
            return false;
        }

        /// <summary>Checks whether a thrown piece struck the open stove doorway.</summary>
        internal static bool Strike(StartingFuelItem item) => Track(item);

        /// <summary>Guides non-VR throws aimed at an open caboose stove straight into its mouth.</summary>
        internal static Vector3 AimThrough(Vector3 eye, Vector3 currentTarget)
        {
            var dir = currentTarget - eye;
            var dist = dir.magnitude;
            if (dist < 1e-4f)
                return currentTarget;
            var rayDir = dir / dist;
            for (var i = 0; i < active.Count; i++)
            {
                var stove = active[i];
                if (stove == null || stove.rig == null || !stove.DoorOpen)
                    continue;
                if ((stove.transform.position - eye).sqrMagnitude > Reach * Reach)
                    continue;
                var localOrigin = stove.transform.InverseTransformPoint(eye);
                var localDir = stove.transform.InverseTransformDirection(rayDir);
                if (IntersectsLocalChamber(localOrigin, localDir, dist + 0.5f, stove.chamberSize))
                    return stove.transform.position;
            }
            return currentTarget;
        }

        /// <summary>
        /// Tight interior bounds of the stove firebox in stove-local space so wood
        /// resting on top of the stove or outside the door is never pulled in.
        /// </summary>
        internal static bool IsInsideChamber(Vector3 localPos, Vector3 chamberSize) =>
            localPos.x >= -chamberSize.x * 0.45f && localPos.x <= chamberSize.x * 0.48f
            && localPos.y >= -chamberSize.y * 0.45f && localPos.y <= chamberSize.y * 0.42f
            && Mathf.Abs(localPos.z) <= chamberSize.z * 0.45f;

        /// <summary>
        /// Local-space ray-box intersection against the stove chamber so aim
        /// redirection respects the caboose's yaw/pitch/roll rotation.
        /// </summary>
        internal static bool IntersectsLocalChamber(Vector3 localOrigin, Vector3 localDir, float maxDist, Vector3 chamberSize)
        {
            var len = localDir.magnitude;
            if (len < 1e-6f || maxDist < 0f)
                return false;
            var d = localDir / len;
            var half = chamberSize * 0.5f;
            var tMin = 0f;
            var tMax = maxDist;
            if (!ClipAxis(localOrigin.x, d.x, -half.x, half.x, ref tMin, ref tMax)) return false;
            if (!ClipAxis(localOrigin.y, d.y, -half.y, half.y, ref tMin, ref tMax)) return false;
            if (!ClipAxis(localOrigin.z, d.z, -half.z, half.z, ref tMin, ref tMax)) return false;
            return tMax >= tMin;
        }

        private static bool ClipAxis(float origin, float dir, float min, float max, ref float tMin, ref float tMax)
        {
            if (Mathf.Abs(dir) < 1e-6f)
                return origin >= min && origin <= max;
            var inv = 1f / dir;
            var t1 = (min - origin) * inv;
            var t2 = (max - origin) * inv;
            if (t1 > t2)
            {
                var tmp = t1;
                t1 = t2;
                t2 = tmp;
            }
            if (t1 > tMin) tMin = t1;
            if (t2 < tMax) tMax = t2;
            return tMax >= tMin;
        }

        private bool TryTakeLoose(StartingFuelItem item, Vector3 worldPos)
        {
            var local = transform.InverseTransformPoint(worldPos);
            if (!IsInsideChamber(local, chamberSize))
                return false;
            if (!DoorOpen)
            {
                Eject(item, "The stove door is shut");
                return true;
            }
            var oil = item.Kind == StartingFuelKind.Rag ? item.Rag.Oil : 0f;
            var lit = item.Kind == StartingFuelKind.Rag && item.Rag.Lit;
            if (rig.DeliverFromPlayer(item.Kind, oil, lit))
            {
                Consume(item);
                return true;
            }
            Eject(item, rig.RefusalFor(item.Kind) ?? "The stove cannot take that");
            return true;
        }

        private static void Consume(StartingFuelItem item)
        {
            item.Consumed = true;
            Destroy(item.gameObject);
        }

        private void Eject(StartingFuelItem item, string why)
        {
            var outPos = transform.TransformPoint(new Vector3(chamberSize.x * 0.65f + 0.18f, 0.05f, 0f));
            item.transform.position = outPos;
            var body = item.Body;
            if (body != null)
            {
                var carVel = rig != null && rig.Car != null && rig.Car.rb != null ? rig.Car.rb.velocity : Vector3.zero;
                body.velocity = carVel + transform.right * 0.5f;
                body.angularVelocity = Vector3.zero;
            }
            item.Previous = outPos;
            if (!string.IsNullOrEmpty(why))
                FireController.Announce(why + ".");
        }
    }
}
