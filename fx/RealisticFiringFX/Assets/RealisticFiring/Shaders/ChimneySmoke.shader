// Chimney smoke for Realistic Firing: an alpha-blended particle lit by the
// scene's sky and sun, so smoke darkens at dusk and under cloud. The texture
// carries density in alpha and baked top-lit relief in RGB; the particle's
// color tints it (white steam, grey haze, dark soot). Soft particles and fog
// follow the game's settings.
Shader "RealisticFiring/ChimneySmoke"
{
    Properties
    {
        _MainTex ("Flipbook (RGB relief, A density)", 2D) = "white" {}
        _InvFade ("Soft particles factor", Range(0.01, 3.0)) = 0.6
        _SunStrength ("Sun strength", Range(0, 2)) = 0.7
        _SkyStrength ("Sky strength", Range(0, 2)) = 1.0
        _MaxLight ("Brightest light", Range(0.5, 3)) = 1.4
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask RGB
        Cull Off
        Lighting Off
        ZWrite Off

        Pass
        {
            Tags { "LightMode"="ForwardBase" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_particles
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _InvFade;
            float _SunStrength;
            float _SkyStrength;
            float _MaxLight;
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                half3 light : TEXCOORD1;
                UNITY_FOG_COORDS(2)
                #ifdef SOFTPARTICLES_ON
                float4 projPos : TEXCOORD3;
                #endif
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                #ifdef SOFTPARTICLES_ON
                o.projPos = ComputeScreenPos(o.vertex);
                COMPUTE_EYEDEPTH(o.projPos.z);
                #endif
                o.color = v.color;
                o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);

                // Sky light from above, plus the sun wrapped around the puff
                // (a cloud of smoke is lit through, not just on one face).
                half3 sky = ShadeSH9(half4(0, 1, 0, 1)) * _SkyStrength;
                half sunUp = saturate(_WorldSpaceLightPos0.y * 0.5 + 0.5);
                half3 sun = _LightColor0.rgb * sunUp * _SunStrength;
                o.light = min(sky + sun, _MaxLight);

                UNITY_TRANSFER_FOG(o, o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.texcoord);
                fixed4 col;
                col.rgb = tex.rgb * i.color.rgb * i.light;
                col.a = tex.a * i.color.a;

                #ifdef SOFTPARTICLES_ON
                float sceneZ = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.projPos)));
                col.a *= saturate(_InvFade * (sceneZ - i.projPos.z));
                #endif

                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }

    Fallback Off
}
