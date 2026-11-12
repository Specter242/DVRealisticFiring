// The starting fuel in the cab and the world for Realistic Firing: wood,
// rags, their crate and tin, lit like any prop (StartingFuel.cginc). Vertex
// colour R carries the bodies' baked occlusion.
// Its exported vertex and fragment version (Shaders/) undefines
// UNITY_COLORSPACE_GAMMA before Unity's includes in every pass: carry that
// over when exporting it again (SurfaceExport).
Shader "RealisticFiring/StartingFuelSurface"
{
    Properties
    {
        _MainTex ("Albedo (RGB), smoothness (A)", 2D) = "white" {}
        _BumpMap ("Normal map", 2D) = "bump" {}
        _Noise ("Char noise (R broad, G fine)", 2D) = "grey" {}
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Char ("Surface char", Range(0, 1)) = 0
        _Glow ("Ember glow", Range(0, 1)) = 0
        _Scorch ("Scorch", Range(0, 1)) = 0
        _Oil ("Oil soaked", Range(0, 1)) = 0
        _Seed ("Pattern seed", Range(0, 1)) = 0
        _Smoothness ("Smoothness", Range(0, 1)) = 0.12
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        Cull Back

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0
        #include "Assets/RealisticFiring/Shaders/StartingFuel.cginc"

        sampler2D _BumpMap;
        half _Metallic;

        struct Input
        {
            float2 uv_MainTex;
            float4 color : COLOR;
        };

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            FuelSurface s = StartingFuelSurface(IN.uv_MainTex, IN.color.r);
            o.Albedo = s.albedo;
            o.Smoothness = s.smoothness;
            o.Metallic = _Metallic;
            o.Normal = UnpackNormal(tex2D(_BumpMap, IN.uv_MainTex));
            o.Emission = s.glow * 2.5;
        }
        ENDCG
    }

    FallBack "Diffuse"
}
