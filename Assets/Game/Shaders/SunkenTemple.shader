Shader "Punchy/SunkenTemple"
{
    // W-113 (item 12): the sunken temple's stone and sand. As Punchy/Colossus (vertex colour x tint, terrain haze),
    // plus a world-space texture so the walls aren't flat colour: sandstone courses of staggered blocks with sunken
    // mortar joints, each block a little lighter or darker and pitted (_Blocks 1), or wind ripples and grain on sand
    // (_Blocks 0). The rooms are lit by their wall torches through the glow lights (Glow.hlsl), as caves are.
    Properties
    {
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _Blocks ("Blocks (1) or sand (0)", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
        half4 _BaseColor;
        half _Blocks;
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
            #include "Glow.hlsl"

            float TempleHash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            float TempleNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(TempleHash(i), TempleHash(i + float2(1, 0)), f.x),
                            lerp(TempleHash(i + float2(0, 1)), TempleHash(i + float2(1, 1)), f.x), f.y);
            }

            // Ashlar on one projection plane (uv in metres, uv.y up the courses): 0.55 m courses of 0.9-1.3 m blocks,
            // staggered course to course. Returns the albedo factor; mortar lines shade the normal a little too.
            half Ashlar(float2 uv, out float joint)
            {
                float course = floor(uv.y / 0.55);
                float x = uv.x + course * 0.47;
                float len = lerp(0.9, 1.3, TempleHash(float2(course, 7.3)));
                float block = floor(x / len);
                float2 f = float2(frac(x / len) * len, frac(uv.y / 0.55) * 0.55);
                float edge = min(min(f.x, len - f.x), min(f.y, 0.55 - f.y));
                joint = 1 - smoothstep(0.015, 0.04, edge);
                float id = TempleHash(float2(block, course));
                half tone = 0.86 + 0.24 * id;
                // Worn, pitted faces: fine noise, darker towards chipped edges.
                tone *= 0.9 + 0.1 * TempleNoise(uv * 9 + id * 13) + 0.05 * TempleNoise(uv * 31);
                tone *= lerp(1, 0.9, 1 - smoothstep(0.04, 0.12, edge));
                return tone * lerp(1, 0.45, joint);
            }

            half Pattern(float3 p, half3 n)
            {
                float3 w = abs(n);
                if (_Blocks < 0.5)
                {
                    // Sand: ripples across the wind and a fine grain.
                    float rip = sin(p.x * 2.1 + p.z * 0.7 + TempleNoise(p.xz * 0.6) * 4) * 0.5 + 0.5;
                    return 0.92 + 0.08 * rip * w.y + 0.06 * TempleNoise(p.xz * 23 + p.y * 7);
                }
                float j0, j1, j2;
                half a = Ashlar(p.zy, j0), b = Ashlar(p.xy, j1);
                // Floors and tops: square flagstones from the same pattern.
                half c = Ashlar(p.xz, j2);
                w = pow(w, 4);
                w /= w.x + w.y + w.z;
                return a * w.x + b * w.z + c * w.y;
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half3 albedo : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.albedo = input.color.rgb * _BaseColor.rgb;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalize(input.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.bakedGI = SampleSH(inputData.normalWS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = input.albedo * Pattern(input.positionWS, inputData.normalWS);
                surface.smoothness = 0.08;
                surface.occlusion = 1;
                surface.alpha = 1;
                half3 color = UniversalFragmentPBR(inputData, surface).rgb;
                // The wall torches (CaveSystem's glow lights).
                color += surface.albedo * PunchyGlowLight(input.positionWS, inputData.normalWS);
                color *= 1 - 0.6 * PunchyCloudShadow(input.positionWS);
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
