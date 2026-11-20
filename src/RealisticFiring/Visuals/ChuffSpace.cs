using System.Collections.Generic;
using DV.ModularAudioCar;
using RealisticStackEffects;
using UnityEngine;
using Voice = RealisticFiring.Core.ChuffVoice;

namespace RealisticFiring.Visuals
{
 internal sealed class ChuffSpace : MonoBehaviour
    {
        // Beats and their echoes at once: a beat lasts about a second and the
        // single beats run to 5 a second, two clip sets blending, with two
        // echoes each; past this the oldest gives way.
        private const int Voices = 20;
        private const float LookEvery = 0.5f;
        private const float FilterEvery = 0.2f;
        // A queued echo is handed to the audio clock this long before it is due, s.
        private const float Lead = 0.05f;
        // The walls are looked for this far below the stack, m: a cutting's
        // sides, not the sky over its top.
        private const float WallDrop = 1.5f;
        // A ceiling higher than this over the rail is no tunnel's, m (TunnelRoof).
        private const float HighestRoof = 30f;
        // The loops' echo is moved only when its delay changes this much, ms:
        // each move empties the filter's delay line.
        private const float EchoStep = 10f;

        private struct Pending
        {
            public float At;
            public AudioClip Clip;
            public float Volume;
            public float Pitch;
            public Vector3 Offset;
        }

        private static int ground;

        private TrainCar car;
        private AudioSource[] sources;
        private AudioLowPassFilter[] lows;
        private float[] busyUntil;
        private readonly List<Pending> pending = new List<Pending>();
        private readonly List<AudioLowPassFilter> loopLows = new List<AudioLowPassFilter>();
        private readonly List<AudioEchoFilter> loopEchoes = new List<AudioEchoFilter>();
        private readonly List<Component> added = new List<Component>();

        // The walls across the track, m (0 none), and the tunnel's roof over the rail, m (0 in the open).
        private float left;
        private float right;
        private float roof;
        private float carry = 0.5f;
        private float loopEchoDelay;
        private float nextLook;
        private float nextFilter;

        /// <summary>The walls and roof last found, for the log and the checks.</summary>
        internal string Describe() =>
            roof > 0f ? $"in a tunnel, roof {roof:0.0} m" : $"walls {(left > 0f ? $"{left:0} m" : "none")} left, {(right > 0f ? $"{right:0} m" : "none")} right";

        internal void Init(TrainCar car, ChuffClipsSimReader reader)
        {
            this.car = car;
            if (ground == 0)
                ground = LayerMask.GetMask("Terrain", "Default");
            if (sources == null)
                Build(reader.regularChuffConfig);
            loopLows.Clear();
            loopEchoes.Clear();
            var seen = new HashSet<GameObject>();
            foreach (var loop in reader.chuffLoops ?? new ChuffClipsSimReader.ChuffLoop[0])
            {
                var layers = loop != null && loop.chuffLoop != null ? loop.chuffLoop.layers : null;
                if (layers == null)
                    continue;
                foreach (var layer in layers)
                {
                    if (layer == null || layer.source == null || !seen.Add(layer.source.gameObject))
                        continue;
                    loopLows.Add(Filter<AudioLowPassFilter>(layer.source.gameObject));
                    var echo = Filter<AudioEchoFilter>(layer.source.gameObject);
                    echo.enabled = false;
                    echo.dryMix = 1f;
                    loopEchoes.Add(echo);
                }
            }
            loopEchoDelay = 0f;
            nextLook = 0f;
            nextFilter = 0f;
            enabled = true;
        }

        /// <summary>The reader let go of its ports: quiet the filters until it is set up again.</summary>
        internal void Sleep()
        {
            pending.Clear();
            Quiet();
            enabled = false;
        }

        /// <summary>Plays <paramref name="clip"/> at the stack, or <paramref name="offset"/> from it, dulled by the way to the listener.</summary>
        internal void Play(AudioClip clip, float volume, float pitch, float delay, Vector3 offset = default, float cutoff = 22000f)
        {
            if (clip == null || !(volume > 0.01f) || !(pitch > 0f) || sources == null)
                return;
            var now = Time.time;
            var pick = 0;
            for (var i = 0; i < sources.Length; i++)
            {
                if (busyUntil[i] <= now)
                {
                    pick = i;
                    break;
                }
                if (busyUntil[i] < busyUntil[pick])
                    pick = i;
            }
            var source = sources[pick];
            if (source == null)
                return;
            source.Stop();
            source.transform.position = transform.position + offset;
            source.clip = clip;
            source.volume = Mathf.Min(1f, volume);
            source.pitch = pitch;
            lows[pick].cutoffFrequency = Mathf.Min(cutoff, Voice.HeardCutoff(ListenerDistance(source.transform.position), carry));
            if (delay > 0f)
                source.PlayDelayed(delay);
            else
                source.Play();
            busyUntil[pick] = now + Mathf.Max(0f, delay) + clip.length / pitch;
        }

