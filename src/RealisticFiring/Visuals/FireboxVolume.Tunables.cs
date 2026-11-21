namespace RealisticFiring.Visuals
{
    internal sealed partial class FireboxVolume
    {
        internal static float Steps = 128f;
        internal static float StepLength = 0.02f;
        // Fire seen through a shut door's holes: fewer ray steps, with the
        // airflow and source uploads refreshed only four times a second.
        internal static float ClosedFire = 1f;
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

        internal static float JetSpread = 0.2f;
        internal static float JetPush = 0.5f;
        internal static float JetCore = 10f;
        internal static float JetBendBy = 0.6f;
        internal static float JetClear = 0.85f;
        // The box's smoke cleared by the share of the gas the jets burn
        // (FireModel.JetBurnout), and the flames over the bed shorter by it:
        // the gas burns the faster for their air, not the more.
        internal static float JetSmokeClear = 0.7f;
        internal static float JetShorten = 0.5f;
        // The share of the gas the jets burn that the flames over the bed are drawn by.
        internal static float JetFlameShare = 0.4f;
        // How long the smoke already in the box takes to clear when the jets
        // open, s, as the box's smoke follows the share they burn (the
        // flames shorten at once: the gas burns faster as their air reaches
        // it; lagged too, the flare as they opened stood tall and washed the
        // view out: the council, round 3): at once,
        // the box went from brown to clear between two frames (the council,
        // round 1; Steins, in Benton and Engdahl p.44: Ringelmann 4 to 0 in
        // about 9 s standing).
        internal static float JetClearTime = 3f;
    }
}
