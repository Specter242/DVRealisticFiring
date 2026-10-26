// Chimney smoke for Realistic Firing: an alpha-blended particle lit by the
// scene's sky and sun, so smoke darkens at dusk and under cloud. The texture
// carries density in alpha and, with _Surface on, the smoke's surface in RGB:
// RG its normal across the particle, B its thickness through it. The sun
// lights the side that faces it, wrapping some way around (light scatters
// through a cloud), and the far side of thick smoke is in its own shadow;
// the sky lights it by which way it faces (brighter from above, the ground's
// below); thin smoke with the sun behind it glows. A soft ball under each
// particle rounds it. Flat-lit (_Flat), the side facing the sun is evenly
// bright, as a cloud's is (light scatters through it), falling quickly past
// the terminator to the sun's light diffused into the shade; the parts of
// the plume between a particle and the sun shadow it (the mod's custom data).
// Lit as one volume (_Volume), each pixel takes the plume's outward way and
// its shade from the sun at its place from the mod's 3D texture over the
// plume, so particles in the same place are lit alike. With _Surface off, RGB is baked relief, lit evenly as
// before. The particle's color tints it (white steam, grey haze, dark soot).
// Fraying: as the particle's alpha falls, the texture's thin parts go first
// and its dense strands stay, so thinning smoke breaks into wisps rather than
// fading evenly; at 0 it fades evenly. Soft particles and fog follow the
// game's settings. Needs the particles' tangent stream for _Surface.
// Lit six ways (_SixWay), the particle is a puff of steam simulated in 3D and
// rolling in a loop (fx/textures/make_rolling_steam.py): two flipbooks hold
// its opacity and its light from the right, top, and front, and from the
// left, bottom, and back, so the sun lights it from its own way and its
// lobes shadow one another; as it thins its thin edges clear first.
// The mod's second custom data gives how far it has rolled (z, loops) and
// which puff it is (w: the puff, plus a half when it is mirrored); frames
// are blended, so the roll is smooth. As a particle thins it turns to a wisp
// (_Wisps on): the plume's own wisp flipbook (_MainTex, its surface lit by
// the sun's and sky's ways), at the particle's own turn, its frame by its age
// (the mod's first custom data, w).
Shader "RealisticFiring/ChimneySmoke"
{
    Properties
    {
        _MainTex ("Flipbook (RG normal and B thickness, or RGB relief; A density)", 2D) = "white" {}
        _WispTex ("The wisps with their relief baked in, as 1.13 drew them", 2D) = "white" {}
        _WispBaked ("Wisps drawn as 1.13 drew them: their relief baked in", Float) = 1
        _WispTurn ("Wisps at their own turn, any way, slowly spinning (0: the particle's)", Float) = 1
        _WispBakedLight ("Their light, as 1.13's plume light", Range(0, 3)) = 1.6
        _WispPuffLit ("Those wisps lit as the puffs are (else as 1.13 lit them)", Float) = 1
        _WispPuffLight ("So lit, their light, a share of the puffs'", Range(0, 2)) = 1
        _Surface ("RGB is the surface (else relief)", Range(0, 1)) = 0
        _Tiles ("Flipbook tiles across", Float) = 1
        _InvFade ("Soft particles factor", Range(0.01, 3.0)) = 0.6
        _SunStrength ("Sun strength", Range(0, 4)) = 0.7
        _SkyStrength ("Sky strength", Range(0, 8)) = 1.0
        _MaxLight ("Brightest light", Range(0.5, 6)) = 1.4
        _Fray ("Fray as it thins", Range(0, 1)) = 0
        _Wrap ("Sun wrapping around", Range(0, 1)) = 0.4
        _Relief ("Surface relief", Range(0, 2)) = 1
        _Bulge ("Roundness", Range(0, 1)) = 0.4
        _Shade ("Thick smoke's own shadow", Range(0, 1)) = 0.5
        _Scatter ("Sun through thin smoke", Range(0, 3)) = 0.5
        _BinormalSign ("Binormal sign", Float) = 1
        _Grey ("Light greyed toward its brightness", Range(0, 1)) = 0
        _PlumeShape ("Lit as the plume's surface", Range(0, 3)) = 0
        _BillowTex ("Billows for thick steam (the flipbook's layout)", 2D) = "black" {}
        _Billow ("Billows where the smoke is thick", Range(0, 1)) = 0
        _BillowFrom ("Billows from this opacity", Range(0, 1)) = 0.3
        _BillowFull ("Billows fully at this opacity", Range(0, 1)) = 0.8
        _Flat ("Flat-lit, with a terminator", Range(0, 1)) = 0
        _Terminator ("Terminator's softness", Range(0.02, 1)) = 0.25
        _ShadeLight ("Sun diffused into the shade", Range(0, 1)) = 0.35
        _SunShadow ("Shadowed by the plume", Range(0, 1)) = 0
        _Volume ("Lit as one volume", Range(0, 1)) = 0
        _VolumeShape ("The volume's shape in the light", Range(0, 3)) = 1
        _VolumeTex ("The volume (outward way, shade)", 3D) = "" {}
        _VolumePrev ("The volume before", 3D) = "" {}
        _NearFadeFrom ("Shown from this far from the camera, m", Float) = 0
        _NearFadeTo ("Fully shown from this far, m", Float) = 0
        _SixWay ("Lit six ways: rolling steam", Range(0, 1)) = 0
        _LightA ("Six ways: right, top, front light; A opacity", 2D) = "black" {}
        _LightB ("Six ways: left, bottom, back light; A thickness's square root", 2D) = "black" {}
        _SixColumns ("Frames across", Float) = 16
        _SixRows ("Frames down", Float) = 8
        _SixFrames ("Frames a loop", Float) = 32
        _SixPlume ("Shaded by the plume's far side", Range(0, 1)) = 0.5
        _SixShade ("Sun scattered into a puff's shade", Range(0, 1)) = 0.4
        _ThickFloor ("Thickness taken as none, below (its square root)", Range(0, 0.1)) = 0.05
        _DebugAlpha ("Development: opacity shown red, ×", Float) = 0
        _ShadeSky ("The shade's scattered light, the sky's color", Range(0, 1)) = 0
        _Wisps ("Wisps from the flipbook as it thins (0: none)", Float) = 0
        _WispBelow ("Wholly a wisp at or below this opacity", Range(0, 1)) = 0.15
        _WispAbove ("Wholly a puff at or above this opacity", Range(0, 1)) = 0.6
        _WispAgeFrom ("Wisps from this share of the flipbook's time", Range(0, 1)) = 0.1
        _WispAgeTo ("Wholly wisps by this share", Range(0, 1)) = 0.3
        _PuffAgeScale ("Moving train's puff handoff time scale", Range(0, 1)) = 1
        _WispBlend ("Wisps blended to their next frame (0: stepped)", Range(0, 1)) = 0
        _WispSheet ("Wisps play the whole sheet in turn (0: each its own row)", Float) = 1
        _WispCoherence ("Wisps each keep their own row, already ragged at birth", Float) = 0
        _WispHold ("So kept, each holds one frame of it", Float) = 0
        _WispFadeFrom ("Wisps' strands fade out from this share of their reach (1: cut there)", Range(0, 1)) = 1
        _ThinWispAgeFrom ("A light beat's puffs turn to wisps from this share", Range(0, 1)) = 0.2
        _ThinWispAgeTo ("Wholly wisps by this share", Range(0, 1)) = 0.45
        _WispRelief ("Wisps' surface relief", Range(0, 4)) = 1
        _WispBulge ("Wisps' roundness", Range(0, 1)) = 0.4
        _WispScatter ("Sun through thin wisps", Range(0, 3)) = 0.5
        _WispSunStrength ("Sun strength on the wisps", Range(0, 4)) = 1
        _SootAlbedo ("Black smoke's reflectance", Range(0, 0.5)) = 0
        _LeastOpacity ("Opacity drawn as none, below", Range(0, 0.05)) = 0.002
        _Fringe ("A puff's thin edge frays into strands", Range(0, 1)) = 0
        _Upright ("Rolling steam faces the camera's place, upright", Range(0, 1)) = 1
        _Grow ("Rolling steam's quad, × its particle's", Range(1, 2)) = 1
        _WispReach ("A wisp's reach, × its puff's half-width", Range(0.5, 2)) = 1
        _FringeShare ("A fringe's strands' opacity, × its particle's wisp's", Range(0, 1)) = 1
        _FringeRimFrom ("Fringe from this far on the plume's silhouette (the mod's)", Range(0, 1)) = 0.45
        _FringeRimTo ("Wholly fringed by this", Range(0, 1)) = 0.8
        _FringeCore ("Its edge, below this thickness", Range(0.01, 1)) = 0.35
        // 15 drawn at half resolution (the mod's PlumeOffscreen), its
        // coverage kept in alpha to lay over the scene after; 14 (RGB) on it.
        [HideInInspector] _ColorMask ("Channels written", Float) = 14
        // 1 while it is drawn at half resolution: the camera's own drawing
        // of it then draws nothing (the particles still built by the game,
        // which builds none it is not drawing itself).
        [HideInInspector] _Offscreen ("Drawn only at half resolution", Float) = 0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Cull Off
        Lighting Off
        ZWrite Off

        Pass
        {
            Tags { "LightMode"="ForwardBase" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_particles
            #pragma multi_compile_fog
            // Eleven interpolators.
            #pragma target 3.5
            // Single-pass stereo on the mod's own keyword: Unity strips its own
            // from a bundle built without VR, and the mod turns this on for stereo cameras.
            #pragma multi_compile __ RF_SINGLE_PASS_STEREO
            #if defined(RF_SINGLE_PASS_STEREO)
            #define UNITY_SINGLE_PASS_STEREO 1
            #endif
            // HLSL, not CG: CG includes these before the define above.
            #include "HLSLSupport.cginc"
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            sampler2D _MainTex;
            sampler2D _WispTex;
            float _WispBaked;
            float _WispTurn;
            float _WispBakedLight;
            float _WispPuffLit;
            float _WispPuffLight;
            sampler2D _BillowTex;
            float _Billow;
            float _BillowFrom;
            float _BillowFull;
            float4 _MainTex_ST;
            float _Surface;
            float _Tiles;
            float _InvFade;
            float _SunStrength;
            float _SkyStrength;
            float _MaxLight;
            float _Fray;
            float _Wrap;
            float _Relief;
            float _Bulge;
            float _Shade;
            float _Scatter;
            float _BinormalSign;
            float _Grey;
            float _PlumeShape;
            float _Flat;
            float _Terminator;
            float _ShadeLight;
            float _SunShadow;
            sampler3D _VolumeTex;
            sampler3D _VolumePrev;
            float4x4 _VolumeMap;
            float4x4 _VolumePrevMap;
            float4x4 _VolumeToWorld;
            float _Volume;
            float _VolumeShape;
            float _VolumeBlend;
            float _NearFadeFrom;
            float _NearFadeTo;
            float _Offscreen;
            // Set to 1 by the mod's command buffer while it draws the smoke at half resolution.
            float _PlumeOffscreenPass;
            sampler2D _LightA;
            sampler2D _LightB;
            float _SixWay;
            float _SixColumns;
            float _SixRows;
            float _SixFrames;
            float _SixPlume;
            float _SixShade;
            float _ThickFloor;
            float _DebugAlpha;
            float _ShadeSky;
            float _Wisps;
            float _WispBelow;
            float _WispAbove;
            float _WispAgeFrom;
            float _WispAgeTo;
            float _PuffAgeScale;
            float _WispBlend;
            float _WispSheet;
            float _WispCoherence;
            float _WispHold;
            float _WispFadeFrom;
            float _ThinWispAgeFrom;
            float _ThinWispAgeTo;
            float _WispRelief;
            float _WispBulge;
            float _WispScatter;
            float _WispSunStrength;
            float _SootAlbedo;
            float _LeastOpacity;
            float _Fringe;
            float _Upright;
            float _Grow;
            float _WispReach;
            float _FringeShare;
            float _FringeRimFrom;
            float _FringeRimTo;
            float _FringeCore;
            float4 _LightB_TexelSize;
            float4 _MainTex_TexelSize;
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                fixed4 color : COLOR;
                // UV in xy; the particle's part of the plume's surface (its
                // first custom data, set by the mod) in zw and the next's xy;
                // its billows' onset and its shade from the plume (the
                // second's xy) in the next's zw; its roll and puff (the
                // second's zw) in the third's xy. Zero without the mod's data.
                float4 texcoord : TEXCOORD0;
                float4 texcoord1 : TEXCOORD1;
                float4 texcoord2 : TEXCOORD2;
                // The particle's middle (the Center stream) in the third's zw
                // and the fourth's x.
                float4 texcoord3 : TEXCOORD3;
                float4 tangent : TANGENT;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                // The texture's UV; lit six ways, this frame's and the next's.
                float4 texcoord : TEXCOORD0;
                // w: lit six ways, the blend to the wisp's next frame.
                float4 world : TEXCOORD1;
                // Lit six ways, their w the ground's light from below (the
                // sky's from above in shape.xyz): constant over the plume,
                // so worked out a vertex, not a pixel.
                half4 normal : TEXCOORD2;
                half4 tangent : TEXCOORD3;
                half4 binormal : TEXCOORD4;
                UNITY_FOG_COORDS(5)
                #ifdef SOFTPARTICLES_ON
                float4 projPos : TEXCOORD6;
                #endif
                half4 shape : TEXCOORD7;
                // Billows' onset, shade, and lit six ways the blend to the
                // next frame and whether the puff is mirrored.
                half4 onsetShade : TEXCOORD8;
                // Lit six ways, the wisp's UV, this frame's and the next's
                // (onsetShade.xy then holds its turn: the cosine and sine of
                // its texture's right in the particle's square).
                float4 wispUV : TEXCOORD9;
                // Lit six ways, where in its puff's frame (1 at the middle of
                // the frame's side; the quad reaches _Grow).
                // z: how far it fringes, on the plume's silhouette (the
                // mod's); w: its wisp's reach.
                float4 local : TEXCOORD10;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);






                float4 objPos = v.vertex;
                float2 corner = v.texcoord.xy - 0.5;
                bool upright = _SixWay > 0.5 && _Upright > 0.5;
                // Grown (_Grow) only where its wisp shows: on the plume's
                // silhouette, or turning to a wisp (grown every one, the plume
                // cost a third more).
                float fringed = smoothstep(_FringeRimFrom, _FringeRimTo, v.texcoord1.w);








                float hard = _SixWay > 0.5 ? saturate(v.texcoord1.x) : 1;
                float wispFrom = lerp(_ThinWispAgeFrom, _WispAgeFrom, hard) * _PuffAgeScale;
                float wispTo = max(wispFrom + 0.01, lerp(_ThinWispAgeTo, _WispAgeTo, hard) * _PuffAgeScale);
                float thinning = _Wisps > 0 ? max(1 - smoothstep(_WispBelow, _WispAbove, v.texcoord1.z),
                                                  smoothstep(wispFrom, wispTo, v.texcoord1.y)) : 0;
                float stretch = upright ? max(fringed, thinning) : 0;
                float grow = lerp(1, _Grow, stretch);
                float reach = _WispReach;
                float3 uprightRight = float3(1, 0, 0), uprightUp = float3(0, 1, 0), uprightToward = float3(0, 0, 1);
                if (upright)
                {
                    float3 center = float3(v.texcoord2.zw, v.texcoord3.x);
                    float3 centerW = mul(unity_ObjectToWorld, float4(center, 1)).xyz;
                    #if defined(USING_STEREO_MATRICES)
                    float3 eye = (unity_StereoWorldSpaceCameraPos[0] + unity_StereoWorldSpaceCameraPos[1]) * 0.5;
                    #else
                    float3 eye = _WorldSpaceCameraPos;
                    #endif
                    float3 toward = normalize(eye - centerW);
                    // Upright as the world's up looks from there; straight
                    // overhead or under, the camera's up.
                    float3 worldUp = float3(0, 1, 0) - toward * toward.y;
                    float upLength = length(worldUp);
                    float3 cameraUp = UNITY_MATRIX_V[1].xyz;
                    cameraUp = normalize(cameraUp - toward * dot(cameraUp, toward) + float3(0, 1e-5, 0));
                    float3 up = normalize(lerp(cameraUp, worldUp / max(upLength, 1e-4), smoothstep(0.1, 0.35, upLength)));
                    float3 right = normalize(cross(toward, up));
                    up = cross(right, toward);
                    // Unity's corner lies the half-diagonal out.
                    float side = length(mul(unity_ObjectToWorld, float4(v.vertex.xyz - center, 0)).xyz) * 1.41421;
                    float3 cornerW = centerW + (right * corner.x + up * corner.y) * side * grow;
                    objPos = mul(unity_WorldToObject, float4(cornerW, 1));
                    uprightRight = right;
                    uprightUp = up;
                    uprightToward = toward;
                }
                o.vertex = UnityObjectToClipPos(objPos);
                o.local = float4(corner * grow * 2, fringed, reach);
                #ifdef SOFTPARTICLES_ON
                o.projPos = ComputeScreenPos(o.vertex);
                COMPUTE_EYEDEPTH(o.projPos.z);
                #endif
                o.color = v.color;
                o.texcoord = TRANSFORM_TEX(v.texcoord.xy, _MainTex).xyxy;
                o.shape = half4(v.texcoord.zw, v.texcoord1.xy);
                o.onsetShade = half4(v.texcoord1.zw, 0, 0);
                if (_SixWay > 0.5)
                {
                    // This frame of the puff's loop and the next, blended.
                    float puff = floor(v.texcoord2.y);
                    half mirrored = frac(v.texcoord2.y) > 0.25 ? 1 : 0;
                    float at = frac(v.texcoord2.x) * _SixFrames;
                    float first = floor(at);
                    float2 uv = 0.5 + corner * grow;
                    uv.x = mirrored > 0.5 ? 1 - uv.x : uv.x;
                    float2 cells = float2(_SixColumns, _SixRows);
                    float index = puff * _SixFrames + first;
                    float next = puff * _SixFrames + fmod(first + 1, _SixFrames);
                    float2 cell = float2(fmod(index, _SixColumns), floor(index / _SixColumns));
                    float2 nextCell = float2(fmod(next, _SixColumns), floor(next / _SixColumns));
                    // The sheet's first row is its top.
                    o.texcoord.xy = float2(cell.x + uv.x, _SixRows - 1 - cell.y + uv.y) / cells;
                    o.texcoord.zw = float2(nextCell.x + uv.x, _SixRows - 1 - nextCell.y + uv.y) / cells;
                    o.onsetShade.zw = half2(at - first, mirrored);
                }
                o.wispUV = 0;
                o.world = float4(mul(unity_ObjectToWorld, objPos).xyz, 0);

                // The particle faces the viewer; its texture's right is its
                // tangent (turned with it), up the binormal. Without the
                // tangent stream, the view's own right.
                half3 toward = normalize(UnityWorldSpaceViewDir(o.world.xyz));
                half3 right = UnityObjectToWorldDir(v.tangent.xyz);
                if (dot(right, right) < 0.01)
                    right = normalize(cross(half3(0, 1, 0), toward) + half3(1e-4, 0, 0));
                right = normalize(right - toward * dot(right, toward));
                o.normal = half4(toward, 0);
                o.tangent = half4(right, 0);
                o.binormal = half4(cross(toward, right) * _BinormalSign, 0);
                if (upright)
                {
                    o.normal = half4(uprightToward, 0);
                    o.tangent = half4(uprightRight, 0);
                    o.binormal = half4(uprightUp, 0);
                }
                if (_SixWay > 0.5)
                {
                    half3 below = max(0, ShadeSH9(half4(0, -1, 0, 1)));
                    // Its w: how far it is a wisp.
                    o.shape = half4(max(0, ShadeSH9(half4(0, 1, 0, 1))), thinning);
                    o.normal.w = below.r;
                    o.tangent.w = below.g;
                    o.binormal.w = below.b;
                    if (_Wisps > 0)
                    {
















                        float3 spun = UnityObjectToWorldDir(v.tangent.xyz);
                        float2 turn = float2(dot(spun, UNITY_MATRIX_V[0].xyz), dot(spun, UNITY_MATRIX_V[1].xyz));
                        turn = dot(turn, turn) > 1e-6 ? normalize(turn) : float2(1, 0);
                        // Its own turn, any way, spinning slowly as 1.13's
                        // particles did (the mod's first custom data, y): the
                        // rolling puffs hold within a few degrees of upright,
                        // and wisps at their turn all lay one way, a grain
                        // round a rope rather than folds crossing (a council,
                        // 2026-10-23).
                        if (_WispTurn > 0.5)
                            turn = float2(cos(v.texcoord.w), sin(v.texcoord.w));
                        float2 p = upright ? o.local.xy : (v.texcoord.xy - 0.5) * 2;
                        float2 turned = 0.5 + 0.5 * float2(dot(p, turn), dot(p, float2(-turn.y, turn.x))) / reach;
                        float row = clamp(floor(v.texcoord.z + 0.5), 0, 7);
                        float at = saturate(v.texcoord1.y) * 8;
                        float first = min(floor(at), 7);
                        float next = min(first + 1, 7);
                        float nextRow = row;
                        float blend = saturate(at - first) * _WispBlend;
                        if (_WispCoherence > 0.5)
                        {
                            // Its own row, the rows unrelated (the sheet in
                            // turn blended one into another, its strands
                            // fading out and others in: flashing), from a
                            // frame of its own past the young compact ones,
                            // its fringe ragged from the first (Sol, 2026-10-23).
                            float phase = saturate(frac(v.texcoord.z) / 0.49);
                            at = lerp(2 + 4 * phase, 7, saturate(v.texcoord1.y));




                            if (_WispHold > 0.5)
                                at = floor(2 + 5.99 * phase);
                            first = min(floor(at), 7);
                            next = min(first + 1, 7);
                            blend = at - first;
                        }
                        else if (_WispSheet > 0.5)
                        {
                            // The whole sheet in turn, blended (2026-10-22):
                            // each wisp's life every eighth of the particle's.
                            at = saturate(v.texcoord1.y) * 63;
                            float frame = min(floor(at), 62);
                            first = fmod(frame, 8);
                            row = floor(frame / 8);
                            next = fmod(frame + 1, 8);
                            nextRow = floor((frame + 1) / 8);
                            blend = at - frame;
                        }
                        // The sheet's first row is its top.
                        o.wispUV = float4(float2(first + turned.x, 7 - row + turned.y) / 8,
                                          float2(next + turned.x, 7 - nextRow + turned.y) / 8);
                        o.world.w = blend;
                        o.onsetShade.xy = half2(turn);
                    }
                }

                UNITY_TRANSFER_FOG(o, o.vertex);
                // Drawn only at half resolution: elsewhere beyond the screen,
                // so no pixel is drawn.
                if (_Offscreen > 0.5 && _PlumeOffscreenPass < 0.5)
                    o.vertex = float4(2, 2, 2, 1);
                return o;
            }

            // The mip level for UV changing by dx and dy a pixel over a texture
            // of size texels: the sampling read at a level, as its gradients
            // read explicitly ran slower (an RX 9070: the plume a fifth dearer).
            float MipLevel(float2 dx, float2 dy, float2 size)
            {
                dx *= size;
                dy *= size;
                return 0.5 * log2(max(max(dot(dx, dx), dot(dy, dy)), 1e-8));
            }










            half3 SurfaceLight(half4 tex, half2 across, half3 right, half3 up, half3 toward, half3 outward, half alpha, half sunReach,
                               half relief, half bulge, half flat, half scatter, half sunStrength)
            {
                half2 slope = (tex.rg * 2 - 1) * relief + across * bulge;
                half facing = sqrt(saturate(1 - dot(slope, slope)));
                half3 n = normalize(right * slope.x + up * slope.y + toward * max(facing, 0.15) + outward);
                half thick = saturate(tex.b * alpha * 2);
                half3 toSun = _WorldSpaceLightPos0.xyz;
                half facingSun = dot(n, toSun);
                half lambert = saturate((facingSun + _Wrap) / (1 + _Wrap)) * sunReach;
                // A cloud's: even wherever it faces the sun, past the
                // terminator only what the cloud diffuses into its shade.
                half sunlit = smoothstep(-_Terminator, _Terminator, facingSun + 0.05) * sunReach;
                half sunward = lerp(lambert, lerp(_ShadeLight, 1, sunlit), flat);
                // The far side of thick smoke is in its own shadow.
                sunward *= lerp(1, sunward, _Shade * thick);
                // Thin smoke with the sun behind it glows.
                half behind = saturate(dot(-toSun, toward));
                half through = behind * behind * behind * behind * scatter * (1 - thick) * sunReach;
                half3 sun = _LightColor0.rgb * (sunward * sunStrength + through);
                half3 sky = max(0, ShadeSH9(half4(n, 1))) * _SkyStrength;
                return min(sky + sun, _MaxLight);
            }

            // The puff's light from the way l (in its frame: right, up, toward
            // the viewer), from its six ways' light.
            half SixWays(half4 a, half4 b, half3 l)
            {
                half3 l2 = l * l;
                return l2.x * (l.x > 0 ? a.r : b.r) + l2.y * (l.y > 0 ? a.g : b.g) + l2.z * (l.z > 0 ? a.b : b.b);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                if (_SixWay > 0.5)
                {
                    // Its thickness first: most of a particle's square is
                    // clear, and drawing it all cost most of the plume's time.
                    // As it thins it turns to a wisp; each only read when shown
                    // (their mip levels worked out outside the branches).
                    // By the plume's opacity here, not the particle's: a dense
                    // plume's particles are each faint (they share it), and
                    // by theirs dense black smoke turned to wisps; and by its
                    // age (the vertex's).
                    half wispy = i.shape.w;
                    float level = MipLevel(ddx(i.texcoord.xy), ddy(i.texcoord.xy), _LightB_TexelSize.zw);
                    float wispLevel = MipLevel(ddx(i.wispUV.xy), ddy(i.wispUV.xy), _MainTex_TexelSize.zw);
                    half blend = i.onsetShade.z;
                    // Where in its square (the puff's frame is the square):
                    // a puff reaches no further than 0.77 of its half-width,
                    // its wisp's frame (turned any way) its reach; beyond,
                    // nothing is read (half of a puff's square).
                    half puffOut = length(i.local.xy);
                    half wispOut = i.local.w;
                    bool readPuff = wispy < 0.995 && puffOut < 0.79;
                    half4 b = 0, bw = 0;
                    if (readPuff)
                        b = lerp(tex2Dlod(_LightB, float4(i.texcoord.xy, 0, level)), tex2Dlod(_LightB, float4(i.texcoord.zw, 0, level)), blend);
                    half rootPuff = saturate((b.a - _ThickFloor) / (1 - _ThickFloor));





                    half atEdge = i.local.z;
                    // Only a puff frays so: a wisp is its own strands (on the
                    // silhouette, its own wisp added again over it, 0.5
                    // opaque drew 0.71, and the edge a rope).
                    half fringe = puffOut < wispOut ? _Fringe * atEdge * (1 - smoothstep(0, _FringeCore, rootPuff)) * (1 - wispy) : 0;
                    half wispyHere = max(wispy, fringe);
                    bool readWisp = wispyHere > 0.005 && puffOut < wispOut;
                    if (!readPuff && !readWisp)
                        discard;
                    if (readWisp)
                    {



                        if (_WispBaked > 0.5)
                        {
                            bw = tex2Dlod(_WispTex, float4(i.wispUV.xy, 0, wispLevel));
                            if (i.world.w > 0.001)
                                bw = lerp(bw, tex2Dlod(_WispTex, float4(i.wispUV.zw, 0, wispLevel)), i.world.w);
                        }
                        else
                        {
                            bw = tex2Dlod(_MainTex, float4(i.wispUV.xy, 0, wispLevel));
                            if (i.world.w > 0.001)
                                bw = lerp(bw, tex2Dlod(_MainTex, float4(i.wispUV.zw, 0, wispLevel)), i.world.w);
                        }




                        if (_WispFadeFrom < 0.999)
                            bw.a *= 1 - smoothstep(_WispFadeFrom * wispOut, wispOut, puffOut);
                    }
                    // Its optical depth through, the texture's thickness
                    // being 1 - exp(-depth / 6).
                    half depthPuff = -6 * log(max(1 - rootPuff * rootPuff, 1e-3));
                    // The wisp as the plume drew it: as the particle thins,
                    // its thin parts go first and its dense strands stay.
                    half cut = _Fray * (1 - i.color.a);
                    half wispOpacity = saturate((bw.a - cut) / max(1 - cut, 0.05)) * pow(i.color.a, 1 - _Fray);
                    half seenWisp = -log(max(1 - wispOpacity, 1e-3));
                    // Thinning, the steam through it thins: its thin edges
                    // clear first and stay soft, its lobes' middles last,
                    // where cutting at a thickness drew each puff's edge as
                    // a disc in thin dark smoke. The depth's share (the
                    // texture's thickness is 1 - exp(-depth / 6)) is set so
                    // the puff covers about its opacity's share of what it
                    // does whole (fit to the puffs' mean). The thickness is
                    // stored as its square root, below a floor taken as
                    // none: the compression's error there, magnified by the
                    // thinning and stacked over many particles, drew a faint
                    // streaky fringe half the plume's width.
                    half share = pow(i.color.a, 1 - 0.5 * _Fray);
                    half depthShare = -0.189 * log(1 - 0.995 * share);
                    // The fringe's strands add their own smoke to the puff's.
                    half seenAs = lerp(depthShare * depthPuff, seenWisp, wispy);
                    half seenFringe = -log(max(1 - wispOpacity * _FringeShare, 1e-3)) * fringe;
                    half opacity = 1 - exp(-(seenAs + seenFringe));
                    // Their light mixed by the smoke each holds here: by the
                    // wispiness alone, where only one held any the other's
                    // nothing darkened it (dark, sharp-edged streaks).
                    half mixWisp = saturate((wispy * seenWisp + seenFringe) / max(seenAs + seenFringe, 1e-4));
                    if (opacity < _LeastOpacity)
                        discard;
                    half4 a = 0;
                    if (readPuff)
                        a = lerp(tex2Dlod(_LightA, float4(i.texcoord.xy, 0, level)), tex2Dlod(_LightA, float4(i.texcoord.zw, 0, level)), blend);
                    half flip = i.onsetShade.w > 0.5 ? -1 : 1;
                    half3 toSun = _WorldSpaceLightPos0.xyz;
                    half3 sunWay = half3(dot(toSun, i.tangent.xyz) * flip, dot(toSun, i.binormal.xyz), dot(toSun, i.normal.xyz));
                    half3 upWay = half3(i.tangent.y * flip, i.binormal.y, i.normal.y);
                    // The plume as one volume here: its shade from the sun, and
                    // its outward way, its far side from the sun in shade (read
                    // after the clear pixels are dropped: before, they doubled
                    // the plume's cost).
                    half4 volume = half4(0.5, 0.5, 0.5, 0);
                    if (_Volume > 0)
                    {
                        float4 at = float4(i.world.xyz, 1);
                        volume = lerp(tex3D(_VolumePrev, mul(_VolumePrevMap, at).xyz),
                                      tex3D(_VolumeTex, mul(_VolumeMap, at).xyz), _VolumeBlend);
                    }
                    half3 outward = mul((float3x3)_VolumeToWorld, volume.rgb * 2 - 1);
                    half outwardSize = length(outward);
                    half farSide = smoothstep(-_Terminator, _Terminator, dot(outward / max(outwardSize, 1e-3), toSun) + 0.05);
                    half reach = (1 - volume.a * _Volume) * lerp(1, farSide, _SixPlume * _Volume * saturate(outwardSize * 4));
                    // The sun on the puff, its shadows lifted by the light the
                    // cloud scatters into them; the sky from above and the
                    // ground's from below, each on the puff's side toward it.
                    // The light scattered into the shade, some of it the sky's
                    // color (a dawn's warm sun drew the whole plume peach).
                    half3 skyAbove = i.shape.xyz;
                    half3 sunColor = _LightColor0.rgb;
                    half sunBright = dot(sunColor, half3(0.299, 0.587, 0.114));
                    half3 skyHue = skyAbove / max(dot(skyAbove, half3(0.299, 0.587, 0.114)), 1e-3);
                    half3 scattered = lerp(sunColor, skyHue * sunBright, _ShadeSky);
                    half direct = SixWays(a, b, sunWay) * reach;
                    // Lit as the steam is, by its own reflectance: black smoke's
                    // color was a fiftieth of white, so its lit lobes and its
                    // creases drew alike, one dark mass (photo 19: soot is gray
                    // where the sun is on its lobes, black in its creases). The
                    // light scattered into the shade is the smoke's own, so it
                    // scales with its reflectance: dark smoke's creases go black.
                    half3 albedo = lerp(i.color.rgb, 1, _SootAlbedo);
                    half fill = saturate(dot(albedo, half3(0.299, 0.587, 0.114)) / 0.85);
                    half3 sun = (sunColor * (1 - _SixShade) * direct + scattered * _SixShade * fill) * _SunStrength;
                    half allRound = (a.r + a.g + a.b + b.r + b.g + b.b) / 6;
                    half fromAbove = SixWays(a, b, upWay);
                    half fromBelow = SixWays(a, b, -upWay);
                    half3 sky = (skyAbove * lerp(allRound, fromAbove, 0.5)
                                 + half3(i.normal.w, i.tangent.w, i.binormal.w) * fromBelow * 0.5) * _SkyStrength;
                    half3 light = min(sky + sun, _MaxLight);




                    if (mixWisp > 0.001)
                    {
                        half2 turn = i.onsetShade.xy;
                        half3 wispX = i.tangent.xyz * turn.x + i.binormal.xyz * turn.y;
                        half3 wispY = i.binormal.xyz * turn.x - i.tangent.xyz * turn.y;
                        half2 across = half2(dot(i.local.xy, turn), dot(i.local.xy, half2(-turn.y, turn.x))) / i.local.w;
                        half3 wispLight = SurfaceLight(bw, across, wispX, wispY, i.normal.xyz, 0, i.color.a, 1,
                                                       _WispRelief, _WispBulge, 0, _WispScatter, _WispSunStrength);
                        if (_WispBaked > 0.5)
                        {
                            if (_WispPuffLit > 0.5)
                            {
                                // Lit as the puffs are, as a puff of their
                                // mean six ways' light (the flipbook's, by
                                // opacity), on its baked relief about its
                                // mean (linear, 0.364): by 1.13's light, the
                                // wisps tan and the puffs white, each puff
                                // handing off drew a pale disc with a tan rim.
                                const half4 meanA = half4(0.47, 0.54, 0.70, 0);
                                const half4 meanB = half4(0.48, 0.53, 0.35, 0);
                                half3 flatSun = (sunColor * (1 - _SixShade) * SixWays(meanA, meanB, sunWay) * reach
                                                 + scattered * _SixShade * fill) * _SunStrength;
                                half3 flatSky = (skyAbove * lerp(0.51, SixWays(meanA, meanB, upWay), 0.5)
                                                 + half3(i.normal.w, i.tangent.w, i.binormal.w) * SixWays(meanA, meanB, -upWay) * 0.5) * _SkyStrength;
                                wispLight = min(flatSky + flatSun, _MaxLight) * bw.rgb * (_WispPuffLight / 0.364);
                            }
                            else
                            {
                                // 1.13's: the sky from above and the sun
                                // wrapped round the particle.
                                half sunUp = saturate(_WorldSpaceLightPos0.y * 0.5 + 0.5);
                                wispLight = min(i.shape.xyz + _LightColor0.rgb * sunUp * 0.7, 1.4) * _WispBakedLight * bw.rgb;
                            }
                        }
                        light = lerp(light, wispLight, mixWisp);
                    }
                    light = lerp(light, dot(light, half3(0.299, 0.587, 0.114)), _Grey);
                    fixed4 puffColor;
                    puffColor.rgb = albedo * light;
                    puffColor.a = opacity;
                    #ifdef SOFTPARTICLES_ON
                    float puffZ = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.projPos)));
                    puffColor.a *= saturate(_InvFade * (puffZ - i.projPos.z));
                    #endif
                    if (_NearFadeTo > _NearFadeFrom)
                        puffColor.a *= saturate((distance(i.world.xyz, _WorldSpaceCameraPos) - _NearFadeFrom) / (_NearFadeTo - _NearFadeFrom));
                    // Development: the opacity, magnified, in red, to find faint
                    // coverage; below 0, the wisp's way on the screen (red right,
                    // green up, blue left or down).
                    if (_DebugAlpha > 0)
                        return fixed4(1, 0, 0, saturate(puffColor.a * _DebugAlpha));
                    // Below -2.5: red how far it is a wisp, green the wisp's
                    // density, blue the wisp's frame blend.
                    if (_DebugAlpha < -2.5)
                        return fixed4(wispy, bw.a, i.world.w, 1);
                    if (_DebugAlpha < -1.5)
                        return fixed4(atEdge, 0, 1 - atEdge, saturate(puffColor.a * 4));
                    if (_DebugAlpha < 0)
                        return fixed4(saturate(i.onsetShade.x), saturate(i.onsetShade.y), saturate(-i.onsetShade.x - i.onsetShade.y), saturate(puffColor.a * 4));
                    UNITY_APPLY_FOG(i.fogCoord, puffColor);
                    return puffColor;
                }

                fixed4 tex = tex2D(_MainTex, i.texcoord.xy);
                // Thick steam billows once it has rolled up off the jet (its
                // onset); as it thins it turns the wisps that fray (both the
                // flipbook's layout).
                if (_Billow > 0)
                {
                    fixed4 billows = tex2D(_BillowTex, i.texcoord.xy);
                    tex = lerp(tex, billows, _Billow * i.onsetShade.x * smoothstep(_BillowFrom, _BillowFull, i.color.a));
                }
                half3 lit;
                if (_Surface > 0.5)
                {
                    // The surface: the texture's normal and a soft ball under
                    // the particle, by where in its frame this is.
                    half2 across = (frac(i.texcoord.xy * _Tiles) - 0.5) * 2;
                    // The plume as one volume here: its outward way and shade,
                    // the newest blended in over the one before.
                    half4 volume = half4(0.5, 0.5, 0.5, 0);
                    if (_Volume > 0)
                    {
                        float4 at = float4(i.world.xyz, 1);
                        volume = lerp(tex3D(_VolumePrev, mul(_VolumePrevMap, at).xyz),
                                      tex3D(_VolumeTex, mul(_VolumeMap, at).xyz), _VolumeBlend);
                    }
                    half3 outward = mul((float3x3)_VolumeToWorld, volume.rgb * 2 - 1) * (_Volume * _VolumeShape);
                    // And where it sits in the plume: out from the plume's
                    // middle, so the plume's top and sunward side are lit as
                    // one rounded mass and its underside is in shade.
                    outward += i.shape.xyz * (i.shape.w * _PlumeShape);
                    // The sun's light reaching the particle past the plume between.
                    half sunReach = (1 - i.onsetShade.y * _SunShadow) * (1 - volume.a * _Volume);
                    lit = SurfaceLight(tex, across, i.tangent.xyz, i.binormal.xyz, normalize(i.normal.xyz), outward, i.color.a, sunReach,
                                       _Relief, _Bulge, _Flat, _Scatter, _SunStrength);
                }
                else
                {
                    // Sky light from above, plus the sun wrapped around the puff.
                    half3 sky = ShadeSH9(half4(0, 1, 0, 1)) * _SkyStrength;
                    half sunUp = saturate(_WorldSpaceLightPos0.y * 0.5 + 0.5);
                    half3 sun = _LightColor0.rgb * sunUp * _SunStrength;
                    lit = min(sky + sun, _MaxLight) * tex.rgb;
                }

                // The light greyed toward its brightness (night's blue cast).
                lit = lerp(lit, dot(lit, half3(0.299, 0.587, 0.114)), _Grey);
                fixed4 col;
                col.rgb = i.color.rgb * lit;
                // Cut the thin density as the particle thins; its overall
                // coverage stays about what an even fade would give.
                half cut = _Fray * (1 - i.color.a);
                half strands = saturate((tex.a - cut) / max(1 - cut, 0.05));
                col.a = strands * pow(i.color.a, 1 - _Fray);




                half darkSmoke = 1 - smoothstep(0.2, 0.45, dot(i.color.rgb, half3(0.299, 0.587, 0.114)));
                col.a *= 1 - darkSmoke;

                #ifdef SOFTPARTICLES_ON
                float sceneZ = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.projPos)));
                col.a *= saturate(_InvFade * (sceneZ - i.projPos.z));
                #endif
                // Smoke right at the camera fades, so a view inside the plume
                // is a fog, not a wall of the particles nearest.
                if (_NearFadeTo > _NearFadeFrom)
                    col.a *= saturate((distance(i.world.xyz, _WorldSpaceCameraPos) - _NearFadeFrom) / (_NearFadeTo - _NearFadeFrom));

                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
