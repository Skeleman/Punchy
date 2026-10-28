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
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

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
            CBUFFER_END
            // 0 above ground, 1 with the camera deep in a cave (CaveSystem).
            float _PunchyUnderground;
            // The player's lantern (xyz position, w strength) and its colour (rgb) and range (w), set by CaveSystem.
            float4 _PunchyLantern;
            float4 _PunchyLanternColor;
            // The fine detail texture shared with the caves (CaveSystem makes it once): rg a tiling normal, b a height,
            // a a sparkle. Two scrolling reads of it give the water its small ripples and glitter.
            TEXTURE2D(_PunchyCaveDetail);
            // 1 once CaveSystem has made the detail texture; until then (or without it) the fine ripples stay off.
            float _PunchyCaveDetailReady;
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

            // Rivers: the wave pattern is carried downstream. Two copies offset by half a cycle cross-fade so the
            // pattern never stretches indefinitely (the standard flow-map technique).
            float2 FlowingSlope(float2 p, float2 flow, float speed, float t)
            {
                const float cycle = 3.0;
                float phase0 = frac(t / cycle);
                float phase1 = frac(t / cycle + 0.5);
                float w0 = 1 - abs(1 - 2 * phase0);
                float2 offset = flow * speed * cycle;
                return WaveSlope(p - offset * phase0, t * 0.3) * w0 +
                       WaveSlope(p - offset * phase1 + 7.3, t * 0.3) * (1 - w0);
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
                float phase0 = frac(t / cycle);
                float phase1 = frac(t / cycle + 0.5);
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
                float stepLength = 0.3;
                float3 previous = origin;
                [loop] for (int i = 0; i < 28; i++)
                {
                    float3 pos = previous + dir * stepLength;
                    float4 clip = TransformWorldToHClip(pos);
                    if (clip.w <= 0.01)
                        return 0;
                    float4 screen = ComputeScreenPos(clip);
                    float2 uv = screen.xy / screen.w;
                    if (any(uv < 0) || any(uv > 1))
                        return 0;
                    float gap = clip.w - LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                    if (gap > 0 && gap < stepLength * 1.5 + 0.4)
                    {
                        // Narrow it down between the last point in front and this one behind.
                        float3 a = previous, b = pos;
                        [unroll] for (int k = 0; k < 5; k++)
                        {
                            float3 m = (a + b) * 0.5;
                            float4 mc = TransformWorldToHClip(m);
                            float4 ms = ComputeScreenPos(mc);
                            if (mc.w - LinearEyeDepth(SampleSceneDepth(ms.xy / ms.w), _ZBufferParams) > 0) b = m; else a = m;
                        }
                        float4 hs = ComputeScreenPos(TransformWorldToHClip(b));
                        uv = hs.xy / hs.w;
                        float2 edge = min(uv, 1 - uv);
                        half fade = smoothstep(0, 0.06, min(edge.x, edge.y)) * (1 - smoothstep(20, 28, i));
                        return half4(SAMPLE_TEXTURE2D_X_LOD(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, UnityStereoTransformScreenSpaceTex(uv), 0).rgb, fade);
                    }
                    previous = pos;
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
                // Very murky water is thick, like oil or sludge.
                float viscous = smoothstep(0.55, 0.9, murk) * (1 - lava);
                float distanceFade = saturate(1 - distance(input.positionWS, GetCameraPositionWS()) / 350);

                // Rivers run faster in the deep middle than at the banks, and much faster down steep rapids.
                float riverSpeed = _FlowSpeed * (1 + 2.5 * rapids) * lerp(0.45, 1.25, saturate(depth / 1.6));
                float2 flowingSlope = FlowingSlope(p, flowDir, riverSpeed, t) * (1 + 0.7 * rapids);
                float streaks = FlowStreaks(p, flowDir, riverSpeed, t);
                float2 slope = lerp(WaveSlope(p, t), flowingSlope, flowAmount);
                // Rain rings only where it's actually raining (from the world weather map).
                slope += RainRipples(p, t) * PunchyWetnessAt(p).g * distanceFade * 0.8;
                // Murky water barely ripples; thick water instead heaves in slow, broad swells and bubbles.
                slope *= 1 - 0.75 * murk;
                float3 bubbles = Bubbles(p, t) * distanceFade;
                slope += lerp(RainRipples(p * 0.45 + 13.7, t * 0.25) * murk * distanceFade * 0.6,
                              WaveSlope(p * 0.3, t * 0.12) * 0.5 + bubbles.xy, viscous);
                float strength = _WaveStrength * (0.5 + _PunchyWind) * (0.25 + 0.75 * distanceFade);
                // Streaks along a river are bands of rougher and smoother water.
                strength *= lerp(1, 0.55 + 0.9 * streaks, flowAmount);
                // Fine ripples: two reads of the shared detail texture drifting different ways (and downstream on a
                // river), stronger in wind and white water, fading with distance; on the shading normal only, so
                // reflections stay crisp. Their sparkle channels line up here and there for glitter in the sun.
                float2 drift = flowDir * flowAmount * riverSpeed * t;
                half4 fine0 = SAMPLE_TEXTURE2D(_PunchyCaveDetail, sampler_PunchyCaveDetail, p * _DetailScale + float2(t * 0.031, t * 0.017) - drift * _DetailScale * 0.6);
                half4 fine1 = SAMPLE_TEXTURE2D(_PunchyCaveDetail, sampler_PunchyCaveDetail, p * _DetailScale * 1.7 + float2(-t * 0.022, t * 0.029) - drift * _DetailScale * 1.1);
                float2 fine = fine0.rg + fine1.rg - 1;
                float fineStrength = _DetailStrength * _PunchyCaveDetailReady * (0.35 + _PunchyWind + rapids) * distanceFade * (1 - 0.8 * murk) * (1 - lava);
                float3 n = normalize(float3(-slope.x * strength - fine.x * fineStrength, 1, -slope.y * strength - fine.y * fineStrength));
                // Reflections use a calmer normal for the broad waves, so they don't break into noise, but the fine
                // ripples do move them: mirrored trees and clouds wobble and break up near the camera. Faded out with
                // distance (and so the screen-space march's ray, the sky lookup and the Fresnel stay steady far off).
                float rippleNear = 1 - smoothstep(25, 110, distance(input.positionWS, GetCameraPositionWS()));
                float fineReflect = fineStrength * 0.7 * rippleNear;
                float3 nReflect = normalize(float3(-slope.x * strength * 0.4 - fine.x * fineReflect, 1, -slope.y * strength * 0.4 - fine.y * fineReflect));
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
                half3 ambient = SampleSH(n);
                // Underground the sun and sky don't reach (cave rock deep down casts no sun shadow, so it's done here).
                shadow *= 1 - _PunchyUnderground;
                ambient *= lerp(1, 0.06, _PunchyUnderground);

                // Glowing (toxic) water swirls between its colour and a shifted hue in slow-moving patches.
                half swirl = smoothstep(0.35, 0.65, PunchyValueNoise(p * 0.06 + float2(t * 0.01, -t * 0.013)) * 0.7 + PunchyValueNoise(p * 0.21) * 0.3);
                input.look.rgb = lerp(input.look.rgb, input.look.rbg, swirl * saturate(glow * 3) * (1 - lava));
                half3 shallowColor = lerp(_ShallowColor.rgb, input.look.rgb, saturate(murk * 1.5));
                half3 deepColor = lerp(_DeepColor.rgb, input.look.rgb * 0.2, saturate(murk * 1.5));
                half3 waterColor = lerp(deepColor, shallowColor, exp(-thickness * _Absorption * (1 + murk * 8)));
                // River streaks: lighter and darker bands drifting downstream.
                waterColor *= lerp(1, 0.88 + 0.24 * streaks, flowAmount * (1 - murk));
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
                half caustic = smoothstep(0.55, 0.85, fine0.b * 0.5 + fine1.b * 0.5) * shallow;
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
                half3 reflection = PunchySkyColor(r, false, true) * lerp(1, 0.04, _PunchyUnderground);
                // ...and the scene where the reflected ray finds it (the far bank, trees, rocks, creatures).
                if (frontFace && _ScreenReflection > 0 && lava < 0.99)
                {
                    half4 scene = ScreenReflection(input.positionWS + float3(0, 0.02, 0), r);
                    reflection = lerp(reflection, scene.rgb, scene.a * _ScreenReflection);
                }
                float3 h = normalize(light.direction + v);
                // Thick water has a broad, soft sheen rather than a sharp glint.
                half glint = pow(saturate(dot(n, h)), exp2(lerp(10 * _Smoothness + 1, 6, viscous))) * shadow * (1 - 0.6 * viscous)
                             * (1 - _PunchyUnderground);

                fresnel *= 1 - 0.5 * murk * (1 - viscous);
                fresnel *= 1 - 0.6 * caveRun;
                half3 color = lerp(body, reflection, fresnel) + light.color * glint * 4 * (1 - 0.6 * murk * (1 - viscous));
                color += _PunchyLanternColor.rgb * lanternGlint * 0.8;
                // Glitter: where both fine reads sparkle at once and the ripple catches the sun just so.
                half glitter = smoothstep(0.55, 0.9, fine0.a * fine1.a * 1.6) * _PunchyCaveDetailReady * pow(saturate(dot(n, h)), 300) * shadow
                               * _Sparkle * distanceFade * (1 - murk) * (1 - lava);
                color += light.color * glitter * 10;
                // Toxic pools glow from within, pulsing slowly in patches.
                color += input.look.rgb * glow * (1 - lava) * (0.7 + 0.3 * sin(t * 0.6 + PunchyValueNoise(p * 0.08) * 6.28)) * 1.5;
                half alpha = saturate(1 - exp(-thickness * _Opacity * (1 + murk * 12)) + fresnel + glint + glitter + lanternGlint);

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
                // White water: the churned (steepest) parts of the flowing ripples turn to foam on rapids, and flecks
                // ride the streaks downstream on any river.
                foam = max(foam, rapids * smoothstep(0.25, 0.6, length(flowingSlope)));
                foam = max(foam, flowAmount * smoothstep(0.9 - 0.06 * rapids, 0.98, streaks) * (0.2 + 0.4 * rapids) * distanceFade * (1 - viscous));
                half3 foamLit = _FoamColor.rgb * (ambient + light.color * shadow * saturate(light.direction.y) + lanternLit);
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

                // The atmosphere (fog and weather) only above ground; a cave pool is right in front of you.
                if (_PunchyUnderground < 0.999)
                    color = lerp(PunchyAtmosphere(color, input.positionWS), color, _PunchyUnderground);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
