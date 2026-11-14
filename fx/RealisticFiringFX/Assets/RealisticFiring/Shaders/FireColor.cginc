











#ifndef REALISTIC_FIRING_FIRE_COLOR
#define REALISTIC_FIRING_FIRE_COLOR

half3 _FireRampDull;
half3 _FireRampRed;
half3 _FireRampOrange;
half3 _FireRampCore;
half3 _FireRampHot;
half3 _FireRampWhite;
half _FireWhiteHeat;
half _FireExposure;
// The coal bed's glow: x the heat of the fire between the lumps at the
// slowest burn (it runs to 1 with the zone's heat), y a burning lump's cap's
// share of its zone's heat, z how much of the cab's light through the door
// the eye still takes in, adapted to the fire, w the heat between the lumps
// of a zone at its full heat.
half4 _FireBed;

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
    // Toward white by way of yellow: a hotter body's green fills before its
    // blue. Straight to the white stop, whose blue is two thirds of its
    // green, the half-way colors were peach and pink (a working bed drew as
    // salmon tiles, 2026-11-12).
    half white = smoothstep(_FireWhiteHeat, 1.0, heat) * 2.0;
    c = lerp(c, _FireRampHot, saturate(white));
    return lerp(c, _FireRampWhite, saturate(white - 1.0));
}

// The fire between and under the lumps: wherever a bed burns at all its
// inside is far hotter than its top, which looks out at the cooler box (the
// footage: yellow gaps between dark-capped lumps, drifting as working). By
// the zone's heat, 0 with no fire; by its square, so only a zone near its
// full heat has pale gaps (by the heat itself a working fire's were white
// with pale flat lumps in them), and to _FireBed.w of the ramp, yellow-white: at
// its top a working bed's gaps clipped to a flat white floor, and only a
// fire driven flat out, which the eye cannot follow, should.
half FireBedCore(half heat)
{
    return lerp(_FireBed.x, _FireBed.w, heat * heat) * saturate(heat * 20.0);
}

// The fire's radiance as the eye sees it.
half3 FireExposed(half3 radiance)
{
    return 1.0 - exp(-max(radiance, 0.0) * _FireExposure);
}

#endif
