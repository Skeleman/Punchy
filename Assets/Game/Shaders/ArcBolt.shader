Shader "Punchy/ArcBolt"
{
    // Electric bolts between arcing spires (FloraSystem.DrawBolts): one instance per bolt firing this frame (the pairs'
    // rhythms are worked out there, so the crackle lands on the strike), its object space running from one end (z 0)
    // to the other (z 1). The mesh is two crossed ribbons along the spine; while it lasts the bolt flickers and its
    // spine jumps to a new jagged path several times a second, from the shared clock and its position, so every
    // player sees the same bolt. Additive and unlit: no lights, no depth.
    Properties
    {
        [HDR] _Color("Colour", Color) = (3.5, 4.5, 6, 1)
        _Width("Half width (m)", Float) = 0.06
        _Jag("Jaggedness (m)", Float) = 0.9
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
                // Flickering, and jumping to a new path about eight times a second.
                float strike = floor(_PunchyClock * 8 + seed * 13);
                half flicker = Hash(floor(_PunchyClock * 40) + seed * 91) > 0.25 ? 1 : 0;
                o.fire = flicker;

                // A jagged path: the spine bent at a dozen points, pinned at both ends.
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
