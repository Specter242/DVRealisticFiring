using System.Collections.Generic;
using System.Linq;
using RealisticFiring.Core;
using RealisticFiring.Diagnostics;
using RealisticFiring.Sim;
using RealisticStackEffects;
using UnityEngine;

namespace RealisticFiring.Visuals
{
 internal sealed class FireboxLighting : MonoBehaviour
    {
        private static readonly int ZonesId = Shader.PropertyToID("_FireZones");
        private static readonly int ZoneLightId = Shader.PropertyToID("_FireZoneLight");
        private static readonly int FlamesId = Shader.PropertyToID("_FireFlames");
        private static readonly int FlameLightId = Shader.PropertyToID("_FireFlameLight");
        private static readonly int GrateUpId = Shader.PropertyToID("_FireGrateUp");
        private static readonly int DoorId = Shader.PropertyToID("_FireDoor");
        private static readonly int DoorLightId = Shader.PropertyToID("_FireDoorLight");
        private static readonly int DebugId = Shader.PropertyToID("_FireDebug");
        private static readonly int LightScaleId = Shader.PropertyToID("_FireLightScale");
        private static readonly int GlowStrengthId = Shader.PropertyToID("_GlowStrength");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BumpMapId = Shader.PropertyToID("_BumpMap");
        private static readonly int CutoffId = Shader.PropertyToID("_Cutoff");

        // A glowing zone's radiance against its heat, as the bed draws its glow
        // there on average (CoalBed: the glow's color, how much of the zone
        // glows, and its strength); and the flames' light against their
        // strength and height. Both to be set against footage on the test bed.
        internal static float BedLight = 1f;
        internal static float FlameLight = 0.6f;

        // How bright the bed's glow draws (CoalBed's strength), and so the
        // light it gives; the embers', seen in the gaps under the coal
        // (EmberBed); and the flames' temperature, 0 to 1, for the color of
        // their light (FireColors.Flame).
        internal static float BedGlow = 0.6f;
        internal static float EmberGlow = 0.7f;
        internal static float FlameLightHeat = 0.6f;

        internal static float WallLight = 0.03f;
        internal static float CoalLight = 0.08f;

        internal static float DoorLight = 5f;
        internal static float DoorDaylight = 0.2f;
        internal static float DoorBounce = 0.15f;
        internal static float S060Light = 1.6f;

        // An alpha-tested or blended stock material's holes (the tube plate's
        // tubes) are cut where its alpha falls under this.
        private const float HoleCutoff = 0.5f;

        private Fire fire;
        private RealisticFirebox firebox;
        private ZonedCoalBed bed;
        private FireboxFlames flames;
        private FireboxVolume volume;
        private CoalLumps lumps;
        private readonly List<(Renderer Renderer, Material[] Stock, Material[] Lit)> taken = new List<(Renderer, Material[], Material[])>();
        private Renderer[] stockEmissive;
        private bool showing;
        private MaterialPropertyBlock block;

        private readonly Vector4[] zones = new Vector4[GrateLayout.ZoneCount];
        private readonly Vector4[] zoneLight = new Vector4[GrateLayout.ZoneCount];
        private readonly Vector4[] flamePlaces = new Vector4[GrateLayout.ZoneCount];
        private readonly Vector4[] flameLight = new Vector4[GrateLayout.ZoneCount];
        private int measuredFrame = -1;
        private Color fireRadiance;

        // The same emission that lights the walls also colors the cab lights.
        internal Color FireRadiance
        {
            get { Measure(); return fireRadiance; }
        }

