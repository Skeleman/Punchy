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
            // On while this camera has the clouds' low-resolution buffer (LowResEffectFeature's ActiveKeyword). The
            // full-resolution march fallback is compiled out of that variant: inlined, its registers lowered occupancy for
            // every sky pixel even when the branch was never taken (job 38: DrawSkybox 3.7 ms at any buffer size).
            #pragma multi_compile_fragment _ PUNCHY_CLOUDS_LOW_RES
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Sky.hlsl"
            #include "LowResEffect.hlsl"

            // The clouds' low-resolution buffer (LowResEffectFeature, CloudsLowRes.shader), when this camera has it.
            Texture2D<half4> _PunchyCloudLowRes;
            Texture2D<float> _PunchyCloudLowRes_Depth;
            float4 _PunchyCloudLowRes_Size;

            // The clouds from the buffer, over the sky and the far layer (both drawn here at full resolution, so the sun, the
            // moon and the stars stay crisp). The skybox only draws on sky, so a depth-aware read can't pick up a tree.
            half3 PunchyCloudsFromBuffer(half3 background, float3 dir, float2 pixel)
            {
                if (dir.y <= 0.003)
                    return background;
                if (((uint)_PunchySkyOff & 2u) == 0)
                    background = PunchyFarClouds(background, dir);
                half4 clouds = PunchyLowResUpsample(_PunchyCloudLowRes, _PunchyCloudLowRes_Depth, _PunchyCloudLowRes_Size, pixel, PUNCHY_LOW_RES_SKY);
                return background * clouds.a + clouds.rgb;
            }

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
                // Under water the sky is endlessly far through the water's fog, and the surface's underside (Snell's
                // window, Water.shader) covers it anyway: skip the clouds and weather marches (job 30: with the water off,
                // the underwater frame was still 27-29 ms, mostly sky).
                [branch] if (_PunchyUnderwater.w > 0)
                    return half4(_PunchyUnderwater.rgb, 1);
                uint off = (uint)_PunchySkyOff;   // (test only, -skyoff: see Sky.hlsl)
                // (Real branches: a ternary would run both sides, and the point of the test bits is to skip the work.)
                half3 sky = half3(0.3, 0.45, 0.7);
                [branch] if ((off & 4u) == 0)
                    sky = PunchySkyColor(dir, true, false);
                [branch] if ((off & 1u) == 0)
                {
                #if defined(PUNCHY_CLOUDS_LOW_RES)
                    sky = PunchyCloudsFromBuffer(sky, dir, input.positionCS.xy);
                #else
                    sky = PunchyVolumetricClouds(sky, dir, input.positionCS.xy);
                #endif
                }
                if ((off & 32u) != 0)
                    return half4(sky, 1);
                // (Same step count as the terrain haze target, so distant land and sky converge exactly.)
                [branch] if ((off & 8u) == 0)
                    sky = PunchyWeatherVolumes(sky, _WorldSpaceCameraPos, dir, 8000, 8);
                return half4(sky, 1);
            }
            ENDHLSL
        }
    }
}
