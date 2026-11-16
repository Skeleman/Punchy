Shader "Punchy/Far Lights"
{
    // R-10 rows 4b-4c: every static light seen from afar at night (SurfaceGlow.FarLights.cs): lit windows, street
    // lights, burning trees, beacon fires, glowing plants and crystals. One camera-facing quad a light (6 vertices from
    // SV_VertexID, read from a structured buffer): a bright core at least a few pixels across with a faint wide halo, so
    // a far town's or forest's lights run together into a glowing cluster; HDR bright so the bloom adds to it, hidden by
    // hills, thinned by the haze much less than the land, and faded in as the light's own reach shrinks on screen, where
    // the near lights and glow maps hand over (rule 20). How bright, big, hazed and faded each kind is comes from
    // FarLightPolicy's table, set per kind as arrays. Hidden by its own soft depth test, not the depth buffer's: a light
    // is only hidden by what's well in front of it (more so the farther it is), since the far land's coarse mesh and its
    // own house both stand over it. Each light a little brighter or dimmer than the next (hashed from where it is).
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
            Name "FarLights"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct FarPoint
            {
                float4 at;      // xyz where it's seen, w its light
                float4 color;   // rgb its colour, w its kind (FarLightKind)
            };
            StructuredBuffer<FarPoint> _FarLights;
            // Per kind (FarLightPolicy.Pack): brightness (on and tier folded in); core size, least pixels, halo reach,
            // halo share; haze share, reach (m), fade from, fade to (reference pixels).
            float4 _FarLightBright[7];
            float4 _FarLightShape[7];
            float4 _FarLightFade[7];
            float4 _FarLightParams;                 // x reference pixels a metre at 1 m (DetailPolicy)
            float4 _FarFireColor;                   // tonight's forest fire colour (its brightest channel 1)
            float _PunchyHazeDensity;               // (Sky.hlsl) the haze per metre

            // How far in front of a light the scene must be to hide it (m, and a share of its distance).
            static const float HideFrom = 12, HideShare = 0.025;
            static const uint KindFire = 2;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 quad : TEXCOORD0;
                nointerpolation half3 color : TEXCOORD1;
                nointerpolation float depth : TEXCOORD2;   // the light's eye depth, less how much the scene may stand over it
                nointerpolation float halo : TEXCOORD3;    // its halo's share
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
                FarPoint p = _FarLights[vertex / 6];
                uint kind = min((uint)p.color.w, 6u);
                float4 shape = _FarLightShape[kind];
                float4 fadeRule = _FarLightFade[kind];
                float2 corner = Corners[vertex % 6];
                float3 at = p.at.xyz;
                float dist = length(_WorldSpaceCameraPos - at);
                float light = p.at.w;
                // At least shape.y pixels across: a pixel's width at that distance, from the projection.
                float pixel = 2 * dist / (UNITY_MATRIX_P[1][1] * _ScaledScreenParams.y);
                float size = max(shape.x * sqrt(light), shape.y * pixel) * 0.5;
                float3 right = UNITY_MATRIX_V[0].xyz, up = UNITY_MATRIX_V[1].xyz;
                Varyings o;
                o.positionCS = TransformWorldToHClip(at + (right * corner.x + up * corner.y) * (size * shape.z));
                // A light 2-3 km off can lie past the far plane (3 km): pinned onto it instead of clipped.
                #if UNITY_REVERSED_Z
                    o.positionCS.z = max(o.positionCS.z, 0);
                #else
                    o.positionCS.z = min(o.positionCS.z, o.positionCS.w);
                #endif
                o.quad = corner * shape.z;   // in core sizes
                o.halo = shape.w;
                o.depth = -TransformWorldToView(at).z - (HideFrom + dist * HideShare);
                // Thinned by a share of the land's haze (it's a light, and should read 3 km off); faded in as the
                // reach of the light it stands in for shrinks on screen (rule 20: by screen size, not metres).
                float h = dist * _PunchyHazeDensity;
                float haze = exp(-h * h * fadeRule.x);
                float pixels = fadeRule.y / max(dist, 0.01) * _FarLightParams.x;
                float fade = 1 - smoothstep(fadeRule.w, fadeRule.z, pixels);
                float vary = lerp(0.6, 1.4, Hash(floor(at)));
                float3 color = kind == KindFire ? _FarFireColor.rgb : p.color.rgb;
                o.color = color * (_FarLightBright[kind].x * light * haze * fade * vary);
                if (fade <= 0 || _FarLightBright[kind].x <= 0)
                    o.positionCS = float4(0, 0, -1, 1);   // a degenerate quad: nothing drawn
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float raw = SampleSceneDepth(input.positionCS.xy / _ScaledScreenParams.xy);
                if (raw != UNITY_RAW_FAR_CLIP_VALUE && LinearEyeDepth(raw, _ZBufferParams) < input.depth)
                    discard;
                // A bright core and a faint wide halo, so a far cluster of lights runs together.
                float r2 = dot(input.quad, input.quad);
                half glow = exp(-r2 * 3.5) + input.halo * exp(-r2 * 0.35);
                return half4(input.color * glow, 0);
            }
            ENDHLSL
        }
    }
}
