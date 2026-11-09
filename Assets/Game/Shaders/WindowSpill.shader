Shader "Punchy/Window Spill"
{
    // W-127: the light a lit window throws outside after dark, a soft warm pool on the ground below it and on the walls
    // facing it. No real light and no glow slot: each pane is a box in front of it (36 vertices from SV_VertexID, read
    // from a structured buffer), and every pixel of the scene inside the box is lit by how near it is to the pane, how
    // far it is off the pane's axis and how much it faces the pane (its normal from the depth texture's slopes).
    // Added on top, gone by day with the windows (_SpillOn), faded out towards the edge of the near scan.
    SubShader
    {
        // After the opaque scene (it reads its depth), before the water, which would otherwise be lit through.
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-50" "RenderPipeline" = "UniversalPipeline" }
        Blend One One
        ZWrite Off
        ZTest Always
        Cull Front

        Pass
        {
            Name "WindowSpill"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Pane
            {
                float4 at;       // xyz the pane's middle, w the angle (radians, atan2(z, x)) it faces out along
                float4 light;    // rgb its colour (strength folded in), w its size (the house's scale)
            };
            StructuredBuffer<Pane> _Panes;
            float4 _SpillParams;   // x on (0 by day), y strength, z fade-out start (m from the camera), w fade-out end

            // The box round a pane, in its own frame (x across, y up, z out), times the house's scale.
            static const float Across = 3.2;
            static const float Below = 6.0;
            static const float Above = 1.5;
            static const float Out = 7.0;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                nointerpolation uint pane : TEXCOORD0;
            };

            void Frame(Pane p, out float3 right, out float3 forward)
            {
                forward = float3(cos(p.at.w), 0, sin(p.at.w));
                right = float3(forward.z, 0, -forward.x);
            }

            Varyings Vert(uint vertex : SV_VertexID)
            {
                // Twelve triangles of a cube a pane: corners from the vertex number.
                static const uint Faces[36] = { 0,2,1, 1,2,3, 4,5,6, 5,7,6, 0,1,4, 1,5,4, 2,6,3, 3,6,7, 0,4,2, 2,4,6, 1,3,5, 3,7,5 };
                uint index = vertex / 36;
                uint corner = Faces[vertex % 36];
                Pane p = _Panes[index];
                float3 right, forward;
                Frame(p, right, forward);
                float s = p.light.w;
                float3 c = float3(corner & 1, (corner >> 1) & 1, (corner >> 2) & 1);
                float3 local = float3(lerp(-Across, Across, c.x), lerp(-Below, Above, c.y), lerp(-0.6, Out, c.z)) * s;
                float3 world = p.at.xyz + right * local.x + float3(0, local.y, 0) + forward * local.z;
                Varyings o;
                o.positionCS = TransformWorldToHClip(world);
                o.pane = index;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                Pane p = _Panes[input.pane];
                float2 uv = input.positionCS.xy / _ScaledScreenParams.xy;
                #if UNITY_REVERSED_Z
                    float depth = SampleSceneDepth(uv);
                #else
                    float depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, SampleSceneDepth(uv));
                #endif
                float3 world = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                float3 right, forward;
                Frame(p, right, forward);
                float s = p.light.w;
                float3 v = world - p.at.xyz;
                float x = dot(v, right), z = dot(v, forward);
                // Only in front of the wall (a little halo on the wall round the pane), and not the sky. Faded in over
                // half a metre behind the pane's plane, so a round wall curving away doesn't show a hard edge.
                if (z < -0.5 * s || depth == UNITY_RAW_FAR_CLIP_VALUE)
                    return 0;
                float behind = saturate((z + 0.5 * s) / (0.5 * s));
                float d = length(v);
                float fall = saturate(1 - d / (Out * s));
                fall *= fall;
                // Out through the opening, widening as it goes: a pool, brightest straight out and below.
                float cone = saturate(1 - abs(x) / (z * 0.75 + 1.1 * s));
                float3 n = normalize(cross(ddy(world), ddx(world)));
                float facing = saturate(dot(n, -v / max(d, 1e-3))) * 0.8 + 0.2;
                float fade = 1 - smoothstep(_SpillParams.z, _SpillParams.w, distance(world, _WorldSpaceCameraPos));
                float light = fall * cone * facing * behind * fade * _SpillParams.x * _SpillParams.y;
                return half4(p.light.rgb * light, 0);
            }
            ENDHLSL
        }
    }
}
