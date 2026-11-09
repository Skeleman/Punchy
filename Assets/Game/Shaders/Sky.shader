Shader "Punchy/Sky"
{
    // Procedural skybox: gradient, sun, moon and stars, cirrus, volumetric clouds, then rain and dust haze, all
    // driven by WeatherSystem globals and world weather maps.
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Sky.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 direction : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.direction = input.positionOS.xyz;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Distant sandstorms and rain curtains show against the sky too.
                float3 dir = normalize(input.direction);
                uint off = (uint)_PunchySkyOff;   // (test only, -skyoff: see Sky.hlsl)
                half3 sky = (off & 4u) == 0 ? PunchySkyColor(dir, true, false) : half3(0.3, 0.45, 0.7);
                if ((off & 1u) == 0)
                    sky = PunchyVolumetricClouds(sky, dir, input.positionCS.xy);
                if ((off & 32u) != 0)
                    return half4(sky, 1);
                // (Same step count as the terrain haze target, so distant land and sky converge exactly.)
                return half4((off & 8u) == 0 ? PunchyWeatherVolumes(sky, _WorldSpaceCameraPos, dir, 8000, 8) : sky, 1);
            }
            ENDHLSL
        }
    }
}
