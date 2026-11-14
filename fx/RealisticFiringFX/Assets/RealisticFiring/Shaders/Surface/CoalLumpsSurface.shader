


















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
        _BedUp ("Metres per the bed's unit up", Float) = 1
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

        float _CrackScale;
        float _GlowStrength;
        half _FireLightScale;
        float _Pulse;
        float _BedUp;

        struct Input
        {
            float4 state;
            float3 lump;
            // x: its height on its rock; y: metres above the coal's surface;
            // z: where its place on the bed falls in the fresh coal's patches.
            float3 rockY;
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
            o.rockY = float3(v.texcoord3.y, (v.vertex.y * present - v.texcoord2.z) * _BedUp, CoalFreshPatch(anchor));
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
            // Each flat face lit as it lies. The rock's vertex normals are
            // averaged over its faces, so its cut faces shaded smooth and the
            // pile drew as rounded pebbles. (The glow below still runs
            // smoothly over the rock by its height on it: by each face's
            // slope it drew the pile as orange and black tiles.)
            float3 facet = cross(ddy(IN.worldPos), ddx(IN.worldPos));
            float facetLength2 = dot(facet, facet);
            half3 rounded = normalize(IN.worldNormal);
            half3 n = facetLength2 > 1e-24 ? facet * rsqrt(facetLength2) : rounded;

            // Fresh coal: the lumps where the zone's green share covers the
            // bed (in patches, as shovelfuls land, a little ragged lump by
            // lump), each catching as that share falls past it (all at once,
            // lumps switched from black to lit between two frames).
            half fresh = CoalFreshCover(green, lerp(IN.rockY.z, seed, 0.1));
            half own = frac(seed * 7.13);

            // Its broken surface, in its own frame: a ridged mottling for its
            // color, smooth patches for its glow (the ridged noise in the
            // glow drew a net of bright cells over each lump), and a fine
            // grain that roughens each face (a face shaded dead flat read as
            // cut paper).
            half4 mottled = tex3D(_Noise, IN.lump * _CrackScale + seed * 17.0);
            half mottle = mottled.r;
            // (Broad: a patch or two across a lump. Finer, they drew as a
            // net over a white-hot lump.)
            half4 patches = tex3D(_Noise, IN.lump * (_CrackScale * 0.4) + seed * 23.0);
            half3 grain = tex3D(_Noise, IN.lump * (_CrackScale * 4.3) + seed * 31.0).gba - 0.5;

            // Its fractures: coal parts along its cleats and bedding into
            // small blocks, so each broad face is a field of them, a little
            // out of plane with one another (the council: "broad
            // uninterrupted facets", "smaller fracture structure"). Cells in
            // its own frame, a centimetre or two across and thinner along
            // the bedding, their walls bent out of line by the mottling's
            // noise; faded out where a cell is under a dozen pixels and gone
            // under four (two to five pixels across, each with its own
            // brightness, they were a sandpaper grain over the lumps from
            // the door: the council's "stipple" on the coal). And a few
            // cracks across it, where the ridged noise crests (a line along
            // every cell's wall drew each lump as a net).
            float3 cells = (IN.lump + 0.016 * (mottled.baa - 0.5)) * (float3(50.0, 85.0, 50.0) * (0.8 + 0.5 * own)) + seed * float3(13.1, 7.7, 5.3);
            float3 cellSize = fwidth(cells);
            half fine = saturate(1.5 - 6.0 * max(cellSize.x, max(cellSize.y, cellSize.z)));
            half crack = smoothstep(0.9, 0.97, tex3D(_Noise, IN.lump * (_CrackScale * 0.5) + seed * 11.0).r);
            float3 cellId = floor(cells);
            half3 chip = frac(sin(dot(cellId, float3(12.9898, 78.233, 37.719)) + float3(0.0, 1.7, 3.1)) * 43758.5453);
            n = normalize(n + (0.22 * fine) * (chip - 0.5));

            // How far a point on it looks into the fire rather than out at
            // the box: down among the other lumps, under the pile's surface,
            // it sees only fire, its sides more than what faces up; above the
            // surface its faces look out at the cooler crown and tubes and
            // crust over dark. Its edges and corners, thin and burning from
            // two sides, glow a little through the crust (where the flat
            // face's normal and the rock's rounded one part). The footage:
            // dark caps, their edges lit, over bright gaps.
            // (Its glow's edge ragged by the fractures, a block in and the
            // next out: straight across each face it drew as a smooth ramp.)
            half deep = saturate((0.014 * (chip.z - 0.5) * fine - IN.rockY.y) / 0.04);
            half lid = saturate(dot(rounded, _FireGrateUp.xyz));
            half edge = smoothstep(0.12, 0.4, 1.0 - abs(dot(n, rounded)));
            // And the cracks through its crust show the heat under it.
            // (What lies deep but faces up still looks out through its gap:
            // the small lumps down there orange on top, bright at their
            // sides, so the gaps are not one even glow.)
            half cavity = saturate(deep * (0.55 + 0.75 * (1.0 - lid)) + 0.14 * edge + 0.3 * crack);
            cavity = cavity * cavity * (3.0 - 2.0 * cavity);

            // A burning lump: its foot at the heat of the fire between the
            // lumps, far hotter than its zone's top however slowly it burns;
            // its cap a cooler crust, a dark red-brown on a slow fire, which
            // heats through only as the zone nears its full heat, and never
            // quite to the gaps' heat: a working bed's lumps still show,
            // orange caps over yellow-white gaps, as the footage's do. (Heated
            // through from 0.55 of the zone's heat, a working bed drew as
            // cream shards on white; from 0.7, to 0.85 of it, as lumps
            // glowing orange all over, where the footage's working bed keeps
            // its caps dark. Only past 0.9, a fire driven flat out, do they
            // heat through, for its glare.)
            half hot = smoothstep(0.9, 1.0, heat);
            half core = FireBedCore(heat);
            half cap = heat * lerp(_FireBed.y * (0.8 + 0.4 * own), 0.85, hot);
            half t = lerp(cap, core, cavity);
            // Fresh coal stays black, a hard shape against the fire: only
            // what of it lies deep in the gaps glows. (Glowing up its sides,
            // each black lump wore a smudged halo.)
            half3 glow = FireColor(t) * (1.0 - fresh)
                + FireColor(core) * (fresh * smoothstep(0.8, 1.0, deep) * 0.5);
            // Uneven in patches, as its brightness, not as its heat: the
            // ramp steepens so toward white that a few percent of heat either
            // way drew a hot lump as a net of white cells on orange.
            glow *= (0.75 + 0.5 * patches.b) * lerp(1.0, 0.8 + 0.4 * chip.y, fine * (1.0 - cavity));
            glow *= _GlowStrength * (1.0 + 0.5 * _Pulse) * (1.0 - 0.7 * ash) * saturate(heat * 20.0);

            // Coke is a matte dark grey-brown; fresh coal black; ash a pale
            // grey, lying on what faces up.
            half3 albedo = lerp(half3(0.05, 0.043, 0.037), half3(0.014, 0.014, 0.014), fresh);
            albedo *= (0.88 + 0.24 * mottle) * lerp(1.0, 0.88 + 0.24 * chip.x, fine) * (1.0 - 0.5 * crack);
            // Coke is porous: pitted dark, finely, where coal's breaks are clean.
            albedo *= 1.0 - (1.0 - fresh) * 0.25 * smoothstep(0.1, 0.4, grain.x);

            // Ash on some lumps and not others, as much of the pile as its
            // zone's ash; and a film of it on most burned lumps once they
            // stop glowing (a dead fire's coke lay as clean grey blocks).
            // Soft and powdery: each lump its own amount, more on what faces
            // up and lying in its cracks and between its fracture blocks,
            // thinning unevenly across it by the broad patches. (As one
            // hard-edged blotch pattern on every lump it read as camouflage.)
            half ashen = saturate((max(ash * 1.5, 0.75 * (1.0 - saturate(heat * 20.0))) - frac(seed * 3.71)) * 2.5);
            half settled = (0.25 + 0.75 * lid) * (0.45 + 0.55 * smoothstep(0.2, 0.8, patches.b)) + 0.5 * crack;
            half dusted = saturate(ashen * (1.0 - fresh) * (0.35 + 0.65 * frac(seed * 9.17)) * settled);
            albedo = lerp(albedo, half3(0.11, 0.1, 0.09) * (0.85 + 0.3 * mottle), dusted);
            o.Albedo = albedo;
            // Rough coke scatters the light off each face; fresh coal's
            // breaks are smoother.
            half rough = lerp(0.16, 0.08, fresh);
            half3 facing = FacingCamera(normalize(n + rough * grain), IN.worldPos);
            // Down among the other lumps less of the box's light reaches it.
            half open = lerp(0.3, 1.0, 1.0 - saturate((0.012 - IN.rockY.y) / 0.045));
            // Highlights off the faces: fresh coal's fractures glossy, in
            // bright and dull bands along its bedding (vitrain and durain);
            // burned coke dull, ash not at all.
            half band = smoothstep(0.35, 0.65, tex3D(_Noise, float3(seed * 5.0, IN.lump.y * _CrackScale * 2.5, seed * 3.0)).g);
            half gloss = lerp(0.05, 0.4 + 0.3 * band, fresh) * (1.0 - dusted) * (1.0 - crack) * lerp(1.0, 2.0 * chip.x * chip.x, fine);
            // Fresh coal lies on top in the flames' light and shows by it,
            // a dark shape with lit edges and faces: at the burning coal's
            // share of that light it drew as a flat black cutout.
            half shown = lerp(1.0, 5.0, fresh);
            half3 fireGlint = 0;
            half3 doorGlint = 0;
            if (gloss > 0.01 && _FireDebug < 0.5)
            {
                half3 view = normalize(_WorldSpaceCameraPos.xyz - IN.worldPos);
                // A broken face of coal is a little curved (conchoidal) and
                // rippled, so its highlight is a broken spot sweeping across
                // it: from a normal between the flat face's and the rock's
                // rounded one, shaken a little by the grain (more, and a
                // flat-out fire's light sparkled on fresh coal like sand).
                // By the flat face alone each face flashed whole, a white
                // triangle.
                half3 glossy = normalize(facing + (dot(rounded, facing) < 0.0 ? -rounded : rounded) + 0.08 * grain);
                fireGlint = gloss * open * _FireLightScale * FireGlint(IN.worldPos, glossy, view);
                doorGlint = gloss * open * DoorGlint(IN.worldPos, glossy, view);
            }
            o.Emission = FireSurface(glow + fireGlint, albedo, FireLight(IN.worldPos, facing) * (_FireLightScale * open * shown), DoorLight(IN.worldPos, facing) * open)
                + DoorExposed(doorGlint);
            o.Alpha = 1;
        }
        ENDCG
    }

    FallBack Off
}
