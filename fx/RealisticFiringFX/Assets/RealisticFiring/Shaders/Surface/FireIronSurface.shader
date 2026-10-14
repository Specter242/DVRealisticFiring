// The fire iron for Realistic Firing: a forged bar, its texture laid out
// along U from the ring handle (0) to the blade's tip (1). Lit like any
// metal in the cab; the end that went into the fire glows from its tip back,
// dull red to orange, as hot as _Heat (0 cold, 1 just out of a white fire),
// the glow shrinking toward the tip as it cools.
Shader "RealisticFiring/FireIronSurface"
{
    Properties
    {
        _MainTex ("Albedo (RGB), smoothness (A)", 2D) = "white" {}
        _BumpMap ("Hammer marks", 2D) = "bump" {}
        _Metallic ("Metallic", Range(0, 1)) = 0.55
        _Heat ("Heat of the working end", Range(0, 1)) = 0
        _HotLength ("Share of the bar that can glow, from the tip", Range(0.01, 0.5)) = 0.18
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0

        sampler2D _MainTex;
        sampler2D _BumpMap;
        half _Metallic;
        half _Heat;
        half _HotLength;

        struct Input
        {
            float2 uv_MainTex;
        };

        half3 IronGlow(half t)
        {
            half3 dull = half3(0.45, 0.03, 0.01);
            half3 cherry = half3(0.85, 0.12, 0.02);
            half3 orange = half3(1.0, 0.38, 0.06);
            half3 c = lerp(half3(0, 0, 0), dull, saturate(t * 3.0));
            c = lerp(c, cherry, saturate(t * 3.0 - 1.0));
            return lerp(c, orange, saturate(t * 3.0 - 2.0));
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            half4 c = tex2D(_MainTex, IN.uv_MainTex);
            o.Albedo = c.rgb;
            o.Smoothness = c.a;
            o.Metallic = _Metallic;
            o.Normal = UnpackNormal(tex2D(_BumpMap, IN.uv_MainTex));

            // From the tip back: hottest at the tip, the glowing length
            // shrinking as it cools.
            half fromTip = saturate((1.0 - IN.uv_MainTex.x) / _HotLength);
            half hot = saturate(_Heat * 1.25 - fromTip);
            o.Emission = IronGlow(hot) * hot * 3.0;
        }
        ENDCG
    }

    FallBack "Diffuse"
}
