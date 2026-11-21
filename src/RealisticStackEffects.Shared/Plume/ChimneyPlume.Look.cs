using System.Collections.Generic;
using System.Linq;
#if !PUBLIC_RELEASE
using RealisticStackEffects.Core;
#endif
using UnityEngine;

namespace RealisticStackEffects
{
    internal sealed partial class ChimneyPlume
    {
        // The copies of its systems' materials the plume has made: Unity
        // makes a renderer its own copy the first time it is asked for its
        // material, and lets the copy outlive it (20 of 20 kept after their
        // renderers went; performance review, 2026-10-25).
        private readonly HashSet<Material> ownMaterials = new HashSet<Material>();

        private Material Own(Renderer renderer)
        {
            var material = renderer.material;
            if (material != null)
                ownMaterials.Add(material);
            return material;
        }

        // The custom smoke shader lights the wisps' surface across each
        // particle, turned with it: it needs the particles' tangents.
        // Its part of the plume's surface (Custom1, PlumeAloft.ShapeOf) after
        // its UV, then its billows' onset and shade (Custom2, PlumeAloft.ExtraOf),
        // then its middle, about which rolling steam rebuilds its quad upright.
        private static readonly List<ParticleSystemVertexStream> SurfaceStreams = new List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal, ParticleSystemVertexStream.Color,
            ParticleSystemVertexStream.UV, ParticleSystemVertexStream.Custom1XYZW, ParticleSystemVertexStream.Custom2XYZW,
            ParticleSystemVertexStream.Center, ParticleSystemVertexStream.Tangent,
        };
        private readonly List<Vector4> shapeData = new List<Vector4>();
        private readonly List<Vector4> extraData = new List<Vector4>();
        // The puffs the rolling steam's flipbooks hold (0: not rolling steam).
        private int rollingPuffs;
        private static readonly int PuffAgeScaleId = Shader.PropertyToID("_PuffAgeScale");

        // As parcels (PlumeParcels) a puff turns to a wisp only as it
        // thins, never by its age (these shares of its flipbook's time are
        // past its end): a dense lump is opaque, and an opaque wisp drew as
        // bright ribbons. Else by the look's ages.
        private static bool AsParcels => Look.Parcels > 0.5f;
        private const float NeverByAge = 5f;
        private static float WispAgeFrom => AsParcels ? NeverByAge : Look.WispAgeFrom;
        private static float WispAgeTo => AsParcels ? NeverByAge + 1f : Look.WispAgeTo;
        private static float ThinWispAgeFrom => AsParcels ? NeverByAge : Look.ThinWispAgeFrom;
        private static float ThinWispAgeTo => AsParcels ? NeverByAge + 1f : Look.ThinWispAgeTo;
        private readonly Dictionary<ParticleSystem, Material> puffMaterials = new Dictionary<ParticleSystem, Material>();
        private float wispTrainSpeed;

