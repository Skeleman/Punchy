Shader "Punchy/Ambient Mist"
{
    // Soft ambient particles (fog wisps, drifting ash): translucent puffs lit by the sun and sky, faded where they
    // touch the ground, and seen through the atmosphere.
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "AmbientMist"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Sky.hlsl"
            #include "AmbientParticles.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float visibility : TEXCOORD2;
                float4 screen : TEXCOORD3;
            };

            Varyings Vert(uint vertex : SV_VertexID, uint instance : SV_InstanceID)
            {
                ParticleVertex p = BuildParticle(vertex, instance);
                Varyings o;
                o.positionWS = p.positionWS;
                o.positionCS = TransformWorldToHClip(p.positionWS);
                o.uv = p.uv;
                o.visibility = p.visibility;
                o.screen = ComputeScreenPos(o.positionCS);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float r = length(input.uv);
                // Wispy edges: value noise over the puff.
                float wisp = PunchyValueNoise(input.uv * 2.2 + input.positionWS.xz * 0.05) * 0.6 + 0.4;
                half alpha = pow(saturate(1 - r), 1.6) * wisp * _ParticleColor.a * input.visibility;

                // Soft where it meets the ground or anything else.
                float2 screenUV = input.screen.xy / input.screen.w;
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                alpha *= saturate((sceneDepth - input.positionCS.w) / 1.5);
                clip(alpha - 0.002);

                Light light = GetMainLight();
                half3 color = _ParticleColor.rgb * (light.color * 0.5 * (1 - PunchyCloudShadow(input.positionWS)) + SampleSH(half3(0, 1, 0)));
                return half4(PunchyAtmosphere(color, input.positionWS), alpha);
            }
            ENDHLSL
        }
    }
}
