Shader "Punchy/Water"
{
    // Sea, lake and river surfaces. Colour comes from how much water the view ray passes through (read from the
    // camera depth texture): clear and tinted in the shallows, dark further down. Sky reflection rises at grazing
    // angles (Fresnel), the sun glints, ripples are sums of directional waves, rivers carry their ripples
    // downstream, and foam lines the shore.
    Properties
    {
        _ShallowColor("Shallow Color", Color) = (0.10, 0.42, 0.45, 1)
        _DeepColor("Deep Color", Color) = (0.01, 0.07, 0.15, 1)
        _Absorption("Colour Absorption (per metre)", Float) = 0.18
        _Opacity("Opacity (per metre)", Float) = 0.45
        _FoamColor("Foam Color", Color) = (0.9, 0.95, 1, 1)
        _FoamDistance("Shore Foam Distance (m)", Float) = 0.8
        _WaveScale("Wave Frequency (per metre)", Float) = 0.35
        _WaveStrength("Wave Strength", Range(0, 1)) = 0.25
        _WaveSpeed("Wave Speed", Float) = 0.8
        _FlowSpeed("River Flow Speed (m/s)", Float) = 1.6
        _Smoothness("Smoothness", Range(0, 1)) = 0.95
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                half _Absorption;
                half _Opacity;
                half4 _FoamColor;
                half _FoamDistance;
                float _WaveScale;
                half _WaveStrength;
                float _WaveSpeed;
                float _FlowSpeed;
                half _Smoothness;
            CBUFFER_END

            // Must match WaterVertex in BuildChunkMeshJob.cs.
            struct WaterAttributes
            {
                float3 positionOS : POSITION;
                float4 data       : TEXCOORD0;   // flow direction xz, depth below the surface, unused
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 data       : TEXCOORD1;
                half   fogFactor  : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(WaterAttributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.data = input.data;
                o.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return o;
            }

            // Slope (dh/dx, dh/dz) of a sum of directional waves. Directions and frequencies are spread so the
            // pattern doesn't visibly repeat.
            float2 WaveSlope(float2 p, float t)
            {
                const float2 dirs[6] =
                {
                    float2(0.82, 0.57), float2(-0.34, 0.94), float2(0.98, -0.21),
                    float2(-0.71, -0.70), float2(0.26, -0.96), float2(-0.93, 0.36)
                };
                const float freqs[6] = { 1.0, 1.37, 1.93, 2.71, 3.83, 5.29 };
                const float steep[6] = { 0.30, 0.24, 0.18, 0.13, 0.09, 0.06 };

                float2 slope = 0;
                [unroll]
                for (int i = 0; i < 6; i++)
                {
                    float k = _WaveScale * freqs[i];
                    // Deep-water dispersion: longer waves travel faster.
                    float phase = dot(dirs[i], p) * k + t * _WaveSpeed * sqrt(k * 9.81);
                    slope += dirs[i] * steep[i] * cos(phase);
                }
                return slope;
            }

            // Rivers: the wave pattern is carried downstream. Two copies offset by half a cycle cross-fade so the
            // pattern never stretches indefinitely (the standard flow-map technique).
            float2 FlowingSlope(float2 p, float2 flow, float t)
            {
                const float cycle = 3.0;
                float phase0 = frac(t / cycle);
                float phase1 = frac(t / cycle + 0.5);
                float w0 = 1 - abs(1 - 2 * phase0);
                float2 offset = flow * _FlowSpeed * cycle;
                return WaveSlope(p - offset * phase0, t * 0.3) * w0 +
                       WaveSlope(p - offset * phase1 + 7.3, t * 0.3) * (1 - w0);
            }

            half4 Frag(Varyings input, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float t = _Time.y;
                float2 p = input.positionWS.xz;
                float2 flow = input.data.xy;
                float flowAmount = saturate(length(flow));

                float2 slope = lerp(WaveSlope(p, t), FlowingSlope(p, flow / max(flowAmount, 1e-3), t), flowAmount);
                // Calm ripples with distance: far away they'd only shimmer and alias.
                float distanceFade = saturate(1 - distance(input.positionWS, GetCameraPositionWS()) / 350);
                float strength = _WaveStrength * (0.25 + 0.75 * distanceFade);
                float3 n = normalize(float3(-slope.x * strength, 1, -slope.y * strength));
                if (!frontFace)
                    n = -n;

                // How much water the view ray passes through before hitting the ground behind the surface.
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float surfaceEye = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                float thickness = max(sceneEye - surfaceEye, 0);

                float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
                Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half shadow = light.shadowAttenuation;
                half3 ambient = SampleSH(n);

                half3 waterColor = lerp(_DeepColor.rgb, _ShallowColor.rgb, exp(-thickness * _Absorption));
                half3 body = waterColor * (ambient + light.color * saturate(dot(n, light.direction)) * shadow * 0.6);

                half fresnel = 0.02 + 0.98 * pow(1 - saturate(dot(n, v)), 5);
                // Keep reflections pointing at the sky; tilted ripples would otherwise mirror the dark lower hemisphere.
                float3 r = reflect(-v, n);
                r.y = max(r.y, 0.05);
                half3 reflection = GlossyEnvironmentReflection(normalize(r), input.positionWS, 1 - _Smoothness, 1, screenUV);
                float3 h = normalize(light.direction + v);
                half glint = pow(saturate(dot(n, h)), exp2(10 * _Smoothness + 1)) * shadow;

                half3 color = lerp(body, reflection, fresnel) + light.color * glint * 4;
                half alpha = saturate(1 - exp(-thickness * _Opacity) + fresnel + glint);

                // Broken foam line where the water thins out against the shore.
                half foamNoise = 0.5 + 0.5 * sin(dot(p, float2(1.3, 0.7)) * 1.7 + t * 1.1) * sin(dot(p, float2(-0.6, 1.1)) * 2.3 - t * 1.4);
                half foam = smoothstep(foamNoise * 0.7, 1, saturate(1 - thickness / _FoamDistance));
                half3 foamLit = _FoamColor.rgb * (ambient + light.color * shadow * saturate(light.direction.y));
                color = lerp(color, foamLit, foam);
                alpha = max(alpha, foam);

                color = MixFog(color, input.fogFactor);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
