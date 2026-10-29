Shader "Punchy/Grass Blades"
{
    // Grass blades for GroundDetailSystem: procedural, double-sided, wind-blown, lit with soft wrap lighting,
    // light shining through the blades, cloud shadows, wetness and the world atmosphere.
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Cull Off

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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Sky.hlsl"
            #include "Glow.hlsl"
            #include "GroundDetail.hlsl"

            StructuredBuffer<Blade> _Blades;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
                half4  color      : TEXCOORD2;   // rgb albedo, a = height fraction along the blade
            };

            Varyings Vert(uint vertex : SV_VertexID, uint instance : SV_InstanceID)
            {
                Blade blade = _Blades[instance];
                Varyings o;
                float t;
                float3 normal;
                BuildBladeVertex(blade, instance, vertex, o.positionWS, normal, t);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = normal;
                o.color = half4(blade.color.rgb, t);
                return o;
            }

            half4 Frag(Varyings input, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                half3 n = normalize(frontFace ? input.normalWS : -input.normalWS);
                n = normalize(n + half3(0, 0.6, 0));   // soften: blades read as a canopy, not as flat cards
                float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half t = input.color.a;

                // Darker in the shaded base of the tuft, lighter at the tips.
                half3 albedo = input.color.rgb * lerp(0.45, 1.15, t);
                albedo *= lerp(1, 0.8, PunchyWetnessAt(input.positionWS.xz).r);

                Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half shadow = light.shadowAttenuation * (1 - PunchyCloudShadow(input.positionWS));
                half wrap = saturate(dot(n, light.direction) * 0.6 + 0.4);
                half through = pow(saturate(dot(-v, light.direction)), 4) * t * 0.6;   // sunlight through the blade
                // Glowing plants and molten pools nearby light the grass round them too.
                half3 color = albedo * (light.color * shadow * (wrap + through) + SampleSH(n) + PunchyGlowLight(input.positionWS, n));

                color = PunchyAtmosphere(color, input.positionWS);
                return half4(color, 1);
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

            StructuredBuffer<Blade> _Blades;

            float4 Vert(uint vertex : SV_VertexID, uint instance : SV_InstanceID) : SV_POSITION
            {
                float3 positionWS, normal;
                float t;
                BuildBladeVertex(_Blades[instance], instance, vertex, positionWS, normal, t);
                return TransformWorldToHClip(positionWS);
            }

            half Frag(float4 positionCS : SV_POSITION) : SV_Target { return positionCS.z; }
            ENDHLSL
        }
    }
}
