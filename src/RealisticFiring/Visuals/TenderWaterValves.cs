using System.Collections.Generic;
using System.Linq;
using DV;
using DV.CabControls.Spec;
using DV.Simulation.Ports;
using LocoSim.Implementations;
using RealisticFiring.Core;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Visuals
{
 internal static class TenderWaterValves
    {
        // Each tender's two handles in its model, and the drop from SHUT to
        // OPEN, in m (its plate's pins, 2.328 and 2.199 m up).
        private static readonly Dictionary<string, (string[] Handles, float Travel)> Fits = new Dictionary<string, (string[], float)>
        {
            ["em_lms_stanier_tender_1_black"] = (new[] { "Trimmer Left", "Trimmer Right" }, 0.13f),
            ["em_lms_stanier_tender_1_grey"] = (new[] { "Trimmer Left", "Trimmer Right" }, 0.13f),
        };

        // The slide to copy: the engine's damper puller.
        private const string SourceLivery = "em_lms_8f_loc_black";
        private const string SourceHolder = "Damper Puller";

        // The grip: the handle's top this long, at least this thick, in m.
        private const float GripLength = 0.4f;
        private const float GripLeast = 0.04f;

        private const string RootName = "[RealisticFiring tender water valves]";

        /// <summary>As the tender's sim starts: its handles follow their valves. Returns a line for the log, or null.</summary>
        public static string Attach(TrainCar car, SimulationFlow flow)
        {
            if (car == null || flow == null || car.carLivery == null || !InjectorValves.HasTenderValves(car.carLivery.id)
                || !Fits.TryGetValue(car.carLivery.id, out var fit))
                return null;
            var handles = car.GetComponentsInChildren<MeshFilter>(true)
                .Where(f => fit.Handles.Contains(f.name) && f.sharedMesh != null)
                .ToList();
            if (handles.Count != 2)
                return $"tender water valves: {handles.Count} handles found, not 2";
            var notes = new List<string>();
            foreach (var handle in handles)
            {
                var right = car.transform.InverseTransformPoint(handle.transform.TransformPoint(handle.sharedMesh.bounds.center)).x > 0f;
                var valve = InjectorValves.TenderValve(right ? 0 : 1);
                if (!flow.TryGetPort(valve.PortId, out var port))
                    return $"tender water valves: no port '{valve.PortId}'";
                var follower = handle.GetComponent<TenderValveHandle>() ?? handle.gameObject.AddComponent<TenderValveHandle>();
                follower.Follow(car, valve, port, fit.Travel);
                notes.Add($"'{valve.Id}' is '{handle.name}'");
            }
            return "tender water valves: " + string.Join(", ", notes);
        }

        /// <summary>As the tender's interactables load: a slide on each handle. Returns a line for the log, or null.</summary>
        public static string AttachControls(TrainCar car, Transform interactables)
        {
            if (car == null || interactables == null || interactables.Find(RootName) != null)
                return null;
            var handles = car.GetComponentsInChildren<TenderValveHandle>(true).Where(h => h.Port != null).ToList();
            if (handles.Count == 0)
                return null;
            if (!Globals.G.Types.TryGetLivery(SourceLivery, out var sourceLivery) || sourceLivery.interiorPrefab == null)
                return "tender water valves: no slide to copy";
            var source = sourceLivery.interiorPrefab.transform.Find(SourceHolder);
            if (source == null || source.GetComponentInChildren<Puller>(true) == null)
                return $"tender water valves: no '{SourceHolder}' to copy";
            var root = new GameObject(RootName).transform;
            root.SetParent(interactables, false);
            return "tender water valves: " + string.Join("; ", handles.Select(h => Build(car, root, source, h)));
        }

        private static string Build(TrainCar car, Transform root, Transform source, TenderValveHandle handle)
        {
            // Copied switched off and set in place before it wakes.
            var frame = new GameObject(handle.Valve.Id).transform;
            frame.gameObject.SetActive(false);
            frame.SetParent(root, false);
            var holder = Object.Instantiate(source.gameObject, frame, false).transform;
            var spec = holder.GetComponentInChildren<Puller>(true);
            var control = spec.transform;
            var pivot = spec.pivot != null ? spec.pivot : control.parent;
            var grips = spec.colliderGameObjects != null ? spec.colliderGameObjects.FirstOrDefault(g => g != null) : null;
            var layer = grips != null ? grips.layer : control.gameObject.layer;
            foreach (var renderer in holder.GetComponentsInChildren<Renderer>(true))
                Object.DestroyImmediate(renderer);
            var handleRenderer = handle.GetComponent<Renderer>();
            if (handleRenderer != null)
                (control.GetComponent<HighlightTag>() ?? control.gameObject.AddComponent<HighlightTag>()).renderers = new List<Renderer> { handleRenderer };
            foreach (var collider in holder.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            if (spec.nonVrStaticInteractionArea != null)
            {
                Object.DestroyImmediate(spec.nonVrStaticInteractionArea.gameObject);
                spec.nonVrStaticInteractionArea = null;
            }

            // At the handle's grip as it stands shut, sliding down the tender's
            // up from there: pulled out (down) is open.
            var grip = handle.RestGrip(car.transform, GripLength);
            pivot.SetPositionAndRotation(car.transform.TransformPoint(grip.center), car.transform.rotation);
            control.SetParent(pivot, false);
            control.localPosition = Vector3.zero;
            control.localRotation = Quaternion.identity;
            var anchor = spec.connectionAnchor != null ? spec.connectionAnchor : new GameObject("Anchor").transform;
            anchor.SetParent(pivot, false);
            anchor.localPosition = new Vector3(0f, -handle.Travel / 2f, 0f);
            anchor.localRotation = Quaternion.identity;
            spec.pivot = pivot;
            spec.useCustomConnectionAnchor = true;
            spec.connectionAnchor = anchor;
            spec.linearLimit = handle.Travel / 2f;
            spec.useSteppedPuller = false;
            spec.invertDirection = false;
            spec.scrollWheelHoverScroll = 0.1f;

            var gripObject = new GameObject("grip");
            gripObject.layer = layer;
            gripObject.transform.SetParent(control, false);
            var box = gripObject.AddComponent<BoxCollider>();
            box.size = Vector3.Max(grip.size, Vector3.one * GripLeast);
            spec.colliderGameObjects = new[] { gripObject };

            foreach (var input in control.GetComponents<MonoBehaviour>()
                         .Where(c => StokerCab.InputComponents.Contains(c.GetType().Name)
                                     || c.GetType().Name == "PercentageControlNames" || c.GetType().Name == "NotchedControlCustomNames")
                         .ToList())
                Object.DestroyImmediate(input);
            var feeder = control.GetComponent<InteractablePortFeeder>();
            if (feeder == null)
            {
                Object.Destroy(frame.gameObject);
                return $"the copied slide for '{handle.Valve.Id}' has no feeder";
            }
            feeder.portId = handle.Valve.PortId;
            control.gameObject.AddComponent<SimControlBinder>().Bind(feeder, handle.Port);
            control.gameObject.AddComponent<HoverName>().Source = () => Hover(handle);
            frame.gameObject.SetActive(true);
            return $"'{handle.Valve.Id}' slides {handle.Travel:F2} m on '{handle.name}', its grip at ({grip.center.x:F2}, {grip.center.y:F2}, {grip.center.z:F2}) m";
        }

        private static string Hover(TenderValveHandle handle)
        {
            var value = handle.Port.Value;
            return $"{handle.Valve.Label}: " + (value >= 0.99f ? "open" : value <= InjectorSpec.OpenThreshold ? "shut" : $"{value:P0} open");
        }
    }

    /// <summary>A tender's water valve handle, down from where it is modeled (shut) as far as its valve is open.</summary>
    internal sealed class TenderValveHandle : MonoBehaviour
    {
        private Vector3 rest;
        private Vector3 down;

        public StokerJetControls.Valve Valve { get; private set; }
        public Port Port { get; private set; }
        public float Travel { get; private set; }

        public void Follow(TrainCar car, StokerJetControls.Valve valve, Port port, float travel)
        {
            if (Port == null)
                rest = transform.localPosition;
            Valve = valve;
            Port = port;
            Travel = travel;
            down = transform.parent != null ? transform.parent.InverseTransformVector(-car.transform.up) : -car.transform.up;
            LateUpdate();
        }

        /// <summary>The handle's top <paramref name="length"/> as it stands shut, in <paramref name="frame"/>'s space.</summary>
        public Bounds RestGrip(Transform frame, float length)
        {
            var mesh = GetComponent<MeshFilter>().sharedMesh.bounds;
            var parent = transform.parent != null ? transform.parent.localToWorldMatrix : Matrix4x4.identity;
            var toFrame = frame.worldToLocalMatrix * parent * Matrix4x4.TRS(rest, transform.localRotation, transform.localScale);
            var box = new Bounds(toFrame.MultiplyPoint3x4(mesh.min), Vector3.zero);
            for (var i = 0; i < 8; i++)
                box.Encapsulate(toFrame.MultiplyPoint3x4(new Vector3(
                    (i & 1) == 0 ? mesh.min.x : mesh.max.x,
                    (i & 2) == 0 ? mesh.min.y : mesh.max.y,
                    (i & 4) == 0 ? mesh.min.z : mesh.max.z)));
            var bottom = Mathf.Max(box.min.y, box.max.y - length);
            box.SetMinMax(new Vector3(box.min.x, bottom, box.min.z), box.max);
            return box;
        }

        private void LateUpdate()
        {
            if (Port != null)
                transform.localPosition = rest + down * (Travel * Mathf.Clamp01(Port.Value));
        }
    }
}
