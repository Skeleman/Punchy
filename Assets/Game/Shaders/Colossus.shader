Shader "Punchy/Colossus"
{
    // Flat-shaded, vertex-coloured solid for the Dunes colossus (DunesColossus builds its meshes in code).
    // _BaseColor tints the vertex colour (W-77: the salt tower and the sunken temple draw with it so they fade into
    // the haze like the terrain, instead of URP Lit's plain fog).
    Properties
    {
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        // A-89: 1 makes vertex alpha below 1 glow (emission = albedo x (1 - alpha) x _GlowStrength), so lava, embers and
        // spores light up at night. 0 (the default) ignores alpha: the ruins and driftwood keep other data there.
        _VertexGlow ("Vertex alpha glows", Float) = 0
        _GlowStrength ("Glow strength", Float) = 1.2
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
        half4 _BaseColor;
        half _VertexGlow;
        half _GlowStrength;
        CBUFFER_END
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
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #define _ENVIRONMENTREFLECTIONS_OFF

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Sky.hlsl"
            // W-166 row 4: a vertex may name a material family (uv3: x the MaterialFamily, y its kind); 0 keeps the
            // flat vertex colour, so meshes with no uv3 draw exactly as before. Stone reads the shared detail texture.
            TEXTURE2D(_PunchyCaveDetail);
            SAMPLER(sampler_PunchyCaveDetail);
            #include "Materials/Library.hlsl"
            #include "Materials/Metal.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                float2 family : TEXCOORD3;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half3 albedo : TEXCOORD2;
                half glow : TEXCOORD3;
                // The pattern frame: object space at the object's own scale, so a walking colossus's pattern rides on it.
                float3 patternPos : TEXCOORD4;
                half3 normalOS : TEXCOORD5;
                nointerpolation half2 family : TEXCOORD6;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.albedo = input.color.rgb * _BaseColor.rgb;
                o.glow = saturate(1 - input.color.a) * _VertexGlow;
                float3 scale = float3(length(UNITY_MATRIX_M._m00_m10_m20), length(UNITY_MATRIX_M._m01_m11_m21), length(UNITY_MATRIX_M._m02_m12_m22));
                o.patternPos = input.positionOS.xyz * scale;
                o.normalOS = input.normalOS;
                o.family = input.family;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalize(input.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half3 albedo = input.albedo;
                [branch] if (input.family.x > 0.5)
                {
                    float footprint = distance(input.positionWS, GetCameraPositionWS()) * 2 / (_ScreenParams.y * UNITY_MATRIX_P._m11);
                    half3 n = normalize(input.normalOS);
                    float3 v = normalize(TransformWorldToObjectDir(inputData.viewDirectionWS));
                    half r = frac(input.family.y * 0.618 + 0.3);
                    albedo = PunchyLibraryAlbedo((int)round(input.family.x), (int)round(input.family.y), input.patternPos, n, v, footprint, r, albedo);
                    inputData.normalWS = normalize(TransformObjectToWorldNormal(n));
                }
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.bakedGI = SampleSH(inputData.normalWS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                // Metal (the library's one metal look, Metal.hlsl): dim diffuse, the colour mostly from tinted sky
                // reflections, a sharp sun highlight and glints (riding on the piece: hashed in its pattern frame).
                bool metal = (int)round(input.family.x) == 6;
                surface.albedo = metal ? albedo * 0.45 : albedo;
                surface.smoothness = 0.08;
                surface.occlusion = 1;
                surface.alpha = 1;
                half3 color = UniversalFragmentPBR(inputData, surface).rgb;
                [branch] if (metal)
                {
                    Light light = GetMainLight(inputData.shadowCoord);
                    color += PunchyMetalShade(albedo, inputData.normalWS, inputData.viewDirectionWS, input.patternPos, light.color,
                                              light.direction, light.shadowAttenuation * light.distanceAttenuation, 1, 0);
                }
                color *= 1 - 0.6 * PunchyCloudShadow(input.positionWS);
                color += input.albedo * (input.glow * _GlowStrength); // self-lit: no shadow, still hazed below
                return half4(PunchyAtmosphere(color, input.positionWS), 1);
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            float4 Vert(float4 positionOS : POSITION, float3 normalOS : NORMAL) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(normalOS);
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE * positionCS.w);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE * positionCS.w);
            #endif
                return positionCS;
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 Vert(float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }

            half Frag(float4 positionCS : SV_POSITION) : SV_Target { return positionCS.z; }
            ENDHLSL
        }

        // Depth and normals for the depth-normals prepass (with depth priming the opaque pass only draws what it drew).
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
            };

            Varyings Vert(float4 positionOS : POSITION, float3 normalOS : NORMAL)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(normalOS);
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
