Shader "Punchy/FarShadow"
{
    // W-77b (#84): the far shadow proxy for the huge landmarks (FarShadow.cs). A ground-hugging strip that darkens
    // what is under it (multiply blend), only past the real shadow distance, and fades with the haze so it never
    // shows as a dark patch from high up. Vertex alpha is the strip's own fade towards its tip and sides.
    Properties
    {
        _Strength ("Strength", Range(0, 1)) = 0.6
        _Near ("Fade in from (m)", Float) = 40
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-50" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend DstColor Zero
            ZWrite Off
            ZTest LEqual
            Cull Off
            Offset -2, -2

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Sky.hlsl"

            CBUFFER_START(UnityPerMaterial)
            half _Strength;
            float _Near;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half alpha : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.alpha = input.color.a;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // PunchyAtmosphere is affine in the colour, so what it lets through is A(1) - A(0).
                half3 through = saturate(PunchyAtmosphere(half3(1, 1, 1), input.positionWS) - PunchyAtmosphere(half3(0, 0, 0), input.positionWS));
                float d = distance(input.positionWS, _WorldSpaceCameraPos);
                // W-82 (#84): the square root of what the haze lets through, not the plain value. A strip 1 km off kept
                // only ~6% darkening (invisible) although the ground there still showed through the haze; this keeps it
                // ~23% there and still fades it to nothing where the haze is all that is left.
                half a = _Strength * input.alpha * smoothstep(_Near, _Near + 15, d) * sqrt(dot(through, half3(0.333, 0.333, 0.334)));
                return half4((1 - a).xxx, 1);
            }
            ENDHLSL
        }
    }
}