        /// <summary>Lights the box of <paramref name="fire"/> by its fire. Returns a line for the log, or null.</summary>
        public static string Attach(Fire fire, ZonedCoalBed bed, RealisticFirebox firebox)
        {
            var wall = PlumeAssets.FireboxWallMaterial;
            if (fire == null || bed == null || !bed.Ready || wall == null || fire.GetComponent<FireboxLighting>() != null)
                return null;
            var lighting = fire.gameObject.AddComponent<FireboxLighting>();
            lighting.fire = fire;
            lighting.bed = bed;
            lighting.firebox = firebox;
            lighting.flames = fire.GetComponent<FireboxFlames>();
            lighting.block = new MaterialPropertyBlock();
            lighting.stockEmissive = fire.emissiveRenderersAffectedByFire;
            foreach (var renderer in BoxRenderers(fire, bed))
            {
                var stock = renderer.sharedMaterials;
                var lit = stock.Select(m => WallFor(wall, m)).ToArray();
                lighting.taken.Add((renderer, stock, lit));
            }
            if (lighting.taken.Count == 0)
                return "no firebox shell to light by the fire";
            return "lit by its fire alone: " + string.Join(", ", lighting.taken.Select(t => $"'{t.Renderer.name}'")) + " and the coal bed";
        }

        // The box's own surfaces: those the game's fire made glow, and the
        // meshes under the fire (the S282's tube plate), but not the stock
        // coal pile the bed stands in for or the mod's own effects.
        private static IEnumerable<Renderer> BoxRenderers(Fire fire, ZonedCoalBed bed) =>
            (fire.emissiveRenderersAffectedByFire ?? new Renderer[0])
            .Concat(fire.GetComponentsInChildren<MeshRenderer>(true))
            .Where(r => r is MeshRenderer && r != bed.StockCoal && (bed.Effects == null || !r.transform.IsChildOf(bed.Effects)))
            .Distinct();

        // The wall material with the stock material's surface: its texture,
        // tint, and relief, its holes where it had them.
        private static Material WallFor(Material wall, Material stock)
        {
            var lit = new Material(wall) { name = $"{wall.name} ({(stock != null ? stock.name : "none")})" };
            if (stock == null)
                return lit;
            if (stock.HasProperty(MainTexId))
            {
                lit.SetTexture(MainTexId, stock.GetTexture(MainTexId));
                lit.SetTextureScale(MainTexId, stock.GetTextureScale(MainTexId));
                lit.SetTextureOffset(MainTexId, stock.GetTextureOffset(MainTexId));
            }
            if (stock.HasProperty(ColorId))
                lit.SetColor(ColorId, stock.GetColor(ColorId));
            if (stock.HasProperty(BumpMapId) && stock.GetTexture(BumpMapId) != null)
                lit.SetTexture(BumpMapId, stock.GetTexture(BumpMapId));
            var holed = stock.IsKeywordEnabled("_ALPHATEST_ON") || stock.IsKeywordEnabled("_ALPHABLEND_ON") || stock.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON");
            lit.SetFloat(CutoffId, holed ? HoleCutoff : 0f);
            return lit;
        }

        private void LateUpdate()
        {
            var start = PerfMeter.Start();
            UpdateLighting();
            PerfMeter.Stop(PerfSection.FireboxLighting, start);
        }

        private void UpdateLighting()
        {
            var show = Main.Settings != null && Main.Settings.ShowZonedCoalBed && bed != null && bed.Ready;
            if (show != showing)
                Show(show);
            if (!show)
                return;
            Measure();
            // The eye adapts to this fire only while it is seen through its
            // open door (with the volume off, while its bed is seen).
            if (volume != null ? volume.Viewed : bed.BedRenderer != null && bed.BedRenderer.isVisible)
                FireColors.Seen(light, volume != null ? volume.Distance : 0f, flames != null ? flames.Draft : 0f);
            FireColors.Publish();
            // Keep the interior lit through gaps in a shut door. Its expensive
            // meshes and volume may sleep, but refresh the light four times a
            // second there, immediately when the door opens.
            if (!bed.Seen && Time.unscaledTime < hiddenLightAt)
                return;
            hiddenLightAt = Time.unscaledTime + 0.25f;
            var gain = firebox.Car?.carLivery?.id == "LocoS060" ? S060Light : 1f;
            foreach (var (renderer, _, _) in taken)
                Apply(renderer, WallLight * gain);
            if (bed.BedRenderer != null)
                Apply(bed.BedRenderer, CoalLight * gain, BedGlow);
            if (lumps == null)
                lumps = fire != null ? fire.GetComponent<CoalLumps>() : null;
            if (lumps != null && lumps.Renderer != null)
                Apply(lumps.Renderer, CoalLight * gain);
        }

