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

            // W-132 row 4: below the horizon the sky fades to the haze's own colour in this direction (full by 0.7 degrees
            // down, nothing at the horizon), so wherever the drawn ground or the horizon skirt ends, deep in the haze, it
            // meets sky of the same colour and the sun sets on what reads as distant land or sea, not on a cut. The far
            // water's sun road (PunchySunRoad) carries on over it, on the sea the ray would meet past the skirt.
            half3 PunchyBelowHorizon(half3 sky, float3 dir)
            {
                [branch] if (dir.y >= 0)
                    return sky;
                float3 camera = _WorldSpaceCameraPos;
                float2 sea = camera.xz + dir.xz * max(camera.y, 1) / max(-dir.y, 1e-3);
                return lerp(sky, PunchyHazeHorizon(dir), saturate(-dir.y * 80))
                       + PunchySunRoad(float3(dir.x, -dir.y, dir.z), sea);
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
                // (Item 38: with the light shafts through the water in front.)
                [branch] if (_PunchyUnderwater.w > 0)
                    return half4(_PunchyUnderwater.rgb + PunchyUnderwaterShafts(_WorldSpaceCameraPos, dir, 1000), 1);
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
                    return half4(PunchyBelowHorizon(sky, dir), 1);
                // The rain and dust volumes along this direction, marched once a frame into a direction table (R-8; it
                // was over half this draw's cost per pixel).
                [branch] if ((off & 8u) == 0)
                    sky = PunchySkyVolumesFromLut(sky, dir);
                return half4(PunchyBelowHorizon(sky, dir), 1);
            }
            ENDHLSL
        }
    }
}
