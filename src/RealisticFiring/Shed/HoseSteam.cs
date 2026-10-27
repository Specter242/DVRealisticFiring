using System.Linq;
using RealisticFiring.Visuals;
using RealisticStackEffects;
using UnityEngine;

namespace RealisticFiring.Shed
{
 internal sealed class HoseSteam : MonoBehaviour
    {
        private const string HissClipName = "SteamRelease_01";

        // Wide open: particles a second, their speed out of the nozzle, m/s,
        // and the roar's volume and pitch.
        private const float MostParticles = 140f;
        private static readonly (float Min, float Max) JetSpeed = (6f, 12f);
        private const float LoudestHiss = 0.7f;
        private const float HissPitch = 1.15f;
        private const float HissTryEvery = 5f;

        private static AudioClip hissClip;

        private PluggableObject plug;
        private PlugSocket rest;
        private ParticleSystem jet;
        private AudioSource hiss;
        private float hissTryAt;

        /// <summary>Fits the blowing steam to <paramref name="hose"/>'s plug. Returns whether it has a jet to show.</summary>
        public static bool Fit(PropHose hose)
        {
            if (hose == null || hose.plug == null)
                return false;
            var go = new GameObject("RealisticFiring hose steam");
            go.transform.SetParent(hose.plug.transform, false);
            // Out of the nozzle at the plug's origin, down its -y, the way it
            // goes into a socket.
            go.transform.localRotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            var steam = go.AddComponent<HoseSteam>();
            steam.plug = hose.plug;
            steam.rest = hose.stationSocket;
            steam.jet = Jet(go.transform);
            return steam.jet != null;
        }

        private static ParticleSystem Jet(Transform parent)
        {
            var prefab = PlumeAssets.OverflowPrefab;
            if (prefab == null)
                return null;
            var fx = Instantiate(prefab, parent, false);
            fx.name = "jet";
            var water = fx.transform.Find("Water");
            if (water != null)
                Destroy(water.gameObject);
            var system = fx.transform.Find("Steam")?.GetComponent<ParticleSystem>();
            if (system == null)
            {
                Destroy(fx);
                return null;
            }
            var main = system.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(JetSpeed.Min, JetSpeed.Max);
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
            main.gravityModifier = -0.1f;
            main.maxParticles = 300;
            // It spreads as it slows, the jet's speed spent within a metre or two.
            var size = system.sizeOverLifetime;
            size.sizeMultiplier = 4f;
            var slowing = system.limitVelocityOverLifetime;
            slowing.enabled = true;
            slowing.limit = 1f;
            slowing.dampen = 0.15f;
            var shape = system.shape;
            shape.angle = 6f;
            shape.radius = 0.01f;
            WorldParticles.FollowOriginShifts(system);
            var emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            system.Play();
            return system;
        }

        private void LateUpdate()
        {
            if (plug == null)
                return;
            var inEngine = plug.State == PluggableObject.PluggableState.PluggedIn && plug.Socket != null && plug.Socket != rest;
            var share = inEngine ? 0f : ShedStands.SteamOpening(plug);
            if (jet != null)
            {
                var emission = jet.emission;
                emission.rateOverTime = MostParticles * share;
                var main = jet.main;
                main.startSpeedMultiplier = Mathf.Lerp(0.3f, 1f, share);
            }
            if (hiss == null && share > 0f && Time.time >= hissTryAt)
            {
                hissTryAt = Time.time + HissTryEvery;
                hiss = Hiss();
            }
            if (hiss != null)
            {
                var volume = LoudestHiss * share * Mathf.Max(0f, Main.Settings.SteamVentVolume);
                hiss.volume = volume;
                if (volume > 0.01f && !hiss.isPlaying)
                    hiss.Play();
                else if (volume <= 0.01f && hiss.isPlaying)
                    hiss.Stop();
            }
        }

        // The roar, on the game's steam release sound, through the plug's own
        // sounds' mixer.
        private AudioSource Hiss()
        {
            if (hissClip == null)
                hissClip = Resources.FindObjectsOfTypeAll<AudioClip>().FirstOrDefault(c => c.name == HissClipName);
            if (hissClip == null)
                return null;
            var mixer = plug.GetComponentsInChildren<AudioSource>(true).Select(s => s.outputAudioMixerGroup).FirstOrDefault(g => g != null);
            var source = gameObject.AddComponent<AudioSource>();
            source.clip = hissClip;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 2f;
            source.maxDistance = 35f;
            source.dopplerLevel = 0f;
            source.pitch = HissPitch;
            source.volume = 0f;
            source.outputAudioMixerGroup = mixer;
            return source;
        }
    }
}