        // Puts the wall material on the box, and takes it off the game's fire's
        // glow, or gives both back.
        private void Show(bool show)
        {
            showing = show;
            foreach (var (renderer, stock, lit) in taken)
            {
                if (renderer != null)
                    renderer.sharedMaterials = show ? lit : stock;
            }
            if (fire != null)
                fire.emissiveRenderersAffectedByFire = show
                    ? (stockEmissive ?? new Renderer[0]).Where(r => taken.All(t => t.Renderer != r)).ToArray()
                    : stockEmissive;
        }

        // The fire's light this frame: each zone's glowing patch and its flames.
        private void Measure()
        {
            if (measuredFrame == Time.frameCount)
                return;
            measuredFrame = Time.frameCount;
            fireRadiance = Color.black;
            if (volume == null)
                volume = fire != null ? fire.GetComponent<FireboxVolume>() : null;
            volume?.MeasureZones();
            var origin = bed.BedPoint(0f, 0f, 0f);
            var across = bed.BedPoint(1f, 0f, 0f) - origin;
            var along = bed.BedPoint(0f, 1f, 0f) - origin;
            var up = (bed.BedPoint(0.5f, 0.5f, 1f) - bed.BedPoint(0.5f, 0.5f, 0f)).normalized;
            var area = across.magnitude * along.magnitude / GrateLayout.ZoneCount;
            var pulse = flames != null ? flames.Pulse : 0f;
            var lit = false;
            light = 0f;
            for (var i = 0; i < GrateLayout.ZoneCount; i++)
            {
                var zone = (GrateZone)i;
                var x = (GrateLayout.Column(zone) + 0.5f) / GrateLayout.Columns;
                var z = (GrateLayout.Row(zone) + 0.5f) / GrateLayout.Rows;
                var surface = bed.BedPoint(x, z, bed.SurfaceHeight(x, z));
                zones[i] = new Vector4(surface.x, surface.y, surface.z, area);
                var glow = ZoneGlow(bed.ZoneHeat(zone), bed.ZoneCover(zone)) * (BedGlow * BedLight * (1f + 0.6f * pulse));
                zoneLight[i] = new Vector4(glow.r, glow.g, glow.b, 0f);

                // Use the same firelight with the door open or shut, even
                // while its volume renderer and airflow are sleeping.
                var drawn = volume != null;
                var strength = drawn ? volume.ZoneStrength(zone) : flames != null ? flames.ZoneFlame(zone) : 0f;
                var height = drawn ? volume.ZoneHeight(zone) : flames != null ? flames.ZoneFlameHeight(zone) : 0f;
                var middle = surface + up * (0.5f * height * (1f + 0.3f * pulse));
                flamePlaces[i] = new Vector4(middle.x, middle.y, middle.z, Mathf.Max(0.08f, 0.5f * height));
                var flame = FireColors.Flame(FlameLightHeat) * (strength * height * FlameLight * (1f + 0.5f * pulse));
                flameLight[i] = new Vector4(flame.r, flame.g, flame.b, 0f);
                fireRadiance += glow + flame;
                // The light the eye adapts to, without the beats.
                light += (glow.grayscale / (1f + 0.6f * pulse) + flame.grayscale / (1f + 0.5f * pulse)) / GrateLayout.ZoneCount;
                lit |= glow.maxColorComponent > 0f || strength > 0f;
            }
            grateUp = new Vector4(up.x, up.y, up.z, lit ? 1f : 0f);
            MeasureDoor();
        }

