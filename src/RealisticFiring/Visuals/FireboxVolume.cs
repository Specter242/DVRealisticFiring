using RealisticFiring.Core;
#if !PUBLIC_RELEASE
using RealisticFiring.Diagnostics;
#endif
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Visuals
{
 internal sealed partial class FireboxVolume : MonoBehaviour
    {
        private const float ClosedUpdateTime = 0.25f;
        private const float PulseMeanTime = 1f;
        private const float LickStretch = 0.35f;

        // How fast a beat's surge comes up, s; and how long it takes to climb
        // from the coal to the flames' top (three steps of this).
        private const float BeatAttack = 0.035f;
        private const float BeatClimbStep = 0.04f;

        // A door open less than this shows nothing of the fire; and from
        // farther than this, m, the volume is not drawn.
        private const float DoorShut = 0.01f;
        private const float ViewDistance = 15f;

        // Kept within a few tiles, so the offsets never lose precision (the
        // noise tiles). The shader reads them at 0.5, 0.7, and 2 times, so
        // they wrap at 20 tiles, a whole number of tiles at each (at 16 the
        // billows, at 0.7, jumped 11.2 tiles and the flames popped).
        private const float WrapTiles = 20f;

        private float closedUpdateAt;
        private float closedUpdated;
        private int sourceRefreshes;

        private ZonedCoalBed bed;
        private FireboxFlames flames;
        private RealisticFirebox firebox;
        private Transform box;
        private MeshRenderer boxRenderer;
        private Material material;
        private Texture2D zonesA;
        private Texture2D zonesB;
        private Vector3 sheetOffset;
        private float pulse;

        // The surge's recent values and their times, newest last: enough
        // frames for the climb's three steps at 240 fps (16 held 0.07 s
        // there, under the 0.12 s the shader asks for).
        private readonly float[] surgeAt = new float[64];
        private readonly float[] surge = new float[64];
        private int surgeCount;
        private Vector3 curlOffset;
        private float rootOffset;
        private float pulseMean;
        private float lickOffset;

        // The zones' maps go to the shader at the airflow's rate too, not
        // every frame: the fire they draw changes as it burns, over seconds.
        private float zonesDueAt;
        // The test bed's tuning the fixed values were last set from.
        private int constantsVersion = -1;
        private Vector3 bedS;
        private Vector3 bedT;
        private float grateLevel;
        private float swayClock;
        private float puffClock;
        private float tipClock;
        private Vector3 boxSize;
        private float alongSign = 1f;

        /// <summary>The engine's brick arch, which the flames go under and round, or null.</summary>
        internal BrickArch Arch
        {
            get => arch;
            set
            {
                arch = value;
                constantsVersion = -1;
            }
        }

        private BrickArch arch;

        // Whether the arch was fitted when the constants were last set.
        private bool archFitted;

        /// <summary>How hard a zone's flames burn in the volume this frame, 0 to 1.</summary>
        public float ZoneStrength(GrateZone zone) => strength[(int)zone];

        /// <summary>How tall a zone's flames stand over its coal this frame, m.</summary>
        public float ZoneHeight(GrateZone zone) => top[(int)zone] * boxSize.y;

        /// <summary>Whether the volume draws this frame.</summary>
        public bool Drawing => boxRenderer != null && boxRenderer.enabled;

        /// <summary>The volume's renderer and material, for its half-resolution drawing (FireboxOffscreen).</summary>
        internal MeshRenderer VolumeRenderer => boxRenderer;
        internal Material VolumeMaterial => material;

        /// <summary>Whether a camera saw the volume last frame, through its open door.</summary>
        public bool Viewed { get; private set; }

        /// <summary>The game camera's distance from the box, m.</summary>
        public float Distance { get; private set; }

        /// <summary>The zones' flames and smoke as the volume draws them, for the test bed's log.</summary>
        internal string Describe()
        {
            var b = new System.Text.StringBuilder($"draft {flames.Draft:F2}, pulse {pulse:F2}; drawing {boxRenderer?.enabled}, viewed {Viewed}, quality {Main.Settings?.FireboxQuality}, half resolution {FireboxOffscreen.Drawing}, steps {material?.GetFloat(StepsId):F0}, spacing {material?.GetFloat(StepLengthId):F3} m; motion {lickOffset:F3}, dither {material?.GetFloat(DitherFrameId):F0}, source refreshes {sourceRefreshes}, clock {Time.unscaledTime:F3}; fire's light {FireColors.Light:F2}, exposure {FireColors.Exposed:F2}; gas burned over the fire {firebox.Model.OverFireBurnout:P0}, jets {firebox.Model.JetAirFlow:F2} kg/s of air (reach {firebox.Model.JetPenetration:P0}, burning {firebox.Model.JetBurnout:P0} of the gas, blowback {firebox.Model.Blowback:F2} kg/s, {material?.GetFloat(JetCountId):F0} drawn), overwhelm {FireColors.Overwhelm:F2}; ceiling {material?.GetFloat(CeilingId):F2}, smoke's {smokeCeiling:F2}; ");
            if (arch != null)
                b.Append($"brick arch's heat {arch.Heat:F2}; ");
            if (airflow != null)
            {
                // The airflow: its speed up and across on average and at most,
                // and how far across it bends the flames at most, m.
                float up = 0f, across = 0f, fastest = 0f, bend = 0f;
                for (var j = 0; j < airflow.Up; j++)
                {
                    for (var i = 0; i < airflow.Across; i++)
                    {
                        var u = airflow.VelocityAcross(i, j);
                        var v = airflow.VelocityUp(i, j);
                        up += v;
                        across += Mathf.Abs(u);
                        fastest = Mathf.Max(fastest, Mathf.Sqrt(u * u + v * v));
                        bend = Mathf.Max(bend, Mathf.Abs(airflow.CameFrom(i, j) - (i + 0.5f) * airflow.Width / airflow.Across));
                    }
                }
                var cells = airflow.Across * airflow.Up;
                b.Append($"airflow up {up / cells:F2} m/s, across {across / cells:F2}, fastest {fastest:F2}, bend {bend:F2} m; ");
                b.Append($"flow weights {flowWeights.x:F4}/{flowWeights.y:F4}, resets {airflow.RestartsA}/{airflow.RestartsB}, eddies {airflow.EddiesStarted}; ");
            }
            b.Append("zones (heat/strength/top/smoke/gas/cover):");
            for (var i = 0; i < GrateLayout.ZoneCount; i++)
                b.Append($" {pixelsB[GrateLayout.Row((GrateZone)i) * GrateLayout.Columns + GrateLayout.Column((GrateZone)i)].r:F2}/{strength[i]:F2}/{top[i]:F2}/{pixelsA[GrateLayout.Row((GrateZone)i) * GrateLayout.Columns + GrateLayout.Column((GrateZone)i)].b:F2}/{gas[i]:F2}/{bed.ZoneCover((GrateZone)i):F2}");
            return b.ToString();
        }

        private bool DoorOpen() => !firebox.HasDoorPort || firebox.DoorOpening > DoorShut;

        // The pixels the fire is drawn over, against the screen its budget
        // was set on: both eyes' in a headset.
        private static float PixelLoad()
        {
            float pixels;
            if (UnityEngine.XR.XRSettings.enabled)
                pixels = 2f * UnityEngine.XR.XRSettings.eyeTextureWidth * UnityEngine.XR.XRSettings.eyeTextureHeight;
            else
                pixels = (float)Screen.width * Screen.height;
            return pixels / FireboxSampling.ReferencePixels;
        }

        private bool Near()
        {
            var camera = PlayerManager.ActiveCamera != null ? PlayerManager.ActiveCamera : Camera.main;
            // A plume capture's camera is put at its view only as it draws:
            // from where it is held, not from where the player stands.
#if PUBLIC_RELEASE
            var eye = camera != null ? camera.transform.position : (Vector3?)null;
#else
            var eye = RealisticStackEffects.PlumeCapture.HeldAt ?? (camera != null ? camera.transform.position : (Vector3?)null);
#endif
            Distance = eye.HasValue && boxRenderer != null
                ? Mathf.Sqrt(boxRenderer.bounds.SqrDistance(eye.Value))
                : 0f;
            return Distance < ViewDistance;
        }

        /// <summary>Builds the volume in <paramref name="fire"/>'s box. Returns a line for the log, or null.</summary>
        public static string Attach(Fire fire, ZonedCoalBed bed, RealisticFirebox firebox)
        {
            var source = PlumeAssets.FireboxVolumeMaterial;
            var flames = fire != null ? fire.GetComponent<FireboxFlames>() : null;
            if (source == null || bed == null || !bed.Ready || flames == null || flames.SmokeSpace == null || fire.GetComponent<FireboxVolume>() != null)
                return null;
            var volume = fire.gameObject.AddComponent<FireboxVolume>();
            volume.bed = bed;
            volume.flames = flames;
            volume.firebox = firebox;
            volume.material = new Material(source) { name = source.name + " (firebox)" };
            volume.zonesA = ZoneTexture("firebox zones A");
            volume.zonesB = ZoneTexture("firebox zones B");
            volume.material.SetTexture(ZonesAId, volume.zonesA);
            volume.material.SetTexture(ZonesBId, volume.zonesB);
            volume.Build();
            FireboxOffscreen.Add(volume);
            var size = volume.boxSize;
            return $"flames and smoke drawn as one volume in the box, {size.x:F2} by {size.z:F2} m and {size.y:F2} m high";
        }

        private static Texture2D ZoneTexture(string name) =>
            new Texture2D(GrateLayout.Columns, GrateLayout.Rows, TextureFormat.RGBAHalf, false, true)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

        // The box: a unit cube in the smoke's space, scaled to the box; the
        // bed's frame against the box's.
        private void Build()
        {
            var space = flames.SmokeSpace;
            var bounds = flames.SmokeBox;
            box = new GameObject("[RealisticFiring firebox volume]").transform;
            box.SetParent(space, false);
            box.localPosition = bounds.center;
            box.localRotation = Quaternion.identity;
            box.localScale = bounds.size;
            box.gameObject.AddComponent<MeshFilter>().sharedMesh = UnitCube();
            boxRenderer = box.gameObject.AddComponent<MeshRenderer>();
            boxRenderer.sharedMaterial = material;
            boxRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            boxRenderer.receiveShadows = false;
            boxRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            boxRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            var scale = space.lossyScale;
            boxSize = new Vector3(bounds.size.x * Mathf.Abs(scale.x), bounds.size.y * Mathf.Abs(scale.y), bounds.size.z * Mathf.Abs(scale.z));
            material.SetVector(BoxSizeId, boxSize);

            // The bed's corners in the box's frame (0..1): its across and along
            // from the box's x and z.
            Vector3 InBox(float s, float t) => box.InverseTransformPoint(bed.BedPoint(s, t, 0f)) + 0.5f * Vector3.one;
            var origin = InBox(0f, 0f);
            var across = InBox(1f, 0f) - origin;
            var along = InBox(0f, 1f) - origin;
            var det = across.x * along.z - along.x * across.z;
            if (Mathf.Abs(det) < 1e-6f)
                det = 1e-6f;
            // [s t] = inverse([across along]) ([u.x u.z] - origin).
            var s = new Vector3(along.z / det, -along.x / det, 0f);
            var t = new Vector3(-across.z / det, across.x / det, 0f);
            s.z = -(s.x * origin.x + s.y * origin.z);
            t.z = -(t.x * origin.x + t.y * origin.z);
            material.SetVector(BedSId, s);
            material.SetVector(BedTId, t);
            bedS = s;
            bedT = t;
            alongSign = Mathf.Sign(along.z);

            // Looking from the cab, the interior may be seen only through its
            // firehole. Scene depth alone let flames appear below the backhead;
            // the volume must also start beyond its plane and inside the hole.
            // These rows map the volume's centered cube to the car's frame,
            // so they remain valid as the locomotive moves and rolls.
            var car = firebox.Car;
            var livery = car != null && car.carLivery != null ? car.carLivery.id : null;
            if (livery != null && FireholeOpening.TryHole(livery, out var hole))
            {
                var toCar = FiringFrame.Of(car).worldToLocalMatrix * box.localToWorldMatrix;
                var acrossHole = toCar.GetRow(0);
                acrossHole.w -= hole.Middle - hole.Width / 2f;
                var upHole = toCar.GetRow(1);
                upHole.w -= hole.Foot;
                var plane = toCar.GetRow(2);
                plane.w -= hole.Plane;
                material.SetVector(HoleAcrossId, acrossHole / hole.Width);
                material.SetVector(HoleUpId, upHole / hole.Height);
                material.SetVector(HolePlaneId, plane);
            }

            InitAirflow();
        }

        private static Mesh cube;

        private static Mesh UnitCube()
        {
            if (cube != null)
                return cube;
            var v = new Vector3[8];
            for (var i = 0; i < 8; i++)
                v[i] = new Vector3((i & 1) - 0.5f, ((i >> 1) & 1) - 0.5f, ((i >> 2) & 1) - 0.5f);
            // Each face's two triangles, wound outward; the shader draws the back faces.
            int[] faces =
            {
                0, 2, 3, 0, 3, 1, // -z
                4, 5, 7, 4, 7, 6, // +z
                0, 4, 6, 0, 6, 2, // -x
                1, 3, 7, 1, 7, 5, // +x
                0, 1, 5, 0, 5, 4, // -y
                2, 6, 7, 2, 7, 3, // +y
            };
            cube = new Mesh { name = "RealisticFiring firebox volume", vertices = v, triangles = faces };
            cube.RecalculateBounds();
            return cube;
        }

        private void LateUpdate()
        {
#if !PUBLIC_RELEASE
            var start = PerfMeter.Start();
#endif
            UpdateVolume();
#if !PUBLIC_RELEASE
            PerfMeter.Stop(PerfSection.FireboxVolume, start);
#endif
        }

        private void UpdateVolume()
        {
#if PUBLIC_RELEASE
            var show = Main.Settings != null && Main.Settings.ShowZonedCoalBed && bed != null && bed.Ready
                       && firebox != null && firebox.Model.FireOn;
#else
            var show = Main.Settings != null && Main.Settings.ShowZonedCoalBed && bed != null && bed.Ready
                       && firebox != null && firebox.Model.FireOn && !FireboxTestBed.HidingFlames;
#endif
            // Keep an inexpensive fire behind a shut door's perforations.
            // Its opaque parts stop the rays at the scene depth as usual.
            // (A capture holds the camera where it shoots only while it renders,
            // so its distance here is the player's: captures skip it.)
#if PUBLIC_RELEASE
            var near = show && Near();
#else
            var near = show && (Near() || FireboxCapture.KeepsVisible);
#endif
            var open = near && DoorOpen();
            var closed = near && !open && ClosedFire > 0.5f;
            var draw = open || closed;
            if (boxRenderer != null && boxRenderer.enabled != draw)
                boxRenderer.enabled = draw;
            flames.VolumeDraws = show;
            // Viewed denotes the full-rate open-door path. The closed path
            // keeps its smaller drawing budget without waking that path.
            Viewed = open && boxRenderer != null && boxRenderer.isVisible;
            if (!show)
                return;
            var dt = Time.deltaTime;
            var draft = flames.Draft;
            // A beat's surge rises over a few frames, as the footage's does
            // (0.07 to 0.10 s), and falls as the fire's pulse does.
            var beat = flames.Pulse;
            pulse = beat > pulse ? pulse + (beat - pulse) * (1f - Mathf.Exp(-dt / BeatAttack)) : beat;
            pulseMean += (pulse - pulseMean) * (1f - Mathf.Exp(-dt / PulseMeanTime));
            jetBurnout += (firebox.Model.JetBurnout - jetBurnout) * (1f - Mathf.Exp(-dt / JetClearTime));
            Remember(Time.time, pulse);
            if (!Viewed && !closed)
                return;
            // Read the local quality each frame, outside the closed door's
            // source-upload throttle. It changes only the ray sampling;
            // the fire's motion, lighting, and sources keep their budgets.
            var sampling = FireboxSampling.For(Main.Settings.FireboxQuality, closed, Distance <= 0f, Steps, StepLength, PixelLoad(), FireboxOffscreen.Drawing);
            material.SetFloat(StepsId, sampling.MaxSteps);
            material.SetFloat(StepLengthId, sampling.StepLength);
            // The flow: up and toward the tube plate, faster with the draft and
            // on each beat; the curls a little faster than the sheets.
            var forcing = Mathf.Sqrt(Mathf.Clamp01(draft));
            var rise = Mathf.Lerp(RiseDrifting, RiseForced, forcing) * (1f + BeatSurge * pulse);
            var lean = LeanForced * draft;
            var tile = Mathf.Max(NoiseTile, 0.05f);
            var flow = new Vector3(0f, rise, alongSign * rise * lean) / tile;
#if !PUBLIC_RELEASE
            if (FireboxTestBed.Enabled && FreezeFlow > 0.5f)
                dt = 0f;
#endif
            sheetOffset = Wrap(sheetOffset + flow * dt);
            // The curls ride the flow with the flames and churn across it, the
            // more so the harder the draft; a working fire's flames stand, so
            // its curls show only part of the flow's rise (carried up at it in
            // full, they streamed the whole fire up, and jumped on each beat).
            var carry = Mathf.Lerp(CarryDrifting, CarryForced, forcing);
            var churn = new Vector3(0.6f, 0.3f, -0.5f) * (0.03f + ChurnForced * forcing) / tile;
            curlOffset = Wrap(curlOffset + (new Vector3(flow.x, flow.y * carry, flow.z) + churn) * dt);
            var warp = Mathf.Lerp(WarpDrifting, WarpForced, forcing);
            rootOffset = Mathf.Repeat(rootOffset + Mathf.Lerp(RootDriftDrifting, RootDriftForced, forcing) * dt, 16f);
            // The short flames' motion by the draft: calm at a low one (rooted
            // at their spots, broader, swaying and pumping little), a working
            // fire's above (its forcing, the draft's square root, reached into
            // a drifting fire's and a drifting fire's into a working one's).
            var lickMotion = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(LickCalmDraft, LickLiveDraft, draft));
            lickOffset = Mathf.Repeat(lickOffset + (rise * LickScale * LickStretch + Mathf.Lerp(LickFlickerDrifting, LickFlickerForced, lickMotion)) * dt, 16f);
            swayClock = Mathf.Repeat(swayClock + Mathf.Lerp(SwayDrifting, SwayForced, forcing) * dt, 16f);
            puffClock = Mathf.Repeat(puffClock + Mathf.Lerp(PuffDrifting, PuffForced, forcing) * dt, 16f);
            tipClock = Mathf.Repeat(tipClock + Mathf.Lerp(TipWhipRateDrifting, TipWhipRateForced, forcing) * (1f + TipWhipBeat * pulse) * dt, 16f);
            // Keep the visible animation smooth through a shut door. Only
            // the sources and airflow below use its four-Hz update budget.
            material.SetFloat(LeanId, lean);
            material.SetVector(SheetOffsetId, sheetOffset);
            material.SetVector(CurlOffsetId, curlOffset);
            material.SetFloat(PulseMeanId, pulseMean);
            material.SetFloat(LickOffsetId, lickOffset);
            material.SetFloat(DitherFrameId, Time.frameCount % 4096);
            material.SetFloat(TipClockId, tipClock);
            material.SetFloat(SwayClockId, swayClock);
            material.SetFloat(PuffClockId, puffClock);
            material.SetFloat(RootOffsetId, rootOffset);
            material.SetVector(PulseHistoryId, new Vector4(pulse, SurgeAgo(BeatClimbStep), SurgeAgo(2f * BeatClimbStep), SurgeAgo(3f * BeatClimbStep)));
            if (closed)
            {
                if (Time.unscaledTime < closedUpdateAt)
                    return;
                dt = Mathf.Clamp(Time.time - closedUpdated, 0f, ClosedUpdateTime);
                closedUpdateAt = Time.unscaledTime + ClosedUpdateTime;
#if !PUBLIC_RELEASE
                if (FireboxTestBed.Enabled && FreezeFlow > 0.5f)
                    dt = 0f;
#endif
            }
            else
                closedUpdateAt = 0f;
            closedUpdated = Time.time;
            sourceRefreshes++;
            MeasureZones();
            if (closed || Time.time >= zonesDueAt)
            {
                Shape(draft);
                zonesDueAt = Time.time + FlowStep;
            }
            Carry(dt, rise, forcing);
#if PUBLIC_RELEASE
            if (constantsVersion != 0 || archFitted != (Arch != null && Arch.Fitted))
            {
                SetConstants();
                constantsVersion = 0;
            }
#else
            if (constantsVersion != FireboxTestBed.TuningVersion || archFitted != (Arch != null && Arch.Fitted))
            {
                SetConstants();
                constantsVersion = FireboxTestBed.TuningVersion;
            }
#endif

            // What changes with the fire's draft and smoke.
            material.SetFloat(LickMotionId, lickMotion);
            material.SetVector(FlowWeightsId, flowWeights);
            material.SetFloat(PuffId, Mathf.Lerp(PuffSizeDrifting, PuffSizeForced, forcing));
            material.SetFloat(FlowCarryId, carry);
            material.SetFloat(TipWhipId, Mathf.Lerp(TipWhipDrifting, TipWhipForced, forcing));
            material.SetFloat(BaseCokeId, Mathf.Lerp(BaseCokeDrifting, BaseCokeForced, forcing));
            material.SetFloat(BaseGlowId, Mathf.Lerp(BaseGlowDrifting, BaseGlowForced, forcing));
            material.SetFloat(WhiteHeartId, WhiteHeart * Mathf.InverseLerp(WhiteDraftLow, WhiteDraftFull, draft));
            var spotLow = Mathf.Clamp(SpotLow, 0f, 0.95f);
            var spotShare = 0.5f * (1f - spotLow);
            var spotGlare = Mathf.Clamp(Mathf.Lerp(SpotGlareDrifting, SpotGlareForced, forcing), 1f, 1f / spotShare);
            material.SetFloat(SpotDimId, (1f - spotShare * spotGlare) / (1f - spotShare));
            material.SetFloat(SpotGlareId, spotGlare);
            // A choked fire's soot gathers into a fuller cloud in still gas;
            // a drawing fire keeps its thinner, broken wisps.
            var smoky = Mathf.Clamp01(firebox.Model.SmokeDensity);
            // Over a few seconds, as the smoke in the box is drawn off or
            // burned: at once, opening the blower and the jets cleared a
            // murky box between two frames (the council, round 1).
            var chokedNow = (1f - Mathf.SmoothStep(0f, 1f, draft / 0.15f)) * smoky * smoky;
            choked += (chokedNow - choked) * (1f - Mathf.Exp(-Time.deltaTime / JetClearTime));
            material.SetFloat(SmokeDensityId, SmokeDensity * (1f + ChokedSmoke * choked));
            material.SetFloat(WarpId, warp);
            material.SetFloat(SmokeClumpsId, SmokeClumps * (1f - 0.4f * choked));
            SetJets();
            FireColors.Publish();
#if PUBLIC_RELEASE
            material.SetFloat(DebugId, 0);
#else
            material.SetFloat(DebugId, FireboxTestBed.Enabled ? FireboxTestBed.VolumeView : 0);
#endif
        }

        private void Remember(float now, float value)
        {
            if (surgeCount == surge.Length)
            {
                System.Array.Copy(surge, 1, surge, 0, surge.Length - 1);
                System.Array.Copy(surgeAt, 1, surgeAt, 0, surgeAt.Length - 1);
                surgeCount--;
            }
            surge[surgeCount] = value;
            surgeAt[surgeCount] = now;
            surgeCount++;
        }

        // The surge as it was this long ago, between the frames remembered.
        private float SurgeAgo(float ago)
        {
            var when = Time.time - ago;
            for (var i = surgeCount - 1; i > 0; i--)
            {
                if (surgeAt[i - 1] <= when)
                {
                    var span = surgeAt[i] - surgeAt[i - 1];
                    return span > 1e-5f ? Mathf.Lerp(surge[i - 1], surge[i], (when - surgeAt[i - 1]) / span) : surge[i];
                }
            }
            return surgeCount > 0 ? surge[0] : 0f;
        }

        private static Vector3 Wrap(Vector3 offset) =>
            new Vector3(Mathf.Repeat(offset.x, WrapTiles), Mathf.Repeat(offset.y, WrapTiles), Mathf.Repeat(offset.z, WrapTiles));

        private void OnDestroy()
        {
            FireboxOffscreen.Remove(this);
            if (flames != null)
                flames.VolumeDraws = false;
            if (box != null)
                Destroy(box.gameObject);
            if (material != null)
                Destroy(material);
            if (zonesA != null)
                Destroy(zonesA);
            if (zonesB != null)
                Destroy(zonesB);
            DisposeAirflow();
        }
    }
}
