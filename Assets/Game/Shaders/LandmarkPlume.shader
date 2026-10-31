Shader "Punchy/Landmark Plume"
{
    // Landmark plumes (steam, smoke, geyser spouts, spray, sparkles): soft puffs lit by the sun and sky, plus their
    // own glow, faded where they touch the ground and seen through the atmosphere.
    SubShader
    {
        // After the water, which otherwise could draw over puffs and bubbles on its surface.
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "LandmarkPlume"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Sky.hlsl"
            #include "Glow.hlsl"
            #include "LandmarkEffects.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float alpha : TEXCOORD2;
                float4 screen : TEXCOORD3;
                nointerpolation uint emitter : TEXCOORD4;
                nointerpolation float age : TEXCOORD5;   // negative: a bubble (see BuildPlume)
            };

            Varyings Vert(uint vertex : SV_VertexID, uint instance : SV_InstanceID)
            {
                PlumeVertex p = BuildPlume(vertex, instance);
                Varyings o;
                o.positionWS = p.positionWS;
                o.positionCS = TransformWorldToHClip(p.positionWS);
                o.uv = p.uv;
                o.alpha = p.alpha;
                o.screen = ComputeScreenPos(o.positionCS);
                o.emitter = instance / _MaxParticles;
                o.age = p.age;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                LandmarkEmitter e = _LandmarkEmitters[input.emitter];
                // A bubble: a glinting dome that swells, then a thin ring spreading and fading where it burst.
                if (input.age < 0)
                {
                    float a = -input.age;
                    float d = length(input.uv);
                    half dome = (1 - smoothstep(0.85, 1, d)) * (0.25 + 0.75 * smoothstep(0.55, 0.15, length(input.uv - float2(-0.3, 0.35))));
                    half ring = 1 - smoothstep(0, 0.14, abs(d - 0.9));
                    half shape = a < 0.6 ? dome * smoothstep(0, 0.15, a) : ring * (1 - smoothstep(0.6, 1, a));
                    half bubbleAlpha = shape * input.alpha;
                    clip(bubbleAlpha - 0.002);
                    Light sunLight = GetMainLight();
                    half3 bubbleLit = e.color.rgb * PunchyEffectLight(e, input.positionWS, half3(0, 1, 0), sunLight.color * 0.8, SampleSH(half3(0, 1, 0))) + e.color.rgb * e.color.a;
                    return half4(PunchyAtmosphere(bubbleLit, input.positionWS), bubbleAlpha);
                }
                float soft = saturate(1 - dot(input.uv, input.uv));
                float wisp = PunchyValueNoise(input.uv * 2.0 + input.positionWS.xz * 0.15 + input.positionWS.y * 0.1) * 0.6 + 0.4;
                half alpha = soft * soft * wisp * input.alpha;

                float2 screenUV = input.screen.xy / input.screen.w;
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                alpha *= saturate((sceneDepth - input.positionCS.w) / 1.0);
                clip(alpha - 0.002);

                Light light = GetMainLight();
                half3 lit = e.color.rgb * PunchyEffectLight(e, input.positionWS, half3(0, 1, 0), light.color * 0.6 * (1 - PunchyCloudShadow(input.positionWS)), SampleSH(half3(0, 1, 0)));
                half3 color = lit + e.color.rgb * e.color.a;
                return half4(PunchyAtmosphere(color, input.positionWS), alpha);
            }
            ENDHLSL
        }
    }
}