        // The open firehole and the cab's light at it, looking out into the
        // cab: the scene's light there (its light probes, or its ambient
        // where it has none), through the hole's open share.
        private void MeasureDoor()
        {
            door = Vector4.zero;
            doorLight = Vector4.zero;
            var car = firebox != null ? firebox.Car : null;
            if (car == null || car.carLivery == null || !FireholeOpening.TryHole(car.carLivery.id, out var hole))
                return;
            var opening = firebox.HasDoorPort ? firebox.DoorOpening : 1f;
            if (opening < 0.01f)
                return;
            var frame = car.transform;
            var middle = frame.TransformPoint(new Vector3(hole.Middle, hole.Foot + 0.5f * hole.Height, hole.Plane));
            var cabward = frame.TransformDirection(Vector3.back);
            LightProbes.GetInterpolatedProbe(middle + 0.3f * cabward, null, out var probe);
            cabDirection[0] = cabward;
            probe.Evaluate(cabDirection, cabLight);
            door = new Vector4(middle.x, middle.y, middle.z, hole.Width * hole.Height * Mathf.Clamp01(opening));
            // A cab may have no useful baked probe even in daylight. Its open
            // hole still admits sky light; at night that fallback vanishes.
            var ambient = DoorDaylight * PlumeWeather.Daylight;
            var sampled = cabLight[0];
            var light = new Color(Mathf.Max(ambient, sampled.r), Mathf.Max(ambient, sampled.g), Mathf.Max(ambient, sampled.b)) * DoorLight;
            doorLight = new Vector4(Mathf.Max(0f, light.r), Mathf.Max(0f, light.g), Mathf.Max(0f, light.b), DoorBounce);
        }

        private Vector4 door;
        private Vector4 doorLight;
        private readonly Vector3[] cabDirection = { Vector3.back };
        private readonly Color[] cabLight = new Color[1];

        private Vector4 grateUp;

        // The fire's light this frame, its zones' glow and flames on average.
        private float light;
        private float hiddenLightAt;

        internal string Describe() =>
            $"lighting {light:F3}, door area {door.w:F3}, cab rgb {doorLight.x:F3}/{doorLight.y:F3}/{doorLight.z:F3}, bed seen {bed.Seen}; " +
            $"point {fire.fireLight?.enabled}/{fire.fireLight?.intensity:F3}, rgb {fire.fireLight?.color.r:F3}/{fire.fireLight?.color.g:F3}/{fire.fireLight?.color.b:F3}, " +
            $"fill {fire.fillLight?.intensity:F3} rgb {fire.fillLight?.color.r:F3}/{fire.fillLight?.color.g:F3}/{fire.fillLight?.color.b:F3}, " +
            $"bounce {fire.bounceLight?.intensity:F3} rgb {fire.bounceLight?.color.r:F3}/{fire.bounceLight?.color.g:F3}/{fire.bounceLight?.color.b:F3}";

        private void Apply(Renderer renderer, float scale, float glow = -1f)
        {
            if (renderer == null)
                return;
            renderer.GetPropertyBlock(block);
            block.SetVectorArray(ZonesId, zones);
            block.SetVectorArray(ZoneLightId, zoneLight);
            block.SetVectorArray(FlamesId, flamePlaces);
            block.SetVectorArray(FlameLightId, flameLight);
            block.SetVector(GrateUpId, grateUp);
            block.SetVector(DoorId, door);
            block.SetVector(DoorLightId, doorLight);
            block.SetFloat(DebugId, FireboxTestBed.Enabled ? FireboxTestBed.LightView : 0);
            block.SetFloat(LightScaleId, scale);
            if (glow >= 0f)
                block.SetFloat(GlowStrengthId, glow);
            renderer.SetPropertyBlock(block);
        }

        /// <summary>
        /// A zone's glow, its radiance on average as the bed draws it
        /// (the fire's color and CoalBed's strength, over the share of the zone
        /// glowing: the gaps first, then the lumps, all of it white-hot),
        /// dimmed where green coal lies dark on it.
        /// </summary>
        internal static Color ZoneGlow(float heat, float cover)
        {
            if (!(heat > 0.001f))
                return Color.black;
            var local = Mathf.Clamp01(heat) * (1f - 0.4f * cover);
            var forced = heat * heat * heat;
            // The gaps glow first; the lumps' caps, dimmer, only when hot.
            var glowing = Mathf.Clamp01(0.25f + 0.45f * heat);
            return FireColors.At(local) * (glowing * (0.4f + 1.2f * heat + 0.8f * forced * forced) * (1f - 0.5f * cover));
        }

        private void OnDestroy()
        {
            if (showing)
                Show(false);
            foreach (var (_, _, lit) in taken)
                foreach (var material in lit)
                    if (material != null)
                        Destroy(material);
        }
    }
}
