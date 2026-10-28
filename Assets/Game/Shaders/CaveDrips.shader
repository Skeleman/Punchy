Shader "Punchy/Cave Drips"
{
    // Water dripping from stalactite tips (CaveSystem): each drip source swells a drop at its tip, lets it fall, and
    // where it lands spreads a small ring on water, or on rock splashes a few droplets up and out and leaves a brief
    // dark wet spot; on its own period, all from the shared clock, so every player sees the same drops without
    // anything being sent. No meshes: each source is six quads built from SV_VertexID (the falling drop, camera-facing
    // and stretched with its speed; the flat ring or wet spot; four splash droplets), read from a structured buffer.
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Drips"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SharedClock.hlsl"

            struct Drip
            {
                float4 tip;    // xyz the stalactite's tip, w the period (s)
                float4 land;   // x where the drop lands (height), y phase 0..1, z ring size, w 1 on water (0 on rock)
            };
            StructuredBuffer<Drip> _Drips;
            float4 _PunchyLantern;        // xyz position, w strength
            float4 _PunchyLanternColor;   // rgb colour, w reach (m)

            static const float Gravity = 9.81;
            static const float Swell = 0.8;      // seconds a drop takes to gather at the tip
            static const float Ring = 0.45;      // seconds a splash ring lasts
            static const float Spot = 1.6;       // seconds a wet spot on rock lasts
            static const float Splash = 0.3;     // seconds the splash droplets fly

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 local      : TEXCOORD0;   // -1..1 across the quad
                float  ring       : TEXCOORD1;   // 0 the drop, 1 the ring, 2 the wet spot, 3 a splash droplet
                float  alpha      : TEXCOORD2;
                half3  light      : TEXCOORD3;
            };

            Varyings Vert(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
            {
                Drip d = _Drips[instanceID];
                uint quad = vertexID / 6;
                uint v = vertexID % 6;
                // Two triangles: corners (-1,-1) (1,-1) (1,1), (-1,-1) (1,1) (-1,1).
                float2 corner = float2(v == 1 || v == 2 || v == 4 ? 1 : -1, v == 2 || v == 4 || v == 5 ? 1 : -1);

                float height = max(d.tip.y - d.land.x, 0.05);
                float fall = sqrt(2 * height / Gravity);
                float t = frac(_PunchyClock / d.tip.w + d.land.y) * d.tip.w;

                Varyings o;
                o.local = corner;
                o.ring = quad;
                float3 p;
                bool onWater = d.land.w > 0.5;
                float landed = t - Swell - fall;
                if (quad >= 2 && onWater)
                {
                    // On water there's only the ring.
                    p = d.tip.xyz;
                    o.alpha = 0;
                    o.ring = 3;
                }
                else if (quad >= 2)
                {
                    // On rock: droplets thrown up and out, each its own way (from the source's phase), falling back.
                    float age = landed / Splash;
                    float visible = age >= 0 && age <= 1 ? 1 : 0;
                    float a = (quad - 2) * 1.5708 + d.land.y * 40;
                    float3 dir = float3(cos(a), 0, sin(a));
                    float s = landed;
                    float3 centre = float3(d.tip.x, d.land.x, d.tip.z) + dir * (0.35 * s) + float3(0, 1.1 * s - 0.5 * Gravity * s * s, 0);
                    float3 toCamera = normalize(GetCameraPositionWS() - centre);
                    float3 side = normalize(cross(float3(0, 1, 0), toCamera));
                    float size = 0.006 * visible;
                    p = centre + side * corner.x * size + float3(0, 1, 0) * corner.y * size;
                    o.alpha = visible * (1 - age) * 0.8;
                    o.ring = 3;
                }
                else if (quad == 1 && !onWater)
                {
                    // On rock: a dark wet spot that dries.
                    float age = landed / Spot;
                    float visible = age >= 0 && age <= 1 ? 1 : 0;
                    float radius = d.land.z * 0.35 * visible;
                    p = float3(d.tip.x, d.land.x + 0.008, d.tip.z) + float3(corner.x, 0, corner.y) * radius;
                    o.alpha = visible * (1 - age * age) * 0.35;
                    o.ring = 2;
                }
                else if (quad == 0)
                {
                    // The drop: swelling at the tip, then falling (hidden after it lands).
                    float grow = saturate(t / Swell);
                    float falling = max(t - Swell, 0);
                    float speed = Gravity * falling;
                    float visible = t < Swell + fall ? 1 : 0;
                    float3 centre = d.tip.xyz - float3(0, 0.5 * Gravity * falling * falling + 0.01 * grow, 0);
                    float3 toCamera = normalize(GetCameraPositionWS() - centre);
                    float3 side = normalize(cross(float3(0, 1, 0), toCamera));
                    float width = 0.011 * grow * visible;
                    float streak = (0.012 + min(speed * 0.008, 0.08)) * grow * visible;
                    p = centre + side * corner.x * width + float3(0, 1, 0) * corner.y * streak;
                    o.alpha = visible * (0.55 + 0.35 * grow);
                }
                else
                {
                    // The ring: spreading and fading where the drop landed.
                    float age = (t - Swell - fall) / Ring;
                    float visible = age >= 0 && age <= 1 ? 1 : 0;
                    float radius = d.land.z * (0.25 + 0.75 * saturate(age)) * visible;
                    p = float3(d.tip.x, d.land.x + 0.012, d.tip.z) + float3(corner.x, 0, corner.y) * radius;
                    o.alpha = visible * (1 - saturate(age)) * 0.6;
                }
                o.positionCS = TransformWorldToHClip(p);
                // Glinting in the lantern's light, faintly visible without it.
                float3 toLantern = _PunchyLantern.xyz - p;
                float reach = _PunchyLanternColor.w > 0 ? saturate(1 - length(toLantern) / _PunchyLanternColor.w) : 0;
                o.light = 0.12 + _PunchyLanternColor.rgb * _PunchyLantern.w * reach * reach * 2;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float r = length(input.local);
                // A soft, bright-cored drop or droplet; a thin ring; a soft dark spot.
                int kind = (int)round(input.ring);
                half shape = kind == 1 ? smoothstep(0.12, 0.0, abs(r - 0.8)) : kind == 2 ? smoothstep(1, 0.3, r) : smoothstep(1, 0.4, r);
                half3 color = kind == 2 ? half3(0, 0, 0) : half3(0.75, 0.85, 0.95) * input.light * (kind == 1 ? 1 : 1.6);
                half a = shape * input.alpha;
                clip(a - 0.01);
                return half4(color, a);
            }
            ENDHLSL
        }
    }
}