        // The plume's light and fraying on its materials, and the shimmer's bend.
        private void ApplyLook()
        {
            lookVersion = PlumeCurrent.LookVersion;
            lookDaylight = PlumeWeather.Daylight;
            lookDirectLight = PlumeWeather.DirectLight;
            var direct = Mathf.Lerp(1f, lookDirectLight, Mathf.Clamp01(Look.TwilightSun));
            volumeMaterials = new[] { smoke, sootCore, jet }.Where(s => s != null)
                .Select(s => s.GetComponent<ParticleSystemRenderer>()).Where(r => r != null)
                .Select(Own).Where(m => m != null && m.HasProperty("_Volume")).ToArray();
            var skyLight = Mathf.Lerp(Look.NightSkyLight, Look.SkyLight, lookDaylight);
            var grey = Mathf.Lerp(Look.NightGrey, Look.LightGrey, lookDaylight);
            foreach (var system in new[] { smoke, sootCore, jet }.Where(s => s != null))
            {
                var renderer = system.GetComponent<ParticleSystemRenderer>();
                var material = renderer != null ? Own(renderer) : null;
                if (material == null || !material.HasProperty("_SkyStrength"))
                    continue;
                material.SetFloat("_SkyStrength", skyLight * Look.Light);
                material.SetFloat("_SunStrength", Look.SunLight * Look.Light * direct);
                material.SetFloat("_MaxLight", Look.MaxLight * Look.Light);
                if (!material.HasProperty("_Surface"))
                    continue;
                material.SetFloat("_Wrap", Look.Wrap);
                material.SetFloat("_Relief", Look.Relief);
                material.SetFloat("_Bulge", Look.Bulge);
                material.SetFloat("_Shade", Look.SelfShadow);
                material.SetFloat("_Scatter", Look.Scatter);
                material.SetFloat("_BinormalSign", Look.BinormalSign);
                if (material.HasProperty("_PlumeShape"))
                    material.SetFloat("_PlumeShape", Look.PlumeShape);
                if (material.HasProperty("_Flat"))
                {
                    material.SetFloat("_Flat", Look.Flat);
                    material.SetFloat("_Terminator", Look.Terminator);
                    material.SetFloat("_ShadeLight", Look.ShadeLight);
                    material.SetFloat("_SunShadow", Look.SunShadow);
                }
                if (material.HasProperty("_VolumeShape"))
                    material.SetFloat("_VolumeShape", Look.VolumeShape);
                if (material.HasProperty("_NearFadeFrom"))
                {
                    material.SetFloat("_NearFadeFrom", Look.NearFadeFrom);
                    material.SetFloat("_NearFadeTo", Look.NearFadeTo);
                }
                // Only the cloud's material has billows (the jet's streaks have none).
                if (material.HasProperty("_Billow"))
                {
                    material.SetFloat("_Billow", material.GetTexture("_BillowTex") != null ? Look.Billow : 0f);
                    material.SetFloat("_BillowFrom", Look.BillowFrom);
                    material.SetFloat("_BillowFull", Mathf.Max(Look.BillowFrom + 0.01f, Look.BillowFull));
                }
                if (material.HasProperty("_Grey"))
                    material.SetFloat("_Grey", grey);
                if (material.HasProperty("_SixPlume"))
                {
                    material.SetFloat("_SixPlume", Look.SixPlume);
                    material.SetFloat("_ShadeSky", Look.ShadeSky);
                    material.SetFloat("_WispBelow", Look.WispBelow);
                    material.SetFloat("_WispAbove", Mathf.Max(Look.WispBelow + 0.01f, Look.WispAbove));
                    material.SetFloat("_WispAgeFrom", WispAgeFrom);
                    material.SetFloat("_WispAgeTo", Mathf.Max(WispAgeFrom + 0.01f, WispAgeTo));
                    material.SetFloat("_WispBlend", Look.WispBlend);
                    material.SetFloat("_WispSheet", Look.WispSheet);
                    material.SetFloat("_WispCoherence", Look.WispCoherence);
                    material.SetFloat("_WispHold", Look.WispHold);
                    material.SetFloat("_WispFadeFrom", Look.WispFadeFrom);
                    material.SetFloat("_ThinWispAgeFrom", ThinWispAgeFrom);
                    material.SetFloat("_ThinWispAgeTo", Mathf.Max(ThinWispAgeFrom + 0.01f, ThinWispAgeTo));
                    material.SetFloat("_WispBaked", Look.WispBaked);
                    material.SetFloat("_WispTurn", Look.WispTurn);
                    material.SetFloat("_WispBakedLight", Look.WispBakedLight);
                    material.SetFloat("_WispPuffLit", Look.WispPuffLit);
                    material.SetFloat("_WispPuffLight", Look.WispPuffLight);
                    material.SetFloat("_WispRelief", Look.WispRelief);
                    material.SetFloat("_WispBulge", Look.WispBulge);
                    material.SetFloat("_WispScatter", Look.WispScatter);
                    material.SetFloat("_WispSunStrength", Look.WispSunLight * Look.Light * direct);
                    material.SetFloat("_SootAlbedo", Look.SootAlbedo);
                    material.SetFloat("_LeastOpacity", Look.LeastOpacity);
                    material.SetFloat("_Fringe", Look.Fringe);
                    material.SetFloat("_Upright", Look.Upright);
                    material.SetFloat("_Grow", Look.Grow);
                    material.SetFloat("_WispReach", Mathf.Min(Look.WispReach, Look.Grow));
                    material.SetFloat("_FringeShare", Look.FringeShare);
                    material.SetFloat("_FringeRimFrom", Look.FringeRimFrom);
                    material.SetFloat("_FringeRimTo", Mathf.Max(Look.FringeRimFrom + 0.01f, Look.FringeRimTo));
                    material.SetFloat("_FringeCore", Look.FringeCore);
                    material.SetFloat("_SixShade", Look.RollShade);
                    material.SetFloat("_ThickFloor", Look.ThickFloor);
                    material.SetFloat("_DebugAlpha", Look.DebugAlpha);
                }
                if (material.GetFloat("_Surface") > 0.5f)
                    renderer.SetActiveVertexStreams(SurfaceStreams);
            }
            // Development: the jet's streaks hidden, to see what they draw.
            var jetShown = jet != null ? jet.GetComponent<ParticleSystemRenderer>() : null;
            if (jetShown != null)
                jetShown.enabled = Look.HideJet <= 0f;
            var heatShown = heat != null ? heat.GetComponent<ParticleSystemRenderer>() : null;
            if (heatShown != null)
            {
                heatShown.enabled = Look.HideHeat <= 0f;
            }
            rollingPuffs = 0;
            foreach (var system in new[] { smoke, sootCore }.Where(s => s != null))
            {
                var renderer = system.GetComponent<ParticleSystemRenderer>();
                // Development: hidden, to time what drawing the plume costs.
                if (renderer != null)
                    renderer.enabled = Look.HidePlume <= 0f;
                var material = renderer != null ? Own(renderer) : null;
                if (material != null && material.HasProperty("_Fray"))
                    material.SetFloat("_Fray", Look.Fray);
                if (material != null)
                    puffMaterials[system] = material;
                // Rolling steam: the puffs its flipbooks hold, a loop of frames each.
                if (material != null && material.HasProperty("_SixWay") && material.GetFloat("_SixWay") > 0.5f)
                    rollingPuffs = Mathf.Max(1, Mathf.RoundToInt(material.GetFloat("_SixColumns") * material.GetFloat("_SixRows")
                                                                 / Mathf.Max(1f, material.GetFloat("_SixFrames"))));
            }
            if (heat != null)
            {
                var heatMain = heat.main;
                heatMain.startLifetime = new ParticleSystem.MinMaxCurve(0.8f * Look.ShimmerLife, 1.2f * Look.ShimmerLife);
            }
            var heatRenderer = heat != null ? heat.GetComponent<ParticleSystemRenderer>() : null;
            var heatMaterial = heatRenderer != null ? Own(heatRenderer) : null;
            if (heatMaterial != null && heatMaterial.HasProperty("_Strength"))
                heatMaterial.SetFloat("_Strength", Look.ShimmerStrength);
            if (vents != null && smoke != null && puffMaterials.TryGetValue(smoke, out var cloudMaterial))
                vents.UseMaterial(cloudMaterial);
            SyncHeatShimmer();
        }

