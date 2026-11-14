// The surface shared by the coal lumps and the two layers under them.
// All three read the same zone heights, heat, swells, and shovel heaps;
// their own depth offsets keep the continuous layers below the lumps.
#ifndef REALISTIC_FIRING_COAL_SURFACE
#define REALISTIC_FIRING_COAL_SURFACE

sampler2D _LumpZones;
sampler2D _BedRelief;
sampler3D _Noise;
float4 _BedMin;
float4 _BedMax;
float _DoorSlope;

// Zone state: height, heat, green share, ash. Relief: swell, heap, freshness.
float3 CoalSurfacePoint(float2 anchor, out float4 zone)
{
    zone = tex2Dlod(_LumpZones, float4(anchor, 0, 0));
    float4 relief = tex2Dlod(_BedRelief, float4(anchor, 0, 0));
    float height = max(zone.r * max(0.2, 1.0 + relief.r) + relief.g, 0.0)
        * smoothstep(0.0, 1.0, anchor.y / max(_DoorSlope, 1e-3));
    // In a zone driven flat out green coal catches at once: none of it
    // lies dark in that glare (a few black lumps sat in the white bed as
    // a hole in the picture).
    zone.b = max(zone.b, relief.b) * (1.0 - 0.9 * smoothstep(0.93, 1.0, zone.g));
    return float3(lerp(_BedMin.x, _BedMax.x, anchor.x),
        lerp(_BedMin.y, _BedMax.y, height), lerp(_BedMin.z, _BedMax.z, anchor.y));
}

// Where on the bed (across, along) fresh coal lies first as a zone's green
// share rises, 0 to 1 and even: in patches a hand or two across, as
// shovelfuls land, not lump by lump all over the zone (the council,
// 2026-11-12: fresh coal read as black pieces scattered through the fire).
// The lumps, the bed under them, and the fire's volume read the same.
half CoalFreshPatch(float2 anchor)
{
    return tex3Dlod(_Noise, float4(anchor.x * 2.3 + 0.21, 0.43, anchor.y * 4.1 + 0.67, 0)).b;
}

// How much of that point fresh coal covers, for the zone's green share.
// It lies on top: a share of the zone's coal covers nearly twice that share
// of its surface (a round of five shovelfuls showed as one small cluster).
#define COAL_FRESH_SPREAD 1.8

half CoalFreshCover(half green, half patch)
{
    return saturate((green * COAL_FRESH_SPREAD - patch) / 0.15);
}

#endif
