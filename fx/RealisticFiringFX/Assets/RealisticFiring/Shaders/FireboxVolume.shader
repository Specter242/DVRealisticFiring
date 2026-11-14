


















Shader "RealisticFiring/FireboxVolume"
{
    Properties
    {
        _Noise ("Noise (R coal ridges, G flame detail, B lobes, A warp)", 3D) = "" {}
        _ZonesA ("Zones: flame strength, flame height, smoke, coal surface", 2D) = "black" {}
        _ZonesB ("Zones: heat, glow, gas flame, green coal's share", 2D) = "black" {}
        _BedRelief ("The bed's relief: swells, heaps, the heaps' freshness", 2D) = "black" {}
        _BedRise ("The box's height a full pile of coal stands, a share of it", Float) = 0
        _BoxLight ("The flames' light filling the box, for the smoke", Vector) = (0, 0, 0, 0)
        _Steps ("Most steps along a ray", Float) = 128
        _StepLength ("Step length, m", Float) = 0.02
        _Glow ("A thick flame's radiance", Float) = 1.4
        _FlameDensity ("Flame density, per m", Float) = 8
        _SmokeDensity ("Smoke density, per m", Float) = 5
        _SmokeAlbedo ("Smoke's share of light given back", Float) = 0.45
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
        _LickHeight ("Short flames in the gaps: height, m", Float) = 0.09
        _LickDepth ("Short flames in the gaps: from this far under the coal's surface, m", Float) = 0.05
        _LickScale ("Short flames in the gaps: noise tiles per m across", Float) = 2.1
        _LickLow ("Short flames in the gaps: threshold at their foot", Float) = 0.65
        _LickStrength ("Short flames in the gaps: their density against the flames'", Float) = 3
        _LickGlow ("Short flames in the gaps: their light against the flames'", Float) = 6
        _LickHeat ("Short flames in the gaps: their foot's heat over the coolest coal, share of the hottest's", Range(0, 1)) = 0.7
        _LickMotion ("Short flames in the gaps: how much they sway and pump, 0 calm to 1 a full draft's", Range(0, 1)) = 1
        _LickStanding ("Short flames: anchored bodies (0 the old scrolling bodies)", Range(0, 1)) = 1
        _LickEdgeSharpness ("Short flames: crisp edges and luminous folds (0 the old soft ramp)", Range(0, 1)) = 1
        _LickDetail ("Short flames: shaped detail (0 the test bed's old smooth cells)", Range(0, 1)) = 1
        _LickCurl ("Short flames: curling at their tips, m", Float) = 0.025
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
        _FlowAPrev ("Airflow: the same a step before", 2D) = "black" {}
        _FlowB ("Airflow: where across the gas was a moment ago, m, and its smoke; the same a step before (zw)", 2D) = "black" {}
        _DetailScale ("Fine filaments: their noise tiles per tile of the flames'", Float) = 3
        _DetailErode ("Fine filaments: how much they fray the flames' edges", Float) = 0.8
        _DetailStreak ("Fine filaments: how much they streak the flames' light", Float) = 0.6
        _SideWidth ("Side walls: their reach, share of the box's width", Float) = 0.3
        _SideSwirl ("Side walls: more curl at them", Float) = 1.5
        _FlowCarry ("How much of the gas's travel the flames' detail shows", Range(0, 1)) = 1
        _DetailFlowCarry ("Fine detail's share of the flow distortion", Range(0, 1)) = 0.25
        _TipWhip ("Flames' tips whipping about, m", Float) = 0
        _EdgeMean ("Flame edges taken as their mean along each step (0 at each step's point)", Range(0, 1)) = 1
        _SmokeLocal ("Smoke near the coal from the coal under it (0 the box's smoke evenly)", Range(0, 1)) = 1
        _SmokeMean ("The zones' smoke on average", Float) = 0
        _Ceiling ("How high in the box anything is drawn, 0 to 1", Float) = 1
        _DetailBlur ("Detail finer than a march step left out (0 none, 1 all of it)", Range(0, 1.5)) = 1
        _DitherAmount ("How far each pixel's reads are dithered along its ray, share of a step", Range(0, 1)) = 1
        _DitherShift ("How far each quad's starts are shifted at random, share of a quarter step", Range(0, 1)) = 1
        _SmokePlume ("A fresh patch's own smoke rolling up from it (0 none)", Float) = 1
        _SmokeStride ("Steps between reads over the flames' reach, in smoke alone", Range(1, 6)) = 3
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
            // the share that gets through. In alpha, the share alone, times
            // what the target held: drawn into its half-size target, cleared
            // to 1, that leaves the share for the laying over (Composite).
            Blend One SrcAlpha, Zero SrcAlpha

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
            #include "Assets/RealisticFiring/Shaders/FireboxView.cginc"

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
            sampler2D _FlowAPrev;
            sampler2D _FlowB;
            // The airflow's two phases' weights, adding to 1.
            float4 _FlowWeights;
            // How far the frame is from the airflow's last step to its next:
            // phase A's maps, phase B's (each 1 the step it restarted), and the
            // bend's and smoke's. The mod sends the maps only as it steps.
            float4 _FlowLerp;
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



































            float4 _ArchLine;
            float4 _ArchFlame;
            float4 _ArchBend;
            float4 _ArchFlow;
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
            // (Toward white with less blue than the coal's white: a thin
            // sliver of a white flame drew a pale pinkish grey, like another
            // material in the fire; thick, or seen flat out, it is white
            // all the same.)
            half3 FlameColor(half t)
            {
                half heat = 0.25 + 0.75 * clamp(t, 0.0, 1.34);
                half white = smoothstep(_FireWhiteHeat, 1.0, heat);
                return FireColor(heat) * half3(1.0, 1.0 - 0.1 * white, 1.0 - 0.55 * white);
            }













            float _DitherFrame;
            float _DitherShift;
            float _SmokePlume;
            float _SmokeStride;

            // The volume at half resolution (the mod's FireboxOffscreen): its
            // light and the share of the scene coming through, marched into
            // a target half the screen's size each way (_FireLowPass 1 while
            // it is drawn there), then laid over the scene by this same draw
            // among the transparent things (_FireLowReady 1 for the camera
            // that drew it, _UseLow 1 on the volume it drew).
            sampler2D _FireLow;
            float4 _FireLowSize;
            float _FireLowPass;
            float _FireLowReady;
            float _UseLow;

            // Depths this far apart, as a share of the pixel's own and a
            // little over, are an edge: a lump's against the gap behind it.
            #define LOW_EDGE 0.012

            // A pixel from the nine half-size texels about it, as far as
            // their rays ended at its own depth (each was stopped at the
            // scene's depth at its own middle); from the one nearest it in
            // depth where none did. Weighed a half to the nearest row and
            // column and the other half between the two beside it, by where
            // the pixel lies: whichever way, the four starts a quad's texels
            // march from (Dither) weigh a quarter each, so the laying over
            // is the quad's blend too (QuadBlend), without its blocks: the
            // texels blended in their quads first, the flames' edges laid
            // over in steps four pixels across.
            half4 Composite(float2 pixel, float here)
            {
                float2 at = pixel * 0.5;
                float2 middle = floor(at) + 0.5;
                float2 f = at - middle;
                // The surface's own slope here, a pixel across and down: a
                // texel's depth is held against the pixel's carried along it
                // to the texel's place. (Against the pixel's own depth, a
                // sheet seen along its length, each texel a little further
                // off than the last, passed for an edge at every other
                // pixel: the flames before the side sheets drew in ribs and
                // blocks.) No steeper than a hundredth of the depth a
                // pixel: past that it is an edge, a lump's against the gap.
                float2 slope = clamp(float2(ddx(here), ddy(here)), -0.01 * here, 0.01 * here);
                float3 wx = float3(0.25 - 0.5 * f.x, 0.5, 0.25 + 0.5 * f.x);
                float3 wy = float3(0.25 - 0.5 * f.y, 0.5, 0.25 + 0.5 * f.y);
                half4 sum = 0;
                float total = 0;
                float nearest = 1e9;
                half4 best = half4(0, 0, 0, 1);
                [unroll]
                for (int j = 0; j < 3; j++)
                {
                    [unroll]
                    for (int k = 0; k < 3; k++)
                    {
                        float2 place = middle + float2(k - 1, j - 1);
                        float2 uv = place * _FireLowSize.zw;
                        half4 texel = tex2Dlod(_FireLow, float4(uv, 0, 0));
                        // (Its depth as its march read it: at the middle of
                        // the full-size pixel under its low left quarter. At
                        // its own middle, a corner between four pixels, the
                        // march and this read fell on different ones along a
                        // row here and there, and the row drew as dashes.)
                        float2 depthAt = (place - 0.25) * _FireLowSize.zw;
                        float carried = here + dot(slope, place * 2.0 - 0.5 - pixel);
                        float apart = abs(LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, float4(depthAt, 0, 0))) - carried);
                        if (apart < nearest)
                        {
                            nearest = apart;
                            best = texel;
                        }
                        float weight = apart < LOW_EDGE * here + 0.004 ? wx[k] * wy[j] : 0.0;
                        sum += texel * weight;
                        total += weight;
                    }
                }
                return total > 1e-4 ? sum / total : best;
            }

            float _EdgeMean;
            float _SmokeLocal;
            float _SmokeMean;
            float _Ceiling;
            float _DetailBlur;
            float _DitherAmount;
            // A march step's length in the noise's texels at one tile a metre,
            // as a mip level: detail read finer than the steps along the ray
            // is caught by one pixel's march and missed by the next (salt
            // over the dark lumps and along the flames' edges), so each read
            // of the noise takes the mip its own scale needs (DetailLod).
            static float StepLod = 0.0;

            float DetailLod(float tilesPerMetre)
            {
                // At full resolution, the whole mip whose texels are no
                // smaller than a step (between two, each read took both, and
                // cost 4 ms a frame from inside the box; at the one below,
                // detail half a step across still got through as a dotted
                // fringe along the flames' edges). At half resolution,
                // between two mips, half a level up: its steps run from 1 cm
                // to 2 across the picture, and at whole levels the flames
                // changed character along a line where they crossed from
                // one to the next.
                float level = max(StepLod + log2(max(tilesPerMetre, 1e-3)), 0.0);
                return _FireLowPass > 0.5 ? (level + 0.5) * _DetailBlur : ceil(level * _DetailBlur - 0.01);
            }

            // A flame's edge, saturate(x * sharp), on average along a step
            // through which x runs evenly from a to b: the share of the step
            // inside the flame, where its point alone is all in or all out.
            // Point by point, an edge a few millimetres thick marched in
            // 2 cm steps came out as sand along every flame (each pixel's
            // start is dithered), which the quads' blend only coarsened.
            float EdgeArea(float x, float sharp)
            {
                float ramp = clamp(x, 0.0, 1.0 / sharp);
                return 0.5 * sharp * ramp * ramp + max(x - 1.0 / sharp, 0.0);
            }

            half EdgeMean(float a, float b, float sharp)
            {
                // (Where it hardly runs at all, against how large a and b
                // are, their middle: the difference of two large areas lost
                // its digits, and a line of pixels along the horizon, where
                // a ray keeps its height over the coal, drew wrong.)
                float run = b - a;
                return abs(run) > 1e-3 * (1.0 + abs(a) + abs(b))
                    ? saturate((EdgeArea(b, sharp) - EdgeArea(a, sharp)) / run) : saturate(0.5 * (a + b) * sharp);
            }

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
                // (Over a quarter of a step and no more: the quad's four
                // starts stand a quarter apart, so further is the same
                // again; and over less of it, _DitherShift, the less the
                // quads differ from one another.)
                float shift = Pcg(quad.x + Pcg(quad.y)) * (0.25 / 4294967296.0) * _DitherShift;
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

            // Where both neighbors' rays are of its length, the mean of the
            // whole quad, its fourth pixel's value from the derivative of a
            // derivative: the four start a quarter step apart, and only all
            // four together sample every step evenly. (Taking in the two
            // neighbors alone left one pixel in four standing out wherever
            // one of the four starts caught what the others missed: a grid
            // of dots, two pixels apart, over the faces deep in the gaps.)
            half3 QuadBlend(half3 value, float2 pixel, float2 weights)
            {
                float2 side = 1.0 - 2.0 * QuadSide(pixel);
                half3 across = side.x * ddx_fine(value);
                half3 upDown = side.y * ddy_fine(value);
                half3 corner = side.x * ddx_fine(upDown);
                if (weights.x > 0.0 && weights.y > 0.0)
                    return value + 0.5 * (across + upDown) + 0.25 * corner;
                return value + (weights.x * across + weights.y * upDown) / (1.0 + weights.x + weights.y);
            }

            // A small flame's shape at a point: how far inside its edge the
            // point lies (its noise over its threshold), its fine detail, and
            // how far up the flame it is. Its foot keeps its old spot; its
            // upper part bends and breaks into irregular tips, the fine
            // detail traveling up with the flame.
            half3 BedFlameField(float3 q, float up, float2 bend, float scale, float height)
            {
                float reach = saturate(up);
                q.xz += bend * scale * reach * reach;
                // More shape changes along a tongue's height, anchored at its
                // buried foot: the old stretch made one smooth taper.
                q.y += max(up, 0.0) * height * scale * 0.45 * _LickDetail;
                // The body stands at its bed location. Scrolling this coarse
                // field extinguished whole tongues as its cells passed; the
                // height pumping, curl, and fine tip detail still animate.
                // (No finer than the half steps it is read at: its noise
                // runs to features a centimetre across, which showed or did
                // not by the pixel's dither, white salt in the brightest
                // gaps and dark dots over the lumps behind.)
                half shape = tex3Dlod(_Noise, float4(q.x, q.y - _LickOffset * (1.0 - _LickStanding), q.z, DetailLod(0.5 * scale))).g;
                half threshold = lerp(_LickLow, 1.0, reach);
                half fine = 0.5;
                if (_LickDetail > 0.0)
                {
                    fine = tex3Dlod(_Noise, float4(q * float3(2.3, 4.0, 2.3)
                                          - float3(0, _LickOffset * 1.5, 0) + float3(0.37, 0.11, 0.71), DetailLod(1.15 * scale))).g;
                    shape += (fine - 0.5) * _LickRoughness * smoothstep(0.1, 1.0, up) * _LickDetail;
                    // Continue tapering above the nominal top, so a split tip
                    // fades out before the enclosing branch's height limit.
                    threshold = lerp(threshold, _LickLow + (1.0 - _LickLow) * max(up, 0.0), _LickDetail);
                }
                return half3(shape - threshold, fine, up);
            }

            // Its density and density-weighted hot core there, from its shape
            // (BedFlameField): inside, fine, up.
            half2 BedFlameLight(half3 field)
            {
                half inside = field.x;
                half fine = field.y;
                half up = field.z;
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

            // The same on average along the ray between two points, their
            // shape taken to run evenly from one to the other: the fronts
            // are a few millimetres thick, and read only at the points
            // themselves they came out as sand (the shape between costs no
            // more reads of the noise).
            half2 BedFlameSpan(half3 from, half3 to)
            {
                return (BedFlameLight(lerp(from, to, 1.0 / 6.0)) + BedFlameLight(lerp(from, to, 0.5))
                    + BedFlameLight(lerp(from, to, 5.0 / 6.0))) / 3.0;
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
                // (Into the half-size target, the scene's depth at the
                // middle of the full-size pixel under the texel's low left
                // quarter, where the laying over reads it again.)
                float scene = _FireLowPass > 0.5
                    ? LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, float4((i.pos.xy - 0.25) * _FireLowSize.zw, 0, 0)))
                    : LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screen)));
                if (_FireLowReady > 0.5 && _UseLow > 0.5 && _FireLowPass < 0.5)
                    return Composite(i.pos.xy, scene);
                float3 forward = -UNITY_MATRIX_V[2].xyz;
                leave = min(leave, scene / max(dot(dir, forward), 1e-4));
                // Every view from outside must enter through the real door.
                // An exterior side/front view must not use a missing or open
                // underside of the locomotive's exterior mesh as a window.
                float viewStart = FireboxViewStart(origin, step3, _HolePlane, _HoleAcross, _HoleUp, enter);
                if (viewStart < 0.0)
                    leave = enter;
                else
                    enter = viewStart;
                // Nor over the flames' reach and the smoke's top (the mod
                // sets it): a low fire's rays stop where it does, and those
                // that pass over it march nothing.
                float roof = _Ceiling - 0.5 - origin.y;
                if (step3.y > 1e-5)
                    leave = min(leave, roof / step3.y);
                else if (step3.y < -1e-5)
                    enter = max(enter, roof / step3.y);
                else if (roof < 0.0)
                    leave = enter;
                // None of the box in front of the scene: nothing to march, though
                // the pixel still runs on to its quad's blend.
                float span = max(leave - enter, 0.0);

                // A step of about _StepLength, at most _Steps of them: a short
                // ray through a corner takes few.
                float steps = span > 0.0 ? clamp(ceil(span / max(_StepLength, 0.005)), 4.0, max(4.0, _Steps)) : 0.0;
                float dt = span / max(steps, 1.0);
                // The ray is read at its two ends and at one dithered point
                // in each step between, and each stretch between two reads
                // is taken from one to the other, by its own length. (Read
                // at the dithered points alone, each standing for a whole
                // step, what lies in the last part of a step before a lump,
                // the flames' dense bright feet, was caught by a pixel's
                // last point or missed: salt over the bed.)
                float jitter = 0.5 + (Dither(i.pos.xy) - 0.5) * _DitherAmount;
                // (At half resolution, the detail a step twice as long
                // resolves: its closer steps are there to sample the same
                // flames twice over, so the few pixels left to share out the
                // dither's pattern have less of it to share. With detail as
                // fine as the closer steps allow, the pattern came back as
                // a weave on the thinnest bright folds: the council, round 8.)
                StepLod = log2(max((_FireLowPass > 0.5 ? 2.0 : 1.0) * dt * 64.0, 1.0));
                half3 light = 0;
                // What gets through, by color: soot dims green and blue more than
                // red, so flames seen through smoke turn brown, not the olive a
                // grey dimming of yellow gives.
                half3 through = 1;
                float tile = 1.0 / max(_NoiseTile, 0.05);
                // The step before's flame shape against its threshold, and
                // which step that was: a step skipped leaves none to run from.
                float insideBefore = 0.0;
                int stepBefore = -2;
                // And the short flames' shape at the end of the step before.
                half3 lickBefore = 0.0;
                int lickStepBefore = -2;

                // Where the last read fell, how many steps on the next falls
                // (more than one over the flames' reach, where only smoke is:
                // _SmokeStride), and whether the ray's end has been read.
                float t = enter;
                float stride = 1.0;
                bool ended = false;

                for (int k = 0; k < 130; k++)
                {
                    if (steps < 0.5 || ended || through.r < 0.01)
                        break;
                    // This read's place along the ray, and the stretch back
                    // to the read before: none before the first, part of a
                    // step to the first dithered point, and whatever is left
                    // to the ray's end.
                    float seg = 0.0;
                    if (k > 0)
                    {
                        float next = k == 1 ? enter + jitter * dt : t + stride * dt;
                        if (next >= leave)
                        {
                            next = leave;
                            ended = true;
                        }
                        seg = next - t;
                        t = next;
                    }
                    stride = 1.0;
                    float3 u = origin + step3 * t + 0.5;

                    // The zones under this point, the flames leaning toward the
                    // tube plate with height.
                    // The flames' bodies ride the airflow: their zones read where
                    // across the gas here came from a moment ago, so they bend
                    // and roll with it (FireboxAirflow).
                    half4 flowBoth = tex2Dlod(_FlowB, float4(u.x, u.y, 0, 0));
                    half2 flowB = lerp(flowBoth.zw, flowBoth.xy, _FlowLerp.z);
                    float carried = flowB.x / _BoxSize.x;
                    float2 bed = float2(dot(_BedS, float3(carried, u.z, 1)), dot(_BedT, float3(carried, u.z, 1)));
                    float2 leaned = bed - float2(0, _Lean * u.y);
                    // Under the brick arch, and over it.
                    bool underArch = false;
                    bool overArch = false;
                    float archRoll = 0.0;
                    // In the layer of gases turning along the bricks: its
                    // curls and filaments blurred by its shear. (Not the
                    // sheets and lobes the flames' shapes are of: blurred
                    // too, the flames there thinned out and went soft.)
                    bool turning = false;
                    float turnLod = 0.0;
                    // The point in the box itself, for what is the box's own
                    // (its smoke's fade at its sides): u goes on as the
                    // point the flames here are read at.
                    float3 uBox = u;
                    half4 zoneA = 0.0;
                    bool zoned = false;
                    if (_ArchFlame.x > 0.0)
                    {
                        float archAlong = (u.z - _ArchLine.x) / (_ArchLine.y - _ArchLine.x);
                        float archUnder = lerp(_ArchLine.z, _ArchLine.w, archAlong);
                        bool overBricks = archAlong > 0.0 && u.y > archUnder;
                        underArch = archAlong > 0.0 && !overBricks;
                        // The zones' lean here, by where the point itself is.
                        float leanBy = -_Lean * u.y;
                        if (underArch)
                        {
                            // The lean toward the tubes gives way to the
                            // trail back to the lip over the first part of
                            // the arch: at once, the flames tore along the
                            // lip's own line.
                            float way = smoothstep(0.0, 0.3, archAlong);
                            float near = saturate((u.y - _GrateLevel) / max(archUnder - _GrateLevel, 1e-3));
                            leanBy = lerp(leanBy, _ArchFlame.y * near * near, way);
                        }
                        else
                            leanBy -= _ArchFlame.z * max(u.y - _ArchLine.z, 0.0);
                        leaned.y = bed.y + leanBy;
                        // The gases' way: how far in from the layer's foot
                        // the point lies, m. Under the bricks, its depth
                        // under them; past the lip and under its edge,
                        // turned about that edge; over its edge, how far
                        // behind the lip, the layer leaning on over the
                        // bricks with height.
                        float archSpan = abs(_ArchLine.y - _ArchLine.x) * _BoxSize.z;
                        float ahead = archAlong * archSpan;
                        float over = (u.y - _ArchLine.z) * _BoxSize.y;
                        float within = -1.0;
                        if (underArch)
                            within = (archUnder - u.y) * _BoxSize.y;
                        else if (over >= 0.0)
                            within = _ArchBend.z * over - ahead;
                        else if (ahead < 0.0)
                            within = length(float2(ahead, over));
                        // Within 30 cm behind the lip.
                        if (!underArch)
                            archRoll = archAlong <= 0.0 ? saturate(1.0 + ahead / 0.3) : 0.0;
                        float ceiling = underArch ? archUnder : _ArchLine.z;
                        if (within > 0.0 && within < _ArchFlame.x * (ceiling - _GrateLevel) * _BoxSize.y)
                        {
                            // The layer: its share of the room between the
                            // coal here and the bricks.
                            zoneA = tex2Dlod(_ZonesA, float4(leaned, 0, 0));
                            zoned = true;
                            float coalHere = lerp(_GrateLevel, zoneA.a, smoothstep(0.0, 1.0, bed.y / max(_DoorSlope, 1e-3)));
                            float layer = _ArchFlame.x * (ceiling - coalHere) * _BoxSize.y;
                            if (within < layer)
                            {
                                // The gas here left the coal nearer the
                                // bricks' foot, the nearer the bricks the
                                // farther back, and has come that far
                                // along them since: its flames are that
                                // coal's, that much farther up them.
                                // (At the bricks, from no farther back
                                // than _ArchFlow.w of the layer's depth,
                                // nor than their foot. From the foot
                                // always, the gases of the whole grate
                                // under the bricks were drawn through the
                                // layer's depth at the lip, eight times
                                // as fine through it as along it: their
                                // flames' detail there drew as hatching,
                                // and blurred to what the march's steps
                                // could carry, as stepped rings about the
                                // lip's edge: the council, round 13.)
                                turning = true;
                                float inward = 1.0 - within / layer;
                                float fromFoot = min(max(archSpan - ahead, 0.0), _ArchFlow.w * layer);
                                float travel = fromFoot * pow(inward, _ArchBend.x);
                                float climbed = layer - within;
                                float run = _ArchBend.y * travel;
                                u.z += travel / max(archSpan, 1e-3) * (_ArchLine.y - _ArchLine.x);
                                // (And no farther up them for its climb
                                // through the layer than _ArchFlow.z of
                                // it: held under the bricks, the gases
                                // gather there, and their flames fill the
                                // layer where free ones would have thinned
                                // out on the way up.)
                                // (Under a fire that burns hard: a weak
                                // one's little gas rises as in the open.
                                // Drawn out through the layer all the
                                // same, a drifting fire's faint flames
                                // reached, unseen, all through it, and
                                // every step of the march there read its
                                // smoke: 8 ms a frame from inside the
                                // box, jobs 2207 and 2213.)
                                float risen = lerp(1.0, _ArchFlow.z, smoothstep(0.35, 0.8, zoneA.r)) * climbed;
                                u.y += (sqrt(risen * risen + run * run) - climbed) / _BoxSize.y;
                                bed = float2(dot(_BedS, float3(carried, u.z, 1)), dot(_BedT, float3(carried, u.z, 1)));
                                leaned = float2(bed.x, bed.y + leanBy);
                                zoned = false;
                                // (The coal's place changes this many
                                // times faster through the layer's depth
                                // than along it.)
                                turnLod = _ArchFlow.x * log2(1.0 + fromFoot * _ArchBend.x * pow(inward, _ArchBend.x - 1.0) / layer);
                            }
                        }
                        // Over the bricks, the flames of the grate under
                        // them are under them.
                        overArch = overBricks && !turning && leaned.y > _ArchFlame.w;
                    }
                    if (!zoned)
                        zoneA = tex2Dlod(_ZonesA, float4(leaned, 0, 0));
                    half4 zoneB = tex2Dlod(_ZonesB, float4(bed, 0, 0));
                    // The coal's surface here: the zone's, sloping down to the
                    // grate at the door as the bed does.
                    // With the bed's swells and heaps (ZonedCoalBed.ReliefTexture),
                    // so the flames stand on the coal as drawn.
                    half4 relief = tex2Dlod(_BedRelief, float4(saturate(bed), 0, 0));
                    // How much fresh coal covers the bed under this point: its
                    // zone's green share, spread and in patches (CoalFreshPatch
                    // and CoalFreshCover in CoalSurface.cginc, the same read).
                    half laid = 0.0;
                    if (zoneB.a > 0.01)
                        laid = saturate((zoneB.a * 1.8 - tex3Dlod(_Noise, float4(bed.x * 2.3 + 0.21, 0.43, bed.y * 4.1 + 0.67, 0)).b) / 0.15);
                    float coal = zoneA.a + (zoneA.a - _GrateLevel) * max(relief.r, -0.8) + relief.g * _BedRise;
                    float surface = lerp(_GrateLevel, coal, smoothstep(0.0, 1.0, bed.y / max(_DoorSlope, 1e-3)));
                    float above = u.y - surface;
                    // The gaps between the lumps reach this far under the coal's
                    // surface (the bed draws its deeper coal there: CoalLumps).
                    float gapFloor = _LickDepth / _BoxSize.y;
                    // A read at that floor or under it, the ray having come
                    // down through it since the read before (deep in a gap,
                    // on the bed under the lumps, on the embers): it is
                    // moved back up the ray to the floor, and its stretch
                    // shortened by as much. Skipped, the part of the stretch
                    // above the floor went unmarched, as long as the pixel's
                    // dither left it, and the gaps' floors drew as a
                    // halftone of lit and unlit pixels.
                    if (above < -gapFloor)
                    {
                        float back = step3.y < -1e-5 ? (-gapFloor - above) / step3.y : -seg;
                        if (back <= -seg)
                            continue;
                        u += step3 * back;
                        seg += back;
                        above = -gapFloor;
                    }







                    half puff = tex3Dlod(_Noise, float4(leaned.x * 1.7 + 0.31, _PuffClock, leaned.y * 3.4 + 0.53, 0)).b;




                    half roots = tex3Dlod(_Noise, float4(leaned.x * 1.2, _RootOffset, leaned.y * 2.4, 0)).a;
                    // The flames' reach here: the surge at this height (the
                    // coal's now, older higher up), the zone's height swung by
                    // it and by the patch's puffing.
                    float climb = saturate(max(above, 0.0) / max(zoneA.g, 0.02)) * 3.0;
                    half surge = climb < 1.0 ? lerp(_PulseHistory.x, _PulseHistory.y, climb)
                               : climb < 2.0 ? lerp(_PulseHistory.y, _PulseHistory.z, climb - 1.0)
                               : lerp(_PulseHistory.z, _PulseHistory.w, climb - 2.0);
                    half swing = surge - _PulseMean;
                    float top = max(zoneA.g * (1.0 + _BeatHeight * swing), 0.02);
                    top *= lerp(1.0, 0.45 + 1.1 * puff, _Puff);
                    top *= 1.0 + _ArchBend.w * archRoll;
                    // Clear of every flame, the tall ones' reach and the short
                    // ones' in the gaps: only smoke from here, smooth against
                    // the flames, so the next read falls _SmokeStride steps on
                    // (over a low fire in a smoky box most of each ray is such;
                    // the march's cost is its reads). Arriving among the flames
                    // from such a stride, only the last step of it is taken as
                    // theirs.
                    bool clear = (above > 1.5 * top * (0.9 + 0.5 * roots) || overArch) && (above + gapFloor) * _BoxSize.y > 1.8 * _LickHeight;
                    if (clear)
                        stride = max(_SmokeStride, 1.0);
                    else
                        seg = min(seg, dt);
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
                    // Puffing and a strong root used to multiply into a much
                    // taller isolated flame. Keep growth within a small bed
                    // tongue's scale while retaining its standing motion.
                    lickTall = min(lickTall, 1.2 * _LickHeight);
                    float lickUp = (above + gapFloor) * _BoxSize.y / max(lickTall, 0.01);
                    // Each patch's sideways push, changing where it is, not carried
                    // up with the flames: the tongues, short and tall, sway from
                    // their feet.
                    half4 sway = tex3Dlod(_Noise, float4(leaned.x * 1.3 + 0.71, _SwayClock, leaned.y * 2.6 + 0.19, 0)) - 0.5;
                    half lick = 0;
                    // Where up the short flames their light is taken for this
                    // stretch of the ray: half way along it, not at its far
                    // end. (At the far end, a ray's last stretch down to the
                    // coal took all its flame at the foot's white heat, and
                    // the stretch before at its own end's orange: how the
                    // dither split the last centimetres between the two
                    // showed as white and orange blocks at the roots.)
                    float lickUpLight = lickUp;
                    half lickCore = 0;
                    // Only over the bed.
                    if (lickUp < 1.5 && all(bed == saturate(bed)))
                    {












                        float lickScale = _LickScale * lerp(0.8, 1.0, _LickMotion);
                        float3 q = u * _BoxSize * lickScale * float3(1, 0.35, 1);
                        q.xz += sway.ab * 2.0 * _Sway * lickScale * saturate(lickUp) * 0.5 * (0.35 + 0.65 * _LickMotion);
                        float2 bend = 0;
                        if (_LickDetail > 0.0)
                        {
                            half4 curl = tex3Dlod(_Noise, float4(q * 0.6 - float3(0, _LickOffset * 0.25, 0), 0)) - 0.5;
                            bend = curl.ga * 2.0 * _LickCurl * lerp(0.5, 1.0, _LickMotion) * _LickDetail;
                        }
                        // Their light taken along the stretch from the read
                        // before to this one, their shape running evenly
                        // between the two: they are dense and bright at
                        // their feet and thin at their fronts, and read at
                        // points they came out as sand. Where the read before
                        // marched none of them, their shape there is read now.
                        half3 lickFar = BedFlameField(q, lickUp, bend, lickScale, lickTall);
                        float3 qBack = step3 * seg * _BoxSize * lickScale * float3(1, 0.35, 1);
                        float upBack = step3.y * seg * _BoxSize.y / max(lickTall, 0.01);
                        lickUpLight = lickUp - 0.5 * upBack;
                        half3 lickNear = lickBefore;
                        if (lickStepBefore != k - 1)
                            lickNear = seg > 0.0 ? BedFlameField(q - qBack, lickUp - upBack, bend, lickScale, lickTall) : lickFar;
                        // And read half way between: their fronts are thinner
                        // than a step, and where the bed's hot spots glare
                        // through them a front between two reads showed or
                        // did not by the pixel's dither (white salt in the
                        // brightest gaps).
                        half2 licks = BedFlameLight(lickFar);
                        if (seg > 0.0)
                        {
                            half3 lickMid = BedFlameField(q - 0.5 * qBack, lickUp - 0.5 * upBack, bend, lickScale, lickTall);
                            licks = 0.5 * (BedFlameSpan(lickNear, lickMid) + BedFlameSpan(lickMid, lickFar));
                        }
                        lickBefore = lickFar;
                        lickStepBefore = k;
                        half both = 2.0 * licks.x;
                        lickCore = licks.y / max(licks.x, 1e-4);
                        // As dense as the coal is hot, not as hard as it is driven: a
                        // standing fire's hot gaps still flame (the footage in a
                        // station: bright tongues over a dark bed).
                        lick = 0.5 * both * saturate(1.6 * zoneB.r) * lerp(_LickHeat, 1.0, zoneB.r) * _LickStrength;
                        // At a low draft they burn only at fixed spots in the bed,
                        // relighting there (the footage, real time: tongues at the
                        // same places for seconds); under a hard draft, all over.
                        lick *= lerp(smoothstep(0.5, 0.68, spotsAt), 1.0, _LickMotion);
                        // None through a patch of fresh coal: they come up
                        // round its edges.
                        lick *= 1.0 - 0.95 * laid;
                    }
                    // The flames proper stand on the coal's surface: over as
                    // much of the stretch back to the read before as lies
                    // above it. (By this read alone, a ray ending on a lump
                    // under the surface lost the flames of its whole last
                    // stretch, as long as its dither left it: a halftone of
                    // dark pixels over the lumps beside every gap.)
                    float aboveNear = above - step3.y * seg;
                    half onCoal = seg > 0.0 ? EdgeMean(aboveNear * 2000.0, above * 2000.0, 1.0) : step(0.0, above);
                    if (onCoal <= 0.0 && lick <= 0.0)
                        continue;
                    above = max(above, 0.0);
                    aboveNear = max(aboveNear, 0.0);
                    // Over the flames' highest reach with no smoke there is nothing to draw.
                    // With smoke, only the smoke: its billows, without the six
                    // reads of the flames' sheets, curls, and filaments (over a
                    // low fire in a smoky box most of each ray is such).
                    bool overFlames = (above > 1.5 * top * (0.9 + 0.5 * roots) || overArch) && lick <= 0.0;
                    if (overFlames && flowB.g < 0.005)
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
                    // And the tall flames' light half way along the stretch,
                    // as the short ones'.
                    float riseLight = 0.5 * (above + aboveNear) / max(tongueHeight, 1e-4);
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










                    half4 flowA = lerp(tex2Dlod(_FlowAPrev, float4(u.x, u.y, 0, 0)), tex2Dlod(_FlowA, float4(u.x, u.y, 0, 0)), _FlowLerp.xxyy);
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
                    float curlLod = DetailLod(2.0 * tile) + turnLod;
                    float fineLod = DetailLod(_DetailScale * tile) + turnLod;
                    half4 phaseA = half4(0.5, 0.5, tex3Dlod(_Noise, float4(tallA - sheetShift * 0.7, 0)).b, 0.5);
                    half4 phaseB = half4(0.5, 0.5, tex3Dlod(_Noise, float4(tallB - sheetShift * 0.7, 0)).b, 0.5);
                    if (!overFlames)
                    {
                        phaseA.x = tex3Dlod(_Noise, float4(tallA - sheetShift + sheetDetail, 0)).g;
                        phaseA.y = tex3Dlod(_Noise, float4(crispA * 2.0 - curlShift, curlLod)).g;
                        phaseA.w = tex3Dlod(_Noise, float4(crispA * _DetailScale - curlShift * 2.0 + fineDetail, fineLod)).g;
                        phaseB.x = tex3Dlod(_Noise, float4(tallB - sheetShift + sheetDetail, 0)).g;
                        phaseB.y = tex3Dlod(_Noise, float4(crispB * 2.0 - curlShift + phaseDetail, curlLod)).g;
                        phaseB.w = tex3Dlod(_Noise, float4(crispB * _DetailScale - curlShift * 2.0 + fineDetail + phaseDetail, fineLod)).g;
                    }
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
                    // (Its depth taken along the stretch back to the read
                    // before: a layer two centimetres deep on the coal, read
                    // at a point, was caught or missed as the short flames
                    // were.)
                    float carpetScale = _BoxSize.y / max(_Carpet, 0.005);
                    half carpetDepth = EdgeMean(1.0 - aboveNear * carpetScale, 1.0 - above * carpetScale, 1.0);
                    half carpet = lerp(saturate(1.0 - above * carpetScale), carpetDepth, _EdgeMean) * (0.5 + 0.5 * curls) * (0.2 + 0.8 * _LickMotion);
                    // A crisp edge where the shape crosses the threshold; hotter the
                    // deeper inside a flame, so its body burns yellow and its edges
                    // orange-red.
                    half inside = shape - threshold;
                    half edge = 1.0 - saturate(inside * 5.0);
                    half fray = lerp(1.0, saturate(fine * 2.0 - 0.2), edge * _DetailErode);
                    half filled = saturate(inside * _Sharp);
                    if (stepBefore == k - 1)
                        filled = lerp(filled, EdgeMean(insideBefore, inside, _Sharp), _EdgeMean);
                    // (Over the flames their shape is not read: none to run from.)
                    if (!overFlames)
                    {
                        insideBefore = inside;
                        stepBefore = k;
                    }
                    half body = overArch ? 0.0 : (filled * (0.7 + 0.6 * lobes) * saturate((1.5 - rise) * 2.0) * fray + carpet) * zoneA.r * onCoal;
                    // (The test bed's: only the flames of the coal under the
                    // arch, to see their way along the bricks and round the
                    // lip without the flames before them.)
                    if (_ArchFlow.y > 0.5 && bed.y < _ArchFlame.w)
                    {
                        body = 0.0;
                        lick = 0.0;
                    }
                    half flame = body + lick;
                    half core = saturate(inside / max(_CoreDepth, 0.02) + carpet);









                    half heat = _HeatScale * lerp(_FlameHeat, 1.0, zoneB.r) * lerp(1.0, _TipHeat, pow(saturate(riseLight), 0.8)) * lerp(_EdgeHeat, 1.0, core) * (1.0 + _BeatHeat * swing);














                    half spot = lerp(_SpotDim, _SpotGlare, smoothstep(_SpotLow, 1.0, roots));
                    half reach = 1.0 - smoothstep(0.0, max(_BaseHeight, 0.01), saturate(riseLight));
                    half reach2 = reach * reach;
                    half foot = 1.0 + (_BaseGlow - 1.0) * lerp(_BaseCoke, 1.0, pow(zoneB.b, 1.5)) * reach2 * lerp(1.0, spot, reach2 * reach2);









                    heat += _WhiteHeart * core * reach2;
                    half lickHeat = saturate(lerp(_LickHeat, 1.0, zoneB.r) * lerp(1.3, 0.45, saturate(lickUpLight)))
                                  * lerp(1.0, lerp(0.6, 1.0, lickCore), _LickDetail)
                                  + _WhiteHeart * (1.0 - saturate(lickUpLight)) * lerp(1.0, lickCore, _LickDetail);
                    heat = lerp(heat, lickHeat, lick / max(flame, 1e-4));
                    // Over fresh coal the flames burn duller, smoky.
                    heat *= 1.0 - 0.25 * laid;
                    // _Glow is an opaque flame's radiance: a thick flame shows it,
                    // a thin one less, as much as its density takes up.
                    half lickRadiance = lerp(1.0, 0.5 + 0.9 * lickCore, _LickDetail);
                    half3 flameLight = FlameColor(heat) * flame * _Glow * lerp(1.0, 0.55 + 0.9 * fine, _DetailStreak) * lerp(1.0, 0.3 + 1.4 * curls, _Crinkle) * max(1.0 + _BeatGlow * swing, 0.2) * lerp(foot, _LickGlow * lerp(1.5, 0.4, saturate(lickUpLight)) * spot * lickRadiance, lick / max(flame, 1e-4));
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
                    // A dense cloud still has smoke between its billows. Large
                    // zero-density holes left the walls visible through it.
                    half smokeShape = max(0.25, lerp(0.5 + lobes, clumps, _SmokeClumps));
                    half smoke = flowB.g * onCoal * saturate((above * _BoxSize.y - 0.04) / 0.18) * smokeShape;
                    // Thinning out toward the volume's own sides and top:
                    // where a box stands wider than it (the S060's round
                    // shell) its smoke showed the volume's square outline.
                    smoke *= smoothstep(0.0, 0.22, min(min(uBox.x, 1.0 - uBox.x), min(1.0 - uBox.y, min(uBox.z, 1.0 - uBox.z))));
                    // Near the coal it is the smoke of the coal under it:
                    // thick over a fresh shovelful, thin over burned-down
                    // coke beside it; most of a metre up it has mixed. (The
                    // airflow carries one sheet of smoke for the whole length
                    // of the box, and a fresh corner's smoke drew as an even
                    // brown veil over all of it.)
                    // And within its zone, of the patches its fresh coal
                    // lies in (as the lumps and the bed under them read
                    // them, CoalSurface.cginc): thick over a patch, thin
                    // beside it.
                    half own = min(zoneA.b / max(_SmokeMean, 0.02), 3.0) * lerp(1.0, 0.4 + 1.6 * laid, saturate(zoneB.a * 4.0));
                    smoke *= lerp(1.0, lerp(own, 1.0, saturate(above * _BoxSize.y / 0.9)), _SmokeLocal);
                    // Lit orange from the coal below (the bed's glow, its color
                    // a little cooler than its flames') and by the flames in it.
                    half3 fireBelow = FlameColor(zoneB.r * 0.75) * zoneB.g * exp(-3.0 * above);
                    // Lit from the coal below, by the flames in it, and by the flames'
                    // light filling the box: pale among a working fire's flames, dark
                    // orange-brown over a standing one.
                    // Approximate the bed light absorbed on its way up through
                    // the smoke. Unattenuated box light made dense smoke glow
                    // almost as brightly as the fire it should hide.
                    // A patch of fresh coal's own smoke, rolling up from it
                    // thick before the airflow has it (the council: no smoke
                    // over fresh coal; the airflow's sheet of it is thin and
                    // unlit over a slow fire): as much as its zone smokes,
                    // in billows, thinning out over most of a metre; lit by
                    // the fire in the gaps round the patch, yellow-brown.
                    // (Its foot the patch's, softly; up from it the billows
                    // spread and wander with the curl, and thin out within
                    // half a metre: straight up from the patch's own edge it
                    // stood as a brown pillar with square sides.)
                    float metresUp = above * _BoxSize.y;
                    half column = 0.0;
                    if (_SmokePlume > 0.0 && zoneB.a > 0.01 && metresUp < 0.6)
                    {
                        float2 from = bed + warp.ga * (0.3 * saturate(metresUp / 0.4));
                        half foot = saturate((zoneB.a * 1.8 + 0.1 - tex3Dlod(_Noise, float4(from.x * 2.3 + 0.21, 0.43, from.y * 4.1 + 0.67, 0)).b) / 0.45);
                        column = _SmokePlume * foot * onCoal * min(zoneA.b * 3.5, 1.2) * saturate(metresUp / 0.12)
                               * (1.0 - smoothstep(0.15, 0.6, metresUp)) * max(0.12, clumps)
                               // (Thin under a working draft, which burns most
                               // of it and carries off the rest: thick there,
                               // it veiled the black coal in orange.)
                               * lerp(1.0, 0.35, _LickMotion);
                    }
                    smoke += column;
                    // (No hotter than an orange: by a working bed's yellow-white
                    // gaps the plume's thin foot glared, and showed the dither.)
                    half3 plumeLight = FireColor(min(FireBedCore(zoneB.r), 0.7)) * (0.5 * column / max(smoke, 1e-4)) * exp(-2.0 * metresUp);
                    half smokeShadow = exp(-smoke * _SmokeDensity * max(above * _BoxSize.y, 0.0));
                    // Over a patch of fresh coal the smoke rolls up thick in
                    // the light of the fire round it, a dull lit plume (the
                    // footage's fresh corner): lit only as the box's smoke
                    // is, it rose dark against a dark box and did not show.
                    half3 lit = smoke * _SmokeAlbedo * ((_SmokeLight * (1.0 + 6.0 * laid) * fireBelow + _BoxLight) * smokeShadow + plumeLight + 0.15 * flameLight);

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
                    half3 kept = exp(-sigma * seg);
                    // The light from this step, as much as gets out of it (energy
                    // conserving: an emissive, absorbing slab of its depth).
                    half3 source = glow + lit;
                    half3 out3 = sigma.g > 1e-4 ? source * (1.0 - kept) / sigma : source * seg;
                    light += through * out3;
                    through *= kept;
                }
                // (At half resolution the laying over blends the quads.)
                if (_FireLowPass < 0.5)
                {
                    float2 weights = QuadWeights(i.pos.xy, span);
                    light = QuadBlend(light, i.pos.xy, weights);
                    through = QuadBlend(through, i.pos.xy, weights);
                }
                // As the eye sees it: the flames' and smoke's light alone, the
                // walls and coal behind already so; those dimmed by the green's
                // share through, the blend's one.
                return half4(FireExposed(light), through.g);
            }
            ENDHLSL
        }
    }
}