        /// <summary>The walls' and tunnel's answer to a beat of <paramref name="clip"/> played <paramref name="late"/> s from now.</summary>
        internal void Echo(AudioClip clip, float loudness, float pitch, float late)
        {
            if (clip == null || !(loudness > 0.02f) || car == null)
                return;
            var at = Time.time + Mathf.Max(0f, late);
            if (roof > 0f)
            {
                for (var i = 0; i < Voice.TunnelEchoes; i++)
                    Queue(at + Voice.TunnelEchoDelay(i), clip, loudness * Voice.TunnelEchoLoudness(i), pitch, Vector3.zero);
                return;
            }
            var across = Across();
            Wall(left, -across);
            Wall(right, across);

            void Wall(float distance, Vector3 way)
            {
                var echo = Voice.EchoLoudness(distance);
                if (echo > 0f)
                    Queue(at + Voice.EchoDelay(distance), clip, loudness * echo, pitch, way * distance);
            }
        }

        private void Queue(float at, AudioClip clip, float volume, float pitch, Vector3 offset)
        {
            if (volume > 0.01f)
                pending.Add(new Pending { At = at, Clip = clip, Volume = volume, Pitch = pitch, Offset = offset });
        }

        private void Update()
        {
            if (car == null)
                return;
            var on = Main.Settings != null && Main.Settings.RealisticChuffs;
            var now = Time.time;
            for (var i = pending.Count - 1; i >= 0; i--)
            {
                var echo = pending[i];
                if (echo.At - now > Lead)
                    continue;
                pending.RemoveAt(i);
                if (on)
                    Play(echo.Clip, echo.Volume, echo.Pitch, echo.At - now, echo.Offset, Voice.WallCutoff);
            }
            if (!on)
            {
                if (now >= nextFilter)
                {
                    nextFilter = now + FilterEvery;
                    Quiet();
                }
                return;
            }
            if (now >= nextLook)
            {
                nextLook = now + LookEvery;
                Look();
            }
            if (now >= nextFilter)
            {
                nextFilter = now + FilterEvery;
                FilterLoops();
            }
        }

        private void Look()
        {
            var across = Across();
            var from = transform.position - Vector3.up * WallDrop;
            left = Physics.Raycast(from, -across, out var hit, Voice.FarthestEcho, ground, QueryTriggerInteraction.Ignore) ? hit.distance : 0f;
            right = Physics.Raycast(from, across, out hit, Voice.FarthestEcho, ground, QueryTriggerInteraction.Ignore) ? hit.distance : 0f;
            roof = Roof();
        }

        // The tunnel's ceiling over the rail at the stack, m, as the game's track gives it; 0 in the open.
        private float Roof()
        {
            var bogie = car.Bogies != null && car.Bogies.Length > 0 ? car.FrontBogie : null;
            if (bogie == null || bogie.track == null || bogie.traveller == null)
                return 0f;
            var rail = bogie.transform.position;
            var span = (float)bogie.traveller.Span + Vector3.Dot(transform.position - rail, bogie.traveller.worldForward);
            var height = bogie.track.SampleCeilingHeight(span);
            return height > 0f && height < HighestRoof ? height : 0f;
        }

