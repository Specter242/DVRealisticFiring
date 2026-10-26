











#ifndef REALISTIC_FIRING_FIRE_COLOR
#define REALISTIC_FIRING_FIRE_COLOR

half3 _FireRampDull;
half3 _FireRampRed;
half3 _FireRampOrange;
half3 _FireRampCore;
half3 _FireRampWhite;
half _FireExposure;

half3 FireColor(half heat)
{
    half4 x = saturate(heat * 4.0 - half4(0.0, 1.0, 2.0, 3.0));
    half3 c = _FireRampDull * x.x;
    c = lerp(c, _FireRampRed, x.y);
    c = lerp(c, _FireRampOrange, x.z);
    c = lerp(c, _FireRampCore, x.w);
    return lerp(c, _FireRampWhite, saturate(heat * 4.0 - 4.0));
}

// The fire's radiance as the eye sees it.
half3 FireExposed(half3 radiance)
{
    return 1.0 - exp(-max(radiance, 0.0) * _FireExposure);
}

#endif
