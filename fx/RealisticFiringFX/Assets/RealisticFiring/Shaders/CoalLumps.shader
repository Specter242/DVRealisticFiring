





















Shader "RealisticFiring/CoalLumps"
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


	// ------------------------------------------------------------
	// Surface shader code generated out of a CGPROGRAM block:


	// ---- forward rendering base pass:
	Pass {
		Name "FORWARD"
		Tags { "LightMode" = "ForwardBase" }

HLSLPROGRAM
// compile directives
#pragma vertex vert_surf
#pragma fragment frag_surf
#pragma target 3.0
#pragma multi_compile_instancing
#pragma multi_compile_fog
#pragma multi_compile_fwdbase noshadowmask nodynlightmap nodirlightmap nolightmap
// Realistic Firing: single-pass stereo on the mod's own keyword.
#pragma multi_compile __ RF_SINGLE_PASS_STEREO
#if defined(RF_SINGLE_PASS_STEREO)
#define UNITY_SINGLE_PASS_STEREO 1
#endif
#include "HLSLSupport.cginc"
#define UNITY_INSTANCED_LOD_FADE
#define UNITY_INSTANCED_SH
#define UNITY_INSTANCED_LIGHTMAPSTS
#include "UnityShaderVariables.cginc"
#include "UnityShaderUtilities.cginc"
// -------- variant for: <when no other keywords are defined>
#if !defined(INSTANCING_ON)
// Surface shader code generated based on:
// vertex modifier: 'vert'
// writes to per-pixel normal: no
// writes to emission: YES
// writes to occlusion: no
// needs world space reflection vector: no
// needs world space normal vector: YES
// needs screen space position: no
// needs world space position: YES
// needs view direction: no
// needs world space view direction: no
// needs world space position for lighting: YES
// needs world space view direction for lighting: YES
// needs world space view direction for lightmaps: no
// needs vertex color: no
// needs VFACE: no
// needs SV_IsFrontFace: no
// passes tangent-to-world matrix to pixel shader: no
// reads from normal: no
// 0 texcoords actually used
#include "UnityCG.cginc"
//Shader does not support lightmap thus we always want to fallback to SH.
#undef UNITY_SHOULD_SAMPLE_SH
#define UNITY_SHOULD_SAMPLE_SH (!defined(UNITY_PASS_FORWARDADD) && !defined(UNITY_PASS_PREPASSBASE) && !defined(UNITY_PASS_SHADOWCASTER) && !defined(UNITY_PASS_META))
#include "Lighting.cginc"
#include "AutoLight.cginc"

#define INTERNAL_DATA
#define WorldReflectionVector(data,normal) data.worldRefl
#define WorldNormalVector(data,normal) normal

// Original surface shader snippet:
#line 40 ""
#ifdef DUMMY_PREPROCESSOR_TO_WORK_AROUND_HLSL_COMPILER_LINE_HANDLING
#endif
/* UNITY: Original start of shader */
        //#pragma surface surf FireOnly vertex:vert noambient noforwardadd nolightmap nodynlightmap nodirlightmap nometa addshadow
        //#pragma target 3.0
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


// vertex-to-fragment interpolation data
// no lightmaps:
#ifndef LIGHTMAP_ON
// half-precision fragment shader registers:
#ifdef UNITY_HALF_PRECISION_FRAGMENT_SHADER_REGISTERS
#define FOG_COMBINED_WITH_WORLD_POS
struct v2f_surf {
  UNITY_POSITION(pos);
  float3 worldNormal : TEXCOORD0;
  float4 worldPos : TEXCOORD1;
  float4 custompack0 : TEXCOORD2; // state
  float4 custompack1 : TEXCOORD3; // lump seed
  float3 custompack2 : TEXCOORD4; // rockY
  #if UNITY_SHOULD_SAMPLE_SH
  half3 sh : TEXCOORD5; // SH
  #endif
  UNITY_LIGHTING_COORDS(6,7)
  UNITY_VERTEX_INPUT_INSTANCE_ID
  UNITY_VERTEX_OUTPUT_STEREO
};
#endif
// high-precision fragment shader registers:
#ifndef UNITY_HALF_PRECISION_FRAGMENT_SHADER_REGISTERS
struct v2f_surf {
  UNITY_POSITION(pos);
  float3 worldNormal : TEXCOORD0;
  float3 worldPos : TEXCOORD1;
  float4 custompack0 : TEXCOORD2; // state
  float4 custompack1 : TEXCOORD3; // lump seed
  float3 custompack2 : TEXCOORD4; // rockY
  #if UNITY_SHOULD_SAMPLE_SH
  half3 sh : TEXCOORD5; // SH
  #endif
  UNITY_FOG_COORDS(6)
  UNITY_SHADOW_COORDS(7)
  UNITY_VERTEX_INPUT_INSTANCE_ID
  UNITY_VERTEX_OUTPUT_STEREO
};
#endif
#endif
// with lightmaps:
#ifdef LIGHTMAP_ON
// half-precision fragment shader registers:
#ifdef UNITY_HALF_PRECISION_FRAGMENT_SHADER_REGISTERS
#define FOG_COMBINED_WITH_WORLD_POS
struct v2f_surf {
  UNITY_POSITION(pos);
  float3 worldNormal : TEXCOORD0;
  float4 worldPos : TEXCOORD1;
  float4 custompack0 : TEXCOORD2; // state
  float4 custompack1 : TEXCOORD3; // lump seed
  float3 custompack2 : TEXCOORD4; // rockY
  float4 lmap : TEXCOORD5;
  UNITY_LIGHTING_COORDS(6,7)
  UNITY_VERTEX_INPUT_INSTANCE_ID
  UNITY_VERTEX_OUTPUT_STEREO
};
#endif
// high-precision fragment shader registers:
#ifndef UNITY_HALF_PRECISION_FRAGMENT_SHADER_REGISTERS
struct v2f_surf {
  UNITY_POSITION(pos);
  float3 worldNormal : TEXCOORD0;
  float3 worldPos : TEXCOORD1;
  float4 custompack0 : TEXCOORD2; // state
  float4 custompack1 : TEXCOORD3; // lump seed
  float3 custompack2 : TEXCOORD4; // rockY
  float4 lmap : TEXCOORD5;
  UNITY_FOG_COORDS(6)
  UNITY_SHADOW_COORDS(7)
  UNITY_VERTEX_INPUT_INSTANCE_ID
  UNITY_VERTEX_OUTPUT_STEREO
};
#endif
#endif

// vertex shader
v2f_surf vert_surf (appdata_full v) {
  UNITY_SETUP_INSTANCE_ID(v);
  v2f_surf o;
  UNITY_INITIALIZE_OUTPUT(v2f_surf,o);
  UNITY_TRANSFER_INSTANCE_ID(v,o);
  UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
  Input customInputData;
  vert (v, customInputData);
  o.custompack0.xyzw = customInputData.state;
  o.custompack1.xyz = customInputData.lump;
  o.custompack2.xyz = customInputData.rockY;
  o.custompack1.w = customInputData.seed;
  o.pos = UnityObjectToClipPos(v.vertex);
  float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
  float3 worldNormal = UnityObjectToWorldNormal(v.normal);
  #if defined(LIGHTMAP_ON) && defined(DIRLIGHTMAP_COMBINED)
  fixed3 worldTangent = UnityObjectToWorldDir(v.tangent.xyz);
  fixed tangentSign = v.tangent.w * unity_WorldTransformParams.w;
  fixed3 worldBinormal = cross(worldNormal, worldTangent) * tangentSign;
  #endif
  #if defined(LIGHTMAP_ON) && defined(DIRLIGHTMAP_COMBINED) && !defined(UNITY_HALF_PRECISION_FRAGMENT_SHADER_REGISTERS)
  o.tSpace0 = float4(worldTangent.x, worldBinormal.x, worldNormal.x, worldPos.x);
  o.tSpace1 = float4(worldTangent.y, worldBinormal.y, worldNormal.y, worldPos.y);
  o.tSpace2 = float4(worldTangent.z, worldBinormal.z, worldNormal.z, worldPos.z);
  #endif
  o.worldPos.xyz = worldPos;
  o.worldNormal = worldNormal;
  #ifdef LIGHTMAP_ON
  o.lmap.xy = v.texcoord1.xy * unity_LightmapST.xy + unity_LightmapST.zw;
  #endif

  // SH/ambient and vertex lights
  #ifndef LIGHTMAP_ON
    #if UNITY_SHOULD_SAMPLE_SH && !UNITY_SAMPLE_FULL_SH_PER_PIXEL
      o.sh = 0;
      // Approximated illumination from non-important point lights
      #ifdef VERTEXLIGHT_ON
        o.sh += Shade4PointLights (
          unity_4LightPosX0, unity_4LightPosY0, unity_4LightPosZ0,
          unity_LightColor[0].rgb, unity_LightColor[1].rgb, unity_LightColor[2].rgb, unity_LightColor[3].rgb,
          unity_4LightAtten0, worldPos, worldNormal);
      #endif
      o.sh = ShadeSHPerVertex (worldNormal, o.sh);
    #endif
  #endif // !LIGHTMAP_ON

  UNITY_TRANSFER_LIGHTING(o,v.texcoord1.xy); // pass shadow and, possibly, light cookie coordinates to pixel shader
  #ifdef FOG_COMBINED_WITH_TSPACE
    UNITY_TRANSFER_FOG_COMBINED_WITH_TSPACE(o,o.pos); // pass fog coordinates to pixel shader
  #elif defined FOG_COMBINED_WITH_WORLD_POS
    UNITY_TRANSFER_FOG_COMBINED_WITH_WORLD_POS(o,o.pos); // pass fog coordinates to pixel shader
  #else
    UNITY_TRANSFER_FOG(o,o.pos); // pass fog coordinates to pixel shader
  #endif
  return o;
}

// fragment shader
fixed4 frag_surf (v2f_surf IN) : SV_Target {
  UNITY_SETUP_INSTANCE_ID(IN);
  // prepare and unpack data
  Input surfIN;
  #ifdef FOG_COMBINED_WITH_TSPACE
    UNITY_EXTRACT_FOG_FROM_TSPACE(IN);
  #elif defined FOG_COMBINED_WITH_WORLD_POS
    UNITY_EXTRACT_FOG_FROM_WORLD_POS(IN);
  #else
    UNITY_EXTRACT_FOG(IN);
  #endif
  UNITY_INITIALIZE_OUTPUT(Input,surfIN);
  surfIN.state.x = 1.0;
  surfIN.lump.x = 1.0;
  surfIN.rockY.x = 1.0;
  surfIN.seed.x = 1.0;
  surfIN.worldPos.x = 1.0;
  surfIN.worldNormal.x = 1.0;
  surfIN.state = IN.custompack0.xyzw;
  surfIN.lump = IN.custompack1.xyz;
  surfIN.rockY = IN.custompack2.xyz;
  surfIN.seed = IN.custompack1.w;
  float3 worldPos = IN.worldPos.xyz;
  #ifndef USING_DIRECTIONAL_LIGHT
    fixed3 lightDir = normalize(UnityWorldSpaceLightDir(worldPos));
  #else
    fixed3 lightDir = _WorldSpaceLightPos0.xyz;
  #endif
  float3 worldViewDir = normalize(UnityWorldSpaceViewDir(worldPos));
  surfIN.worldNormal = IN.worldNormal;
  surfIN.worldPos = worldPos;
  #ifdef UNITY_COMPILER_HLSL
  SurfaceOutput o = (SurfaceOutput)0;
  #else
  SurfaceOutput o;
  #endif
  o.Albedo = 0.0;
  o.Emission = 0.0;
  o.Specular = 0.0;
  o.Alpha = 0.0;
  o.Gloss = 0.0;
  fixed3 normalWorldVertex = fixed3(0,0,1);
  o.Normal = IN.worldNormal;
  normalWorldVertex = IN.worldNormal;

  // call surface function
  surf (surfIN, o);

  // compute lighting & shadowing factor
  UNITY_LIGHT_ATTENUATION(atten, IN, worldPos)
  fixed4 c = 0;

  // Setup lighting environment
  UnityGI gi;
  UNITY_INITIALIZE_OUTPUT(UnityGI, gi);
  gi.indirect.diffuse = 0;
  gi.indirect.specular = 0;
  gi.light.color = _LightColor0.rgb;
  gi.light.dir = lightDir;
  // Call GI (lightmaps/SH/reflections) lighting function
  UnityGIInput giInput;
  UNITY_INITIALIZE_OUTPUT(UnityGIInput, giInput);
  giInput.light = gi.light;
  giInput.worldPos = worldPos;
  giInput.worldViewDir = worldViewDir;
  giInput.atten = atten;
  #if defined(LIGHTMAP_ON) || defined(DYNAMICLIGHTMAP_ON)
    giInput.lightmapUV = IN.lmap;
  #else
    giInput.lightmapUV = 0.0;
  #endif
  #if UNITY_SHOULD_SAMPLE_SH && !UNITY_SAMPLE_FULL_SH_PER_PIXEL
    giInput.ambient = IN.sh;
  #else
    giInput.ambient.rgb = 0.0;
  #endif
  giInput.probeHDR[0] = unity_SpecCube0_HDR;
  giInput.probeHDR[1] = unity_SpecCube1_HDR;
  #if defined(UNITY_SPECCUBE_BLENDING) || defined(UNITY_SPECCUBE_BOX_PROJECTION)
    giInput.boxMin[0] = unity_SpecCube0_BoxMin; // .w holds lerp value for blending
  #endif
  #ifdef UNITY_SPECCUBE_BOX_PROJECTION
    giInput.boxMax[0] = unity_SpecCube0_BoxMax;
    giInput.probePosition[0] = unity_SpecCube0_ProbePosition;
    giInput.boxMax[1] = unity_SpecCube1_BoxMax;
    giInput.boxMin[1] = unity_SpecCube1_BoxMin;
    giInput.probePosition[1] = unity_SpecCube1_ProbePosition;
  #endif
  LightingFireOnly_GI(o, giInput, gi);

  #if UNITY_SHOULD_SAMPLE_SH && !defined(LIGHTMAP_ON)
    gi.indirect.diffuse = 0;
  #endif

  // realtime lighting: call lighting function
  c += LightingFireOnly (o, worldViewDir, gi);
  c.rgb += o.Emission;
  UNITY_APPLY_FOG(_unity_fogCoord, c); // apply fog
  UNITY_OPAQUE_ALPHA(c.a);
  return c;
}


#endif

// -------- variant for: INSTANCING_ON 
#if defined(INSTANCING_ON)
// Surface shader code generated based on:
// vertex modifier: 'vert'
// writes to per-pixel normal: no
// writes to emission: YES
// writes to occlusion: no
// needs world space reflection vector: no
// needs world space normal vector: YES
// needs screen space position: no
// needs world space position: YES
// needs view direction: no
// needs world space view direction: no
// needs world space position for lighting: YES
// needs world space view direction for lighting: YES
// needs world space view direction for lightmaps: no
// needs vertex color: no
// needs VFACE: no
// needs SV_IsFrontFace: no
// passes tangent-to-world matrix to pixel shader: no
// reads from normal: no
// 0 texcoords actually used
#include "UnityCG.cginc"
//Shader does not support lightmap thus we always want to fallback to SH.
#undef UNITY_SHOULD_SAMPLE_SH
#define UNITY_SHOULD_SAMPLE_SH (!defined(UNITY_PASS_FORWARDADD) && !defined(UNITY_PASS_PREPASSBASE) && !defined(UNITY_PASS_SHADOWCASTER) && !defined(UNITY_PASS_META))
#include "Lighting.cginc"
#include "AutoLight.cginc"

#define INTERNAL_DATA
#define WorldReflectionVector(data,normal) data.worldRefl
#define WorldNormalVector(data,normal) normal

// Original surface shader snippet:
#line 40 ""
#ifdef DUMMY_PREPROCESSOR_TO_WORK_AROUND_HLSL_COMPILER_LINE_HANDLING
#endif
/* UNITY: Original start of shader */
        //#pragma surface surf FireOnly vertex:vert noambient noforwardadd nolightmap nodynlightmap nodirlightmap nometa addshadow
        //#pragma target 3.0
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


// vertex-to-fragment interpolation data
// no lightmaps:
#ifndef LIGHTMAP_ON
// half-precision fragment shader registers:
#ifdef UNITY_HALF_PRECISION_FRAGMENT_SHADER_REGISTERS
#define FOG_COMBINED_WITH_WORLD_POS
struct v2f_surf {
  UNITY_POSITION(pos);
  float3 worldNormal : TEXCOORD0;
  float4 worldPos : TEXCOORD1;
  float4 custompack0 : TEXCOORD2; // state
  float4 custompack1 : TEXCOORD3; // lump seed
  float3 custompack2 : TEXCOORD4; // rockY
  #if UNITY_SHOULD_SAMPLE_SH
  half3 sh : TEXCOORD5; // SH
  #endif
  UNITY_LIGHTING_COORDS(6,7)
  UNITY_VERTEX_INPUT_INSTANCE_ID
  UNITY_VERTEX_OUTPUT_STEREO
};
#endif
// high-precision fragment shader registers:
#ifndef UNITY_HALF_PRECISION_FRAGMENT_SHADER_REGISTERS
struct v2f_surf {
  UNITY_POSITION(pos);
  float3 worldNormal : TEXCOORD0;
  float3 worldPos : TEXCOORD1;
  float4 custompack0 : TEXCOORD2; // state
  float4 custompack1 : TEXCOORD3; // lump seed
  float3 custompack2 : TEXCOORD4; // rockY
  #if UNITY_SHOULD_SAMPLE_SH
  half3 sh : TEXCOORD5; // SH
  #endif
  UNITY_FOG_COORDS(6)
  UNITY_SHADOW_COORDS(7)
  UNITY_VERTEX_INPUT_INSTANCE_ID
  UNITY_VERTEX_OUTPUT_STEREO
};
#endif
#endif
// with lightmaps:
#ifdef LIGHTMAP_ON
// half-precision fragment shader registers:
#ifdef UNITY_HALF_PRECISION_FRAGMENT_SHADER_REGISTERS
#define FOG_COMBINED_WITH_WORLD_POS
struct v2f_surf {
  UNITY_POSITION(pos);
  float3 worldNormal : TEXCOORD0;
  float4 worldPos : TEXCOORD1;
  float4 custompack0 : TEXCOORD2; // state
  float4 custompack1 : TEXCOORD3; // lump seed
  float3 custompack2 : TEXCOORD4; // rockY
  float4 lmap : TEXCOORD5;
  UNITY_LIGHTING_COORDS(6,7)
  UNITY_VERTEX_INPUT_INSTANCE_ID
  UNITY_VERTEX_OUTPUT_STEREO
};
#endif
// high-precision fragment shader registers:
#ifndef UNITY_HALF_PRECISION_FRAGMENT_SHADER_REGISTERS
struct v2f_surf {
  UNITY_POSITION(pos);
  float3 worldNormal : TEXCOORD0;
  float3 worldPos : TEXCOORD1;
  float4 custompack0 : TEXCOORD2; // state
  float4 custompack1 : TEXCOORD3; // lump seed
  float3 custompack2 : TEXCOORD4; // rockY
  float4 lmap : TEXCOORD5;
  UNITY_FOG_COORDS(6)
  UNITY_SHADOW_COORDS(7)
  UNITY_VERTEX_INPUT_INSTANCE_ID
  UNITY_VERTEX_OUTPUT_STEREO
};
#endif
#endif

// vertex shader
v2f_surf vert_surf (appdata_full v) {
  UNITY_SETUP_INSTANCE_ID(v);
  v2f_surf o;
  UNITY_INITIALIZE_OUTPUT(v2f_surf,o);
  UNITY_TRANSFER_INSTANCE_ID(v,o);
  UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
  Input customInputData;
  vert (v, customInputData);
  o.custompack0.xyzw = customInputData.state;
  o.custompack1.xyz = customInputData.lump;
  o.custompack2.xyz = customInputData.rockY;
  o.custompack1.w = customInputData.seed;
  o.pos = UnityObjectToClipPos(v.vertex);
  float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
  float3 worldNormal = UnityObjectToWorldNormal(v.normal);
  #if defined(LIGHTMAP_ON) && defined(DIRLIGHTMAP_COMBINED)
  fixed3 worldTangent = UnityObjectToWorldDir(v.tangent.xyz);
  fixed tangentSign = v.tangent.w * unity_WorldTransformParams.w;
  fixed3 worldBinormal = cross(worldNormal, worldTangent) * tangentSign;
  #endif
  #if defined(LIGHTMAP_ON) && defined(DIRLIGHTMAP_COMBINED) && !defined(UNITY_HALF_PRECISION_FRAGMENT_SHADER_REGISTERS)
  o.tSpace0 = float4(worldTangent.x, worldBinormal.x, worldNormal.x, worldPos.x);
  o.tSpace1 = float4(worldTangent.y, worldBinormal.y, worldNormal.y, worldPos.y);
  o.tSpace2 = float4(worldTangent.z, worldBinormal.z, worldNormal.z, worldPos.z);
  #endif
  o.worldPos.xyz = worldPos;
  o.worldNormal = worldNormal;
  #ifdef LIGHTMAP_ON
  o.lmap.xy = v.texcoord1.xy * unity_LightmapST.xy + unity_LightmapST.zw;
  #endif

  // SH/ambient and vertex lights
  #ifndef LIGHTMAP_ON
    #if UNITY_SHOULD_SAMPLE_SH && !UNITY_SAMPLE_FULL_SH_PER_PIXEL
      o.sh = 0;
      // Approximated illumination from non-important point lights
      #ifdef VERTEXLIGHT_ON
        o.sh += Shade4PointLights (
          unity_4LightPosX0, unity_4LightPosY0, unity_4LightPosZ0,
          unity_LightColor[0].rgb, unity_LightColor[1].rgb, unity_LightColor[2].rgb, unity_LightColor[3].rgb,
          unity_4LightAtten0, worldPos, worldNormal);
      #endif
      o.sh = ShadeSHPerVertex (worldNormal, o.sh);
    #endif
  #endif // !LIGHTMAP_ON

  UNITY_TRANSFER_LIGHTING(o,v.texcoord1.xy); // pass shadow and, possibly, light cookie coordinates to pixel shader
  #ifdef FOG_COMBINED_WITH_TSPACE
    UNITY_TRANSFER_FOG_COMBINED_WITH_TSPACE(o,o.pos); // pass fog coordinates to pixel shader
  #elif defined FOG_COMBINED_WITH_WORLD_POS
    UNITY_TRANSFER_FOG_COMBINED_WITH_WORLD_POS(o,o.pos); // pass fog coordinates to pixel shader
  #else
    UNITY_TRANSFER_FOG(o,o.pos); // pass fog coordinates to pixel shader
  #endif
  return o;
}

// fragment shader
fixed4 frag_surf (v2f_surf IN) : SV_Target {
  UNITY_SETUP_INSTANCE_ID(IN);
  // prepare and unpack data
  Input surfIN;
  #ifdef FOG_COMBINED_WITH_TSPACE
    UNITY_EXTRACT_FOG_FROM_TSPACE(IN);
  #elif defined FOG_COMBINED_WITH_WORLD_POS
    UNITY_EXTRACT_FOG_FROM_WORLD_POS(IN);
  #else
    UNITY_EXTRACT_FOG(IN);
  #endif
  UNITY_INITIALIZE_OUTPUT(Input,surfIN);
  surfIN.state.x = 1.0;
  surfIN.lump.x = 1.0;
  surfIN.rockY.x = 1.0;
  surfIN.seed.x = 1.0;
  surfIN.worldPos.x = 1.0;
  surfIN.worldNormal.x = 1.0;
  surfIN.state = IN.custompack0.xyzw;
  surfIN.lump = IN.custompack1.xyz;
  surfIN.rockY = IN.custompack2.xyz;
  surfIN.seed = IN.custompack1.w;
  float3 worldPos = IN.worldPos.xyz;
  #ifndef USING_DIRECTIONAL_LIGHT
    fixed3 lightDir = normalize(UnityWorldSpaceLightDir(worldPos));
  #else
    fixed3 lightDir = _WorldSpaceLightPos0.xyz;
  #endif
  float3 worldViewDir = normalize(UnityWorldSpaceViewDir(worldPos));
  surfIN.worldNormal = IN.worldNormal;
  surfIN.worldPos = worldPos;
  #ifdef UNITY_COMPILER_HLSL
  SurfaceOutput o = (SurfaceOutput)0;
  #else
  SurfaceOutput o;
  #endif
  o.Albedo = 0.0;
  o.Emission = 0.0;
  o.Specular = 0.0;
  o.Alpha = 0.0;
  o.Gloss = 0.0;
  fixed3 normalWorldVertex = fixed3(0,0,1);
  o.Normal = IN.worldNormal;
  normalWorldVertex = IN.worldNormal;

  // call surface function
  surf (surfIN, o);

  // compute lighting & shadowing factor
  UNITY_LIGHT_ATTENUATION(atten, IN, worldPos)
  fixed4 c = 0;

  // Setup lighting environment
  UnityGI gi;
  UNITY_INITIALIZE_OUTPUT(UnityGI, gi);
  gi.indirect.diffuse = 0;
  gi.indirect.specular = 0;
  gi.light.color = _LightColor0.rgb;
  gi.light.dir = lightDir;
  // Call GI (lightmaps/SH/reflections) lighting function
  UnityGIInput giInput;
  UNITY_INITIALIZE_OUTPUT(UnityGIInput, giInput);
  giInput.light = gi.light;
  giInput.worldPos = worldPos;
  giInput.worldViewDir = worldViewDir;
  giInput.atten = atten;
  #if defined(LIGHTMAP_ON) || defined(DYNAMICLIGHTMAP_ON)
    giInput.lightmapUV = IN.lmap;
  #else
    giInput.lightmapUV = 0.0;
  #endif
  #if UNITY_SHOULD_SAMPLE_SH && !UNITY_SAMPLE_FULL_SH_PER_PIXEL
    giInput.ambient = IN.sh;
  #else
    giInput.ambient.rgb = 0.0;
  #endif
  giInput.probeHDR[0] = unity_SpecCube0_HDR;
  giInput.probeHDR[1] = unity_SpecCube1_HDR;
  #if defined(UNITY_SPECCUBE_BLENDING) || defined(UNITY_SPECCUBE_BOX_PROJECTION)
    giInput.boxMin[0] = unity_SpecCube0_BoxMin; // .w holds lerp value for blending
  #endif
  #ifdef UNITY_SPECCUBE_BOX_PROJECTION
    giInput.boxMax[0] = unity_SpecCube0_BoxMax;
    giInput.probePosition[0] = unity_SpecCube0_ProbePosition;
    giInput.boxMax[1] = unity_SpecCube1_BoxMax;
    giInput.boxMin[1] = unity_SpecCube1_BoxMin;
    giInput.probePosition[1] = unity_SpecCube1_ProbePosition;
  #endif
  LightingFireOnly_GI(o, giInput, gi);

  #if UNITY_SHOULD_SAMPLE_SH && !defined(LIGHTMAP_ON)
    gi.indirect.diffuse = 0;
  #endif

  // realtime lighting: call lighting function
  c += LightingFireOnly (o, worldViewDir, gi);
  c.rgb += o.Emission;
  UNITY_APPLY_FOG(_unity_fogCoord, c); // apply fog
  UNITY_OPAQUE_ALPHA(c.a);
  return c;
}


#endif


ENDHLSL

}

	// ---- deferred shading pass:
	Pass {
		Name "DEFERRED"
		Tags { "LightMode" = "Deferred" }

HLSLPROGRAM
// compile directives
#pragma vertex vert_surf
#pragma fragment frag_surf
#pragma target 3.0
#pragma multi_compile_instancing
#pragma exclude_renderers nomrt
#pragma skip_variants FOG_LINEAR FOG_EXP FOG_EXP2
#pragma multi_compile_prepassfinal noshadowmask nodynlightmap nodirlightmap nolightmap
// Realistic Firing: single-pass stereo on the mod's own keyword.
#pragma multi_compile __ RF_SINGLE_PASS_STEREO
#if defined(RF_SINGLE_PASS_STEREO)
#define UNITY_SINGLE_PASS_STEREO 1
#endif
#include "HLSLSupport.cginc"
#define UNITY_INSTANCED_LOD_FADE
#define UNITY_INSTANCED_SH
#define UNITY_INSTANCED_LIGHTMAPSTS
#include "UnityShaderVariables.cginc"
#include "UnityShaderUtilities.cginc"
// -------- variant for: <when no other keywords are defined>
#if !defined(INSTANCING_ON)
// Surface shader code generated based on:
// vertex modifier: 'vert'
// writes to per-pixel normal: no
// writes to emission: YES
// writes to occlusion: no
// needs world space reflection vector: no
// needs world space normal vector: YES
// needs screen space position: no
// needs world space position: YES
// needs view direction: no
// needs world space view direction: no
// needs world space position for lighting: YES
// needs world space view direction for lighting: YES
// needs world space view direction for lightmaps: no
// needs vertex color: no
// needs VFACE: no
// needs SV_IsFrontFace: no
// passes tangent-to-world matrix to pixel shader: no
// reads from normal: YES
// 0 texcoords actually used
#include "UnityCG.cginc"
//Shader does not support lightmap thus we always want to fallback to SH.
#undef UNITY_SHOULD_SAMPLE_SH
#define UNITY_SHOULD_SAMPLE_SH (!defined(UNITY_PASS_FORWARDADD) && !defined(UNITY_PASS_PREPASSBASE) && !defined(UNITY_PASS_SHADOWCASTER) && !defined(UNITY_PASS_META))
#include "Lighting.cginc"

#define INTERNAL_DATA
#define WorldReflectionVector(data,normal) data.worldRefl
#define WorldNormalVector(data,normal) normal

// Original surface shader snippet:
#line 40 ""
#ifdef DUMMY_PREPROCESSOR_TO_WORK_AROUND_HLSL_COMPILER_LINE_HANDLING
#endif
/* UNITY: Original start of shader */
        //#pragma surface surf FireOnly vertex:vert noambient noforwardadd nolightmap nodynlightmap nodirlightmap nometa addshadow
        //#pragma target 3.0
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


// vertex-to-fragment interpolation data
struct v2f_surf {
  UNITY_POSITION(pos);
  float3 worldNormal : TEXCOORD0;
  float3 worldPos : TEXCOORD1;
  float4 custompack0 : TEXCOORD2; // state
  float4 custompack1 : TEXCOORD3; // lump seed
  float3 custompack2 : TEXCOORD4; // rockY
  float4 lmap : TEXCOORD5;
#ifndef LIGHTMAP_ON
  #if UNITY_SHOULD_SAMPLE_SH && !UNITY_SAMPLE_FULL_SH_PER_PIXEL
    half3 sh : TEXCOORD6; // SH
  #endif
#else
  #ifdef DIRLIGHTMAP_OFF
    float4 lmapFadePos : TEXCOORD6;
  #endif
#endif
  UNITY_VERTEX_INPUT_INSTANCE_ID
  UNITY_VERTEX_OUTPUT_STEREO
};

// vertex shader
v2f_surf vert_surf (appdata_full v) {
  UNITY_SETUP_INSTANCE_ID(v);
  v2f_surf o;
  UNITY_INITIALIZE_OUTPUT(v2f_surf,o);
  UNITY_TRANSFER_INSTANCE_ID(v,o);
  UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
  Input customInputData;
  vert (v, customInputData);
  o.custompack0.xyzw = customInputData.state;
  o.custompack1.xyz = customInputData.lump;
  o.custompack2.xyz = customInputData.rockY;
  o.custompack1.w = customInputData.seed;
  o.pos = UnityObjectToClipPos(v.vertex);
  float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
  float3 worldNormal = UnityObjectToWorldNormal(v.normal);
  o.worldPos.xyz = worldPos;
  o.worldNormal = worldNormal;
  o.lmap.zw = 0;
#ifdef LIGHTMAP_ON
  o.lmap.xy = v.texcoord1.xy * unity_LightmapST.xy + unity_LightmapST.zw;
  #ifdef DIRLIGHTMAP_OFF
    o.lmapFadePos.xyz = (mul(unity_ObjectToWorld, v.vertex).xyz - unity_ShadowFadeCenterAndType.xyz) * unity_ShadowFadeCenterAndType.w;
    o.lmapFadePos.w = (-UnityObjectToViewPos(v.vertex).z) * (1.0 - unity_ShadowFadeCenterAndType.w);
  #endif
#else
  o.lmap.xy = 0;
    #if UNITY_SHOULD_SAMPLE_SH && !UNITY_SAMPLE_FULL_SH_PER_PIXEL
      o.sh = 0;
      o.sh = ShadeSHPerVertex (worldNormal, o.sh);
    #endif
#endif
  return o;
}
#ifdef LIGHTMAP_ON
float4 unity_LightmapFade;
#endif
fixed4 unity_Ambient;

// fragment shader
void frag_surf (v2f_surf IN,
    out half4 outGBuffer0 : SV_Target0,
    out half4 outGBuffer1 : SV_Target1,
    out half4 outGBuffer2 : SV_Target2,
    out half4 outEmission : SV_Target3
#if defined(SHADOWS_SHADOWMASK) && (UNITY_ALLOWED_MRT_COUNT > 4)
    , out half4 outShadowMask : SV_Target4
#endif
) {
  UNITY_SETUP_INSTANCE_ID(IN);
  // prepare and unpack data
  Input surfIN;
  #ifdef FOG_COMBINED_WITH_TSPACE
    UNITY_EXTRACT_FOG_FROM_TSPACE(IN);
  #elif defined FOG_COMBINED_WITH_WORLD_POS
    UNITY_EXTRACT_FOG_FROM_WORLD_POS(IN);
  #else
    UNITY_EXTRACT_FOG(IN);
  #endif
  UNITY_INITIALIZE_OUTPUT(Input,surfIN);
  surfIN.state.x = 1.0;
  surfIN.lump.x = 1.0;
  surfIN.rockY.x = 1.0;
  surfIN.seed.x = 1.0;
  surfIN.worldPos.x = 1.0;
  surfIN.worldNormal.x = 1.0;
  surfIN.state = IN.custompack0.xyzw;
  surfIN.lump = IN.custompack1.xyz;
  surfIN.rockY = IN.custompack2.xyz;
  surfIN.seed = IN.custompack1.w;
  float3 worldPos = IN.worldPos.xyz;
  #ifndef USING_DIRECTIONAL_LIGHT
    fixed3 lightDir = normalize(UnityWorldSpaceLightDir(worldPos));
  #else
    fixed3 lightDir = _WorldSpaceLightPos0.xyz;
  #endif
  float3 worldViewDir = normalize(UnityWorldSpaceViewDir(worldPos));
  surfIN.worldNormal = IN.worldNormal;
  surfIN.worldPos = worldPos;
  #ifdef UNITY_COMPILER_HLSL
  SurfaceOutput o = (SurfaceOutput)0;
  #else
  SurfaceOutput o;
  #endif
  o.Albedo = 0.0;
  o.Emission = 0.0;
  o.Specular = 0.0;
  o.Alpha = 0.0;
  o.Gloss = 0.0;
  fixed3 normalWorldVertex = fixed3(0,0,1);
  o.Normal = IN.worldNormal;
  normalWorldVertex = IN.worldNormal;

  // call surface function
  surf (surfIN, o);
fixed3 originalNormal = o.Normal;
  half atten = 1;

  // Setup lighting environment
  UnityGI gi;
  UNITY_INITIALIZE_OUTPUT(UnityGI, gi);
  gi.indirect.diffuse = 0;
  gi.indirect.specular = 0;
  gi.light.color = 0;
  gi.light.dir = half3(0,1,0);
  // Call GI (lightmaps/SH/reflections) lighting function
  UnityGIInput giInput;
  UNITY_INITIALIZE_OUTPUT(UnityGIInput, giInput);
  giInput.light = gi.light;
  giInput.worldPos = worldPos;
  giInput.worldViewDir = worldViewDir;
  giInput.atten = atten;
  #if defined(LIGHTMAP_ON) || defined(DYNAMICLIGHTMAP_ON)
    giInput.lightmapUV = IN.lmap;
  #else
    giInput.lightmapUV = 0.0;
  #endif
  #if UNITY_SHOULD_SAMPLE_SH && !UNITY_SAMPLE_FULL_SH_PER_PIXEL
    giInput.ambient = IN.sh;
  #else
    giInput.ambient.rgb = 0.0;
  #endif
  giInput.probeHDR[0] = unity_SpecCube0_HDR;
  giInput.probeHDR[1] = unity_SpecCube1_HDR;
  #if defined(UNITY_SPECCUBE_BLENDING) || defined(UNITY_SPECCUBE_BOX_PROJECTION)
    giInput.boxMin[0] = unity_SpecCube0_BoxMin; // .w holds lerp value for blending
  #endif
  #ifdef UNITY_SPECCUBE_BOX_PROJECTION
    giInput.boxMax[0] = unity_SpecCube0_BoxMax;
    giInput.probePosition[0] = unity_SpecCube0_ProbePosition;
    giInput.boxMax[1] = unity_SpecCube1_BoxMax;
    giInput.boxMin[1] = unity_SpecCube1_BoxMin;
    giInput.probePosition[1] = unity_SpecCube1_ProbePosition;
  #endif
  LightingFireOnly_GI(o, giInput, gi);

  #if UNITY_SHOULD_SAMPLE_SH && !defined(LIGHTMAP_ON)
    gi.indirect.diffuse = 0;
  #endif

  // call lighting function to output g-buffer
  outEmission = LightingFireOnly_Deferred (o, worldViewDir, gi, outGBuffer0, outGBuffer1, outGBuffer2);
  #if defined(SHADOWS_SHADOWMASK) && (UNITY_ALLOWED_MRT_COUNT > 4)
    outShadowMask = UnityGetRawBakedOcclusions (IN.lmap.xy, worldPos);
  #endif
  #ifndef UNITY_HDR_ON
  outEmission.rgb = exp2(-outEmission.rgb);
  #endif
}


#endif

// -------- variant for: INSTANCING_ON 
#if defined(INSTANCING_ON)
// Surface shader code generated based on:
// vertex modifier: 'vert'
// writes to per-pixel normal: no
// writes to emission: YES
// writes to occlusion: no
// needs world space reflection vector: no
// needs world space normal vector: YES
// needs screen space position: no
// needs world space position: YES
// needs view direction: no
// needs world space view direction: no
// needs world space position for lighting: YES
// needs world space view direction for lighting: YES
// needs world space view direction for lightmaps: no
// needs vertex color: no
// needs VFACE: no
// needs SV_IsFrontFace: no
// passes tangent-to-world matrix to pixel shader: no
// reads from normal: YES
// 0 texcoords actually used
#include "UnityCG.cginc"
//Shader does not support lightmap thus we always want to fallback to SH.
#undef UNITY_SHOULD_SAMPLE_SH
#define UNITY_SHOULD_SAMPLE_SH (!defined(UNITY_PASS_FORWARDADD) && !defined(UNITY_PASS_PREPASSBASE) && !defined(UNITY_PASS_SHADOWCASTER) && !defined(UNITY_PASS_META))
#include "Lighting.cginc"

#define INTERNAL_DATA
#define WorldReflectionVector(data,normal) data.worldRefl
#define WorldNormalVector(data,normal) normal

// Original surface shader snippet:
#line 40 ""
#ifdef DUMMY_PREPROCESSOR_TO_WORK_AROUND_HLSL_COMPILER_LINE_HANDLING
#endif
/* UNITY: Original start of shader */
        //#pragma surface surf FireOnly vertex:vert noambient noforwardadd nolightmap nodynlightmap nodirlightmap nometa addshadow
        //#pragma target 3.0
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


// vertex-to-fragment interpolation data
struct v2f_surf {
  UNITY_POSITION(pos);
  float3 worldNormal : TEXCOORD0;
  float3 worldPos : TEXCOORD1;
  float4 custompack0 : TEXCOORD2; // state
  float4 custompack1 : TEXCOORD3; // lump seed
  float3 custompack2 : TEXCOORD4; // rockY
  float4 lmap : TEXCOORD5;
#ifndef LIGHTMAP_ON
  #if UNITY_SHOULD_SAMPLE_SH && !UNITY_SAMPLE_FULL_SH_PER_PIXEL
    half3 sh : TEXCOORD6; // SH
  #endif
#else
  #ifdef DIRLIGHTMAP_OFF
    float4 lmapFadePos : TEXCOORD6;
  #endif
#endif
  UNITY_VERTEX_INPUT_INSTANCE_ID
  UNITY_VERTEX_OUTPUT_STEREO
};

// vertex shader
v2f_surf vert_surf (appdata_full v) {
  UNITY_SETUP_INSTANCE_ID(v);
  v2f_surf o;
  UNITY_INITIALIZE_OUTPUT(v2f_surf,o);
  UNITY_TRANSFER_INSTANCE_ID(v,o);
  UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
  Input customInputData;
  vert (v, customInputData);
  o.custompack0.xyzw = customInputData.state;
  o.custompack1.xyz = customInputData.lump;
  o.custompack2.xyz = customInputData.rockY;
  o.custompack1.w = customInputData.seed;
  o.pos = UnityObjectToClipPos(v.vertex);
  float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
  float3 worldNormal = UnityObjectToWorldNormal(v.normal);
  o.worldPos.xyz = worldPos;
  o.worldNormal = worldNormal;
  o.lmap.zw = 0;
#ifdef LIGHTMAP_ON
  o.lmap.xy = v.texcoord1.xy * unity_LightmapST.xy + unity_LightmapST.zw;
  #ifdef DIRLIGHTMAP_OFF
    o.lmapFadePos.xyz = (mul(unity_ObjectToWorld, v.vertex).xyz - unity_ShadowFadeCenterAndType.xyz) * unity_ShadowFadeCenterAndType.w;
    o.lmapFadePos.w = (-UnityObjectToViewPos(v.vertex).z) * (1.0 - unity_ShadowFadeCenterAndType.w);
  #endif
#else
  o.lmap.xy = 0;
    #if UNITY_SHOULD_SAMPLE_SH && !UNITY_SAMPLE_FULL_SH_PER_PIXEL
      o.sh = 0;
      o.sh = ShadeSHPerVertex (worldNormal, o.sh);
    #endif
#endif
  return o;
}
#ifdef LIGHTMAP_ON
float4 unity_LightmapFade;
#endif
fixed4 unity_Ambient;

// fragment shader
void frag_surf (v2f_surf IN,
    out half4 outGBuffer0 : SV_Target0,
    out half4 outGBuffer1 : SV_Target1,
    out half4 outGBuffer2 : SV_Target2,
    out half4 outEmission : SV_Target3
#if defined(SHADOWS_SHADOWMASK) && (UNITY_ALLOWED_MRT_COUNT > 4)
    , out half4 outShadowMask : SV_Target4
#endif
) {
  UNITY_SETUP_INSTANCE_ID(IN);
  // prepare and unpack data
  Input surfIN;
  #ifdef FOG_COMBINED_WITH_TSPACE
    UNITY_EXTRACT_FOG_FROM_TSPACE(IN);
  #elif defined FOG_COMBINED_WITH_WORLD_POS
    UNITY_EXTRACT_FOG_FROM_WORLD_POS(IN);
  #else
    UNITY_EXTRACT_FOG(IN);
  #endif
  UNITY_INITIALIZE_OUTPUT(Input,surfIN);
  surfIN.state.x = 1.0;
  surfIN.lump.x = 1.0;
  surfIN.rockY.x = 1.0;
  surfIN.seed.x = 1.0;
  surfIN.worldPos.x = 1.0;
  surfIN.worldNormal.x = 1.0;
  surfIN.state = IN.custompack0.xyzw;
  surfIN.lump = IN.custompack1.xyz;
  surfIN.rockY = IN.custompack2.xyz;
  surfIN.seed = IN.custompack1.w;
  float3 worldPos = IN.worldPos.xyz;
  #ifndef USING_DIRECTIONAL_LIGHT
    fixed3 lightDir = normalize(UnityWorldSpaceLightDir(worldPos));
  #else
    fixed3 lightDir = _WorldSpaceLightPos0.xyz;
  #endif
  float3 worldViewDir = normalize(UnityWorldSpaceViewDir(worldPos));
  surfIN.worldNormal = IN.worldNormal;
  surfIN.worldPos = worldPos;
  #ifdef UNITY_COMPILER_HLSL
  SurfaceOutput o = (SurfaceOutput)0;
  #else
  SurfaceOutput o;
  #endif
  o.Albedo = 0.0;
  o.Emission = 0.0;
  o.Specular = 0.0;
  o.Alpha = 0.0;
  o.Gloss = 0.0;
  fixed3 normalWorldVertex = fixed3(0,0,1);
  o.Normal = IN.worldNormal;
  normalWorldVertex = IN.worldNormal;

  // call surface function
  surf (surfIN, o);
fixed3 originalNormal = o.Normal;
  half atten = 1;

  // Setup lighting environment
  UnityGI gi;
  UNITY_INITIALIZE_OUTPUT(UnityGI, gi);
  gi.indirect.diffuse = 0;
  gi.indirect.specular = 0;
  gi.light.color = 0;
  gi.light.dir = half3(0,1,0);
  // Call GI (lightmaps/SH/reflections) lighting function
  UnityGIInput giInput;
  UNITY_INITIALIZE_OUTPUT(UnityGIInput, giInput);
  giInput.light = gi.light;
  giInput.worldPos = worldPos;
  giInput.worldViewDir = worldViewDir;
  giInput.atten = atten;
  #if defined(LIGHTMAP_ON) || defined(DYNAMICLIGHTMAP_ON)
    giInput.lightmapUV = IN.lmap;
  #else
    giInput.lightmapUV = 0.0;
  #endif
  #if UNITY_SHOULD_SAMPLE_SH && !UNITY_SAMPLE_FULL_SH_PER_PIXEL
    giInput.ambient = IN.sh;
  #else
    giInput.ambient.rgb = 0.0;
  #endif
  giInput.probeHDR[0] = unity_SpecCube0_HDR;
  giInput.probeHDR[1] = unity_SpecCube1_HDR;
  #if defined(UNITY_SPECCUBE_BLENDING) || defined(UNITY_SPECCUBE_BOX_PROJECTION)
    giInput.boxMin[0] = unity_SpecCube0_BoxMin; // .w holds lerp value for blending
  #endif
  #ifdef UNITY_SPECCUBE_BOX_PROJECTION
    giInput.boxMax[0] = unity_SpecCube0_BoxMax;
    giInput.probePosition[0] = unity_SpecCube0_ProbePosition;
    giInput.boxMax[1] = unity_SpecCube1_BoxMax;
    giInput.boxMin[1] = unity_SpecCube1_BoxMin;
    giInput.probePosition[1] = unity_SpecCube1_ProbePosition;
  #endif
  LightingFireOnly_GI(o, giInput, gi);

  #if UNITY_SHOULD_SAMPLE_SH && !defined(LIGHTMAP_ON)
    gi.indirect.diffuse = 0;
  #endif

  // call lighting function to output g-buffer
  outEmission = LightingFireOnly_Deferred (o, worldViewDir, gi, outGBuffer0, outGBuffer1, outGBuffer2);
  #if defined(SHADOWS_SHADOWMASK) && (UNITY_ALLOWED_MRT_COUNT > 4)
    outShadowMask = UnityGetRawBakedOcclusions (IN.lmap.xy, worldPos);
  #endif
  #ifndef UNITY_HDR_ON
  outEmission.rgb = exp2(-outEmission.rgb);
  #endif
}


#endif


ENDHLSL

}

	// ---- shadow caster pass:
	Pass {
		Name "ShadowCaster"
		Tags { "LightMode" = "ShadowCaster" }
		ZWrite On ZTest LEqual

HLSLPROGRAM
// compile directives
#pragma vertex vert_surf
#pragma fragment frag_surf
#pragma target 3.0
#pragma multi_compile_instancing
#pragma skip_variants FOG_LINEAR FOG_EXP FOG_EXP2
#pragma multi_compile_shadowcaster noshadowmask nodynlightmap nodirlightmap nolightmap
#include "HLSLSupport.cginc"
#define UNITY_INSTANCED_LOD_FADE
#define UNITY_INSTANCED_SH
#define UNITY_INSTANCED_LIGHTMAPSTS
#include "UnityShaderVariables.cginc"
#include "UnityShaderUtilities.cginc"
// -------- variant for: <when no other keywords are defined>
#if !defined(INSTANCING_ON)
// Surface shader code generated based on:
// vertex modifier: 'vert'
// writes to per-pixel normal: no
// writes to emission: YES
// writes to occlusion: no
// needs world space reflection vector: no
// needs world space normal vector: no
// needs screen space position: no
// needs world space position: no
// needs view direction: no
// needs world space view direction: no
// needs world space position for lighting: YES
// needs world space view direction for lighting: YES
// needs world space view direction for lightmaps: no
// needs vertex color: no
// needs VFACE: no
// needs SV_IsFrontFace: no
// passes tangent-to-world matrix to pixel shader: no
// reads from normal: no
// 0 texcoords actually used
#include "UnityCG.cginc"
//Shader does not support lightmap thus we always want to fallback to SH.
#undef UNITY_SHOULD_SAMPLE_SH
#define UNITY_SHOULD_SAMPLE_SH (!defined(UNITY_PASS_FORWARDADD) && !defined(UNITY_PASS_PREPASSBASE) && !defined(UNITY_PASS_SHADOWCASTER) && !defined(UNITY_PASS_META))
#include "Lighting.cginc"

#define INTERNAL_DATA
#define WorldReflectionVector(data,normal) data.worldRefl
#define WorldNormalVector(data,normal) normal

// Original surface shader snippet:
#line 40 ""
#ifdef DUMMY_PREPROCESSOR_TO_WORK_AROUND_HLSL_COMPILER_LINE_HANDLING
#endif
/* UNITY: Original start of shader */
        //#pragma surface surf FireOnly vertex:vert noambient noforwardadd nolightmap nodynlightmap nodirlightmap nometa addshadow
        //#pragma target 3.0
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


// vertex-to-fragment interpolation data
struct v2f_surf {
  V2F_SHADOW_CASTER;
  float3 worldPos : TEXCOORD1;
  float4 custompack0 : TEXCOORD2; // state
  float4 custompack1 : TEXCOORD3; // lump seed
  float3 custompack2 : TEXCOORD4; // rockY
  UNITY_VERTEX_INPUT_INSTANCE_ID
  UNITY_VERTEX_OUTPUT_STEREO
};

// vertex shader
v2f_surf vert_surf (appdata_full v) {
  UNITY_SETUP_INSTANCE_ID(v);
  v2f_surf o;
  UNITY_INITIALIZE_OUTPUT(v2f_surf,o);
  UNITY_TRANSFER_INSTANCE_ID(v,o);
  UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
  Input customInputData;
  vert (v, customInputData);
  o.custompack0.xyzw = customInputData.state;
  o.custompack1.xyz = customInputData.lump;
  o.custompack2.xyz = customInputData.rockY;
  o.custompack1.w = customInputData.seed;
  float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
  float3 worldNormal = UnityObjectToWorldNormal(v.normal);
  o.worldPos.xyz = worldPos;
  TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
  return o;
}

// fragment shader
fixed4 frag_surf (v2f_surf IN) : SV_Target {
  UNITY_SETUP_INSTANCE_ID(IN);
  // prepare and unpack data
  Input surfIN;
  #ifdef FOG_COMBINED_WITH_TSPACE
    UNITY_EXTRACT_FOG_FROM_TSPACE(IN);
  #elif defined FOG_COMBINED_WITH_WORLD_POS
    UNITY_EXTRACT_FOG_FROM_WORLD_POS(IN);
  #else
    UNITY_EXTRACT_FOG(IN);
  #endif
  UNITY_INITIALIZE_OUTPUT(Input,surfIN);
  surfIN.state.x = 1.0;
  surfIN.lump.x = 1.0;
  surfIN.rockY.x = 1.0;
  surfIN.seed.x = 1.0;
  surfIN.worldPos.x = 1.0;
  surfIN.worldNormal.x = 1.0;
  surfIN.state = IN.custompack0.xyzw;
  surfIN.lump = IN.custompack1.xyz;
  surfIN.rockY = IN.custompack2.xyz;
  surfIN.seed = IN.custompack1.w;
  float3 worldPos = IN.worldPos.xyz;
  #ifndef USING_DIRECTIONAL_LIGHT
    fixed3 lightDir = normalize(UnityWorldSpaceLightDir(worldPos));
  #else
    fixed3 lightDir = _WorldSpaceLightPos0.xyz;
  #endif
  #ifdef UNITY_COMPILER_HLSL
  SurfaceOutput o = (SurfaceOutput)0;
  #else
  SurfaceOutput o;
  #endif
  o.Albedo = 0.0;
  o.Emission = 0.0;
  o.Specular = 0.0;
  o.Alpha = 0.0;
  o.Gloss = 0.0;
  fixed3 normalWorldVertex = fixed3(0,0,1);

  // call surface function
  surf (surfIN, o);
  SHADOW_CASTER_FRAGMENT(IN)
}


#endif

// -------- variant for: INSTANCING_ON 
#if defined(INSTANCING_ON)
// Surface shader code generated based on:
// vertex modifier: 'vert'
// writes to per-pixel normal: no
// writes to emission: YES
// writes to occlusion: no
// needs world space reflection vector: no
// needs world space normal vector: no
// needs screen space position: no
// needs world space position: no
// needs view direction: no
// needs world space view direction: no
// needs world space position for lighting: YES
// needs world space view direction for lighting: YES
// needs world space view direction for lightmaps: no
// needs vertex color: no
// needs VFACE: no
// needs SV_IsFrontFace: no
// passes tangent-to-world matrix to pixel shader: no
// reads from normal: no
// 0 texcoords actually used
#include "UnityCG.cginc"
//Shader does not support lightmap thus we always want to fallback to SH.
#undef UNITY_SHOULD_SAMPLE_SH
#define UNITY_SHOULD_SAMPLE_SH (!defined(UNITY_PASS_FORWARDADD) && !defined(UNITY_PASS_PREPASSBASE) && !defined(UNITY_PASS_SHADOWCASTER) && !defined(UNITY_PASS_META))
#include "Lighting.cginc"

#define INTERNAL_DATA
#define WorldReflectionVector(data,normal) data.worldRefl
#define WorldNormalVector(data,normal) normal

// Original surface shader snippet:
#line 40 ""
#ifdef DUMMY_PREPROCESSOR_TO_WORK_AROUND_HLSL_COMPILER_LINE_HANDLING
#endif
/* UNITY: Original start of shader */
        //#pragma surface surf FireOnly vertex:vert noambient noforwardadd nolightmap nodynlightmap nodirlightmap nometa addshadow
        //#pragma target 3.0
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


// vertex-to-fragment interpolation data
struct v2f_surf {
  V2F_SHADOW_CASTER;
  float3 worldPos : TEXCOORD1;
  float4 custompack0 : TEXCOORD2; // state
  float4 custompack1 : TEXCOORD3; // lump seed
  float3 custompack2 : TEXCOORD4; // rockY
  UNITY_VERTEX_INPUT_INSTANCE_ID
  UNITY_VERTEX_OUTPUT_STEREO
};

// vertex shader
v2f_surf vert_surf (appdata_full v) {
  UNITY_SETUP_INSTANCE_ID(v);
  v2f_surf o;
  UNITY_INITIALIZE_OUTPUT(v2f_surf,o);
  UNITY_TRANSFER_INSTANCE_ID(v,o);
  UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
  Input customInputData;
  vert (v, customInputData);
  o.custompack0.xyzw = customInputData.state;
  o.custompack1.xyz = customInputData.lump;
  o.custompack2.xyz = customInputData.rockY;
  o.custompack1.w = customInputData.seed;
  float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
  float3 worldNormal = UnityObjectToWorldNormal(v.normal);
  o.worldPos.xyz = worldPos;
  TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
  return o;
}

// fragment shader
fixed4 frag_surf (v2f_surf IN) : SV_Target {
  UNITY_SETUP_INSTANCE_ID(IN);
  // prepare and unpack data
  Input surfIN;
  #ifdef FOG_COMBINED_WITH_TSPACE
    UNITY_EXTRACT_FOG_FROM_TSPACE(IN);
  #elif defined FOG_COMBINED_WITH_WORLD_POS
    UNITY_EXTRACT_FOG_FROM_WORLD_POS(IN);
  #else
    UNITY_EXTRACT_FOG(IN);
  #endif
  UNITY_INITIALIZE_OUTPUT(Input,surfIN);
  surfIN.state.x = 1.0;
  surfIN.lump.x = 1.0;
  surfIN.rockY.x = 1.0;
  surfIN.seed.x = 1.0;
  surfIN.worldPos.x = 1.0;
  surfIN.worldNormal.x = 1.0;
  surfIN.state = IN.custompack0.xyzw;
  surfIN.lump = IN.custompack1.xyz;
  surfIN.rockY = IN.custompack2.xyz;
  surfIN.seed = IN.custompack1.w;
  float3 worldPos = IN.worldPos.xyz;
  #ifndef USING_DIRECTIONAL_LIGHT
    fixed3 lightDir = normalize(UnityWorldSpaceLightDir(worldPos));
  #else
    fixed3 lightDir = _WorldSpaceLightPos0.xyz;
  #endif
  #ifdef UNITY_COMPILER_HLSL
  SurfaceOutput o = (SurfaceOutput)0;
  #else
  SurfaceOutput o;
  #endif
  o.Albedo = 0.0;
  o.Emission = 0.0;
  o.Specular = 0.0;
  o.Alpha = 0.0;
  o.Gloss = 0.0;
  fixed3 normalWorldVertex = fixed3(0,0,1);

  // call surface function
  surf (surfIN, o);
  SHADOW_CASTER_FRAGMENT(IN)
}


#endif


ENDHLSL

}

	// ---- end of surface shader generated code


    }

    FallBack Off
}
