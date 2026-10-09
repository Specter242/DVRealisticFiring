using RealisticFiring.Core;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Visuals
{
    /// <summary>
    /// The stoker's coal flying off the distributing table just inside the
    /// door: dark lumps on arcs to the zones the jets reach, as the stoker's
    /// last distribution sends them, and glowing fines streaming over the bed;
    /// fines lost up the stack or over the arch fly on past the front. Seen through the open
    /// door. Simulated in the firebox's own frame, so it moves with the loco.
    /// </summary>
    internal sealed class StokerSpray : MonoBehaviour
    {
        // Firebox coal is 1–2 in. stoker coal; about 20 lumps to the kg.
        private const float LumpsPerKg = 20f;
        private const float FinesPerKg = 60f;
        private const int MaxPerFrame = 6;

        private const float LumpMinSize = 0.025f;
        private const float LumpMaxSize = 0.045f;
        private const float FineMinSize = 0.015f;
        private const float FineMaxSize = 0.03f;

        // The table, as a point in the bed's box: middle, just inside the
        // door, a little above a full bed.
        private static readonly Vector3 Table = new Vector3(0.5f, 0.02f, 1.3f);

        // Flight time from the table: quickest to the back, longest to the front, in s.
        private const float ShortestFlight = 0.18f;
        private const float FlightPerMetre = 0.22f;

        private static readonly Color FineColor = new Color(1f, 0.6f, 0.25f);

        private static Material lumpMaterial;

        private RealisticFirebox firebox;
        private ZonedCoalBed bed;
        private ParticleSystem lumps;
        private ParticleSystem fines;
        private float lumpsDue;
        private float finesDue;

        /// <summary>Adds the spray to a firebox's zoned coal bed.</summary>
        public static void Attach(ZonedCoalBed bed, RealisticFirebox firebox)
        {
            if (bed == null || bed.GetComponent<StokerSpray>() != null)
                return;
            var spray = bed.gameObject.AddComponent<StokerSpray>();
            spray.bed = bed;
            spray.firebox = firebox;
            spray.lumps = spray.CreateSystem("stoker lumps", lumpRenderer: true);
            spray.fines = SparkMaterial() != null ? spray.CreateSystem("stoker fines", lumpRenderer: false) : null;
        }

        private ParticleSystem CreateSystem(string name, bool lumpRenderer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.gravityModifier = 1f;
            main.maxParticles = 400;
            main.startSpeed = 0f;
            main.startRotation3D = lumpRenderer;
            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.enabled = false;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            var cube = lumpRenderer ? Resources.GetBuiltinResource<Mesh>("Cube.fbx") : null;
            if (cube != null)
            {
                renderer.renderMode = ParticleSystemRenderMode.Mesh;
                renderer.mesh = cube;
                renderer.sharedMaterial = LumpMaterial();
                var rotation = system.rotationOverLifetime;
                rotation.enabled = true;
                rotation.separateAxes = true;
                rotation.x = new ParticleSystem.MinMaxCurve(-12f, 12f);
                rotation.y = new ParticleSystem.MinMaxCurve(-12f, 12f);
                rotation.z = new ParticleSystem.MinMaxCurve(-12f, 12f);
            }
            else
            {
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.sharedMaterial = lumpRenderer ? LumpMaterial() : SparkMaterial();
            }
            system.Play();
            return system;
        }

        private void LateUpdate()
        {
            if (firebox == null || bed == null || !bed.Ready || lumps == null)
                return;
            var stoker = firebox.StokerModel;
            if (stoker == null || !(firebox.LastStoker is StokerFlow flow) || !(flow.Fed > 0f))
                return;

            var dt = Time.deltaTime;
            var fedRate = stoker.EngineSpeed * stoker.Spec.MaxFeedRate;
            var lumpShare = 0f;
            var fineShare = 0f;
            for (var i = 0; i < GrateLayout.ZoneCount; i++)
            {
                lumpShare += stoker.LumpShares[i];
                fineShare += stoker.FineShares[i];
            }

            var fineTotal = fineShare + stoker.LostShare;
            lumpsDue = Mathf.Min(lumpsDue + fedRate * lumpShare * LumpsPerKg * dt, MaxPerFrame);
            finesDue = Mathf.Min(finesDue + fedRate * fineTotal * FinesPerKg * dt, MaxPerFrame);
            var gravity = transform.InverseTransformDirection(Physics.gravity);
            var from = transform.InverseTransformPoint(bed.BedPoint(Table.x, Table.y, Table.z));

            for (; lumpsDue >= 1f; lumpsDue -= 1f)
            {
                var target = Pick(stoker, lumps: true, lumpShare, out var overArch);
                Throw(lumps, from, target, gravity, Random.Range(LumpMinSize, LumpMaxSize), new Color(0.06f, 0.055f, 0.05f), overArch);
            }
            for (; finesDue >= 1f && fines != null; finesDue -= 1f)
            {
                var target = Pick(stoker, lumps: false, fineTotal, out var overArch);
                Throw(fines, from, target, gravity, Random.Range(FineMinSize, FineMaxSize), FineColor, overArch);
            }
        }

        // A landing point for the next piece, by the stoker's last distribution,
        // in the spray's frame; coal lost goes past the front, high.
        private Vector3 Pick(Stoker stoker, bool lumps, float total, out bool overArch)
        {
            var roll = Random.value * total;
            var shares = lumps ? stoker.LumpShares : stoker.FineShares;
            overArch = false;
            for (var i = 0; i < GrateLayout.ZoneCount; i++)
            {
                roll -= shares[i];
                if (roll > 0f)
                    continue;
                var zone = (GrateZone)i;
                var across = (GrateLayout.Column(zone) + Random.value) / GrateLayout.Columns;
                var along = (GrateLayout.Row(zone) + Random.value) / GrateLayout.Rows;
                return transform.InverseTransformPoint(bed.BedPoint(across, along, bed.ZoneHeight(zone)));
            }
            overArch = true;
            return transform.InverseTransformPoint(bed.BedPoint(Random.value, 1.15f, 3f));
        }

        // Launches a piece on the arc that lands on the target under gravity.
        private static void Throw(ParticleSystem system, Vector3 from, Vector3 to, Vector3 gravity, float size, Color color, bool overArch)
        {
            var distance = Vector3.Distance(from, to);
            var flight = ShortestFlight + FlightPerMetre * distance;
            var velocity = (to - from - 0.5f * gravity * flight * flight) / flight;
            system.Emit(new ParticleSystem.EmitParams
            {
                position = from + Random.insideUnitSphere * 0.03f,
                velocity = velocity,
                startLifetime = overArch ? flight * 0.8f : flight,
                startSize = size,
                startColor = color,
                rotation3D = Random.insideUnitSphere * 180f,
                applyShapeToPosition = false,
            }, 1);
        }

        private static Material LumpMaterial()
        {
            if (lumpMaterial != null)
                return lumpMaterial;
            lumpMaterial = new Material(Shader.Find("Standard")) { name = "RealisticFiring stoker coal", color = new Color(0.06f, 0.055f, 0.05f) };
            lumpMaterial.SetFloat("_Glossiness", 0.45f);
            lumpMaterial.EnableKeyword("_EMISSION");
            lumpMaterial.SetColor("_EmissionColor", new Color(0.05f, 0.012f, 0f));
            return lumpMaterial;
        }

        // The chimney's own cinder material, from the effects bundle.
        private static Material SparkMaterial()
        {
            var prefab = PlumeAssets.Prefab;
            var sparks = prefab != null ? prefab.transform.Find("Sparks") : null;
            var renderer = sparks != null ? sparks.GetComponent<ParticleSystemRenderer>() : null;
            return renderer != null ? renderer.sharedMaterial : null;
        }
    }
}
