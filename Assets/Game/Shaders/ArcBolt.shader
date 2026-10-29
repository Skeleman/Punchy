Shader "Punchy/ArcBolt"
{
    // Electric bolts between arcing spires (FloraSystem.DrawBolts): one instance per pair of spires, its object space
    // running from one tip (z 0) to the other (z 1). The mesh is two crossed ribbons along the spine; each strike the
    // spine jumps to a new jagged path. Every pair fires on its own rhythm from the shared clock (every few seconds to
    // most of a minute, for a fifth of a second or so, flickering), seeded by its position, so every player sees the
    // same bolts. Additive and unlit: no lights, no depth.
    Properties
    {
        [HDR] _Color("Colour", Color) = (3.5, 4.5, 6, 1)
        _Width("Half width (m)", Float) = 0.06
        _Jag("Jaggedness (m)", Float) = 0.9
        [Toggle] _AlwaysOn("Always on (checking only)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Bolt"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SharedClock.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Width;
                float _Jag;
                float _AlwaysOn;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;   // only z is used: along the bolt
                float4 uv : TEXCOORD0;          // x along 0..1, y side -1/1, z which ribbon (0/1)
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half fire : TEXCOORD0;
                half edge : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float Hash(float n) { return frac(sin(n) * 43758.5453); }

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings o = (Varyings)0;
                float3 origin = float3(UNITY_MATRIX_M[0].w, UNITY_MATRIX_M[1].w, UNITY_MATRIX_M[2].w);
                float seed = Hash(dot(round(origin), float3(12.9898, 78.233, 37.719)));
                // This pair's rhythm: how often it fires and for how long.
                float period = lerp(5, 45, seed);
                float duration = lerp(0.1, 0.4, Hash(seed * 7.3));
                float t = _PunchyClock + seed * period;
                float strike = floor(t / period);
                float since = t - strike * period;
                half flicker = Hash(floor(_PunchyClock * 40) + seed * 91) > 0.25 ? 1 : 0;
                o.fire = since < duration || _AlwaysOn > 0.5 ? flicker : 0;

                // A new jagged path each strike: the spine bent at a dozen points, pinned at both tips.
                float along = input.uv.x;
                float k = floor(along * 12);
                float f = frac(along * 12);
                float2 j0 = float2(Hash(k * 3.1 + strike * 17.7 + seed * 5), Hash(k * 7.9 + strike * 3.3 + seed * 11)) - 0.5;
                float2 j1 = float2(Hash((k + 1) * 3.1 + strike * 17.7 + seed * 5), Hash((k + 1) * 7.9 + strike * 3.3 + seed * 11)) - 0.5;
                float pin = sin(along * PI);
                float2 jag = lerp(j0, j1, f) * _Jag * 2 * pin;
                float3 p = float3(jag, along);
                // In metres across, whatever the bolt's length (the matrix stretches z only).
                float3 side = input.uv.z < 0.5 ? float3(1, 0, 0) : float3(0, 1, 0);
                p += side * input.uv.y * _Width;
                o.positionCS = TransformObjectToHClip(p);
                o.edge = abs(input.uv.y);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                clip(input.fire - 0.5);
                half core = 1 - input.edge * 0.6;
                return half4(_Color.rgb * core, 1);
            }
            ENDHLSL
        }
    }
}
