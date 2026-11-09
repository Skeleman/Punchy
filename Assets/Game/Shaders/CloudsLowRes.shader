Shader "Hidden/Punchy/CloudsLowRes"
{
    // The volumetric clouds, marched at low resolution through LowResEffectFeature (first user of the buffer): scatter in rgb,
    // transmittance in a, read back in Sky.shader. Pixels the sky doesn't show, and under water, are left clear for free.
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex PunchyLowResVert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Sky.hlsl"
            #include "LowResEffect.hlsl"

            half4 Frag(PunchyLowResVaryings input) : SV_Target
            {
                uint off = (uint)_PunchySkyOff;
                [branch] if (_PunchyUnderwater.w > 0 || (off & 1u) != 0 || !PunchyLowResAnySky(input.positionCS.xy))
                    return half4(0, 0, 0, 1);
                float4 jitter = PunchyLowResJitter();
                float3 dir = PunchyLowResRay(input.positionCS.xy + jitter.xy);
                half3 scatter;
                float transmit;
                PunchyCloudMarch(dir, input.positionCS.xy, jitter.w, scatter, transmit);
                return half4(scatter, transmit);
            }
            ENDHLSL
        }
    }
}
