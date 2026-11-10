Shader "Punchy/Town Lights"
{
    // R-10 row 4b: a lit town's windows and street lights seen from afar at night (SurfaceGlow.TownLights.cs). One
    // camera-facing quad a light (6 vertices from SV_VertexID, read from a structured buffer): a bright warm core at
    // least a few pixels across with a faint wide halo, so a far town's lights run together into a glowing cluster;
    // HDR bright so the bloom adds to it, hidden by hills, thinned by the haze much less than the land, and faded in
    // past a couple of hundred metres where the town's own lights take over. Hidden by its own soft depth test, not the
    // depth buffer's: a light is only hidden by what's well in front of it (more so the farther it is), since the far
    // land's coarse mesh and its own house both stand over it. Each light a little brighter or dimmer than the next
    // (hashed from where it is), so a town isn't even.
    SubShader
    {
        // After the water (Transparent-10), which isn't in the depth texture, so a town across a bay isn't covered.
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-4" "RenderPipeline" = "UniversalPipeline" }
        Blend One One
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "TownLights"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            StructuredBuffer<float4> _TownLights;   // xyz where it's seen, w its light (negative: a street light)
            float4 _TownLightParams;                // x brightness (windows on folded in)
            float4 _TownWindowColor, _TownLampColor;
            float _PunchyHazeDensity;               // (Sky.hlsl) the haze per metre

            // Sizes (m at light 1), the least it's drawn (pixels), and the fade-in (m from the camera).
            static const float WindowSize = 1.4, LampSize = 1.1, MinPixels = 5, FadeFrom = 150, FadeTo = 280;
            // The halo round each light: how far it reaches (in light sizes) and how bright it is next to the light's core.
            static const float HaloReach = 4, HaloShare = 0.15;
            // How far in front of a light the scene must be to hide it (m, and a share of its distance).
            static const float HideFrom = 12, HideShare = 0.025;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 quad : TEXCOORD0;
                nointerpolation half3 color : TEXCOORD1;
                nointerpolation float depth : TEXCOORD2;   // the light's eye depth, less how much the scene may stand over it
            };

            float Hash(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            Varyings Vert(uint vertex : SV_VertexID)
            {
                static const float2 Corners[6] = { float2(-1, -1), float2(1, -1), float2(-1, 1), float2(-1, 1), float2(1, -1), float2(1, 1) };
                float4 lightAt = _TownLights[vertex / 6];
                float2 corner = Corners[vertex % 6];
                float3 toCamera = _WorldSpaceCameraPos - lightAt.xyz;
                float dist = length(toCamera);
                float3 at = lightAt.xyz;
                bool lamp = lightAt.w < 0;
                float light = abs(lightAt.w);
                // At least MinPixels across: a pixel's width at that distance, from the projection.
                float pixel = 2 * dist / (UNITY_MATRIX_P[1][1] * _ScaledScreenParams.y);
                float size = max((lamp ? LampSize : WindowSize) * sqrt(light), MinPixels * pixel) * 0.5;
                float3 right = UNITY_MATRIX_V[0].xyz, up = UNITY_MATRIX_V[1].xyz;
                Varyings o;
                o.positionCS = TransformWorldToHClip(at + (right * corner.x + up * corner.y) * (size * HaloReach));
                // A town 2-3 km off can lie past the far plane (3 km): pinned onto it instead of clipped.
                #if UNITY_REVERSED_Z
                    o.positionCS.z = max(o.positionCS.z, 0);
                #else
                    o.positionCS.z = min(o.positionCS.z, o.positionCS.w);
                #endif
                o.quad = corner * HaloReach;   // in light sizes
                o.depth = -TransformWorldToView(at).z - (HideFrom + dist * HideShare);
                // Thinned by the haze a twentieth as much as the land (it's a light, and a town should read 3 km off), faded in past FadeFrom.
                float h = dist * _PunchyHazeDensity;
                float haze = exp(-h * h * 0.05);
                float fade = smoothstep(FadeFrom, FadeTo, dist);
                float vary = lerp(0.6, 1.4, Hash(floor(lightAt.xyz)));
                float3 color = lamp ? _TownLampColor.rgb : _TownWindowColor.rgb;
                o.color = color * (_TownLightParams.x * light * haze * fade * vary);
                if (fade <= 0)
                    o.positionCS = float4(0, 0, -1, 1);   // a degenerate quad: nothing drawn
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float raw = SampleSceneDepth(input.positionCS.xy / _ScaledScreenParams.xy);
                if (raw != UNITY_RAW_FAR_CLIP_VALUE && LinearEyeDepth(raw, _ZBufferParams) < input.depth)
                    discard;
                // A bright core and a faint wide halo, so a far town's lights run together into a glowing cluster.
                float r2 = dot(input.quad, input.quad);
                half glow = exp(-r2 * 3.5) + HaloShare * exp(-r2 * 0.35);
                return half4(input.color * glow, 0);
            }
            ENDHLSL
        }
    }
}
