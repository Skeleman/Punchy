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
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Atmosphere.hlsl"
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
                nointerpolation float age : TEXCOORD5;   // negative: a bubble, above 1.5: a leaf (see BuildPlume)
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
                // A bubble: a glinting dome that swells, then, where it burst, ripples spreading as a swimmer's do (Z-36:
                // the swimmers' rings, Water.shader): a lit crest with darker troughs either side and a weaker ring
                // inside it, dying away as (1 - t) squared.
                if (input.age < 0)
                {
                    float a = -input.age;
                    float d = length(input.uv);
                    half dome = (1 - smoothstep(0.85, 1, d)) * (0.25 + 0.75 * smoothstep(0.55, 0.15, length(input.uv - float2(-0.3, 0.35))));
                    const float w = 0.07;
                    float dd = d - 0.8;
                    half crest = exp(-dd * dd / (w * w)) + 0.5 * exp(-(d - 0.45) * (d - 0.45) / (w * w));
                    half trough = exp(-(dd + 0.15) * (dd + 0.15) / (w * w)) + 0.7 * exp(-(dd - 0.14) * (dd - 0.14) / (w * w));
                    half fade = 1 - saturate((a - 0.6) / 0.4);
                    half ripple = (crest + trough * 0.7) * fade * fade;
                    half shape = a < 0.6 ? dome * smoothstep(0, 0.15, a) : ripple;
                    half bubbleAlpha = shape * input.alpha;
                    clip(bubbleAlpha - 0.002);
                    Light sunLight = GetMainLight();
                    half3 bubbleLit = e.color.rgb * PunchyEffectLight(e, input.positionWS, half3(0, 1, 0), sunLight.color * 0.8, SampleSH(half3(0, 1, 0))) + e.color.rgb * e.color.a;
                    // The ripple's troughs are the water darkened, its crests the light caught on it.
                    if (a >= 0.6)
                        bubbleLit = lerp(e.color.rgb * 0.3, bubbleLit * 1.2, crest / max(crest + trough, 1e-3));
                    return half4(PunchyAtmosphere(bubbleLit, input.positionWS), bubbleAlpha);
                }
                if (input.age > 1.5)
                {
                    float lr = input.age - 2;
                    float2 q = input.uv;
                    half leafAlpha = smoothstep(0, 0.08, 0.95 * (1 - q.y * q.y) - abs(q.x)) * input.alpha;
                    float2 leafScreen = input.screen.xy / input.screen.w;
                    leafAlpha *= saturate((LinearEyeDepth(SampleSceneDepth(leafScreen), _ZBufferParams) - input.positionCS.w) / 0.1);
                    clip(leafAlpha - 0.02);
                    half3 n = normalize(cross(ddx(input.positionWS), ddy(input.positionWS)));
                    Light leafSun = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                    half sunLit = leafSun.shadowAttenuation * (1 - PunchyCloudShadow(input.positionWS)) * (abs(dot(n, leafSun.direction)) * 0.75 + 0.25);
                    half3 tone = lerp(e.color.rgb, half3(0.42, 0.24, 0.04), step(0.72, EffectHash(lr * 7.7)) * lerp(0.5, 0.9, EffectHash(lr * 3.1)));
                    tone *= (0.8 + 0.4 * EffectHash(lr * 5.3)) * (1 - 0.3 * (1 - smoothstep(0.02, 0.07, abs(q.x))));
                    half3 leafLit = tone * PunchyEffectLight(e, input.positionWS, n, leafSun.color * sunLit, SampleSH(half3(0, 1, 0)));
                    return half4(PunchyAtmosphere(leafLit, input.positionWS), leafAlpha);
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
