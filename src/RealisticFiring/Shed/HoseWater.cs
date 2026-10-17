using System.Linq;
using RealisticFiring.Visuals;
using UnityEngine;

namespace RealisticFiring.Shed
{
 internal sealed class HoseWater : MonoBehaviour
    {
        private const string SoundClipName = "WaterDump_01";

        // Wide open: particles a second, and their speed out of the coupling,
        // m/s (15 kg/s through a 1½-in. bore leaves at about 9 m/s; the
        // stream's look, not its flow); the sound's volume.
        private const float MostParticles = 160f;
        private static readonly (float Min, float Max) JetSpeed = (4f, 6.5f);
        private const float Loudest = 0.6f;
        private const float SoundTryEvery = 5f;

        private static AudioClip soundClip;

        private PluggableObject plug;
        private PlugSocket rest;
        private ParticleSystem stream;
        private AudioSource sound;
        private float soundTryAt;

        /// <summary>Fits the running water to <paramref name="hose"/>'s coupling. Returns whether it has a stream to show.</summary>
        public static bool Fit(PropHose hose)
        {
            if (hose == null || hose.plug == null)
                return false;
            var go = new GameObject("RealisticFiring hose water");
            go.transform.SetParent(hose.plug.transform, false);
            // Out of the coupling at the plug's origin, down its -y, the way
            // it goes into a socket.
            go.transform.localRotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            var water = go.AddComponent<HoseWater>();
            water.plug = hose.plug;
            water.rest = hose.stationSocket;
            water.stream = Stream(go.transform);
            return water.stream != null;
        }

        private static ParticleSystem Stream(Transform parent)
        {
            var prefab = PlumeAssets.OverflowPrefab;
            if (prefab == null)
                return null;
            var fx = Instantiate(prefab, parent, false);
            fx.name = "stream";
            var steam = fx.transform.Find("Steam");
            if (steam != null)
                Destroy(steam.gameObject);
            var system = fx.transform.Find("Water")?.GetComponent<ParticleSystem>();
            if (system == null)
            {
                Destroy(fx);
                return null;
            }
            var main = system.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(JetSpeed.Min, JetSpeed.Max);
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.08f);
            main.gravityModifier = 1f;
            main.maxParticles = 300;
            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 4f;
            shape.radius = 0.015f;
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
            var share = inEngine ? 0f : ShedStands.Opening(plug);
            if (stream != null)
            {
                var emission = stream.emission;
                emission.rateOverTime = MostParticles * share;
                var main = stream.main;
                main.startSpeedMultiplier = Mathf.Lerp(0.4f, 1f, share);
            }
            if (sound == null && share > 0f && Time.time >= soundTryAt)
            {
                soundTryAt = Time.time + SoundTryEvery;
                sound = Sound();
            }
            if (sound != null)
            {
                sound.volume = Loudest * share;
                if (share > 0.01f && !sound.isPlaying)
                    sound.Play();
                else if (share <= 0.01f && sound.isPlaying)
                    sound.Stop();
            }
        }

        // The running water, on the game's water-dump sound, through the
        // coupling's own sounds' mixer.
        private AudioSource Sound()
        {
            if (soundClip == null)
                soundClip = Resources.FindObjectsOfTypeAll<AudioClip>().FirstOrDefault(c => c.name == SoundClipName);
            if (soundClip == null)
                return null;
            var mixer = plug.GetComponentsInChildren<AudioSource>(true).Select(s => s.outputAudioMixerGroup).FirstOrDefault(g => g != null);
            var source = gameObject.AddComponent<AudioSource>();
            source.clip = soundClip;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 2f;
            source.maxDistance = 25f;
            source.dopplerLevel = 0f;
            source.volume = 0f;
            source.outputAudioMixerGroup = mixer;
            return source;
        }
    }
}
