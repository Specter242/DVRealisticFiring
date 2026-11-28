using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using RealisticFiring.Sim;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RealisticFiring.Visuals
{
 internal static class S282OilTender
    {
        internal const string ModelFile = "S282OilTender.json";
        internal const string RootName = "[RealisticFiring oil tank]";
        internal const string CollidersName = "[RealisticFiring oil tank colliders]";
        internal const string FillersName = "[RealisticFiring oil fillers]";

        private const string CapLivery = "LocoDE2";
        private const string CapPath = "[fuel tank cap]";
        private const string BodyPath = "LocoS282B_Body";
        private const string CoalPath = "Coal&Water/I_TenderCoal";
        private const string ShovelPilePath = "Coal&Water/coal pile trigger";

        // The tender body's renderers whose current material each added piece
        // wears; the gauge face comes from the S282 cab's own gauges.
        private static readonly Dictionary<string, string> MaterialSource = new Dictionary<string, string>
        {
            { "LocoS282B_Body", "LOD0/s282_tender" },
            { "LocoS282A_Interior", "LOD0/s282_tender_cab" },
        };
        private const string GaugeMaterial = "LocoS282A_Gauges";
        private const string GaugeSourceLivery = "LocoS282A";
        private const string GaugeSourcePath = "Static/Gauges";

        private static S282OilTenderData data;
        private static bool attempted;
        private static Mesh[][] meshes;
        private static float nextCheck;

        // Each S282 tender as last dressed: oil (true) or coal.
        private static readonly Dictionary<TrainCar, bool> Dressed = new Dictionary<TrainCar, bool>();

        /// <summary>
        /// Whether <paramref name="car"/> is an S282 tender carrying oil: coupled
        /// behind an S282 built to burn oil, or standing uncoupled as it last was.
        /// </summary>
        internal static bool IsConverted(TrainCar car)
        {
            if (car == null || car.carLivery == null || car.carLivery.id != S282OilTenderData.Livery || !Main.Session.S282OilBurner)
                return false;
            var engine = S282OilConversion.EngineAhead(car);
            if (engine != null)
                return S282OilConversion.IsOil(engine);
            return Dressed.TryGetValue(car, out var oil) && oil;
        }

        /// <summary>Dresses every loaded S282 tender as its engine burns: the tank fitted or taken off, the coal hidden or shown.</summary>
        internal static void EnsureFitted()
        {
            if (!Main.Session.S282OilBurner || Time.unscaledTime < nextCheck)
                return;
            nextCheck = Time.unscaledTime + 1f;
            var spawner = CarSpawner.Instance;
            if (spawner == null || spawner.AllCars == null)
                return;
            foreach (var stale in Dressed.Keys.Where(c => c == null).ToList())
                Dressed.Remove(stale);
            foreach (var car in spawner.AllCars)
            {
                if (car == null || car.carLivery == null || car.carLivery.id != S282OilTenderData.Livery)
                    continue;
                var oil = IsConverted(car);
                var was = Dressed.TryGetValue(car, out var dressed) && dressed;
                // Never dressed and burning coal: nothing to do.
                if (!oil && !was && car.transform.Find(BodyPath)?.Find(RootName) == null)
                {
                    Dressed[car] = false;
                    continue;
                }
                Dressed[car] = oil;
                if (oil != was)
                    Patches.OilTenderPitStopPatch.Refit(car);
                var note = oil ? Fit(car) : Undress(car);
                if (note != null)
                    Main.Mod.Logger.Log($"{car.ID}: {note}");
            }
        }

        private static string Fit(TrainCar car)
        {
            if (!Load())
                return null;
            var notes = new List<string>();
            var body = car.transform.Find(BodyPath);
            if (body != null)
            {
                var root = body.Find(RootName);
                if (root == null)
                    notes.Add(FitBody(car, body));
                else
                {
                    if (!root.gameObject.activeSelf)
                    {
                        root.gameObject.SetActive(true);
                        notes.Add("oil tank back on (coupled behind an oil burner)");
                    }
                    SyncMaterials(body, root);
                }
            }
            foreach (var outside in new[] { car.loadedExternalInteractables, car.loadedDummyExternalInteractables })
            {
                if (outside != null && outside.transform.Find(CollidersName) == null)
                    notes.Add(DressOutside(outside.transform));
            }
            notes.Add(FitFillers(car));
            notes.RemoveAll(n => n == null);
            return notes.Count > 0 ? string.Join("; ", notes) : null;
        }

        // The fuel station's hose sockets: the stock diesels' fuel tank caps on the fillers, one each side.
        private static string FitFillers(TrainCar car)
        {
            var fillers = car.transform.Find(FillersName);
            if (fillers != null)
            {
                if (fillers.gameObject.activeSelf)
                    return null;
                fillers.gameObject.SetActive(true);
                return "fuel fillers back on";
            }
            if (data.Fillers == null || data.Fillers.Length == 0)
                return null;
            if (!DV.Globals.G.Types.TryGetLivery(CapLivery, out var livery) || livery.prefab == null)
                return "no fuel fillers (no DE2 to take its fuel tank cap from)";
            var caps = livery.prefab.transform.Find(CapPath);
            var right = caps != null ? caps.Cast<Transform>().FirstOrDefault(t => t.localScale.x > 0f) : null;
            var left = caps != null ? caps.Cast<Transform>().FirstOrDefault(t => t.localScale.x < 0f) : null;
            if (right == null)
                return "no fuel fillers (the DE2 has no fuel tank cap)";
            var root = new GameObject(FillersName).transform;
            root.gameObject.SetActive(false);
            root.SetParent(car.transform, false);
            var notes = new List<string>();
            foreach (var f in data.Fillers)
            {
                var source = f.Side < 0 && left != null ? left : right;
                var cap = Object.Instantiate(source.gameObject, root, false).transform;
                cap.name = f.Side < 0 ? "fuel filler left" : "fuel filler right";
                cap.localPosition = new Vector3(f.At[0], f.At[1], f.At[2]);
                cap.localRotation = source.localRotation;
                var scale = source.localScale;
                // A right cap mirrored for the left, where the DE2 has none.
                if (f.Side < 0 && left == null)
                    scale.x = -scale.x;
                cap.localScale = scale;
                var socket = cap.GetComponentInChildren<PlugSocket>(true);
                notes.Add($"{cap.name}{(socket != null ? $" (socket '{socket.socketTag}' for '{socket.connectionTag}')" : " (no socket)")}");
            }
            root.gameObject.SetActive(true);
            return "fuel fillers: " + string.Join(", ", notes);
        }

        // Behind a coal engine: the tank off, the coal heap, its colliders, and the shovel's pile back.
        private static string Undress(TrainCar car)
        {
            var notes = new List<string>();
            var fillers = car.transform.Find(FillersName);
            if (fillers != null && fillers.gameObject.activeSelf)
            {
                fillers.gameObject.SetActive(false);
                notes.Add("fuel fillers off");
            }
            var root = car.transform.Find(BodyPath)?.Find(RootName);
            if (root != null && root.gameObject.activeSelf)
            {
                root.gameObject.SetActive(false);
                notes.Add("oil tank off (coupled behind a coal engine)");
            }
            foreach (var outside in new[] { car.loadedExternalInteractables, car.loadedDummyExternalInteractables })
            {
                var ours = outside != null ? outside.transform.Find(CollidersName) : null;
                if (ours == null)
                    continue;
                var hidden = ours.GetComponent<HiddenCoal>();
                if (hidden != null)
                {
                    foreach (var renderer in hidden.Renderers.Where(r => r != null))
                        renderer.enabled = true;
                    foreach (var collider in hidden.Colliders.Where(c => c != null))
                        collider.enabled = true;
                    if (hidden.Pile != null)
                        hidden.Pile.SetActive(true);
                }
                Object.Destroy(ours.gameObject);
                notes.Add("coal heap back");
            }
            return notes.Count > 0 ? string.Join("; ", notes) : null;
        }

        // The coal's drawing, colliders, and the shovel's pile as dressing found them shown.
        private sealed class HiddenCoal : MonoBehaviour
        {
            public readonly List<Renderer> Renderers = new List<Renderer>();
            public readonly List<Collider> Colliders = new List<Collider>();
            public GameObject Pile;
        }

        /// <summary>The loaded model, or null (missing or invalid; logged once).</summary>
        internal static S282OilTenderData Data => Load() ? data : null;

        private static bool Load()
        {
            if (attempted)
                return data != null;
            attempted = true;
            var path = Path.Combine(Main.Mod.Path, ModelFile);
            try
            {
                if (!File.Exists(path)) throw new FileNotFoundException("no " + ModelFile + " beside the mod");
                if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("Model file is too large");
                var loaded = JsonConvert.DeserializeObject<S282OilTenderData>(File.ReadAllText(path));
                if (loaded == null) throw new InvalidDataException("Empty oil tender model");
                loaded.Validate();
                data = loaded;
                meshes = new Mesh[4][];
                for (var lod = 0; lod < 4; lod++)
                    meshes[lod] = data.Meshes.Where(p => p.Lod == lod).Select(Create).ToArray();
                return true;
            }
            catch (Exception e)
            {
                Main.Mod.Logger.Warning("S282 tender left with its coal: " + e.Message);
                return false;
            }
        }

        internal static Mesh Create(S282OilTenderData.Piece p)
        {
            var mesh = new Mesh { name = $"S282 oil tender {p.Name} LOD{p.Lod}" };
            mesh.vertices = p.Vertices.Select(v => new Vector3(v[0], v[1], v[2])).ToArray();
            mesh.normals = p.Normals.Select(v => new Vector3(v[0], v[1], v[2])).ToArray();
            mesh.uv = p.Uv.Select(v => new Vector2(v[0], v[1])).ToArray();
            if (p.Colors != null)
                mesh.colors = p.Colors.Select(c => new Color(c[0], c[1], c[2], c[3])).ToArray();
            mesh.triangles = p.Triangles;
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            mesh.UploadMeshData(true);
            return mesh;
        }

        // The tank on the body, its pieces joined to the body's LOD levels.
        private static string FitBody(TrainCar car, Transform body)
        {
            var lodGroup = body.GetComponent<LODGroup>();
            var materials = MaterialSource.ToDictionary(kv => kv.Key, kv => body.Find(kv.Value)?.GetComponent<MeshRenderer>());
            if (materials.Values.Any(r => r == null || r.sharedMaterial == null))
                return "oil tank: the tender body's renderers changed; left with its coal";
            var gauge = GaugeRenderer();
            var root = new GameObject(RootName).transform;
            root.SetParent(body, false);
            var lods = lodGroup != null ? lodGroup.GetLODs() : null;
            var pieces = data.Meshes.ToArray();
            var added = 0;
            for (var lod = 0; lod < 4; lod++)
            {
                var level = lods != null ? Math.Min(lod, lods.Length - 1) : 0;
                var renderers = new List<Renderer>();
                var lodPieces = pieces.Where(p => p.Lod == lod).ToArray();
                for (var i = 0; i < lodPieces.Length; i++)
                {
                    var source = lodPieces[i].Material == GaugeMaterial ? gauge : materials.TryGetValue(lodPieces[i].Material, out var r) ? r : null;
                    if (source == null || source.sharedMaterial == null)
                        continue;
                    var go = new GameObject($"{lodPieces[i].Name} LOD{lod}");
                    go.layer = body.gameObject.layer;
                    go.transform.SetParent(root, false);
                    go.AddComponent<MeshFilter>().sharedMesh = meshes[lod][i];
                    var renderer = go.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = source.sharedMaterial;
                    renderer.shadowCastingMode = source.shadowCastingMode;
                    renderer.receiveShadows = source.receiveShadows;
                    renderer.lightProbeUsage = source.lightProbeUsage;
                    renderer.reflectionProbeUsage = source.reflectionProbeUsage;
                    renderer.probeAnchor = source.probeAnchor;
                    renderers.Add(renderer);
                    added++;
                }
                if (lods != null && level == lod)
                    lods[level].renderers = lods[level].renderers.Concat(renderers).ToArray();
                else if (lod > 0)
                    // No such level on this body: the pieces would draw at every distance, over the nearer ones.
                    foreach (var r in renderers) r.gameObject.SetActive(false);
            }
            if (lods != null)
            {
                lodGroup.SetLODs(lods);
                lodGroup.RecalculateBounds();
            }
            foreach (var g in data.TenderGauges ?? new S282OilTenderData.Gauge[0])
            {
                var hand = root.Find($"{g.Needle} LOD0");
                if (hand != null)
                    GaugeNeedle.Fit(hand, g, () => OilLevel(car));
            }
            return $"oil tank fitted in the tender's coal space ({added} pieces over {(lods != null ? lods.Length : 1)} LODs)";
        }

        // How full the tender's oil is, 0 to 1: its coal container's, which holds the oil.
        private static float OilLevel(TrainCar tender)
        {
            var flow = tender != null && tender.SimController != null ? tender.SimController.SimulationFlow : null;
            var container = flow?.OrderedSimComps.OfType<LocoSim.Implementations.ResourceContainer>()
                .FirstOrDefault(c => c.resourceType == LocoSim.Resources.ResourceContainerType.COAL);
            return container != null ? Mathf.Clamp01(container.normalizedReadOutPort.Value) : 0f;
        }

        // The S282 cab's gauges, for the oil level dial's face; null without them (the dial's face is left out).
        private static MeshRenderer GaugeRenderer()
        {
            if (!DV.Globals.G.Types.TryGetLivery(GaugeSourceLivery, out var livery) || livery.interiorPrefab == null)
                return null;
            return livery.interiorPrefab.transform.Find(GaugeSourcePath)?.GetComponent<MeshRenderer>();
        }

        // A repaint swaps the body's materials: the tank follows.
        private static void SyncMaterials(Transform body, Transform root)
        {
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var piece = data.Meshes.FirstOrDefault(p => renderer.name == $"{p.Name} LOD{p.Lod}");
                if (piece == null || !MaterialSource.TryGetValue(piece.Material, out var sourcePath))
                    continue;
                var source = body.Find(sourcePath)?.GetComponent<MeshRenderer>();
                if (source != null && source.sharedMaterial != null && renderer.sharedMaterial != source.sharedMaterial)
                    renderer.sharedMaterial = source.sharedMaterial;
            }
        }

        // The outside parts: the coal heap hidden, its colliders off, the
        // shovel's pile gone, and the tank's own colliders where the coal's were.
        private static string DressOutside(Transform outside)
        {
            var coal = outside.Find(CoalPath);
            if (coal == null)
                return "oil tank: no coal heap in the tender's outside parts; left as it is";
            int walkLayer = -1, itemLayer = -1;
            var colliders = new GameObject(CollidersName).transform;
            colliders.SetParent(outside, false);
            // What it hides, to show again behind a coal engine.
            var hidden = colliders.gameObject.AddComponent<HiddenCoal>();
            foreach (var renderer in coal.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.enabled)
                    hidden.Renderers.Add(renderer);
                renderer.enabled = false;
            }
            foreach (var collider in coal.GetComponentsInChildren<Collider>(true))
            {
                if (collider.name.Contains("walkable") && walkLayer < 0) walkLayer = collider.gameObject.layer;
                if (collider.name.Contains("item_collider") && itemLayer < 0) itemLayer = collider.gameObject.layer;
                if (collider.enabled)
                    hidden.Colliders.Add(collider);
                collider.enabled = false;
            }
            var pile = outside.Find(ShovelPilePath);
            if (pile != null)
            {
                hidden.Pile = pile.gameObject.activeSelf ? pile.gameObject : null;
                pile.gameObject.SetActive(false);
            }
            foreach (var layer in new[] { walkLayer, itemLayer }.Where(l => l >= 0).Distinct())
            {
                foreach (var b in data.Colliders)
                {
                    var go = new GameObject($"{b.Name} ({LayerMask.LayerToName(layer)})");
                    go.layer = layer;
                    go.transform.SetParent(colliders, false);
                    go.transform.localPosition = new Vector3(b.Center[0], b.Center[1], b.Center[2]);
                    go.transform.localRotation = Quaternion.Euler(b.RotationX, 0f, 0f);
                    go.AddComponent<BoxCollider>().size = new Vector3(b.Size[0], b.Size[1], b.Size[2]);
                }
            }
            return $"coal heap hidden{(pile != null ? ", shovel pile off" : "")}, tank colliders on layers {LayerMask.LayerToName(walkLayer)} and {LayerMask.LayerToName(itemLayer)}";
        }
    }
}
