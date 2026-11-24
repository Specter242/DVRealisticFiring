using System.Collections.Generic;
using RealisticFiring.Sim;
using RealisticStackEffects;
using UnityEngine;

namespace RealisticFiring.Visuals
{
    /// <summary>
    /// Light wood smoke rising from the caboose's roof stovepipe while its
    /// stove burns. Completely independent of locomotive <see cref="ChimneyPlume"/>
    /// and <c>SteamSmokeParticlePortReader</c>, so it coexists cleanly with
    /// Realistic Stack Effects while still respecting <see cref="WorldParticles"/>
    /// origin shifts and <see cref="PlumeWeather"/> wind and daylight.
    /// </summary>
    /// <remarks>
    /// The smoke is drawn with the mod's own chimney smoke shader, lit by the
    /// game's sun and sky as the locomotives' smoke is, through the injector
    /// overflow's plain material: wisps from an 8 × 8 flipbook, each particle
    /// playing one row. It needs that flipbook's tiles and the overflow's
    /// vertex streams; without them (1.24.3.1012 and before) every particle
    /// drew the whole sheet, 64 tiny wisps, and the plume read as faint specks.
    /// Budget: one shared material, one draw per burning caboose, at most
    /// <see cref="MaxParticles"/> particles (about 36 alive at the most smoke),
    /// none for a cold stove or past <see cref="MaxVisibleDistance"/>.
    /// </remarks>
    internal sealed class CabooseStoveSmoke : MonoBehaviour
    {
        // The stock CarCabooseRed's stovepipe, part of its interior mesh: about
        // 0.25 m across, centred at x -1.15, z 0.65, its top at 3.70 m, a third
        // of a metre over the roof, just ahead of the cupola (2026-11-21, its
        // mesh and colliders walked in game). The smoke starts at its mouth.
        private static readonly Vector3 DefaultChimneyLocal = new Vector3(-1.15f, 3.72f, 0.65f);
        private static readonly Vector3 StockStoveLocal = new Vector3(-0.629f, 1.429f, 0.770f);
        private const float MaxVisibleDistance = 250f;
        private const int MaxParticles = 40;

        // Emission and opacity from a faint ember wisp (Smoke01 near 0) to a
        // smoky shut-down fire (near 1).
        private const float LeastRate = 1.0f;
        private const float MostRate = 4.5f;
        private const float LeastAlpha = 0.30f;
        private const float MostAlpha = 0.62f;

        // Thin wood smoke scatters bluish; a smoky fire's is a warmer grey.
        private static readonly Color ThinSmoke = new Color(0.78f, 0.80f, 0.85f);
        private static readonly Color ThickSmoke = new Color(0.66f, 0.65f, 0.63f);

        private CabooseStoveRig rig;
        private ParticleSystem smoke;
        private ParticleSystem.Particle[] buffer;
        private bool litByScene;
        private bool originBound;
        private Vector3 chimneyLocal = DefaultChimneyLocal;
        private bool chimneyResolved;

        private static Material fallbackMaterial;

        internal void Bind(CabooseStoveRig owner)
        {
            rig = owner;
        }

        internal void Clear()
        {
            if (smoke != null)
            {
                Destroy(smoke.gameObject);
                smoke = null;
            }
            buffer = null;
            originBound = false;
            chimneyResolved = false;
        }

        private void OnDestroy() => Clear();

        internal void Tick(CabooseStoveRig owner)
        {
            rig = owner;
            if (rig == null || rig.Car == null || !rig.FeatureEnabled)
            {
                StopEmitting();
                return;
            }

            var model = rig.Model;
            var intensity = model.Smoke01;
            var active = intensity > 0.015f;

            // Distance cull far cabooses so idle or distant cars cost nothing.
            var cam = PlayerManager.ActiveCamera;
            if (cam != null && (cam.transform.position - transform.position).sqrMagnitude > MaxVisibleDistance * MaxVisibleDistance)
            {
                StopEmitting();
                return;
            }

            if (!active)
            {
                StopEmitting();
                if (smoke != null && smoke.particleCount > 0)
                    CarryParticles(Time.deltaTime);
                return;
            }

            EnsureSystem();
            if (smoke == null)
                return;

            if (!originBound)
                originBound = WorldParticles.FollowOriginShifts(smoke);

            var emission = smoke.emission;
            emission.rateOverTime = Mathf.Lerp(LeastRate, MostRate, intensity);

            // The chimney smoke shader takes its light from the scene, so it
            // darkens at dusk itself; the plain fallback glows unless dimmed.
            var tint = Color.Lerp(ThinSmoke, ThickSmoke, intensity);
            if (!litByScene)
                tint *= Mathf.Lerp(0.18f, 0.88f, PlumeWeather.Daylight);
            var alpha = Mathf.Lerp(LeastAlpha, MostAlpha, intensity);
            var main = smoke.main;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(tint.r * 0.94f, tint.g * 0.94f, tint.b * 0.94f, alpha * 0.8f),
                new Color(tint.r, tint.g, tint.b, alpha));
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.65f, 1.0f + 0.45f * model.Heat01);

