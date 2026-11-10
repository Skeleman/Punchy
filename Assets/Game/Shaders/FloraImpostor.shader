Shader "Punchy/FloraImpostor"
{
    // R-7: octahedral impostors for far flora (see FloraImpostor.hlsl and FloraSystem.Impostors.cs). The Bake pass
    // draws a model once per frame of the grid (instanced, one instance per frame) into its tile of the atlases.
    Properties
    {
        // Not a real setting: keeps these draws off the SRP Batcher, as Flora.shader's.
        [HideInInspector] _FloraNoBatcher("", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            // Only drawn by FloraSystem's bake (a command buffer naming this pass), never by a camera.
            Name "ImpostorBake"
            Tags { "LightMode" = "PunchyImpostorBake" }
            Cull Off
            ZTest LEqual
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Flora.hlsl"
            #include "FloraImpostor.hlsl"

            /// The model's bounding sphere: centre height, radius (FloraModel.CentreY, Radius).
            float2 _ImpostorSphere;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 normalOS : TEXCOORD0;
                half4 color : TEXCOORD1;    // rgb albedo, a occlusion
                float2 depth : TEXCOORD2;   // x depth along the view (-1..1), y leafy
                float4 moss : TEXCOORD3;    // FloraMossData
                nointerpolation float3 view : TEXCOORD4;
            };

            Varyings Vert(FloraAttributes input)
            {
                // Instance i draws frame (i % frames, i / frames), looking at the model from that frame's direction,
                // orthographic over the bounding sphere, into that frame's tile.
                float frames = _ImpostorFrames;
                float2 frame = float2(input.instanceID % (uint)frames, input.instanceID / (uint)frames);
                float3 d = ImpostorFrameDirection(frame, frames, _ImpostorFull > 0.5);
                float3 right, up;
                ImpostorBasis(d, right, up);
                float3 p = (input.positionOS - float3(0, _ImpostorSphere.x, 0)) / _ImpostorSphere.y;
                float2 local = float2(dot(p, right), dot(p, up)) * 0.5 + 0.5;
                float2 uv = (frame + local) / frames;
                float depth = dot(p, d);   // towards the eye is nearer

                Varyings o;
                float2 xy = uv * 2 - 1;
                #if UNITY_UV_STARTS_AT_TOP
                xy.y = -xy.y;   // so the atlas is sampled at the same uv it was drawn at
                #endif
                #if UNITY_REVERSED_Z
                o.positionCS = float4(xy, saturate(depth * 0.5 + 0.5), 1);
                #else
                o.positionCS = float4(xy, -depth, 1);
                #endif
                o.normalOS = input.normalOS;
                o.color = half4(input.color.rgb, input.data.w);
                int part = (int)round(input.data.y);
                o.depth = float2(depth, part == 1 || part == 2 || part == 5 ? 1 : 0);
                o.moss = FloraMossData(input);
                o.view = d;
                return o;
            }

            void Frag(Varyings input, out half4 albedo : SV_Target0, out half4 data : SV_Target1)
            {
                FloraMossClip(input.moss);
                // Double-sided like the mesh: the side facing this frame's eye.
                float3 n = SafeNormalize((float3)input.normalOS);
                if (dot(n, input.view) < 0)
                    n = -n;
                albedo = half4(input.color.rgb, 1);
                data = half4(ImpostorPackNormal(n), input.depth.x * 0.5 + 0.5, saturate(input.color.a) * 0.49 + input.depth.y * 0.51);
            }
            ENDHLSL
        }
    }
}
