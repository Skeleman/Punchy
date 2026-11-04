Shader "Punchy/Gas Puff"
{
    // A skunk's gas cloud (SkunkGas, A-43): the soft particle's round, wispy puff, but unlit (its colour as given, only
    // seen through the atmosphere) and with no fade where it meets the ground, so a cloud hanging low over dark ground
    // stays plainly visible. Punchy/Soft Particle lights its puffs dimly and fades them near geometry, which hid it.
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "GasPuff"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Sky.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                half4 color : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = input.uv;
                o.color = input.color;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2 - 1;
                float r = length(p);
                // Wisps: value noise over the puff (particles spin randomly, so each looks different).
                float wisp = PunchyValueNoise(p * 2.5 + 7.3) * 0.6 + PunchyValueNoise(p * 5.0 - 3.1) * 0.4;
                half alpha = pow(saturate(1 - r), 1.2) * lerp(0.6, 1.0, wisp) * input.color.a;
                clip(alpha - 0.002);
                // (A touch of the sky's light so it isn't glowing at night, but never dimmer than 70% of its colour.)
                half3 color = input.color.rgb * max(0.7, saturate(SampleSH(half3(0, 1, 0)).g * 1.5));
                return half4(PunchyAtmosphere(color, input.positionWS), alpha);
            }
            ENDHLSL
        }
    }
}
