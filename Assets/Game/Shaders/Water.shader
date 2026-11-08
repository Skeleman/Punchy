Shader "Punchy/Water"
{
    // Sea, lake and river surfaces. Colour comes from how much water the view ray passes through (read from the
    // camera depth texture): clear and tinted in the shallows, dark further down. Reflections rise at grazing angles
    // (Fresnel): the scene itself where a short screen-space march finds it (banks, trees), the live sky elsewhere.
    // The sun glints, ripples are sums of directional waves, rivers carry their ripples and streaks downstream
    // (faster in the deep middle), and foam lines the shore and the rapids.
    // Two other liquids share the sheet: very murky water (swamps) turns thick and oily, slowly heaving and bubbling,
    // with drifting scum; lava (the lava amount in the vertex) is bright molten orange flowing in slow warped swirls,
    // with thin, sparse crust riding on it and thickening at the edges, barely reflective. (Cave pools use the same
    // path, drawn by CaveSystem.)
    Properties
    {
        _ShallowColor("Shallow Color", Color) = (0.10, 0.42, 0.45, 1)
        _DeepColor("Deep Color", Color) = (0.01, 0.07, 0.15, 1)
        _Absorption("Colour Absorption (per metre)", Float) = 0.18
        _Opacity("Opacity (per metre)", Float) = 0.45
        _FoamColor("Foam Color", Color) = (0.9, 0.95, 1, 1)
        _FoamDistance("Shore Foam Distance (m)", Float) = 0.8
        _WaveScale("Wave Frequency (per metre)", Float) = 0.35
        _WaveStrength("Wave Strength", Range(0, 1)) = 0.25
        _WaveSpeed("Wave Speed", Float) = 0.8
        _FlowSpeed("River Flow Speed (m/s)", Float) = 1.6
        _Smoothness("Smoothness", Range(0, 1)) = 0.95
        _ScreenReflection("Screen-Space Reflection", Range(0, 1)) = 1
        _DetailStrength("Fine Ripples", Range(0, 1)) = 0.5
        _DetailScale("Fine Ripple Repeats (per metre)", Float) = 0.4
        _Sparkle("Sun Sparkle", Range(0, 1)) = 0.6
        // 1 on CaveSystem's copy for cave pools and streams: always under rock, whatever the camera's depth.
        [HideInInspector] _UnderRock("Under Rock", Float) = 0
        // 1 for white pools (CaveSystem, per draw): the pool fades out towards its rim (the disc mesh's depth) instead of
        // ending in a hard edge, and its glow with it.
        [HideInInspector] _SoftRim("Soft Rim", Float) = 0
    }

    SubShader
    {
        // Drawn before the other see-through things (fog wisps, flames, rain, waterfalls): they all stand above the water,
        // and in one queue a big water chunk, sorted by its middle, could be drawn after mist in front of it (Z-10).
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Sky.hlsl"
            #include "SharedClock.hlsl"

            // Weather global (WeatherSystem): 0..1 wind strength at the camera.
            half _PunchyWind;

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                half _Absorption;
                half _Opacity;
                half4 _FoamColor;
                half _FoamDistance;
                float _WaveScale;
                half _WaveStrength;
                float _WaveSpeed;
                float _FlowSpeed;
                half _Smoothness;
                half _ScreenReflection;
                half _DetailStrength;
                float _DetailScale;
                half _Sparkle;
                half _UnderRock;
                half _SoftRim;
            CBUFFER_END
            // 0 above ground, 1 with the camera deep in a cave (CaveSystem).
            float _PunchyUnderground;
            // Test only (DebugControls "-waterprobe n"): 1 shows the foam, 2 the water's thickness over the bed, 3 the alpha, 4 the foam's parts (shore, churn, streaks).
            float _PunchyWaterProbe;
            // Ripples swimmers have left in the water near the camera (CreatureWakeSystem): x, age (0..1 over its life),
            // z, how strongly it was stirred (0 an empty slot).
            float4 _PunchySwimmers[48];
            int _PunchySwimmerCount;
            // Where the ripples are, coarsely (CreatureWakeSystem's MarkMask): a cell 1 where some ripple reaches into it,
            // and (x, z of the map's corner, cells per metre, cells across).
            TEXTURE2D(_PunchySwimmerMask);
            float4 _PunchySwimmerMaskAt;

            // Whether any ripple reaches p (its cell in the mask): elsewhere, most of the water, the loop is skipped.
            bool NearRipple(float2 p)
            {
                int2 c = (int2)floor((p - _PunchySwimmerMaskAt.xy) * _PunchySwimmerMaskAt.z);
                return all(c >= 0) && all(c < (int)_PunchySwimmerMaskAt.w) && LOAD_TEXTURE2D(_PunchySwimmerMask, c).r > 0;
            }
            // The player's lantern (xyz position, w strength) and its colour (rgb) and range (w), set by CaveSystem.
            float4 _PunchyLantern;
            float4 _PunchyLanternColor;
            // The fine detail texture shared with the caves (CaveSystem makes it once): rg a tiling normal, b a height,
            // a a sparkle. Two scrolling reads of it give the water its small ripples and glitter.
            TEXTURE2D(_PunchyCaveDetail);
            // 1 once CaveSystem has made the detail texture; until then (or without it) the fine ripples stay off.
            float _PunchyCaveDetailReady;
            // 1 to draw the fine detail at all, 0 to skip it (CaveSystem, from -waterdetail).
            float _PunchyWaterDetail;
            // 1 to skip the screen-space reflection march (CaveSystem, from -waterssr 0, for profiling only).
            float _PunchyWaterNoSsr;
            SAMPLER(sampler_PunchyCaveDetail);

            // Must match WaterVertex in BuildChunkMeshJob.cs.
            struct WaterAttributes
            {
                float3 positionOS : POSITION;
                float4 data       : TEXCOORD0;   // flow direction xz, depth below the surface, rapids 0..1
                float4 look       : TEXCOORD1;   // biome water colour rgb, murk 0..1
                float2 glow       : TEXCOORD2;   // self-glow (toxic water), lava 0..1
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 data       : TEXCOORD1;
                half   fogFactor  : TEXCOORD2;
                float4 look       : TEXCOORD3;
                float2 glow       : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(WaterAttributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.data = input.data;
                o.look = input.look;
                o.glow = input.glow;
                o.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return o;
            }

            // Slope (dh/dx, dh/dz) of a sum of directional waves. Directions and frequencies are spread so the
            // pattern doesn't visibly repeat.
            float2 WaveSlope(float2 p, float t)
            {
                const float2 dirs[6] =
                {
                    float2(0.82, 0.57), float2(-0.34, 0.94), float2(0.98, -0.21),
                    float2(-0.71, -0.70), float2(0.26, -0.96), float2(-0.93, 0.36)
                };
                const float freqs[6] = { 1.0, 1.37, 1.93, 2.71, 3.83, 5.29 };
                const float steep[6] = { 0.30, 0.24, 0.18, 0.13, 0.09, 0.06 };

                float2 slope = 0;
                [unroll]
                for (int i = 0; i < 6; i++)
                {
                    float k = _WaveScale * freqs[i];
                    // Deep-water dispersion: longer waves travel faster.
                    float phase = dot(dirs[i], p) * k + t * _WaveSpeed * sqrt(k * 9.81);
                    slope += dirs[i] * steep[i] * cos(phase);
                }
                return slope;
            }

            // Wakes and rings round things in the water (G-15/F-25, Z-27), on the shared clock: every ripple a swimmer
            // has left is a ring spreading out from where it was made at 1.4 m/s and dying away, with a little foam on
            // its crest when it was stirred hard. A row of them trailing behind a swimmer is its wake (spreading rings
            // from a moving source make the V by themselves), left in the water as it turns; one keeping still leaves
            // rings on rings. Only ripples within 5 m of the point cost more than a distance test.
            float2 SwimmerWakes(float2 p, float t, out half foam)
            {
                float2 slope = 0;
                foam = 0;
                for (int k = 0; k < _PunchySwimmerCount; k++)
                {
                    float4 s = _PunchySwimmers[k];
                    float2 rel = p - s.xz;
                    float r2 = dot(rel, rel);
                    [branch] if (s.w > 0 && r2 < 25)
                    {
                        float r = sqrt(r2);
                        float age = s.y;
                        float radius = age * 4.2;
                        float width = 0.18 + age * 0.35;
                        float d = r - radius;
                        float fade = (1 - age) * (1 - age) * s.w;
                        // The ring: a crest with a trough either side, the shared clock rippling along it a little.
                        float ring = exp(-d * d / (width * width));
                        float wave = sin(d / width * 2.6 - t * 3);
                        slope += rel / max(r, 1e-3) * ring * wave * fade * 0.45;
                        foam = max(foam, smoothstep(0.6, 1, ring * (0.5 + 0.5 * wave)) * fade * smoothstep(0.8, 1.4, s.w) * 0.35);
                    }
                }
                return slope;
            }

            // Rivers: the wave pattern is carried downstream. Two copies offset by half a cycle cross-fade so the
            // pattern never stretches indefinitely (the standard flow-map technique). The cycle's phase drifts slowly
            // across the water and the blend keeps its contrast, so a whole river's ripples and white water don't
            // fade and swell together every few seconds (F-24).
            float2 FlowingSlope(float2 p, float2 flow, float speed, float t)
            {
                const float cycle = 3.0;
                float jitter = PunchyValueNoise(p * 0.03) * 2;
                float phase0 = frac(t / cycle + jitter);
                float phase1 = frac(t / cycle + jitter + 0.5);
                float w0 = 1 - abs(1 - 2 * phase0);
                float2 offset = flow * speed * cycle;
                return (WaveSlope(p - offset * phase0, t * 0.3) * w0 +
                        WaveSlope(p - offset * phase1 + 7.3, t * 0.3) * (1 - w0)) * rsqrt(w0 * w0 + (1 - w0) * (1 - w0));
            }

            // Whitewater foam carried by the current (W-98): the foam pattern itself drifts downstream at the river's
            // speed, in two reads half a short cycle apart that cross-fade, like the flowing ripples. (A pattern fixed in
            // place under moving ripples read as a white decal sliding over the water.)
            half FlowFoam(float2 p, float2 flow, float speed, float t)
            {
                const float cycle = 1.6;
                float jitter = PunchyValueNoise(p * 0.05 + 9.7) * 2;
                float phase0 = frac(t / cycle + jitter);
                float phase1 = frac(t / cycle + jitter + 0.5);
                float w0 = 1 - abs(1 - 2 * phase0);
                float2 offset = flow * speed * cycle;
                float2 q0 = p - offset * phase0;
                float2 q1 = p - offset * phase1 + 4.7;
                float n0 = PunchyValueNoise(q0 * 3.1) * 0.55 + PunchyValueNoise(q0 * 1.3 + 2.9) * 0.45;
                float n1 = PunchyValueNoise(q1 * 3.1) * 0.55 + PunchyValueNoise(q1 * 1.3 + 2.9) * 0.45;
                float s = n0 * w0 + n1 * (1 - w0);
                // Cross-fading flattens the contrast mid-way; stretch it back out.
                return saturate((s - 0.5) * (1.7 + 0.9 * (1 - abs(2 * w0 - 1))) + 0.5);
            }

            // A waterfall's falling sheets along one horizontal axis u: broad white bands, finer ropes and flecks.
            half FallSheet(float u, float fallV)
            {
                return PunchyValueNoise(float2(u * 2.3, fallV * 0.35)) * 0.5 + PunchyValueNoise(float2(u * 6.1 + 3.1, fallV * 0.9)) * 0.3
                     + PunchyValueNoise(float2(u * 15.0 + 7.7, fallV * 2.4)) * 0.2;
            }

            // Raindrop rings: each cell of a jittered grid spawns a ring that expands and fades, at a random phase.
            float2 RainRipples(float2 p, float t)
            {
                float2 slope = 0;
                [unroll]
                for (int layer = 0; layer < 2; layer++)
                {
                    float2 q = p * 1.7 + layer * 7.31;
                    float2 cell = floor(q);
                    float2 centre = float2(PunchyHash(cell + 3.1 + layer), PunchyHash(cell + 5.7 + layer)) * 0.5 + 0.25;
                    float2 d = frac(q) - centre;
                    float r = length(d);
                    float phase = frac(t * 0.9 + PunchyHash(cell + 11.3 * layer));
                    float wave = sin((r - phase * 0.45) * 45) * (1 - phase) * smoothstep(0.45, 0, r) *
                                 smoothstep(0.12, 0, abs(r - phase * 0.45));
                    slope += d / max(r, 1e-3) * wave;
                }
                return slope;
            }

            // Drifting streaks of surface texture along a river: noise stretched along the flow and carried downstream,
            // cross-faded between two copies like the flowing ripples.
            float StreakNoise(float2 q, float2 dir)
            {
                float2 across = float2(-dir.y, dir.x);
                float2 uv = float2(dot(q, dir) * 0.3, dot(q, across) * 1.8);
                return PunchyValueNoise(uv) * 0.65 + PunchyValueNoise(uv * 2.3 + 3.7) * 0.35;
            }

            float FlowStreaks(float2 p, float2 dir, float speed, float t)
            {
                const float cycle = 4.0;
                float jitter = PunchyValueNoise(p * 0.03 + 3.1) * 2;
                float phase0 = frac(t / cycle + jitter);
                float phase1 = frac(t / cycle + jitter + 0.5);
                float w0 = 1 - abs(1 - 2 * phase0);
                float2 offset = dir * speed * cycle;
                float s = StreakNoise(p - offset * phase0, dir) * w0 + StreakNoise(p - offset * phase1 + 5.1, dir) * (1 - w0);
                // Cross-fading flattens the contrast mid-way; stretch it back out.
                return saturate((s - 0.5) * (1.6 + 0.8 * (1 - abs(2 * w0 - 1))) + 0.5);
            }

            // Slow bubbles in thick water: here and there a dome swells up, then pops (xy slope, z = bubble mask).
            float3 Bubbles(float2 p, float t)
            {
                float2 q = p * 0.9;
                float2 cell = floor(q);
                if (PunchyHash(cell + 6.6) > 0.18)
                    return 0;
                float2 centre = float2(PunchyHash(cell + 1.3), PunchyHash(cell + 4.1)) * 0.5 + 0.25;
                float2 d = frac(q) - centre;
                float life = frac(t * 0.15 + PunchyHash(cell + 8.8));
                float radius = 0.2 * smoothstep(0, 0.9, life) * (PunchyHash(cell + 2.2) * 0.6 + 0.4);
                float r = length(d);
                float dome = step(r, radius) * step(life, 0.92);
                float height = sqrt(max(radius * radius - r * r, 0));
                float2 slope = dome * d / max(height, 0.03) * 0.5;
                // The pop: a quick ring spreading from where the bubble was.
                float popAge = saturate((life - 0.92) / 0.08);
                float ring = (life > 0.92) * smoothstep(0.05, 0, abs(r - popAge * 0.35)) * (1 - popAge);
                slope += d / max(r, 1e-3) * ring * 1.5;
                return float3(slope, dome);
            }

            // The scene reflected in the water, found by marching the reflected ray through the camera depth texture
            // (rgb, a = how sure: 0 where the ray left the screen or found nothing, so the sky shows instead).
            half4 ScreenReflection(float3 origin, float3 dir)
            {
                // Clip and screen positions are linear in the world position, so the ray's are worked out once at its
                // origin and along its direction, and each step is a multiply-add instead of a matrix transform (W-117).
                float4 screen0 = ComputeScreenPos(TransformWorldToHClip(origin));
                float4 screenDir = ComputeScreenPos(mul(GetWorldToHClipMatrix(), float4(dir, 0)));
                float stepLength = 0.3;
                float travelled = 0, previous = 0;
                [loop] for (int i = 0; i < 28; i++)
                {
                    travelled += stepLength;
                    float4 screen = screen0 + screenDir * travelled;
                    if (screen.w <= 0.01)
                        return 0;
                    float2 uv = screen.xy / screen.w;
                    if (any(uv < 0) || any(uv > 1))
                        return 0;
                    float gap = screen.w - LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                    if (gap > 0 && gap < stepLength * 1.5 + 0.4)
                    {
                        // Narrow it down between the last point in front and this one behind.
                        float a = previous, b = travelled;
                        [unroll] for (int k = 0; k < 5; k++)
                        {
                            float m = (a + b) * 0.5;
                            float4 ms = screen0 + screenDir * m;
                            if (ms.w - LinearEyeDepth(SampleSceneDepth(ms.xy / ms.w), _ZBufferParams) > 0) b = m; else a = m;
                        }
                        float4 hs = screen0 + screenDir * b;
                        uv = hs.xy / hs.w;
                        float2 edge = min(uv, 1 - uv);
                        half fade = smoothstep(0, 0.06, min(edge.x, edge.y)) * (1 - smoothstep(20, 28, i));
                        return half4(SAMPLE_TEXTURE2D_X_LOD(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, UnityStereoTransformScreenSpaceTex(uv), 0).rgb, fade);
                    }
                    previous = travelled;
                    stepLength *= 1.22;
                }
                return 0;
            }

            half4 Frag(Varyings input, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float t = _PunchyClock;   // the shared clock, so every player sees the same ripples
                float2 p = input.positionWS.xz;
                float2 flow = input.data.xy;
                float flowAmount = saturate(length(flow));
                float2 flowDir = flow / max(flowAmount, 1e-3);
                float depth = input.data.z;
                float rapids = input.data.w;
                float murk = saturate(input.look.a);
                float glow = input.glow.x;
                float lava = saturate(input.glow.y);
                // White pools (#35): 1 in the middle, easing to 0 at the disc's edge (depth there is 1 - r², so the outer
                // third or so fades), so the shore shows through and no hard rim is drawn. 1 everywhere on other water.
                half rim = lerp(1, smoothstep(0.0, 0.55, depth), _SoftRim);
                // White pools' middle (W-17: it read as a milky blob): 0 at the shore, 1 at the centre. The body there
                // darkens to a deep, dark water colour and the glow thins out, so only a pale sheen is left round it.
                half whiteMiddle = _SoftRim * smoothstep(0.25, 1.0, depth);
                // Very murky water is thick, like oil or sludge.
                float viscous = smoothstep(0.55, 0.9, murk) * (1 - lava);
                float distanceFade = saturate(1 - distance(input.positionWS, GetCameraPositionWS()) / 350);
                // W-111: how steep the mesh face is (a waterfall's drop), from the mesh, not the ripples. Everything laid
                // out on the flat (xz) water (streaks, carried foam, flecks, fine detail, caustics, glitter, the sky
                // mirror) smears or runs upward on a near-vertical face and made falls glow, so it fades out there and
                // only the falling sheets below are left. The wide ramp keeps neighbouring triangles (each flat) from
                // showing a hard step in shade where they tilt past the threshold.
                float3 face = cross(ddy(input.positionWS), ddx(input.positionWS));
                face *= face.y < 0 ? -1 : 1;
                face = normalize(face);
                half steep = smoothstep(0.85, 0.35, face.y);

                // Rivers run faster in the deep middle than at the banks, and much faster down steep rapids.
                float riverSpeed = _FlowSpeed * (1 + 2.5 * rapids) * lerp(0.45, 1.25, saturate(depth / 1.6));
                // (The flow-mapped ripples grow choppier with speed, so faster water reads rougher, not just whiter.)
                // (Refactor 3.4, job 29: water was a third of the lake's GPU frame. Each layer below is skipped where its
                // weight is zero, so the picture is unchanged: flow on still water, rain in dry weather, thick-water swells
                // and murky rings on clear water, the toxic swirl on plain water.)
                float2 flowingSlope = 0;
                float streaks = 0.5;
                [branch] if (flowAmount > 0 || rapids > 0)
                {
                    flowingSlope = FlowingSlope(p, flowDir, riverSpeed, t) * (1 + 1.3 * rapids);
                    streaks = FlowStreaks(p, flowDir, riverSpeed, t);
                }
                float2 slope = lerp(WaveSlope(p, t), flowingSlope, flowAmount);
                // Rain rings only where it's actually raining (from the world weather map).
                float rain = PunchyWetnessAt(p).g * distanceFade;
                [branch] if (rain > 0)
                    slope += RainRipples(p, t) * rain * 0.8;
                // (Wakes keep their own strength: the ripples' follows the wind, and a swimmer stirs calm water too.)
                // (Only out to 45 m, a little past the 40 m the feeder sends ripples from, and only in the mask's cells a
                // ripple reaches into: the rest of the water skips the loop.)
                half wakeFoam = 0;
                float2 wakeSlope = 0;
                [branch] if (_PunchySwimmerCount > 0 && distance(input.positionWS, GetCameraPositionWS()) < 45 && NearRipple(p))
                    wakeSlope = SwimmerWakes(p, t, wakeFoam) * 0.6 * distanceFade;
                // Murky water barely ripples; thick water instead heaves in slow, broad swells and bubbles.
                slope *= 1 - 0.75 * murk;
                float3 bubbles = 0;
                float2 murkRings = 0, swells = 0;
                [branch] if (murk > 0)
                    murkRings = RainRipples(p * 0.45 + 13.7, t * 0.25) * murk * distanceFade * 0.6;
                [branch] if (viscous > 0)
                {
                    bubbles = Bubbles(p, t) * distanceFade;
                    swells = WaveSlope(p * 0.3, t * 0.12) * 0.5 + bubbles.xy;
                }
                slope += lerp(murkRings, swells, viscous);
                float strength = _WaveStrength * (0.5 + _PunchyWind) * (0.25 + 0.75 * distanceFade);
                // Streaks along a river are bands of rougher and smoother water.
                strength *= lerp(1, 0.55 + 0.9 * streaks, flowAmount);
                // Fine ripples: two reads of the shared detail texture drifting different ways (and downstream on a
                // river), stronger in wind and white water, fading with distance; on the shading normal only, so
                // reflections stay crisp. Their sparkle channels line up here and there for glitter in the sun.
                // The downstream drift is cycled, not left to grow: river speed varies across a river (with depth), so
                // a drift of speed × time would pull neighbouring pixels further apart in the texture every second,
                // turning the detail to noise and the texture reads incoherent (slow) after a few minutes. Each read
                // drifts over its own 4 s cycle, half a cycle apart, and fades out as it jumps back.
                const float detailCycle = 4.0;
                float phaseA = frac(t / detailCycle), phaseB = frac(t / detailCycle + 0.5);
                float2 driftStep = flowDir * flowAmount * riverSpeed * detailCycle;
                // With the detail off (-waterdetail 0, for profiling), the reads are skipped and the flat values below
                // leave no ripples, caustics or glitter. Past 350 m (distanceFade 0) the ripples and glitter are faded
                // out anyway, so the reads are skipped there too (W-117): only caustics in far shallows would use them,
                // and at that distance the mipmapped height averages out below their threshold.
                half4 fine0 = half4(0.5, 0.5, 0, 0);
                half4 fine1 = fine0;
                UNITY_BRANCH
                if (_PunchyWaterDetail > 0.5 && distanceFade > 0)
                {
                    fine0 = SAMPLE_TEXTURE2D(_PunchyCaveDetail, sampler_PunchyCaveDetail, p * _DetailScale + float2(t * 0.031, t * 0.017) - driftStep * phaseA * _DetailScale * 0.6);
                    fine1 = SAMPLE_TEXTURE2D(_PunchyCaveDetail, sampler_PunchyCaveDetail, p * _DetailScale * 1.7 + float2(-t * 0.022, t * 0.029) - driftStep * phaseB * _DetailScale * 1.1);
                }
                // On still water both reads count fully; on flowing water each fades in and out with its cycle.
                // (A triangle 0..1..0 over the cycle, doubled, averages 1, like a still read.)
                float fadeA = lerp(1, 2 * (1 - abs(1 - 2 * phaseA)), flowAmount);
                float fadeB = lerp(1, 2 * (1 - abs(1 - 2 * phaseB)), flowAmount);
                float2 fine = (fine0.rg - 0.5) * fadeA + (fine1.rg - 0.5) * fadeB;
                float fineStrength = _DetailStrength * _PunchyCaveDetailReady * (0.35 + _PunchyWind + rapids) * distanceFade * (1 - 0.8 * murk) * (1 - lava) * (1 - steep);
                strength *= 1 - 0.8 * steep;
                float3 n =normalize(float3(-slope.x * strength - fine.x * fineStrength - wakeSlope.x, 1, -slope.y * strength - fine.y * fineStrength - wakeSlope.y));
                // Reflections use a calmer normal for the broad waves, so they don't break into noise, but the fine
                // ripples do move them: mirrored trees and clouds wobble and break up near the camera. Faded out with
                // distance (and so the screen-space march's ray, the sky lookup and the Fresnel stay steady far off).
                float rippleNear = 1 - smoothstep(25, 110, distance(input.positionWS, GetCameraPositionWS()));
                float fineReflect = fineStrength * 0.7 * rippleNear;
                float3 nReflect = normalize(float3(-slope.x * strength * 0.4 - fine.x * fineReflect - wakeSlope.x * 0.6, 1, -slope.y * strength * 0.4 - fine.y * fineReflect - wakeSlope.y * 0.6));
                // Running water underground has no calm sky to mirror: its reflection breaks up with the flow and fades.
                float caveRun = _PunchyUnderground * flowAmount;
                nReflect = normalize(lerp(nReflect, n, caveRun));
                if (!frontFace)
                {
                    n = -n;
                    nReflect = -nReflect;
                }

                // How much water the view ray passes through before hitting the ground behind the surface.
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float surfaceEye = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                float thickness = max(sceneEye - surfaceEye, 0);

                float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
                Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half shadow = light.shadowAttenuation * (1 - PunchyCloudShadow(input.positionWS));

                // Looking up from under the water: Snell's window (Zach, 11-08). Light from above reaches the eye only
                // through a cone of about 48.6 degrees round the surface normal (asin(1 / 1.333)), so the whole sky shows
                // squeezed into a disc overhead, bent and broken by the ripples. Outside the cone the surface is a mirror
                // of the water below (total internal reflection) and reads as the water's own colour. The sky is read along
                // the refracted ray, which does the squeezing; the scene behind isn't read from the screen, because it is
                // already fogged by its full distance under water. This replaces the above-water shading on the underside,
                // so it skips the lighting, sky reflection, foam and glints there (job 29: the underside cost ~9.7 ms).
                [branch] if (!frontFace && GetCameraPositionWS().y < input.positionWS.y)
                {
                    // Ripples bend the window hard near the camera and less far off, where the same slope reads smaller.
                    const float SnellBend = 2.2;
                    float3 nWindow = normalize(float3(n.x * SnellBend, n.y, n.z * SnellBend));
                    float3 up = -nWindow;                 // the face's normal turned up, into the air
                    float3 look = -v;                     // from the eye towards the surface
                    float cosI = saturate(dot(look, up));
                    float sinI = sqrt(1 - cosI * cosI);
                    // Per channel, with slightly different indices, so the rim splits into a faint rainbow; transmission
                    // fades over the last few degrees before the critical angle, as the Fresnel term from below does.
                    half3 window = 1 - smoothstep(0.86, 1.0, sinI * half3(1.326, 1.333, 1.342));
                    // (refract wants the normal on the incoming side, facing the eye: the face's own, pointing down.)
                    float3 r = refract(look, nWindow, 1.333);
                    if (dot(r, r) < 1e-4)
                        r = up;
                    half3 sky = PunchySkyColor(r, true, true) * lerp(1, 0.04, max(_PunchyUnderground, _UnderRock));
                    // The sun through the window: a bright, broken spot that dances with the ripples.
                    sky += light.color * pow(saturate(dot(r, light.direction)), 900) * 6 * shadow;
                    // Outside the window: the water below, mirrored. The underwater fog colour is what that looks like.
                    half3 below = _PunchyUnderwater.w > 0 ? (half3)_PunchyUnderwater.rgb : _DeepColor.rgb;
                    below *= 0.75 + 0.25 * saturate(cosI * 2);
                    // Just inside the rim the mirrored water and the sky mix, brightest at the edge.
                    half3 under = lerp(below, sky, window) + sky * window * (1 - window) * 0.6;
                    under = lerp(PunchyAtmosphere(under, input.positionWS), under, _PunchyUnderground);
                    return half4(under, rim);
                }
                half3 ambient = SampleSH(n);
                // Underground the sun and sky don't reach (cave rock deep down casts no sun shadow, so it's done here).
                shadow *= 1 - _PunchyUnderground;
                ambient *= lerp(1, 0.06, _PunchyUnderground);

                // Glowing (toxic) water swirls between its colour and a shifted hue in slow-moving patches, a few metres
                // across so a small pool shows both, and never wholly the shifted hue (a toxic pool read as one flat
                // purple, G-27).
                [branch] if (glow > 0 && lava < 1)
                {
                    half swirl = smoothstep(0.35, 0.65, PunchyValueNoise(p * 0.3 + float2(t * 0.05, -t * 0.065)) * 0.7 + PunchyValueNoise(p * 1.05) * 0.3);
                    input.look.rgb = lerp(input.look.rgb, input.look.rbg, swirl * 0.6 * saturate(glow * 3) * (1 - lava));
                }
                half3 shallowColor = lerp(_ShallowColor.rgb, input.look.rgb, saturate(murk * 1.5));
                half3 deepColor = lerp(_DeepColor.rgb, input.look.rgb * 0.2, saturate(murk * 1.5));
                half3 waterColor = lerp(deepColor, shallowColor, exp(-thickness * _Absorption * (1 + murk * 8)));
                waterColor *= 1 - 0.7 * whiteMiddle;
                // River streaks: lighter and darker bands drifting downstream.
                waterColor *= lerp(1, 0.88 + 0.24 * streaks, flowAmount * (1 - murk) * (1 - steep));
                half3 body = waterColor * (ambient + light.color * saturate(dot(n, light.direction)) * shadow * 0.6);
                // The player's lantern (it's lit underground): it lights the water's body and foam, with a highlight, so
                // cave pools and streams read as water rather than holes.
                half3 lanternLit = 0;
                half lanternGlint = 0;
                if (_PunchyLantern.w > 0)
                {
                    float3 toLantern = _PunchyLantern.xyz - input.positionWS;
                    float reach = saturate(1 - length(toLantern) / _PunchyLanternColor.w);
                    reach *= reach;
                    lanternLit = _PunchyLanternColor.rgb * _PunchyLantern.w * reach;
                    lanternGlint = pow(saturate(dot(n, normalize(normalize(toLantern) + v))), 400) * reach * _PunchyLantern.w;
                }
                body += waterColor * lanternLit * 0.8;
                // Shallows: light focused by the fine ripples dances over the bed where the water is thin.
                half shallow = exp(-thickness * 0.9) * (1 - murk) * (1 - lava);
                half caustic = smoothstep(0.55, 0.85, fine0.b * 0.5 + fine1.b * 0.5) * shallow * (1 - steep);
                body += waterColor * caustic * (light.color * shadow * 0.8 + lanternLit);
                // Ripple faces turned to the light read a touch lighter, those turned away a touch darker.
                body *= 1 + clamp(dot(fine, light.direction.xz), -1, 1) * fineReflect * 0.6;

                half fresnel = 0.02 + 0.98 * pow(1 - saturate(dot(nReflect, v)), 5);
                // Reflect the live sky (same function as the skybox, so reflections follow the weather). Keep reflections
                // pointing at the sky; tilted ripples would otherwise mirror the lower hemisphere.
                float3 r = reflect(-v, nReflect);
                r.y = max(r.y, 0.03);
                // Underground (cave pools and streams, seen from inside) there is no sky to mirror: only darkness, and
                // whatever the screen-space march finds.
                // Cave water is under rock even near a mouth, where the camera isn't counted as underground yet.
                half underRock = max(_PunchyUnderground, _UnderRock);
                half3 reflection = PunchySkyColor(r, false, true) * lerp(1, 0.04, underRock);
                fresnel *= 1 - 0.5 * murk * (1 - viscous);
                fresnel *= 1 - 0.6 * caveRun;
                // (A fall's face, seen side on, would mirror the whole bright sky through its flat-water normal.)
                fresnel *= 1 - 0.85 * steep;
                // ...and the scene where the reflected ray finds it (the far bank, trees, rocks, creatures). The march is
                // the water's dearest step; where the final Fresnel leaves the reflection under SsrMinWeight of the
                // colour (looking steeply down, murky water, fall faces), what it would find barely shows, so it's
                // skipped (W-117).
                const half SsrMinWeight = 0.025;
                if (frontFace && _PunchyWaterNoSsr < 0.5 && _ScreenReflection > 0 && lava < 0.99 && fresnel * _ScreenReflection > SsrMinWeight)
                {
                    half4 scene = ScreenReflection(input.positionWS + float3(0, 0.02, 0), r);
                    reflection = lerp(reflection, scene.rgb, scene.a * _ScreenReflection);
                }
                float3 h = normalize(light.direction + v);
                // Thick water has a broad, soft sheen rather than a sharp glint.
                half glint = pow(saturate(dot(n, h)), exp2(lerp(10 * _Smoothness + 1, 6, viscous))) * shadow * (1 - 0.6 * viscous)
                             * (1 - underRock);
                glint *= 1 - steep;
                half3 color = lerp(body, reflection, fresnel) + light.color * glint * 4 * (1 - 0.6 * murk * (1 - viscous));
                color += _PunchyLanternColor.rgb * lanternGlint * 0.8;
                // Glitter: where both fine reads sparkle at once and the ripple catches the sun just so.
                half glitter = smoothstep(0.55, 0.9, fine0.a * fine1.a * 1.6) * _PunchyCaveDetailReady * pow(saturate(dot(n, h)), 300) * shadow
                               * _Sparkle * distanceFade * (1 - murk) * (1 - lava) * (1 - steep);
                color += light.color * glitter * 10;
                // Toxic pools glow from within, pulsing slowly in patches.
                // (White pools: a band of pale glow just inside the soft rim, faint in the middle.)
                color += input.look.rgb * glow * rim * rim * (1 - 0.8 * whiteMiddle) * (1 - lava) * (0.7 + 0.3 * sin(t * 0.6 + PunchyValueNoise(p * 0.08) * 6.28)) * 1.5;
                // White pools stay see-through over their whole width, so the floor shows under the middle too.
                half bodyAlpha = (1 - exp(-thickness * _Opacity * (1 + murk * 12))) * lerp(1, 0.55, _SoftRim);
                half alpha = saturate(bodyAlpha + fresnel + glint + glitter + lanternGlint);

                // Thick water: an oily rainbow film in the reflection, and pale scum drifting in slow rafts.
                if (viscous > 0)
                {
                    float film = PunchyValueNoise(p * 0.12 + t * 0.01) * 2 + dot(n, v) * 3;
                    half3 sheen = 0.5 + 0.5 * cos(6.28 * (film + float3(0, 0.33, 0.67)));
                    color += (sheen - 0.5) * (0.04 + 0.12 * fresnel) * viscous * reflection;
                    color += light.color * bubbles.z * 0.03 * viscous;
                    half scum = smoothstep(0.58, 0.72, PunchyValueNoise(p * 0.11 + float2(t * 0.015, t * 0.008)) * 0.75 +
                                                      PunchyValueNoise(p * 0.6) * 0.25);
                    half3 scumColor = input.look.rgb * 1.3 + half3(0.06, 0.05, 0.0);
                    half3 scumLit = scumColor * (ambient + light.color * shadow * saturate(light.direction.y) * 0.8);
                    color = lerp(color, scumLit, scum * viscous * 0.85);
                    alpha = max(alpha, scum * viscous);
                }

                // Broken foam line where the water thins out against the shore (and around rocks in the stream).
                half foamNoise = PunchyValueNoise(p * 0.9 + float2(t * 0.25, -t * 0.18)) * 0.6 + PunchyValueNoise(p * 2.7 - t * 0.4) * 0.4;
                // The line laps: it creeps up the shore and draws back, unevenly along it.
                half lap = 0.75 + 0.25 * sin(t * 1.4 + foamNoise * 5);
                // Where the water itself is deep, thin water in front of the bed can only be something standing in it (a
                // swimmer, a rock): a much narrower, fainter line there.
                half inDeep = smoothstep(0.5, 1.3, depth);
                half foam = smoothstep(foamNoise * 0.7, 1, saturate(1 - thickness / (_FoamDistance * lap * (1 + rapids) * lerp(1, 0.3, inDeep))));
                foam *= lerp(1, 0.55, inDeep);
                // Not a solid white strip: clear right at the edge (a wet line, where the bed shows through), and broken
                // into patches along it. Underground, still shallow water barely foams (a shallow stream would otherwise
                // count as shore all over).
                foam *= smoothstep(0.02, 0.12, thickness) * (0.55 + 0.45 * smoothstep(0.3, 0.7, PunchyValueNoise(p * 1.6 + t * 0.3)));
                foam *= lerp(1, 0.2, _PunchyUnderground);
                foam *= 1 - viscous * 0.7;
                // W-113 item 15: on a steep face the water lies a hair over its bed, so the thickness (from the depth buffer)
                // flickers pixel to pixel and this line came out as fine static all down steep cave creeks. The falling
                // sheets below carry the white there instead.
                foam *= 1 - steep;
                // White water: the churned (steepest) parts of the flowing ripples turn to foam on rapids, broken into
                // flecks, never a flat sheet (G-28; not gated by the streaks, whose direction turns sharply at the river
                // mesh's corners and drew zigzags), and flecks ride the streaks downstream on any river.
                half churn = smoothstep(0.35, 0.75, length(flowingSlope));
                // W-98: the foam rides the current (FlowFoam) instead of sitting still under it, and covers more of the
                // water the faster it runs: a few drifting patches as the river quickens, most of the surface in a rapid.
                half whiteWater = 0;
                [branch] if (rapids > 0.02 && flowAmount > 0.02)
                {
                    half carried = FlowFoam(p, flowDir * flowAmount, riverSpeed, t);
                    half cover = smoothstep(0.7 - 0.26 * rapids, 0.88 - 0.16 * rapids, carried);
                    // (From 0.1: cave brooks carry a floor of 0.12 down the middle and should stay clear there.)
                    whiteWater = cover * smoothstep(0.1, 0.6, rapids) * (0.55 + 0.45 * churn) * distanceFade;
                }
                half3 foamParts = half3(foam, min(max(whiteWater * 0.85, rapids * rapids * churn * 0.35), 0.85) * (1 - steep),
                                        flowAmount * smoothstep(0.9 - 0.06 * rapids, 0.98, streaks) * (0.2 + 0.4 * rapids) * distanceFade * (1 - viscous)
                                        // (Fewer in white water, where they scratched thin lines over the carried foam.)
                                        * (1 - 0.75 * smoothstep(0.3, 0.8, rapids))
                                        // Not on running cave water: there the flecks came out as thin lines across the
                                        // flow (Z-34, seen with -waterprobe 4), striping every cave stream.
                                        * (1 - caveRun) * (1 - steep));
                foam = max(foam, max(foamParts.y, foamParts.z));
                // W-98: a waterfall is the river's own surface where it tips over a drop (no curtain over it any more):
                // where the face is steep (from the mesh, not the ripples), it turns to falling water, white sheets and
                // darker gaps streaming straight down across its width.
                // (W-111: the face and its steepness come from the top of the pass.)
                half falling = steep * smoothstep(0.0, 0.1, flowAmount) * (1 - viscous) * (1 - lava);
                // A fall is a thick sheet: its darker gaps are still water, not windows onto the rock behind (a short
                // drop has little water behind its face, so the body alone left a see-through shelf).
                alpha = max(alpha, falling * 0.85);
                [branch] if (falling > 0.01)
                {
                    // W-113 item 15: the sheets were read along the face's own across direction, dot(p, across); that
                    // direction comes from screen derivatives and wobbles a hair pixel to pixel, and times a world position
                    // over a kilometre out it turned the sheets to static on every steep cave creek. Read along the fixed
                    // x and z axes instead and blend by how the face is turned (as triplanar mapping does).
                    float2 across = normalize(float2(-face.z, face.x) + 1e-5);
                    float2 axisWeight = across * across;
                    float fallV = input.positionWS.y + t * 5.5;
                    // (A third, finer octave breaks the slow sheets into ropes and flecks.)
                    half sheet = FallSheet(p.x, fallV) * axisWeight.x + FallSheet(p.y + 11.3, fallV) * axisWeight.y;
                    falling *= lerp(0.2, 1, smoothstep(0.32, 0.78, sheet));
                    foam = max(foam, falling * 0.9);
                }
                foam = max(foam, wakeFoam * (1 - viscous));
                // (On a fall the sun lights the face as it is turned, not as flat water: lit flat, a fall's face in shade
                // still glowed white at noon.)
                half sunFacing = lerp(saturate(light.direction.y), 0.4 + 0.5 * saturate(dot(face, light.direction)), steep);
                half3 foamLit = _FoamColor.rgb * (ambient + light.color * shadow * sunFacing + lanternLit);
                color = lerp(color, foamLit, foam * (1 - lava));
                alpha = max(alpha, foam * (1 - lava));

                // Lava: mostly bright molten orange and yellow, flowing in slow, domain-warped swirls (noise warped by
                // noise, each layer drifting its own way, so nothing looks like cells), with thin, sparse islands of
                // dark crust riding on it; they gather and thicken towards the edges, where it cools against the rock.
                // It hardly reflects anything.
                if (lava > 0)
                {
                    // Carried by any flow, and always drifting a little, so even a still pool visibly moves.
                    float2 q = (p - flowDir * flowAmount * t * 0.15 + float2(t * 0.04, t * 0.025)) * 0.28;
                    float2 w1 = float2(PunchyValueNoise(q * 1.3 + float2(t * 0.021, 0)), PunchyValueNoise(q * 1.3 + float2(5.2, -t * 0.017)));
                    float2 w2 = float2(PunchyValueNoise((q + w1 * 1.6) * 2.1 + float2(-t * 0.013, 1.7)),
                                       PunchyValueNoise((q + w1 * 1.6) * 2.1 + float2(8.3, t * 0.019)));
                    float swirl = PunchyValueNoise(q + w1 * 1.4 + w2 * 0.8 + t * 0.006) * 0.6 + PunchyValueNoise((q + w2) * 3.3 - t * 0.01) * 0.4;
                    // Black plates of cooled crust over about a third of it, riding the swirl, plus a thin rim where the
                    // melt meets the rock; the plates are split by thin cracks that still glow.
                    half crust = smoothstep(0.55, 0.6, swirl);
                    half crack = 1 - smoothstep(0.0, 0.035, abs(PunchyValueNoise((q + w2 * 0.5) * 7.0) - 0.5));
                    crust *= 1 - crack * 0.85;
                    crust = max(crust, 1 - saturate(thickness / 0.2));
                    // Crust edges glow hotter, as if the melt is just closing over them.
                    half edge = smoothstep(0.49, 0.55, swirl) * (1 - crust);
                    half band = PunchyValueNoise((q + w1 * 2.0) * 4.0 + t * 0.03);
                    // Red melt; orange on the warmer bands, yellow only on the hottest ones, the crust edges and the
                    // cracks.
                    half3 red = lerp(half3(0.9, 0.1, 0.015), input.look.rgb, 0.15);
                    half3 molten = lerp(red, half3(1.0, 0.3, 0.03), smoothstep(0.6, 0.85, band));
                    molten = lerp(molten, half3(1.0, 0.75, 0.22), saturate(smoothstep(0.86, 0.97, band) + edge * 0.6 + crack * 0.5));
                    // Slow bubbles rise through the open melt and pop, brighter where they break the surface.
                    half bubble = Bubbles(p * 1.4, t * 0.8).z * (1 - crust);
                    molten = lerp(molten, half3(1.0, 0.6, 0.15), bubble * 0.7);
                    half pulse = 0.9 + 0.1 * sin(t * 0.7 + swirl * 6.28);
                    // Bright enough to glow, not so bright the tonemapper washes the colour out to white.
                    half3 hot = molten * (0.55 + 0.3 * band + 0.3 * edge + 0.4 * bubble) * pulse;
                    half3 rock = half3(0.03, 0.025, 0.025);
                    half3 rockLit = rock * (ambient + light.color * saturate(dot(n, light.direction)) * shadow) + red * 0.04;
                    half3 lavaColor = lerp(hot, rockLit, crust);
                    // A faint, rough sheen on the crust only.
                    lavaColor += reflection * fresnel * 0.06 * crust;
                    color = lerp(color, lavaColor, lava);
                    alpha = lerp(alpha, 1, lava);
                }

                alpha *= rim;

                if (_PunchyWaterProbe > 0.5)
                    return _PunchyWaterProbe < 1.5 ? half4(foam, foam, foam, 1) : _PunchyWaterProbe > 3.5 ? half4(foamParts * 2, 1) : _PunchyWaterProbe < 2.5 ? half4(saturate(thickness), frac(thickness * 4), 0, 1) : half4(alpha, alpha, alpha, 1);
                // The atmosphere (fog and weather) only above ground; a cave pool is right in front of you.
                if (_PunchyUnderground < 0.999)
                    color = lerp(PunchyAtmosphere(color, input.positionWS), color, _PunchyUnderground);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
