















Shader "RealisticFiring/CoalLumpsSurface"
{
    Properties
    {
        _Noise ("Crack noise (R ridged)", 3D) = "" {}
        _LumpZones ("Zones: height, heat, green, ash", 2D) = "black" {}
        _BedMin ("The bed's box, low corner (its units)", Vector) = (0, 0, 0, 0)
        _BedMax ("The bed's box, high corner (its units)", Vector) = (1, 1, 1, 0)
        _DoorSlope ("The bed's slope down to the door, share of its length", Float) = 0.07
        _BedRelief ("The bed's relief: swells, heaps, the heaps' freshness", 2D) = "black" {}
        _CrackScale ("Mottling per m", Float) = 20
        _GlowStrength ("Glow strength", Float) = 1
        _FireLightScale ("Fire light", Float) = 0.5
        _Pulse ("Exhaust beat pulse", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf FireOnly vertex:vert noambient noforwardadd nolightmap nodynlightmap nodirlightmap nometa addshadow
        #pragma target 3.0
        #include "Assets/RealisticFiring/Shaders/FireLight.cginc"
        #include "Assets/RealisticFiring/Shaders/CoalSurface.cginc"

        sampler3D _Noise;
        float _CrackScale;
        float _GlowStrength;
        half _FireLightScale;
        float _Pulse;

        struct Input
        {
            float4 state;
            float3 lump;
            float rockY;
            float seed;
            float3 worldPos;
            float3 worldNormal;
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            float2 anchor = v.texcoord1.xy;
            float4 zone;
            float3 at = CoalSurfacePoint(anchor, zone);
            // Where no coal lies, the lumps shrink away into the grate.
            float present = saturate(zone.r * 20.0);
            // Its own frame for its mottling, m: its point on the unit rock (UV3) by its size.
            o.lump = v.texcoord3.xyz * v.texcoord2.y;
            o.rockY = v.texcoord3.y;
            v.vertex.xyz = at + v.vertex.xyz * present - float3(0, v.texcoord2.z, 0);
            o.state = zone;
            o.seed = v.texcoord2.x;
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            half heat = IN.state.g;
            half green = IN.state.b;
            half ash = IN.state.a;
            half seed = IN.seed;
            half3 n = normalize(IN.worldNormal);

            // Fresh coal: the lumps whose seed falls under the zone's green share.
            half fresh = step(seed, green);
            // Each burning lump its own heat about the zone's.
            half own = frac(seed * 7.13);
            half t = saturate(heat * (0.6 + 0.4 * own)) * (1.0 - fresh);
            // As the zone heats toward white, its burning lumps heat through.
            // Keeping their random cooler share at full heat left most of
            // the pile dark even when the fire between them was white hot.
            half hot = FireHeatThrough(heat);
            t = lerp(t, heat * (1.0 - fresh), hot);

            // Its ash film and cooler patches, smooth over it (cracks cut from
            // the noise drew as flat yellow patches stuck on the lumps).
            half mottle = tex3D(_Noise, IN.lump * _CrackScale + seed * 17.0).r;
            // Its foot, low on the rock, meets the gaps' fire; its cap is
            // furthest from it.
            half low = saturate(0.5 - IN.rockY * 1.2);
            // A burning lump glows through as it heats: hardly on a slow fire,
            // all over a working one, as hot coke does in the footage, its cap
            // a darker crust (dark caps over orange feet on a slower fire).
            half body = 0.4 * saturate((t - 0.45) * 2.0) * (0.45 + 0.55 * low) * (0.5 + 0.9 * mottle);
            // The cooler crust remains below white heat; at full heat the
            // cap glows too, with slight mottling. Fresh lumps stay black.
            body = lerp(body, (0.7 + 0.3 * mottle) * (1.0 - fresh), hot);
            // Its foot glows in the gaps' fire; green coal shows it round its
            // edges first.
            half margin = low * low * saturate(heat * 1.6) * lerp(0.55, 0.8, fresh);
            // Cooler lumps are red-orange. At white heat both their bodies
            // and feet reach the same end of the ramp as the gaps.
            half3 glow = FireColor(t * lerp(0.7, 1.0, hot)) * body + FireColor(heat * lerp(0.6, 1.0, hot)) * margin;
            glow *= _GlowStrength * (1.0 + 0.5 * _Pulse) * (1.0 - 0.7 * ash);

            half3 albedo = lerp(half3(0.05, 0.045, 0.04), half3(0.022, 0.021, 0.02), fresh);
            albedo = lerp(albedo, half3(0.38, 0.36, 0.33), saturate(ash * 1.2) * (1.0 - fresh));
            o.Albedo = albedo;
            half3 facing = FacingCamera(n, IN.worldPos);
            o.Emission = FireSeen(glow + FireLit(albedo, FireLight(IN.worldPos, facing) * _FireLightScale + DoorLight(IN.worldPos, facing)));
            o.Alpha = 1;
        }
        ENDCG
    }

    FallBack Off
}
