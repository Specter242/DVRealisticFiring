// The surface shared by the coal lumps and the two layers under them.
// All three read the same zone heights, heat, swells, and shovel heaps;
// their own depth offsets keep the continuous layers below the lumps.
#ifndef REALISTIC_FIRING_COAL_SURFACE
#define REALISTIC_FIRING_COAL_SURFACE

sampler2D _LumpZones;
sampler2D _BedRelief;
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
    zone.b = max(zone.b, relief.b);
    return float3(lerp(_BedMin.x, _BedMax.x, anchor.x),
        lerp(_BedMin.y, _BedMax.y, height), lerp(_BedMin.z, _BedMax.z, anchor.y));
}

#endif
