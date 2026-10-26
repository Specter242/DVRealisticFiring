


















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
#line 36 ""
#ifdef DUMMY_PREPROCESSOR_TO_WORK_AROUND_HLSL_COMPILER_LINE_HANDLING
#endif
/* UNITY: Original start of shader */
        //#pragma surface surf FireOnly vertex:vert noambient noforwardadd nolightmap nodynlightmap nodirlightmap nometa addshadow
        //#pragma target 3.0
        #include "Assets/RealisticFiring/Shaders/FireLight.cginc"

        sampler3D _Noise;
        sampler2D _LumpZones;
        float4 _BedMin;
        float4 _BedMax;
        float _DoorSlope;
        sampler2D _BedRelief;
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
            float4 zone = tex2Dlod(_LumpZones, float4(anchor, 0, 0));
            // The coal's surface there, with the bed's swells and heaps
            // (ZonedCoalBed.ReliefTexture), sloping down to the door as the
            // bed does.
            float4 relief = tex2Dlod(_BedRelief, float4(anchor, 0, 0));
            float height = max(zone.r * max(0.2, 1.0 + relief.r) + relief.g, 0.0) * smoothstep(0.0, 1.0, anchor.y / max(_DoorSlope, 1e-3));
            // A fresh heap's lumps lie black, as a zone's green coal does.
            zone.b = max(zone.b, relief.b);
            float3 at = float3(lerp(_BedMin.x, _BedMax.x, anchor.x), lerp(_BedMin.y, _BedMax.y, height), lerp(_BedMin.z, _BedMax.z, anchor.y));
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
            // Its foot glows in the gaps' fire; green coal shows it round its
            // edges first.
            half margin = low * low * saturate(heat * 1.6) * lerp(0.55, 0.8, fresh);
            // An ember's red-orange, cooler than the gaps.
            half3 glow = FireColor(t * 0.7) * body + FireColor(heat * 0.6) * margin;
            glow *= _GlowStrength * (1.0 + 0.5 * _Pulse) * (1.0 - 0.7 * ash);

            half3 albedo = lerp(half3(0.05, 0.045, 0.04), half3(0.022, 0.021, 0.02), fresh);
            albedo = lerp(albedo, half3(0.38, 0.36, 0.33), saturate(ash * 1.2) * (1.0 - fresh));
            o.Albedo = albedo;
            half3 facing = FacingCamera(n, IN.worldPos);
            o.Emission = FireSeen(glow + FireLit(albedo, FireLight(IN.worldPos, facing) * _FireLightScale + DoorLight(IN.worldPos, facing)));
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
  float4 custompack1 : TEXCOORD3; // lump rockY
  float custompack2 : TEXCOORD4; // seed
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
  float4 custompack1 : TEXCOORD3; // lump rockY
  float custompack2 : TEXCOORD4; // seed
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
  float4 custompack1 : TEXCOORD3; // lump rockY
  float custompack2 : TEXCOORD4; // seed
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
  float4 custompack1 : TEXCOORD3; // lump rockY
  float custompack2 : TEXCOORD4; // seed
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
  o.custompack1.w = customInputData.rockY;
  o.custompack2.x = customInputData.seed;
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
  surfIN.rockY = IN.custompack1.w;
  surfIN.seed = IN.custompack2.x;
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
#line 36 ""
#ifdef DUMMY_PREPROCESSOR_TO_WORK_AROUND_HLSL_COMPILER_LINE_HANDLING
#endif
/* UNITY: Original start of shader */
        //#pragma surface surf FireOnly vertex:vert noambient noforwardadd nolightmap nodynlightmap nodirlightmap nometa addshadow
        //#pragma target 3.0
        #include "Assets/RealisticFiring/Shaders/FireLight.cginc"

        sampler3D _Noise;
        sampler2D _LumpZones;
        float4 _BedMin;
        float4 _BedMax;
        float _DoorSlope;
        sampler2D _BedRelief;
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
            float4 zone = tex2Dlod(_LumpZones, float4(anchor, 0, 0));
            // The coal's surface there, with the bed's swells and heaps
            // (ZonedCoalBed.ReliefTexture), sloping down to the door as the
            // bed does.
            float4 relief = tex2Dlod(_BedRelief, float4(anchor, 0, 0));
            float height = max(zone.r * max(0.2, 1.0 + relief.r) + relief.g, 0.0) * smoothstep(0.0, 1.0, anchor.y / max(_DoorSlope, 1e-3));
            // A fresh heap's lumps lie black, as a zone's green coal does.
            zone.b = max(zone.b, relief.b);
            float3 at = float3(lerp(_BedMin.x, _BedMax.x, anchor.x), lerp(_BedMin.y, _BedMax.y, height), lerp(_BedMin.z, _BedMax.z, anchor.y));
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
            // Its foot glows in the gaps' fire; green coal shows it round its
            // edges first.
            half margin = low * low * saturate(heat * 1.6) * lerp(0.55, 0.8, fresh);
            // An ember's red-orange, cooler than the gaps.
            half3 glow = FireColor(t * 0.7) * body + FireColor(heat * 0.6) * margin;
            glow *= _GlowStrength * (1.0 + 0.5 * _Pulse) * (1.0 - 0.7 * ash);

            half3 albedo = lerp(half3(0.05, 0.045, 0.04), half3(0.022, 0.021, 0.02), fresh);
            albedo = lerp(albedo, half3(0.38, 0.36, 0.33), saturate(ash * 1.2) * (1.0 - fresh));
            o.Albedo = albedo;
            half3 facing = FacingCamera(n, IN.worldPos);
            o.Emission = FireSeen(glow + FireLit(albedo, FireLight(IN.worldPos, facing) * _FireLightScale + DoorLight(IN.worldPos, facing)));
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
  float4 custompack1 : TEXCOORD3; // lump rockY
  float custompack2 : TEXCOORD4; // seed
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
  float4 custompack1 : TEXCOORD3; // lump rockY
  float custompack2 : TEXCOORD4; // seed
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
  float4 custompack1 : TEXCOORD3; // lump rockY
  float custompack2 : TEXCOORD4; // seed
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
  float4 custompack1 : TEXCOORD3; // lump rockY
  float custompack2 : TEXCOORD4; // seed
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
  o.custompack1.w = customInputData.rockY;
  o.custompack2.x = customInputData.seed;
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
  surfIN.rockY = IN.custompack1.w;
  surfIN.seed = IN.custompack2.x;
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
#line 36 ""
#ifdef DUMMY_PREPROCESSOR_TO_WORK_AROUND_HLSL_COMPILER_LINE_HANDLING
#endif
/* UNITY: Original start of shader */
        //#pragma surface surf FireOnly vertex:vert noambient noforwardadd nolightmap nodynlightmap nodirlightmap nometa addshadow
        //#pragma target 3.0
        #include "Assets/RealisticFiring/Shaders/FireLight.cginc"

        sampler3D _Noise;
        sampler2D _LumpZones;
        float4 _BedMin;
        float4 _BedMax;
        float _DoorSlope;
        sampler2D _BedRelief;
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
            float4 zone = tex2Dlod(_LumpZones, float4(anchor, 0, 0));
            // The coal's surface there, with the bed's swells and heaps
            // (ZonedCoalBed.ReliefTexture), sloping down to the door as the
            // bed does.
            float4 relief = tex2Dlod(_BedRelief, float4(anchor, 0, 0));
            float height = max(zone.r * max(0.2, 1.0 + relief.r) + relief.g, 0.0) * smoothstep(0.0, 1.0, anchor.y / max(_DoorSlope, 1e-3));
            // A fresh heap's lumps lie black, as a zone's green coal does.
            zone.b = max(zone.b, relief.b);
            float3 at = float3(lerp(_BedMin.x, _BedMax.x, anchor.x), lerp(_BedMin.y, _BedMax.y, height), lerp(_BedMin.z, _BedMax.z, anchor.y));
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
            // Its foot glows in the gaps' fire; green coal shows it round its
            // edges first.
            half margin = low * low * saturate(heat * 1.6) * lerp(0.55, 0.8, fresh);
            // An ember's red-orange, cooler than the gaps.
            half3 glow = FireColor(t * 0.7) * body + FireColor(heat * 0.6) * margin;
            glow *= _GlowStrength * (1.0 + 0.5 * _Pulse) * (1.0 - 0.7 * ash);

            half3 albedo = lerp(half3(0.05, 0.045, 0.04), half3(0.022, 0.021, 0.02), fresh);
            albedo = lerp(albedo, half3(0.38, 0.36, 0.33), saturate(ash * 1.2) * (1.0 - fresh));
            o.Albedo = albedo;
            half3 facing = FacingCamera(n, IN.worldPos);
            o.Emission = FireSeen(glow + FireLit(albedo, FireLight(IN.worldPos, facing) * _FireLightScale + DoorLight(IN.worldPos, facing)));
            o.Alpha = 1;
        }


