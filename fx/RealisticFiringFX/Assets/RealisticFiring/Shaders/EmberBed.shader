// The glowing layer under the coal for Realistic Firing: the hot embers seen
// through the gaps between the dark lumps on top. The mod writes the same
// state in the vertex color as for the coal (R heat, G green coal, B ash,
// A clinker); the embers glow by heat, mottled by the coal texture's lump
// hotness and a slow wavering noise, in the fire's colors as the eye sees
// them (FireColor.cginc), reaching white heat with the coal above them.
// With lump geometry, all three layers share the same surface and heat.
// UV0 is the bed's frame (across, along, 0..1).
Shader "RealisticFiring/EmberBed"
{
    Properties
    {
        _CoalTex ("Coal lumps (R hotness, G height, B grain)", 2D) = "white" {}
        _NoiseTex ("Fire noise (R broad, G fine)", 2D) = "gray" {}
        _Tiling ("Lumps per bed width", Float) = 2.0
        _GlowStrength ("Glow strength", Range(0, 12)) = 0.7
        _FlickerSpeed ("Flicker speed", Range(0, 4)) = 0.6
        _Pulse ("Exhaust beat pulse", Range(0, 1)) = 0
        _UnderLumps ("Use the lumps' surface", Float) = 0
        _UnderDrop ("Depth under that surface (its units)", Float) = 0
        _UnderFloor ("Lowest ember surface (its units)", Float) = 0
        _LumpZones ("Zones: height, heat, green, ash", 2D) = "black" {}
        _BedRelief ("The bed's relief: swells, heaps, freshness", 2D) = "black" {}
        _BedMin ("The bed's box, low corner", Vector) = (0, 0, 0, 0)
        _BedMax ("The bed's box, high corner", Vector) = (1, 1, 1, 0)
        _DoorSlope ("The bed's slope down to the door", Float) = 0.07
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fog
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
            #include "Assets/RealisticFiring/Shaders/CoalSurface.cginc"

            sampler2D _CoalTex;
            sampler2D _NoiseTex;
            float _Tiling;
            float _GlowStrength;
            float _FlickerSpeed;
            float _Pulse;
            float _UnderLumps;
            float _UnderDrop;
            float _UnderFloor;

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
            };

            v2f vert(appdata v)
            {
                v2f o;
                if (_UnderLumps > 0.5)
                {
                    float4 zone;
                    v.vertex.y = max(_UnderFloor, CoalSurfacePoint(v.uv, zone).y - _UnderDrop);
                    v.color.rgb = zone.gba;
                }
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.uv;
                UNITY_TRANSFER_FOG(o, o.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                half heat = i.color.r;
                half ash = i.color.b;
                float2 uv = i.uv * _Tiling * 1.7;
                half hot = tex2D(_CoalTex, uv).r;
                float t = _Time.y * _FlickerSpeed;
                half broad = tex2D(_NoiseTex, i.uv * 1.3 + float2(t * 0.05, t * 0.09)).r;
                half fine = tex2D(_NoiseTex, uv * 0.8 - float2(t * 0.11, t * 0.03)).g;
                // Embers glow even under a slow fire; ash dulls them.
                half glowHeat = saturate((0.25 + 0.75 * heat) * (0.85 + 0.3 * hot) * (0.9 + 0.2 * fine));
                glowHeat = lerp(glowHeat, heat, FireHeatThrough(heat));
                half3 c = FireColor(glowHeat) * (0.45 + 0.9 * broad) * (1.0 - 0.7 * ash);
                // The ramp brightens with the heat itself; a little more as it
                // is forced, and with each beat.
                c *= _GlowStrength * (0.6 + 0.4 * heat * heat) * (1.0 + 0.5 * _Pulse);
                // Nothing glows where nothing burns: a dead zone is black, while
                // a slow fire (heat 0.2 and up) keeps its full glow.
                c *= saturate(heat * 20.0);
                fixed4 col = fixed4(FireExposed(c), 1.0);
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDHLSL
        }
    }
}
