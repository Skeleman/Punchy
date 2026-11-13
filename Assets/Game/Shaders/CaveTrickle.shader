Shader "Punchy/Cave Trickle"
{
    // Water trickling down a cave wall from a slit (W-141, CaveSystem.Trickles): a thin, mostly see-through film that
    // hugs the rock (a grid of points CaveSystem found down the wall), shaped by its kind (one thread, a fan of threads,
    // a seeping sheet, or drops falling off an overhang), with a slow shimmer running down it on the shared clock and a
    // glint of the lantern. Where it lands, rings on the puddle or pool and a little spray. No meshes: 62 quads per
    // trickle from SV_VertexID, read from a structured buffer; the slit, wet streak and puddle are in the rock (Cave).
    // W-142 row 3: a lava seep (the fourth header row's z set) is the same sheet as a slow glowing ooze, crusting dark
    // in drifting patches with hot cracks through the crust, and a molten glow where it lands instead of rings and spray.
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Trickles"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SharedClock.hlsl"
            #include "Glow.hlsl"

            // Per trickle 84 float4s: (kind, width, seed, threads), (land xyz, puddle), (out x, 0, out z, drop),
            // (flow, 1 on water, 1 lava, 0), then 16 rows x 5 columns of points down the wall (w: unused).
            StructuredBuffer<float4> _Trickles;
            float _TrickleFade[12];
            float _SplashFade[12];
            float4 _PunchyLantern;        // xyz position, w strength
            float4 _PunchyLanternColor;   // rgb colour, w reach (m)

            static const uint Stride = 84;
            static const uint Columns = 5;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;   // across 0..1, down 0..1 (the wall); -1..1 (splash, spray)
                float3 positionWS : TEXCOORD1;
                nointerpolation float4 info : TEXCOORD2;   // x part (0 wall, 1 rings, 2 spray), y kind, z seed, w threads
                nointerpolation float4 more : TEXCOORD3;   // x drop (m), y fade, z flow, w 1 on water
                float3 normalWS   : TEXCOORD4;
                nointerpolation float lava : TEXCOORD5;   // 1 for a lava seep
            };

            float3 SafeNormal(float3 v, float3 fallback)
            {
                float l = length(v);
                return l > 1e-5 ? v / l : fallback;
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + 1), f.x), f.y);
            }

            Varyings Vert(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
            {
                uint b = instanceID * Stride;
                float4 head = _Trickles[b];
                float4 land = _Trickles[b + 1];
                float4 outward = _Trickles[b + 2];
                float4 extra = _Trickles[b + 3];
                int kind = (int)round(head.x);
                uint quad = vertexID / 6;
                uint v = vertexID % 6;
                float2 corner = float2(v == 1 || v == 2 || v == 4 ? 1 : 0, v == 2 || v == 4 || v == 5 ? 1 : 0);
                float3 out3 = SafeNormal(outward.xyz, float3(1, 0, 0));
                float3 slit = _Trickles[b + 4 + Columns / 2].xyz;
                float3 foot = _Trickles[b + 4 + 15 * Columns + Columns / 2].xyz;
                // Off an overhang the water falls clear of the wall, from just past the lip.
                float3 lip = slit + out3 * 0.12;
                if (kind == 3)
                    foot = float3(lip.x, land.y, lip.z);

                Varyings o;
                o.info = float4(0, kind, head.z, head.w);
                o.more = float4(max(outward.w, 0.1), _TrickleFade[instanceID], extra.x, extra.y);
                o.lava = extra.z;
                float3 p;
                if (quad < 60)
                {
                    uint col = quad % 4 + (uint)corner.x;
                    uint row = quad / 4 + (uint)corner.y;
                    o.uv = float2(col / 4.0, row / 15.0);
                    if (kind == 3)
                    {
                        // A ribbon hanging from the lip, turned to the camera.
                        float3 toCamera = GetCameraPositionWS() - lip;
                        float3 side = SafeNormal(cross(float3(0, 1, 0), toCamera), float3(-out3.z, 0, out3.x));
                        p = float3(lip.x, lerp(lip.y, land.y + 0.01, o.uv.y), lip.z) + side * (o.uv.x - 0.5) * head.y;
                        o.normalWS = SafeNormal(float3(toCamera.x, 0, toCamera.z), out3);
                    }
                    else
                    {
                        p = _Trickles[b + 4 + row * Columns + col].xyz;
                        o.normalWS = out3;
                    }
                }
                else if (quad == 60)
                {
                    // Rings where it lands: on a pool, or across the puddle.
                    o.info.x = 1;
                    o.more.y = _SplashFade[instanceID];
                    o.uv = corner * 2 - 1;
                    float radius = extra.y > 0.5 ? 0.35 : max(land.w, 0.25) * 0.8;
                    p = foot + float3(0, 0.012, 0) + float3(o.uv.x, 0, o.uv.y) * radius;
                    o.normalWS = float3(0, 1, 0);
                }
                else
                {
                    // A little spray where it hits, turned to the camera.
                    o.info.x = 2;
                    o.more.y = _SplashFade[instanceID];
                    o.uv = corner * 2 - 1;
                    float size = 0.06 + head.y * 0.1 * (kind == 2 ? 0.6 : 1);
                    float3 centre = foot + float3(0, size * 0.7, 0);
                    float3 toCamera = GetCameraPositionWS() - centre;
                    float3 side = SafeNormal(cross(float3(0, 1, 0), toCamera), float3(1, 0, 0));
                    p = centre + side * o.uv.x * size + float3(0, 1, 0) * o.uv.y * size;
                    o.normalWS = SafeNormal(toCamera, float3(0, 1, 0));
                }
                o.positionWS = p;
                o.positionCS = TransformWorldToHClip(p);
                return o;
            }

            // How much water covers this spot of the wall, by kind (x across, y metres down from the slit).
            float WallMask(int kind, float x, float down, float y, float t, float seed, float threads)
            {
                float flowY = y - t * 0.8;
                if (kind == 0)
                {
                    float centre = 0.5 + 0.13 * sin(y * 3.1 + seed * 6.2) + 0.06 * sin(y * 7.3 + seed * 3.1);
                    return smoothstep(0.22, 0.07, abs(x - centre));
                }
                if (kind == 2)
                {
                    // A seeping sheet: wavy edges, broken by dry patches and streaks.
                    float edge = 0.07 + 0.05 * sin(y * 4.3 + seed * 5.0) + 0.04 * Noise(float2(seed * 9.0, y * 2.0));
                    float body = smoothstep(edge, edge + 0.08, x) * smoothstep(edge, edge + 0.08, 1 - x);
                    float holes = smoothstep(0.28, 0.5, Noise(float2(x * 7 + seed * 13, y * 2.2)));
                    float streaks = 0.55 + 0.45 * Noise(float2(x * 22 + seed * 3, flowY * 0.6));
                    return body * holes * streaks;
                }
                // A fan's threads part from the slit; off an overhang they fall straight, breaking into drops.
                float mask = 0;
                int n = (int)clamp(round(threads), 1, 4);
                for (int k = 0; k < n; k++)
                {
                    float target = (k + 0.5) / n;
                    float centre = kind == 1 ? lerp(0.5, target, smoothstep(0, 0.4, down)) : lerp(0.5, target, 0.6);
                    centre += 0.03 * sin(y * 5.0 + k * 2.3 + seed * 7.0);
                    float thin = kind == 1 ? 0.08 / n + 0.025 : kind == 3 ? 0.035 : 0.06;
                    float thread = smoothstep(thin, thin * 0.3, abs(x - centre));
                    if (kind == 3)
                    {
                        // A bead hanging at the lip, then drops falling now and then: each thread at its own pace,
                        // with some beats skipped, so they never line up into a dashed line.
                        float pace = 1.1 + 0.9 * Hash(float2(k, seed * 17));
                        float beat = y * 1.4 - t * pace + Hash(float2(seed, k)) * 7;
                        float cell = floor(beat);
                        float falls = step(0.6, Hash(float2(cell, k + seed * 31)));
                        float at = 0.15 + 0.7 * Hash(float2(cell * 1.7 + 3, k + seed * 11));
                        float drops = step(abs(frac(beat) - at), 0.035) * falls;
                        float whole = 1 - smoothstep(0.03, 0.08, y);
                        thread *= max(whole, drops);
                    }
                    mask = max(mask, thread);
                }
                return mask;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                bool lava = input.lava > 0.5;
                // Lava oozes at a tenth of water's pace.
                float t = _PunchyClock * (lava ? 0.1 : 1);
                int part = (int)round(input.info.x);
                int kind = (int)round(input.info.y);
                float seed = input.info.z;
                float3 p = input.positionWS;
                float3 n = SafeNormal(input.normalWS, float3(0, 1, 0));
                float3 toEye = SafeNormal(GetCameraPositionWS() - p, n);

                // Light: faint without a lantern; the lantern, torches and molten glow light it and glint off it.
                float3 toLantern = _PunchyLantern.xyz - p;
                float lanternDistance = length(toLantern);
                float reach = _PunchyLanternColor.w > 0 ? saturate(1 - lanternDistance / max(_PunchyLanternColor.w, 1e-3)) : 0;
                half3 lantern = _PunchyLanternColor.rgb * _PunchyLantern.w * reach * reach * 2;
                half3 light = 0.12 + lantern + PunchyGlowLight(p, (half3)n);

                float mask;
                float shimmer;
                if (part == 0)
                {
                    float y = input.uv.y * input.more.x;
                    mask = WallMask(kind, input.uv.x, input.uv.y, y, t, seed, input.info.w);
                    // Fade in from the slit, and out into a pool.
                    mask *= smoothstep(0.0, 0.05, y) * (input.more.w > 0.5 ? smoothstep(1.0, 0.94, input.uv.y) : 1);
                    shimmer = Noise(float2(input.uv.x * 9 + seed * 17, (y - t * 0.9) * 4.5));
                    // The film ripples, so its glint wanders.
                    float3 side = float3(-n.z, 0, n.x);
                    n = SafeNormal(n + side * (shimmer - 0.5) * 1.6 + float3(0, (Noise(float2(seed, y * 6 - t * 3)) - 0.5) * 1.2, 0), n);
                }
                else if (part == 1)
                {
                    // Rings spreading where the water lands, two at a time.
                    float r = length(input.uv);
                    float a = frac(t * 0.9 + seed);
                    float c = frac(t * 0.9 + seed + 0.5);
                    mask = smoothstep(0.08, 0.0, abs(r - a)) * (1 - a) + smoothstep(0.08, 0.0, abs(r - c)) * (1 - c);
                    mask *= step(r, 1) * 0.7;
                    shimmer = 0.5;
                }
                else
                {
                    // Spray: tiny droplets flickering in a soft cloud.
                    float r = length(input.uv);
                    float drops = step(0.72, Noise(input.uv * 5 + float2(seed * 31, -t * 7)));
                    mask = smoothstep(1, 0.2, r) * (0.04 + drops) * 0.35;
                    shimmer = drops;
                }

                if (lava)
                {
                    // A slow glowing ooze: thicker and opaque, dark crust drifting down it in patches with hot cracks
                    // between, brightest at the slit; where it lands a molten glow (no rings, no spray).
                    half3 hot = half3(1.0, 0.36, 0.06);
                    if (part == 2)
                    {
                        clip(-1);
                        return 0;
                    }
                    if (part == 0)
                    {
                        float y = input.uv.y * input.more.x;
                        float crust = smoothstep(0.45, 0.7, Noise(float2(input.uv.x * 4 + seed * 9, (y - t * 0.9) * 1.6)));
                        crust = max(crust, smoothstep(0.6, 1.0, input.uv.y) * 0.6);
                        float crack = 1 - smoothstep(0.0, 0.06, abs(Noise(float2(input.uv.x * 11 + seed * 5, (y - t * 0.9) * 5)) - 0.5));
                        float heat = lerp(1, crack * 0.8, crust) * lerp(1.6, 0.9, input.uv.y);
                        half3 crustColor = half3(0.07, 0.05, 0.045) * min(light, 1.5);
                        half3 ooze = lerp(crustColor, hot * 2.2, saturate(heat)) + hot * shimmer * 0.4 * (1 - crust);
                        float oa = saturate(min(mask * 1.6, 1) * 0.92 * input.more.y);
                        clip(oa - 0.01);
                        return half4(ooze, oa);
                    }
                    float r = length(input.uv);
                    float pulse = 0.8 + 0.2 * sin(_PunchyClock * 0.7 + seed * 6.3);
                    float glowA = smoothstep(1, 0.1, r) * 0.55 * pulse * input.more.y;
                    clip(glowA - 0.01);
                    return half4(hot * 2.5 * saturate(1.2 - r), glowA);
                }

                float3 halfway = SafeNormal(SafeNormal(toLantern, n) + toEye, n);
                float glint = pow(saturate(dot(n, halfway)), 64) * reach * _PunchyLantern.w;
                // Mostly see-through: a faint body, brighter where the ripples catch the light.
                half3 color = half3(0.55, 0.64, 0.7) * min(light, 1.5) * (0.25 + 0.5 * shimmer) + _PunchyLanternColor.rgb * glint * 1.5;
                float a = mask * (0.1 + 0.18 * shimmer * shimmer * (0.5 + input.more.z) + 0.5 * glint) * input.more.y;
                a = saturate(a);
                clip(a - 0.01);
                return half4(color, a);
            }
            ENDHLSL
        }
    }
}
