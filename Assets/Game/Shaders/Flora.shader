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
            half PunchyOctave(float period, float footprint)
            {
                return smoothstep(3, 8, period / max(footprint, 1e-4));
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
                float wobble = PunchyValueNoise(float2(along * 0.7, sideways * 2.0)) - 0.5;
                half fine = smoothstep(0.4, 0.8, PunchyValueNoise(float2(sideways * 40 + wobble * 3, along * 1.4)));
                half broad = smoothstep(0.35, 0.8, PunchyValueNoise(float2((across.x - across.y) * 9 + wobble * 2, along * 0.45)));
                half grain = fine * 0.55 * PunchyOctave(0.025, footprint) + broad * 0.45 * PunchyOctave(0.11, footprint);
                return 1 - grain * 0.32;
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
            };

            Varyings Vert(FloraAttributes input)
            {
                FloraVertex v = FloraTransform(input);
                Varyings o;
                o.positionWS = v.positionWS;
                o.positionCS = TransformWorldToHClip(v.positionWS);
                o.normalWS = v.normalWS;
                // Darker towards the ground and on downward faces (leaves and petals: towards the ground only).
                int partV = (int)round(input.data.y);
                half solid = partV == 1 || partV == 2 || partV == 5 ? 0 : 1;
                half grounding = PunchyGrounding(v.positionWS.y - v.plant.position.y, v.normalWS.y, solid);
                o.color = half4(input.color.rgb, input.data.w * grounding);
                uint g = v.plant.ground;
                o.ground = half4(g & 255, (g >> 8) & 255, (g >> 16) & 255, g >> 24) / 255.0;
                o.baseHeight = float2(v.positionWS.y - v.plant.position.y, v.plant.scale);
                o.extra = float4(input.data.y, v.fade, v.plant.snow, v.plant.random);
                o.grainPos = input.positionOS;
                o.grainAxis = input.color.a;
                // Keyed on the pane (each has a slightly different colour) and the building.
                float3 key = round(input.color.rgb * 255);
                o.windowHour = frac(sin(dot(key, float3(0.917, 0.473, 0.131)) * 10 + v.plant.random * 61.7) * 43758.5453);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                FloraDither(input.positionCS, input.extra.y, input.positionWS);
                int part = (int)round(input.extra.x);
                // A rock tint (ground alpha at its top value) or a built thing's dirt band (any other non-zero alpha).
                bool rockTint = input.ground.a > 0.995;
                bool built = input.ground.a > 0 && !rockTint;
                bool leafy = part == 1 || part == 2 || part == 5;
                bool metal = part == 4;
                // Thin tapered strands (roots, stems) can end in a degenerate normal; a NaN there blooms into a bright fleck.
                half3 n = SafeNormalize(input.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
                // Built things are drawn double-sided too, and a roof is one sheet: seen from below it's the sheet's
                // back, whose normal still points up. Light the side you see.
                if (part == 0 && built && dot(n, v) < 0)
                    n = -n;

                // Per-plant tint: brightness, and leaves a little yellower or bluer.
                half r = input.extra.w;
                half3 albedo = input.color.rgb * (0.88 + 0.24 * r);

                // How many metres one pixel covers here, and the facet's own normal (outside any branch, so the
                // derivatives are good).
                float footprint = length(fwidth(input.positionWS));
                float3 facet = normalize(cross(ddy(input.positionWS), ddx(input.positionWS)) + 1e-9);
                // Cloth: lit by the surface the wind has waved (its folds catch the light), facing the viewer.
                if (part == 10)
                    n = dot(facet, v) < 0 ? -facet : facet;
                // Built things (those with a dirt band) and banded rock: weathering that stays at every range
                // (PunchyWeathering).
                bool strata = part == 0 && frac(input.extra.x) > 0.04 && frac(input.extra.x) < 0.15;
                if (part == 0 && built || strata)
                    albedo *= PunchyWeathering(input.positionWS, n, footprint, r);
                // Timber (marked Bark + 0.02): grain along each piece (Z-30: wood read as leather or plaster).
                if (part == 0 && frac(input.extra.x) > 0.01 && frac(input.extra.x) < 0.035)
                    albedo *= PunchyWoodGrain(input.grainPos, input.grainAxis, footprint);
                // Banded rock (FloraSpecies.RockStrata): exactly the terrain's strata (TerrainSurface.shader), same
                // world heights and colours, so a rock prop's layers line up with the cliff beside it.
                if (strata)
                {
                    float2 sp = input.positionWS.xz;
                    float sy = input.positionWS.y + (PunchyValueNoise(sp * 0.02) - 0.5) * 6 + (PunchyValueNoise(sp * 0.11) - 0.5) * 1.2;
                    half band = PunchyValueNoise(float2(sy / 4.5, 7.1)) * 0.7 + PunchyValueNoise(float2(sy / 1.1, 3.3)) * 0.3;
                    albedo *= lerp(1, lerp(half3(0.55, 0.45, 0.42), half3(1.45, 1.3, 1.08), smoothstep(0.3, 0.7, band)), 0.9);
                }
                // Rock and bark up close: grain and fine relief from the shared detail texture (the caves' own), read
                // three ways and blended by the surface's facing, fading out with distance.
                float near = 1 - smoothstep(18, 45, distance(input.positionWS, GetCameraPositionWS()));
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
                // Windows: dark glass by day (their colour is the light they give at night, below).
                half3 windowLight = albedo;
                if (part == 6)
                    albedo = half3(0.05, 0.06, 0.07) + albedo * 0.06;

                // Snow settles on upward faces where the ground under the plant is snowy.
                half snow = saturate(input.extra.z * 1.5) * smoothstep(0.15, 0.6, n.y);
                albedo = lerp(albedo, half3(0.86, 0.88, 0.92), snow);

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
                else if (part == 0)
                    albedo = PunchyDirt(albedo, input.ground, input.baseHeight.x, input.baseHeight.y, wet, input.positionWS);
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
                half ao = input.color.a;

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
                    color = color * (1 - crack * 0.6) + half3(1.0, 0.36, 0.08) * crack * pulse * 2.4;
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
                    color += half3(1.0, 0.4, 0.1) * sliver * patch * pulse * 1.8;
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
            #pragma fragment Frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "Flora.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float fade : TEXCOORD0;
            };

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
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Shadows don't crossfade (dithered shadows show as dotted patches on the ground): a plant changing
                // LOD keeps casting with its outgoing level until the change is complete.
                clip(input.fade >= 0 && input.fade < 1 ? -1 : 1);
                return 0;
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
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Flora.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float fade : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            Varyings Vert(FloraAttributes input)
            {
                FloraVertex v = FloraTransform(input);
                Varyings o;
                o.positionCS = TransformWorldToHClip(v.positionWS);
                o.fade = v.fade;
                o.positionWS = v.positionWS;
                return o;
            }

            half Frag(Varyings input) : SV_Target
            {
                FloraDither(input.positionCS, input.fade, input.positionWS);
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
            };

            Varyings Vert(FloraAttributes input)
            {
                FloraVertex v = FloraTransform(input);
                Varyings o;
                o.positionCS = TransformWorldToHClip(v.positionWS);
                o.fade = v.fade;
                o.positionWS = v.positionWS;
                o.normalWS = v.normalWS;
                return o;
            }

            void Frag(Varyings input, bool front : SV_IsFrontFace, out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                FloraDither(input.positionCS, input.fade, input.positionWS);
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