// vertex-to-fragment interpolation data
struct v2f_surf {
  UNITY_POSITION(pos);
  float3 worldNormal : TEXCOORD0;
  float3 worldPos : TEXCOORD1;
  float4 custompack0 : TEXCOORD2; // state
  float4 custompack1 : TEXCOORD3; // lump rockY
  float custompack2 : TEXCOORD4; // seed
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
  o.custompack1.w = customInputData.rockY;
  o.custompack2.x = customInputData.seed;
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
  surfIN.rockY = IN.custompack1.w;
  surfIN.seed = IN.custompack2.x;
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
#line 36 ""
#ifdef DUMMY_PREPROCESSOR_TO_WORK_AROUND_HLSL_COMPILER_LINE_HANDLING
#endif
/* UNITY: Original start of shader */
        //#pragma surface surf FireOnly vertex:vert noambient noforwardadd nolightmap nodynlightmap nodirlightmap nometa addshadow
        //#pragma target 3.0
        #include "Assets/RealisticFiring/Shaders/FireLight.cginc"

        sampler3D _Noise;
        sampler2D _LumpZones;
        float4 _BedMin;
        float4 _BedMax;
        float _DoorSlope;
        sampler2D _BedRelief;
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
            float4 zone = tex2Dlod(_LumpZones, float4(anchor, 0, 0));
            // The coal's surface there, with the bed's swells and heaps
            // (ZonedCoalBed.ReliefTexture), sloping down to the door as the
            // bed does.
            float4 relief = tex2Dlod(_BedRelief, float4(anchor, 0, 0));
            float height = max(zone.r * max(0.2, 1.0 + relief.r) + relief.g, 0.0) * smoothstep(0.0, 1.0, anchor.y / max(_DoorSlope, 1e-3));
            // A fresh heap's lumps lie black, as a zone's green coal does.
            zone.b = max(zone.b, relief.b);
            float3 at = float3(lerp(_BedMin.x, _BedMax.x, anchor.x), lerp(_BedMin.y, _BedMax.y, height), lerp(_BedMin.z, _BedMax.z, anchor.y));
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
            // Its foot glows in the gaps' fire; green coal shows it round its
            // edges first.
            half margin = low * low * saturate(heat * 1.6) * lerp(0.55, 0.8, fresh);
            // An ember's red-orange, cooler than the gaps.
            half3 glow = FireColor(t * 0.7) * body + FireColor(heat * 0.6) * margin;
            glow *= _GlowStrength * (1.0 + 0.5 * _Pulse) * (1.0 - 0.7 * ash);

