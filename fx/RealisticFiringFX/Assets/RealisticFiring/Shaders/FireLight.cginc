











#ifndef REALISTIC_FIRING_FIRE_LIGHT
#define REALISTIC_FIRING_FIRE_LIGHT

#include "Assets/RealisticFiring/Shaders/FireColor.cginc"

// xyz: a zone's glowing surface in the world; w: its area, m².
float4 _FireZones[9];
// rgb: the zone's radiance, as the bed draws its glow there.
float4 _FireZoneLight[9];
// xyz: the middle of a zone's flames in the world; w: their radius, m.
float4 _FireFlames[9];
// rgb: the flames' radiant intensity over pi.
float4 _FireFlameLight[9];
// xyz: the grate's up in the world; w: 1 while there is a fire to light by.
float4 _FireGrateUp;
// xyz: the open firehole's middle in the world; w: its open area, m².
float4 _FireDoor;
// rgb: the cab's light coming in through it; w: its scattered share.
float4 _FireDoorLight;
// The bed's frame in the world, for what is laid out along the box's walls
// (their staybolts): its corner by the door (xyz), and its unit ways across
// (w its width, m) and along (w its length, m); up is _FireGrateUp.
float4 _FireBedOrigin;
float4 _FireBedAcross;
float4 _FireBedAlong;
// The firebox test bed's views (a development tool): 0 as drawn, 1 the
// surface's albedo alone, 2 the fire's light on a white surface.
float _FireDebug;

// A surface's light as the eye sees it (FireExposed), or a test bed view as it is.
half3 FireSeen(half3 emission)
{
    return _FireDebug > 0.5 ? emission : FireExposed(emission);
}

// A surface's light: its albedo in the fire's light, or a test bed view.
half3 FireLit(half3 albedo, half3 light)
{
    return _FireDebug > 1.5 ? light : _FireDebug > 0.5 ? albedo : albedo * light;
}

// How much of a zone's light goes to the side against straight up.
#define FIRE_SIDE_GLOW 0.35

// The light from the fire on a surface at p facing n, over pi: a surface's
// albedo times this is what it gives back.
half3 FireLight(float3 p, half3 n)
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
        // A patch's light, softened so a surface on it takes no more than
        // the patch's own radiance.
        light += _FireZoneLight[i].rgb * (area * facing * emitting / (UNITY_PI * zone2 + area));

        float3 toFlame = _FireFlames[i].xyz - p;
        float flame2 = max(dot(toFlame, toFlame), 1e-4);
        half flameFacing = saturate(dot(n, toFlame * rsqrt(flame2)));
        float radius2 = _FireFlames[i].w * _FireFlames[i].w;
        light += _FireFlameLight[i].rgb * (flameFacing / (flame2 + radius2));
    }
    return light * _FireGrateUp.w;
}






half3 DoorLight(float3 p, half3 n)
{
    float3 toDoor = _FireDoor.xyz - p;
    float door2 = max(dot(toDoor, toDoor), 1e-4);
    half facing = 0.25 + 0.75 * saturate(dot(n, toDoor * rsqrt(door2)));
    // The opening is an area source, with a little of its incoming light
    // scattered back by the interior. A point-like inverse-square source
    // alone left the cold tube plate effectively black. Both terms vanish
    // with the open area, so a shut door adds no daylight to the box.
    float direct = _FireDoor.w * facing / (door2 + _FireDoor.w + 1e-4);
    float bounced = _FireDoorLight.w * _FireDoor.w / (0.25 * door2 + _FireDoor.w + 1e-4);
    return _FireDoorLight.rgb * (direct + bounced);
}

// The cab's light through the door as the eye sees it. The eye stops down
// for a bright fire, and that dims the cab's light in the box as well; but
// opened up for a dim or dead fire it must not brighten that light past the
// cab's own, or a box lit only through its door draws as bright as the
// backhead around it (a fire put out drew its cold coal and walls as bright
// as the backhead's plate in shade, 2026-10-27 captures). Linear, as the
// game draws the cab: there is no tone mapping.
// And an eye on a burning fire has adapted to it, as far as _FireBed.z
// says: cab daylight on black coal is a hundredth of a glowing bed's light,
// and drew a drifting fire's dark lumps as grey as a dead fire's (2026-11-12).
half3 DoorExposed(half3 radiance)
{
    return radiance * (min(_FireExposure, 1.0) * _FireBed.z);
}

