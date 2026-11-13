Shader "Punchy/LeafFall"
{
    // W-142 row 1: falling leaves (LeafFallSystem). One quad per leaf, posed by the CPU (position, size, a rotation
    // quaternion) and shaped here by kind: a pointed leaf with a midrib, a pine needle, a flake of mushroom cap, or a
    // bit of curled rootlet. Lit on both sides with light shining through when the sun is behind it; underground the
    // lantern, torches and glow light it instead. Fading out on the ground is a dither, so it stays opaque.
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Transparent-50" "RenderPipeline" = "UniversalPipeline" }
        Cull Off

        Pass
        {
            Name "LeafFall"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Sky.hlsl"
            #include "Glow.hlsl"

            // Matches LeafFallSystem.GpuLeaf (64 bytes).
            struct Leaf
            {
                float3 position;
                float size;
                float4 rotation;
                float4 color;      // rgb, a = how much is left (fade)
                float kind;        // 0 leaf, 1 needle, 2 cap flake, 3 rootlet
                float seed;
                float grounded;
                float pad;
            };
            StructuredBuffer<Leaf> _Leaves;
            float _PunchyUnderground;
            float4 _PunchyLantern;        // xyz position, w strength
            float4 _PunchyLanternColor;   // rgb colour, w reach (m)

            static const float2 Corner[6] = { float2(-1, -1), float2(1, -1), float2(1, 1), float2(-1, -1), float2(1, 1), float2(-1, 1) };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half4 color : TEXCOORD3;
                float3 info : TEXCOORD4;   // kind, seed, grounded
            };

            float3 Rotate(float4 q, float3 v)
            {
                float3 t = 2 * cross(q.xyz, v);
                return v + q.w * t + cross(q.xyz, t);
            }

            Varyings Vert(uint vertex : SV_VertexID, uint instance : SV_InstanceID)
            {
                Leaf l = _Leaves[instance];
                int kind = (int)round(l.kind);
                float2 c = Corner[vertex % 6];
                // Leaf outline in a long quad (x across, y along); needles and rootlets are thinner still.
                float2 extent = kind == 1 ? float2(0.12, 1.0) : kind == 3 ? float2(0.35, 1.0) : kind == 2 ? float2(0.8, 0.8) : float2(0.5, 1.0);
                float3 local = float3(c * extent * l.size * 0.5, 0);
                // A guard against a degenerate quaternion (rule 24): fall back to no rotation.
                float4 q = dot(l.rotation, l.rotation) > 1e-6 ? normalize(l.rotation) : float4(0, 0, 0, 1);

                Varyings o;
                o.positionWS = l.position + Rotate(q, local);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = c;
                o.normalWS = (half3)Rotate(q, float3(0, 0, 1));
                o.color = (half4)l.color;
                o.info = float3(kind, l.seed, l.grounded);
                return o;
            }

            half4 Frag(Varyings input, bool front : SV_IsFrontFace) : SV_Target
            {
                int kind = (int)round(input.info.x);
                float seed = input.info.y;
                float2 uv = input.uv;
                half3 albedo = input.color.rgb;

                // Shape by kind; clip outside it.
                float inside;
                if (kind == 0)
                {
                    // A pointed oval, widest a little below the middle, with a short stem and a pale midrib.
                    float y = uv.y;
                    float half_width = sqrt(saturate(1 - y * y)) * (1 - 0.35 * saturate(y)) * 0.95;
                    inside = half_width - abs(uv.x);
                    if (y < -0.75 && abs(uv.x) < 0.07)
                        inside = 0.01;   // the stem
                    half rib = 1 - smoothstep(0.02, 0.07, abs(uv.x));
                    half veins = smoothstep(0.9, 0.98, abs(sin((y - abs(uv.x) * 0.9) * 9.0))) * 0.4;
                    albedo *= 1 - 0.15 * veins;
                    albedo = lerp(albedo, albedo * 1.35 + 0.03, rib * 0.5);
                }
                else if (kind == 1)
                {
                    inside = 1 - abs(uv.x) - smoothstep(0.85, 1.0, abs(uv.y));
                }
                else if (kind == 2)
                {
                    // A torn bit of cap: a lumpy round edge, paler gills on one side.
                    float a = atan2(uv.y, uv.x);
                    float edge = 0.75 + 0.18 * sin(a * 3.0 + seed * 40.0) + 0.08 * sin(a * 7.0 + seed * 13.0);
                    inside = edge - length(uv);
                    if (!front)
                        albedo = lerp(albedo, half3(0.82, 0.76, 0.66), 0.6) * (0.85 + 0.15 * abs(sin(a * 18.0)));
                }
                else
                {
                    // A curled rootlet: a thin line wandering across the quad.
                    float wander = sin(uv.y * 3.0 + seed * 20.0) * 0.45;
                    inside = 0.22 - abs(uv.x - wander);
                }
                clip(inside);

                // Fading on the ground: a screen dither, so the pass stays opaque.
                float2 pixel = input.positionCS.xy;
                float dither = frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
                clip(input.color.a - dither * 0.999 - 0.0005);

                float3 p = input.positionWS;
                half3 n = normalize(input.normalWS);
                n = front ? n : -n;
                float3 toEye = normalize(GetCameraPositionWS() - p);

                // Above ground: sun (shadowed) on whichever side faces it, light shining through from behind, sky fill.
                Light light = GetMainLight(TransformWorldToShadowCoord(p));
                half shadow = light.shadowAttenuation * (1 - PunchyCloudShadow(p)) * (1 - _PunchyUnderground);
                half facing = dot(n, light.direction);
                half through = saturate(-facing) * saturate(dot(-toEye, light.direction) * 0.5 + 0.5) * (kind == 0 ? 0.9 : 0.3);
                half3 surface = light.color * shadow * (saturate(facing) * 0.8 + through) + SampleSH(n) * lerp(1, 0.06, _PunchyUnderground);

                // Underground: faint, plus the lantern, torches and molten glow.
                float3 toLantern = _PunchyLantern.xyz - p;
                float reach = _PunchyLanternColor.w > 0 ? saturate(1 - length(toLantern) / max(_PunchyLanternColor.w, 1e-3)) : 0;
                half3 lantern = _PunchyLanternColor.rgb * _PunchyLantern.w * reach * reach * 2;
                half3 cave = (0.12 + lantern + PunchyGlowLight(p, n)) * _PunchyUnderground;

                half3 color = albedo * (surface + cave);
                return half4(PunchyAtmosphere(color, p), 1);
            }
            ENDHLSL
        }
    }
}
