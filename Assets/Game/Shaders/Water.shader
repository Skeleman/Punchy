Shader "Punchy/Water"
{
    // Sea, lake and river surfaces. Colour comes from how much water the view ray passes through (read from the
    // camera depth texture): clear and tinted in the shallows, dark further down. Reflections rise at grazing angles
    // (Fresnel): the scene itself where a short screen-space march finds it (banks, trees), the live sky elsewhere.
    // The sun glints, ripples are sums of directional waves, rivers carry their ripples and streaks downstream
    // (faster in the deep middle), and foam lines the shore and the rapids.
    // Two other liquids share the sheet: very murky water (swamps) turns thick and oily, slowly heaving and bubbling,
    // with drifting scum; lava (the lava amount in the vertex) is a dark crust churning slowly in flow-noise swirls, hot
    // veins glowing through it, closing up at the edges, never reflective. (Cave pools use the same path, drawn by
    // CaveSystem.)
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

        // (Both passes share the material's constants: the SRP batcher wants them laid out the same in every pass.)
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
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
        ENDHLSL

        // The water's depth first (playtest 11-08 item 4): the swell lifts the sea into waves that hide each other at low
        // angles, and see-through water drawn without depth layered the hidden faces over the near ones in mesh order
        // (and the far side of a crest over its face). This pass lays down only the nearest surface's depth, so the
        // colour pass below shades just that layer per pixel (and skips the hidden ones). It writes the depth buffer
        // only: the camera depth texture the colour pass reads (reflections, refraction, thickness) was copied before.
        Pass
        {
            Name "WaterDepth"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite On
            ColorMask 0
            Cull Off
            // (A hair further than the colour pass's own depth, so that pass always passes its own test.)
            Offset 1, 1

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "SharedClock.hlsl"
            #include "WaterSwell.hlsl"

            struct DepthAttributes
            {
                float3 positionOS : POSITION;
                float4 data       : TEXCOORD0;   // as WaterAttributes: z the depth below the surface
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 DepthVert(DepthAttributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float swell;
                return TransformWorldToHClip(PunchySwellVertex(TransformObjectToWorld(input.positionOS), input.data.z, _PunchyClock, swell));
            }

            half4 DepthFrag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

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
            #include "WaterSwell.hlsl"

            // Weather global (WeatherSystem): 0..1 wind strength at the camera.
            half _PunchyWind;

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
            // How far the ripples bend the view of what's under the water, in screen units (0: off, -waterrefraction 0).
            // CaveSystem sets it with the other water globals; a global so the profiler can compare.
            float _PunchyRefraction;
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
                float  swell      : TEXCOORD5;   // how strongly the sea swells here (WaterSwell.hlsl), 0 elsewhere
                float  sea        : TEXCOORD6;   // 1 on the sea (its surface at sea level), 0 on lakes and rivers
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(WaterAttributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS);
                o.sea = step(abs(pos.positionWS.y - _PunchySwell.y), 0.05);
                // The sea rises and falls in swells (-noswells for the flat sea); the same move as the depth pass's.
                pos.positionWS = PunchySwellVertex(pos.positionWS, input.data.z, _PunchyClock, o.swell);
                pos.positionCS = TransformWorldToHClip(pos.positionWS);
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
            // The turn for the second fine-detail read (cos, sin of about 34 degrees), so its texel grid never lines up
            // with the first's.
            static const float2x2 DetailTurn = float2x2(0.829, -0.559, 0.559, 0.829);

            float2 FlowingSlope(float2 p, float2 flow, float speed, float t)
            {
                const float cycle = 3.0;
                float jitter = PunchyValueNoise(p * 0.03) * 2;
                float phase0 = frac(t / cycle + jitter);
                float phase1 = frac(t / cycle + jitter + 0.5);
                float w0 = 1 - abs(1 - 2 * phase0);
                float2 offset = flow * speed * cycle;
                // (Zach, 11-08: rivers should read as moving water, not only by their foam.) The ripples are stretched
                // across the current, about twice as long across it as along it, so they lie as ridges the water
                // carries downstream; the slope comes back through the same stretch (the chain rule).
                float2 along = flow / max(length(flow), 1e-3);
                float2 across = float2(-along.y, along.x);
                const float AcrossStretch = 0.45;
                float2 q = along * dot(p, along) + across * dot(p, across) * AcrossStretch;
                float2 g = (WaveSlope(q - offset * phase0, t * 0.3) * w0 +
                            WaveSlope(q - offset * phase1 + 7.3, t * 0.3) * (1 - w0)) * rsqrt(w0 * w0 + (1 - w0) * (1 - w0));
                return along * dot(g, along) + across * dot(g, across) * AcrossStretch;
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

            // Lava (playtest 11-08 item 21; the technique is inspired by nimitz's "Noise animation - Lava", shadertoy
            // lslXRS, written here from its description): flow noise. Each octave reads the noise's gradient, turns it
            // round over time (rotating gradients) and pushes the domain along it, so the next octave is carried by the
            // last one's flow, then eases the pushed domain back towards a plainly drifting one so it never tears; the
            // octaves are sine-ridged, so where they line up thin ridges run through, which the colour turns to hot veins.
            float LavaNoise(float2 x)
            {
                return PunchyValueNoise(x * 1.6);
            }

            float2 LavaGradient(float2 x)
            {
                const float e = 0.09;
                return float2(LavaNoise(x + float2(e, 0)) - LavaNoise(x - float2(e, 0)),
                              LavaNoise(x + float2(0, e)) - LavaNoise(x - float2(0, e))) / (2 * e);
            }

            // How much crust over the melt at p (small: a hot vein; large: dark crust), on a clock that runs slowly.
            float LavaFlow(float2 p, float time, int octaves)
            {
                float2 bp = p;
                float z = 2, crust = 0;
                [loop] for (int i = 1; i <= octaves; i++)
                {
                    p += time * 0.6;
                    bp += time * 1.9;
                    float2 g = LavaGradient(i * p * 0.34 + time);
                    // (Turned with time, and against a gentle slope of position, so the swirls don't all turn alike.)
                    float turn = time * 6 - (0.05 * p.x + 0.03 * p.y) * 40;
                    float c = cos(turn), s = sin(turn);
                    g = float2(g.x * c - g.y * s, g.x * s + g.y * c);
                    p += g * 0.5;
                    crust += (sin(LavaNoise(p) * 7) * 0.5 + 0.5) / z;
                    p = lerp(bp, p, 0.77);
                    z *= 1.4;
                    p *= 2;
                    bp *= 1.9;
                }
                return crust;
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
            // (Refactor 3.4: steps, their growth and the refinement steps come from the water's quality policy, by tier;
            // 28 growing 1.22x and 5 is the march as tuned, which High keeps near the camera.)
            half4 ScreenReflection(float3 origin, float3 dir, int steps, float growth, int refine)
            {
                // Clip and screen positions are linear in the world position, so the ray's are worked out once at its
                // origin and along its direction, and each step is a multiply-add instead of a matrix transform (W-117).
                float4 screen0 = ComputeScreenPos(TransformWorldToHClip(origin));
                float4 screenDir = ComputeScreenPos(mul(GetWorldToHClipMatrix(), float4(dir, 0)));
                float stepLength = _PunchyWaterSsrGrowth.w;
                float travelled = 0, previous = 0;
                [loop] for (int i = 0; i < steps; i++)
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
                        [loop] for (int k = 0; k < refine; k++)
                        {
                            float m = (a + b) * 0.5;
                            float4 ms = screen0 + screenDir * m;
                            if (ms.w - LinearEyeDepth(SampleSceneDepth(ms.xy / ms.w), _ZBufferParams) > 0) b = m; else a = m;
                        }
                        float4 hs = screen0 + screenDir * b;
                        uv = hs.xy / hs.w;
                        float2 edge = min(uv, 1 - uv);
                        half fade = smoothstep(0, 0.06, min(edge.x, edge.y)) * (1 - smoothstep(steps * 0.71, steps, i));
                        return half4(SAMPLE_TEXTURE2D_X_LOD(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, UnityStereoTransformScreenSpaceTex(uv), 0).rgb, fade);
                    }
                    previous = travelled;
                    stepLength *= growth;
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
                // The water's quality policy (refactor 3.4, WaterQuality.cs): how big a metre of water looks here decides
                // the reflection march's tier, refraction, how many swell waves are summed and the fine detail's fade
                // (distanceFade: 350 m at the reference view, as tuned).
                float pixelsPerMetre = PunchyWaterPixelsPerMetre(distance(input.positionWS, GetCameraPositionWS()));
                float distanceFade = saturate(1 - _PunchyWaterDetailAt.y / pixelsPerMetre);
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
                    // (Stronger as the river quickens, so the current shows in the surface itself.)
                    flowingSlope = FlowingSlope(p, flowDir, riverSpeed, t) * (1 + 1.3 * rapids) * (1 + 0.5 * saturate(riverSpeed));
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
                    // (The second read is turned about 34 degrees: with both reads square to the texture, their texel grids
                    // lined up and the glitter, which needs both to sparkle at once, came out in rows and columns, crossing
                    // stripes of glints on open water (playtest 11-07, 11-08). Its normal is turned back below.)
                    fine1 = SAMPLE_TEXTURE2D(_PunchyCaveDetail, sampler_PunchyCaveDetail, mul(DetailTurn, p * _DetailScale * 1.7 - driftStep * phaseB * _DetailScale * 1.1) + float2(-t * 0.022, t * 0.029));
                }
                // On still water both reads count fully; on flowing water each fades in and out with its cycle.
                // (A triangle 0..1..0 over the cycle, doubled, averages 1, like a still read.)
                float fadeA = lerp(1, 2 * (1 - abs(1 - 2 * phaseA)), flowAmount);
                float fadeB = lerp(1, 2 * (1 - abs(1 - 2 * phaseB)), flowAmount);
                float2 fine = (fine0.rg - 0.5) * fadeA + mul(transpose(DetailTurn), fine1.rg - 0.5) * fadeB;
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
                // Sea swell: tilt both normals by the swell's own slope (worked out per pixel, as the swells are only a few
                // vertices long far off), and foam where the waves bunch the surface hardest: whitecaps in the wind, and
                // over the shelf, where they shoal and steepen, breakers whatever the wind.
                half swellFoam = 0;
                [branch] if (input.swell > 0)
                {
                    PunchySwellResult sw = PunchySwell(input.positionWS.xz, t, input.swell, depth, PunchySwellWaveCount(pixelsPerMetre));
                    // (Item 19, waves too round: the slope as the displaced surface has it, steeper where the waves bunch
                    // towards a crest and gentler in the stretched troughs, so crests read peaked even between vertices.)
                    float2 swellSlope = sw.slope / max(1 - sw.squeeze, 0.3);
                    n = normalize(float3(n.x / n.y - swellSlope.x, 1, n.z / n.y - swellSlope.y));
                    nReflect = normalize(float3(nReflect.x / nReflect.y - swellSlope.x, 1, nReflect.z / nReflect.y - swellSlope.y));
                    // Whitecaps (playtest 11-08 item 6: none were seen, gated to a strong wind and the very crests): from a
                    // moderate wind up, on the fronts the waves squeeze together (the Gerstner bunching, so they ride the
                    // crests as the waves run), more of them the harder it blows, broken into patches.
                    half wind = saturate(_PunchySwell.z);
                    half shoal = 1 - smoothstep(_PunchySwellDepths.z, _PunchySwellDepths.w, depth);
                    // (Thresholds raised with the choppier short waves, item 19, which bunch the surface harder.)
                    half capsFrom = lerp(0.25, 0.18, saturate((wind - 0.3) / 0.6));
                    capsFrom = lerp(capsFrom, min(capsFrom, 0.2), shoal);
                    half caps = smoothstep(capsFrom, capsFrom + 0.06, sw.squeeze) * max(smoothstep(0.25, 0.5, wind), shoal);
                    // (Finer and sparser than the old crest foam: a storm's caps came out as broad white blobs.)
                    half breakUp = PunchyValueNoise(p * 2.3 + float2(t * 0.21, -t * 0.13)) * 0.6 + PunchyValueNoise(p * 6.1 - t * 0.3) * 0.4;
                    swellFoam = saturate(caps * smoothstep(0.5, 0.72, breakUp) * 1.6) * distanceFade;
                }
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
                    // Near the top (playtest 11-08 item 8): within the last metre or so the window gives way to the real
                    // view above, read from the screen behind the surface and wobbled by the ripples, so the shore and
                    // trees show before the lens breaks through (and surfacing doesn't pop). Where nothing is behind the
                    // surface (the sky: the skybox draws plain water colour down here), the window's sky stands in.
                    half nearTop = (1 - smoothstep(0.2, 1.4, _PunchyUnderwaterSurface.y)) * step(1e-4, _PunchyUnderwater.w) * (1 - _UnderRock);
                    [branch] if (nearTop > 0)
                    {
                        float2 aboveUV = saturate(GetNormalizedScreenSpaceUV(input.positionCS) + n.xz * 0.04);
                        float aboveEye = LinearEyeDepth(SampleSceneDepth(aboveUV), _ZBufferParams);
                        float surfaceEye = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                        half3 above = SAMPLE_TEXTURE2D_X_LOD(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, UnityStereoTransformScreenSpaceTex(aboveUV), 0).rgb;
                        half isSky = step(_ProjectionParams.z * 0.95, aboveEye) + step(aboveEye, surfaceEye);
                        above = lerp(above, sky, saturate(isSky));
                        under = lerp(under, above, nearTop * 0.85);
                    }
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
                // (Refactor 3.4: the march's tier by how big the water looks; it fades out over the far tier's last third,
                // so no ring shows where it stops, and Low reflections skip it everywhere.)
                float4 tiers = _PunchyWaterTiers;
                int ssrTier = pixelsPerMetre > tiers.y ? 0 : pixelsPerMetre > tiers.z ? 1 : pixelsPerMetre > tiers.w ? 2 : 3;
                int ssrSteps = ssrTier == 0 ? (int)_PunchyWaterSsr.x : ssrTier == 1 ? (int)_PunchyWaterSsr.y : ssrTier == 2 ? (int)_PunchyWaterSsr.z : 0;
                half ssrReach = saturate((pixelsPerMetre - tiers.w) / (0.5 * tiers.w));
                if (frontFace && ssrSteps > 0 && _PunchyWaterNoSsr < 0.5 && _ScreenReflection > 0 && lava < 0.99 && fresnel * _ScreenReflection * ssrReach > SsrMinWeight)
                {
                    float ssrGrowth = ssrTier == 0 ? _PunchyWaterSsrGrowth.x : ssrTier == 1 ? _PunchyWaterSsrGrowth.y : _PunchyWaterSsrGrowth.z;
                    half4 scene = ScreenReflection(input.positionWS + float3(0, 0.02, 0), r, ssrSteps, ssrGrowth, (int)_PunchyWaterSsr.w);
                    reflection = lerp(reflection, scene.rgb, scene.a * _ScreenReflection * ssrReach);
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
                // Shore contact by kind (Zach, 11-08): the sea washes up the beach and draws back in long, slow surges;
                // a river's edge foam is carried downstream with the current; a lake lies nearly still against its shore,
                // with only a thin, faint line that barely moves.
                half seaShore = input.sea;
                half riverShore = saturate(flowAmount * 4) * (1 - seaShore);
                half lakeShore = (1 - seaShore) * (1 - riverShore);
                float2 carried = p - flowDir * flowAmount * riverSpeed * t * riverShore;
                float2 still = float2(t * 0.25, -t * 0.18) * (1 - riverShore) * lerp(1, 0.25, lakeShore);
                half foamNoise = PunchyValueNoise(carried * 0.9 + still) * 0.6 + PunchyValueNoise(carried * 2.7 - t * 0.4 * (1 - riverShore) * lerp(1, 0.25, lakeShore)) * 0.4;
                // The line laps: it creeps up the shore and draws back, unevenly along it. A river's laps as before; a lake's
                // hardly moves; the sea's is the surf, below.
                half lap = seaShore
                         + riverShore * (0.75 + 0.25 * sin(t * 1.4 + foamNoise * 5))
                         + lakeShore * (0.5 + 0.06 * sin(t * 0.4 + foamNoise * 3));
                // Where the water itself is deep, thin water in front of the bed can only be something standing in it (a
                // swimmer, a rock): a much narrower, fainter line there.
                half inDeep = smoothstep(0.5, 1.3, depth);
                half foam = smoothstep(foamNoise * 0.7, 1, saturate(1 - thickness / (_FoamDistance * lap * (1 + rapids) * lerp(1, 0.3, inDeep))));
                foam *= lerp(1, 0.55, inDeep);
                // A lake's line is faint.
                foam *= lerp(1, 0.45, lakeShore);
                // The sea's surf (playtest 11-08 items 2 and 5: too soft, and it ran against the waves): a bore runs in to
                // the waterline once per swell, timed by the swell arriving where it is (the cycle of each set's two longest
                // waves), so it moves the way the waves do. A crisp front, churned foam trailing behind it, fading as it
                // reaches the sand, and a faint wash shoreward of it.
                half surgeCycle = 0;
                [branch] if (seaShore > 0 && thickness < _FoamDistance * 2)
                {
                    PunchySwellResult arriving = PunchySwell(p, t, 1, 10, 2);   // (only its phase is used: deep, so not held down)
                    // (theta falls with time: this rises 0..1 over each swell, from 0 as a crest passes.)
                    surgeCycle = frac(0.25 - atan2(arriving.phasor.x, arriving.phasor.y) / 6.2831853);
                }
                float surgeReach = _FoamDistance * lerp(1.5, 0.05, surgeCycle);
                float fromFront = thickness - surgeReach;
                half surfLine = (fromFront < 0 ? 1 - smoothstep(0, 0.03 + 0.03 * _FoamDistance, -fromFront)
                                               : exp(-fromFront / (0.12 + 0.25 * _FoamDistance)))
                              * smoothstep(0, 0.06, surgeCycle) * (1 - smoothstep(0.75, 1, surgeCycle));
                half wash = (1 - saturate(thickness / max(surgeReach, 1e-3))) * 0.15;
                half seaFoam = saturate(max(surfLine * smoothstep(0.3, 0.45, foamNoise + 0.2), wash)) * (1 - inDeep);
                foam = lerp(foam, seaFoam, seaShore);
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
                foam = max(foam, swellFoam);
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

                // Lava (playtest 11-08 item 21: the cellular plates read as cartoon lava): flow noise churning slowly,
                // thin bright-hot veins glowing through a dark crust (LavaFlow), the colour going as the inverse of the
                // crust, so the thinnest crust burns brightest. It glows (past white-point, for any bloom, about as bright
                // as the old melt) and nothing on it reflects or ripples. Fewer octaves as it shrinks on screen (the water's
                // quality policy). The crust closes up where it cools against the rock at the edges.
                if (lava > 0)
                {
                    // (World position wrapped every km, so the noise stays precise far out; carried by any flow. The
                    // clock wraps too, every ~80 minutes, a jump nobody stands watching for.)
                    float2 home = p - floor(p / 1024) * 1024 - flowDir * flowAmount * frac(t / 4096) * 4096 * 0.12;
                    float lavaTime = frac(t * 0.1 / 500) * 500;
                    int octaves = pixelsPerMetre > _PunchyWaterTiers.y ? 6 : pixelsPerMetre > _PunchyWaterTiers.z ? 5 : 4;
                    float crust = LavaFlow(home * 0.45, lavaTime, octaves);
                    crust += 1.5 * (1 - saturate(thickness / 0.2));
                    // (A little darker and harder than the plain inverse, so most of a pool is crust and only the veins burn.)
                    half3 lavaColor = pow(half3(0.2, 0.07, 0.01) / max(crust * 1.35, 0.05), 1.7);
                    // (Held to the old melt's brightest, so the glow stays in the lava light's budget.)
                    lavaColor = min(lavaColor, half3(2.3, 1.2, 0.5));
                    color = lerp(color, lavaColor, lava);
                    alpha = lerp(alpha, 1, lava);
                }

                alpha *= rim;

                if (_PunchyWaterProbe > 0.5)
                    return _PunchyWaterProbe < 1.5 ? half4(foam, foam, foam, 1) : _PunchyWaterProbe > 3.5 ? half4(foamParts * 2, 1) : _PunchyWaterProbe < 2.5 ? half4(saturate(thickness), frac(thickness * 4), 0, 1) : half4(alpha, alpha, alpha, 1);
                // The atmosphere (fog and weather) only above ground; a cave pool is right in front of you.
                if (_PunchyUnderground < 0.999)
                    color = lerp(PunchyAtmosphere(color, input.positionWS), color, _PunchyUnderground);
                // What lies under the water is seen bent by the ripples (Zach, 11-08): the bed and stones shimmer and, on a
                // river, slide with the current. Read from the screen behind, nudged by the ripple normal, more through
                // thicker water and near the camera, none at the water's edge (where alpha goes to 0 anyway); the nudge is
                // dropped where it would land on something in front of the water. The water is then composited here.
                // (Refactor 3.4: in the near and mid tiers only, fading out from mid to far.)
                half refractionTier = saturate((pixelsPerMetre - _PunchyWaterTiers.w) / max(_PunchyWaterTiers.z - _PunchyWaterTiers.w, 1e-3));
                [branch] if (frontFace && _PunchyRefraction > 0 && alpha < 0.995 && distanceFade * refractionTier > 0)
                {
                    float2 bend = n.xz * _PunchyRefraction * saturate(thickness * 0.8) * distanceFade * refractionTier * (1 + flowAmount);
                    float2 bentUV = saturate(screenUV + bend);
                    float bentEye = LinearEyeDepth(SampleSceneDepth(bentUV), _ZBufferParams);
                    bentUV = bentEye > surfaceEye ? bentUV : screenUV;
                    half3 behind = SAMPLE_TEXTURE2D_X_LOD(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, UnityStereoTransformScreenSpaceTex(bentUV), 0).rgb;
                    color = lerp(behind, color, alpha);
                    alpha = 1;
                }
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
