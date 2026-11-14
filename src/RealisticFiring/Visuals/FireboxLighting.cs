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
        private static readonly int BedOriginId = Shader.PropertyToID("_FireBedOrigin");
        private static readonly int BedAcrossId = Shader.PropertyToID("_FireBedAcross");
        private static readonly int BedAlongId = Shader.PropertyToID("_FireBedAlong");
        private static readonly int StayboltsId = Shader.PropertyToID("_Staybolts");
        private static readonly int SootId = Shader.PropertyToID("_Soot");
        private static readonly int LightScaleId = Shader.PropertyToID("_FireLightScale");
        private static readonly int GlowStrengthId = Shader.PropertyToID("_GlowStrength");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BumpMapId = Shader.PropertyToID("_BumpMap");
        private static readonly int CutoffId = Shader.PropertyToID("_Cutoff");
        private static readonly int ArchCornerId = Shader.PropertyToID("_FireArchCorner");
        private static readonly int ArchAcrossId = Shader.PropertyToID("_FireArchAcross");
        private static readonly int ArchAlongId = Shader.PropertyToID("_FireArchAlong");
        private static readonly int ArchUpId = Shader.PropertyToID("_FireArchUp");
        private static readonly int ArchGlowId = Shader.PropertyToID("_FireArchGlow");

        // A glowing zone's radiance against its heat, as the bed draws its glow
        // there on average (CoalBed: the glow's color, how much of the zone
        // glows, and its strength); and the flames' light against their
        // strength and height. Both to be set against footage on the test bed.
        internal static float BedLight = 1f;
        // How much of a burning zone, seen from the box, is its bright gaps
        // and the lumps' glowing feet; the rest is their darker caps.
        internal static float GapShare = 0.25f;
        internal static float FlameLight = 0.6f;

        // How bright the bed's glow draws (CoalBed's strength), and so the
        // light it gives; the embers', seen in the gaps under the coal
        // (EmberBed); and the flames' temperature, 0 to 1, for the color of
        // their light (FireColors.Flame).
        internal static float BedGlow = 0.6f;
        internal static float EmberGlow = 0.7f;
        internal static float FlameLightHeat = 0.6f;

        internal static float WallLight = 0.18f;
        internal static float CoalLight = 0.08f;

        internal static float DoorLight = 5f;
        internal static float DoorDaylight = 0.2f;
        internal static float DoorBounce = 0.15f;
        internal static float S060Light = 1.6f;

        // Staybolt heads on the box's sheets, 0 none to 1 (FireboxWall).
        internal static float Staybolts = 1f;
        // Soot up the box's sheets and on its crown, 0 none to 1.
        internal static float Soot = 1f;

        // An alpha-tested or blended stock material's holes (the tube plate's
        // tubes) are cut where its alpha falls under this.
        private const float HoleCutoff = 0.5f;

        private Fire fire;
        private RealisticFirebox firebox;
        private ZonedCoalBed bed;
        private FireboxFlames flames;
        private FireboxVolume volume;
        private CoalLumps lumps;
        private BrickArch arch;
        private readonly List<(Renderer Renderer, Material[] Stock, Material[] Lit)> taken = new List<(Renderer, Material[], Material[])>();
        private Renderer[] stockEmissive;
        private bool showing;
        private MaterialPropertyBlock block;
        /// <summary>Other things in the box lit by its fire as the coal is: the wood and rags on the grate.</summary>
        internal readonly List<Renderer> Extra = new List<Renderer>();

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
            lighting.arch = fire.GetComponent<BrickArch>();
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
            // The brick arch: paler than the sheets, with no staybolts, and
            // too hot under the fire to hold soot.
            if (arch != null && arch.Renderer != null)
                Apply(arch.Renderer, BrickArch.ArchLight * gain, bolts: 0f, soot: 0f);
            Extra.RemoveAll(r => r == null);
            foreach (var renderer in Extra)
                if (renderer.gameObject.activeInHierarchy) Apply(renderer, CoalLight * gain);
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
            // FireRadiance is also read by the cab's lights before this
            // component's LateUpdate. Apply the hidden budget here so every
            // caller shares it, while a visible fire measures immediately.
            if (!bed.Seen && Time.unscaledTime < hiddenMeasureAt)
                return;
            hiddenMeasureAt = Time.unscaledTime + 0.25f;
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
            bedOrigin = origin;
            bedAcross = across.normalized;
            bedAcross.w = across.magnitude;
            bedAlong = along.normalized;
            bedAlong.w = along.magnitude;
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
                // Wood's glowing char lights the box as embers do, under its flames.
                var starter = firebox.Model.Starters[i];
                if (starter.EmberGlow > 0f)
                    glow += CharGlow(0.45f * starter.EmberGlow) * (BedGlow * BedLight);
                zoneLight[i] = new Vector4(glow.r, glow.g, glow.b, 0f);

                // Use the same firelight with the door open or shut, even
                // while its volume renderer and airflow are sleeping.
                var drawn = volume != null;
                var strength = drawn ? volume.ZoneStrength(zone) : flames != null ? flames.ZoneFlame(zone) : 0f;
                var height = drawn ? volume.ZoneHeight(zone) : flames != null ? flames.ZoneFlameHeight(zone) : 0f;
                // A wood fire's flames, and a burning rag's, over their zone
                // (the volume draws them too; behind a shut door it may sleep).
                var wood = WoodFlame.Strength(starter);
                if (wood <= 0f && starter.RagLit)
                    wood = WoodFlame.RagLight;
                if (wood > strength * height / 0.35f)
                {
                    strength = Mathf.Max(strength, 0.8f * wood);
                    height = Mathf.Max(height, WoodFlame.Top(starter, wood));
                }
                // Under the brick arch the flames stand no higher than its bricks.
                if (arch != null)
                    height = Mathf.Min(height, arch.Room(surface));
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
            // The arch's underside, for its shadow on what stands over it.
            archCorner = archAcross = archAlong = archUp = archGlow = Vector4.zero;
            if (arch != null && arch.Renderer != null && arch.Renderer.enabled)
            {
                arch.Underside(out var corner, out var acrossArch, out var alongArch, out var upArch);
                archCorner = new Vector4(corner.x, corner.y, corner.z, 1f);
                archAcross = new Vector4(acrossArch.x, acrossArch.y, acrossArch.z, 1f / Mathf.Max(acrossArch.sqrMagnitude, 1e-6f));
                archAlong = new Vector4(alongArch.x, alongArch.y, alongArch.z, 1f / Mathf.Max(alongArch.sqrMagnitude, 1e-6f));
                archUp = new Vector4(upArch.x, upArch.y, upArch.z, BrickArch.ArchShadow);
                // And the bricks' own light on what lies under and beside
                // them: their glow, and the fire's light they give back.
                var middle = corner + 0.5f * (acrossArch + alongArch);
                var gain = firebox.Car?.carLivery?.id == "LocoS060" ? S060Light : 1f;
                var radiance = FireColors.At(Mathf.Clamp01(0.93f * arch.Heat)) * BrickArch.ArchGlow
                               + LightAt(middle, -upArch) * (BrickArch.ArchLight * gain * BrickArch.ArchAlbedo);
                archGlow = new Vector4(radiance.r, radiance.g, radiance.b, 0f);
            }
            MeasureDoor();
        }

        // The fire's light on a surface at a point facing a way, over pi, as
        // the shaders sum it (FireLight in FireLight.cginc).
        private Color LightAt(Vector3 point, Vector3 normal)
        {
            const float sideGlow = 0.35f;
            var up = (Vector3)grateUp;
            var sum = Color.black;
            for (var i = 0; i < GrateLayout.ZoneCount; i++)
            {
                var toZone = (Vector3)zones[i] - point;
                var zone2 = Mathf.Max(toZone.sqrMagnitude, 1e-4f);
                var l = toZone / Mathf.Sqrt(zone2);
                var area = zones[i].w;
                var emitting = sideGlow + (1f - sideGlow) * Mathf.Clamp01(Vector3.Dot(up, -l));
                sum += (Color)zoneLight[i] * (area * Mathf.Clamp01(Vector3.Dot(normal, l)) * emitting / (Mathf.PI * zone2 + area));
                var toFlame = (Vector3)flamePlaces[i] - point;
                var flame2 = Mathf.Max(toFlame.sqrMagnitude, 1e-4f);
                var radius = flamePlaces[i].w;
                sum += (Color)flameLight[i] * (Mathf.Clamp01(Vector3.Dot(normal, toFlame / Mathf.Sqrt(flame2))) / (flame2 + radius * radius));
            }
            return sum * grateUp.w;
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
        private Vector4 bedOrigin;
        private Vector4 bedAcross;
        private Vector4 bedAlong;
        private Vector4 archCorner;
        private Vector4 archAcross;
        private Vector4 archAlong;
        private Vector4 archUp;
        private Vector4 archGlow;

        // The fire's light this frame, its zones' glow and flames on average.
        private float light;
        private float hiddenLightAt;
        private float hiddenMeasureAt;

        internal string Describe() =>
            $"lighting {light:F3}, door area {door.w:F3}, cab rgb {doorLight.x:F3}/{doorLight.y:F3}/{doorLight.z:F3}, bed seen {bed.Seen}; " +
            $"point {fire.fireLight?.enabled}/{fire.fireLight?.intensity:F3}, rgb {fire.fireLight?.color.r:F3}/{fire.fireLight?.color.g:F3}/{fire.fireLight?.color.b:F3}, " +
            $"fill {fire.fillLight?.intensity:F3} rgb {fire.fillLight?.color.r:F3}/{fire.fillLight?.color.g:F3}/{fire.fillLight?.color.b:F3}, " +
            $"bounce {fire.bounceLight?.intensity:F3} rgb {fire.bounceLight?.color.r:F3}/{fire.bounceLight?.color.g:F3}/{fire.bounceLight?.color.b:F3}";

        private void Apply(Renderer renderer, float scale, float glow = -1f, float bolts = 1f, float soot = 1f)
        {
            if (renderer == null)
                return;
            renderer.GetPropertyBlock(block);
            block.SetVectorArray(ZonesId, zones);
            block.SetVectorArray(ZoneLightId, zoneLight);
            block.SetVectorArray(FlamesId, flamePlaces);
            block.SetVectorArray(FlameLightId, flameLight);
            block.SetVector(GrateUpId, grateUp);
            block.SetVector(BedOriginId, bedOrigin);
            block.SetVector(BedAcrossId, bedAcross);
            block.SetVector(BedAlongId, bedAlong);
            block.SetFloat(StayboltsId, Mathf.Clamp01(Staybolts) * bolts);
            block.SetFloat(SootId, Mathf.Clamp01(Soot) * soot);
            block.SetVector(ArchCornerId, archCorner);
            block.SetVector(ArchAcrossId, archAcross);
            block.SetVector(ArchAlongId, archAlong);
            block.SetVector(ArchUpId, archUp);
            block.SetVector(ArchGlowId, archGlow);
            block.SetVector(DoorId, door);
            block.SetVector(DoorLightId, doorLight);
            block.SetFloat(DebugId, FireboxTestBed.Enabled ? FireboxTestBed.LightView : 0);
            block.SetFloat(LightScaleId, scale);
            if (glow >= 0f)
                block.SetFloat(GlowStrengthId, glow);
            renderer.SetPropertyBlock(block);
        }

        /// <summary>
        /// A zone's glow, its radiance on average as the bed and its lumps
        /// draw it (the gaps at the bed's core heat, the lumps' caps cooler
        /// until the zone nears white heat), dimmed where green coal lies
        /// dark on it.
        /// </summary>
        internal static Color ZoneGlow(float heat, float cover)
        {
            if (!(heat > 0.001f))
                return Color.black;
            // The gaps and the lumps' feet at the bed's core heat, a share of
            // what the box sees of the bed; the lumps' caps, cooler, the rest.
            var cap = Mathf.Clamp01(heat) * Mathf.Lerp(FireColors.CapHeat, 0.85f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.9f, 1f, heat)));
            var glow = FireColors.At(FireColors.CoreHeat(heat)) * GapShare + FireColors.At(cap) * (1f - GapShare);
            return glow * (1f - 0.5f * cover);
        }

        // Wood's glowing char, by its own heat: no deep bed burns under it.
        private static Color CharGlow(float heat)
        {
            if (!(heat > 0.001f))
                return Color.black;
            var forced = heat * heat * heat;
            return FireColors.At(Mathf.Clamp01(heat)) * (Mathf.Clamp01(0.25f + 0.45f * heat) * (0.4f + 1.2f * heat + 0.8f * forced * forced));
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
