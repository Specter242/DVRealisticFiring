// Flames for Realistic Firing: upright cards standing on the fire bed, each
// turned about the firebox's up axis to face the camera, drawing licking
// tongues from scrolled noise: yellow-white at the roots, orange, red at the
// ragged tips. The mod writes each card's corner in UV1 (x across -0.5..0.5,
// y up 0..1, in m) and its state in the vertex color: R how hard its zone
// burns (taller, brighter), G its green coal (lazier, oranger, smokier), B a
// random seed, A its height in m; UV2 x how many tongues it carries (1 for
// a single tongue; a sheet carries a row of them over a continuous band of
// flame) and y its width as a multiple of a tongue's. _Draft leans the flames toward the front
// (the firebox's +z) and quickens them; _Pulse is the last exhaust beat's
// surge, which stretches them up and draws them forward.
Shader "RealisticFiring/FlameCard"
{
    Properties
    {
        _NoiseTex ("Fire noise (R broad, G fine)", 2D) = "gray" {}
        _Intensity ("Intensity", Range(0, 12)) = 1.6
        _Speed ("Lick speed", Range(0, 4)) = 1.2
        _Draft ("Draft", Range(0, 1)) = 0
        _Pulse ("Exhaust beat pulse", Range(0, 1)) = 0
        _InvFade ("Soft root factor", Range(0.01, 10)) = 4.0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Blend SrcAlpha One
        ColorMask RGB
        Cull Off
        Lighting Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_particles
            // Single-pass stereo on the mod's own keyword: Unity strips its own
            // from a bundle built without VR, and the mod turns this on for stereo cameras.
            #pragma multi_compile __ RF_SINGLE_PASS_STEREO
            #if defined(RF_SINGLE_PASS_STEREO)
            #define UNITY_SINGLE_PASS_STEREO 1
            #endif
            // HLSL, not CG: CG includes these before the define above.
            #include "HLSLSupport.cginc"
            #include "UnityCG.cginc"

            sampler2D _NoiseTex;
            float _Intensity;
            float _Speed;
            float _Draft;
            float _Pulse;
            float _InvFade;
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 corner : TEXCOORD1;
                float2 sheet : TEXCOORD2;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 projPos : TEXCOORD1;
                float tongues : TEXCOORD2;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float3 center = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0)).xyz;
                float3 up = normalize(mul((float3x3)unity_ObjectToWorld, float3(0, 1, 0)));
                float3 front = normalize(mul((float3x3)unity_ObjectToWorld, float3(0, 0, 1)));
                // Turned toward the middle of the two eyes in VR, so both see one card.
                #if defined(USING_STEREO_MATRICES)
                float3 toCamera = 0.5 * (unity_StereoWorldSpaceCameraPos[0] + unity_StereoWorldSpaceCameraPos[1]) - center;
                #else
                float3 toCamera = _WorldSpaceCameraPos - center;
                #endif
                float3 right = normalize(cross(up, toCamera));

                // Drawn by the draft: narrower, leaning forward, and shorter, as a
                // forced fire's flames are; each beat stretches them for a moment.
                float drawn = saturate(_Draft + _Pulse);
                float height = v.color.a * (1.0 - 0.35 * _Draft + 0.35 * _Pulse);
                float width = (1.0 - 0.35 * drawn);
                float y = v.corner.y;
                float3 world = center
                    + right * v.corner.x * width * max(0.06, v.color.a * 0.5) * max(1.0, v.sheet.y)
                    + up * y * height
                    + front * y * y * height * (0.15 + 1.1 * drawn);
                o.vertex = UnityWorldToClipPos(world);
                o.color = v.color;
                o.uv = float2(v.corner.x + 0.5, y);
                o.tongues = max(1.0, v.sheet.x);
                o.projPos = ComputeScreenPos(o.vertex);
                COMPUTE_EYEDEPTH(o.projPos.z);
                return o;
            }

            // Flame colors: white-yellow roots, yellow, orange, red ragged tips.
            half3 FlameColor(half f)
            {
                half3 c = lerp(half3(0.6, 0.08, 0.01), half3(1.0, 0.38, 0.05), saturate(f * 2.0));
                c = lerp(c, half3(1.0, 0.72, 0.25), saturate(f * 2.0 - 0.8));
                return lerp(c, half3(1.0, 0.92, 0.7), saturate(f * 3.0 - 2.2));
            }

            fixed4 frag(v2f i) : SV_Target
            {
                half burn = i.color.r;
                half green = i.color.g;
                half seed = i.color.b * 17.0;
                float drawn = saturate(_Draft + _Pulse);
                float t = _Time.y * _Speed * (0.6 + 1.6 * drawn) * (1.0 - 0.4 * green);

                // A sheet is a row of tongues, each in its own lane, of its own
                // height; a single card is one lane.
                float2 uv = i.uv;
                float lanes = i.tongues;
                float lane = floor(uv.x * lanes);
                float lu = frac(uv.x * lanes);
                half laneSeed = seed + lane * 0.37;
                half reach = lanes > 1.5 ? 0.5 + 0.6 * tex2D(_NoiseTex, float2(laneSeed, t * 0.04)).r : 1.0;
                float lv = uv.y / reach;

                // Two noises scrolled upward, the finer faster: ragged licking tongues.
                half broad = tex2D(_NoiseTex, float2(uv.x * 1.8 * lanes + seed, uv.y * 0.6 - t * 0.55)).r;
                half fine = tex2D(_NoiseTex, float2(uv.x * 4.2 * lanes + seed * 1.3, uv.y * 1.5 - t * 1.25)).g;
                half turbulence = broad * 0.65 + fine * 0.5;

                // A tongue rooted at its lane's foot, swaying more the higher it reaches.
                half sway = (tex2D(_NoiseTex, float2(laneSeed * 3.1 + 0.3, lv * 0.35 - t * 0.3)).r - 0.5) * 0.9 * saturate(lv);
                half across = saturate(1.0 - abs(lu - 0.5 - sway) * 2.0);
                half shape = pow(across, 2.2) * pow(saturate(1.0 - lv), 0.8);
                // A sheet's tongues rise from one continuous band of flame.
                half band = lanes > 1.5 ? saturate(1.0 - uv.y * 3.5) * saturate(min(uv.x, 1.0 - uv.x) * lanes * 1.2) * 0.75 : 0.0;
                half f = saturate(max(shape, band) * 1.5 + turbulence - 1.0 - 0.25 * lv);
                f = saturate(f * 3.0) * saturate(burn * 1.6 + 0.15 * green);

                // Soft where the flames root into the coal.
                float sceneZ = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.projPos)));
                float fade = saturate(_InvFade * (sceneZ - i.projPos.z));

                half3 color = FlameColor(f * lerp(1.0, 0.65, green));
                half alpha = saturate(f * 1.3) * fade;
                return fixed4(color * _Intensity * (0.6 + 0.6 * burn) * (1.0 + 0.4 * _Pulse), alpha);
            }
            ENDHLSL
        }
    }
}