            CarryParticles(Time.deltaTime);
        }

        private void StopEmitting()
        {
            if (smoke == null) return;
            var emission = smoke.emission;
            emission.rateOverTime = 0f;
        }

        private void EnsureSystem()
        {
            if (smoke != null)
                return;
            ResolveChimney();

            var holder = new GameObject("[RealisticFiring caboose chimney smoke]");
            holder.transform.SetParent(transform, false);
            holder.transform.localPosition = chimneyLocal;
            holder.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            smoke = holder.AddComponent<ParticleSystem>();
            smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = smoke.main;
            main.playOnAwake = true;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = MaxParticles;
            main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.28f, 0.42f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.65f, 1.0f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0f;

            var emission = smoke.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;

            var shape = smoke.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 8f;
            shape.radius = 0.06f;

            // Narrow at the pipe, spreading to a metre and more as it rises and thins.
            var sizeOverLife = smoke.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.5f),
                new Keyframe(0.2f, 1.3f),
                new Keyframe(1f, 4.0f)));

            var colorOverLife = smoke.colorOverLifetime;
            colorOverLife.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.08f),
                    new GradientAlphaKey(0.7f, 0.45f),
                    new GradientAlphaKey(0f, 1f),
                });
            colorOverLife.color = new ParticleSystem.MinMaxGradient(grad);

            var rotOverLife = smoke.rotationOverLifetime;
            rotOverLife.enabled = true;
            rotOverLife.z = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);

            // A gentle waver that builds as the smoke leaves the pipe.
            var noise = smoke.noise;
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(0.3f, new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.3f, 1f), new Keyframe(1f, 1f)));
            noise.frequency = 0.35f;
            noise.scrollSpeed = 0.25f;
            noise.damping = true;

            var renderer = holder.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.maxParticleSize = 3f;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            UseSmokeLook(smoke, renderer);

            originBound = WorldParticles.FollowOriginShifts(smoke);
            buffer = new ParticleSystem.Particle[MaxParticles];
            smoke.Play();
        }

        private void ResolveChimney()
        {
            if (chimneyResolved)
                return;
            chimneyResolved = true;
            chimneyLocal = DefaultChimneyLocal;

            // Check if the caboose has an explicitly named chimney/stovepipe transform.
            foreach (var t in transform.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t == transform) continue;
                var n = t.name;
                if (n.IndexOf("stovepipe", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("chimney", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    chimneyLocal = transform.InverseTransformPoint(t.position);
                    return;
                }
            }

            // If a custom caboose placed its Stove elsewhere, keep the stock pipe's place beside it.
            var cab = GetComponent<CabooseStoveCab>();
            if (cab != null && cab.StoveLocalPosition is Vector3 stovePos)
            {
                if ((stovePos - StockStoveLocal).sqrMagnitude > 0.25f)
                    chimneyLocal = new Vector3(stovePos.x + DefaultChimneyLocal.x - StockStoveLocal.x, DefaultChimneyLocal.y,
                        stovePos.z + DefaultChimneyLocal.z - StockStoveLocal.z);
            }
        }

        private void CarryParticles(float dt)
        {
            if (smoke == null || buffer == null || !(dt > 0f))
                return;
            var count = smoke.GetParticles(buffer);
            if (count <= 0)
                return;
            var targetVel = PlumeWeather.Wind + Vector3.up * 0.95f;
            var drag = 1f - Mathf.Exp(-dt * 1.1f);
            for (var i = 0; i < count; i++)
            {
                var vel = buffer[i].velocity;
                buffer[i].velocity = Vector3.Lerp(vel, targetVel, drag);
            }
            smoke.SetParticles(buffer, count);
        }

        /// <summary>
        /// The injector overflow's plain steam material (the chimney smoke
        /// shader without its rolling puffs) with that system's flipbook tiles,
        /// each particle one row of wisps, and its vertex streams; the plain
        /// fallback when the effects bundle is missing.
        /// </summary>
        private void UseSmokeLook(ParticleSystem system, ParticleSystemRenderer renderer)
        {
            var steam = PlumeAssets.OverflowPrefab != null ? PlumeAssets.OverflowPrefab.transform.Find("Steam") : null;
            var source = steam != null ? steam.GetComponent<ParticleSystemRenderer>() : null;
            var sourceSystem = steam != null ? steam.GetComponent<ParticleSystem>() : null;
            if (source == null || source.sharedMaterial == null || sourceSystem == null)
            {
                litByScene = false;
                renderer.sharedMaterial = FallbackMaterial();
                return;
            }
            litByScene = true;
            renderer.sharedMaterial = source.sharedMaterial;
            var streams = new List<ParticleSystemVertexStream>();
            source.GetActiveVertexStreams(streams);
            renderer.SetActiveVertexStreams(streams);

            var from = sourceSystem.textureSheetAnimation;
            var sheet = system.textureSheetAnimation;
            sheet.enabled = from.enabled;
            if (!from.enabled)
                return;
            sheet.mode = ParticleSystemAnimationMode.Grid;
            sheet.numTilesX = from.numTilesX;
            sheet.numTilesY = from.numTilesY;
            sheet.animation = ParticleSystemAnimationType.SingleRow;
            sheet.rowMode = ParticleSystemAnimationRowMode.Random;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
            sheet.cycleCount = 1;
        }

        private static Material FallbackMaterial()
        {
            if (fallbackMaterial != null)
                return fallbackMaterial;
            var shader = Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            fallbackMaterial = new Material(shader) { name = "RF Caboose Smoke Fallback" };
            var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[32 * 32];
            for (var y = 0; y < 32; y++)
                for (var x = 0; x < 32; x++)
                {
                    var d = new Vector2((x - 15.5f) / 15.5f, (y - 15.5f) / 15.5f).magnitude;
                    var a = Mathf.Pow(Mathf.Clamp01(1f - d), 1.8f);
                    pixels[y * 32 + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels(pixels);
            tex.Apply();
            fallbackMaterial.mainTexture = tex;
            return fallbackMaterial;
        }
    }
}
