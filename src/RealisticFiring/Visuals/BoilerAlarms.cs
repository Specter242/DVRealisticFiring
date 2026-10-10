using System.Linq;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Visuals
{
 internal sealed class BoilerAlarms : MonoBehaviour
    {
        private const string WhistleClipName = "Whistle_07_Breathy";
        private const string BlowClipName = "SteamRelease_01";

        // A small alarm whistle sounds far above the engine's chime.
        private const float WhistlePitch = 2.4f;
        private const float WhistleVolume = 0.7f;

        // The alarm whistle sits this far above the fire, over the backhead.
        private const float WhistleAboveFire = 1.3f;

        // A plug blowing this hard, in kg/s, roars at full volume.
        private const float LoudestBlow = 0.6f;
        private const float BlowPitch = 0.75f;

        private BoilerWaterRig rig;
        private AudioSource whistle;
        private AudioSource blow;

        /// <summary>
        /// Gives <paramref name="car"/>'s boiler its alarm or plug sounds, at the
        /// fire. Returns a line for the log, or null without either guard.
        /// </summary>
        public static string Attach(TrainCar car, Fire fire)
        {
            if (!BoilerWaterRig.TryGet(car, out var rig) || (rig.Alarm == null && rig.Plug == null))
                return null;
            var alarms = fire.GetComponent<BoilerAlarms>();
            if (alarms == null)
                alarms = fire.gameObject.AddComponent<BoilerAlarms>();
            alarms.Set(car, rig);
            var heard = rig.Alarm != null
                ? alarms.whistle != null ? "low-water alarm whistle" : $"low-water alarm (no '{WhistleClipName}' clip)"
                : alarms.blow != null ? "fusible plug" : $"fusible plug (no '{BlowClipName}' clip)";
            return $"boiler guard: {heard}";
        }

        private void Set(TrainCar car, BoilerWaterRig rig)
        {
            this.rig = rig;
            if (whistle != null || blow != null)
                return;
            var mixer = car.interior != null
                ? car.interior.GetComponentsInChildren<AudioSource>(true).Select(s => s.outputAudioMixerGroup).FirstOrDefault(g => g != null)
                : null;
            var clips = Resources.FindObjectsOfTypeAll<AudioClip>();
            if (rig.Alarm != null)
                whistle = Sound("LowWaterAlarm", clips.FirstOrDefault(c => c.name == WhistleClipName), mixer, WhistlePitch, Vector3.up * WhistleAboveFire);
            if (rig.Plug != null)
                blow = Sound("FusiblePlug", clips.FirstOrDefault(c => c.name == BlowClipName), mixer, BlowPitch, Vector3.zero);
        }

        private AudioSource Sound(string name, AudioClip clip, UnityEngine.Audio.AudioMixerGroup mixer, float pitch, Vector3 offset)
        {
            if (clip == null)
                return null;
            var holder = new GameObject(name);
            holder.transform.SetParent(transform, false);
            holder.transform.position = transform.position + offset;
            var source = holder.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.pitch = pitch;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 3f;
            source.maxDistance = 80f;
            source.dopplerLevel = 0f;
            source.volume = 0f;
            source.outputAudioMixerGroup = mixer;
            return source;
        }

        private void LateUpdate()
        {
            if (rig == null)
                return;
            SetVolume(whistle, rig.Alarm != null && rig.Alarm.Sounding ? WhistleVolume : 0f);
            SetVolume(blow, Mathf.Clamp01(rig.PlugVent / LoudestBlow));
        }

        private static void SetVolume(AudioSource source, float volume)
        {
            if (source == null)
                return;
            source.volume = volume;
            if (volume > 0.01f && !source.isPlaying)
                source.Play();
            else if (volume <= 0.01f && source.isPlaying)
                source.Stop();
        }
    }
}
