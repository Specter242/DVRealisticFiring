// Heat shimmer over the stack for Realistic Firing: each particle shows the
// scene behind it, grabbed once a frame, displaced by two layers of tiling
// noise scrolling up through it, so the hot gas leaving the stack bends what
// is seen through it. The particle's alpha is the shimmer's strength; a soft
// round mask fades it to nothing at its edge, where the displaced copy is
// blended back into the undisplaced scene.
Shader "RealisticFiring/HeatHaze"
{
    Properties
    {
        _NoiseTex ("Noise (R broad, G fine)", 2D) = "gray" {}
        _Strength ("Most displacement, share of the view", Range(0, 0.05)) = 0.006
        _Scroll ("Noise rise, tiles a second", Range(0, 4)) = 0.8
        _Tiling ("Noise tiles across a particle", Range(0.1, 4)) = 1.2
    }

    SubShader
    {
        // After every plume and the game's steam (the mod also sorts it at an
        // order above theirs: ChimneyPlume.ShimmerLast).
        Tags { "Queue"="Transparent+1" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }

        // One copy of the screen a frame, taken when the first shimmer is
        // drawn: after all the smoke, so it holds every plume (drawn among
        // the smoke, a farther engine's shimmer took it before a nearer plume
        // was drawn, and laid over that plume it cut its middle away in the
        // shimmer's shape; 0Krait0, Nexus, 2026-10-21). Named for that place,
        // Transparent+1 after all the smoke, so other mods' shimmers drawn
        // there share the one copy (geluk's scheme, TurboTurbo, 2026-10-22).
        GrabPass { "_SharedGrab_Transparent1" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGB
            Cull Off
            Lighting Off
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // Single-pass stereo on the mod's own keyword, as the smoke's.
            #pragma multi_compile __ RF_SINGLE_PASS_STEREO
            #if defined(RF_SINGLE_PASS_STEREO)
            #define UNITY_SINGLE_PASS_STEREO 1
            #endif
            #include "HLSLSupport.cginc"
            #include "UnityCG.cginc"

            sampler2D _SharedGrab_Transparent1;
            sampler2D _NoiseTex;
            float _Strength;
            float _Scroll;
            float _Tiling;

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
                float4 grab : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.grab = ComputeGrabScreenPos(o.vertex);
                o.color = v.color;
                o.texcoord = v.texcoord;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Soft round edge: strongest in the middle, nothing at the rim.
                float2 centered = i.texcoord * 2 - 1;
                half mask = saturate(1 - dot(centered, centered));
                mask *= mask;

                // Two layers of noise rising through it at different rates.
                float rise = _Time.y * _Scroll;
                float2 uv = i.texcoord * _Tiling;
                // Each way a broad and a fine layer, -1 to 1 together.
                half2 bend;
                bend.x = tex2D(_NoiseTex, uv + float2(0.13, -rise)).r
                       + tex2D(_NoiseTex, uv * 2.3 + float2(0.71, -1.7 * rise)).g - 1;
                bend.y = tex2D(_NoiseTex, uv * 1.6 + float2(0.37, -1.3 * rise)).r
                       + tex2D(_NoiseTex, uv * 2.9 + float2(0.53, -2.1 * rise)).g - 1;

                float2 offset = bend * (_Strength * mask * i.color.a);
                #if defined(UNITY_SINGLE_PASS_STEREO)
                // Both eyes share one image twice as wide.
                offset.x *= 0.5;
                #endif
                float2 screen = i.grab.xy / i.grab.w + offset;
                fixed4 col = tex2D(_SharedGrab_Transparent1, screen);
                col.a = mask * saturate(i.color.a * 3);
                return col;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
