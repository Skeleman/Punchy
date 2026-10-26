Shader "Punchy/Ambient Glow"
{
    // Glowing ambient particles (sparkles, spores, embers): additive points of HDR light, twinkling, so bloom
    // gives them a halo.
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "AmbientGlow"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "AmbientParticles.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half3 glow : TEXCOORD1;
            };

            Varyings Vert(uint vertex : SV_VertexID, uint instance : SV_InstanceID)
            {
                ParticleVertex p = BuildParticle(vertex, instance);
                Varyings o;
                o.positionCS = TransformWorldToHClip(p.positionWS);
                o.uv = p.uv;
                o.glow = _ParticleColor.rgb * p.visibility * p.twinkle;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float d2 = dot(input.uv, input.uv);
                half intensity = exp(-d2 * 30.0) * 2.0 + exp(-d2 * 6.0) * 0.25;
                return half4(input.glow * intensity, 0);
            }
            ENDHLSL
        }
    }
}
