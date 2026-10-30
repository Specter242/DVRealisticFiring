#ifndef REALISTIC_FIRING_FIREBOX_VIEW
#define REALISTIC_FIRING_FIREBOX_VIEW

// Camera and ray in the volume's centered unit cube. Only the firehole
// exposes the interior to an outside observer; scene depth still hides the
// solid door and anything in front of it in FireboxVolume.shader.
// Return the ray start, or -1 when the view must not expose the firebox.
float FireboxViewStart(float3 origin, float3 direction, float4 plane, float4 across, float4 up, float enter)
{
    // Unfitted openings retain scene-depth occlusion, as before.
    if (dot(plane.xyz, plane.xyz) < 1e-8)
        return enter;
    if (all(abs(origin) <= 0.5001))
        return enter;

    float behind = dot(plane, float4(origin, 1));
    if (behind >= -1e-4)
    {
        // The mouth can extend from the door plane to the back of the
        // rendered volume. A head in that short passage is inside too.
        // Bound it at the nearest projection of the box onto the door normal,
        // so the passage cannot extend through the sides or tube plate.
        float mouthEnd = max(plane.w - dot(abs(plane.xyz), float3(0.5, 0.5, 0.5)), 0.0);
        float2 mouth = float2(dot(across, float4(origin, 1)), dot(up, float4(origin, 1)));
        return behind <= mouthEnd + 1e-4 && all(mouth >= 0.0) && all(mouth <= 1.0) ? enter : -1.0;
    }

    float heading = dot(plane.xyz, direction);
    if (heading <= 0.0)
        return -1.0;
    float atHole = -behind / heading;
    float4 crossing = float4(origin + direction * atHole, 1);
    float2 hole = float2(dot(across, crossing), dot(up, crossing));
    return all(hole >= 0.0) && all(hole <= 1.0) ? max(enter, atHole) : -1.0;
}
#endif
