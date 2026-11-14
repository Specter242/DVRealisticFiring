// The starting fuel on the grate for Realistic Firing: as StartingFuelSurface,
// but lit by the fire alone, as the coal and walls are (FireLight.cginc):
// the bed's glow, the flames, and the cab's light through the door.
// Its exported vertex and fragment version (Shaders/) undefines
// UNITY_COLORSPACE_GAMMA before Unity's includes in every pass: carry that
// over when exporting it again (SurfaceExport).
Shader "RealisticFiring/StartingFuelFireSurface"
{
    Properties
    {
        _MainTex ("Albedo (RGB), smoothness (A)", 2D) = "white" {}
        _Noise ("Char noise (R broad, G fine)", 2D) = "grey" {}
        _Char ("Surface char", Range(0, 1)) = 0
        _Glow ("Ember glow", Range(0, 1)) = 0
        _Scorch ("Scorch", Range(0, 1)) = 0
        _Oil ("Oil soaked", Range(0, 1)) = 0
        _Seed ("Pattern seed", Range(0, 1)) = 0
        _Smoothness ("Smoothness", Range(0, 1)) = 0.12
        _FireLightScale ("Fire light", Float) = 0.08
        _GlowStrength ("Glow strength", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf FireOnly noambient noforwardadd nolightmap nodynlightmap nodirlightmap nometa addshadow
        #pragma target 3.0
        #include "Assets/RealisticFiring/Shaders/FireLight.cginc"
        #include "Assets/RealisticFiring/Shaders/StartingFuel.cginc"

        half _FireLightScale;
        half _GlowStrength;

        struct Input
        {
            float2 uv_MainTex;
            float4 color : COLOR;
            float3 worldPos;
            float3 worldNormal;
        };

        void surf(Input IN, inout SurfaceOutput o)
        {
            FuelSurface s = StartingFuelSurface(IN.uv_MainTex, IN.color.r);
            half3 n = FacingCamera(normalize(IN.worldNormal), IN.worldPos);
            o.Albedo = s.albedo;
            o.Emission = FireSurface(s.glow * 3.0 * _GlowStrength, s.albedo, FireLight(IN.worldPos, n) * _FireLightScale, DoorLight(IN.worldPos, n));
            o.Alpha = 1;
        }
        ENDCG
    }

    FallBack Off
}
