// The chimney's smoke drawn at half resolution (the mod's PlumeOffscreen;
// Nexus, 2026-10-25: two players' frame rate fell from 60 to 15 with the
// plume, whose cost is its particles' overlapping layers, a quarter as many
// pixels so), laid over the scene. Drawn by an object of the mod's in the
// scene's transparent things, at the plume's place among them (laid over
// from a command buffer before them, the sky, drawn among them, covered
// it), and only by the camera that drew the smoke (_PlumeOffscreenReady).
// Each pixel is blended from its four nearest half-resolution texels, or,
// at the edge of something standing in front (their depths, PlumeDepth's,
// unlike this pixel's), taken from the one whose depth is nearest its own,
// so the smoke neither spills over the edge nor leaves a gap along it.
Shader "RealisticFiring/PlumeComposite"
{
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend One OneMinusSrcAlpha
        ColorMask RGB

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            sampler2D_float _PlumeLowDepth;
            sampler2D _PlumeLow;
            // The scene's and the smoke's sizes: width, height, and their inverses.
            float4 _PlumeFull;
            float4 _PlumeHalf;
            // 1 while the camera drawing has drawn the smoke at half resolution.
            float _PlumeOffscreenReady;
            // Development (the mod's OffscreenDebug): 1 a tint over the whole
            // screen, 2 the smoke's coverage, 3 the scene's and the half-size depths.
            float _PlumeCompositeDebug;

            // Depths this far apart, as a share of this pixel's, are an edge.
            #define EDGE 0.1

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            // One triangle over the whole screen, given in clip space; for
            // any other camera, beyond the screen.
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = _PlumeOffscreenReady > 0.5 ? float4(v.vertex.xy, 0.5, 1) : float4(2, 2, 2, 1);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float here = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, float4(i.pos.xy * _PlumeFull.zw, 0, 0)));
                if (_PlumeCompositeDebug > 0.5)
                {
                    if (_PlumeCompositeDebug < 1.5)
                        return float4(0.3, 0, 0.3, 0.3);
                    if (_PlumeCompositeDebug < 2.5)
                        return float4(tex2Dlod(_PlumeLow, float4(i.pos.xy * 0.5 * _PlumeHalf.zw, 0, 0)).a, 0, 0, 1);
                    float halfDepth = LinearEyeDepth(tex2Dlod(_PlumeLowDepth, float4(i.pos.xy * 0.5 * _PlumeHalf.zw, 0, 0)).r);
                    return float4(0, here / 100, halfDepth / 100, 1);
                }
                // This pixel among the half-resolution texels, and the four nearest.
                float2 at = i.pos.xy * 0.5;
                float2 t0 = floor(at - 0.5) + 0.5;
                float2 uv[4] = {
                    t0 * _PlumeHalf.zw,
                    (t0 + float2(1, 0)) * _PlumeHalf.zw,
                    (t0 + float2(0, 1)) * _PlumeHalf.zw,
                    (t0 + 1) * _PlumeHalf.zw
                };
                float nearest = 1e9;
                float worst = 0;
                float2 best = uv[0];
                [unroll]
                for (int k = 0; k < 4; k++)
                {
                    float apart = abs(LinearEyeDepth(tex2Dlod(_PlumeLowDepth, float4(uv[k], 0, 0)).r) - here);
                    worst = max(worst, apart);
                    if (apart < nearest)
                    {
                        nearest = apart;
                        best = uv[k];
                    }
                }
                if (worst < EDGE * here)
                    return tex2Dlod(_PlumeLow, float4(at * _PlumeHalf.zw, 0, 0));
                return tex2Dlod(_PlumeLow, float4(best, 0, 0));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