// A surface in the box as the eye sees it: its own glow and the fire's light
// on it through the eye's response to the fire, the cab's light through the
// door no brighter than the cab; or a test bed view as it was.
half3 FireSurface(half3 glow, half3 albedo, half3 fireLight, half3 doorLight)
{
    if (_FireDebug > 0.5)
        return glow + FireLit(albedo, fireLight + doorLight);
    return FireExposed(glow + albedo * fireLight) + DoorExposed(albedo * doorLight);
}

// The normal turned to face the camera: a box's shell may face either way.
half3 FacingCamera(half3 n, float3 p)
{
    return dot(n, _WorldSpaceCameraPos.xyz - p) < 0 ? -n : n;
}

// Glossy highlights off fresh coal's fractured faces (the footage: "fractured,
// black, locally shiny"): a normalized Blinn-Phong lobe and Schlick's Fresnel
// on each of the sources FireLight and DoorLight sum, taken as points. The
// sources are broad patches and balls, so the lobe is broad too. Coal's
// vitreous luster reflects about 5 % head on (a refractive index near 1.6).
#define FIRE_GLINT_POWER 40.0
#define FIRE_GLINT_F0 0.05

half GlintLobe(half3 n, float3 l, half3 v)
{
    half3 h = normalize(l + v);
    half fresnel = FIRE_GLINT_F0 + (1.0 - FIRE_GLINT_F0) * pow(1.0 - saturate(dot(v, h)), 5.0);
    return fresnel * (FIRE_GLINT_POWER + 8.0) / 8.0 * pow(saturate(dot(n, h)), FIRE_GLINT_POWER);
}

// The fire's light glinting off a surface at p facing n toward the eye along
// v: each source's light on it (as FireLight gives it) times its lobe.
half3 FireGlint(float3 p, half3 n, half3 v)
{
    half3 glint = 0;
    UNITY_UNROLL
    for (int i = 0; i < 9; i++)
    {
        float3 toZone = _FireZones[i].xyz - p;
        float zone2 = max(dot(toZone, toZone), 1e-4);
        float3 l = toZone * rsqrt(zone2);
        half facing = saturate(dot(n, l));
        half emitting = FIRE_SIDE_GLOW + (1.0 - FIRE_SIDE_GLOW) * saturate(dot(_FireGrateUp.xyz, -l));
        float area = _FireZones[i].w;
        glint += _FireZoneLight[i].rgb * (area * facing * emitting / (UNITY_PI * zone2 + area)) * GlintLobe(n, l, v);

        float3 toFlame = _FireFlames[i].xyz - p;
        float flame2 = max(dot(toFlame, toFlame), 1e-4);
        float3 lf = toFlame * rsqrt(flame2);
        float radius2 = _FireFlames[i].w * _FireFlames[i].w;
        glint += _FireFlameLight[i].rgb * (saturate(dot(n, lf)) / (flame2 + radius2)) * GlintLobe(n, lf, v);
    }
    return glint * _FireGrateUp.w;
}

// The cab's light through the door glinting off it: the door's direct light
// only (its scattered share comes from no one direction).
half3 DoorGlint(float3 p, half3 n, half3 v)
{
    float3 toDoor = _FireDoor.xyz - p;
    float door2 = max(dot(toDoor, toDoor), 1e-4);
    float3 l = toDoor * rsqrt(door2);
    float direct = _FireDoor.w * saturate(dot(n, l)) / (door2 + _FireDoor.w + 1e-4);
    return _FireDoorLight.rgb * direct * GlintLobe(n, l, v);
}

// A lighting model that takes nothing from Unity's lights, ambient, or
// reflections: the surface's emission carries the fire's light. Drawn
// deferred, it writes black albedo and no specular, so the game's lights
// add nothing, and it stays in the depth buffer.
half4 LightingFireOnly(SurfaceOutput s, half3 viewDir, UnityGI gi)
{
    return half4(0, 0, 0, s.Alpha);
}

half4 LightingFireOnly_Deferred(SurfaceOutput s, half3 viewDir, UnityGI gi, out half4 outGBuffer0, out half4 outGBuffer1, out half4 outGBuffer2)
{
    outGBuffer0 = half4(0, 0, 0, 1);
    outGBuffer1 = half4(0, 0, 0, 0);
    outGBuffer2 = half4(s.Normal * 0.5 + 0.5, 1);
    // The lighting buffer's start, as Unity's own deferred lighting returns
    // it: the surface's emission, here all of its light.
    return half4(s.Emission, 1);
}

void LightingFireOnly_GI(SurfaceOutput s, UnityGIInput data, inout UnityGI gi)
{
}

#endif
