Shader "Punchy/WideShadow"
{
    // R-5: the wide cached sun-shadow map (WideShadow.cs) is laid with this shader: drawn from the sun into an R16
    // texture with a min blend, each texel keeps how far along the sunlight (0-1 over the view's depth) the nearest
    // caster is: the coarse terrain, the colossi and the huge landmarks. The terrain and the flora read the map
    // themselves (Shaders/WideShadow.hlsl). Loaded with Resources.Load.
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            // Only drawn by WideShadow's command buffer (its light mode matches no URP pass, so the renderer skips it).
            Name "WideShadowCaster"
            Tags { "LightMode" = "WideShadowCaster" }
            BlendOp Min
            Blend One One
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4x4 _WideShadowVP;    // world to the sun view's clip space
            float4 _WideShadowEye;     // xyz the view's near plane centre, w one over its depth
            float4 _WideShadowSun;     // xyz the way the sunlight travels

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float along : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = mul(_WideShadowVP, float4(positionWS, 1));
                o.along = dot(positionWS - _WideShadowEye.xyz, _WideShadowSun.xyz) * _WideShadowEye.w;
                return o;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                return saturate(input.along).xxxx;
            }
            ENDHLSL
        }
    }
}
