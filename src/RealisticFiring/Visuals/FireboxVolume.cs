using RealisticFiring.Core;
using RealisticFiring.Diagnostics;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Visuals
{
 internal sealed class FireboxVolume : MonoBehaviour
    {
        private static readonly int ZonesAId = Shader.PropertyToID("_ZonesA");
        private static readonly int ZonesBId = Shader.PropertyToID("_ZonesB");
        private static readonly int BoxSizeId = Shader.PropertyToID("_BoxSize");
        private static readonly int BedSId = Shader.PropertyToID("_BedS");
        private static readonly int BedTId = Shader.PropertyToID("_BedT");
        private static readonly int HoleAcrossId = Shader.PropertyToID("_HoleAcross");
        private static readonly int HoleUpId = Shader.PropertyToID("_HoleUp");
        private static readonly int HolePlaneId = Shader.PropertyToID("_HolePlane");
        private static readonly int LeanId = Shader.PropertyToID("_Lean");
        private static readonly int SheetOffsetId = Shader.PropertyToID("_SheetOffset");
        private static readonly int CurlOffsetId = Shader.PropertyToID("_CurlOffset");
        private static readonly int PulseHistoryId = Shader.PropertyToID("_PulseHistory");
        private static readonly int StepsId = Shader.PropertyToID("_Steps");
        private static readonly int StepLengthId = Shader.PropertyToID("_StepLength");
        private static readonly int GlowId = Shader.PropertyToID("_Glow");
        private static readonly int FlameDensityId = Shader.PropertyToID("_FlameDensity");
        private static readonly int SmokeDensityId = Shader.PropertyToID("_SmokeDensity");
        private static readonly int SmokeAlbedoId = Shader.PropertyToID("_SmokeAlbedo");
        private static readonly int SmokeLightId = Shader.PropertyToID("_SmokeLight");
        private static readonly int SharpId = Shader.PropertyToID("_Sharp");
        private static readonly int EdgeMeanId = Shader.PropertyToID("_EdgeMean");
        private static readonly int SmokeLocalId = Shader.PropertyToID("_SmokeLocal");
        private static readonly int SmokeMeanId = Shader.PropertyToID("_SmokeMean");
        private static readonly int CeilingId = Shader.PropertyToID("_Ceiling");
        private static readonly int DetailBlurId = Shader.PropertyToID("_DetailBlur");
        private static readonly int DitherAmountId = Shader.PropertyToID("_DitherAmount");
        private static readonly int DitherShiftId = Shader.PropertyToID("_DitherShift");
        private static readonly int SmokePlumeId = Shader.PropertyToID("_SmokePlume");
        private static readonly int SmokeStrideId = Shader.PropertyToID("_SmokeStride");
        private static readonly int WarpId = Shader.PropertyToID("_Warp");
        private static readonly int NoiseTileId = Shader.PropertyToID("_NoiseTile");
        private static readonly int SheetLowId = Shader.PropertyToID("_SheetLow");
        private static readonly int SheetHighId = Shader.PropertyToID("_SheetHigh");
        private static readonly int CarpetId = Shader.PropertyToID("_Carpet");
        private static readonly int StretchId = Shader.PropertyToID("_Stretch");
        private static readonly int DebugId = Shader.PropertyToID("_VolumeDebug");
        private static readonly int MixSheetsId = Shader.PropertyToID("_MixSheets");
        private static readonly int MixCurlsId = Shader.PropertyToID("_MixCurls");
        private static readonly int MixLobesId = Shader.PropertyToID("_MixLobes");
        private static readonly int HeatScaleId = Shader.PropertyToID("_HeatScale");
        private static readonly int FlameHeatId = Shader.PropertyToID("_FlameHeat");
        private static readonly int EdgeHeatId = Shader.PropertyToID("_EdgeHeat");
        private static readonly int CoreDepthId = Shader.PropertyToID("_CoreDepth");
        private static readonly int SmokeClumpsId = Shader.PropertyToID("_SmokeClumps");
        private static readonly int RootsId = Shader.PropertyToID("_Roots");
        private static readonly int BeatGlowId = Shader.PropertyToID("_BeatGlow");
        private static readonly int BeatHeightId = Shader.PropertyToID("_BeatHeight");
        private static readonly int BeatHeatId = Shader.PropertyToID("_BeatHeat");
        private static readonly int BeatCoverId = Shader.PropertyToID("_BeatCover");
        private static readonly int LickHeightId = Shader.PropertyToID("_LickHeight");
        private static readonly int LickDepthId = Shader.PropertyToID("_LickDepth");
        private static readonly int LickScaleId = Shader.PropertyToID("_LickScale");
        private static readonly int LickLowId = Shader.PropertyToID("_LickLow");
        private static readonly int LickStrengthId = Shader.PropertyToID("_LickStrength");
        private static readonly int LickGlowId = Shader.PropertyToID("_LickGlow");
        private static readonly int LickHeatId = Shader.PropertyToID("_LickHeat");
        private static readonly int LickMotionId = Shader.PropertyToID("_LickMotion");
        private static readonly int LickStandingId = Shader.PropertyToID("_LickStanding");
        private static readonly int LickEdgeSharpnessId = Shader.PropertyToID("_LickEdgeSharpness");
        private static readonly int LickDetailId = Shader.PropertyToID("_LickDetail");
        private static readonly int LickCurlId = Shader.PropertyToID("_LickCurl");
        private static readonly int LickRoughnessId = Shader.PropertyToID("_LickRoughness");
        private static readonly int LickOffsetId = Shader.PropertyToID("_LickOffset");
        private static readonly int DoorSlopeId = Shader.PropertyToID("_DoorSlope");
        private static readonly int GrateLevelId = Shader.PropertyToID("_GrateLevel");
        private static readonly int BedReliefId = Shader.PropertyToID("_BedRelief");
        private static readonly int BedRiseId = Shader.PropertyToID("_BedRise");
        private static readonly int BoxLightId = Shader.PropertyToID("_BoxLight");
        private static readonly int SwayId = Shader.PropertyToID("_Sway");
        private static readonly int PuffId = Shader.PropertyToID("_Puff");
        private static readonly int BuoyancyId = Shader.PropertyToID("_Buoyancy");
        private static readonly int BaseGlowId = Shader.PropertyToID("_BaseGlow");
        private static readonly int BaseCokeId = Shader.PropertyToID("_BaseCoke");
        private static readonly int FlowCarryId = Shader.PropertyToID("_FlowCarry");
        private static readonly int DetailFlowCarryId = Shader.PropertyToID("_DetailFlowCarry");
        private static readonly int TipWhipId = Shader.PropertyToID("_TipWhip");
        private static readonly int TipClockId = Shader.PropertyToID("_TipClock");
        private static readonly int BaseHeightId = Shader.PropertyToID("_BaseHeight");
        private static readonly int WhiteHeartId = Shader.PropertyToID("_WhiteHeart");
        private static readonly int SpotLowId = Shader.PropertyToID("_SpotLow");
        private static readonly int SpotDimId = Shader.PropertyToID("_SpotDim");
        private static readonly int SpotGlareId = Shader.PropertyToID("_SpotGlare");
        private static readonly int TipHeatId = Shader.PropertyToID("_TipHeat");
        private static readonly int SwayClockId = Shader.PropertyToID("_SwayClock");
        private static readonly int PuffClockId = Shader.PropertyToID("_PuffClock");
        private static readonly int SideWidthId = Shader.PropertyToID("_SideWidth");
        private static readonly int SideSwirlId = Shader.PropertyToID("_SideSwirl");
        private static readonly int FlowAId = Shader.PropertyToID("_FlowA");
        private static readonly int FlowBId = Shader.PropertyToID("_FlowB");
        private static readonly int FlowAPrevId = Shader.PropertyToID("_FlowAPrev");
        private static readonly int FlowLerpId = Shader.PropertyToID("_FlowLerp");
        private static readonly int FlowWeightsId = Shader.PropertyToID("_FlowWeights");
        private static readonly int DetailScaleId = Shader.PropertyToID("_DetailScale");
        private static readonly int DetailErodeId = Shader.PropertyToID("_DetailErode");
        private static readonly int DetailStreakId = Shader.PropertyToID("_DetailStreak");
        private static readonly int GasFillId = Shader.PropertyToID("_GasFill");
        private static readonly int StretchFineId = Shader.PropertyToID("_StretchFine");
        private static readonly int DitherFrameId = Shader.PropertyToID("_DitherFrame");
        private static readonly int CrinkleId = Shader.PropertyToID("_Crinkle");
        private static readonly int SparseId = Shader.PropertyToID("_Sparse");
        private static readonly int PulseMeanId = Shader.PropertyToID("_PulseMean");
        private static readonly int RootOffsetId = Shader.PropertyToID("_RootOffset");
        private static readonly int ArchLineId = Shader.PropertyToID("_ArchLine");
        private static readonly int ArchFlameId = Shader.PropertyToID("_ArchFlame");
        private static readonly int ArchBendId = Shader.PropertyToID("_ArchBend");
        private static readonly int ArchFlowId = Shader.PropertyToID("_ArchFlow");

        internal static float Steps = 128f;
        internal static float StepLength = 0.02f;
        // Fire seen through a shut door's holes: fewer ray steps, with the
        // airflow and source uploads refreshed only four times a second.
        internal static float ClosedFire = 1f;
        private const float ClosedUpdateTime = 0.25f;
        private float closedUpdateAt;
        private float closedUpdated;
        private int sourceRefreshes;
        internal static float Glow = 1.1f;
        internal static float FlameDensity = 15f;
        // Dense firebox smoke must obscure the fire and walls. The old
        // low extinction and bright scattering showed a luminous haze,
        // especially against the white-hot bed.
        internal static float SmokeDensity = 5f;
        internal static float SmokeAlbedo = 0.45f;
        internal static float SmokeLight = 0.08f;
        internal static float SmokeClumps = 1f;
        internal static float ChokedSmoke = 2f;
        internal static float Sharp = 55f;
        // The flames' edges taken as their mean along each step of the march
        // (1), not at each step's point (0): point by point, an edge a few
        // millimetres thick in 2 cm steps drew as sand along every flame.
        internal static float EdgeMean = 1f;
        // Near the coal the smoke is the smoke of the coal under it (1), not
        // the box's evenly (0): thick over a fresh shovelful.
        internal static float SmokeLocal = 1f;
        // How much of the detail finer than a march step is left out of the
        // flames (each read of the curls and fine filaments at the whole mip
        // its scale needs): read finer than the steps along the ray, it is
        // caught by one pixel's march and missed by its neighbor's, a grain
        // through the flames' bright feet, the worse the more their light
        // is streaked by it. (It does nothing for the salt over the lumps,
        // which was the ray's last part-step: jobs 2009 and 2010.)
        internal static float DetailBlur = 1f;
        internal static float DitherAmount = 1f;
        // How far each quad's four starts are shifted at random, a share of
        // the quarter step they stand apart: what the quad's mean still
        // misses shows as grain from quad to quad at 1 and as faint combed
        // striations through the flames at 0 (job 2038: fresh at 1, 0.3,
        // and 0; working, flat out, burned down, and drifting at 0).
        internal static float DitherShift = 0.3f;
        // A patch of fresh coal's own smoke, rolling up from it before the
        // airflow has it (0 none).
        internal static float SmokePlume = 1f;
        // How many steps on the march's next read falls from one clear of
        // every flame, where only smoke is (1: every step, as it was).
        internal static float SmokeStride = 3f;
        // A drifting fire's lazy tongues: this much taller, a share of the
        // room over the coal, with no draft over a bed past a quarter of its
        // heat (the footage, shut off: loose tongues 0.2 to 0.5 of the box
        // high; a duller standing fire keeps its few short ones).
        internal static float LazyTop = 0.14f;
        internal static float LazyDraft = 0.3f;
        internal static float NoiseTile = 1f;

        // The flames' sheets: where the noise's crests rise over these at the
        // coal and at the flames' top they burn; the flame lying on the coal,
        // m; and how far the sheets stretch up.
        internal static float SheetLow = 0.62f;
        internal static float SheetHigh = 0.95f;

        internal static float MixSheets = 0.2f;
        internal static float MixCurls = 0.5f;
        internal static float MixLobes = 0.3f;

        // The flames' heat, for their color, against their coal's.
        internal static float HeatScale = 0.95f;

        internal static float FlameHeat = 0.9f;
        internal static float EdgeHeat = 0.35f;
        internal static float CoreDepth = 0.25f;
        internal static float Carpet = 0.02f;
        internal static float Stretch = 0.4f;
        internal static float StretchFine = 0.8f;
        // And the flames' light varying sheet to sheet by the curls (the
        // footage's flames crinkled: some sheets yellow, some dim orange,
        // their edges darker; ours only by heat and a fine streak).
        internal static float Crinkle = 1f;

        // How fast the flames rise, m/s, under a full draft and with none
        // (the footage: 1.8 and 0.05 box heights a second, by optical flow
        // on the slow motion; working, 1.2 m/s keeps as much of the picture
        // from frame to frame as the footage does, 2 m/s too little), and
        // how far they lean toward the tube plate, the bed's length per box
        // height, at a full draft; and each beat's surge of that speed. Much
        // faster and the noise moves a feature a frame, which reads as
        // flicker, not flame.
        internal static float RiseForced = 1.2f;
        internal static float RiseDrifting = 0.08f;
        internal static float LeanForced = 0.6f;
        internal static float BeatSurge = 0.5f;

        internal static float BeatGlow = 0.15f;
        internal static float BeatHeight = 0.3f;
        internal static float BeatHeat = 0.15f;
        internal static float BeatCover = 0.04f;
        private const float PulseMeanTime = 1f;

        internal static float LickHeight = 0.09f;
        internal static float LickScale = 2.1f;
        internal static float LickLow = 0.65f;
        internal static float LickStrength = 3f;
        internal static float LickGlow = 2.5f;
        internal static float LickHeat = 0.7f;
        internal static float LickStanding = 1f;
        // Narrow the edge fade and luminous folds without moving the rooted
        // body. Zero keeps the old soft ramp and wider fronts for comparisons.
        internal static float LickEdgeSharpness = 1f;
        internal static float LickDetail = 1f;
        internal static float LickCurl = 0.025f;
        internal static float LickRoughness = 0.18f;
        private const float LickStretch = 0.35f;

        // How fast the short flames flicker beyond their rise, in their
        // noise's tiles a second, under a full draft and with none.
        internal static float LickFlickerForced = 0.6f;
        internal static float LickFlickerDrifting = 0.25f;
        // The draft under which they are calm and over which a working
        // fire's, a share of a full one.
        internal static float LickCalmDraft = 0.03f;
        internal static float LickLiveDraft = 0.3f;

        // At the side walls the flames curl and sway more (the footage, by
        // optical flow: twice the swirl at the sides as in the middle): how far
        // in the walls reach, a share of the box's width, and how much more.
        internal static float SideWidth = 0.3f;
        internal static float SideSwirl = 1.5f;

        internal static float DetailScale = 3f;

        internal static float EddyRate = 1.5f;
        internal static float EddyGrowthTime = 0.3f;
        internal static float EddyRadius = 0.2f;
        internal static float EddyLow = 0.05f;
        internal static float EddyHigh = 0.3f;
        internal static float EddySpinForced = 6f;
        internal static float EddySpinDrifting = 2f;
        internal static float DetailErode = 0.9f;
        internal static float DetailStreak = 0.55f;

        internal static float Sway = 0.12f;
        internal static float SwayForced = 1.2f;
        internal static float SwayDrifting = 0.35f;
        internal static float PuffSizeForced = 0.25f;
        internal static float PuffSizeDrifting = 0.6f;
        internal static float PuffForced = 1.5f;
        internal static float PuffDrifting = 0.4f;
        internal static float Buoyancy = 0.7f;

        internal static float CarryForced = 0.3f;
        internal static float CarryDrifting = 1f;
        internal static float FlowPeriod = 0.7f;
        // Fine curls renew locally before the large sheets stretch them into
        // parallel filaments. They still travel up with the gas.
        internal static float DetailFlowCarry = 0.25f;
        internal static float TipWhipForced = 0.2f;
        internal static float TipWhipDrifting = 0f;
        internal static float TipWhipRateForced = 6f;
        internal static float TipWhipRateDrifting = 0.5f;
        internal static float TipWhipBeat = 3f;

        internal static float BaseGlowForced = 5f;
        internal static float BaseGlowDrifting = 3.5f;
        internal static float BaseCokeForced = 0.8f;
        internal static float BaseCokeDrifting = 0.5f;
        internal static float BaseHeight = 0.6f;

        internal static float WhiteHeart = 0.33f;
        internal static float WhiteDraftLow = 0.2f;
        internal static float WhiteDraftFull = 0.9f;

        // The glare gathers over the coal's hot spots (the footage: a few
        // small white-hot patches over the coal, not a band; Sol's review,
        // 2026-10-25): where the spots begin in their noise (spread evenly 0
        // to 1, so 0.6 gives them a fifth of the coal's weight), and the
        // feet's and short flames' glare over them, under a full draft and
        // with none; between them it dims to keep the same on average.
        internal static float SpotLow = 0.6f;
        internal static float SpotGlareForced = 2.4f;
        internal static float SpotGlareDrifting = 1.5f;
        internal static float TipHeat = 0.75f;

        // The flames' turbulence: how far the curls bend them, drifting and
        // under a full draft, and how fast the curls churn across the flow
        // there, m/s, so a working fire's shapes tear and re-form (the
        // footage: a working fire's small features last 0.04 to 0.15 s, a
        // drifting fire's 0.2 to 0.8 s).
        internal static float WarpDrifting = 0.35f;
        internal static float WarpForced = 0.6f;
        internal static float ChurnForced = 0.4f;

        // The coal's hot spots, the flames standing over them: how much they
        // shape the flames, and how fast they wander, in tiles a second,
        // under a full draft and with none (the footage's picture changes as
        // much over a second as from frame to frame, its flames standing
        // where they burn; the flow alone changed it a quarter as much).
        internal static float Roots = 0.35f;
        internal static float RootDriftForced = 0.1f;
        internal static float RootDriftDrifting = 0.03f;

        // The test bed's still: the flow held where it is (1), to tell the
        // flames' motion from anything else that changes a frame.
        internal static float FreezeFlow;

        internal static float CokeFlame = 0.05f;
        internal static float GasFlame = 0.9f;
        internal static float GasFull = 0.35f;
        internal static float GasExponent = 4f;
        internal static float GasStrength = 0.9f;
        internal static float GasFill = 0.08f;
        // Weak flames break up into a few tongues, off the threshold by this
        // at none, as the square of their weakness (a standing fire's lay
        // thin over its coal as a haze; the footage in a station: a few
        // bright tongues over a dark bed).
        internal static float Sparse = 0.25f;
        internal static float LazyLength = 1.1f;

        // The smoke from a zone's coal against its gas (a share of all-green
        // coal's) and a shovelful's smoke-out charge (kg). The airflow carries
        // it (FireboxAirflow.Smoke: the box filling standing, wisps streaming
        // to the tubes working); and the light the box's flames give it, a
        // share of their radiance on average (the footage working: pale
        // grey-tan wisps among the flames; standing, a murky dark orange-brown
        // lit from below).
        internal static float GasSmoke = 0.3f;
        internal static float SmokeOutPerKg = 0.5f;
        internal static float SmokeBoxLight = 0.6f;

        private ZonedCoalBed bed;
        private FireboxFlames flames;
        private RealisticFirebox firebox;
        private Transform box;
        private MeshRenderer boxRenderer;
        private Material material;
        private Texture2D zonesA;
        private Texture2D zonesB;
        private readonly Color[] pixelsA = new Color[GrateLayout.ZoneCount];
        private readonly Color[] pixelsB = new Color[GrateLayout.ZoneCount];
        private readonly float[] strength = new float[GrateLayout.ZoneCount];
        private readonly float[] top = new float[GrateLayout.ZoneCount];
        // Each zone's gas over the fire, a share of a forced zone's burning rate.
        private readonly float[] gas = new float[GrateLayout.ZoneCount];
        private Vector3 sheetOffset;
        private float pulse;

        // How fast a beat's surge comes up, s; and how long it takes to climb
        // from the coal to the flames' top (three steps of this).
        private const float BeatAttack = 0.035f;
        private const float BeatClimbStep = 0.04f;

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
        private FireboxAirflow airflow;
        private Texture2D flowA;
        private Texture2D flowAPrev;
        private Texture2D flowB;
        // The airflow's smoke under this is not drawn (the shader's own
        // limit for a step with nothing in it); and the smoke's top, a share
        // of the box's height.
        private const float SmokeDrawn = 0.004f;
        private float smokeCeiling = 1f;
        private Color[] flowPixelsB;

        // The airflow steps at a fixed rate, and the picture is drawn between
        // its last two steps: its time kept at any frame rate (a frame's step,
        // clamped to 50 ms, ran slow under load, its random pushes changed
        // with the frame rate, and at 144 fps it cost a step a frame). Behind
        // a hitch it catches up this many steps at most and lets the rest go.
        private const float FlowStep = 1f / 30f;
        private const int FlowCatchUp = 4;
        private float flowDue;
        // The zones' maps go to the shader at the airflow's rate too, not
        // every frame: the fire they draw changes as it burns, over seconds.
        private float zonesDueAt;
        // The test bed's tuning the fixed values were last set from.
        private int constantsVersion = -1;
        private Color[] flowBeforeA;
        private Color[] flowBeforeB;
        private Color[] flowNowA;
        private Color[] flowNowB;
        private Vector4 weightsBefore;
        private Vector4 weightsNow;
        private Vector4 flowWeights;
        private bool restartedA;
        private bool restartedB;
        private float[] columnBurn;
        private float[] columnHeat;
        private float[] columnSurface;
        private float[] columnSmoke;
        private Vector3 bedS;
        private Vector3 bedT;
        private float grateLevel;
        private float swayClock;
        private float puffClock;
        private float tipClock;
        private Vector3 boxSize;
        private float alongSign = 1f;

        /// <summary>How hard a zone's flames burn in the volume this frame, 0 to 1.</summary>
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

        public float ZoneStrength(GrateZone zone) => strength[(int)zone];

        /// <summary>How tall a zone's flames stand over its coal this frame, m.</summary>
        public float ZoneHeight(GrateZone zone) => top[(int)zone] * boxSize.y;

        /// <summary>The zones' flames and smoke as the volume draws them, for the test bed's log.</summary>
        internal string Describe()
        {
            var b = new System.Text.StringBuilder($"draft {flames.Draft:F2}, pulse {pulse:F2}; drawing {boxRenderer?.enabled}, viewed {Viewed}, quality {Main.Settings?.FireboxQuality}, half resolution {FireboxOffscreen.Drawing}, steps {material?.GetFloat(StepsId):F0}, spacing {material?.GetFloat(StepLengthId):F3} m; motion {lickOffset:F3}, dither {material?.GetFloat(DitherFrameId):F0}, source refreshes {sourceRefreshes}, clock {Time.unscaledTime:F3}; fire's light {FireColors.Light:F2}, exposure {FireColors.Exposed:F2}; gas burned over the fire {firebox.Model.OverFireBurnout:P0}, overwhelm {FireColors.Overwhelm:F2}; ceiling {material?.GetFloat(CeilingId):F2}, smoke's {smokeCeiling:F2}; ");
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

        /// <summary>Whether the volume draws this frame.</summary>
        public bool Drawing => boxRenderer != null && boxRenderer.enabled;

        /// <summary>The volume's renderer and material, for its half-resolution drawing (FireboxOffscreen).</summary>
        internal MeshRenderer VolumeRenderer => boxRenderer;
        internal Material VolumeMaterial => material;

        /// <summary>Whether a camera saw the volume last frame, through its open door.</summary>
        public bool Viewed { get; private set; }

        /// <summary>The game camera's distance from the box, m.</summary>
        public float Distance { get; private set; }

        // A door open less than this shows nothing of the fire; and from
        // farther than this, m, the volume is not drawn.
        private const float DoorShut = 0.01f;
        private const float ViewDistance = 15f;

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
            var eye = RealisticStackEffects.PlumeCapture.HeldAt ?? (camera != null ? camera.transform.position : (Vector3?)null);
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
                var toCar = car.transform.worldToLocalMatrix * box.localToWorldMatrix;
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

            // The airflow over the box's cross-section, and its maps for the shader.
            airflow = new FireboxAirflow(boxSize.x, boxSize.y);
            Texture2D FlowTexture(string name) => new Texture2D(airflow.Across, airflow.Up, TextureFormat.RGBAHalf, false, true)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            flowA = FlowTexture("RealisticFiring flow phases");
            flowAPrev = FlowTexture("RealisticFiring flow phases, a step before");
            flowB = FlowTexture("RealisticFiring flow bend");
            var cells = airflow.Across * airflow.Up;
            flowPixelsB = new Color[cells];
            flowBeforeA = new Color[cells];
            flowBeforeB = new Color[cells];
            flowNowA = new Color[cells];
            flowNowB = new Color[cells];
            ReadFlow();
            System.Array.Copy(flowNowA, flowBeforeA, flowNowA.Length);
            System.Array.Copy(flowNowB, flowBeforeB, flowNowB.Length);
            weightsBefore = weightsNow;
            columnBurn = new float[airflow.Across];
            columnHeat = new float[airflow.Across];
            columnSurface = new float[airflow.Across];
            columnSmoke = new float[airflow.Across];
            UploadFlow(false);
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
            var start = PerfMeter.Start();
            UpdateVolume();
            PerfMeter.Stop(PerfSection.FireboxVolume, start);
        }

        private void UpdateVolume()
        {
            var show = Main.Settings != null && Main.Settings.ShowZonedCoalBed && bed != null && bed.Ready
                       && firebox != null && firebox.Model.FireOn && !FireboxTestBed.HidingFlames;
            // Keep an inexpensive fire behind a shut door's perforations.
            // Its opaque parts stop the rays at the scene depth as usual.
            // (A capture holds the camera where it shoots only while it renders,
            // so its distance here is the player's: captures skip it.)
            var near = show && (Near() || FireboxCapture.KeepsVisible);
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
            if (FireboxTestBed.Enabled && FreezeFlow > 0.5f)
                dt = 0f;
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
                if (FireboxTestBed.Enabled && FreezeFlow > 0.5f)
                    dt = 0f;
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
            if (constantsVersion != FireboxTestBed.TuningVersion)
            {
                SetConstants();
                constantsVersion = FireboxTestBed.TuningVersion;
            }

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
            var choked = (1f - Mathf.SmoothStep(0f, 1f, draft / 0.15f)) * smoky * smoky;
            material.SetFloat(SmokeDensityId, SmokeDensity * (1f + ChokedSmoke * choked));
            material.SetFloat(WarpId, warp);
            material.SetFloat(SmokeClumpsId, SmokeClumps * (1f - 0.4f * choked));
            FireColors.Publish();
            material.SetFloat(DebugId, FireboxTestBed.Enabled ? FireboxTestBed.VolumeView : 0);
        }

        // The values that change only when the test bed tunes them: set once,
        // and again after each tuning, not every frame (audit V7, 2026-11-09).
        private void SetConstants()
        {
            // The brick arch, where the engine has one (BrickArch): where its
            // bricks are in the box, and how the flames go under and round them.
            if (Arch != null && BrickArch.ArchShown > 0.5f)
            {
                material.SetVector(ArchLineId, Arch.Line);
                material.SetVector(ArchFlameId, new Vector4(Mathf.Clamp(BrickArch.ArchLayer, 0.05f, 0.95f), BrickArch.ArchBack, BrickArch.ArchOver, Arch.LipAlongBed));
                material.SetVector(ArchBendId, new Vector4(Mathf.Max(1f, BrickArch.ArchBend), BrickArch.ArchDrag, BrickArch.ArchTurn, BrickArch.ArchRoll));
                material.SetVector(ArchFlowId, new Vector4(BrickArch.ArchBlur, FireboxTestBed.Enabled ? BrickArch.ArchAlone : 0f, Mathf.Clamp(BrickArch.ArchSqueeze, 0.05f, 1f), Mathf.Max(0f, BrickArch.ArchCarry)));
            }
            else
                material.SetVector(ArchFlameId, Vector4.zero);
            material.SetFloat(RootsId, Roots);
            material.SetFloat(BeatGlowId, BeatGlow);
            material.SetFloat(BeatHeightId, BeatHeight);
            material.SetFloat(BeatHeatId, BeatHeat);
            material.SetFloat(BeatCoverId, BeatCover);
            material.SetFloat(LickHeightId, LickHeight);
            material.SetFloat(LickDepthId, CoalLumps.UnderDrop);
            material.SetFloat(LickScaleId, LickScale);
            material.SetFloat(LickLowId, LickLow);
            material.SetFloat(LickStrengthId, LickStrength);
            material.SetFloat(LickGlowId, LickGlow);
            material.SetFloat(LickHeatId, LickHeat);
            material.SetFloat(LickStandingId, Mathf.Clamp01(LickStanding));
            material.SetFloat(LickEdgeSharpnessId, Mathf.Clamp01(LickEdgeSharpness));
            material.SetFloat(LickDetailId, Mathf.Clamp01(LickDetail));
            material.SetFloat(LickCurlId, Mathf.Max(0f, LickCurl));
            material.SetFloat(LickRoughnessId, Mathf.Max(0f, LickRoughness));
            material.SetFloat(DoorSlopeId, ZonedCoalBed.DoorSlope);
            material.SetFloat(SideWidthId, SideWidth);
            material.SetFloat(SideSwirlId, SideSwirl);
            material.SetFloat(DetailScaleId, DetailScale);
            material.SetFloat(DetailErodeId, DetailErode);
            material.SetFloat(DetailStreakId, DetailStreak);
            material.SetFloat(GasFillId, GasFill);
            material.SetFloat(StretchFineId, StretchFine);
            material.SetFloat(CrinkleId, Crinkle);
            material.SetFloat(SparseId, Sparse);
            material.SetFloat(SwayId, Sway);
            material.SetFloat(DetailFlowCarryId, Mathf.Clamp01(DetailFlowCarry));
            material.SetFloat(BuoyancyId, Buoyancy);
            material.SetFloat(BaseHeightId, BaseHeight);
            material.SetFloat(SpotLowId, Mathf.Clamp(SpotLow, 0f, 0.95f));
            material.SetFloat(TipHeatId, TipHeat);
            material.SetFloat(GlowId, Glow);
            material.SetFloat(FlameDensityId, FlameDensity);
            material.SetFloat(SmokeAlbedoId, SmokeAlbedo);
            material.SetFloat(SmokeLightId, SmokeLight);
            material.SetFloat(SharpId, Sharp);
            material.SetFloat(EdgeMeanId, Mathf.Clamp01(EdgeMean));
            material.SetFloat(SmokeLocalId, Mathf.Clamp01(SmokeLocal));
            material.SetFloat(DetailBlurId, Mathf.Clamp(DetailBlur, 0f, 1.5f));
            material.SetFloat(DitherAmountId, Mathf.Clamp01(DitherAmount));
            material.SetFloat(DitherShiftId, Mathf.Clamp01(DitherShift));
            material.SetFloat(SmokePlumeId, Mathf.Max(0f, SmokePlume));
            material.SetFloat(SmokeStrideId, Mathf.Clamp(SmokeStride, 1f, 6f));
            material.SetFloat(NoiseTileId, NoiseTile);
            material.SetFloat(SheetLowId, SheetLow);
            material.SetFloat(SheetHighId, SheetHigh);
            material.SetFloat(CarpetId, Carpet);
            material.SetFloat(StretchId, Stretch);
            material.SetFloat(MixSheetsId, MixSheets);
            material.SetFloat(MixCurlsId, MixCurls);
            material.SetFloat(MixLobesId, MixLobes);
            material.SetFloat(HeatScaleId, HeatScale);
            material.SetFloat(FlameHeatId, FlameHeat);
            material.SetFloat(EdgeHeatId, EdgeHeat);
            material.SetFloat(CoreDepthId, CoreDepth);
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

        // Kept within a few tiles, so the offsets never lose precision (the
        // noise tiles). The shader reads them at 0.5, 0.7, and 2 times, so
        // they wrap at 20 tiles, a whole number of tiles at each (at 16 the
        // billows, at 0.7, jumped 11.2 tiles and the flames popped).
        private const float WrapTiles = 20f;

        private static Vector3 Wrap(Vector3 offset) =>
            new Vector3(Mathf.Repeat(offset.x, WrapTiles), Mathf.Repeat(offset.y, WrapTiles), Mathf.Repeat(offset.z, WrapTiles));

        // A wood fire's flames at full strength against a coal zone's.
        private const float WoodStrength = 0.8f;

        // Each zone's flames and smoke from its coal: how hard and how tall its
        // flames burn, its smoke, its coal's surface; its heat and glow.
        private int zonesFrame = -1;

        // Also used by the lighting behind a shut door. Nine source samples
        // stay current without running the airflow or uploading any textures.
        internal void MeasureZones()
        {
            if (zonesFrame == Time.frameCount)
                return;
            zonesFrame = Time.frameCount;
            if (!firebox.Model.FireOn)
            {
                System.Array.Clear(strength, 0, strength.Length);
                System.Array.Clear(top, 0, top.Length);
                return;
            }
            var draft = flames.Draft;
            var source = firebox.Smoke;
            var length = Mathf.Lerp(LazyLength, 1f, draft);
            var forced = Mathf.Max(1e-5f, firebox.Model.ForcedZoneRate);
            for (var i = 0; i < GrateLayout.ZoneCount; i++)
            {
                var zone = (GrateZone)i;
                var heat = bed.ZoneHeat(zone);
                var burn = flames.ZoneFlame(zone);
                var green = bed.ZoneCover(zone);
                // The gas leaving the bed, burning over it, smothered as its coke's flame is.
                var share = firebox.Model.ZoneOverFireGas(zone) * firebox.Model.OverFireBurnout / forced;
                var gas = Mathf.Pow(Mathf.Clamp01(share / GasFull), GasExponent) * flames.ZoneFlameShown(zone);
                strength[i] = Mathf.Clamp01(burn * (0.35f + 0.65f * heat) + GasStrength * gas);
                this.gas[i] = share;
                var smoke = GasSmoke * source.GasRate(zone) / FireboxSmoke.SmokePerZone + SmokeOutPerKg * source.Charge(zone);

                var x = (GrateLayout.Column(zone) + 0.5f) / GrateLayout.Columns;
                var z = (GrateLayout.Row(zone) + 0.5f) / GrateLayout.Rows;
                var surface = box.InverseTransformPoint(bed.BedPoint(x, z, bed.SmoothHeight(x, z))).y + 0.5f;
                // The old height spent a whole box height above an already
                // raised bed, saturating at the crown even at a light firing.
                var lazy = LazyTop * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, LazyDraft, draft))) * Mathf.Clamp01((heat - 0.25f) * 4f);
                top[i] = strength[i] < 0.01f ? 0f
                    : Mathf.Clamp01(0.05f + CokeFlame * burn + GasFlame * gas * length + lazy) * Mathf.Max(0f, 1f - surface);
                // Its wood and rag fire, in the same flames (WoodFlame): from
                // the grate, over any coal laid on it, to the pile's flame top.
                var starter = firebox.Model.Starters[i];
                var wood = WoodFlame.Strength(starter);
                var flameHeat = heat;
                if (wood > 0f && boxSize.y > 0f)
                {
                    var grate = box.InverseTransformPoint(bed.BedPoint(x, z, 0f)).y + 0.5f;
                    var woodTop = Mathf.Clamp01((WoodFlame.Top(starter, wood) - (surface - grate) * boxSize.y) / boxSize.y);
                    strength[i] = Mathf.Max(strength[i], WoodStrength * wood);
                    top[i] = Mathf.Max(top[i], woodTop);
                    flameHeat = Mathf.Max(flameHeat, WoodFlame.ColorHeat * wood);
                }
                var glow = FireboxLighting.ZoneGlow(heat, green).maxColorComponent * FireboxLighting.BedLight;
                // The texture's rows run along the bed (the door first), its columns across.
                var texel = GrateLayout.Row(zone) * GrateLayout.Columns + GrateLayout.Column(zone);
                pixelsA[texel] = new Color(strength[i], top[i], Mathf.Min(smoke, 1.5f), surface);
                // (Its green coal's share as the bed's shaders show it: none
                // dark in a zone driven flat out, CoalSurface.cginc.)
                pixelsB[texel] = new Color(flameHeat, glow, gas, green * (1f - 0.9f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.93f, 1f, heat))));
            }
        }

        private void Shape(float draft)
        {
            MeasureZones();
            zonesA.SetPixels(pixelsA);
            zonesA.Apply(false);
            // The light filling the box, on average, for the smoke in it: the
            // flames' and the bed's glow (in a closed box the bed lights all
            // of it, not only what is over it).
            var filled = Color.black;
            for (var i = 0; i < GrateLayout.ZoneCount; i++)
            {
                var zone = (GrateZone)i;
                // (With a hard-driven fire's white hearts: the box's light whiter,
                // and its smoke paler, as the footage's wisps among working flames.)
                var flameHeat = 0.25f + 0.75f * pixelsB[GrateLayout.Row(zone) * GrateLayout.Columns + GrateLayout.Column(zone)].r + 0.5f * WhiteHeart * Mathf.InverseLerp(WhiteDraftLow, WhiteDraftFull, draft);
                filled += FireColors.At(flameHeat) * (strength[i] * top[i] * Glow)
                          + FireboxLighting.ZoneGlow(bed.ZoneHeat(zone), bed.ZoneCover(zone)) * (FireboxLighting.BedGlow * FireboxLighting.BedLight);
            }
            filled *= SmokeBoxLight / GrateLayout.ZoneCount;
            var smokeMean = 0f;
            for (var i = 0; i < GrateLayout.ZoneCount; i++)
                smokeMean += pixelsA[i].b / GrateLayout.ZoneCount;
            material.SetFloat(SmokeMeanId, smokeMean);
            material.SetVector(BoxLightId, new Vector4(filled.r, filled.g, filled.b, 0f));
            // The grate's height, where the coal slopes down to at the door.
            grateLevel = box.InverseTransformPoint(bed.BedPoint(0.5f, 0.5f, 0f)).y + 0.5f;
            material.SetFloat(GrateLevelId, grateLevel);
            // The bed's relief, added in the shader to the zones' smooth surface.
            var bedRise = box.InverseTransformPoint(bed.BedPoint(0.5f, 0.5f, 1f)).y + 0.5f - grateLevel;
            material.SetFloat(BedRiseId, bedRise);
            // How high in the box anything is drawn, for the march to stop
            // under: the coal's highest surface with its swells and heaps;
            // over it the tallest tongue, as the shader draws it (to 1.4 of
            // its zone's height by its hot spots and lobes, swung by a beat
            // and a puff, no taller than half the room, its ragged tip half
            // as far again), or the short flames in the gaps; and the smoke's
            // top. A drifting fire's march took every step up to the crown
            // for flames a hand high (33 ms a frame from inside the box).
            var surfaceMost = 0f;
            var surfaceLeast = 1f;
            var topMost = 0f;
            for (var i = 0; i < GrateLayout.ZoneCount; i++)
            {
                surfaceMost = Mathf.Max(surfaceMost, pixelsA[i].a);
                surfaceLeast = Mathf.Min(surfaceLeast, pixelsA[i].a);
                topMost = Mathf.Max(topMost, pixelsA[i].g);
            }
            var coalMost = grateLevel + Mathf.Max(0f, surfaceMost - grateLevel) * (1f + bed.ReliefSwell) + bed.ReliefHeap * bedRise;
            var tongue = Mathf.Min(1.4f * (1f + Mathf.Max(0f, BeatHeight)) * 1.55f * topMost, 0.5f * Mathf.Max(0f, 1f - Mathf.Min(surfaceLeast, grateLevel)));
            var licks = boxSize.y > 0f ? 1.8f * LickHeight / boxSize.y : 1f;
            var flameCeiling = coalMost + Mathf.Max(1.5f * tongue, licks) + 0.03f;
            material.SetFloat(CeilingId, Mathf.Clamp01(Mathf.Max(flameCeiling, smokeCeiling)));
            if (bed.ReliefTexture != null)
                material.SetTexture(BedReliefId, bed.ReliefTexture);
            zonesB.SetPixels(pixelsB);
            zonesB.Apply(false);
        }

        // Carries the flames on the airflow: steps it at its fixed rate, sends
        // the shader its maps after each step, and has it draw them between
        // the last two (_FlowLerp), so nothing goes up between the steps.
        private void Carry(float dt, float speed, float forcing)
        {
            flowDue += dt;
            var steps = 0;
            for (; flowDue >= FlowStep && steps < FlowCatchUp; steps++)
            {
                flowDue -= FlowStep;
                StepFlow(speed, forcing);
            }
            flowDue = Mathf.Min(flowDue, FlowStep);
            if (steps > 0)
                UploadFlow(steps == 1);
            var t = flowDue / FlowStep;
            // A phase that restarted in the last step is shown as it is now:
            // between its old and new coordinates is nowhere, and its weight
            // is nil there anyway.
            material.SetVector(FlowLerpId, new Vector4(restartedA ? 1f : t, restartedB ? 1f : t, t, 0f));
            flowWeights = Vector4.Lerp(weightsBefore, weightsNow, t);
        }

        // The airflow's maps after a step: where the gas was when each phase
        // began, now (_FlowA) and a step before (_FlowAPrev); where across it
        // was a moment ago and its smoke, now and a step before, together
        // (_FlowB). After one step the map that was now is the one before,
        // so the two swap and only the new one goes up.
        private void UploadFlow(bool oneStep)
        {
            (flowA, flowAPrev) = (flowAPrev, flowA);
            flowA.SetPixels(flowNowA);
            flowA.Apply(false);
            if (!oneStep)
            {
                flowAPrev.SetPixels(flowBeforeA);
                flowAPrev.Apply(false);
            }
            // The highest row holding any smoke to speak of, now or a step
            // before, and so the smoke's top (the shader reads between rows).
            var smokeRow = -1;
            for (var k = 0; k < flowPixelsB.Length; k++)
            {
                flowPixelsB[k] = new Color(flowNowB[k].r, flowNowB[k].g, flowBeforeB[k].r, flowBeforeB[k].g);
                if (flowNowB[k].g > SmokeDrawn || flowBeforeB[k].g > SmokeDrawn)
                    smokeRow = Mathf.Max(smokeRow, k / airflow.Across);
            }
            smokeCeiling = smokeRow < 0 ? 0f : Mathf.Clamp01((smokeRow + 1.5f) / airflow.Up);
            flowB.SetPixels(flowPixelsB);
            flowB.Apply(false);
            material.SetTexture(FlowAId, flowA);
            material.SetTexture(FlowAPrevId, flowAPrev);
            material.SetTexture(FlowBId, flowB);
        }

        // Steps the airflow from each column across the box: how hard its coal
        // burns (its zones' flames, across the bed's rows), how hot its gas
        // leaves (the more its fresh coal's gas burns, the hotter), and its
        // coal's surface.
        private void StepFlow(float speed, float forcing)
        {
            for (var i = 0; i < airflow.Across; i++)
            {
                var x = (i + 0.5f) / airflow.Across;
                var burn = 0f;
                var hot = 0f;
                var surface = 0f;
                var smoke = 0f;
                var inBed = 0;
                for (var row = 0; row < 3; row++)
                {
                    var z = (row + 0.5f) / 3f;
                    var bs = bedS.x * x + bedS.y * z + bedS.z;
                    var bt = bedT.x * x + bedT.y * z + bedT.z;
                    if (bs < 0f || bs > 1f || bt < 0f || bt > 1f)
                        continue;
                    var a = ZoneAt(pixelsA, bs, bt);
                    var b = ZoneAt(pixelsB, bs, bt);
                    burn += a.r;
                    hot += a.r * b.r * (0.6f + 0.4f * b.b);
                    surface += a.a;
                    smoke += a.b;
                    inBed++;
                }
                columnBurn[i] = inBed > 0 ? burn / inBed : 0f;
                columnHeat[i] = inBed > 0 ? hot / inBed : 0f;
                columnSurface[i] = (inBed > 0 ? surface / inBed : grateLevel) * boxSize.y;
                columnSmoke[i] = inBed > 0 ? smoke / inBed : 0f;
            }
            airflow.EddySpin = Mathf.Lerp(EddySpinDrifting, EddySpinForced, forcing);
            airflow.EddyRate = EddyRate;
            airflow.EddyGrowthTime = Mathf.Max(0f, EddyGrowthTime);
            airflow.EddyRadius = EddyRadius;
            airflow.EddyLow = EddyLow;
            airflow.EddyHigh = EddyHigh;
            airflow.Period = FlowPeriod;
            System.Array.Copy(flowNowA, flowBeforeA, flowNowA.Length);
            System.Array.Copy(flowNowB, flowBeforeB, flowNowB.Length);
            weightsBefore = weightsNow;
            var restartsA = airflow.RestartsA;
            var restartsB = airflow.RestartsB;
            airflow.Step(FlowStep, columnBurn, columnHeat, columnSurface, speed, columnSmoke);
            restartedA = airflow.RestartsA != restartsA;
            restartedB = airflow.RestartsB != restartsB;
            ReadFlow();
        }

        // The airflow's maps as they are now: where the gas was at each
        // phase's start, m; where across it was a moment ago, m.
        private void ReadFlow()
        {
            for (var j = 0; j < airflow.Up; j++)
            {
                for (var i = 0; i < airflow.Across; i++)
                {
                    var k = j * airflow.Across + i;
                    flowNowA[k] = new Color(airflow.PhaseAAcross(i, j), airflow.PhaseAUp(i, j), airflow.PhaseBAcross(i, j), airflow.PhaseBUp(i, j));
                    flowNowB[k] = new Color(airflow.CameFrom(i, j), airflow.Smoke(i, j), 0f, 0f);
                }
            }
            weightsNow = new Vector4(airflow.WeightA, airflow.WeightB, 0f, 0f);
        }

        // A zone texture's value at the bed's (across, along), bilinear between
        // the zones' middles as the shader reads it.
        private static Color ZoneAt(Color[] pixels, float across, float along)
        {
            var fx = Mathf.Clamp(across * GrateLayout.Columns - 0.5f, 0f, GrateLayout.Columns - 1f);
            var fy = Mathf.Clamp(along * GrateLayout.Rows - 0.5f, 0f, GrateLayout.Rows - 1f);
            var x0 = Mathf.Min((int)fx, GrateLayout.Columns - 2);
            var y0 = Mathf.Min((int)fy, GrateLayout.Rows - 2);
            var tx = fx - x0;
            var ty = fy - y0;
            Color P(int c, int r) => pixels[r * GrateLayout.Columns + c];
            return Color.Lerp(Color.Lerp(P(x0, y0), P(x0 + 1, y0), tx), Color.Lerp(P(x0, y0 + 1), P(x0 + 1, y0 + 1), tx), ty);
        }

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
            if (flowA != null)
                Destroy(flowA);
            if (flowAPrev != null)
                Destroy(flowAPrev);
            if (flowB != null)
                Destroy(flowB);
        }
    }
}
