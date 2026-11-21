using RealisticFiring.Sim;
using RealisticStackEffects;
using UnityEngine;

namespace RealisticFiring.Visuals
{
    /// <summary>
    /// Gentle wood-smoke chimney plume rising from the caboose's roof stovepipe.
    /// Completely independent of locomotive <see cref="ChimneyPlume"/> and
    /// <c>SteamSmokeParticlePortReader</c>, so it coexists cleanly with
    /// Realistic Stack Effects while still respecting <see cref="WorldParticles"/>
    /// origin shifts and <see cref="PlumeWeather"/> wind and daylight.
    /// </summary>
    internal sealed class CabooseStoveSmoke : MonoBehaviour
    {
        // Stock CarCabooseRed roof exit directly over the stove (-0.63.. -1.25, z ~ 0.65, roof y ~ 3.48).
        private static readonly Vector3 DefaultChimneyLocal = new Vector3(-0.98f, 3.60f, 0.65f);
        private const float MaxVisibleDistance = 250f;
        private const int MaxParticles = 160;

        private CabooseStoveRig rig;
        private ParticleSystem smoke;
        private ParticleSystem.Particle[] buffer;
        private Material ownMaterial;
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
            if (ownMaterial != null)
            {
                Destroy(ownMaterial);
                ownMaterial = null;
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
            emission.rateOverTime = Mathf.Lerp(1.5f, 7.5f, intensity);

            // Light wood-smoke grey-blue tinted by daylight so night smoke never glows.
            var light = Mathf.Lerp(0.18f, 0.88f, PlumeWeather.Daylight);
            var alpha = Mathf.Lerp(0.14f, 0.34f, intensity);
            var main = smoke.main;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.68f * light, 0.70f * light, 0.74f * light, alpha * 0.8f),
                new Color(0.80f * light, 0.81f * light, 0.84f * light, alpha));
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.65f, 1.15f + 0.45f * model.Heat01);

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
            var main = smoke.main;
            main.playOnAwake = true;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = MaxParticles;
            main.startLifetime = new ParticleSystem.MinMaxCurve(5.5f, 9.0f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.28f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.7f, 1.2f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0f;

            var emission = smoke.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;

            var shape = smoke.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 10f;
            shape.radius = 0.065f;

            var sizeOverLife = smoke.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.65f),
                new Keyframe(0.25f, 1.8f),
                new Keyframe(1f, 4.6f)));

            var colorOverLife = smoke.colorOverLifetime;
            colorOverLife.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.10f),
                    new GradientAlphaKey(0.65f, 0.55f),
                    new GradientAlphaKey(0f, 1f),
                });
            colorOverLife.color = new ParticleSystem.MinMaxGradient(grad);

            var rotOverLife = smoke.rotationOverLifetime;
            rotOverLife.enabled = true;
            rotOverLife.z = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f);

            var renderer = holder.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = SmokeMaterial();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

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

            // If a custom caboose placed its Stove elsewhere, align X/Z over it.
            var cab = GetComponent<CabooseStoveCab>();
            if (cab != null && cab.StoveLocalPosition is Vector3 stovePos)
            {
                if ((stovePos - new Vector3(-0.629f, 1.429f, 0.770f)).sqrMagnitude > 0.25f)
                    chimneyLocal = new Vector3(stovePos.x - 0.30f, DefaultChimneyLocal.y, stovePos.z - 0.12f);
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

        private Material SmokeMaterial()
        {
            // Reuse RF's own smoke material when the FX bundle is present (with six-way/wisp
            // shader extras switched off, matching PlumeAssets.PlainOverflowSteam).
            var source = PlumeAssets.OverflowPrefab != null
                ? PlumeAssets.OverflowPrefab.transform.Find("Steam")?.GetComponent<ParticleSystemRenderer>()?.sharedMaterial
                : null;
            if (source != null)
            {
                ownMaterial = new Material(source) { name = "RF Caboose Wood Smoke" };
                return ownMaterial;
            }
            return FallbackMaterial();
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
