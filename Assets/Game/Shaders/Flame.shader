Shader "Punchy/Flame"
{
    // The standard fire (#131) drawn by FireSystem: per flame, a quad turned to the camera round the flame's own axis
    // (up, leaned by the wind and the caller), shaped into a teardrop with tongues licking up; a spark quad that now
    // and then flies up out of it (the embers); a soft glow round its base; and two smoke puffs rising from its tip.
    // Colours and settings come from the FireEffect asset (Resources/StandardFire) as globals FireSystem sets each
    // frame. Everything moves on the shared clock and the flame's seed, so the caller only says where. The flame,
    // embers and glow are additive (bloom does the halo); the smoke is a second, alpha-blended pass.
    Properties
    {
        _Intensity("Brightness", Float) = 1.8
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Sky.hlsl"
    #include "SharedClock.hlsl"

    CBUFFER_START(UnityPerMaterial)
        half _Intensity;
    CBUFFER_END
    float2 _PunchyWindDirection;
    half _PunchyWind;
    // FireSystem.Upload: outer, middle, core per colour; glow (size, strength); embers (chance, rate, rise);
    // smoke (strength, rise, rate) and its colour.
    float4 _PunchyFireColours[30];
    float4 _PunchyFireGlow;
    float4 _PunchyFireEmbers;
    float4 _PunchyFireSmoke;
    float4 _PunchyFireSmokeColour;

    // Quads: 0 flame, 1 spark (ember), 2 glow, 3 and 4 smoke puffs half a cycle apart.
    #define QUAD_FLAME 0
    #define QUAD_SPARK 1
    #define QUAD_GLOW 2
    #define QUAD_SMOKE 3

    struct Attributes
    {
        float3 positionOS : POSITION;   // the flame's base, in world space (the mesh is drawn untransformed)
        float4 uv0        : TEXCOORD0;  // corner x (-1..1), corner y (0..1), seed 0..1, colour + 10 x quad
        float4 uv1        : TEXCOORD1;  // lean (world xyz, m at the tip), size (height, m)
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float4 uv         : TEXCOORD0;  // corner x, y, seed, colour
        half   fade       : TEXCOORD1;  // sparks, glow and smoke: brightness or thickness now
        half   quad       : TEXCOORD2;
        half   fogFactor  : TEXCOORD3;
    };

    void FireColours(int index, out half3 outer, out half3 mid, out half3 core)
    {
        outer = (half3)_PunchyFireColours[index * 3].rgb;
        mid = (half3)_PunchyFireColours[index * 3 + 1].rgb;
        core = (half3)_PunchyFireColours[index * 3 + 2].rgb;
    }

    // smokePass: this pass draws only the smoke puffs; the other only the flame, spark and glow. A quad that isn't
    // this pass's is collapsed to a point off screen.
    Varyings FireVert(Attributes input, bool smokePass)
    {
        Varyings o = (Varyings)0;
        int quad = (int)floor((input.uv0.w + 0.5) / 10.0);
        bool smoke = quad >= QUAD_SMOKE;
        if (smoke != smokePass)
        {
            o.positionCS = float4(0, 0, -2, 1);
            return o;
        }
        float t = _PunchyClock;
        float seed = input.uv0.z * 97.0;
        float size = max(input.uv1.w, 0.01);
        // The axis: up, bent by the wind (a little) and the caller's lean (a carried torch trailing back).
        float3 windDir = float3(_PunchyWindDirection.x, 0, _PunchyWindDirection.y) * _PunchyWind;
        float3 axis = normalize(float3(0, 1, 0) + input.uv1.xyz / size + windDir * 0.3);
        // Height flickers on two beats; width follows a little behind.
        float flick = 1 + 0.12 * sin(t * 11.0 + seed) + 0.07 * sin(t * 17.3 + seed * 2.1);
        float h = size * flick;
        float3 basePos = input.positionOS;
        float3 centre = basePos + axis * h * 0.5;
        float width = h * 0.22;
        if (quad == QUAD_SPARK)
        {
            // An ember every so often: up out of the tip, drifting with the flame's lean, fading as it goes.
            float cycle = t * _PunchyFireEmbers.y + seed * 0.37;
            float phase = frac(cycle);
            float on = step(frac(sin(floor(cycle) * 12.9898 + seed) * 43758.5453), _PunchyFireEmbers.x);
            float3 jitter = float3(sin(seed * 5.1 + floor(cycle)), 0, cos(seed * 3.7 + floor(cycle))) * 0.15 * size;
            centre = basePos + axis * h * (0.8 + phase * _PunchyFireEmbers.z) + jitter * phase;
            width = size * 0.035;
            o.fade = on * (1 - phase) * smoothstep(0, 0.1, phase);
        }
        else if (quad == QUAD_GLOW)
        {
            // The glow: a soft disc round the lower flame, breathing with it, pulled a little towards the camera so
            // the ground cuts less of it.
            centre = basePos + axis * h * 0.3;
            width = size * _PunchyFireGlow.x;
            centre += normalize(GetCameraPositionWS() - centre) * width * 0.5;
            o.fade = _PunchyFireGlow.y * (0.8 + 0.2 * flick);
        }
        else if (smoke)
        {
            // A puff: up from the tip, swelling and thinning as it climbs, carried off by the wind.
            float cycle = t * _PunchyFireSmoke.z + seed * 0.53 + (quad - QUAD_SMOKE) * 0.5;
            float phase = frac(cycle);
            float rise = phase * _PunchyFireSmoke.y * size;
            float3 sway = float3(sin(seed * 2.3 + floor(cycle) * 1.7), 0, cos(seed * 4.1 + floor(cycle) * 2.9)) * 0.2 * size;
            centre = basePos + axis * h * 0.9 + float3(0, rise, 0) + (windDir * 0.6 + sway) * phase * _PunchyFireSmoke.y * size * 0.5;
            width = size * (0.2 + phase * 0.8);
            o.fade = _PunchyFireSmoke.x * smoothstep(0, 0.2, phase) * (1 - phase);
        }
        float3 view = normalize(GetCameraPositionWS() - centre);
        float3 pos;
        if (quad == QUAD_FLAME)
        {
            float3 right = normalize(cross(axis, view) + float3(1e-4, 0, 0));
            pos = basePos + right * input.uv0.x * width + axis * input.uv0.y * h;
        }
        else
        {
            // Sparks, glow and smoke face the camera square on.
            float3 up = normalize(cross(view, float3(1, 0, 0)) + 1e-4);
            float3 right = normalize(cross(up, view) + float3(1e-4, 0, 0));
            up = normalize(cross(view, right));
            pos = centre + right * input.uv0.x * width + up * (input.uv0.y * 2 - 1) * width;
        }
        o.positionCS = TransformWorldToHClip(pos);
        o.uv = input.uv0;
        o.quad = quad;
        o.fogFactor = ComputeFogFactor(o.positionCS.z);
        return o;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Flame"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            Varyings Vert(Attributes input) { return FireVert(input, false); }

            half4 Frag(Varyings input) : SV_Target
            {
                float t = _PunchyClock;
                float seed = input.uv.z * 97.0;
                int colour = (int)(fmod(input.uv.w + 0.5, 10.0));   // rounded: the interpolated value can land just under
                half3 outer, mid, core;
                FireColours(colour, outer, mid, core);
                half3 color;
                if (input.quad > 1.5)
                {
                    // Glow: soft all the way out.
                    float r = length(float2(input.uv.x, input.uv.y * 2 - 1));
                    float g = saturate(1 - r);
                    color = mid * g * g * input.fade;
                }
                else if (input.quad > 0.5)
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

        Pass
        {
            Name "Smoke"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            Varyings Vert(Attributes input) { return FireVert(input, true); }

            half4 Frag(Varyings input) : SV_Target
            {
                float t = _PunchyClock;
                float seed = input.uv.z * 97.0;
                float2 p = float2(input.uv.x, input.uv.y * 2 - 1);
                // A ragged soft puff: a disc broken up by slowly turning noise.
                float n = PunchyValueNoise(p * 1.8 + float2(seed + input.quad * 3.1, t * 0.3));
                float a = pow(saturate(1 - length(p)), 1.5) * (0.6 + 0.8 * n) * input.fade;
                half3 color = MixFog((half3)_PunchyFireSmokeColour.rgb, input.fogFactor);
                return half4(color, saturate(a));
            }
            ENDHLSL
        }
    }
}
