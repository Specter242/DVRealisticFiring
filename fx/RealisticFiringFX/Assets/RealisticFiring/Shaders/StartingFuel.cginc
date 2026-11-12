// The starting fuel's surface for Realistic Firing: wood, rags, and their
// containers. Each renderer's property block gives its state, 0 to 1:
// _Scorch browns it before it lights; _Char blackens its surface in
// spreading patches into cracked charcoal, independent of how much of it
// has burned through; _Glow lights the char's cracks from within as
// embers; _Oil soaks cloth dark and glossy. _Seed shifts the patterns per
// piece. Bark and end grain keep their own contrast under the char.

#ifndef REALISTIC_FIRING_STARTING_FUEL
#define REALISTIC_FIRING_STARTING_FUEL

sampler2D _MainTex;
sampler2D _Noise;
half _Char;
half _Glow;
half _Scorch;
half _Oil;
half _Seed;
// Each material's gloss, set by the builder: the albedo's alpha only varies
// it, so a texture whose alpha is lost to compression cannot read as a mirror.
half _Smoothness;

struct FuelSurface
{
    half3 albedo;
    half smoothness;
    half3 glow;
};

FuelSurface StartingFuelSurface(float2 uv, half shade)
{
    FuelSurface s;
    half4 c = tex2D(_MainTex, uv);
    half3 albedo = c.rgb * shade;
    half smooth = _Smoothness * (0.6 + 0.8 * saturate(c.a * 4.0));
    float2 at = uv * float2(1.3, 2.1) + _Seed * float2(17.3, 5.7);
    half broad = tex2D(_Noise, at * 0.6).r;
    half fine = tex2D(_Noise, at * 1.3).g;
    // Browning ahead of the flame.
    albedo = lerp(albedo, albedo * half3(0.42, 0.30, 0.22), saturate(_Scorch) * 0.7);
    // Char grows in patches, then covers.
    half charred = saturate((_Char * 1.35 - broad * 0.75 - 0.1) * 5.0);
    // Alligator cracks in the char: thin dark lines between blocks.
    half crack = 1.0 - saturate(abs(fine * 2.0 - 1.0) * 16.0);
    half3 coal = half3(0.030, 0.027, 0.025) * (0.8 + 0.5 * fine) * (1.0 - 0.6 * crack);
    // Keep a ghost of the wood's own contrast (bark against end grain).
    coal *= 0.75 + 0.5 * saturate(Luminance(c.rgb) * 2.0);
    albedo = lerp(albedo, coal, charred);
    smooth = lerp(smooth, 0.12 + 0.08 * fine, charred);
    // Oil-soaked cloth: dark amber-brown, only a little glossy, so a
    // bright sky's reflection does not wash the soaking back out.
    half oil = saturate(_Oil);
    albedo *= lerp(half3(1.0, 1.0, 1.0), half3(0.30, 0.25, 0.17), oil);
    smooth = lerp(smooth, 0.26, oil);
    // Embers: the cracks first, then the char's face as it heats. Char
    // glows unevenly: hot patches where the draft reaches, dull red or dark
    // between them, never one even net.
    half hot = saturate(_Glow);
    half patch = smoothstep(0.30, 0.80, tex2D(_Noise, at * 0.23 + float2(0.37, 0.61)).r);
    half live = hot * (0.12 + 0.88 * patch);
    half3 ember = lerp(half3(0.45, 0.05, 0.01), half3(1.0, 0.40, 0.07), live);
    s.glow = ember * charred * (crack * 1.2 * live + 0.10 * live * smoothstep(0.4, 0.9, broad));
    s.albedo = albedo;
    s.smoothness = smooth;
    return s;
}

#endif
