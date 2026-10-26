Shader "Punchy/Heat Haze"
{
    // Full-screen pass (a Full Screen Pass Renderer Feature, before post-processing). Over hot ground on sunny days:
    //  - shimmer: the image wobbles where the view skims low over hot land, growing with distance;
    //  - mirage: far flats seen at a grazing angle take on the sky's colour in patches, like pools of water.
    // How hot each place is comes from AmbienceSystem's heat map (biome Heat); _PunchyHeat is the sun/weather factor.
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "HeatHaze"

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_PunchyHeatMap); SAMPLER(sampler_PunchyHeatMap);
            float4 _PunchyHeatRect;   // xy origin, zw 1 / size
            float _PunchyHeat;
            half3 _PunchySkyHorizon;

            float HeatAt(float2 xz)
            {
                float2 uv = (xz - _PunchyHeatRect.xy) * _PunchyHeatRect.zw;
                float inside = all(uv > 0) && all(uv < 1) ? 1 : 0;
                return SAMPLE_TEXTURE2D_LOD(_PunchyHeatMap, sampler_PunchyHeatMap, uv, 0).r * inside;
            }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                float a = frac(sin(dot(i, float2(127.1, 311.7))) * 43758.5453);
                float b = frac(sin(dot(i + float2(1, 0), float2(127.1, 311.7))) * 43758.5453);
                float c = frac(sin(dot(i + float2(0, 1), float2(127.1, 311.7))) * 43758.5453);
                float d = frac(sin(dot(i + float2(1, 1), float2(127.1, 311.7))) * 43758.5453);
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float3 WorldAt(float2 uv, out float rawDepth)
            {
                rawDepth = SampleSceneDepth(uv);
                return ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
            }

            bool IsSky(float rawDepth)
            {
            #if UNITY_REVERSED_Z
                return rawDepth <= 1e-6;
            #else
                return rawDepth >= 1 - 1e-6;
            #endif
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half4 original = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                if (_PunchyHeat <= 0.001)
                    return original;

                float raw;
                float3 camera = GetCameraPositionWS();
                float3 world = WorldAt(uv, raw);
                float3 toPoint = world - camera;
                float distance = IsSky(raw) ? 3000 : length(toPoint);
                float3 dir = normalize(toPoint);
                // Where the hot air is: the ground the ray heads over, a little short of what it hits.
                float2 over = camera.xz + dir.xz * min(distance, 3000) * 0.8;
                float heat = HeatAt(over) * _PunchyHeat;
                if (heat <= 0.001)
                    return original;

                // Shimmer: rays skimming the ground pass through the most hot air.
                float grazing = 1 - smoothstep(0.0, 0.14, abs(dir.y));
                float shimmer = heat * grazing * smoothstep(15, 220, distance);
                float t = _Time.y;
                float2 screen = uv * _ScreenParams.xy;
                float2 wobble = float2(Noise(screen * 0.045 + float2(0, t * 2.2)) - 0.5,
                                       Noise(screen * 0.06 + float2(17.3, t * 3.1)) - 0.5);
                float2 offset = wobble * shimmer * float2(2.5, 4.0) / _ScreenParams.xy;
                float2 shifted = uv + offset;
                // Don't drag nearby things into the haze (or the haze onto them).
                float rawShifted;
                float3 shiftedWorld = WorldAt(shifted, rawShifted);
                float shiftedDistance = IsSky(rawShifted) ? 3000 : length(shiftedWorld - camera);
                half4 color = shiftedDistance > distance * 0.7 ? SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, shifted) : original;

                // Mirage: distant ground seen at a grazing angle reflects the sky, in shifting patches.
                if (!IsSky(raw) && dir.y < 0)
                {
                    float patches = smoothstep(0.45, 0.75, Noise(world.xz * 0.004 + t * 0.02));
                    float mirage = heat * patches * smoothstep(250, 700, distance) * (1 - smoothstep(0.004, 0.03, -dir.y));
                    float ripple = 0.85 + 0.15 * Noise(screen * float2(0.02, 0.3) + float2(t * 0.5, 0));
                    color.rgb = lerp(color.rgb, _PunchySkyHorizon * ripple, mirage * 0.55);
                }
                return color;
            }
            ENDHLSL
        }
    }
}
