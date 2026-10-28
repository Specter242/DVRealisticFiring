// The fire bed for Realistic Firing: coal lumps lit only by the fire
// (FireLight.cginc), glowing where the fire burns. The mod writes each
// vertex's state in its color: R the fire's heat there (0 black, through
// dull red, orange, and yellow, to 1 white-hot), G how much green coal shows
// (dark lumps lying on the fire, lit from below at their sides), B ash
// (grey, a dull glow under it), A clinker (a dark glassy crust). A live fire
// glows first deep in the gaps between lumps, then over the hottest lumps
// (each lump has its own hotness), until a white-hot fire glows all over;
// the glow wavers with a scrolling noise, faster under draft, and jumps with
// each exhaust beat (_Pulse). UV0 is the bed's own frame (across, along,
// 0..1), for the shovel's aim. UV3.x is where no coal lies (0 to 1): the
// bare grate shows there, iron bars along the box with dark gaps between
// them, under any ash left on it, and nothing glows.
Shader "RealisticFiring/CoalBedSurface"
{
    Properties
    {
        _CoalTex ("Coal lumps (R hotness, G height, B grain)", 2D) = "white" {}
        _NormalTex ("Coal relief", 2D) = "bump" {}
        _NormalStrength ("Relief strength", Range(0, 2)) = 1.0
        _NoiseTex ("Fire noise (R broad, G fine)", 2D) = "gray" {}
        _Tiling ("Lumps per bed width", Float) = 3.0
        _CoalColor ("Coal", Color) = (0.035, 0.032, 0.03, 1)
        _AshColor ("Ash", Color) = (0.42, 0.40, 0.37, 1)
        _ClinkerColor ("Clinker", Color) = (0.09, 0.06, 0.05, 1)
        _GrateColor ("Grate bars", Color) = (0.055, 0.052, 0.05, 1)
        _GrateBars ("Grate bars across the box", Float) = 22.0
        _GlowStrength ("Glow strength", Range(0, 12)) = 1.0
        _Pulse ("Exhaust beat pulse", Range(0, 1)) = 0
        _FlickerSpeed ("Flicker speed", Range(0, 4)) = 0.6
        _AimRect ("Aimed zone (across0, along0, across1, along1)", Vector) = (0, 0, 0, 0)
        _AimColor ("Aim color (alpha = strength)", Color) = (0.25, 0.6, 1, 0)
        _FireLightScale ("Fire light", Float) = 1
        _UnderLumps ("Under lumps: the deeper coal between them, whole", Range(0, 1)) = 0
        _UnderDrop ("Under lumps: how far under the coal's surface it lies (its units)", Float) = 0
        _LumpZones ("Zones: height, heat, green, ash", 2D) = "black" {}
        _BedRelief ("The bed's relief: swells, heaps, freshness", 2D) = "black" {}
        _BedMin ("The bed's box, low corner", Vector) = (0, 0, 0, 0)
        _BedMax ("The bed's box, high corner", Vector) = (1, 1, 1, 0)
        _DoorSlope ("The bed's slope down to the door", Float) = 0.07
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

        sampler2D _CoalTex;
        sampler2D _NormalTex;
        float _NormalStrength;
        sampler2D _NoiseTex;
        float _Pulse;
        float _Tiling;
        fixed4 _CoalColor;
        fixed4 _AshColor;
        fixed4 _ClinkerColor;
        fixed4 _GrateColor;
        float _GrateBars;
        float _GlowStrength;
        float _FlickerSpeed;
        float4 _AimRect;
        fixed4 _AimColor;
        half _FireLightScale;
        half _UnderLumps;
        float _UnderDrop;

        struct Input
        {
            float2 bed;
            float4 state;
            float bare;
            float3 worldPos;
            float3 worldNormal;
            INTERNAL_DATA
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.bed = v.texcoord.xy;
            o.state = v.color;
            o.bare = v.texcoord3.x;
            if (_UnderLumps > 0.5)
            {
                // Follow the lumps' surface, without a separate coarse
                // height field or heat jitter crossing through them.
                float4 zone;
                v.vertex.y = CoalSurfacePoint(o.bed, zone).y - _UnderDrop;
                o.state.rgb = zone.gba;
            }
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            half heat = IN.state.r;
            half green = IN.state.g;
            half ash = IN.state.b;
            half clinker = IN.state.a;

            float2 uv = IN.bed * _Tiling;
            half4 lumps = tex2D(_CoalTex, uv);
            half hot = lumps.r;
            half height = lumps.g;
            half grain = lumps.b;

            // Green coal lies on the fire as whole lumps of its own, the
            // coolest first, sitting higher over the gaps.
            half fresh = saturate((green * 1.1 - hot) * 10.0);

            // Gaps through the lumps where the fire burns, showing the embers
            // below; green coal, ash, and clinker close them.
            half opening = saturate(heat * 1.4) * (1.0 - 0.4 * fresh) * (1.0 - 0.3 * green) * (1.0 - 0.8 * ash) * (1.0 - clinker);
            // Under the lumps (CoalLumps) the bed is the deeper coal between
            // them, whole: its own flat lumps, cut open to the embers, drew as
            // flat yellow polygons and an orange mosaic between and through them.
            half gapLevel = (0.62 + 0.12 * saturate((heat - 0.7) / 0.3)) * opening;
            clip(height - gapLevel * (1.0 - _UnderLumps) + _UnderLumps);

            // An empty grate: its bars, with the gaps between them open, where
            // no ash covers them.
            half bars = saturate(IN.bare * 2.0 - 0.5) * (1.0 - saturate(ash * 1.5));
            half gap = step(0.6, frac(IN.bed.x * _GrateBars));
            clip(0.5 - bars * gap);

            half3 relief = UnpackNormal(tex2D(_NormalTex, uv));
            o.Normal = normalize(lerp(half3(0, 0, 1), relief, _NormalStrength * (1.0 - 0.6 * ash)));

            float t = _Time.y * _FlickerSpeed;
            half broad = tex2D(_NoiseTex, IN.bed * 1.5 + float2(0.0, t * 0.11)).r;
            half fine = tex2D(_NoiseTex, uv * 0.7 + float2(t * 0.07, -t * 0.19)).g;
            half waver = 0.75 + 0.5 * broad * (0.6 + 0.8 * fine);

            half3 albedo = _CoalColor.rgb * (0.7 + 0.5 * grain);
            albedo = lerp(albedo, _AshColor.rgb * (0.8 + 0.3 * grain), ash);
            albedo = lerp(albedo, _ClinkerColor.rgb, clinker);
            albedo = lerp(albedo, _GrateColor.rgb * (0.85 + 0.3 * grain), bars);
            o.Albedo = albedo;
            o.Normal = normalize(lerp(o.Normal, half3(0, 0, 1), bars));

            // The lumps on top stay dark, lit from below at their edges by the
            // embers in the gaps, until the fire is hot enough to light them
            // too: the hottest lumps first, and all of them when it is forced.
            // Green lumps stay black on top, their sides lit by the fire below.
            half edges = saturate((gapLevel + 0.22 - height) / 0.22) * saturate(heat * 2.0);
            // The lumps' tops glow less than the gaps between them, even forced:
            // dark caps over bright gaps, as the footage shows.
            half crowns = 0.4 * saturate((heat - 0.55) / 0.35) * (0.45 + 0.55 * saturate(hot + (heat - 0.9) * 6.0));
            half sides = saturate((0.78 - height) / 0.3) * saturate(heat * 1.5);
            half mask = max(max(edges, crowns) * (1.0 - fresh), 0.7 * sides * fresh);
            // Under the lumps, the deeper coal glows evenly, mottled softly by
            // its own lumps (the texture's hotness, blurred) and the wavering
            // noise, dimmer under green coal.
            half deep = tex2Dbias(_CoalTex, float4(uv, 0, 3)).r;
            half soft = (0.35 + 0.5 * deep + 0.4 * broad * fine) * (1.0 - 0.6 * green);
            mask = lerp(mask, soft, _UnderLumps);
            half local = saturate(heat * (0.75 + 0.25 * hot) * (1.05 - 0.2 * height) + 0.15 * saturate((heat - 0.9) / 0.1));
            half lit = (1.0 - 0.6 * ash) * (1.0 - 0.85 * clinker);
            half forced = heat * heat * heat;
            // Deep coal reaches the same white heat as the lumps above it.
            // Green cover dims it through the mask, without making cold
            // texture patches in an otherwise white-hot layer.
            half through = _UnderLumps * FireHeatThrough(heat);
            half colorHeat = lerp(local * (1.0 - 0.4 * fresh), heat, through);
            half3 glow = FireColor(colorHeat) * mask * lit * waver * (0.4 + 1.2 * heat + 0.8 * forced * forced);
            o.Emission = glow * _GlowStrength * (1.0 + 0.6 * _Pulse) * step(0.001, heat) * (1.0 - saturate(IN.bare * 2.0));

            // The fire's light on the coal, the ash, and the bars: no other light reaches them.
            half3 n = normalize(WorldNormalVector(IN, o.Normal));
            o.Emission = FireSeen(o.Emission + FireLit(albedo, FireLight(IN.worldPos, n) * _FireLightScale + DoorLight(IN.worldPos, n)));

            // The zone a loaded shovel is aimed at.
            float2 inside = step(_AimRect.xy, IN.bed) * step(IN.bed, _AimRect.zw);
            o.Emission += _AimColor.rgb * (_AimColor.a * inside.x * inside.y);
        }
        ENDCG
    }

    FallBack "Diffuse"
}
