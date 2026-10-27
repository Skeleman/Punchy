Shader "Punchy/Cave"
{
    // Cave walls (CaveSystem): rough rock in two tones broken up by noise, lit by daylight only near the entrance
    // (vertex colour alpha: 1 at the mouth, falling to 0 deep inside) and by any additional lights, such as the
    // player's lantern, everywhere. At the entrance the walls take the surrounding terrain's colour (vertex rgb) and
    // blend into the rock a few metres in (uv.x). The mesh is in world space and double-sided.
    Properties
    {
        _RockColor ("Rock", Color) = (0.48, 0.44, 0.4, 1)
        _DarkColor ("Crevices", Color) = (0.22, 0.2, 0.18, 1)
        _Darkness ("Light deep inside", Range(0, 0.2)) = 0.05
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _RockColor;
            half4 _DarkColor;
            half _Darkness;
        CBUFFER_END

        struct CaveAttributes
        {
            float3 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 color : COLOR;
            float2 data : TEXCOORD0;   // x terrain blend, y rock tint
        };
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Sky.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 color : TEXCOORD2;
                float2 data : TEXCOORD3;
            };

            Varyings Vert(CaveAttributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.color = input.color;
                o.data = input.data;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Normals come from the rock's shape and always face into the cave, whichever way a triangle winds.
                float3 n = normalize(input.normalWS);
                float3 p = input.positionWS;
                float openness = saturate(input.color.a);

                // Rock: two tones in blotches and strata, darker in the hollows (rgb carries a per-vertex tint).
                float blotch = PunchyValueNoise(p.xz * 0.35 + p.y * 0.2) * 0.6 + PunchyValueNoise(p.xy * 1.3 + p.z) * 0.4;
                float strata = 0.5 + 0.5 * sin(p.y * 2.3 + PunchyValueNoise(p.xz * 0.2) * 4);
                half3 rock = lerp(_DarkColor.rgb, _RockColor.rgb, saturate(blotch * 0.8 + strata * 0.3)) * input.data.y;
                // Where it stands in for the terrain, speckle it like the terrain's own close-up detail.
                half3 ground = input.color.rgb * (0.7 + 0.45 * blotch);
                half3 albedo = lerp(rock, ground, saturate(input.data.x));

                InputData inputData = (InputData)0;
                inputData.positionWS = p;
                inputData.normalWS = n;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

                // Daylight reaches only near the entrance.
                Light sun = GetMainLight(TransformWorldToShadowCoord(p));
                half3 light = sun.color * saturate(dot(n, sun.direction)) * sun.shadowAttenuation * openness;
                light += SampleSH(n) * lerp(_Darkness, 1, openness);

                // Lanterns and other lights work everywhere.
                uint count = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(count)
                    Light l = GetAdditionalLight(lightIndex, p, half4(1, 1, 1, 1));
                    light += l.color * l.distanceAttenuation * l.shadowAttenuation * saturate(dot(n, l.direction) * 0.8 + 0.2);
                LIGHT_LOOP_END

                half3 color = albedo * light;
                return half4(lerp(color, PunchyAtmosphere(color, p), openness), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            float4 Vert(CaveAttributes input) : SV_POSITION
            {
                float3 p = TransformObjectToWorld(input.positionOS);
                float3 n = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirection = normalize(_LightPosition - p);
            #else
                float3 lightDirection = _LightDirection;
            #endif
                return ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(p, n, lightDirection)));
            }

            half4 Frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Vert(CaveAttributes input) : SV_POSITION { return TransformObjectToHClip(input.positionOS); }
            half Frag(float4 positionCS : SV_POSITION) : SV_Target { return positionCS.z; }
            ENDHLSL
        }

        // Needed so the camera depth texture (built from the depth-normals prepass) includes caves.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
            };

            Varyings Vert(CaveAttributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            void Frag(Varyings input, out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                outNormalWS = half4(NormalizeNormalPerPixel(input.normalWS), 0);
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }
    }
}
