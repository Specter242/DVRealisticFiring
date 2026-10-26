// A rooted, narrow steam stream. Noise travels down its length; its body
// stays continuous, with no separate particles or round flipbook puffs.
Shader "RealisticFiring/StokerJet"
{
    Properties
    {
        _NoiseTex ("Flow noise", 2D) = "gray" {}
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask RGB
        Cull Off
        ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile __ RF_SINGLE_PASS_STEREO
            #if defined(RF_SINGLE_PASS_STEREO)
            #define UNITY_SINGLE_PASS_STEREO 1
            #endif
            #include "HLSLSupport.cginc"
            #include "UnityCG.cginc"

            sampler2D _NoiseTex;
            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 flow : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 flow : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.uv;
                o.flow = v.flow;
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float along = i.uv.y;
                float clock = _Time.y * i.flow.x + i.flow.y;
                float noise = tex2D(_NoiseTex, float2(i.uv.x * .7, along * 2 - clock * 5)).r;
                float fine = tex2D(_NoiseTex, float2(i.uv.x * 1.7 + .31, along * 5 - clock * 9)).g;
                float center = (i.uv.x * 2 - 1) + (noise - .5) * .35 * along;
                float edge = 1 - smoothstep(.2, 1, abs(center));
                float tip = 1 - smoothstep(.5, 1, along);
                // The moving texture varies density, but never cuts the jet
                // into packets. It fades smoothly into the hot surrounding gas.
                float density = .55 + .3 * noise + .15 * fine;
                return float4(i.color.rgb, i.color.a * edge * tip * density);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