        private bool HeatShimmerOn => StackHost.Settings == null || StackHost.Settings.HeatShimmer;

        // Reacts live to the HeatShimmer setting without a restart: stops,
        // clears, and deactivates the shimmer when off, and reactivates and
        // resumes playing when turned back on.
        private void SyncHeatShimmer()
        {
            if (heat == null)
                return;
            if (!HeatShimmerOn)
            {
                if (heat.gameObject.activeSelf || heat.isPlaying || heat.particleCount > 0)
                {
                    heat.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    heat.gameObject.SetActive(false);
                }
                heatDue = 0f;
                heatStrengths.Clear();
                if (intendedSizes.TryGetValue(heat, out var sizes))
                    sizes.Clear();
                return;
            }
            if (!heat.gameObject.activeSelf)
                heat.gameObject.SetActive(true);
            if (!heat.isPlaying)
                heat.Play();
        }

#if !PUBLIC_RELEASE
        private void LogStatus(PlumeInputs inputs)
        {
            var interval = StackHost.Settings.StatusLogInterval;
            if (interval <= 0 || Time.time - statusTime < interval
                || !fire.Tended)
                return;

            statusTime = Time.time;
            var puff = LastPuff;
            StackHost.Mod.Logger.Log($"{car.ID} smoke: {beatCount} beats in {interval} s, exhaust {inputs.ExhaustPressure:F1} bar " +
                                $"(strength {PlumeModel.BeatStrength(tuning, inputs.ExhaustPressure):P0}, heavy {PlumeModel.Heaviness(tuning, inputs.ExhaustPressure):P0}), " +
                                $"flow {inputs.TotalFlowNormalized:P0}, soot {inputs.Soot:P0} (firebox {PlumeModel.FireboxSoot(inputs):P0}); " +
                                $"last puff {puff.Speed:F1} m/s, {puff.Size:F1} m, {puff.Particles}+{puff.SootParticles} particles, life ×{puff.LifetimeScale:F1}, " +
                                $"{puff.Sparks:F2} sparks, color #{ColorUtility.ToHtmlStringRGBA(ToColor(puff.Color))}; " +
                                $"plume top {plumeTop:F0} m, buoyancy {stackFlux:F0} m⁴/s³, {PlumeWeather.Describe()}, {SteamLine()}, " +
                                (roof.OverStack is TunnelRoof.Sample over ? $"under a roof {over.Height:F1} m over the rail " : "in the open ") +
                                $"({string.Join("; ", aloft.Select(a => $"{a.Key.name} {a.Value.Describe()}"))}); " +
                                $"alive {string.Join(", ", Systems().Where(s => s != null).Select(s => $"{s.name} {s.particleCount}"))}");
            beatCount = 0;
        }
#endif