        private void FilterLoops()
        {
            var air = PlumeWeather.Air;
            carry = Voice.Carry(PlumeWeather.Wind.magnitude, air.Humidity);
            var cutoff = Voice.HeardCutoff(ListenerDistance(transform.position), carry);
            foreach (var low in loopLows)
            {
                if (low == null)
                    continue;
                low.enabled = true;
                low.cutoffFrequency = cutoff;
            }

            // The tunnel rings; in the open, the nearer wall answers.
            float delay = 0f, wet = 0f, decay = 0f;
            if (roof > 0f)
            {
                delay = Voice.TunnelEchoDelay(0) * 1000f;
                wet = Voice.TunnelEchoLoudness(0);
                decay = 0.5f;
            }
            else
            {
                foreach (var wall in new[] { left, right })
                {
                    var echo = Voice.EchoLoudness(wall);
                    if (echo > wet)
                    {
                        wet = echo;
                        delay = Voice.EchoDelay(wall) * 1000f;
                        decay = 0.15f;
                    }
                }
            }
            var move = Mathf.Abs(delay - loopEchoDelay) >= EchoStep;
            if (move)
                loopEchoDelay = delay;
            foreach (var echo in loopEchoes)
            {
                if (echo == null)
                    continue;
                echo.enabled = wet > 0f;
                if (!(wet > 0f))
                    continue;
                if (move)
                    echo.delay = Mathf.Max(10f, delay);
                echo.wetMix = wet;
                echo.decayRatio = decay;
            }
        }

        private void Quiet()
        {
            foreach (var low in loopLows)
                if (low != null)
                    low.enabled = false;
            foreach (var echo in loopEchoes)
                if (echo != null)
                    echo.enabled = false;
        }

        private void Build(ChuffClipsSimReader.IndividualChuffAudioSourceConfig config)
        {
            sources = new AudioSource[Voices];
            lows = new AudioLowPassFilter[Voices];
            busyUntil = new float[Voices];
            for (var i = 0; i < Voices; i++)
            {
                var voice = new GameObject($"RF chuff {i}");
                voice.transform.SetParent(transform, false);
                var source = voice.AddComponent<AudioSource>();
                Configure(source, config);
                var low = voice.AddComponent<AudioLowPassFilter>();
                low.cutoffFrequency = 22000f;
                sources[i] = source;
                lows[i] = low;
            }
        }

        // As the game plays its own beats (NAudio.Play).
        private static void Configure(AudioSource source, ChuffClipsSimReader.IndividualChuffAudioSourceConfig config)
        {
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            source.spread = config.spread;
            source.minDistance = config.minDistance;
            source.maxDistance = config.maxDistance;
            source.outputAudioMixerGroup = config.mixerGroup != null ? config.mixerGroup : NAudio.Default3DMixerGroup;
            var curves = config.customCurves;
            if (curves.rolloff != null)
            {
                source.rolloffMode = AudioRolloffMode.Custom;
                source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, curves.rolloff);
            }
            if (curves.reverb != null)
                source.SetCustomCurve(AudioSourceCurveType.ReverbZoneMix, curves.reverb);
            if (curves.spatial != null)
                source.SetCustomCurve(AudioSourceCurveType.SpatialBlend, curves.spatial);
            if (curves.spread != null)
                source.SetCustomCurve(AudioSourceCurveType.Spread, curves.spread);
        }

#if !PUBLIC_RELEASE
        /// <summary>
        /// A source of its own at the stack for the synthesized exhaust
        /// (<see cref="ChuffSynth"/>), heard as the beats are and dulled and
        /// echoed as the loops are. Its filters go after the component that
        /// makes its sound, added first.
        /// </summary>
        internal void Carry(AudioSource source, ChuffClipsSimReader.IndividualChuffAudioSourceConfig config)
        {
            Configure(source, config);
            loopLows.Add(Filter<AudioLowPassFilter>(source.gameObject));
            var echo = Filter<AudioEchoFilter>(source.gameObject);
            echo.enabled = false;
            echo.dryMix = 1f;
            loopEchoes.Add(echo);
        }
#endif

        private T Filter<T>(GameObject on) where T : Behaviour
        {
            var filter = on.GetComponent<T>();
            if (filter == null)
            {
                filter = on.AddComponent<T>();
                added.Add(filter);
            }
            return filter;
        }

        private Vector3 Across()
        {
            var across = car != null ? car.transform.right : transform.right;
            across.y = 0f;
            return across.sqrMagnitude > 0.01f ? across.normalized : Vector3.right;
        }

        private static float ListenerDistance(Vector3 at)
        {
            var camera = PlayerManager.ActiveCamera != null ? PlayerManager.ActiveCamera : Camera.main;
            return camera != null ? Vector3.Distance(camera.transform.position, at) : 0f;
        }

        private void OnDestroy()
        {
            if (sources != null)
                foreach (var source in sources)
                    if (source != null)
                        Destroy(source.gameObject);
            foreach (var filter in added)
                if (filter != null)
                    Destroy(filter);
        }
    }
}
