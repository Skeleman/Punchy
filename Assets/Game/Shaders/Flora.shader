Shader "Punchy/Flora"
{
    // Procedural plants (FloraSystem): vertex-coloured, wind-blown, with soft wrapped lighting and light shining
    // through leaves and petals, a waxy sheen on cacti, snow on upward faces where the ground is snowy, wetness,
    // cloud shadows and the world atmosphere. Double-sided, since leaves and petals are single triangles.
    Properties
    {
        // Not a real setting: a material property outside UnityPerMaterial keeps these draws off the SRP Batcher,
        // which doesn't pass the per-draw unity_BaseCommandID that Flora.hlsl reads its draw's region by.
        [HideInInspector] _FloraNoBatcher("", Float) = 0
        // 1 on CaveSystem's copy: cave plants are lit as underground wherever the camera is (see Frag).
        [HideInInspector] _FloraInCave("", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Cull Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma multi_compile_local_vertex _ FLORA_STATIC   // refactor 3.5: models that never move skip the wind
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Sky.hlsl"
            #include "Flora.hlsl"
            #include "Grounding.hlsl"
            #include "Metal.hlsl"
            #include "Glow.hlsl"
            #include "SharedClock.hlsl"

            /// How much of a detail octave of this period (m) to keep, given how many metres one pixel covers: all of it
            /// while a period spans eight pixels or more, none once it's down to three (where it would shimmer).
            float _PunchyFloraDetailOff;

            half PunchyOctave(float period, float footprint)
            {
                return smoothstep(3, 8, period / max(footprint, 1e-4));
            }

            // W-112: the colour the Ember Forest's embers glow in, its night's fire colour (FireSystem.EmberTint, set by
            // LandmarkEffectSystem); the wood-fire ember until it has been set (w 0).
            float4 _PunchyEmberTint;
            half3 EmberTint()
            {
                return _PunchyEmberTint.w > 0.5 ? (half3)_PunchyEmberTint.rgb : half3(1.0, 0.36, 0.08);
            }

            /// Weathering on built things that holds up at any range: the procedural equivalent of mipmapping. Courses
            /// of blocks (stone rows, planks, thatch layers: 0.3 m rows of 0.6 m blocks, and 1.2 m bands that outlast
            /// them), mottling at 0.7, 2.5 and 9 m, and rain streaks down the walls. Each octave drops out as it gets
            /// too fine for the pixels (PunchyOctave) and those left are scaled up to the same contrast, so a far wall
            /// keeps a few octaves of variation instead of fading to one flat colour. Returns an albedo multiplier.
            /// Wood grain (Z-30): fine and broad streaks running along the piece (its grain axis, in the plant's own frame,
            /// so they turn with it), wavering a little, darker in the lines; each scale drops out as it gets too fine for
            /// the pixels, as the weathering's octaves do. Returns an albedo multiplier.
            half PunchyWoodGrain(float3 p, half axisCode, float footprint)
            {
                int axis = (int)round(axisCode * 4);
                float along = axis == 1 ? p.x : axis == 2 ? p.y : p.z;
                float2 across = axis == 1 ? p.yz : axis == 2 ? p.xz : p.xy;
                float sideways = across.x + across.y;
                // Lines about 3 cm apart running along the piece, bent by a slow waver and a quicker one so they read
                // as long wavy streaks, some fainter than others; broad lighter and darker bands under them. (The first
                // version's streaks, at most 18% darker and mostly faded out by the pixel footprint, didn't show.)
                float wobble = (PunchyValueNoise(float2(along * 0.6, sideways * 1.5)) - 0.5) * 2.0
                             + (PunchyValueNoise(float2(along * 2.3, sideways * 5.0 + 7.1)) - 0.5) * 0.5;
                float lines = sideways * 33 + wobble * 2;
                half streak = smoothstep(0.55, 0.95, abs(frac(lines) * 2 - 1));
                streak *= 0.55 + 0.45 * PunchyValueNoise(float2(lines * 0.5, along * 0.8));
                half broad = PunchyValueNoise(float2(sideways * 8 + wobble, along * 0.35)) - 0.5;
                half grain = streak * 0.45 * PunchyOctave(0.03, footprint) + broad * 0.35 * PunchyOctave(0.12, footprint);
                return 1 - grain;
            }

            /// W-99: a sawn log end (look 5; the rings are in its vertex colours): fine end-grain speckle and saw marks only.
            half PunchyLogEnd(float3 p, float footprint)
            {
                half fineFade = 1 - smoothstep(0.004, 0.012, footprint);
                half speckle = PunchyValueNoise(p.yz * 160) * 0.6 + PunchyValueNoise(p.yz * 61 + 3.7) * 0.4;
                half saw = PunchyValueNoise(float2(p.y * 7 + p.z * 2, 1.3) * 9);
                return 1 + ((speckle - 0.5) * 0.22 + (saw - 0.5) * 0.08) * fineFade;
            }

            /// W-99: a built surface's look (FloraMeshBuilder.Surface): 1 planks, 2 shingles, 3 thatch, 4 a shingle course
            /// laid as geometry (only its tiles here; key: the course's own colour, which staggers its joints). Worked out in
            /// the plant's own frame: rows climb the face (on a slope measured along it, so a course keeps its size on any
            /// pitch) and "across" runs level along it. Each scale drops out as it gets too fine for the pixels.
            half PunchySurface(int look, float3 p, float3 nOS, float footprint, half3 key)
            {
                float slope = sqrt(saturate(1 - nOS.y * nOS.y));
                float across = slope < 0.3 ? p.x : dot(p.xz, normalize(float2(-nOS.z, nOS.x) + float2(1e-4, 0)));
                float row = slope < 0.3 ? p.z : p.y / max(slope, 0.35);
                if (look == 1)
                {
                    // Lapped boards about 21 cm high, each its own length and shade, a shadow under each lap, butt joints.
                    float r = row / 0.21;
                    int board = (int)floor(r);
                    half fr = frac(r);
                    half h = PunchyCellHash(int2(board, 31));
                    float len = 2.2 + h * 1.6;
                    float a = across / len + h * 7.3;
                    half piece = PunchyCellHash(int2(board, (int)floor(a) + 977));
                    half joint = 1 - smoothstep(0.004, 0.014, min(frac(a), 1 - frac(a)) * len);
                    half lap = smoothstep(0.78, 1.0, fr) * 0.4 + (1 - smoothstep(0.0, 0.05, fr)) * 0.2;
                    half o = PunchyOctave(0.21, footprint);
                    half grain = PunchyWoodGrain(float3(across, row + board * 0.37, 0), 0.25, footprint);
                    return grain * lerp(1, lerp(0.84, 1.12, piece) * (1 - lap - joint * 0.4), o);
                }
                if (look == 2 || look == 4)
                {
                    // Shingles about 30 cm wide, joints staggered course to course, a shadow under the course above
                    // (geometric courses cast their own), and a fine grain down each.
                    float r = row / 0.24;
                    half fr = frac(r);
                    float course = look == 4 ? floor(frac(dot(key, half3(97.31, 61.17, 13.73))) * 64) : floor(r);
                    float t = across / 0.3 + frac(course * 0.618);
                    half ft = frac(t);
                    half h = PunchyCellHash(int2((int)course, (int)floor(t)));
                    half joint = 1 - smoothstep(0.02, 0.07, min(ft, 1 - ft));
                    half under = look == 4 ? 0 : smoothstep(0.7, 1.0, fr) * 0.4;
                    half lip = look == 4 ? 0 : (1 - smoothstep(0.0, 0.08, fr)) * 0.12;
                    half fine = (PunchyValueNoise(float2(t * 9, row * 1.5 + h * 10)) - 0.5) * 0.3 * PunchyOctave(0.03, footprint);
                    half m = lerp(0.8, 1.14, h) * (1 - under - joint * 0.45 * (1 - under)) * (1 + lip);
                    return lerp(1, m, PunchyOctave(0.24, footprint)) * (1 + fine);
                }
                // Thatch: straw strands down the slope in ragged layers, clumps lighter and darker, shadow under each layer.
                float rr = row / 0.42 + (PunchyValueNoise(float2(across * 1.3, 3.7)) - 0.5) * 0.4;
                half strand = PunchyValueNoise(float2(across * 38, row * 1.2)) * 0.6 + PunchyValueNoise(float2(across * 90 + 3.1, row * 2.5)) * 0.4;
                half clump = PunchyValueNoise(float2(across * 4.0, row * 1.5 + 11.1));
                half layer = smoothstep(0.62, 1.0, frac(rr)) * 0.32 * PunchyOctave(0.42, footprint);
                return (1 + (strand - 0.5) * 0.6 * PunchyOctave(0.02, footprint) + (clump - 0.5) * 0.3 * PunchyOctave(0.25, footprint)) * (1 - layer);
            }

            /// W-99: cloth's linen weave, on the facet's plane in the plant's own frame: 3 cm threads over and under in a
            /// check, a little uneven along each thread (slubs), and broad soft blotches; fading out with the pixels.
            half PunchyLinen(float3 p, float3 nOS, float footprint)
            {
                float3 a = abs(nOS);
                float2 uv = a.x > a.y && a.x > a.z ? p.zy : a.y > a.z ? p.xz : p.xy;
                float2 t = uv / 0.03;
                float2 f = frac(t);
                int2 c = (int2)floor(t);
                bool warp = ((c.x + c.y) & 1) == 0;
                half thread = warp ? sin(f.x * 3.14159) : sin(f.y * 3.14159);
                half slub = PunchyValueNoise(warp ? float2(c.x * 1.7, t.y * 0.15) : float2(t.x * 0.15, c.y * 1.7 + 9.3));
                half fine = (thread * 0.7 + slub * 0.3 - 0.55) * 0.6 * PunchyOctave(0.03, footprint);
                // Sewn panels: a dark seam every ~55 cm with a slight tone change per panel, so it reads at a few metres.
                float pv = uv.y / 0.55;
                half seam = (1 - smoothstep(0.0, 0.035, abs(frac(pv) - 0.5) - 0.465)) * 0.22;
                half panel = (frac(sin(floor(pv) * 12.9898) * 43758.55) - 0.5) * 0.12;
                half mid = (panel - seam) * PunchyOctave(0.12, footprint);
                half coarse = (PunchyValueNoise(uv * 3.1) - 0.5) * 0.22 * PunchyOctave(0.3, footprint);
                return 1 + fine + mid + coarse;
            }

            /// W-113: a tent's hide, as leather, not linen: blotchy tanning, fine creases and pores, so it reads at a few
            /// metres. Playtest 11-07: the seams and stitches it had, on a square lattice about 80 cm apart, read as a grid
            /// up close, not leather; the panels' own edges between the poles are the seams now, and each hide's tone
            /// drifts softly over a metre or two instead of changing square by square.
            half PunchyHide(float3 p, float3 nOS, float footprint)
            {
                float3 a = abs(nOS);
                float2 uv = a.x > a.y && a.x > a.z ? p.zy : a.y > a.z ? p.xz : p.xy;
                half blotch = (PunchyValueNoise(uv * 1.4) - 0.5) * 0.34 + (PunchyValueNoise(uv * 4.3 + 5.1) - 0.5) * 0.16;
                half crease = (PunchyValueNoise(uv * float2(9, 26)) - 0.5) * 0.12 + (PunchyValueNoise(uv * 55) - 0.5) * 0.08;
                // Tanned unevenly: a slow drift in tone across the hide, and darker worn patches here and there.
                half hides = (PunchyValueNoise(uv * 0.55 + 11.3) - 0.5) * 0.16;
                half worn = smoothstep(0.62, 0.8, PunchyValueNoise(uv * 2.2 + 31.7)) * 0.1;
                return 1 + blotch * PunchyOctave(0.4, footprint) + crease * PunchyOctave(0.03, footprint)
                         + (hides - worn) * PunchyOctave(0.1, footprint);
            }

            half3 PunchyWeathering(float3 p, half3 n, float footprint, half r)
            {
                p += r * 37;
                float3 q;
                half w0 = PunchyOctave(0.7, footprint), w1 = PunchyOctave(2.5, footprint), w2 = PunchyOctave(9, footprint);
                q = p / 0.7; half m0 = PunchyValueNoise(q.xz + q.y * 0.7) * 0.6 + PunchyValueNoise(q.zy * 1.3 + 5.1) * 0.4;
                q = p / 2.5; half m1 = PunchyValueNoise(q.xz + q.y * 0.7 + 11.3) * 0.6 + PunchyValueNoise(q.zy * 1.3 + 2.7) * 0.4;
                q = p / 9.0; half m2 = PunchyValueNoise(q.xz + q.y * 0.7 + 23.9) * 0.6 + PunchyValueNoise(q.zy * 1.3 + 8.2) * 0.4;
                // Along the surface, across the slope (walls and roofs alike).
                float across = dot(p.xz, normalize(float2(-n.z, n.x) + 1e-4));
                half wb = PunchyOctave(0.3, footprint), wc = PunchyOctave(1.2, footprint);
                int row = (int)floor(p.y / 0.3);
                half block = PunchyCellHash(int2(row, (int)floor(across / 0.6 + (row & 1) * 0.5)));
                half band = PunchyCellHash(int2((int)floor(p.y / 1.2), 7919));
                half sum = w0 * (m0 - 0.5) + w1 * (m1 - 0.5) * 1.2 + w2 * (m2 - 0.5) * 1.4 + wb * (block - 0.5) * 0.9 + wc * (band - 0.5) * 0.8;
                half weight = w0 + w1 * 1.2 + w2 * 1.4 + wb * 0.9 + wc * 0.8;
                half m = sum / max(weight, 0.5) * 2.2;   // about -0.5..0.5 whatever survives
                // Warmer and lighter one way, cooler and darker the other.
                half3 tint = lerp(half3(0.82, 0.85, 0.9), half3(1.14, 1.09, 0.99), saturate(m + 0.5));
                // Streaks down the walls (not roofs): narrow across, long down, darker below sills and eaves.
                half wall = 1 - smoothstep(0.35, 0.7, abs(n.y));
                half ws = PunchyOctave(0.45, footprint);
                half streak = smoothstep(0.55, 0.85, PunchyValueNoise(float2(across / 0.45, p.y / 3.5))) * lerp(0.5, 1, ws);
                return tint * (1 - streak * wall * 0.22);
            }

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
                half4  color      : TEXCOORD2;   // rgb albedo, a ambient occlusion
                float4 extra      : TEXCOORD3;   // x part, y fade, z snow, w random
                // A window pane's own lighting-up hour (0..1), worked out per vertex and not interpolated, so every pixel
                // of the pane agrees (hashed per pixel from the interpolated colour, single pixels flickered on and off).
                nointerpolation float windowHour : TEXCOORD4;
                // The ground colour for a built thing's dirt band (a 0: none) and the instance's scale; the height above
                // its base.
                nointerpolation half4 ground : TEXCOORD5;
                float2 baseHeight : TEXCOORD6;   // height above the base, instance scale
                // Wood's grain (Z-30): the point in the plant's own frame, and which of its axes the grain runs along
                // (FloraMeshBuilder.Wood: the vertex colour's alpha on wood, 0.25 X, 0.5 Y, 0.75 Z).
                float3 grainPos : TEXCOORD7;
                nointerpolation half grainAxis : TEXCOORD8;
                float4 moss : TEXCOORD9;   // FloraMossData: cover (2: not moss), point in the plant's frame
            };

            Varyings Vert(FloraAttributes input)
            {
                FloraVertex v = FloraTransform(input);
                Varyings o;
                o.positionWS = v.positionWS;
                o.positionCS = TransformWorldToHClip(v.positionWS);
                o.normalWS = v.normalWS;
                // (Darker towards the ground: worked out per pixel in Frag, from baseHeight.)
                o.color = half4(input.color.rgb, input.data.w);
                uint g = v.plant.ground;
                o.ground = half4(g & 255, (g >> 8) & 255, (g >> 16) & 255, g >> 24) / 255.0;
                o.baseHeight = float2(v.positionWS.y - v.plant.position.y, v.plant.scale);
                o.extra = float4(input.data.y, v.fade, v.plant.snow, v.plant.random);
                o.grainPos = input.positionOS;
                o.grainAxis = input.color.a;
                o.moss = FloraMossData(input);
                // Keyed on the pane (each has a slightly different colour) and the building.
                float3 key = round(input.color.rgb * 255);
                o.windowHour = frac(sin(dot(key, float3(0.917, 0.473, 0.131)) * 10 + v.plant.random * 61.7) * 43758.5453);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                FloraDither(input.positionCS, input.extra.y, input.positionWS);
                FloraMossClip(input.moss);
                int part = (int)round(input.extra.x);
                // A rock tint (ground alpha at its top value) or a built thing's dirt band (any other non-zero alpha).
                bool rockTint = input.ground.a > 0.998;
                // Item 13: a plant on grassy ground, its leaves leaning to the ground's green (FloraPlacement.GreenTint: alpha 254).
                bool greenTint = input.ground.a > 0.99 && !rockTint;
                bool built = input.ground.a > 0 && !rockTint && !greenTint;
                bool leafy = part == 1 || part == 2 || part == 5;
                bool metal = part == 4;
                // Thin tapered strands (roots, stems) can end in a degenerate normal; a NaN there blooms into a bright fleck.
                half3 n = SafeNormalize(input.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
                // The facet's own normal (outside any branch, so the derivatives are good).
                float3 facet = normalize(cross(ddy(input.positionWS), ddx(input.positionWS)) + 1e-9);
                // Built things are drawn double-sided too, and a roof is one sheet: seen from below it's the sheet's
                // back, whose normal still points up. Light the side you see. Only if the facet agrees that it's the back:
                // near a roof's silhouette the smoothed normal already leans away on faces that do face you, and flipping
                // those drew black shapes at the eaves when looking up (playtest 11-07, 11-08).
                if (part == 0 && built && dot(n, v) < 0 && dot(n, dot(facet, v) < 0 ? -facet : facet) < 0)
                    n = -n;

                // Per-plant tint: brightness, and leaves a little yellower or bluer.
                half r = input.extra.w;
                half3 albedo = input.color.rgb * (0.88 + 0.24 * r);

                // How many metres one pixel covers here, and the facet's own normal (outside any branch, so the
                // derivatives are good).
                float footprint = length(fwidth(input.positionWS));
                // Cloth: lit by the surface the wind has waved (its folds catch the light), facing the viewer.
                if (part == 10)
                    n = dot(facet, v) < 0 ? -facet : facet;
                // The facet in the plant's own frame (W-99's surfaces and weave are laid out there).
                float3 facetOS = normalize(cross(ddy(input.grainPos), ddx(input.grainPos)) + 1e-9);
                // Timber (marked Bark + 0.02) and its look (W-99: FloraMeshBuilder.Surface, riding on the grain code).
                bool wood = part == 0 && frac(input.extra.x) > 0.01 && frac(input.extra.x) < 0.035;
                int look = wood ? (int)round((input.grainAxis - round(input.grainAxis * 4) * 0.25) / 0.02) : 0;
                // Built things (those with a dirt band) and banded rock: weathering that stays at every range
                // (PunchyWeathering). Timber takes only a little of it: its stone-like courses read as plaster (W-99).
                bool strata = part == 0 && frac(input.extra.x) > 0.04 && frac(input.extra.x) < 0.15;
                // (Refactor 3.5, measuring a bake: "-nofloradetail" sets _PunchyFloraDetailOff and skips the procedural
                // surface detail below, weathering, wood, cloth and strata, so a profiler A/B shows the most a bake could save.)
                bool detail = _PunchyFloraDetailOff < 0.5;
                if (detail && (part == 0 && built || strata))
                    albedo *= lerp(1, PunchyWeathering(input.positionWS, n, footprint, r), wood ? 0.4 : 1);
                // Timber: grain along each piece (Z-30: wood read as leather or plaster), or its planks, shingles or thatch.
                if (detail && wood)
                    albedo *= look == 0 ? PunchyWoodGrain(input.grainPos, input.grainAxis, footprint)
                                        : look == 5 ? PunchyLogEnd(input.grainPos, footprint)
                                        : PunchySurface(look, input.grainPos, facetOS, footprint, input.color.rgb);
                // Cloth: a linen weave (W-99).
                // Hide (Cloth + 0.45, W-113): leather instead.
                if (detail && part == 10)
                    albedo *= input.extra.x > 10.42 ? PunchyHide(input.grainPos, facetOS, footprint)
                                                    : PunchyLinen(input.grainPos, facetOS, footprint);
                // Banded rock (FloraSpecies.RockStrata): exactly the terrain's strata (TerrainSurface.shader), same
                // world heights and colours, so a rock prop's layers line up with the cliff beside it.
                if (detail && strata)
                {
                    float2 sp = input.positionWS.xz;
                    float sy = input.positionWS.y + (PunchyValueNoise(sp * 0.02) - 0.5) * 6 + (PunchyValueNoise(sp * 0.11) - 0.5) * 1.2;
                    half band = PunchyValueNoise(float2(sy / 4.5, 7.1)) * 0.7 + PunchyValueNoise(float2(sy / 1.1, 3.3)) * 0.3;
                    albedo *= lerp(1, lerp(half3(0.55, 0.45, 0.42), half3(1.45, 1.3, 1.08), smoothstep(0.3, 0.7, band)), 0.9);
                }
                // Rock and bark up close: grain and fine relief from the shared detail texture (the caves' own), read
                // three ways and blended by the surface's facing, fading out with distance.
                // Built things keep it much further (W-23): faded by 45 m, a thatch roof turned into a smooth, plain cone
                // whose few facets showed, across the 30-55 m where huts also change LOD. Few of them, and the texture's
                // mips keep the far relief from shimmering.
                float detailFade = distance(input.positionWS, GetCameraPositionWS());
                float near = built ? 1 - smoothstep(60, 140, detailFade) : 1 - smoothstep(18, 45, detailFade);
                // Timber keeps only a trace of the rock's relief (W-99: it gave the fishing huts' boards a plaster's depth).
                near *= wood ? (look == 3 ? 0.35 : 0.15) : 1;
                if (part == 0 && near > 0.01 && _PunchyCaveDetailReady > 0)
                {
                    float3 q = input.positionWS * 0.9;
                    float3 w = pow(abs(n), 4);
                    w /= w.x + w.y + w.z;
                    half4 dx = SAMPLE_TEXTURE2D(_PunchyCaveDetail, sampler_PunchyCaveDetail, q.zy);
                    half4 dy = SAMPLE_TEXTURE2D(_PunchyCaveDetail, sampler_PunchyCaveDetail, q.xz);
                    half4 dz = SAMPLE_TEXTURE2D(_PunchyCaveDetail, sampler_PunchyCaveDetail, q.xy);
                    half4 detail = dx * w.x + dy * w.y + dz * w.z;
                    albedo *= 1 + (detail.b - 0.5) * 0.5 * near;
                    half2 bump = (detail.rg - 0.5) * 0.9 * near;
                    n = normalize(n + float3(0, bump.x, bump.y) * w.x + float3(bump.x, 0, bump.y) * w.y + float3(bump.x, bump.y, 0) * w.z);
                }
                if (part == 1)
                {
                    albedo *= lerp(half3(0.95, 1.0, 1.06), half3(1.1, 1.04, 0.82), r);
                    // Leaf clusters: crowns break into lighter, yellower clumps and darker gaps between them (fading out
                    // with distance, where the crown reads as one mass anyway).
                    float3 q = (input.positionWS - frac(r * 7.3) * 10) * 2.2;
                    half leafNoise = PunchyValueNoise(q.xz + q.y * 0.7) * 0.6 + PunchyValueNoise(q.zy * 2.3 + 5.1) * 0.4;
                    half leafNear = 1 - smoothstep(40, 90, distance(input.positionWS, GetCameraPositionWS()));
                    albedo *= lerp(1, lerp(half3(0.62, 0.66, 0.7), half3(1.25, 1.22, 1.0), leafNoise), leafNear * 0.85);
                }
                // Moss (W-64): darker, greener clumps and yellower tips, and darker still in the thin cover near the rim.
                if (part == 13)
                {
                    half clump = FloraMossClump(input.moss.yzw);
                    half fine = FloraMossNoise(input.moss.yzw * 80);
                    albedo *= lerp(half3(0.42, 0.56, 0.36), half3(1.0, 0.98, 0.62), clump) * (0.8 + 0.4 * fine);
                    albedo *= lerp(0.75, 1, saturate(input.moss.x * 2));
                    // W-101 (#80): tiny cushions, each with a bright crown and a dark seam round it, so the sheet reads
                    // as a mat of tufts rather than a smooth green cloth.
                    half tuft = FloraMossNoise(input.moss.yzw * 26 + 9.7);
                    albedo *= lerp(0.5, 1.15, smoothstep(0.25, 0.75, tuft));
                }
                // Windows: dark glass by day (their colour is the light they give at night, below).
                half3 windowLight = albedo;
                if (part == 6)
                    albedo = half3(0.05, 0.06, 0.07) + albedo * 0.06;

                // Snow settles on upward faces where the ground under the plant is snowy.
                // W-99: cloth goes by its own facet turned up (n faces the viewer there, which from below would keep a
                // tent's roof bare), and other parts by the steeper of normal and facet so faceted stake tips catch it.
                // Only clearly upward faces fully whiten, the cover is capped and broken up so it sits with the
                // terrain snow nearby instead of glowing.
                half3 fA = dot(facet, n) < 0 ? -facet : facet;
                half up = part == 10 ? abs(facet.y) : max(n.y, fA.y);
                half breakup = PunchyValueNoise(input.positionWS.xz * 1.7) * 0.6 + PunchyValueNoise(input.positionWS.xz * 7.3) * 0.4;
                half snow = min(saturate(input.extra.z * 1.5) * smoothstep(0.3, 0.7, up) * lerp(0.6, 1.1, breakup), 0.7);
                albedo = lerp(albedo, half3(0.78, 0.8, 0.85), snow);

                half wet = PunchyWetnessAt(input.positionWS.xz).r * (1 - snow);
                // Rocks on dry ground take the biome's own rock colour (FloraPlacement.RockTint: the ground's alpha at its
                // top value), keeping their own light and dark (and losing their moss's green) (G-36). Built things: a
                // band of the ground's colour up their base, like dirt splashed up the walls.
                if (part == 0 && rockTint)
                {
                    half lum = dot(albedo, half3(0.3, 0.59, 0.11));
                    half rockLum = max(dot(input.ground.rgb, half3(0.3, 0.59, 0.11)), 0.02);
                    albedo = lerp(albedo, input.ground.rgb * clamp(lum / rockLum, 0.45, 1.6), 0.85);
                }
                else if (part == 0 && !greenTint)
                    albedo = PunchyDirt(albedo, input.ground, input.baseHeight.x, input.baseHeight.y, wet, input.positionWS);
                // Leaves take the ground's hue and keep their own light and dark; only what is clearly green moves, so
                // autumn, blossom and fungus colours stay.
                if (part == 1 && greenTint)
                {
                    half green = saturate((albedo.g - max(albedo.r, albedo.b)) / max(albedo.g, 0.02) * 4);
                    half lum = dot(albedo, half3(0.3, 0.59, 0.11));
                    half groundLum = max(dot(input.ground.rgb, half3(0.3, 0.59, 0.11)), 0.02);
                    albedo = lerp(albedo, input.ground.rgb * clamp(lum / groundLum, 0.2, 1.6), 0.6 * green);
                }
                albedo *= lerp(1, part == 0 ? 0.62 : 0.82, wet);

                Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half shadow = light.shadowAttenuation * (1 - PunchyCloudShadow(input.positionWS));
                // Underground (cave plants, seen from a cave) the sun and sky don't reach; the player's lantern does.
                // Cave plants are underground whatever the camera's depth: no direct sun (nor light through leaves, nor
                // sheen, which made roots flare against the sun from near a mouth), and only a little of the sky's
                // ambient (the daylight spilling in at a mouth).
                shadow *= (1 - _PunchyUnderground) * (1 - _FloraInCave);
                half3 sky = SampleSH(n) * lerp(1, 0.06, max(_PunchyUnderground, _FloraInCave * 0.75));
                // Roof undersides, eaves and overhangs on built things see the ground, not the sky (Grounding.hlsl).
                half under = part == 0 && built ? PunchyUnderside(n.y) : 0;
                if (under > 0)
                {
                    sky = PunchyUndersideAmbient(sky, input.ground.rgb, SampleSH(half3(0, 1, 0)) * lerp(1, 0.06, _PunchyUnderground), under);
                    albedo *= lerp(1, PunchyUndersideAlbedo, under);
                }
                // Glowing plants and molten pools nearby (their own glow included, softly).
                sky += PunchyGlowLight(input.positionWS, n);
                if (_PunchyLantern.w > 0)
                {
                    float3 toLantern = _PunchyLantern.xyz - input.positionWS;
                    float reach = saturate(1 - length(toLantern) / _PunchyLanternColor.w);
                    sky += _PunchyLanternColor.rgb * _PunchyLantern.w * reach * reach * saturate(dot(n, normalize(toLantern)) * 0.7 + 0.3);
                }
                half ndl = dot(n, light.direction);
                half diffuse = leafy ? saturate(ndl * 0.6 + 0.4) : saturate(ndl * 0.8 + 0.2);
                half through = leafy ? pow(saturate(dot(-v, light.direction)), 4) * 0.55 : 0;
                // Darker towards the ground and on downward faces (leaves and petals: towards the ground only), per pixel:
                // worked out at the vertices it smeared the tight band at the ground over a whole face, so a building's
                // foundation exposed on a slope (a metre or two of skirt) showed it stretched down its length (Z-12, G-22).
                half ao = input.color.a * PunchyGrounding(input.baseHeight.x, n.y, part == 1 || part == 2 || part == 5 ? 0 : 1);

                // Metal leaves: dim diffuse, the colour mostly comes from tinted reflections.
                half3 color = albedo * (light.color * shadow * (diffuse + through) * lerp(0.6, 1, ao) + sky * ao) * (metal ? 0.45 : 1);
                // Tinted reflections of the sky, a sharp sun highlight, and glints that twinkle as the leaves move and
                // the view changes (shared with creature gear: Metal.hlsl).
                if (metal)
                    color += PunchyMetalShade(albedo, n, v, input.positionWS, light.color, light.direction, shadow, ao, _PunchyUnderground);
                // Bark and rock take a faint reflection of the sky at grazing angles, as the terrain does (its
                // surfaces have a little smoothness), so rocks fade into the same sky-tinted haze as the land. Metal
                // and crystal too: dark ones (obsidian) otherwise stay black specks on a distant hazy slope. Up close
                // dry stone, wood and plaster stay matte (rain adds its own sheen below).
                if (part == 0 || metal)
                {
                    half fresnel = 0.04 + 0.96 * pow(1 - saturate(dot(n, v)), 5);
                    half sheenFar = metal ? 1 : lerp(0.25, 1, smoothstep(30, 120, distance(input.positionWS, GetCameraPositionWS())));
                    color += SampleSH(reflect(-v, n)) * fresnel * 0.35 * sheenFar * ao * lerp(1, 0.06, _PunchyUnderground) * lerp(1, PunchyUndersideSky, under);
                }

                // Glass (crystals): a dark clear body lit faintly from inside, a bright rim where the surface turns
                // away whose hue shifts (light bent through it), the sky reflected at grazing angles, and sharp
                // highlights from the sun and the lantern. Drawn solid: the look of glass without its cost.
                if (part == 7)
                {
                    half facingV = saturate(dot(n, v));
                    half rim = pow(1 - facingV, 4);
                    half3 bent = lerp(albedo, albedo.gbr * 0.7 + albedo * 0.5, 0.7);
                    // A deep, nearly black clear core where the face looks straight at you...
                    color = albedo * (light.color * shadow * diffuse * 0.08 + sky * 0.12) * ao;
                    // ...glowing from inside where the light travels through more of it (towards the edges)...
                    color += albedo * (0.03 + 0.35 * pow(1 - facingV, 3)) * lerp(1, 2.5, max(_PunchyUnderground, _FloraInCave));
                    // ...and a bright rim whose hue is bent, the sky caught in it at grazing angles.
                    color += lerp(bent, 1, 0.25) * rim * (SampleSH(reflect(-v, n)) * lerp(1, 0.15, _PunchyUnderground) + sky * 0.6 + 0.12) * 2.6;
                    half3 hSun = normalize(light.direction + v);
                    color += light.color * shadow * pow(saturate(dot(n, hSun)), 320) * 5;
                    if (_PunchyLantern.w > 0)
                    {
                        float3 toLantern = _PunchyLantern.xyz - input.positionWS;
                        float reachL = saturate(1 - length(toLantern) / _PunchyLanternColor.w);
                        half3 hL = normalize(normalize(toLantern) + v);
                        color += _PunchyLanternColor.rgb * _PunchyLantern.w * reachL * pow(saturate(dot(n, hL)), 200) * 4;
                    }
                }

                // Bark veined with cool light (FloraSpecies.GlowingVeins, marked as Bark + 0.4): the same crack network,
                // its light flowing slowly up the trunk.
                if (part == 0 && input.extra.x > 0.32)
                {
                    float3 q = input.positionWS * float3(1.6, 0.6, 1.6);
                    float vein = abs(PunchyValueNoise(q.xz + q.y * 1.3 + r * 9.0) - 0.5) + abs(PunchyValueNoise(q.zy * 1.1 + 2.3) - 0.5) * 0.5;
                    half veinLine = 1 - smoothstep(0.015, 0.05, vein);
                    half flow = 0.35 + 0.65 * pow(0.5 + 0.5 * sin(_PunchyClock * 0.8 - input.positionWS.y * 1.4 + r * 30), 3);
                    color += half3(0.35, 0.75, 1.0) * veinLine * flow * 1.8;
                }
                // Bark with glowing seams (FloraSpecies.GlowingSeams, marked as Bark + 0.25): a network of thin cracks
                // where value noise crosses its midline, pulsing slowly along the trunk, hot as embers under char.
                else if (part == 0 && input.extra.x > 0.15)
                {
                    float3 q = input.positionWS * float3(2.2, 0.9, 2.2);
                    float vein = abs(PunchyValueNoise(q.xz + q.y * 1.7 + r * 13.0) - 0.5) + abs(PunchyValueNoise(q.zy * 1.3 + 4.1) - 0.5) * 0.6;
                    half crack = 1 - smoothstep(0.02, 0.07, vein);
                    half pulse = 0.65 + 0.35 * sin(_PunchyClock * 1.3 - input.positionWS.y * 0.8 + r * 20);
                    color = color * (1 - crack * 0.6) + EmberTint() * crack * pulse * 2.4;
                }
                // Ember leaves and needles (the same species, marked as Foliage + 0.25): thin glowing slivers through the
                // crown, hot at their middle, pulsing slowly out of step (G-33/F-47).
                if (part == 1 && abs(frac(input.extra.x) - 0.25) < 0.04)   // only the exact ember mark, nothing else on a leaf
                {
                    // Along the grain (Z-39): the noise varies across the crown and only slowly with height, so where it
                    // crosses its midline are near-upright sheets, cutting the needles as upright slivers, broken into
                    // dashes a hand or two long by a mask along the height.
                    float3 q = input.positionWS;
                    float vein = abs(PunchyValueNoise(q.xz * 3.1 + q.y * 0.15 + r * 17.0) - 0.5) + abs(PunchyValueNoise(q.zx * 5.3 + q.y * 0.25 - 3.3) - 0.5) * 0.5;
                    half dash = smoothstep(0.35, 0.6, PunchyValueNoise(float2(q.y * 2.2, dot(q.xz, float2(1.7, 2.3)) + r * 7.0)));
                    half sliver = (1 - smoothstep(0.015, 0.05, vein)) * dash;
                    half patch = smoothstep(0.45, 0.7, PunchyValueNoise(input.positionWS.xz * 0.35 + r * 11.0));
                    half pulse = 0.6 + 0.4 * sin(_PunchyClock * 1.1 - input.positionWS.y * 0.6 + r * 23);
                    color += EmberTint() * half3(1.0, 1.1, 1.25) * sliver * patch * pulse * 1.8;
                }

                // A bark face's eyes (part 9): a faint glow in its colour that blinks shut for a moment every few seconds.
                // Electric arcs (part 11): blue-white, crackling on and off in bursts, bright enough to bloom.
                if (part == 11)
                {
                    float tick = floor(_PunchyClock * 13 + r * 50);
                    half on = frac(sin(tick * 12.9898 + r * 78.2) * 43758.5453) > 0.35 ? 1 : 0.12;
                    half burst = smoothstep(0.45, 0.85, 0.5 + 0.5 * sin(_PunchyClock * 0.9 + r * 20));
                    color = albedo * (0.4 + 3.6 * on * (0.3 + 0.7 * burst));
                }
                if (part == 9)
                {
                    float beat = frac(_PunchyClock * 0.17 + r * 5.3);
                    half open = beat > 0.97 ? 0 : 1;
                    color = albedo * (0.08 + 1.4 * open * lerp(0.6, 1.4, _PunchyNight));
                }

                // Glowing parts shine by themselves (bright enough to bloom at night), with a gentle, slow shimmer of
                // the species' own amount (carried as a fraction on the part; 0 for crystals), out of step plant to plant.
                if (part == 5)
                {
                    // A steady glow beyond 100 m, where the shimmer can't be seen.
                    float amount = saturate((input.extra.x - 5) / 0.4) * (1 - smoothstep(80, 100, distance(input.positionWS, GetCameraPositionWS())));
                    float pulse = 1 + amount * sin(_PunchyClock * 0.55 + r * 40 + input.positionWS.y * 0.7);
                    // Brighter in the dark of a cave (eyes adjusted to the dark see the glow stand out).
                    color += albedo * 1.6 * pulse * lerp(1, 2.5, _PunchyUnderground);
                }

                // Windows light up as night falls and go out one by one late at night (the deeper the sun, the later):
                // each window keeps its own hour, and a few stay lit all night.
                if (part == 6)
                {
                    // Its own hour (see Varyings.windowHour).
                    float h = input.windowHour;
                    float late = saturate(-_PunchySunDirection.y * 1.6);
                    float on = smoothstep(0.25, 0.55, _PunchyNight) * (h > 0.85 ? 1 : step(late, 0.3 + 0.7 * h));
                    color += windowLight * on * 2.2;
                }

                // A waxy sheen on cacti and egg shells (and on anything wet), from the sun and, near it, the lantern.
                // Honeycomb (part 8): faint hexagonal cells up close, and glossier than wax.
                if (part == 8)
                {
                    float2 q = float2(input.positionWS.x + input.positionWS.z, input.positionWS.y) * 28;
                    float2 hexA = fmod(abs(q), float2(1.732, 1)) - float2(0.866, 0.5);
                    float2 hexB = fmod(abs(q + float2(0.866, 0.5)), float2(1.732, 1)) - float2(0.866, 0.5);
                    float2 h2 = dot(hexA, hexA) < dot(hexB, hexB) ? hexA : hexB;
                    float rim = max(abs(h2.y), dot(abs(h2), float2(0.866, 0.5)));
                    half cellNear = 1 - smoothstep(4, 10, distance(input.positionWS, GetCameraPositionWS()));
                    color *= 1 - smoothstep(0.38, 0.47, rim) * 0.35 * cellNear;
                }
                half gloss = part == 3 ? 0.35 : part == 8 ? 0.55 : wet * 0.5;
                if (gloss > 0)
                {
                    half3 h = normalize(light.direction + v);
                    color += light.color * shadow * pow(saturate(dot(n, h)), part == 3 ? 24 : part == 8 ? 40 : 60) * gloss * 0.3 * ao;
                    if (_PunchyLantern.w > 0)
                    {
                        float3 toLantern = _PunchyLantern.xyz - input.positionWS;
                        float reachL = saturate(1 - length(toLantern) / _PunchyLanternColor.w);
                        half3 hL = normalize(normalize(toLantern) + v);
                        color += _PunchyLanternColor.rgb * _PunchyLantern.w * reachL * pow(saturate(dot(n, hL)), 40) * gloss * 1.5 * ao;
                    }
                }

                color = PunchyAtmosphere(color, input.positionWS);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma multi_compile_local_vertex _ FLORA_STATIC   // refactor 3.5: models that never move skip the wind
            #pragma fragment Frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #define FLORA_SHADOW_PASS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "Flora.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;
            // W-107's cheap far caster: xyz the camera, w the near range (0: off). Past it the far level of detail
            // casts as a solid card, so world-space noise cuts holes in it for a dappled canopy shadow.
            float4 _FloraCheapHoles;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float fade : TEXCOORD0;
                float4 moss : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            float HoleHash(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            // Value noise on 1.3 m cells: blotches about the size of the gaps between a crown's leaf clumps.
            float HoleNoise(float3 p)
            {
                p /= 1.3;
                float3 i = floor(p);
                float3 f = p - i;
                f = f * f * (3 - 2 * f);
                return lerp(lerp(lerp(HoleHash(i), HoleHash(i + float3(1, 0, 0)), f.x),
                                 lerp(HoleHash(i + float3(0, 1, 0)), HoleHash(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(HoleHash(i + float3(0, 0, 1)), HoleHash(i + float3(1, 0, 1)), f.x),
                                 lerp(HoleHash(i + float3(0, 1, 1)), HoleHash(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            Varyings Vert(FloraAttributes input)
            {
                FloraVertex v = FloraTransform(input);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - v.positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                Varyings o;
                o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(v.positionWS, v.normalWS, lightDirectionWS)));
                o.fade = v.fade;
                o.moss = FloraMossData(input);
                o.positionWS = v.positionWS;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Shadows don't crossfade (dithered shadows show as dotted patches on the ground): a plant changing
                // LOD keeps casting with its outgoing level until the change is complete.
                clip(input.fade >= 0 && input.fade < 1 ? -1 : 1);
                FloraMossClip(input.moss);
                // (Only crowns: solid casters come with a negative fade, 3.7 step 3.)
                if (input.fade > 0 && _FloraCheapHoles.w > 0 && distance(input.positionWS.xz, _FloraCheapHoles.xz) > _FloraCheapHoles.w)
                    clip(HoleNoise(input.positionWS) - 0.45);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            // W-107: FloraSystem's far shadow cache (FloraSystem.ShadowCache.cs) draws trees from the sun into a float
            // texture: each texel keeps how far along the sun's direction the nearest caster is. Never a URP pass.
            Name "FloraCache"
            Tags { "LightMode" = "FloraCache" }
            ZWrite On
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #define FLORA_CACHE_PASS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Flora.hlsl"

            float4 _FloraCacheSun;   // xyz the way the sunlight travels
            float4x4 _FloraCacheVP;  // the cache's view from the sun (orthographic), ready for the GPU

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float fade : TEXCOORD0;
                float along : TEXCOORD1;
                float4 moss : TEXCOORD2;
            };

            Varyings Vert(FloraAttributes input)
            {
                FloraVertex v = FloraTransform(input);
                Varyings o;
                o.positionCS = mul(_FloraCacheVP, float4(v.positionWS, 1));
                o.fade = v.fade;
                o.along = dot(v.positionWS, _FloraCacheSun.xyz);
                o.moss = FloraMossData(input);
                return o;
            }

            float Frag(Varyings input) : SV_Target
            {
                clip(input.fade >= 0 && input.fade < 1 ? -1 : 1);
                FloraMossClip(input.moss);
                return input.along;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma multi_compile_local_vertex _ FLORA_STATIC   // refactor 3.5: models that never move skip the wind
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Flora.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float fade : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float4 moss : TEXCOORD2;
            };

            Varyings Vert(FloraAttributes input)
            {
                FloraVertex v = FloraTransform(input);
                Varyings o;
                o.positionCS = TransformWorldToHClip(v.positionWS);
                o.fade = v.fade;
                o.positionWS = v.positionWS;
                o.moss = FloraMossData(input);
                return o;
            }

            half Frag(Varyings input) : SV_Target
            {
                FloraDither(input.positionCS, input.fade, input.positionWS);
                FloraMossClip(input.moss);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        // Depth and normals for the depth-normals prepass (SSAO). With it, the camera depth texture comes from this
        // prepass, so without this pass plants would be missing from it (and from SSAO and the heat haze).
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma multi_compile_local_vertex _ FLORA_STATIC   // refactor 3.5: models that never move skip the wind
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include "Flora.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float fade : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                float4 moss : TEXCOORD3;
            };

            Varyings Vert(FloraAttributes input)
            {
                FloraVertex v = FloraTransform(input);
                Varyings o;
                o.positionCS = TransformWorldToHClip(v.positionWS);
                o.fade = v.fade;
                o.positionWS = v.positionWS;
                o.normalWS = v.normalWS;
                o.moss = FloraMossData(input);
                return o;
            }

            void Frag(Varyings input, bool front : SV_IsFrontFace, out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                FloraDither(input.positionCS, input.fade, input.positionWS);
                FloraMossClip(input.moss);
                // Leaves are double-sided: the back face's normal points the other way.
                half3 n = NormalizeNormalPerPixel(input.normalWS);
                outNormalWS = half4(front ? n : -n, 0);
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }
    }
}
