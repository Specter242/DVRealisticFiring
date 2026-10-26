// The firebox's own walls, crown, and tube plate for Realistic Firing, lit
// only by the fire (FireLight.cginc): the stock shell's texture, dark except
// where the bed and the flames light it, in place of the cab's material,
// which the game's fire made glow evenly. Holes in the texture's alpha (the
// tube plate's tubes) are cut out.
Shader "RealisticFiring/FireboxWallSurface"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _Color ("Color", Color) = (1, 1, 1, 1)
        _BumpMap ("Normal map", 2D) = "bump" {}
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0
        _FireLightScale ("Fire light", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        Cull Off

        CGPROGRAM
        #pragma surface surf FireOnly noambient noforwardadd nolightmap nodynlightmap nodirlightmap nometa addshadow
        #pragma target 3.0
        #include "Assets/RealisticFiring/Shaders/FireLight.cginc"

        sampler2D _MainTex;
        sampler2D _BumpMap;
        fixed4 _Color;
        half _Cutoff;
        half _FireLightScale;

        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
            float3 worldNormal;
            INTERNAL_DATA
        };

        void surf(Input IN, inout SurfaceOutput o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            clip(c.a - _Cutoff);
            o.Normal = UnpackNormal(tex2D(_BumpMap, IN.uv_MainTex));
            half3 n = FacingCamera(normalize(WorldNormalVector(IN, o.Normal)), IN.worldPos);
            o.Albedo = c.rgb;
            o.Emission = FireSeen(FireLit(c.rgb, FireLight(IN.worldPos, n) * _FireLightScale + DoorLight(IN.worldPos, n)));
            o.Alpha = 1;
        }
        ENDCG
    }

    FallBack Off
}
