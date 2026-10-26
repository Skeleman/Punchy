Shader "Punchy/Pebbles"
{
    // Small faceted stones for GroundDetailSystem, built procedurally per instance. Wet stones darken and shine.
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

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
            #include "GroundDetail.hlsl"

            StructuredBuffer<Pebble> _Pebbles;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                nointerpolation half3 normalWS : TEXCOORD1;
                nointerpolation half3 albedo   : TEXCOORD2;
            };

            Varyings Vert(uint vertex : SV_VertexID, uint instance : SV_InstanceID)
            {
                Pebble pebble = _Pebbles[instance];
                Varyings o;
                float3 normal;
                BuildPebbleVertex(pebble, instance, vertex, o.positionWS, normal);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = normal;
                o.albedo = pebble.color.rgb;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half wet = PunchyWetnessAt(input.positionWS.xz).r;
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalize(input.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.bakedGI = SampleSH(inputData.normalWS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = input.albedo * lerp(1, 0.6, wet);
                surface.smoothness = lerp(0.25, 0.7, wet);
                surface.occlusion = 1;
                surface.alpha = 1;
                half3 color = UniversalFragmentPBR(inputData, surface).rgb;
                color *= 1 - 0.6 * PunchyCloudShadow(input.positionWS);
                return half4(PunchyAtmosphere(color, input.positionWS), 1);
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "GroundDetail.hlsl"

            StructuredBuffer<Pebble> _Pebbles;

            float4 Vert(uint vertex : SV_VertexID, uint instance : SV_InstanceID) : SV_POSITION
            {
                float3 positionWS, normal;
                BuildPebbleVertex(_Pebbles[instance], instance, vertex, positionWS, normal);
                return TransformWorldToHClip(positionWS);
            }

            half Frag(float4 positionCS : SV_POSITION) : SV_Target { return positionCS.z; }
            ENDHLSL
        }
    }
}
