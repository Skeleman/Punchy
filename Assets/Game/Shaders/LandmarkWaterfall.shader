Shader "Punchy/Landmark Waterfall"
{
    // Waterfall curtains: a translucent sheet with streaks rushing down it, whitening into foam as it falls.
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "LandmarkWaterfall"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Sky.hlsl"
            #include "LandmarkEffects.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float fallLength : TEXCOORD3;
                nointerpolation uint emitter : TEXCOORD4;
            };

            Varyings Vert(uint vertex : SV_VertexID, uint instance : SV_InstanceID)
            {
                FallVertex f = BuildWaterfall(vertex, instance);
                Varyings o;
                o.positionWS = f.positionWS;
                o.positionCS = TransformWorldToHClip(f.positionWS);
                o.normalWS = f.normalWS;
                o.uv = f.uv;
                o.fallLength = f.fallLength;
                o.emitter = instance;
                return o;
            }

            half4 Frag(Varyings input, bool front : SV_IsFrontFace) : SV_Target
            {
                LandmarkEmitter e = _LandmarkEmitters[input.emitter];
                float2 uv = input.uv;
                float speed = 0.9;
                // Streaks: narrow across, long down, rushing downwards.
                float along = uv.y * input.fallLength / 3.0 - _Time.y * speed;
                float streak = PunchyValueNoise(float2(uv.x * e.shape.y * 1.6, along)) * 0.6 +
                               PunchyValueNoise(float2(uv.x * e.shape.y * 4.0 + 13.0, along * 2.3)) * 0.4;
                float foam = saturate(uv.y * 0.9 + (streak - 0.5) * 0.8);

                float edges = smoothstep(0, 0.12, uv.x) * smoothstep(1, 0.88, uv.x);
                float ragged = smoothstep(0.25, 0.55, streak + 0.35 - abs(uv.x - 0.5) * 0.6);
                half alpha = e.timing.z * edges * lerp(0.55, 1, streak) * lerp(ragged, 1, 0.5) * smoothstep(0, 0.03, uv.y);
                clip(alpha - 0.01);

                float3 n = normalize(front ? input.normalWS : -input.normalWS);
                Light light = GetMainLight();
                half3 albedo = lerp(e.color.rgb, half3(0.92, 0.95, 0.97), foam);
                half diffuse = saturate(dot(n, light.direction)) * 0.6 + 0.4;
                half3 color = albedo * (light.color * diffuse * (1 - PunchyCloudShadow(input.positionWS)) + SampleSH(n));
                return half4(PunchyAtmosphere(color, input.positionWS), alpha);
            }
            ENDHLSL
        }
    }
}