        // The white steam in a few words: how white the air lets it be, how
        // long it lasts, and what makes it so.
        private string SteamLine()
        {
            var past = airVelocity - stackVelocity;
            var speed = new Vector2(past.x, past.z).magnitude;
            return $"steam white {steamWhite:F2} formed half by {Seconds(steamCloud.Forming(speed, tuning.SteamMixing, tuning.SteamForming, 0.5f))}, " +
                   $"lasting {steamCloud.Lasting(speed, tuning.SteamMixing, 1f / Mathf.Exp(1f), tuning.SteamForming):F1} s at {speed:F1} m/s " +
                   $"(flue gas {flueGas:F1} kg/kg, {tuning.FlueGasMixing * flueGas:F2} mixing; exhaust wetness {wetness:F2}; {steamFlowNow:F2} kg/s up the stack), " +
                   $"the fire's water white {waterCloud.Whiteness:F2}, shimmer {shimmerNow:F2}";
        }

        /// <summary>The plume in a few lines, for the test bed.</summary>
        public string Readout()
        {
            var past = airVelocity - stackVelocity;
            var speed = new Vector2(past.x, past.z).magnitude;
            var half = steamCloud.Forming(speed, tuning.SteamMixing, tuning.SteamForming, 0.5f);
            var third = steamCloud.Lasting(speed, tuning.SteamMixing, 1f / Mathf.Exp(1f), tuning.SteamForming);
            var gone = steamCloud.Lasting(speed, tuning.SteamMixing, 0.05f, tuning.SteamForming);
            return $"Steam white: {steamWhite:F2} of the tuned air's; half formed at {Seconds(half)}, a third left at {third:F1} s, gone by {gone:F1} s, the air past at {speed:F1} m/s\n" +
                   $"Flue gas {flueGas:F1} kg a kg of steam ({tuning.FlueGasMixing * flueGas:F2} mixing in); exhaust wetness {wetness:F2}; {steamFlowNow:F2} kg/s of steam up the stack\n" +
                   $"The fire's own water: white {waterCloud.Whiteness:F2}. The stream between beats (the blower's): white {streamCloud.Whiteness:F2}. Heat shimmer: {shimmerNow:F2}\n" +
                   $"Stack: bore {2f * boreRadius:F2} m, outside {2f * outsideRadius:F2} m, lip {(stackLip.HasValue ? $"{stackLip.Value:F2} m over the emitter" : "not found")}\n" +
                   $"Last puff: {LastPuff.Speed:F1} m/s, {LastPuff.Size:F1} m, opacity {LastPuff.Color.A:F2}, color #{ColorUtility.ToHtmlStringRGB(ToColor(LastPuff.Color))}, " +
                   $"{LastPuff.SmokeShare:P0} smoke; plume top {plumeTop:F0} m\n" +
                   $"Particles: {string.Join("; ", aloft.Select(a => $"{a.Key.name} {a.Value.Describe()}"))}; " +
                   $"as one volume, {(volume != null ? volume.ShadedShare : 0f):P0} of its smoke under half the sun\n" +
                   (vents != null ? vents.Readout() + "\n" : "") +
                   ShaderLight();
        }

