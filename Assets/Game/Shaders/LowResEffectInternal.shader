Shader "Hidden/Punchy/LowResEffectInternal"
{
    // LowResEffectFeature's own passes: 0 shrinks the camera depth, 1 blends this frame's result with the reprojected last one.
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "LowResEffect.hlsl"
        ENDHLSL

        // 0: the low-resolution depth, keeping the furthest depth in each cell, so a cell with any sky in it counts as sky.
        Pass
        {
            Name "Depth"
            HLSLPROGRAM
            #pragma vertex PunchyLowResVert
            #pragma fragment Frag
            Texture2D<float> _PunchyLowResSourceDepth;
            float4 _PunchyLowResResolve;   // x history weight, y reproject by depth, z scale

            float Frag(PunchyLowResVaryings input) : SV_Target
            {
                int scale = (int)_PunchyLowResResolve.z;
                int2 start = int2(input.positionCS.xy) * scale;
                float depth = 1 - PUNCHY_LOW_RES_SKY;
                for (int y = 0; y < scale; y++)
                    for (int x = 0; x < scale; x++)
                    {
                        float d = _PunchyLowResSourceDepth.Load(int3(start + int2(x, y), 0));
                        depth = PUNCHY_LOW_RES_FURTHER(depth, d);
                    }
                return depth;
            }
            ENDHLSL
        }

        // 1: blend with the last frame, its value clamped to this frame's neighbourhood so moving clouds and fast turns don't ghost.
        Pass
        {
            Name "Resolve"
            HLSLPROGRAM
            #pragma vertex PunchyLowResVert
            #pragma fragment Frag
            Texture2D<float4> _PunchyLowResCurrent;
            Texture2D<float4> _PunchyLowResHistory;
            float4x4 _PunchyLowResPrevVP;
            float3 _PunchyLowResCameraDelta;
            float4 _PunchyLowResResolve;   // x history weight, y reproject by depth, z scale

            float4 Frag(PunchyLowResVaryings input) : SV_Target
            {
                int2 pos = int2(input.positionCS.xy);
                int2 last = int2(_PunchyLowResSize.xy) - 1;
                float4 current = _PunchyLowResCurrent.Load(int3(pos, 0));
                float weight = _PunchyLowResResolve.x;
                if (weight <= 0)
                    return current;

                float4 low = current, high = current;
                for (int y = -1; y <= 1; y++)
                    for (int x = -1; x <= 1; x++)
                    {
                        float4 c = _PunchyLowResCurrent.Load(int3(clamp(pos + int2(x, y), 0, last), 0));
                        low = min(low, c);
                        high = max(high, c);
                    }

                float2 uv = input.positionCS.xy * _PunchyLowResSize.zw;
                float2 ndc = float2(uv.x * 2 - 1, (1 - uv.y) * 2 - 1);
                float4 clip;
                float depth = _PunchyLowResDepth.Load(int3(pos, 0));
                if (_PunchyLowResResolve.y > 0 && abs(depth - PUNCHY_LOW_RES_SKY) > 1e-6)
                {
                    float4 world = mul(_PunchyLowResInvVP, float4(ndc, depth, 1));
                    world /= world.w;
                    clip = mul(_PunchyLowResPrevVP, float4(world.xyz + _PunchyLowResCameraDelta, 1));
                }
                else
                {
                    float4 ray = mul(_PunchyLowResInvVP, float4(ndc, 0.5, 1));
                    clip = mul(_PunchyLowResPrevVP, float4(ray.xyz / ray.w, 0));
                }
                if (clip.w <= 0)
                    return current;
                float2 previous = clip.xy / clip.w;
                previous = float2(previous.x * 0.5 + 0.5, 0.5 - previous.y * 0.5);
                if (any(previous < 0) || any(previous > 1))
                    return current;
                float4 history = clamp(_PunchyLowResHistory.SampleLevel(sampler_LinearClamp, previous, 0), low, high);
                return lerp(current, history, weight);
            }
            ENDHLSL
        }
    }
}
