











#ifndef REALISTIC_FIRING_FIRE_COLOR
#define REALISTIC_FIRING_FIRE_COLOR

half3 _FireRampDull;
half3 _FireRampRed;
half3 _FireRampOrange;
half3 _FireRampCore;
half3 _FireRampWhite;
half _FireWhiteHeat;
half _FireExposure;

// The coal heats through before the end of the color ramp. Reusing the
// white blend for its crust, local heat, and color compressed those nested
// changes into the last few percent of heat, despite each being smooth.
half FireHeatThrough(half heat)
{
    return smoothstep(_FireWhiteHeat - 0.2, min(_FireWhiteHeat + 0.1, 1.0), heat);
}

half3 FireColor(half heat)
{
    half4 x = saturate(heat * 4.0 - half4(0.0, 1.0, 2.0, 3.0));
    half3 c = _FireRampDull * x.x;
    c = lerp(c, _FireRampRed, x.y);
    c = lerp(c, _FireRampOrange, x.z);
    c = lerp(c, _FireRampCore, x.w);
    return lerp(c, _FireRampWhite, smoothstep(_FireWhiteHeat, 1.0, heat));
}

// The fire's radiance as the eye sees it.
half3 FireExposed(half3 radiance)
{
    return 1.0 - exp(-max(radiance, 0.0) * _FireExposure);
}

#endif