        // The light the smoke shader puts on the plume's top facing the sun,
        // worked out as it does: the sky's from above and the sun's, capped.
        private static string ShaderLight()
        {
            var sky = new Color[1];
            RenderSettings.ambientProbe.Evaluate(new[] { Vector3.up }, sky);
            var sun = Sun();
            var sunLight = 0f;
            if (sun != null && sun.isActiveAndEnabled)
            {
                var color = QualitySettings.activeColorSpace == ColorSpace.Linear ? sun.color.linear : sun.color;
                sunLight = color.grayscale * sun.intensity;
            }
            var skyShare = sky[0].grayscale * Mathf.Lerp(Look.NightSkyLight, Look.SkyLight, PlumeWeather.Daylight) * Look.Light;
            var sunShare = sunLight * Look.SunLight * Look.Light * Mathf.Lerp(1f, PlumeWeather.DirectLight, Mathf.Clamp01(Look.TwilightSun));
            var cap = Look.MaxLight * Look.Light;
            return $"Light on the smoke: sky {skyShare:F2} + sun {sunShare:F2} = {Mathf.Min(skyShare + sunShare, cap):F2} (cap {cap:F2}), " +
                   $"{(sun != null ? sun.name : "no sun")}, {QualitySettings.activeColorSpace} color";
        }

        // The scene's main light, the sun (or the moon), as the shader takes
        // it: the render settings' sun, else the strongest directional light,
        // looked for again no more often than this, s.
        private const float SunSearchEvery = 5f;
        private static Light sunFound;
        private static float sunSearched = float.NegativeInfinity;

        internal static Light Sun()
        {
            if (RenderSettings.sun != null)
                return RenderSettings.sun;
            if (sunFound != null && sunFound.isActiveAndEnabled || Time.unscaledTime - sunSearched < SunSearchEvery)
                return sunFound;
            sunSearched = Time.unscaledTime;
            sunFound = null;
            foreach (var light in FindObjectsOfType<Light>())
                if (light.type == LightType.Directional && light.isActiveAndEnabled && (sunFound == null || light.intensity > sunFound.intensity))
                    sunFound = light;
            return sunFound;
        }

        private static string Seconds(float seconds) => float.IsInfinity(seconds) ? "never" : $"{seconds:F2} s";

        // The plume's systems, gathered once it is set up: gathered each
        // frame for its sorting, they were garbage every frame.
        private ParticleSystem[] systems;

        private ParticleSystem[] Systems()
        {
            if (systems != null)
                return systems;
            var gathered = new[] { smoke, sootCore, jet, heat, vents?.Drawn, vents?.Behind }.Concat(embers ?? new ParticleSystem[0]).Where(s => s != null).Distinct().ToArray();
            if (Active)
                systems = gathered;
            return gathered;
        }
    }
}