            half3 albedo = lerp(half3(0.05, 0.045, 0.04), half3(0.022, 0.021, 0.02), fresh);
            albedo = lerp(albedo, half3(0.38, 0.36, 0.33), saturate(ash * 1.2) * (1.0 - fresh));
            o.Albedo = albedo;
            half3 facing = FacingCamera(n, IN.worldPos);
            o.Emission = FireSeen(glow + FireLit(albedo, FireLight(IN.worldPos, facing) * _FireLightScale + DoorLight(IN.worldPos, facing)));
            o.Alpha = 1;
        }


// vertex-to-fragment interpolation data
struct v2f_surf {
  UNITY_POSITION(pos);
  float3 worldNormal : TEXCOORD0;
  float3 worldPos : TEXCOORD1;
  float4 custompack0 : TEXCOORD2; // state
  float4 custompack1 : TEXCOORD3; // lump rockY
  float custompack2 : TEXCOORD4; // seed
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
  o.custompack1.w = customInputData.rockY;
  o.custompack2.x = customInputData.seed;
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
  surfIN.rockY = IN.custompack1.w;
  surfIN.seed = IN.custompack2.x;
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
#line 36 ""
#ifdef DUMMY_PREPROCESSOR_TO_WORK_AROUND_HLSL_COMPILER_LINE_HANDLING
#endif
/* UNITY: Original start of shader */
        //#pragma surface surf FireOnly vertex:vert noambient noforwardadd nolightmap nodynlightmap nodirlightmap nometa addshadow
        //#pragma target 3.0
        #include "Assets/RealisticFiring/Shaders/FireLight.cginc"

        sampler3D _Noise;
        sampler2D _LumpZones;
        float4 _BedMin;
        float4 _BedMax;
        float _DoorSlope;
        sampler2D _BedRelief;
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
            float4 zone = tex2Dlod(_LumpZones, float4(anchor, 0, 0));
            // The coal's surface there, with the bed's swells and heaps
            // (ZonedCoalBed.ReliefTexture), sloping down to the door as the
            // bed does.
            float4 relief = tex2Dlod(_BedRelief, float4(anchor, 0, 0));
            float height = max(zone.r * max(0.2, 1.0 + relief.r) + relief.g, 0.0) * smoothstep(0.0, 1.0, anchor.y / max(_DoorSlope, 1e-3));
            // A fresh heap's lumps lie black, as a zone's green coal does.
            zone.b = max(zone.b, relief.b);
            float3 at = float3(lerp(_BedMin.x, _BedMax.x, anchor.x), lerp(_BedMin.y, _BedMax.y, height), lerp(_BedMin.z, _BedMax.z, anchor.y));
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
            // Its foot glows in the gaps' fire; green coal shows it round its
            // edges first.
            half margin = low * low * saturate(heat * 1.6) * lerp(0.55, 0.8, fresh);
            // An ember's red-orange, cooler than the gaps.
            half3 glow = FireColor(t * 0.7) * body + FireColor(heat * 0.6) * margin;
            glow *= _GlowStrength * (1.0 + 0.5 * _Pulse) * (1.0 - 0.7 * ash);

