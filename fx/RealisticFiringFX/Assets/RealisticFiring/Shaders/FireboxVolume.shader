


















Shader "RealisticFiring/FireboxVolume"
{
    Properties
    {
        _Noise ("Noise (R coal ridges, G flame detail, B lobes, A warp)", 3D) = "" {}
        _ZonesA ("Zones: flame strength, flame height, smoke, coal surface", 2D) = "black" {}
        _ZonesB ("Zones: heat, glow, gas flame", 2D) = "black" {}
        _BedRelief ("The bed's relief: swells, heaps, the heaps' freshness", 2D) = "black" {}
        _BedRise ("The box's height a full pile of coal stands, a share of it", Float) = 0
        _BoxLight ("The flames' light filling the box, for the smoke", Vector) = (0, 0, 0, 0)
        _Steps ("Most steps along a ray", Float) = 128
        _StepLength ("Step length, m", Float) = 0.02
        _Glow ("A thick flame's radiance", Float) = 1.4
        _FlameDensity ("Flame density, per m", Float) = 8
        _SmokeDensity ("Smoke density, per m", Float) = 3
        _SmokeAlbedo ("Smoke's share of light given back", Float) = 0.6
        _SmokeLight ("Smoke's light from the fire under it", Float) = 1
        _Sharp ("Flame edge sharpness", Float) = 4
        _Warp ("Curl warp", Float) = 0.35
        _NoiseTile ("Noise tile, m", Float) = 0.8
        _SheetLow ("Sheet threshold at the coal", Float) = 0.62
        _SheetHigh ("Sheet threshold at the flames' top", Float) = 0.97
        _Carpet ("Flame carpet on the coal, m", Float) = 0.06
        _Stretch ("Vertical stretch of the sheets", Float) = 0.75
        _StretchFine ("Vertical stretch of the curls and fine filaments", Float) = 0.8
        _Crinkle ("Flames' light varying sheet to sheet by the curls", Range(0, 1.5)) = 0
        _VolumeDebug ("Test bed view: 0 all, 1 flames alone, 2 smoke alone", Float) = 0
        _MixSheets ("Shape: thin sheets", Float) = 0.3
        _MixCurls ("Shape: small curls", Float) = 0.25
        _MixLobes ("Shape: big lobes", Float) = 0.45
        _HeatScale ("Flames' heat against the coal's", Float) = 0.85
        _FlameHeat ("Flames' heat over the coolest burning coal, share of the hottest's", Range(0, 1)) = 0.9
        _EdgeHeat ("Flames' heat at their edges against their cores'", Range(0, 1)) = 0.35
        _CoreDepth ("How deep inside a flame its core is, in its shape's noise", Float) = 0.5
        _SmokeClumps ("Smoke in billows with clear gaps (0 even)", Range(0, 1)) = 1
        _Roots ("Flames standing over the coal's hot spots", Float) = 0.35
        _BeatGlow ("A beat's surge of the flames' light", Float) = 0.4
        _BeatHeight ("A beat's surge of the flames' height", Float) = 0.5
        _BeatHeat ("A beat's surge of the flames' heat, for their color", Float) = 0.3
        _BeatCover ("A beat's spread of the flames, off their threshold", Float) = 0.06
        _LickHeight ("Short flames in the gaps: height, m", Float) = 0.12
        _LickDepth ("Short flames in the gaps: from this far under the coal's surface, m", Float) = 0.05
        _LickScale ("Short flames in the gaps: noise tiles per m across", Float) = 1.5
        _LickLow ("Short flames in the gaps: threshold at their foot", Float) = 0.72
        _LickStrength ("Short flames in the gaps: their density against the flames'", Float) = 3
        _LickGlow ("Short flames in the gaps: their light against the flames'", Float) = 6
        _LickHeat ("Short flames in the gaps: their foot's heat over the coolest coal, share of the hottest's", Range(0, 1)) = 0.7
        _LickMotion ("Short flames in the gaps: how much they sway and pump, 0 calm to 1 a full draft's", Range(0, 1)) = 1
        _LickStanding ("Short flames: anchored bodies (0 the old scrolling bodies)", Range(0, 1)) = 1
        _LickEdgeSharpness ("Short flames: crisp edges and luminous folds (0 the old soft ramp)", Range(0, 1)) = 1
        _LickDetail ("Short flames: shaped detail (0 the test bed's old smooth cells)", Range(0, 1)) = 1
        _LickCurl ("Short flames: curling at their tips, m", Float) = 0.04
        _LickRoughness ("Short flames: roughness of their upper edges", Float) = 0.18
        _DoorSlope ("The bed's slope down to the door, share of its length", Float) = 0.07
        _GrateLevel ("The grate's height in the box, 0 to 1", Float) = 0
        _Sway ("Tongues' sway at their tips, m", Float) = 0.12
        _Puff ("Each patch's flames growing and shrinking on their own", Range(0, 1)) = 0.6
        _Buoyancy ("How the flames speed up as they rise: 1 not, less faster", Float) = 0.7
        _BaseGlow ("A flame's light at its foot against its top's, over a full gas flame", Float) = 5
        _BaseCoke ("A flame's bright foot over burned-down coke, share of a full gas flame's", Range(0, 1)) = 0.5
        _BaseHeight ("How far up a flame its bright foot reaches, share of its height", Float) = 0.6
        _WhiteHeart ("A hard-driven fire's hearts: heat over the ramp's cores", Float) = 0
        _SpotLow ("Hot spots: where they begin, in their noise (spread evenly 0 to 1)", Range(0, 1)) = 0.6
        _SpotDim ("Hot spots: the feet's and short flames' glare between them", Float) = 0.5
        _SpotGlare ("Hot spots: the feet's and short flames' glare over them", Float) = 3
        _TipHeat ("A flame's heat at its top against its foot's", Float) = 0.5
        _GasFill ("Gas flames: how much fuller, off the threshold", Float) = 0.08
        _Sparse ("Weak flames: how much sparser, off the threshold, at none", Float) = 0.25
        _FlowA ("Airflow: where the gas was when each phase began, m (A across, up; B across, up)", 2D) = "black" {}
        _FlowB ("Airflow: where across the gas was a moment ago, m", 2D) = "black" {}
        _DetailScale ("Fine filaments: their noise tiles per tile of the flames'", Float) = 3
        _DetailErode ("Fine filaments: how much they fray the flames' edges", Float) = 0.8
        _DetailStreak ("Fine filaments: how much they streak the flames' light", Float) = 0.6
        _SideWidth ("Side walls: their reach, share of the box's width", Float) = 0.3
        _SideSwirl ("Side walls: more curl at them", Float) = 1.5
        _FlowCarry ("How much of the gas's travel the flames' detail shows", Range(0, 1)) = 1
        _DetailFlowCarry ("Fine detail's share of the flow distortion", Range(0, 1)) = 0.25
        _TipWhip ("Flames' tips whipping about, m", Float) = 0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }

        Pass
        {
            // The box's far side, so a camera inside it still draws; the depth
            // test is the march's own, against the depth buffer.
            Cull Front
            ZWrite Off
            ZTest Always
            // Premultiplied: the volume's light plus what is behind times
            // the share that gets through.
            Blend One SrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // 5.0 for the fine derivatives that share a quad's light (QuadBlend).
            #pragma target 5.0
            // Single-pass stereo on the mod's own keyword: Unity strips its own
            // from a bundle built without VR, and the mod turns this on for stereo cameras.
            #pragma multi_compile __ RF_SINGLE_PASS_STEREO
            #if defined(RF_SINGLE_PASS_STEREO)
            #define UNITY_SINGLE_PASS_STEREO 1
            #endif
            // HLSL, not CG: CG includes these before the define above.
            #include "HLSLSupport.cginc"
            #include "UnityCG.cginc"
            #include "Assets/RealisticFiring/Shaders/FireColor.cginc"

            sampler3D _Noise;
            sampler2D _ZonesA;
            sampler2D _ZonesB;
            sampler2D _BedRelief;
            float _BedRise;
            float3 _BoxLight;
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

            float _Steps;
            float _StepLength;
            float _Glow;
            float _FlameDensity;
            float _SmokeDensity;
            float _SmokeAlbedo;
            float _SmokeLight;
            float _Sharp;
            float _Warp;
            float _NoiseTile;
            // The beat's surge now and 40, 80, and 120 ms ago: it climbs from
            // the coal, the flames higher up taking it later.
            float4 _PulseHistory;
            float _SheetLow;
            float _SheetHigh;
            float _Carpet;
            float _Stretch;
            float _VolumeDebug;
            float _MixSheets;
            float _MixCurls;
            float _MixLobes;
            float _HeatScale;
            float _FlameHeat;
            float _EdgeHeat;
            float _CoreDepth;
            float _SmokeClumps;
            float _Roots;
            float _BeatGlow;
            float _BeatHeight;
            float _BeatHeat;
            float _BeatCover;
            float _LickHeight;
            float _LickDepth;
            float _LickScale;
            float _LickLow;
            float _LickStrength;
            float _LickGlow;
            float _LickHeat;
            float _LickMotion;
            float _LickStanding;
            float _LickEdgeSharpness;
            float _LickDetail;
            float _LickCurl;
            float _LickRoughness;
            float _DoorSlope;
            float _GrateLevel;
            float _Sway;
            float _Puff;
            float _Buoyancy;
            float _BaseGlow;
            float _BaseCoke;
            float _BaseHeight;
            float _WhiteHeart;
            float _SpotLow;
            float _SpotDim;
            float _SpotGlare;
            float _TipHeat;
            float _GasFill;
            float _StretchFine;
            float _Crinkle;
            float _Sparse;
            sampler2D _FlowA;
            sampler2D _FlowB;
            // The airflow's two phases' weights, adding to 1.
            float4 _FlowWeights;
            float _DetailScale;
            float _DetailErode;
            float _DetailStreak;
            // How far the sway and the puffing have changed, in tiles of their noise.
            float _SwayClock;
            float _PuffClock;
            // How far the short flames in the gaps have risen, in their noise's tiles.
            float _LickOffset;
            float _SideWidth;
            float _SideSwirl;
            float _FlowCarry;
            float _DetailFlowCarry;
            // The tips' whipping: how far it has changed, in its noise's tiles.
            float _TipWhip;
            float _TipClock;
            // The surge's mean over the last second: the beats swing the flames
            // about their own level rather than raising it.
            float _PulseMean;
            // How far the hot spots have wandered, in tiles.
            float _RootOffset;
            // The box's size, m (across, up, along the smoke's space).
            float3 _BoxSize;
            // The bed's frame from the box's: across (s) and along (t, the door
            // 0 to the tube plate 1) as rows over (box x, box z, 1).
            float3 _BedS;
            float3 _BedT;
            // The firehole in the car's frame, evaluated in the centered cube:
            // across and up 0..1, and signed distance ahead of the backhead.
            // All zero when a cab's opening is not known.
            float4 _HoleAcross;
            float4 _HoleUp;
            float4 _HolePlane;
            // How far the flames lean toward the tube plate, in the bed's length
            // per box height; and the noise's offsets, in tiles: the sheets and
            // lobes (xyz), and the curls and warp.
            float _Lean;
            float3 _SheetOffset;
            float3 _CurlOffset;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 world : TEXCOORD0;
                float4 screen : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.screen = ComputeScreenPos(o.pos);
                return o;
            }

            // A flame's color by its temperature, 0 to 1: the fire's ramp from
            // its dull red up (FireColor.cginc).
            half3 FlameColor(half t)
            {
                return FireColor(0.25 + 0.75 * clamp(t, 0.0, 1.34));
            }













            float _DitherFrame;

            uint Pcg(uint v)
            {
                uint state = v * 747796405u + 2891336453u;
                uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
                return (word >> 22u) ^ word;
            }

            // The pixel's place in its quad: 0 left or top, 1 right or bottom.
            float2 QuadSide(float2 pixel)
            {
                return floor(pixel) - 2.0 * floor(pixel * 0.5);
            }

            float Dither(float2 pixel)
            {
                uint2 quad = (uint2)floor(pixel * 0.5);
                float shift = Pcg(quad.x + Pcg(quad.y)) * (1.0 / 4294967296.0);
                // Quarters 0 and 2 along the top row, 3 and 1 along the bottom:
                // each pixel's neighbors across and up or down are a quarter
                // and a half step from it.
                float2 odd = QuadSide(pixel);
                float quarter = 2.0 * odd.x + 3.0 * odd.y - 4.0 * odd.x * odd.y;
                return frac(shift + 0.25 * quarter + _DitherFrame * 0.61803399);
            }

            // A pixel's value blended with its quad neighbors' across and up or
            // down (weights a half and a quarter each), unless a neighbor's ray
            // through the box is of another length: an edge in front, the door
            // or the coal, which keeps its own.
            float2 QuadWeights(float2 pixel, float span)
            {
                float2 side = 1.0 - 2.0 * QuadSide(pixel);
                float spanX = span + side.x * ddx_fine(span);
                float spanY = span + side.y * ddy_fine(span);
                float near = 0.05 + 0.1 * span;
                return float2(abs(spanX - span) < near ? 0.5 : 0.0, abs(spanY - span) < near ? 0.5 : 0.0);
            }

            half3 QuadBlend(half3 value, float2 pixel, float2 weights)
            {
                float2 side = 1.0 - 2.0 * QuadSide(pixel);
                half3 across = value + side.x * ddx_fine(value);
                half3 upDown = value + side.y * ddy_fine(value);
                return (value + weights.x * across + weights.y * upDown) / (1.0 + weights.x + weights.y);
            }

            // A small flame's density and density-weighted hot core. Its foot
            // keeps its old spot; its upper part bends and breaks into irregular
            // tips, the fine detail traveling up with the flame.
            half2 BedFlame(float3 q, float up, float2 bend, float scale, float height)
            {
                float reach = saturate(up);
                q.xz += bend * scale * reach * reach;
                // More shape changes along a tongue's height, anchored at its
                // buried foot: the old stretch made one smooth taper.
                q.y += max(up, 0.0) * height * scale * 0.45 * _LickDetail;
                // The body stands at its bed location. Scrolling this coarse
                // field extinguished whole tongues as its cells passed; the
                // height pumping, curl, and fine tip detail still animate.
                half shape = tex3Dlod(_Noise, float4(q.x, q.y - _LickOffset * (1.0 - _LickStanding), q.z, 0)).g;
                half threshold = lerp(_LickLow, 1.0, reach);
                half fine = 0.5;
                if (_LickDetail > 0.0)
                {
                    fine = tex3Dlod(_Noise, float4(q * float3(2.3, 4.0, 2.3)
                                          - float3(0, _LickOffset * 1.5, 0) + float3(0.37, 0.11, 0.71), 0)).g;
                    shape += (fine - 0.5) * _LickRoughness * smoothstep(0.1, 1.0, up) * _LickDetail;
                    // Continue tapering above the nominal top, so a split tip
                    // fades out before the enclosing branch's height limit.
                    threshold = lerp(threshold, _LickLow + (1.0 - _LickLow) * max(up, 0.0), _LickDetail);
                }
                half inside = shape - threshold;
                half body = saturate(inside * _Sharp * 0.4);
                // Tighten the broad density fade around its existing midpoint.
                // Keeping that contour avoids simply swelling bright blobs.
                body = lerp(body, smoothstep(0.25, 0.75, body), _LickEdgeSharpness);
                half core = saturate(inside * _Sharp * 0.6);
                if (_LickDetail > 0.0)
                {
                    // A luminous, wrinkled front above the thick root. Filling
                    // every cell all the way through made smooth candle blobs;
                    // these fronts split and join around darker interiors.
                    half width = lerp(0.065 + 0.045 * fine, 0.04 + 0.025 * fine, _LickEdgeSharpness);
                    half front = saturate(1.0 - smoothstep(width * 0.7, width * 1.7, inside));
                    // Bring the narrower luminous folds farther down the body;
                    // a broad filled interior made the short flames look soft.
                    half layered = lerp(smoothstep(0.15, 0.85, up), smoothstep(0.1, 0.65, up), _LickEdgeSharpness) * _LickDetail;
                    body *= lerp(1.0, front, layered);
                    core *= lerp(1.0, sqrt(front), layered);
                }
                return half2(body, body * core);
            }

            half4 frag(v2f i) : SV_Target
            {
                float3 eye = _WorldSpaceCameraPos;
                float3 dir = normalize(i.world - eye);
                // The ray in the box's own frame, the unit cube about its middle;
                // t is the world distance along it.
                float3 origin = mul(unity_WorldToObject, float4(eye, 1)).xyz;
                float3 step3 = mul((float3x3)unity_WorldToObject, dir);
                float3 inv = 1.0 / step3;
                float3 near = (-0.5 - origin) * inv;
                float3 far = (0.5 - origin) * inv;
                float3 lo = min(near, far);
                float3 hi = max(near, far);
                float enter = max(max(lo.x, lo.y), max(lo.z, 0.0));
                float leave = min(min(hi.x, hi.y), hi.z);
                // What stands nearer: the walls, the coal, the door, the cab.
                float scene = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screen)));
                float3 forward = -UNITY_MATRIX_V[2].xyz;
                leave = min(leave, scene / max(dot(dir, forward), 1e-4));
                // From the cab, admit only rays through the firehole and only
                // the part beyond its plane. A camera inside the box still sees
                // the whole fire.
                float behind = dot(_HolePlane, float4(origin, 1));
                if (behind < -1e-4)
                {
                    float heading = dot(_HolePlane.xyz, step3);
                    float atHole = -behind / max(heading, 1e-6);
                    float4 crossing = float4(origin + step3 * atHole, 1);
                    float2 hole = float2(dot(_HoleAcross, crossing), dot(_HoleUp, crossing));
                    enter = max(enter, atHole);
                    if (heading <= 0.0 || any(hole < 0.0) || any(hole > 1.0))
                        leave = enter;
                }
                // None of the box in front of the scene: nothing to march, though
                // the pixel still runs on to its quad's blend.
                float span = max(leave - enter, 0.0);

                // A step of about _StepLength, at most _Steps of them: a short
                // ray through a corner takes few.
                float steps = span > 0.0 ? clamp(ceil(span / max(_StepLength, 0.005)), 4.0, max(4.0, _Steps)) : 0.0;
                float dt = span / max(steps, 1.0);
                float t = enter + dt * Dither(i.pos.xy);
                half3 light = 0;
                // What gets through, by color: soot dims green and blue more than
                // red, so flames seen through smoke turn brown, not the olive a
                // grey dimming of yellow gives.
                half3 through = 1;
                float tile = 1.0 / max(_NoiseTile, 0.05);

                for (int k = 0; k < 128; k++)
                {
                    if (k >= steps || through.r < 0.01)
                        break;
                    float3 u = origin + step3 * t + 0.5;
                    t += dt;

                    // The zones under this point, the flames leaning toward the
                    // tube plate with height.
                    // The flames' bodies ride the airflow: their zones read where
                    // across the gas here came from a moment ago, so they bend
                    // and roll with it (FireboxAirflow).
                    half4 flowB = tex2Dlod(_FlowB, float4(u.x, u.y, 0, 0));
                    float carried = flowB.x / _BoxSize.x;
                    float2 bed = float2(dot(_BedS, float3(carried, u.z, 1)), dot(_BedT, float3(carried, u.z, 1)));
                    float2 leaned = bed - float2(0, _Lean * u.y);
                    half4 zoneA = tex2Dlod(_ZonesA, float4(leaned, 0, 0));
                    half4 zoneB = tex2Dlod(_ZonesB, float4(bed, 0, 0));
                    // The coal's surface here: the zone's, sloping down to the
                    // grate at the door as the bed does.
                    // With the bed's swells and heaps (ZonedCoalBed.ReliefTexture),
                    // so the flames stand on the coal as drawn.
                    half4 relief = tex2Dlod(_BedRelief, float4(saturate(bed), 0, 0));
                    float coal = zoneA.a + (zoneA.a - _GrateLevel) * max(relief.r, -0.8) + relief.g * _BedRise;
                    float surface = lerp(_GrateLevel, coal, smoothstep(0.0, 1.0, bed.y / max(_DoorSlope, 1e-3)));
                    float above = u.y - surface;
                    // The gaps between the lumps reach this far under the coal's
                    // surface (the bed draws its deeper coal there: CoalLumps).
                    float gapFloor = _LickDepth / _BoxSize.y;
                    if (above < -gapFloor)
                        continue;







                    half puff = tex3Dlod(_Noise, float4(leaned.x * 1.7 + 0.31, _PuffClock, leaned.y * 3.4 + 0.53, 0)).b;
                    // (The short flames pump with the puffing and sway only as hard
                    // as the draft drives them: drifting, they stand calm.)
                    // The bed's burning spots, read only near it: at a low draft the
                    // short flames burn only there (the footage, real time: tongues
                    // relighting at the same places for seconds), the strongest
                    // the tallest, a few clusters rather than an even field.
                    // (Its fine band the spots, its coarse one, about 30 cm, the
                    // clusters they gather in: one even field of them stood like
                    // candles.)
                    half4 spotNoise = 0.0;
                    if ((above + gapFloor) * _BoxSize.y < 3.0 * _LickHeight)
                        spotNoise = tex3Dlod(_Noise, float4(u.x * _BoxSize.x * _LickScale * 0.7 + 0.37, 0.61, u.z * _BoxSize.z * _LickScale * 0.7 + 0.83, 0));
                    half spotsAt = spotNoise.g * smoothstep(0.4, 0.65, spotNoise.a);
                    half spotTall = lerp(0.6 + 1.2 * saturate((spotsAt - 0.55) / 0.25), 1.0, _LickMotion);
                    float lickTall = _LickHeight * (0.5 + lerp(0.5, puff, 0.5 + 0.5 * _LickMotion)) * spotTall;
                    float lickUp = (above + gapFloor) * _BoxSize.y / max(lickTall, 0.01);
                    // Each patch's sideways push, changing where it is, not carried
                    // up with the flames: the tongues, short and tall, sway from
                    // their feet.
                    half4 sway = tex3Dlod(_Noise, float4(leaned.x * 1.3 + 0.71, _SwayClock, leaned.y * 2.6 + 0.19, 0)) - 0.5;
                    half lick = 0;
                    half lickCore = 0;
                    // Only over the bed.
                    if (lickUp < 1.5 && all(bed == saturate(bed)))
                    {










                        float lickScale = _LickScale * lerp(0.6, 1.0, _LickMotion);
                        float3 half3Step = step3 * (dt * 0.5);
                        float3 q = u * _BoxSize * lickScale * float3(1, 0.35, 1);
                        q.xz += sway.ab * 2.0 * _Sway * lickScale * saturate(lickUp) * 0.5 * (0.35 + 0.65 * _LickMotion);
                        float3 q2 = q + half3Step * _BoxSize * lickScale * float3(1, 0.35, 1);
                        float lickUp2 = lickUp + half3Step.y * _BoxSize.y / max(lickTall, 0.01);
                        float2 bend = 0;
                        if (_LickDetail > 0.0)
                        {
                            half4 curl = tex3Dlod(_Noise, float4(q * 0.6 - float3(0, _LickOffset * 0.25, 0), 0)) - 0.5;
                            bend = curl.ga * 2.0 * _LickCurl * lerp(0.5, 1.0, _LickMotion) * _LickDetail;
                        }
                        half2 licks = BedFlame(q, lickUp, bend, lickScale, lickTall);
                        half2 licks2 = BedFlame(q2, lickUp2, bend, lickScale, lickTall);
                        // Two samples retain coverage along the ray as the
                        // short flames' density edges become crisper.
                        half both = licks.x + licks2.x;
                        lickCore = (licks.y + licks2.y) / max(both, 1e-4);
                        // As dense as the coal is hot, not as hard as it is driven: a
                        // standing fire's hot gaps still flame (the footage in a
                        // station: bright tongues over a dark bed).
                        lick = 0.5 * both * saturate(1.6 * zoneB.r) * lerp(_LickHeat, 1.0, zoneB.r) * _LickStrength;
                        // At a low draft they burn only at fixed spots in the bed,
                        // relighting there (the footage, real time: tongues at the
                        // same places for seconds); under a hard draft, all over.
                        lick *= lerp(smoothstep(0.58, 0.72, spotsAt), 1.0, _LickMotion);
                    }
                    // The flames proper stand on the coal's surface.
                    half onCoal = step(0.0, above);
                    if (onCoal < 0.5 && lick <= 0.0)
                        continue;
                    above = max(above, 0.0);
                    // The surge here: the coal's now, older higher up.
                    float climb = saturate(above / max(zoneA.g, 0.02)) * 3.0;
                    half surge = climb < 1.0 ? lerp(_PulseHistory.x, _PulseHistory.y, climb)
                               : climb < 2.0 ? lerp(_PulseHistory.y, _PulseHistory.z, climb - 1.0)
                               : lerp(_PulseHistory.z, _PulseHistory.w, climb - 2.0);
                    half swing = surge - _PulseMean;
                    float top = max(zoneA.g * (1.0 + _BeatHeight * swing), 0.02);
                    top *= lerp(1.0, 0.45 + 1.1 * puff, _Puff);




                    half roots = tex3Dlod(_Noise, float4(leaned.x * 1.2, _RootOffset, leaned.y * 2.4, 0)).a;
                    // Over the flames' highest reach with no smoke there is nothing to draw.
                    if (above > 1.5 * top * (0.9 + 0.5 * roots) && flowB.g < 0.005)
                        continue;









                    float metres = above * _BoxSize.y;
                    float3 p = float3(u.x * _BoxSize.x, pow(metres + 0.1, _Buoyancy) - pow(0.1, _Buoyancy), u.z * _BoxSize.z) * tile;
                    half4 warp = tex3Dlod(_Noise, float4(p * 0.5 - _CurlOffset * 0.5, 0)) - 0.5;
                    // The big lobes a working fire rolls in: the warp's own lobes.
                    half lobes = warp.b + 0.5;
                    // Each flame's own height: taller over the hot spots, ragged
                    // with the lobes, so an evenly burning fire has no flat top
                    // (one height for every zone drew a lid on the flames).
                    // The ragged tips can reach 1.5 times this local height.
                    // Even under a beat or a puff, leave the upper quarter of
                    // the room above the coal clear of flame. Cap each tongue's
                    // height before tapering it, rather than slicing a flat lid.
                    float tongueHeight = min(top * (0.6 + 0.5 * roots + 0.3 * lobes), 0.5 * max(1.0 - surface, 0.0));
                    float rise = above / max(tongueHeight, 1e-4);
                    // At the side walls the flames curl and sway the more (the
                    // footage: twice the swirl at the sides as in the middle).
                    half wall = saturate(1.0 - min(u.x, 1.0 - u.x) / max(_SideWidth, 0.01));
                    float3 bent = p + warp.agb * _Warp * (0.25 + 0.75 * saturate(rise * 1.2)) * (1.0 + _SideSwirl * wall);
                    // Each tongue sways from its foot, the more the higher.
                    bent.xz += sway.ab * 2.0 * _Sway * tile * pow(saturate(rise), 1.5) * (1.0 + _SideSwirl * wall);






                    if (_TipWhip > 0.0)
                    {
                        half4 whip = tex3Dlod(_Noise, float4(leaned.x * 3.5 + 0.47, _TipClock, leaned.y * 7.0 + 0.11, 0)) - 0.5;
                        bent += whip.aba * float3(2.0, 1.0, 2.0) * _TipWhip * tile * saturate(rise);
                    }
                    float3 moved = bent - p;










                    half4 flowA = tex2Dlod(_FlowA, float4(u.x, u.y, 0, 0));
                    float2 here = u.xy * _BoxSize.xy;
                    flowA = here.xyxy + _FlowCarry * (flowA - here.xyxy);
                    float3 along = float3(0, 0, 1);
                    float3 tallA = (float3(flowA.x * tile, flowA.y * tile, p.z) + moved) * float3(1, _Stretch, 1);
                    float3 tallB = (float3(flowA.z * tile, flowA.w * tile, p.z) + moved) * float3(1, _Stretch, 1);




                    float3 crisp = float3(1, _StretchFine / max(_Stretch, 0.05), 1);
                    // Keep the fine curls from being drawn into parallel lines
                    // by the large sheets' shear. Fresh local detail travels up
                    // with the gas while taking only part of its distortion.
                    float3 localFine = (float3(here * tile, p.z) + moved) * float3(1, _StretchFine, 1);
                    localFine.y -= _CurlOffset.y * _StretchFine;
                    float3 crispA = lerp(localFine, tallA * crisp, _DetailFlowCarry);
                    float3 crispB = lerp(localFine, tallB * crisp, _DetailFlowCarry);
                    float3 sheetShift = _SheetOffset * along;
                    float3 curlShift = _CurlOffset * float3(1, 0, 1);
                    // The sheets and the edge detail use the broadband field too.
                    // The ridged R channel made parallel crests that followed the
                    // flame's curves, especially on a weak fire. Offset the scales
                    // from one another so their details do not line up.
                    float3 sheetDetail = float3(0.37, 0.19, 0.53);
                    float3 fineDetail = float3(0.11, 0.47, 0.71);
                    // Locally renewed phases need independent detail too, so
                    // mixing them does not amplify one shared field's contrast.
                    float3 phaseDetail = (1.0 - _DetailFlowCarry) * float3(0.37, 0.61, 0.23);
                    half4 phaseA = half4(tex3Dlod(_Noise, float4(tallA - sheetShift + sheetDetail, 0)).g,
                                         tex3Dlod(_Noise, float4(crispA * 2.0 - curlShift, 0)).g,
                                         tex3Dlod(_Noise, float4(tallA - sheetShift * 0.7, 0)).b,
                                         tex3Dlod(_Noise, float4(crispA * _DetailScale - curlShift * 2.0 + fineDetail, 0)).g);
                    half4 phaseB = half4(tex3Dlod(_Noise, float4(tallB - sheetShift + sheetDetail, 0)).g,
                                         tex3Dlod(_Noise, float4(crispB * 2.0 - curlShift + phaseDetail, 0)).g,
                                         tex3Dlod(_Noise, float4(tallB - sheetShift * 0.7, 0)).b,
                                         tex3Dlod(_Noise, float4(crispB * _DetailScale - curlShift * 2.0 + fineDetail + phaseDetail, 0)).g);
                    // Mixed keeping their contrast, so the flames do not thin as
                    // one phase fades into the other.
                    half4 mixed = 0.5 + (_FlowWeights.x * (phaseA - 0.5) + _FlowWeights.y * (phaseB - 0.5))
                                      / max(sqrt(_FlowWeights.x * _FlowWeights.x + _FlowWeights.y * _FlowWeights.y), 0.5);
                    half sheets = mixed.x;
                    half curls = mixed.y;
                    half billows = mixed.z;



                    half fine = mixed.w;
                    // Tongues and lobes from the big billows, their edges thinned
                    // into sheets and rippled by the curls, dark between.
                    half shape = _MixSheets * sheets + _MixCurls * curls + _MixLobes * billows;
                    shape += _Roots * (roots - 0.5);

                    // Thin sheets low, breaking into tongues higher, tapering out
                    // over its top; and a carpet of flame on the coal itself.
                    // Each beat spreads the flames as it lifts them: they grow, and
                    // the light changes with their size and heat, not all at once.
                    // Weak flames break up into a few tongues rather than lie thin over
                    // the coal (a standing fire's drew as a glowing haze).
                    half weak = 1.0 - zoneA.r;
                    half threshold = lerp(_SheetLow, _SheetHigh, pow(saturate(rise), 0.8)) + 0.5 * saturate(rise - 1.0) - _BeatCover * swing - _GasFill * zoneB.b + _Sparse * weak * weak;
                    // (Thinner at a low draft: over a standing fire, seen low across
                    // the bed, its curls drew as rippled contour lines.)
                    half carpet = saturate(1.0 - above * _BoxSize.y / max(_Carpet, 0.005)) * (0.5 + 0.5 * curls) * (0.2 + 0.8 * _LickMotion);
                    // A crisp edge where the shape crosses the threshold; hotter the
                    // deeper inside a flame, so its body burns yellow and its edges
                    // orange-red.
                    half inside = shape - threshold;
                    half edge = 1.0 - saturate(inside * 5.0);
                    half fray = lerp(1.0, saturate(fine * 2.0 - 0.2), edge * _DetailErode);
                    half body = (saturate(inside * _Sharp) * (0.7 + 0.6 * lobes) * saturate((1.5 - rise) * 2.0) * fray + carpet) * zoneA.r * onCoal;
                    half flame = body + lick;
                    half core = saturate(inside / max(_CoreDepth, 0.02) + carpet);









                    half heat = _HeatScale * lerp(_FlameHeat, 1.0, zoneB.r) * lerp(1.0, _TipHeat, pow(saturate(rise), 0.8)) * lerp(_EdgeHeat, 1.0, core) * (1.0 + _BeatHeat * swing);














                    half spot = lerp(_SpotDim, _SpotGlare, smoothstep(_SpotLow, 1.0, roots));
                    half reach = 1.0 - smoothstep(0.0, max(_BaseHeight, 0.01), saturate(rise));
                    half reach2 = reach * reach;
                    half foot = 1.0 + (_BaseGlow - 1.0) * lerp(_BaseCoke, 1.0, pow(zoneB.b, 1.5)) * reach2 * lerp(1.0, spot, reach2 * reach2);









                    heat += _WhiteHeart * core * reach2;
                    half lickHeat = saturate(lerp(_LickHeat, 1.0, zoneB.r) * lerp(1.3, 0.45, saturate(lickUp)))
                                  * lerp(1.0, lerp(0.6, 1.0, lickCore), _LickDetail)
                                  + _WhiteHeart * (1.0 - saturate(lickUp)) * lerp(1.0, lickCore, _LickDetail);
                    heat = lerp(heat, lickHeat, lick / max(flame, 1e-4));
                    // _Glow is an opaque flame's radiance: a thick flame shows it,
                    // a thin one less, as much as its density takes up.
                    half lickRadiance = lerp(1.0, 0.5 + 0.9 * lickCore, _LickDetail);
                    half3 flameLight = FlameColor(heat) * flame * _Glow * lerp(1.0, 0.55 + 0.9 * fine, _DetailStreak) * lerp(1.0, 0.3 + 1.4 * curls, _Crinkle) * max(1.0 + _BeatGlow * swing, 0.2) * lerp(foot, _LickGlow * lerp(1.5, 0.4, saturate(lickUp)) * spot * lickRadiance, lick / max(flame, 1e-4));
                    half3 glow = flameLight * _FlameDensity;

                    // Smoke over the zone, thinner toward the crown, lit from the
                    // coal below and by the flames in it.
                    // In billows with clear gaps between, as a fresh round's smoke
                    // rolls up (evenly, a dense round's smoke drew as one black
                    // ball).
                    half clumps = 2.0 * saturate(billows * 1.6 - 0.35) * (0.6 + 0.8 * lobes);
                    // From a little over the coal: it gathers above (laid on the coal
                    // and lit from it, a standing fire's smoke hid its bed in an orange
                    // haze; the footage's is grey, over a clear bed).
                    // As much as the airflow carries here (FireboxAirflow.Smoke):
                    // filling the box over a standing fire, streaming to the tubes
                    // in wisps over a working one.
                    // (From 10 cm over the coal: thicker there and lit hard from
                    // under it, the smoke lay on a standing bed as glowing ripples.)
                    half smoke = flowB.g * onCoal * saturate((above * _BoxSize.y - 0.1) / 0.3) * lerp(0.5 + lobes, clumps, _SmokeClumps);
                    // Lit orange from the coal below (the bed's glow, its color
                    // a little cooler than its flames') and by the flames in it.
                    half3 fireBelow = FlameColor(zoneB.r * 0.75) * zoneB.g * exp(-3.0 * above);
                    // Lit from the coal below, by the flames in it, and by the flames'
                    // light filling the box: pale among a working fire's flames, dark
                    // orange-brown over a standing one.
                    half3 lit = smoke * _SmokeAlbedo * (_SmokeLight * fireBelow + 0.3 * flameLight + _BoxLight);

                    // The test bed's views: the flames or the smoke alone.
                    if (_VolumeDebug > 1.5)
                    {
                        flame = 0;
                        glow = 0;
                    }
                    else if (_VolumeDebug > 0.5)
                    {
                        smoke = 0;
                        lit = 0;
                    }
                    float3 sigma = flame * _FlameDensity + smoke * _SmokeDensity * float3(0.75, 1.0, 1.3);
                    half3 kept = exp(-sigma * dt);
                    // The light from this step, as much as gets out of it (energy
                    // conserving: an emissive, absorbing slab of its depth).
                    half3 source = glow + lit;
                    half3 out3 = sigma.g > 1e-4 ? source * (1.0 - kept) / sigma : source * dt;
                    light += through * out3;
                    through *= kept;
                }
                float2 weights = QuadWeights(i.pos.xy, span);
                light = QuadBlend(light, i.pos.xy, weights);
                through = QuadBlend(through, i.pos.xy, weights);
                // As the eye sees it: the flames' and smoke's light alone, the
                // walls and coal behind already so; those dimmed by the green's
                // share through, the blend's one.
                return half4(FireExposed(light), through.g);
            }
            ENDHLSL
        }
    }
}
