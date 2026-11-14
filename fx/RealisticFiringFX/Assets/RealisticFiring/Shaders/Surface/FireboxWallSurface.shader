// The firebox's own walls, crown, and tube plate for Realistic Firing, lit
// only by the fire (FireLight.cginc): the stock shell's texture, dark except
// where the bed and the flames light it, in place of the cab's material,
// which the game's fire made glow evenly. Holes in the texture's alpha (the
// tube plate's tubes) are cut out.
// The brick arch takes it too (the mod's BrickArch, _Arch 1): its own mesh,
// each brick's tone and each face's heat in its vertex colors, glowing by
// its heat as well as lit; and the sheets over the arch stand in its shadow.
Shader "RealisticFiring/FireboxWallSurface"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _Color ("Color", Color) = (1, 1, 1, 1)
        _BumpMap ("Normal map", 2D) = "bump" {}
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0
        _FireLightScale ("Fire light", Float) = 1
        _Staybolts ("Staybolt heads on the sheets (0 none)", Range(0, 1)) = 1
        _Soot ("Soot up the sheets and on the crown (0 none)", Range(0, 1)) = 1
        _Arch ("The brick arch (1): tinted and heated by its vertex colors", Float) = 0
        _ArchHeat ("The bricks' heat, 0 to 1 on the fire's ramp", Range(0, 1)) = 0
        _ArchGlow ("The bricks' glow by their heat", Float) = 0.6
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
        half _Staybolts;
        half _Soot;
        half _Arch;
        half _ArchHeat;
        half _ArchGlow;
        // The brick arch's underside in the world, for its shadow: its corner
        // at the lip (w 1 with an arch), its edges across the box and down
        // to the tube plate (w one over each one's length squared), its
        // face's way up (w 1 while it shadows what is over it), and the
        // radiance its glowing bricks give on average.
        float4 _FireArchCorner;
        float4 _FireArchAcross;
        float4 _FireArchAlong;
        float4 _FireArchUp;
        float4 _FireArchGlow;

        // Staybolt heads, m: a firebox's inner sheets are held to its outer
        // ones by rows of them about 4 in apart each way, each riveted over
        // to a low dome about an inch across (the footage: rows of them
        // catching the fire's light up the side sheets).
        #define STAY_PITCH 0.1
        #define STAY_RADIUS 0.012

        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
            float3 worldNormal;
            fixed4 color : COLOR;
            INTERNAL_DATA
        };

        // How much of a source's light reaches a point over the arch's
        // underside: none where the way to the source passes down through
        // the bricks, softly at the lip (the sources are broad).
        half PastArch(float3 p, float3 toSource)
        {
            float fall = min(dot(toSource, _FireArchUp.xyz), -1e-4);
            float t = dot(_FireArchCorner.xyz - p, _FireArchUp.xyz) / fall;
            float3 hit = p + toSource * t - _FireArchCorner.xyz;
            float a = dot(hit, _FireArchAcross.xyz) * _FireArchAcross.w;
            float b = dot(hit, _FireArchAlong.xyz) * _FireArchAlong.w;
            // (Past the bricks' foot the way down is through the throat
            // sheet or the tube plate they spring from.)
            half blocked = step(t, 1.0) * step(-0.02, a) * step(a, 1.02) * smoothstep(-0.2, 0.08, b);
            return 1.0 - blocked;
        }

        // The glowing bricks' own light on a point under them, over pi, as
        // FireLight's from a zone of the bed: from three patches of the
        // underside, the lip's to the foot's. (Without it the throat sheet
        // under the arch's foot, between the bed and the glowing bricks,
        // was the darkest steel in the box: the council.)
        half3 ArchLight(float3 p, half3 n)
        {
            float area = length(cross(_FireArchAcross.xyz, _FireArchAlong.xyz)) / 3.0;
            half light = 0;
            UNITY_UNROLL
            for (int i = 0; i < 3; i++)
            {
                float3 toPatch = _FireArchCorner.xyz + 0.5 * _FireArchAcross.xyz + ((i + 0.5) / 3.0) * _FireArchAlong.xyz - p;
                float patch2 = max(dot(toPatch, toPatch), 1e-4);
                float3 l = toPatch * rsqrt(patch2);
                light += area * saturate(dot(n, l)) * saturate(dot(l, _FireArchUp.xyz)) / (UNITY_PI * patch2 + area);
            }
            return _FireArchGlow.rgb * light;
        }

        // And on the sheets right beside them: where the bricks meet a side
        // sheet or the sheet they spring from, it is lit by the glowing
        // brick against it, a hand's breadth out. (The S060's arch hung in
        // a black box, nothing showing where it met its sheets: the
        // council.)
        half3 ArchBeside(float3 p, half3 n)
        {
            float3 q = p - _FireArchCorner.xyz;
            float a = saturate(dot(q, _FireArchAcross.xyz) * _FireArchAcross.w);
            float b = saturate(dot(q, _FireArchAlong.xyz) * _FireArchAlong.w);
            float3 toBrick = a * _FireArchAcross.xyz + b * _FireArchAlong.xyz - q;
            float away2 = dot(toBrick, toBrick);
            half facing = saturate(0.4 + 0.6 * dot(n, toBrick * rsqrt(max(away2, 1e-6))));
            return _FireArchGlow.rgb * (facing / (1.0 + away2 / 0.02));
        }

        // FireLight for a point over the arch: each zone's glow and flames
        // as far as they are seen past it.
        half3 FireLightPastArch(float3 p, half3 n)
        {
            half3 light = 0;
            UNITY_UNROLL
            for (int i = 0; i < 9; i++)
            {
                float3 toZone = _FireZones[i].xyz - p;
                float zone2 = max(dot(toZone, toZone), 1e-4);
                float3 l = toZone * rsqrt(zone2);
                half facing = saturate(dot(n, l));
                half emitting = FIRE_SIDE_GLOW + (1.0 - FIRE_SIDE_GLOW) * saturate(dot(_FireGrateUp.xyz, -l));
                float area = _FireZones[i].w;
                light += _FireZoneLight[i].rgb * (area * facing * emitting / (UNITY_PI * zone2 + area)) * PastArch(p, toZone);

                float3 toFlame = _FireFlames[i].xyz - p;
                float flame2 = max(dot(toFlame, toFlame), 1e-4);
                half flameFacing = saturate(dot(n, toFlame * rsqrt(flame2)));
                float radius2 = _FireFlames[i].w * _FireFlames[i].w;
                light += _FireFlameLight[i].rgb * (flameFacing / (flame2 + radius2)) * PastArch(p, toFlame);
            }
            return light * _FireGrateUp.w;
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            clip(c.a - _Cutoff);
            // The arch's tubes take the firebrick's texture blurred, for
            // scale's unevenness without the brick's pits and cracks (they
            // drew as cracked tubes).
            bool tube = _Arch > 0.5 && IN.color.a < 0.01;
            if (tube)
                c = tex2Dlod(_MainTex, float4(IN.uv_MainTex, 0, 4)) * _Color;
            // The arch's bricks, each its own tone, and its dark tubes; how
            // hot each spot of a brick glows is in its texture's alpha.
            // Its bevels, sides, and top darker than its faces while it is
            // cool, by the share of the heat they run at; the hotter the
            // arch, the less (cooler and darker, they framed each hot face
            // as a light panel: the council).
            half hotSpot = c.a;
            half hard = smoothstep(0.5, 0.9, _ArchHeat);
            if (_Arch > 0.5)
                c.rgb *= IN.color.rgb * (IN.color.a < 0.01 ? 1.0 : lerp(0.45 + 0.55 * IN.color.a, 1.0, hard));
            o.Normal = tube ? UnpackNormal(tex2Dlod(_BumpMap, float4(IN.uv_MainTex, 0, 4))) : UnpackNormal(tex2D(_BumpMap, IN.uv_MainTex));
            half3 n = FacingCamera(normalize(WorldNormalVector(IN, o.Normal)), IN.worldPos);

            // Where the point lies in the bed's frame, m, and which sheet it
            // is on, by the way the sheet itself faces: a side sheet, the
            // crown, or the door's sheet (the tube plate, at the bed's far
            // end, has its tubes).
            float3 across = _FireBedAcross.xyz;
            float3 up = _FireGrateUp.xyz;
            float3 along = _FireBedAlong.xyz;
            float3 rel = IN.worldPos - _FireBedOrigin.xyz;
            float3 local = float3(dot(rel, across), dot(rel, up), dot(rel, along));
            half3 flat = normalize(WorldNormalVector(IN, half3(0, 0, 1)));
            half3 faces = abs(half3(dot(flat, across), dot(flat, up), dot(flat, along)));
            half occlusion = 1.0;
            if (_Staybolts > 0.0 && _FireBedAcross.w > 0.0)
            {
                float2 grid = local.zy;
                float3 gridU = along;
                float3 gridV = up;
                half sheet = faces.x;
                if (faces.y > faces.x && faces.y > faces.z)
                {
                    grid = local.xz;
                    gridU = across;
                    gridV = along;
                    sheet = faces.y;
                }
                else if (faces.z > faces.x)
                {
                    grid = local.xy;
                    gridU = across;
                    gridV = up;
                    sheet = faces.z * step(local.z, 0.5 * _FireBedAlong.w);
                }
                float2 cells = grid / STAY_PITCH;
                float2 size = fwidth(cells);
                // Only on a sheet about square to the box, and while a head
                // is a few pixels across.
                half shown = _Staybolts * smoothstep(0.8, 0.95, sheet) * saturate((0.2 - max(size.x, size.y)) / 0.12);
                if (shown > 0.0)
                {
                    float2 off = (frac(cells) - 0.5) * STAY_PITCH;
                    float r = length(off) / STAY_RADIUS;
                    // A low dome: its face leans outward from its middle, so
                    // the fire below lights its lower side; and a dark line
                    // round it, where it meets the sheet.
                    half dome = shown * smoothstep(1.0, 0.85, r);
                    float3 lean = (gridU * off.x + gridV * off.y) / STAY_RADIUS;
                    // (More of the dome and less of the line: drawn mostly
                    // by the line, the heads read as rings stamped in the
                    // sheet.)
                    n = normalize(n + (1.7 * dome) * lean);
                    occlusion *= 1.0 - 0.22 * shown * smoothstep(0.85, 1.0, r) * smoothstep(1.25, 1.05, r);
                }
            }

            // Dark where the coal lies against the sheet: the lumps keep the
            // fire's light off it there, and under the coal's surface the
            // sheet is not seen lit. The coal's surface here from the zones',
            // the nearer the more.
            if (_FireGrateUp.w > 0.0)
            {
                float height = 0.0;
                float weights = 0.0;
                UNITY_UNROLL
                for (int i = 0; i < 9; i++)
                {
                    float3 toZone = _FireZones[i].xyz - IN.worldPos;
                    float above = dot(toZone, up);
                    float3 level = toZone - up * above;
                    float weight = 1.0 / (dot(level, level) + 0.01);
                    height += weight * above;
                    weights += weight;
                }
                float aboveCoal = -height / weights;
                occlusion *= lerp(0.35, 1.0, smoothstep(-0.01, 0.07, aboveCoal));
                // Soot: thicker up the sheets and on the crown, away from
                // the fire that burns it off, in uneven patches (the sheet's
                // own texture, read coarse, for their shape).
                half patchy = tex2D(_MainTex, IN.uv_MainTex * 0.31 + 0.17).g;
                half soot = _Soot * smoothstep(0.15, 0.9, aboveCoal) * smoothstep(0.15, 0.75, patchy + 0.25);
                c.rgb *= 1.0 - 0.55 * soot;
            }

            o.Albedo = c.rgb;
            // Over the arch's underside the fire is seen only past its lip.
            // Under it, its glowing bricks light the sheets too; and its
            // tubes, hanging between the rows, take the bricks' glow on
            // their sides (lit from below alone they drew as flat strips).
            half3 fire;
            half3 beside = 0;
            half over = dot(IN.worldPos - _FireArchCorner.xyz, _FireArchUp.xyz);
            // (The arch's own faces take the fire as it falls on them: a
            // crowned arch's bricks stand over their own underside's
            // plane, and were shadowed by it.)
            // (Its shadow comes on over the first hand's breadth above that
            // plane: at once, it drew a crisp line along a side sheet.)
            if (_Arch < 0.5 && _FireArchCorner.w > 0.5 && _FireArchUp.w > 0.5 && over > 0.15)
                fire = FireLightPastArch(IN.worldPos, n);
            else if (_Arch < 0.5 && _FireArchCorner.w > 0.5 && _FireArchUp.w > 0.5 && over > 0.03)
                fire = lerp(FireLight(IN.worldPos, n), FireLightPastArch(IN.worldPos, n), smoothstep(0.03, 0.15, over));
            else
                fire = FireLight(IN.worldPos, n);
            if (_Arch < 0.5 && _FireArchCorner.w > 0.5)
            {
                if (over <= 0.03)
                    fire += ArchLight(IN.worldPos, n);
                beside = ArchBeside(IN.worldPos, n);
            }
            if (_Arch > 0.5 && IN.color.a < 0.01)
                fire += _FireArchGlow.rgb * (0.35 * saturate(1.0 - abs(dot(n, _FireArchUp.xyz))));
            // The bricks' own glow by their heat (each corner's share of it in
            // the vertex's alpha: the underside the hottest, the tubes none),
            // uneven over each brick as its texture's alpha has it (evenly,
            // each row of bricks drew as one flat orange panel); less so
            // the hotter, where the ramp runs steep and the same unevenness
            // drew as bright blotches.
            half3 glow = 0;
            if (_Arch > 0.5)
            {
                // (The hotter, the nearer every face and spot runs to the
                // arch's own heat: toward white the ramp is steep, and a
                // brick's cooler sides and darker spots drew as orange
                // frames and blotches round pale panels: the council.)
                half uneven = lerp(0.3, 0.03, hard);
                half share = 1.0 - (1.0 - IN.color.a) * lerp(1.0, 0.1, hard);
                half brickHeat = _ArchHeat * share * (1.0 + uneven * (2.0 * hotSpot - 1.0));
                // Toward white with little blue, as the flames' own white
                // (FireboxVolume): white brick behind the fire's smoke, which
                // dims green and blue more than red, drew pink.
                half white = smoothstep(_FireWhiteHeat, 1.0, brickHeat);
                glow = FireColor(brickHeat) * half3(1.0, 1.0 - 0.1 * white, 1.0 - 0.6 * white) * _ArchGlow * step(0.01, IN.color.a);
            }
            // (The sheet beside the bricks by their light at a hand's
            // breadth, over no darker a steel than a tenth: by the walls'
            // own share of the fire's light over the S060's near-black
            // sheets, nothing showed where its arch met them.)
            glow += max(c.rgb, 0.12) * beside * (1.5 * occlusion);
            o.Emission = FireSurface(glow, c.rgb, fire * (_FireLightScale * occlusion), DoorLight(IN.worldPos, n) * occlusion);
            // No bluer than the brick itself, however bright: a white-hot
            // fire's light on pale brick clipped to white, and white behind
            // the fire's smoke, which dims green and blue more than red,
            // drew pink.
            // (The hotter the less: at 0.7 of the green, the brick's own
            // buff, a flat-out fire's bricks were still peach.)
            if (_Arch > 0.5 && _FireDebug < 0.5)
                o.Emission.b = min(o.Emission.b, lerp(0.7, 0.42, smoothstep(0.5, 0.9, _ArchHeat)) * o.Emission.g);
            o.Alpha = 1;
        }
        ENDCG
    }

    FallBack Off
}
