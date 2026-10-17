using System.Collections.Generic;
using System.Linq;
using DV;
using DV.Simulation.Cars;
using DV.Simulation.Ports;
using LocoSim.Implementations;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Visuals
{
    /// <summary>
    /// The ashpan's hopper handle, on the outside of the loco, worked from
    /// the ground: a copy of the S282's brake cylinder release handle (a rod
    /// out from the side with a pull handle at its end), its brake behaviour
    /// removed and its button set to hold: held, the hopper doors open and
    /// the ashes fall out; let go, they shut. It sticks out on the fireman's
    /// side at the back of the firebox, with an ASHPAN plate on the side by
    /// it. It is added to the car's external interactables each time they
    /// load (the cab's interior is gone once the player steps down), once the
    /// grate's place in the car is known from a cab of the livery.
    /// </summary>
    internal sealed class AshpanLever : MonoBehaviour
    {
        private const string RootName = "[RealisticFiring ashpan lever]";

        // The brake cylinder release handle on the S282's tender: a rod
        // straight out along its z with the pull at its end (the engine's own
        // is a valve with its rod along the frame, so it pointed forward).
        private const string SourceHandleLivery = "LocoS282B";
        private const string SourceHandle = "BrakeCylinderRelease";
        private const string SourceButton = "C brake_cylinder_release";

        // The handle: this far under the grate and this far forward of its
        // back edge, its pull this far out from the side of the loco found by
        // a ray in from beyond it, in m, unless seated. The rod runs this far from its root to the pull.
        private const float BelowGrate = 0.3f;
        private const float AlongFromGrateBack = 0.2f;
        private const float PullOut = 0.3f;
        private const float RodLength = 0.86f;
        private const float SideSearch = 3f;
        private const float FallbackHalfWidth = 1.4f;

        // The plate on the side above the rod, in m.
        private const float PlateUp = 0.14f;

        private static readonly HashSet<string> NoHanger = new HashSet<string> { "Jk3A" };
        private const float RodRadius = 0.0085f;
        private const float CollarRadius = 0.019f;
        private const float CollarFrom = 0.84f;
        private const float CollarTo = 0.86f;
        private const float BossRadius = 0.013f;
        private const float BossTo = 0.875f;
        private const float RodIntoSide = 0.03f;

        private static readonly Dictionary<string, Vector3> Placements = new Dictionary<string, Vector3>
        {
            ["S484-840A"] = new Vector3(-1.08f, 1.27f, -6.83f),
            ["S464-730"] = new Vector3(-1.07f, 1.18f, 1.79f),
            ["LocoS282A"] = new Vector3(-0.61f, 0.99f, 1.66f),
            ["LocoS060"] = new Vector3(-0.73f, 1.11f, -2.26f),
            ["FEF-3"] = new Vector3(-1.35f, 1.33f, -7.26f),
            ["Jk3A"] = new Vector3(-0.86f, 1.01f, -0.87f),
            ["em_lms_8f_loc_black"] = new Vector3(-1.00f, 1.42f, -2.39f), // 2026-10-14
            ["em_lms_8f_loc_grey"] = new Vector3(-1.00f, 1.42f, -2.39f),
        };

        private TrainCar car;
        private RealisticFirebox firebox;
        private Port port;
        private float nextTry;

        public static void Attach(TrainCar car, RealisticFirebox firebox, Port port)
        {
            if (car == null || firebox == null || port == null || car.GetComponent<AshpanLever>() != null)
                return;
            var lever = car.gameObject.AddComponent<AshpanLever>();
            lever.car = car;
            lever.firebox = firebox;
            lever.port = port;
        }

        private void Start()
        {
            car.ExternalInteractableLoaded += OnLoaded;
            car.ExternalInteractableAboutToBeUnloaded += OnUnloading;
            OnLoaded(car.loadedExternalInteractables);
        }

        private void OnDestroy()
        {
            if (car != null)
            {
                car.ExternalInteractableLoaded -= OnLoaded;
                car.ExternalInteractableAboutToBeUnloaded -= OnUnloading;
            }
        }

        // The grate's place comes from the cab; until a cab of the livery has
        // loaded there is nowhere to put the lever, so look again now and then.
        private void Update()
        {
            if (Time.time < nextTry)
                return;
            nextTry = Time.time + 2f;
            var root = car.loadedExternalInteractables;
            if (root != null && root.transform.Find(RootName) == null)
                OnLoaded(root);
        }

        private void OnLoaded(GameObject root)
        {
            if (root == null || root.transform.Find(RootName) != null
                || (!firebox.GrateBox.HasValue && !Placements.ContainsKey(car.carLivery.id)))
                return;
            var result = Build(root.transform);
            if (result != null && Main.Settings.LogFireboxCreation)
                Main.Mod.Logger.Log($"{car.ID}: {result}");
        }

        private static void OnUnloading(GameObject root)
        {
            var old = root != null ? root.transform.Find(RootName) : null;
            if (old != null)
                Destroy(old.gameObject);
        }

        private string Build(Transform root)
        {
            if (!Globals.G.Types.TryGetLivery(StokerCab.SourceLivery, out var livery) || livery.interiorPrefab == null)
                return $"no {StokerCab.SourceLivery} cab to take the ashpan plate from";
            if (!Globals.G.Types.TryGetLivery(SourceHandleLivery, out var tender) || tender.externalInteractablesPrefab == null)
                return $"no {SourceHandleLivery} to take the ashpan handle from";
            var source = tender.externalInteractablesPrefab.transform.Find(SourceHandle);
            var sourceButton = source != null ? source.Find(SourceButton) : null;
            if (sourceButton == null || sourceButton.localPosition.z < 0.5f * RodLength)
                return $"no brake cylinder release handle with its pull out at the end of its rod on the {SourceHandleLivery} to copy for the ashpan";
            var cabPrefab = livery.interiorPrefab.transform;
            var sourceValve = cabPrefab.Find(StokerCab.SourceValve);
            var sourceControl = sourceValve != null ? sourceValve.Find(StokerCab.SourceControl) : null;
            var sourceLabel = cabPrefab.Find(StokerCab.SourceLabel);

            // On the fireman's side (left, -x), at the back of the grate, under
            // it, unless seated for the livery.
            var frame = car.transform;
            var seatedFor = Placements.TryGetValue(car.carLivery.id, out var seated);
            float pull, y, z, side;
            bool found;
            if (seatedFor)
            {
                (pull, y, z) = (seated.x, seated.y, seated.z);
                found = TryFindSide(-SideSearch, y, z, out side);
            }
            else
            {
                var grate = firebox.GrateBox.Value;
                y = grate.min.y - BelowGrate;
                z = grate.min.z + AlongFromGrateBack;
                found = TryFindSide(-SideSearch, y, z, out side);
                if (!found)
                    side = -FallbackHalfWidth;
                pull = side - PullOut;
            }
            // The plate goes on the wall beside the handle wherever it was
            // moved: the first surface of the loco in from the pull.
            if (!TryFindSide(pull - 0.05f, y + PlateUp, z, out var wall))
                wall = pull + PullOut;

            var holderRoot = new GameObject(RootName);
            holderRoot.SetActive(false);
            holderRoot.transform.SetParent(root, false);

            // The rod out from the side, its pull at the end, pointing away
            // from the loco: its own z runs from its root to the pull.
            var handle = Object.Instantiate(source.gameObject, holderRoot.transform, false).transform;
            handle.name = RealisticFirebox.AshpanControl.Id;
            handle.SetPositionAndRotation(frame.TransformPoint(new Vector3(pull + RodLength, y, z)),
                Quaternion.LookRotation(-frame.right, frame.up));

            if (NoHanger.Contains(car.carLivery.id))
                DropHanger(handle, pull + RodLength - (found ? side : wall) - RodIntoSide);

            var button = handle.Find(SourceButton);
            foreach (var brake in button.GetComponents<MonoBehaviour>().Where(c => c != null && c.GetType().Name == "BrakeCylinderReleaseButtonFeeder").ToList())
                Object.DestroyImmediate(brake);
            var spec = button.GetComponent<DV.CabControls.Spec.Button>();
            if (spec == null)
            {
                Destroy(holderRoot);
                return "the brake cylinder release handle has no button to copy for the ashpan";
            }
            // Held, it stays pressed; let go, it comes back.
            spec.isToggle = true;
            spec.isTogglingBack = true;
            var feeder = button.gameObject.AddComponent<InteractablePortFeeder>();
            feeder.portId = RealisticFirebox.AshpanControl.PortId;
            button.gameObject.AddComponent<SimControlBinder>().Bind(feeder, port);
            button.gameObject.AddComponent<HoverName>().Source = Hover;

            // The plate on the loco's side above the rod, facing out: a frame
            // whose z points into the side, as a valve's wheel faces back
            // along its z.
            if (sourceControl != null && sourceLabel != null)
            {
                var plate = new GameObject("label frame").transform;
                plate.SetParent(handle, false);
                plate.SetPositionAndRotation(frame.TransformPoint(new Vector3(wall - 0.01f, y + PlateUp, z)),
                    Quaternion.LookRotation(frame.right, frame.up));
                StokerCab.PlaceLabel(sourceControl, sourceLabel, plate, holderRoot.transform,
                    holderRoot.transform.InverseTransformPoint(plate.position), "ASHPAN");
            }
            holderRoot.SetActive(true);

            return $"ashpan handle out on the left side, its pull at ({pull:F2}, {y:F2}, {z:F2}) m in the car frame" +
                   (seatedFor ? " (seated for the livery)" : found ? $", the side at x {side:F2}" : " (no side of the loco found there; guessed)") +
                   $", its plate on the wall at x {wall:F2}";
        }

        // Hides the handle's rod and hanger and draws the rod alone from
        // from m along it (its root's end, inside the loco, unseen) to the pull.
        private static void DropHanger(Transform handle, float from)
        {
            var staticPart = handle.Find("StaticPart");
            if (staticPart == null)
                return;
            var layer = handle.gameObject.layer;
            var model = staticPart.Find("model");
            if (model != null)
            {
                foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    layer = renderer.gameObject.layer;
                    renderer.enabled = false;
                }
            }
            var colliders = staticPart.Find("Colliders");
            if (colliders != null)
            {
                foreach (var box in colliders.GetComponentsInChildren<BoxCollider>(true))
                    DestroyImmediate(box);
            }
            from = Mathf.Clamp(from, 0f, CollarFrom - 0.05f);
            var rod = new Shed.FittingMesh();
            rod.Rod(Shed.Metal.Iron, RodRadius, from, CollarFrom + 0.002f, Vector3.zero, Vector3.forward);
            rod.Rod(Shed.Metal.Iron, CollarRadius, CollarFrom, CollarTo, Vector3.zero, Vector3.forward);
            rod.Rod(Shed.Metal.Iron, BossRadius, CollarTo - 0.001f, BossTo, Vector3.zero, Vector3.forward);
            rod.Build(staticPart, "RealisticFiring ashpan rod", layer);
        }

        // The first surface of this car in toward its middle from a point on
        // its left, at x in the car frame.
        private bool TryFindSide(float fromX, float y, float z, out float side)
        {
            side = 0f;
            var frame = car.transform;
            var from = frame.TransformPoint(new Vector3(fromX, y, z));
            Physics.SyncTransforms();
            foreach (var hit in Physics.RaycastAll(from, frame.right, -fromX, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
            {
                if (TrainCar.Resolve(hit.collider.gameObject) != car)
                    continue;
                side = frame.InverseTransformPoint(hit.point).x;
                return true;
            }
            return false;
        }

        private string Hover()
        {
            var model = firebox.Model;
            var open = port.Value > 0.05f;
            return $"{RealisticFirebox.AshpanControl.Label}: {(open ? "open, ashes falling" : "hold to let the ashes out")}; " +
                   $"ashpan {model.Ashpan:F0} kg ({model.AshpanFill:P0}{(model.AshpanFill >= 0.5f ? ", clean it" : "")})";
        }
    }
}
