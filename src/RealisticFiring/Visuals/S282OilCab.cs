using System;
using System.Collections.Generic;
using System.Linq;
using DV;
using DV.CabControls;
using DV.Simulation.Ports;
using LocoSim.Implementations;
using RealisticFiring.Sim;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RealisticFiring.Visuals
{
 internal static class S282OilCab
    {
        internal const string RootName = "[RealisticFiring oil burner cab]";
        private const string InteriorMaterialPath = "Static/Cab";
        private const string GaugeMaterialPath = "Static/Gauges";
        private static readonly Dictionary<S282OilTenderData.Piece, Mesh> Meshes = new Dictionary<S282OilTenderData.Piece, Mesh>();
        private static float nextCheck;

        private const float FiringValveScrollDegrees = 1f;

        private const float LeverDamper = 30f;
        private const int ValveNotches = 41;

        // The peephole's cover swings this far open, toward the fireman, degrees; it stays where it is put, and a
        // click of the wheel swings it 10 degrees.
        private const float CoverOpens = 110f;

        private const float WheelGripRadius = 0.033f;

        // The 2901's keys: the firing valve on Gear A, the atomizer on Gear B (Awaymode, Nexus, 2026-10-23).
        private const string FiringValveKeys = "GearAIncrement";
        private const string AtomizerKeys = "GearBIncrement";


        /// <summary>The atomizer's and heater's valves, by their names in the cab (the model's clones).</summary>
        internal const string AtomizerName = "atomizer wheel";
        internal const string HeaterName = "oil heater wheel";

        private static readonly string[] Unused =
        {
            "Center/CoalDump", "Center/Firebox/C_FireboxHandle", "Center/Firebox/IA_FireboxHandle", "Center/Firebox/FireboxDoor_animated",
        };

        /// <summary>Whether <paramref name="car"/> is an S282 built to burn oil, which wears this cab.</summary>
        internal static bool IsConverted(TrainCar car) => S282OilConversion.IsOil(car);

        /// <summary>Fits every loaded S282 cab that lacks its oil burner's equipment.</summary>
        internal static void EnsureFitted()
        {
            if (!Main.Session.S282OilBurner || Time.unscaledTime < nextCheck)
                return;
            nextCheck = Time.unscaledTime + 1f;
            var spawner = CarSpawner.Instance;
            if (spawner == null || spawner.AllCars == null)
                return;
            foreach (var car in spawner.AllCars)
            {
                if (!IsConverted(car) || car.loadedInterior == null || car.loadedInterior.transform.Find(RootName) != null)
                    continue;
                var note = Fit(car, car.loadedInterior.transform);
                if (note != null)
                    Main.Mod.Logger.Log($"{car.ID}: {note}");
            }
        }

        private const string SootyPiece = "cab peephole";

        // The door's refractory lining: matte firebrick, for the same reason as the bore (its glossy firebrick
        // drew a cream crescent down the hole, job 3347).
        private const string LinerPiece = "door liner";

        // The sight hole on through the lining, sooty at the door and glowing toward the fire (Lining).
        private const string LiningPiece = "peephole lining";

        // The bore: dull, with no shine. The fire's light runs straight down the bore toward the eye, which a
        // Standard material reflects at that grazing angle whatever its colour and gloss (the build ignores the
        // keyword that turns highlights off): the bore drew tan, the light's colour (jobs 3326 to 3334; red made
        // it red only in red, and the fire's own light off took the tan away). A Lambert material has no
        // highlight at all; Standard, black and fully metallic (no specular colour), is the fallback.
        private static Material bore;

        private static Material Bore(Material basis)
        {
            if (bore != null)
                return bore;
            var lambert = Shader.Find("Legacy Shaders/Diffuse");
            if (lambert != null)
                return bore = new Material(lambert) { name = "RealisticFiring peephole bore", color = Color.white, mainTexture = BoreTexture() };
            var standard = Shader.Find("Standard");
            var material = standard != null ? new Material(standard) : new Material(basis);
            material.name = "RealisticFiring soot";
            foreach (var map in new[] { "_MainTex", "_BumpMap", "_MetallicGlossMap", "_EmissionMap" })
                if (material.HasProperty(map))
                    material.SetTexture(map, null);
            material.DisableKeyword("_NORMALMAP");
            material.DisableKeyword("_METALLICGLOSSMAP");
            material.DisableKeyword("_EMISSION");
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", Color.black);
            if (material.HasProperty("_EmissionColor"))
                material.SetColor("_EmissionColor", Color.black);
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", 1f);
            if (material.HasProperty("_Glossiness"))
                material.SetFloat("_Glossiness", 0f);
            return bore = material;
        }

        private static Texture2D boreTexture;

        /// <summary>
        /// The bore's inside, once round (U) and once along it (V, 0 at the
        /// mouth to 1 at the door's back): the mouth's chamfer bright steel,
        /// the steel sooting darker inward to near black where it meets the
        /// lining, mottled, so the eye reads the bore's depth by day and by the
        /// cab's light (the fire's own light only grazes it).
        /// </summary>
        private static Texture2D BoreTexture()
        {
            if (boreTexture != null)
                return boreTexture;
            const int w = 32, h = 64;
            var steel = new Color(0.40f, 0.385f, 0.36f);
            var soot = new Color(0.06f, 0.055f, 0.05f);
            var pixels = new Color32[w * h];
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var v = (y + 0.5f) / h;
                var a = 2f * Mathf.PI * (x + 0.5f) / w;
                var c = Color.Lerp(steel, soot, Mathf.Pow(Mathf.Clamp01((v - 0.04f) / 0.9f), 0.7f));
                // Round the bore without a seam: noise on a circle.
                var mottle = 0.82f + 0.36f * Mathf.PerlinNoise(4.1f + 1.3f * Mathf.Cos(a) + 2.2f * v, 7.3f + 1.3f * Mathf.Sin(a) + 3.1f * v);
                pixels[y * w + x] = (Color32)(c * mottle);
            }
            boreTexture = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "RealisticFiring peephole bore", wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp };
            boreTexture.SetPixels32(pixels);
            boreTexture.Apply(true, true);
            return boreTexture;
        }

        // The lining's light from the cab, doubled by its shader: dim, deep in the hole.
        private static readonly Color LiningLit = new Color(0.3f, 0.3f, 0.3f, 1f);

        // Its glow at the fire's end by the brick's heat, of the bricks' own (BrickArch.HeatColor): the hot face
        // beside the flame glows orange (at 0.6 it drew dull red, job 3380). The shader holds each channel to 1.
        internal const float LiningGlow = 2f;

        /// <summary>
        /// The sight hole through the door's lining (<see cref="LiningPiece"/>): a
        /// Legacy VertexLit material, lit with no highlight (as the bore) and
        /// glowing by its emission through its texture (LiningTexture), so the
        /// glow runs from nothing at the door to the brick's at the fire. Null
        /// where the build lacks the shader.
        /// </summary>
        private static Material Lining()
        {
            var vertexLit = Shader.Find("Legacy Shaders/VertexLit");
            if (vertexLit == null)
                return null;
            var material = new Material(vertexLit) { name = "RealisticFiring peephole lining", mainTexture = LiningTexture() };
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", LiningLit);
            if (material.HasProperty("_SpecColor"))
                material.SetColor("_SpecColor", Color.black);
            if (material.HasProperty(LiningEmissionId))
                material.SetColor(LiningEmissionId, Color.black);
            return material;
        }

        // A name, not an ID: Shader.PropertyToID in a static initializer breaks the class outside the game (the
        // adapter checks).
        private const string LiningEmissionId = "_Emission";
        private static Texture2D liningTexture;

        /// <summary>
        /// The lining's sight hole, once round (U) and once along it (V, 0 at
        /// the door to 1 at the fire): castable refractory, sooty where it meets
        /// the bore and clean and pale toward the fire, mottled. Its shader
        /// takes its emission through this, so it glows most at the fire.
        /// </summary>
        private static Texture2D LiningTexture()
        {
            if (liningTexture != null)
                return liningTexture;
            const int w = 32, h = 64;
            var refractory = new Color(0.68f, 0.47f, 0.33f);
            var pixels = new Color32[w * h];
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var v = (y + 0.5f) / h;
                var a = 2f * Mathf.PI * (x + 0.5f) / w;
                var clean = Mathf.Lerp(0.1f, 1f, Mathf.Pow(v, 1.4f));
                var mottle = 0.8f + 0.4f * Mathf.PerlinNoise(2.7f + 1.6f * Mathf.Cos(a) + 3.3f * v, 5.9f + 1.6f * Mathf.Sin(a) + 2.1f * v);
                pixels[y * w + x] = (Color32)(refractory * (clean * mottle));
            }
            liningTexture = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "RealisticFiring peephole lining", wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp };
            liningTexture.SetPixels32(pixels);
            liningTexture.Apply(true, true);
            return liningTexture;
        }

        private static string Fit(TrainCar car, Transform interior)
        {
            var data = S282OilTender.Data;
            if (data == null || data.Cab == null || data.Cab.Length == 0)
                return null;
            var interiorMaterial = interior.Find(InteriorMaterialPath)?.GetComponent<MeshRenderer>()?.sharedMaterial;
            var gaugeMaterial = interior.Find(GaugeMaterialPath)?.GetComponent<MeshRenderer>()?.sharedMaterial;
            if (interiorMaterial == null)
                return "oil burner cab: the cab's own material is missing; left as it is";
            var root = new GameObject(RootName).transform;
            root.gameObject.SetActive(false);
            root.SetParent(interior, false);
            // By the cab's frame, not the car's (CabFrame).
            var frame = CabFrame.Of(car);
            root.SetPositionAndRotation(frame.position, frame.rotation);
            var notes = new List<string>();
            var pieces = 0;
            var moving = new Dictionary<string, (Mesh Mesh, Material Material)>();
            RealisticOilFirebox.TryGet(car, out var oil);
            Material firebrick = null;
            Material liner = null;
            Material lining = null;
            foreach (var p in data.Cab.Where(p => p.Lod == 0))
            {
                if (p.Material == S282OilTenderData.Firebrick && firebrick == null)
                    firebrick = BrickArch.FirebrickMaterial(interiorMaterial);
                var material = p.Material == "LocoS282A_Gauges" ? gaugeMaterial
                    : p.Name == SootyPiece ? Bore(interiorMaterial)
                    : p.Name == LinerPiece ? liner ?? (liner = BrickArch.MatteFirebrick())
                    : p.Name == LiningPiece ? lining ?? (lining = Lining()) ?? (lining = BrickArch.MatteFirebrick())
                    : p.Material == "LocoS282A_Interior" ? interiorMaterial
                    : p.Material == S282OilTenderData.Firebrick ? firebrick
                    : null;
                if (material == null)
                    continue;
                if (!Meshes.TryGetValue(p, out var mesh) || mesh == null)
                    Meshes[p] = mesh = S282OilTender.Create(p);
                if (S282OilTenderData.MovingPieces.Contains(p.Name))
                {
                    moving[p.Name] = (mesh, material);
                    continue;
                }
                Piece(root, p.Name, mesh, material, interior.gameObject.layer);
                pieces++;
            }
            notes.Add($"{pieces} pieces");
            if (firebrick != null || lining != null)
                root.gameObject.AddComponent<FirepanHeat>().Set(firebrick, lining != null && lining.HasProperty(LiningEmissionId) ? lining : null, oil);
            if (lining != null)
                notes.Add($"the peephole lined in '{lining.shader.name}'");
            var hidden = 0;
            foreach (var path in data.CabHide ?? new string[0])
            {
                var part = interior.Find(path);
                if (part == null)
                    continue;
                foreach (var r in part.GetComponentsInChildren<Renderer>(true))
                {
                    r.enabled = false;
                    hidden++;
                }
            }
            foreach (var path in data.Firebox?.Hide ?? new string[0])
            {
                var part = interior.Find(path);
                if (part == null)
                    continue;
                foreach (var r in part.GetComponentsInChildren<Renderer>(true))
                {
                    r.enabled = false;
                    hidden++;
                }
            }
            notes.Add($"{hidden} coal-firing parts hidden");
            var unused = 0;
            foreach (var path in Unused)
            {
                var part = interior.Find(path);
                if (part == null)
                    continue;
                foreach (var collider in part.GetComponentsInChildren<Collider>(true))
                {
                    collider.enabled = false;
                    unused++;
                }
            }
            if (unused > 0)
                notes.Add($"the shaker's and the fire doors' {unused} grips off");

            var flow = car.SimController != null ? car.SimController.SimulationFlow : null;
            // The stock doors are gone: their control (its keys still work it unseen) is held shut.
            var doorId = car.SimController != null && car.SimController.firebox != null ? car.SimController.firebox.fireboxDoorPortId : null;
            if (flow != null && !string.IsNullOrEmpty(doorId) && flow.TryGetPort(doorId, out var door, canBeNullOrEmpty: true) && door != null)
                root.gameObject.AddComponent<DoorHeldShut>().Port = door;
            var cabPrefab = Globals.G.Types.TryGetLivery(S282OilConversion.Livery, out var livery) && livery.interiorPrefab != null
                ? livery.interiorPrefab.transform
                : null;
            var valves = new List<Transform>();
            foreach (var c in data.CabClones ?? new S282OilTenderData.Clone[0])
            {
                Port port = null;
                var working = c.Port != null && cabPrefab != null && flow != null && flow.TryGetPort(c.Port, out port, canBeNullOrEmpty: true) && port != null;
                var control = working ? PlaceValve(car, interior, root, cabPrefab, c, port, oil) : null;
                if (control != null)
                {
                    valves.Add(control);
                    notes.Add($"{c.Name} working {c.Port}");
                }
                else
                {
                    notes.Add(PlaceClone(interior, root, c) + " (drawn only)");
                }
            }

            Transform lever = null;
            if (data.FiringValve != null && moving.ContainsKey(S282OilTenderData.FiringLever)
                && flow != null && flow.TryGetPort(S282OilConversion.FiringValve.PortId, out var firingPort, canBeNullOrEmpty: true) && firingPort != null
                && cabPrefab != null)
            {
                lever = PlaceFiringLever(car, interior, root, cabPrefab, data.FiringValve, moving, firingPort, oil);
                notes.Add(lever != null ? "firing valve working" : "no firing valve (no lever to copy)");
            }
            if (lever == null)
            {
                // Drawn where it would stand, at a working fire's notch.
                foreach (var name in new[] { S282OilTenderData.FiringLever, S282OilTenderData.FiringCrank, S282OilTenderData.FiringLink })
                    moving.Remove(name);
                notes.Add("firing valve drawn only");
            }
            if (lever != null && valves.Count > 0)
                FiringValvePassThrough.Attach(root.gameObject, lever, valves);

            if (data.Peephole != null && moving.TryGetValue(S282OilTenderData.PeepholeCover, out var cover))
            {
                Port peepPort = null;
                var working = cabPrefab != null && flow != null && flow.TryGetPort(OilBurnerFit.PeepHolePortId, out peepPort, canBeNullOrEmpty: true) && peepPort != null;
                var placed = working ? PlacePeephole(interior, root, cabPrefab, data.Peephole, cover, peepPort, oil) : null;
                if (placed == null)
                {
                    var hinge = Piece(root, S282OilTenderData.PeepholeCover, cover.Mesh, cover.Material, interior.gameObject.layer);
                    hinge.localPosition = V(data.Peephole.Hinge);
                }
                notes.Add(placed != null ? "peephole's cover working" : "peephole's cover drawn only");
            }

            var gauges = 0;
            foreach (var g in data.CabGauges ?? new S282OilTenderData.Gauge[0])
            {
                if (!moving.TryGetValue(g.Needle, out var needle))
                    continue;
                var hand = Piece(root, g.Needle, needle.Mesh, needle.Material, interior.gameObject.layer);
                Func<float> read = g.Needle == S282OilTenderData.AtomizerNeedle && oil != null ? () => AtomizerGaugeBar(oil) : (Func<float>)(() => 0f);
                GaugeNeedle.Fit(hand, g, read);
                gauges++;
            }
            if (gauges > 0)
                notes.Add($"{gauges} gauge needle{(gauges > 1 ? "s" : "")}");

            var labels = 0;
            var sourceLabel = interior.Find(StokerCab.SourceLabel);
            foreach (var l in data.CabLabels ?? new S282OilTenderData.Label[0])
                if (PlaceLabel(sourceLabel, root, l))
                    labels++;
            notes.Add($"{labels} plates");

            var shape = data.SandBox;
            var sand = shape != null ? FlueSandBox.BuildOnModel(car, root, V(shape.Middle), V(shape.Size), shape.BelowRim, interiorMaterial) : null;
            notes.Add(sand != null ? "flue sand in the sand box" : "no flue sand (no sand on the engine, or no sand box in the model)");
            if (sand != null)
                notes.Add(PlaceSandScoop(car, root, data, shape));
            root.gameObject.SetActive(true);
            return "oil burner cab: " + string.Join(", ", notes);
        }

        private const float PeepTargetDepth = 0.12f;
        private static readonly Vector3 PeepTargetSize = new Vector3(0.2f, 0.2f, PeepTargetDepth);
        private const float ScoopAside = 0.13f;
        private const float ScoopShortOfBackhead = 0.04f;

        private static string PlaceSandScoop(TrainCar car, Transform root, S282OilTenderData data, S282OilTenderData.SandBoxShape box)
        {
            var peep = data.Peephole;
            if (peep == null)
                return "no sand scoop target (no peephole in the model)";
            var mouth = V(peep.Middle);
            FlueSandBox.BuildFireboxTarget(root, root.TransformPoint(mouth - new Vector3(0f, 0f, PeepTargetDepth / 2f)), root.rotation,
                PeepTargetSize);
            var middle = V(box.Middle);
            var size = V(box.Size);
            var floor = middle.y - size.y / 2f;
            var front = middle.z + size.z / 2f - ScoopShortOfBackhead - Items.SandScoopMesh.Tip;
            var rest = new Vector3(middle.x + size.x / 2f + ScoopAside, floor + 0.05f, front);
            Items.SandScoopRest.Add(root.gameObject, car, root.TransformPoint(rest), root.rotation);
            return $"the sand scoop's target at the peephole and its place on the deck at ({rest.x:F2}, {rest.y:F2}, {rest.z:F2})";
        }

        private static Transform Piece(Transform root, string name, Mesh mesh, Material material, int layer)
        {
            var go = new GameObject(name);
            go.layer = layer;
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = name == ShadowSeal
                ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly
                : UnityEngine.Rendering.ShadowCastingMode.TwoSided;
            return go.transform;
        }

        // The slab behind the blanking door that seals the firehole against the fire's light, drawn only into
        // shadows (build_model.py, door_seal).
        private const string ShadowSeal = "shadow seal";

        // A working copy of the cab's own valve (its holder and control) on the fitting's spindle, at the
        // fitting's size, working the burner's port; its wheel's middle where the fitting wants it. Null
        // when the cab has no such valve.
        private static Transform PlaceValve(TrainCar car, Transform interior, Transform root, Transform cabPrefab,
            S282OilTenderData.Clone c, Port port, RealisticOilFirebox oil)
        {
            var sourceHolder = cabPrefab.Find(c.Holder ?? "");
            var model = cabPrefab.Find(c.Source ?? "");
            var modelMesh = model != null ? model.GetComponent<MeshFilter>()?.sharedMesh : null;
            if (sourceHolder == null || sourceHolder.Find(c.Control ?? "") == null || modelMesh == null)
                return null;
            var holder = Object.Instantiate(sourceHolder.gameObject, root, false).transform;
            holder.name = c.Name;
            var control = holder.Find(c.Control);
            // As the cab has it, turned from its own axis onto the fitting's, at the fitting's size.
            var turn = Quaternion.FromToRotation(V(c.SourceAxis), V(c.Axis));
            holder.localRotation = turn * (Quaternion.Inverse(cabPrefab.rotation) * sourceHolder.rotation);
            var scale = sourceHolder.lossyScale.x / Mathf.Max(1e-6f, cabPrefab.lossyScale.x) * c.Scale;
            holder.localScale = Vector3.one * scale;
            holder.localPosition = Vector3.zero;
            var copiedModel = holder.Find(c.Control + "/model") ?? control;
            // The copy works as the valve it was taken from; it wears the fitting's own wheel where that is
            // another (the heater: the blower's working valve, the bell's wheel).
            var copiedFilter = copiedModel.GetComponent<MeshFilter>();
            if (copiedFilter != null && copiedFilter.sharedMesh != modelMesh)
            {
                copiedFilter.sharedMesh = modelMesh;
                var sourceRenderer = model.GetComponent<MeshRenderer>();
                var copiedRenderer = copiedModel.GetComponent<MeshRenderer>();
                if (sourceRenderer != null && copiedRenderer != null)
                    copiedRenderer.sharedMaterials = sourceRenderer.sharedMaterials;
            }
            var middle = root.InverseTransformPoint(copiedModel.TransformPoint(modelMesh.bounds.center));
            holder.localPosition = V(c.At) - middle;
            // Only the wheel is drawn, as approved: the modeled valve stands behind it.
            foreach (var r in holder.GetComponentsInChildren<Renderer>(true))
                if (!r.transform.IsChildOf(copiedModel))
                    r.enabled = false;

            foreach (var input in control.GetComponents<MonoBehaviour>().Where(b => StokerCab.InputComponents.Contains(b.GetType().Name)).ToList())
                Object.DestroyImmediate(input);
            var feeder = control.GetComponent<InteractablePortFeeder>();
            if (feeder == null)
            {
                Object.Destroy(holder.gameObject);
                return null;
            }
            // Its own grip only, as wide as its wheel: the static grab helper, grown with the wheel, reached over
            // the next valve, and the grips' spheres overlapped.
            var spec = control.GetComponent<DV.CabControls.Spec.ControlSpec>();
            if (spec is DV.CabControls.Spec.Rotary helped && helped.nonVrStaticInteractionArea != null)
            {
                Object.DestroyImmediate(helped.nonVrStaticInteractionArea.gameObject);
                helped.nonVrStaticInteractionArea = null;
            }
            foreach (var area in holder.GetComponentsInChildren<DV.Interaction.StaticInteractionArea>(true).ToList())
                Object.DestroyImmediate(area.gameObject);
            foreach (var sphere in control.GetComponentsInChildren<SphereCollider>(true))
                sphere.radius = WheelGripRadius;
            feeder.portId = c.Port;
            control.gameObject.AddComponent<SimControlBinder>().Bind(feeder, port);
            var atomizer = c.Port == OilBurnerFit.Atomizer.PortId;
            var hover = control.gameObject.AddComponent<HoverName>();
            if (oil != null)
                hover.Source = atomizer ? (Func<string>)(() => OilBurnerCab.AtomizerHover(oil)) : () => OilBurnerCab.HeaterHover(oil);
            else
                hover.Text = atomizer ? OilBurnerFit.Atomizer.Label : OilBurnerFit.Heater.Label;
            // One notch a click of the mouse wheel; both set finely, as on the 2901.
            if (control.GetComponent<DV.CabControls.Spec.Rotary>() is DV.CabControls.Spec.Rotary rotary)
            {
                rotary.scrollWheelUseSpringRotation = true;
                rotary.scrollWheelHoverScroll = rotary.scrollWheelHoverScroll < 0f ? -1f : 1f;
                rotary.useSteppedJoint = true;
                rotary.notches = ValveNotches;
            }
            if (atomizer)
                CabKeys.Bind(interior, control, AtomizerKeys,
                    model.parent.GetComponent<DV.KeyboardInput.MouseScrollKeyboardInput>() is DV.KeyboardInput.MouseScrollKeyboardInput keys
                    && keys.scrollAction != null && keys.scrollAction.flip);
            return control;
        }

        // The firing valve: a working lever (a copy of the cab's cylinder cock lever, its own drawing hidden)
        // at the modeled lever's pivot, swinging about the cab's x from shut toward the backhead to wide open
        // back toward the fireman, wearing the modeled lever; the link rod and the regulating valve's crank
        // follow it. Null when the cab has no lever to copy.
        private static Transform PlaceFiringLever(TrainCar car, Transform interior, Transform root, Transform cabPrefab,
            S282OilTenderData.FiringValveShape shape, Dictionary<string, (Mesh Mesh, Material Material)> moving, Port port, RealisticOilFirebox oil)
        {
            var source = cabPrefab.Find(StokerCab.SourceLever);
            if (source == null || source.Find(StokerCab.SourceLeverControl) == null)
                return null;
            var holder = Object.Instantiate(source.gameObject, root, false).transform;
            holder.name = S282OilConversion.FiringValve.Id;
            var control = holder.Find(StokerCab.SourceLeverControl);
            // The control upright at the pivot in the cab's frame.
            holder.localRotation = Quaternion.Inverse(control.localRotation);
            holder.localPosition = Vector3.zero;
            holder.localPosition = V(shape.Pivot) - root.InverseTransformPoint(control.position);

            foreach (var r in holder.GetComponentsInChildren<Renderer>(true))
                r.enabled = false;
            foreach (var input in control.GetComponents<MonoBehaviour>()
                         .Where(b => StokerCab.InputComponents.Contains(b.GetType().Name) || b.GetType().Name == "PercentageControlNames")
                         .ToList())
                Object.DestroyImmediate(input);

            var spec = control.GetComponent<DV.CabControls.Spec.Lever>();
            var feeder = control.GetComponent<InteractablePortFeeder>();
            if (spec == null || feeder == null)
            {
                Object.Destroy(holder.gameObject);
                return null;
            }
            // Grabbed by the modeled lever, from its tail to the grip's top.
            var layer = spec.colliderGameObjects != null && spec.colliderGameObjects.Length > 0 && spec.colliderGameObjects[0] != null
                ? spec.colliderGameObjects[0].layer
                : control.gameObject.layer;
            foreach (var collider in holder.GetComponentsInChildren<Collider>(true).ToList())
                Object.DestroyImmediate(collider);
            if (spec.nonVrStaticInteractionArea != null)
            {
                Object.DestroyImmediate(spec.nonVrStaticInteractionArea.gameObject);
                spec.nonVrStaticInteractionArea = null;
            }
            var grab = new GameObject("firing valve grab");
            grab.layer = layer;
            grab.transform.SetParent(control, false);
            var top = shape.Length + 0.11f;
            var box = grab.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, (top - shape.Tail) / 2f, 0f);
            box.size = new Vector3(0.06f, top + shape.Tail, 0.06f);
            spec.colliderGameObjects = new[] { grab };
            spec.interactionPoint = null;

            // Shut toward the backhead (+x tips the top forward), wide open back toward the fireman.
            spec.jointAxis = Vector3.right;
            spec.useLimits = true;
            spec.jointLimitMin = shape.Open;
            spec.jointLimitMax = shape.Shut;
            spec.invertDirection = true;
            spec.useSpring = true;
            spec.useSteppedJoint = false;
            spec.steppedValueUpdate = false;
            spec.scrollWheelHoverScroll = FiringValveScrollDegrees;
            spec.jointDamper = LeverDamper;
            spec.notch = null;
            control.gameObject.AddComponent<HoldsWhereLeft>();

            // The modeled lever on the control, the link rod and the crank beside it.
            var (leverMesh, material) = moving[S282OilTenderData.FiringLever];
            var drawn = Piece(control, S282OilTenderData.FiringLever, leverMesh, material, interior.gameObject.layer);
            drawn.localPosition = Vector3.zero;
            drawn.localRotation = Quaternion.identity;
            Outline(control, drawn);
            Transform crank = null, link = null;
            if (moving.TryGetValue(S282OilTenderData.FiringCrank, out var c))
            {
                crank = Piece(root, S282OilTenderData.FiringCrank, c.Mesh, c.Material, interior.gameObject.layer);
                crank.localPosition = V(shape.Crank);
            }
            if (moving.TryGetValue(S282OilTenderData.FiringLink, out var l))
                link = Piece(root, S282OilTenderData.FiringLink, l.Mesh, l.Material, interior.gameObject.layer);
            if (crank != null && link != null)
                root.gameObject.AddComponent<FiringLinkage>().Set(root, control, crank, link, shape);

            feeder.portId = S282OilConversion.FiringValve.PortId;
            control.gameObject.AddComponent<SimControlBinder>().Bind(feeder, port);
            var hover = control.gameObject.AddComponent<HoverName>();
            if (oil != null)
                hover.Source = () => OilBurnerCab.FiringHover(oil);
            else
                hover.Text = S282OilConversion.FiringValve.Label;
            control.gameObject.AddComponent<FiringStopWhenSpawned>().Firebox = oil;
            CabKeys.Bind(interior, control, FiringValveKeys, flip: false);
            return control;
        }

        // The peephole's cover: a working control (a copy of the cab's cylinder cock lever, its drawing hidden) on
        // the cover's hinge, swinging about the cab's y from shut over the bore to open toward the fireman, wearing
        // the modeled cover; it works the burner's peephole, the way in for burning waste and flue sand. Null
        // when the cab has no lever to copy.
        private static Transform PlacePeephole(Transform interior, Transform root, Transform cabPrefab, S282OilTenderData.PeepholeShape shape,
            (Mesh Mesh, Material Material) cover, Port port, RealisticOilFirebox oil)
        {
            var source = cabPrefab.Find(StokerCab.SourceLever);
            if (source == null || source.Find(StokerCab.SourceLeverControl) == null)
                return null;
            var holder = Object.Instantiate(source.gameObject, root, false).transform;
            holder.name = OilBurnerFit.PeepHoleId;
            var control = holder.Find(StokerCab.SourceLeverControl);
            holder.localRotation = Quaternion.Inverse(control.localRotation);
            holder.localPosition = Vector3.zero;
            holder.localPosition = V(shape.Hinge) - root.InverseTransformPoint(control.position);
            foreach (var r in holder.GetComponentsInChildren<Renderer>(true))
                r.enabled = false;
            foreach (var input in control.GetComponents<MonoBehaviour>()
                         .Where(b => StokerCab.InputComponents.Contains(b.GetType().Name) || b.GetType().Name == "PercentageControlNames")
                         .ToList())
                Object.DestroyImmediate(input);
            var spec = control.GetComponent<DV.CabControls.Spec.Lever>();
            var feeder = control.GetComponent<InteractablePortFeeder>();
            if (spec == null || feeder == null)
            {
                Object.Destroy(holder.gameObject);
                return null;
            }
            var layer = spec.colliderGameObjects != null && spec.colliderGameObjects.Length > 0 && spec.colliderGameObjects[0] != null
                ? spec.colliderGameObjects[0].layer
                : control.gameObject.layer;
            foreach (var collider in holder.GetComponentsInChildren<Collider>(true).ToList())
                Object.DestroyImmediate(collider);
            if (spec.nonVrStaticInteractionArea != null)
            {
                Object.DestroyImmediate(spec.nonVrStaticInteractionArea.gameObject);
                spec.nonVrStaticInteractionArea = null;
            }
            // Grabbed by the cover, its knob and its strap.
            var hinge = V(shape.Hinge);
            var middle = V(shape.Middle) - hinge;
            var grab = new GameObject("peephole cover grab");
            grab.layer = layer;
            grab.transform.SetParent(control, false);
            var box = grab.AddComponent<BoxCollider>();
            box.center = new Vector3(middle.x / 2f - shape.CoverRadius / 2f, 0f, -0.02f);
            box.size = new Vector3(Mathf.Abs(middle.x) + shape.CoverRadius + 0.02f, 2f * shape.CoverRadius, 0.05f);
            spec.colliderGameObjects = new[] { grab };
            spec.interactionPoint = null;

            // Shut at 0 degrees, open swung toward the fireman (about the cab's y, its free edge coming back).
            spec.jointAxis = Vector3.up;
            spec.useLimits = true;
            spec.jointLimitMin = -CoverOpens;
            spec.jointLimitMax = 0f;
            spec.invertDirection = true;
            spec.useSpring = true;
            spec.useSteppedJoint = false;
            spec.steppedValueUpdate = false;
            spec.scrollWheelHoverScroll = 10f;
            spec.jointDamper = LeverDamper;
            spec.notch = null;
            control.gameObject.AddComponent<HoldsWhereLeft>();

            var drawn = Piece(control, S282OilTenderData.PeepholeCover, cover.Mesh, cover.Material, interior.gameObject.layer);
            drawn.localPosition = Vector3.zero;
            drawn.localRotation = Quaternion.identity;
            Outline(control, drawn);
            feeder.portId = OilBurnerFit.PeepHolePortId;
            control.gameObject.AddComponent<SimControlBinder>().Bind(feeder, port);
            control.gameObject.AddComponent<HoverName>().Source = () =>
                port.Value >= 0.5f
                    ? "Peephole: open; throw burning waste in to light the burner, or flue sand to scour the flues"
                    : port.Value > 0.02f ? "Peephole: cracked" : "Peephole: shut";
            return control;
        }

        private static void Outline(Transform control, Transform drawn)
        {
            var renderer = drawn.GetComponent<Renderer>();
            var holder = control.parent != null ? control.parent : control;
            foreach (var other in holder.GetComponentsInChildren<HighlightTag>(true))
                other.renderers = new List<Renderer> { renderer };
            var tag = control.GetComponent<HighlightTag>() ?? control.gameObject.AddComponent<HighlightTag>();
            tag.renderers = new List<Renderer> { renderer };
        }

        /// <summary>
        /// Where the peephole is, in the world, on a loaded S282 converted to oil
        /// (for the oil fire's flame sheet, before the cover's control is built);
        /// null otherwise.
        /// </summary>
        internal static Vector3? PeepholeWorld(TrainCar car)
        {
            var shape = IsConverted(car) ? S282OilTender.Data?.Peephole : null;
            return shape != null && car.loadedInterior != null ? CabFrame.Of(car).TransformPoint(V(shape.Middle)) : (Vector3?)null;
        }

        /// <summary>
        /// The atomizer's gauge, bar: the steam behind the atomizer valve, the
        /// boiler's (or the shed line's, stronger), let through as far as the
        /// valve is open against the burner's nozzle, about a third the nozzle's
        /// bore at full open (inferred).
        /// </summary>
        internal static float AtomizerGaugeBar(RealisticOilFirebox oil)
        {
            var supply = Mathf.Max(0f, oil.BoilerPressure - 1f);
            if (oil.Model.OnSteamLine)
                supply = Mathf.Max(supply, RealisticOilFirebox.SteamLinePressure - 1f);
            var open = 3f * oil.AtomizerValve;
            return supply * open * open / (open * open + 1f);
        }

        // A copy of the cab's own part (its meshes and materials), turned from its own axis onto the fitting's and
        // centred where the fitting wants it.
        private static string PlaceClone(Transform interior, Transform root, S282OilTenderData.Clone c)
        {
            var source = interior.Find(c.Source);
            var filter = source != null ? source.GetComponent<MeshFilter>() : null;
            var renderer = source != null ? source.GetComponent<MeshRenderer>() : null;
            if (filter == null || filter.sharedMesh == null || renderer == null)
                return $"no {c.Name} (no '{c.Source}' in the cab)";
            var sourceRotation = Quaternion.Inverse(root.rotation) * source.rotation;
            var turn = Quaternion.FromToRotation(V(c.SourceAxis), V(c.Axis)) * sourceRotation;
            var scale = Vector3.Scale(source.lossyScale, Vector3.one * c.Scale);
            var go = new GameObject(c.Name);
            go.layer = source.gameObject.layer;
            go.transform.SetParent(root, false);
            go.transform.localRotation = turn;
            go.transform.localScale = scale;
            go.transform.localPosition = V(c.At) - turn * Vector3.Scale(scale, filter.sharedMesh.bounds.center);
            go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
            return c.Name;
        }

        // A copy of the cab's own label plate, facing where the fitting's plate faces, with its text.
        private static bool PlaceLabel(Transform sourceLabel, Transform root, S282OilTenderData.Label l)
        {
            if (sourceLabel == null)
                return false;
            var label = Object.Instantiate(sourceLabel.gameObject, root, false).transform;
            label.name = "label " + l.Text;
            // The stock plate faces the cab (-z in the car's frame); turned to face the fitting's way.
            var stockRotation = Quaternion.Inverse(root.rotation) * sourceLabel.rotation;
            label.localRotation = Quaternion.LookRotation(-V(l.Facing), Vector3.up) * stockRotation;
            label.localPosition = V(l.At);
            label.localScale = sourceLabel.lossyScale;
            foreach (var localize in label.GetComponentsInChildren<MonoBehaviour>(true).Where(m => m.GetType().Name == "Localize").ToList())
                Object.DestroyImmediate(localize);
            var tmp = label.GetComponentInChildren<TextMeshPro>(true);
            if (tmp != null)
                tmp.text = l.Text;
            return tmp != null;
        }

        private static Vector3 V(float[] v) => new Vector3(v[0], v[1], v[2]);

        /// <summary>
        /// The firing valve's link rod and the regulating valve's crank, following
        /// the lever. As modeled the rod runs nearly straight down from the
        /// lever's tail pin to the crank's pin above its spindle, so the two
        /// arms swing fore and aft together: the crank turns in step with the
        /// lever, at the tail's length over the crank's so its pin keeps under
        /// the tail pin, upright at the lever's modeled notch, and the rod is
        /// stretched between the pins (within a few mm of its length).
        /// </summary>
        internal sealed class FiringLinkage : MonoBehaviour
        {
            private Transform root, lever, crank, link;
            private Vector3 tailOnLever;
            private Vector2 crankMiddle;
            private float crankRadius, linkX, shown, ratio;

            internal void Set(Transform frame, Transform leverControl, Transform crankPiece, Transform linkPiece, S282OilTenderData.FiringValveShape shape)
            {
                root = frame;
                lever = leverControl;
                crank = crankPiece;
                link = linkPiece;
                linkX = shape.LinkX;
                tailOnLever = new Vector3(shape.LinkX - shape.Pivot[0], -shape.Tail, 0f);
                crankMiddle = new Vector2(shape.Crank[1], shape.Crank[2]);
                crankRadius = shape.CrankRadius;
                shown = shape.Shown;
                ratio = shape.Tail / shape.CrankRadius;
                LateUpdate();
            }

            /// <summary>The crank's turn from upright, degrees, toward the backhead positive.</summary>
            internal float CrankAngle { get; private set; }

            /// <summary>The lever's lean from upright, degrees, toward the backhead positive.</summary>
            internal float LeverAngle { get; private set; }

            private void LateUpdate()
            {
                if (root == null || lever == null || crank == null || link == null)
                    return;
                var up = root.InverseTransformDirection(lever.up);
                LeverAngle = Mathf.Atan2(up.z, up.y) * Mathf.Rad2Deg;
                // The tail, below the pivot, swings the other way from the grip: the crank's pin goes with it.
                CrankAngle = -ratio * (LeverAngle - shown);
                crank.localRotation = Quaternion.AngleAxis(CrankAngle, Vector3.right);
                var a = CrankAngle * Mathf.Deg2Rad;
                var pin = crankMiddle + crankRadius * new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                var tail = root.InverseTransformPoint(lever.TransformPoint(tailOnLever));
                var from = new Vector3(linkX, tail.y, tail.z);
                var to = new Vector3(linkX, pin.x, pin.y);
                var along = to - from;
                link.localPosition = from;
                link.localRotation = Quaternion.FromToRotation(Vector3.up, along.normalized);
                link.localScale = new Vector3(1f, along.magnitude, 1f);
            }
        }

        /// <summary>
        /// A lever without notches that stays where it is let go, as a firing
        /// valve's quadrant lever or a cover on its hinge does: the game's own
        /// springs it back to its middle when let go (its grab handler resets its
        /// target) or when a scroll ends (<see cref="Patches.HeldLeverScrollPatch"/>
        /// keeps its target where it is).
        /// </summary>
        internal sealed class HoldsWhereLeft : MonoBehaviour
        {
            // The game passes a lever's angle on to the sim only once it has moved a degree from the last it passed
            // (LeverBase.diffThreshold): a wheel click of a degree could move the firing valve's handle and not its
            // oil (3 clicks read 1.9 % a click, job 3329). These pass every twentieth of a degree.
            private const float PassesEvery = 0.05f;
            private static readonly System.Reflection.FieldInfo Threshold = HarmonyLib.AccessTools.Field(typeof(LeverBase), "diffThreshold");
            private bool thresholdSet;

            private void Update()
            {
                if (!thresholdSet && Threshold != null && GetComponent<LeverBase>() is LeverBase lever)
                {
                    Threshold.SetValue(lever, PassesEvery);
                    thresholdSet = true;
                }
                var handler = GetComponent<DV.Interaction.GrabHandlerHingeJoint>();
                if (handler == null)
                    return;
                handler.resetTargetPositionOnUngrab = false;
                if (thresholdSet || Threshold == null)
                    enabled = false;
            }
        }

        /// <summary>The stock fire doors' control, held shut: the doors are gone, but its keys still work it.</summary>
        private sealed class DoorHeldShut : MonoBehaviour
        {
            internal Port Port { get; set; }

            private void LateUpdate()
            {
                if (Port != null && Port.Value != 0f && !Net.IsClient)
                    Port.ExternalValueUpdate(0f);
            }
        }

        /// <summary>
        /// The firepan's bricks and the peephole's lining (its glow toward the
        /// fire), as hot as the burner's brickwork (OilFire's brick temperature).
        /// </summary>
        private sealed class FirepanHeat : MonoBehaviour
        {
            private Material material;
            private Material lining;
            private RealisticOilFirebox oil;

            internal void Set(Material bricks, Material peepholeLining, RealisticOilFirebox firebox)
            {
                material = bricks;
                lining = peepholeLining;
                oil = firebox;
                LateUpdate();
            }

            private void LateUpdate()
            {
                if (material == null && lining == null)
                    return;
                var brick = oil != null ? oil.Model.BrickTemperature : Core.OilFire.ColdBrick;
                var heat = Mathf.InverseLerp(500f, Core.OilFire.HotBrick, brick);
                if (material != null)
                    BrickArch.SetHeat(material, heat);
                if (lining != null)
                    lining.SetColor(LiningEmissionId, BrickArch.HeatColor(heat) * LiningGlow);
            }

            private void OnDestroy()
            {
                if (material != null)
                    Destroy(material);
                if (lining != null)
                    Destroy(lining);
            }
        }

        // The firing valve's click for the drifting fire's stop, once the game has spawned its lever.
        private sealed class FiringStopWhenSpawned : MonoBehaviour
        {
            internal RealisticOilFirebox Firebox { get; set; }

            private void Update()
            {
                var impl = GetComponent<ControlImplBase>();
                if (impl == null)
                    return;
                FiringValveStopClick.Attach(impl, Firebox);
                Destroy(this);
            }
        }
    }
}
