Shader "Punchy/Terrain Surface"
{
    Properties
    {
        _EmissionScale("Emission Scale", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

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
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            // Reflect the flat ambient colour (which WeatherSystem keeps in step with the sky) rather than the
            // environment cubemap, which is baked once and would show the wrong sky as weather changes.
            #define _ENVIRONMENTREFLECTIONS_OFF
            #include "TerrainSurface.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Sky.hlsl"
            #include "Glow.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
                half4  surface    : TEXCOORD2;
                half4  extra      : TEXCOORD3;
                half3  emission   : TEXCOORD4;
                half   fogFactor  : TEXCOORD5;
                half   bump       : TEXCOORD6;
                float  cave       : TEXCOORD7;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // Fine relief: a two-octave height noise (clods ~0.7 m, grain ~0.2 m), returned with its slope.
            float MicroHeight(float2 p)
            {
                return PunchyValueNoise(p / 0.7) * 0.6 + PunchyValueNoise(p / 0.21 + 17.3) * 0.4;
            }

            float3 MicroDetail(float2 p)   // xy = slope (dh/dx, dh/dz), z = height
            {
                const float e = 0.04;
                float h = MicroHeight(p);
                return float3((MicroHeight(p + float2(e, 0)) - h) / e, (MicroHeight(p + float2(0, e)) - h) / e, h);
            }

            Varyings Vert(TerrainAttributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);

                VertexPositionInputs pos = GetVertexPositionInputs(TerrainPositionOS(input));
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.surface = input.surface;
                o.extra = input.extra;
                o.emission = input.emission.rgb;
                o.fogFactor = ComputeFogFactor(pos.positionCS.z);
                o.bump = input.morph.z;
                o.cave = input.morph.w;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                TerrainCaveClip(input.cave);

                float distance = length(input.positionWS - GetCameraPositionWS());
                half3 normalWS = NormalizeNormalPerPixel(input.normalWS);
                float2 p = input.positionWS.xz;

                // Fine relief up close (fades out before it could shimmer), skipped on skirts.
                half detailFade = (1 - smoothstep(25, 70, distance)) * input.bump * (input.extra.w <= 0.01);
                float3 micro = detailFade > 0.001 ? MicroDetail(p) : float3(0, 0, 0.5);

                // Rain: flat ground collects puddles (dark, mirror-smooth, no relief); elsewhere wet ground just
                // darkens and gets a soft sheen. Grass and snow don't glaze. Far away the sheen fades, since at
                // that distance it would only read as a bright smear.
                half wet = PunchyWetnessAt(p).r * (1 - saturate(input.extra.z * 2));
                // Shores: ground just above the water stays wet, a dark band with an uneven upper edge. The mesh
                // stores it as a negative skirt drop (surface vertices leave that channel 0; skirts are positive).
                half shore = 0;
                [branch] if (input.extra.w < -0.001)
                    shore = smoothstep(0.2, 0.75, saturate(-input.extra.w) + (PunchyValueNoise(p * 0.8) - 0.5) * 0.35)
                          * (1 - saturate(input.extra.z * 2));
                half flatness = smoothstep(0.96, 0.995, normalWS.y);
                half puddleNoise = PunchyValueNoise(p / 2.3) * 0.65 + PunchyValueNoise(p / 0.9 + 5.1) * 0.35;
                half puddle = smoothstep(0.62, 0.7, puddleNoise + wet * 0.35 - 0.25) * flatness * smoothstep(0.2, 0.5, wet)
                            * (1 - input.extra.y);
                half sheen = max(wet, shore * 0.6) * (1 - 0.8 * input.extra.y) * (1 - smoothstep(120, 450, distance));

                half relief = detailFade * 0.45 * (1 - puddle);
                normalWS = normalize(normalWS - half3(micro.x, 0, micro.y) * relief);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                // Skirts take their shadowing from the surface edge they hang from.
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS + float3(0, max(input.extra.w, 0), 0));
                inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
                inputData.bakedGI = SampleSH(inputData.normalWS);
                // Glowing plants and molten pools nearby light the ground round them.
                inputData.bakedGI += PunchyGlowLight(input.positionWS, inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                half3 albedo = input.surface.rgb * (1 + (micro.z - 0.5) * 0.5 * detailFade);   // grain in the colour too
                albedo *= lerp(1, 0.72, wet);
                albedo *= lerp(1, 0.55, shore * (1 - 0.4 * input.extra.y));
                surface.albedo = lerp(albedo, albedo * 0.6, puddle);
                surface.smoothness = lerp(lerp(input.surface.a, max(input.surface.a, 0.45), sheen), 0.92, puddle);
                surface.metallic = input.extra.x;
                surface.occlusion = 1;
                surface.emission = input.emission * _EmissionScale;
                surface.alpha = 1;

                half4 color = UniversalFragmentPBR(inputData, surface);

                // Puddles mirror the real sky (clouds included), strongest at grazing angles.
                if (puddle > 0.01)
                {
                    float3 v = inputData.viewDirectionWS;
                    float3 r = reflect(-v, half3(0, 1, 0));
                    r.y = max(r.y, 0.03);
                    half fresnel = 0.03 + 0.97 * pow(1 - saturate(v.y), 5);
                    color.rgb = lerp(color.rgb, PunchySkyColor(r, false, true), puddle * lerp(0.35, 0.9, fresnel));
                }

                // Cloud shadows: dim the share of the light that comes straight from the sun.
                half cloudShadow = PunchyCloudShadow(input.positionWS + float3(0, max(input.extra.w, 0), 0));
                if (cloudShadow > 0)
                {
                    Light mainLight = GetMainLight(inputData.shadowCoord);
                    half direct = Luminance(mainLight.color) * saturate(dot(inputData.normalWS, mainLight.direction)) * mainLight.shadowAttenuation;
                    half ambient = Luminance(inputData.bakedGI);
                    color.rgb *= 1 - cloudShadow * direct / max(direct + ambient, 1e-3);
                }

                color.rgb = PunchyAtmosphere(color.rgb, input.positionWS);
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "TerrainSurface.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float cave : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(TerrainAttributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                o.cave = input.morph.w;

                // Collapse skirts onto their top edge so they cast no shadow.
                float3 positionWS = TransformObjectToWorld(TerrainPositionOS(input) + float3(0, max(input.extra.w, 0), 0));
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                TerrainCaveClip(input.cave);
                return 0;
            }
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
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "TerrainSurface.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float cave : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(TerrainAttributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                o.positionCS = TransformObjectToHClip(TerrainPositionOS(input));
                o.cave = input.morph.w;
                return o;
            }

            half Frag(Varyings input) : SV_Target
            {
                TerrainCaveClip(input.cave);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            #include "TerrainSurface.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3  normalWS   : TEXCOORD0;
                float  cave       : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(TerrainAttributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                o.positionCS = TransformObjectToHClip(TerrainPositionOS(input));
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.cave = input.morph.w;
                return o;
            }

            void Frag(Varyings input, out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                TerrainCaveClip(input.cave);
                outNormalWS = half4(NormalizeNormalPerPixel(input.normalWS), 0);
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }
    }
}
