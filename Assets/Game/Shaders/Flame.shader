Shader "Punchy/Flame"
{
    // Flames (torches, cave torches, campfires) drawn by FireSystem: one quad per flame turned to the camera round the
    // flame's own axis (up, leaned by the wind and the caller), shaped into a teardrop with tongues licking up, and a
    // spark quad that now and then flies up out of it. Everything moves on the shared clock and the flame's seed, so
    // the caller only says where. Emissive and additive (bloom does the glow): no real lights.
    Properties
    {
        _Intensity("Brightness", Float) = 1.8
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Flame"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Sky.hlsl"
            #include "SharedClock.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Intensity;
            CBUFFER_END
            float2 _PunchyWindDirection;
            half _PunchyWind;

            struct Attributes
            {
                float3 positionOS : POSITION;   // the flame's base, in world space (the mesh is drawn untransformed)
                float4 uv0        : TEXCOORD0;  // corner x (-1..1), corner y (0..1), seed 0..1, palette + 10 for a spark
                float4 uv1        : TEXCOORD1;  // lean (world xyz, m at the tip), size (height, m)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 uv         : TEXCOORD0;  // corner x, y, seed, palette
                half   fade       : TEXCOORD1;  // sparks: brightness over their flight
                half   spark      : TEXCOORD2;
                half   fogFactor  : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                float t = _PunchyClock;
                float seed = input.uv0.z * 97.0;
                float size = max(input.uv1.w, 0.01);
                float spark = input.uv0.w >= 10 ? 1 : 0;
                // The axis: up, bent by the wind (a little) and the caller's lean (a carried torch trailing back).
                float3 wind = float3(_PunchyWindDirection.x, 0, _PunchyWindDirection.y) * _PunchyWind * 0.3;
                float3 axis = normalize(float3(0, 1, 0) + input.uv1.xyz / size + wind);
                // Height flickers on two beats; width follows a little behind.
                float h = size * (1 + 0.12 * sin(t * 11.0 + seed) + 0.07 * sin(t * 17.3 + seed * 2.1));
                float3 basePos = input.positionOS;
                float3 centre = basePos + axis * h * 0.5;
                float width = h * 0.22;
                if (spark > 0)
                {
                    // A spark every so often: up out of the tip, drifting with the flame's lean, fading as it goes.
                    float cycle = t * 0.8 + seed * 0.37;
                    float phase = frac(cycle);
                    float on = step(frac(sin(floor(cycle) * 12.9898 + seed) * 43758.5453), 0.4);
                    float3 jitter = float3(sin(seed * 5.1 + floor(cycle)), 0, cos(seed * 3.7 + floor(cycle))) * 0.15 * size;
                    centre = basePos + axis * h * (0.8 + phase * 2.4) + jitter * phase;
                    width = size * 0.035;
                    o.fade = on * (1 - phase) * smoothstep(0, 0.1, phase);
                }
                float3 view = normalize(GetCameraPositionWS() - centre);
                float3 up = spark > 0 ? normalize(cross(view, float3(1, 0, 0)) + 1e-4) : axis;
                float3 right = normalize(cross(up, view) + float3(1e-4, 0, 0));
                if (spark > 0)
                    up = normalize(cross(view, right));
                float3 pos = spark > 0
                    ? centre + right * input.uv0.x * width + up * (input.uv0.y * 2 - 1) * width
                    : basePos + right * input.uv0.x * width + axis * input.uv0.y * h;
                o.positionCS = TransformWorldToHClip(pos);
                o.uv = input.uv0;
                o.spark = spark;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            // Palettes: outer, middle and core colours (linear). 0 wood fire, 1 blue, 2 green, 3 violet.
            void Palette(int index, out half3 outer, out half3 mid, out half3 core)
            {
                if (index == 1)      { outer = half3(0.05, 0.12, 0.9);  mid = half3(0.2, 0.5, 1.0);  core = half3(0.8, 0.95, 1.0); }
                else if (index == 2) { outer = half3(0.05, 0.6, 0.08);  mid = half3(0.35, 1.0, 0.3); core = half3(0.85, 1.0, 0.75); }
                else if (index == 3) { outer = half3(0.35, 0.05, 0.8);  mid = half3(0.75, 0.3, 1.0); core = half3(1.0, 0.85, 1.0); }
                else                 { outer = half3(0.9, 0.12, 0.01);  mid = half3(1.0, 0.45, 0.05); core = half3(1.0, 0.9, 0.55); }
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float t = _PunchyClock;
                float seed = input.uv.z * 97.0;
                int palette = (int)fmod(input.uv.w, 10);
                half3 outer, mid, core;
                Palette(palette, outer, mid, core);
                half3 color;
                if (input.spark > 0)
                {
                    float r = length(float2(input.uv.x, input.uv.y * 2 - 1));
                    color = lerp(mid, core, 0.5) * saturate(1 - r) * input.fade * 2;
                }
                else
                {
                    float x = input.uv.x, y = input.uv.y;
                    // Tongues: the flame's body wavers sideways more towards the top, in rising noise.
                    float n = PunchyValueNoise(float2(x * 1.7 + seed, y * 2.6 - t * 2.4));
                    float n2 = PunchyValueNoise(float2(x * 3.1 - seed, y * 5.0 - t * 3.7));
                    float xx = x + (n - 0.5) * 0.55 * y + sin(y * 6.0 - t * 8.0 + seed) * 0.1 * y;
                    // Teardrop: rounded at the base, widest a quarter of the way up, tapering to a tip torn by the noise.
                    float width = sqrt(saturate((y + 0.04) / 0.3)) * pow(saturate(1.0 - y), 1.3) * 0.85;
                    float d = abs(xx) / max(width, 1e-3);
                    float body = smoothstep(1.0, 0.45, d);
                    float top = 0.9 + 0.1 * n2;
                    body *= smoothstep(top, top - 0.55, y);
                    // The hot core: low and in the middle.
                    float hot = smoothstep(0.6, 0.1, d) * smoothstep(0.45, 0.05, y) * 0.8;
                    color = lerp(outer, mid, smoothstep(0.35, 0.95, body) * smoothstep(0.85, 0.2, y));
                    color = lerp(color, core, hot);
                    color *= body * (0.85 + 0.3 * n2);
                }
                color *= _Intensity;
                color = MixFogColor(color, half3(0, 0, 0), input.fogFactor);
                return half4(color, 0);
            }
            ENDHLSL
        }
    }
}
