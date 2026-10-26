// The scene's depth at half resolution, for the chimney's smoke drawn so
// (the mod's PlumeOffscreen; PlumeComposite lays it over the scene): the
// farthest of each two by two, pass 0 in the smoke's own depth buffer, for
// its particles to be hidden by what stands in front of them, pass 1 as a
// value for PlumeComposite to read (a depth buffer read as a texture gave
// nothing, and one bound beside another target took no depth).
Shader "RealisticFiring/PlumeDepth"
{
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        HLSLINCLUDE
        #include "UnityCG.cginc"

        UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
        // The scene's size: width, height, and their inverses.
        float4 _PlumeFull;

        struct appdata
        {
            float4 vertex : POSITION;
        };

        struct v2f
        {
            float4 pos : SV_POSITION;
        };

        // The farthest of the four scene pixels under a half-resolution one.
        float Farthest(float2 pixel)
        {
            float2 corner = (floor(pixel) * 2 + 0.5) * _PlumeFull.zw;
            float a = SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, float4(corner, 0, 0));
            float b = SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, float4(corner + float2(_PlumeFull.z, 0), 0, 0));
            float c = SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, float4(corner + float2(0, _PlumeFull.w), 0, 0));
            float d = SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, float4(corner + _PlumeFull.zw, 0, 0));
            #if defined(UNITY_REVERSED_Z)
            return min(min(a, b), min(c, d));
            #else
            return max(max(a, b), max(c, d));
            #endif
        }

        // One triangle over the whole target, given in clip space.
        v2f vert(appdata v)
        {
            v2f o;
            o.pos = float4(v.vertex.xy, 0.5, 1);
            return o;
        }
        ENDHLSL

        Pass
        {
            ColorMask 0
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            float frag(v2f i) : SV_Depth
            {
                return Farthest(i.pos.xy);
            }
            ENDHLSL
        }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            float frag(v2f i) : SV_Target
            {
                return Farthest(i.pos.xy);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