            half3 albedo = lerp(half3(0.05, 0.045, 0.04), half3(0.022, 0.021, 0.02), fresh);
            albedo = lerp(albedo, half3(0.38, 0.36, 0.33), saturate(ash * 1.2) * (1.0 - fresh));
            o.Albedo = albedo;
            half3 facing = FacingCamera(n, IN.worldPos);
            o.Emission = FireSeen(glow + FireLit(albedo, FireLight(IN.worldPos, facing) * _FireLightScale + DoorLight(IN.worldPos, facing)));
            o.Alpha = 1;
        }


// vertex-to-fragment interpolation data
struct v2f_surf {
  V2F_SHADOW_CASTER;
  float3 worldPos : TEXCOORD1;
  float4 custompack0 : TEXCOORD2; // state
  float4 custompack1 : TEXCOORD3; // lump rockY
  float custompack2 : TEXCOORD4; // seed
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
  o.custompack1.w = customInputData.rockY;
  o.custompack2.x = customInputData.seed;
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
  surfIN.rockY = IN.custompack1.w;
  surfIN.seed = IN.custompack2.x;
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
#line 36 ""
#ifdef DUMMY_PREPROCESSOR_TO_WORK_AROUND_HLSL_COMPILER_LINE_HANDLING
#endif
/* UNITY: Original start of shader */
        //#pragma surface surf FireOnly vertex:vert noambient noforwardadd nolightmap nodynlightmap nodirlightmap nometa addshadow
        //#pragma target 3.0
        #include "Assets/RealisticFiring/Shaders/FireLight.cginc"

        sampler3D _Noise;
        sampler2D _LumpZones;
        float4 _BedMin;
        float4 _BedMax;
        float _DoorSlope;
        sampler2D _BedRelief;
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
            float4 zone = tex2Dlod(_LumpZones, float4(anchor, 0, 0));
            // The coal's surface there, with the bed's swells and heaps
            // (ZonedCoalBed.ReliefTexture), sloping down to the door as the
            // bed does.
            float4 relief = tex2Dlod(_BedRelief, float4(anchor, 0, 0));
            float height = max(zone.r * max(0.2, 1.0 + relief.r) + relief.g, 0.0) * smoothstep(0.0, 1.0, anchor.y / max(_DoorSlope, 1e-3));
            // A fresh heap's lumps lie black, as a zone's green coal does.
            zone.b = max(zone.b, relief.b);
            float3 at = float3(lerp(_BedMin.x, _BedMax.x, anchor.x), lerp(_BedMin.y, _BedMax.y, height), lerp(_BedMin.z, _BedMax.z, anchor.y));
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
            // Its foot glows in the gaps' fire; green coal shows it round its
            // edges first.
            half margin = low * low * saturate(heat * 1.6) * lerp(0.55, 0.8, fresh);
            // An ember's red-orange, cooler than the gaps.
            half3 glow = FireColor(t * 0.7) * body + FireColor(heat * 0.6) * margin;
            glow *= _GlowStrength * (1.0 + 0.5 * _Pulse) * (1.0 - 0.7 * ash);

            half3 albedo = lerp(half3(0.05, 0.045, 0.04), half3(0.022, 0.021, 0.02), fresh);
            albedo = lerp(albedo, half3(0.38, 0.36, 0.33), saturate(ash * 1.2) * (1.0 - fresh));
            o.Albedo = albedo;
            half3 facing = FacingCamera(n, IN.worldPos);
            o.Emission = FireSeen(glow + FireLit(albedo, FireLight(IN.worldPos, facing) * _FireLightScale + DoorLight(IN.worldPos, facing)));
            o.Alpha = 1;
        }


// vertex-to-fragment interpolation data
struct v2f_surf {
  V2F_SHADOW_CASTER;
  float3 worldPos : TEXCOORD1;
  float4 custompack0 : TEXCOORD2; // state
  float4 custompack1 : TEXCOORD3; // lump rockY
  float custompack2 : TEXCOORD4; // seed
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
  o.custompack1.w = customInputData.rockY;
  o.custompack2.x = customInputData.seed;
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
  surfIN.rockY = IN.custompack1.w;
  surfIN.seed = IN.custompack2.x;
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
