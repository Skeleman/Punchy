Shader "Punchy/Soft Particle"
{
    // Soft, wispy puffs (breath vapour, kicked-up snow) for particle systems: a round falloff broken up by noise,
    // lit by the sun and sky so they darken at night, faded where they touch geometry, seen through the atmosphere.
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "SoftParticle"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Sky.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                half4 color : TEXCOORD2;
                float4 screen : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = input.uv;
                o.color = input.color;
                o.screen = ComputeScreenPos(o.positionCS);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2 - 1;
                float r = length(p);
                // Wisps: value noise over the puff (particles spin randomly, so each looks different).
                float wisp = PunchyValueNoise(p * 2.5 + 7.3) * 0.6 + PunchyValueNoise(p * 5.0 - 3.1) * 0.4;
                half alpha = pow(saturate(1 - r), 1.5) * lerp(0.45, 1.0, wisp) * input.color.a;

                // Soft edges where the puff meets the ground or a body.
                float2 screenUV = input.screen.xy / input.screen.w;
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float depth = input.positionCS.w;   // view depth
                alpha *= saturate((sceneDepth - depth) / 0.3);
                clip(alpha - 0.002);

                Light light = GetMainLight();
                half3 color = input.color.rgb * (light.color * 0.55 + SampleSH(half3(0, 1, 0)) * 1.1);
                return half4(PunchyAtmosphere(color, input.positionWS), alpha);
            }
            ENDHLSL
        }
    }
}
