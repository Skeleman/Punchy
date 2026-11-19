Shader "Punchy/Cave"
{
    // Cave walls (CaveSystem): rough rock in two tones broken up by noise, lit by daylight only near the entrance
    // (vertex colour alpha: 1 at the mouth, falling to 0 deep inside) and by any additional lights, such as the
    // player's lantern, everywhere. At the entrance the walls take the surrounding terrain's colour (vertex rgb) and
    // blend into the rock a few metres in (uv.x). The mesh is in world space and double-sided.
    //
    // Each vertex carries its cave's style slot (uv.z) into _PunchyCaveStyles, the table CaveSystem uploads from the
    // CaveStyle assets: rock colours and banding, a finish (gloss, metal, crystal facets, translucent ice, frost sheen),
    // fine surface detail from one shared texture, a floor material (ground facing up: sand, gravel, ash, frost crust)
    // and glowing cracks in sparse patches. One shader and one material for every style. Slot 0 is no style, drawn
    // with this material's own colours.
    //
    // W-138 row 4b: a road tunnel's walls carry 32 x a tunnel code on top of the slot (1 + 1 if its road is paved + 2
    // if it's built: lamps or timber frames), and their rib shade channel carries the road's share of the floor
    // (negative: a fallen stone's share). Tunnels draw their road as cobbles or packed grit and their walls as rough
    // rock, with cut stone courses where built, all in the cave's own light; no veins, cracks or ribs.
    Properties
    {
        _RockColor ("Rock", Color) = (0.48, 0.44, 0.4, 1)
        _DarkColor ("Crevices", Color) = (0.22, 0.2, 0.18, 1)
        _Darkness ("Light deep inside", Range(0, 0.2)) = 0.05
        // A-91: what a room kit copy of this material is made of: -1 as is (crystals, bone), 0 bare rock (damp,
        // drips, mineral veins), 1 dressed stone (blocks, flagstones), 2 wood, 3 iron.
        [HideInInspector] _Dressed ("Dressed", Float) = 0
        // Light a plain (slot 0) copy gives off everywhere: pale wall fungi that would read black deep in.
        [HideInInspector] _OwnLight ("Own light", Color) = (0, 0, 0, 0)
        // 1: a plain copy (wall details, tendrils) whose vertices carry their cave's slot keeps its own colours but takes
        // that style's everywhere light (Crystal's purple, Ice's blue), so it sits in the same air as the walls (W-179).
        [HideInInspector] _StyleAir ("Style air", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _RockColor;
            half4 _DarkColor;
            half _Darkness;
            half _Dressed;
            half4 _OwnLight;
            half _StyleAir;
        CBUFFER_END

        struct CaveAttributes
        {
            float3 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 color : COLOR;
            float4 data : TEXCOORD0;   // x terrain blend, y rock tint, z style slot (+ 32 x tunnel code), w rib shade / road share
            float4 rock : TEXCOORD1;   // W-162: rgb the country rock round a mouth, a its share (no stream: 0)
        };
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Atmosphere.hlsl"
            #include "Materials/Wood.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 color : TEXCOORD2;
                float3 data : TEXCOORD3;   // x terrain blend, y rock tint, z rib shade (a tunnel: road share, negative a stone's)
                nointerpolation float style : TEXCOORD4;
                half4 country : TEXCOORD5;   // W-162: rgb the country rock, a its share (1 at a mouth, 0 deep inside)
            };

            // Nine vectors per style slot (CaveStyle.Pack): rock rgb + strata, dark rgb + strata frequency,
            // glow rgb + crack width, (gloss, metallic, facets, translucency), (glow scale, glow everywhere, cracks),
            // floor rgb + grit, floor dark rgb + floor gloss, (detail, detail scale, sheen, moss), moss rgb.
            float4 _PunchyCaveStyles[32 * 9];
            // The nearest glowing things (lava pools, glowing plants): they light the walls round them in the shader
            // instead of with real lights (the list is declared in Glow.hlsl, shared with the terrain and plants).
            #include "Glow.hlsl"
            #include "WideShadow.hlsl"
            #include "Surface.hlsl"
            // Shared tiling detail (CaveSystem makes it once): rg a fine normal, b a height, a a sparkle.
            TEXTURE2D(_PunchyCaveDetail);
            SAMPLER(sampler_PunchyCaveDetail);
            float _PunchyCaveDetailReady;
            float _PunchyTerrainDetailPpm;
            #include "Materials/RockWall.hlsl"
            #include "Materials/Roots.hlsl"

            Varyings Vert(CaveAttributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.color = input.color;
                o.data = input.data.xyw;
                o.style = input.data.z;
                // A mesh with no rock stream (kit pieces, hangings) may read (0, 0, 0, 1): no colour, no share.
                o.country = half4(input.rock.rgb, input.rock.a * step(1e-4, dot(input.rock.rgb, 1)));
                return o;
            }

            // Integer hash of a lattice cell → three values in -0.5..0.5 (for crystal facets far from the origin).
            float3 FacetHash(int3 c)
            {
                uint h = asuint(c.x) * 0x8da6b343u ^ asuint(c.y) * 0xd8163841u ^ asuint(c.z) * 0xcb1ab31fu;
                h ^= h >> 13; h *= 0x85ebca6bu; h ^= h >> 16;
                uint3 v = uint3(h, h * 0x27d4eb2du, h * 0x165667b1u);
                return float3(v >> 8) * (1.0 / 16777216.0) - 0.5;
            }

            // Distance to the nearest border between random cells (second-nearest minus nearest feature point): 0 on
            // a border, growing towards a cell's middle, with its gradient for the crack bevel.
            float CellEdge(float3 q, out float3 grad)
            {
                int3 c = int3(floor(q));
                float f1 = 9, f2 = 9;
                float3 u1 = 0, u2 = 0;
                for (int z = -1; z <= 1; z++)
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    int3 cell = c + int3(x, y, z);
                    float3 v = q - (float3(cell) + 0.5 + FacetHash(cell) * 0.9);
                    float d = length(v);
                    if (d < f1) { f2 = f1; u2 = u1; f1 = d; u1 = v / max(d, 1e-5); }
                    else if (d < f2) { f2 = d; u2 = v / max(d, 1e-5); }
                }
                grad = u2 - u1;
                return f2 - f1;
            }

            // The shared detail texture on three planes, blended by the normal: rg a tangent-space normal in -1..1 per
            // plane (folded into a world-space nudge), b a height, a a sparkle.
            float4 Detail(float3 p, float3 n, float scale, out float3 nudge)
            {
                float3 w = pow(abs(n), 4);
                w /= w.x + w.y + w.z;
                float4 x = SAMPLE_TEXTURE2D(_PunchyCaveDetail, sampler_PunchyCaveDetail, p.zy * scale);
                float4 y = SAMPLE_TEXTURE2D(_PunchyCaveDetail, sampler_PunchyCaveDetail, p.xz * scale);
                float4 z = SAMPLE_TEXTURE2D(_PunchyCaveDetail, sampler_PunchyCaveDetail, p.xy * scale);
                float2 nx = x.rg * 2 - 1, ny = y.rg * 2 - 1, nz = z.rg * 2 - 1;
                nudge = float3(0, nx.y, nx.x) * w.x + float3(ny.x, 0, ny.y) * w.y + float3(nz.x, nz.y, 0) * w.z;
                return x * w.x + y * w.y + z * w.z;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 shape = normalize(input.normalWS);
                float3 n = shape;
                float3 p = input.positionWS;
                float openness = saturate(input.color.a);

                int raw = max((int)round(input.style), 0);
                int slot = raw & 31;
                int code = raw >> 5;
                bool tunnel = code > 0;
                bool paved = tunnel && ((code - 1) & 1) != 0;
                bool built = tunnel && ((code - 1) & 2) != 0;
                float4 s0 = float4(_RockColor.rgb, 0.3), s1 = float4(_DarkColor.rgb, 0.366), s2 = float4(_OwnLight.rgb, 0), s3 = 0, s4 = float4(1, 1, 0, 0);
                float4 s5 = float4(_RockColor.rgb * 0.8, 0.3), s6 = float4(_DarkColor.rgb, 0), s7 = float4(0.3, 1, 0, 0), s8 = 0;
                // Slotless copies (dungeon props, kit pieces) take the camera's cave air (Surface.hlsl).
                half3 air = slot == 0 ? (half3)_PunchyCaveAir.rgb : 0;
                if (_StyleAir > 0.5 && slot > 0)
                {
                    air = _PunchyCaveStyles[slot * 9 + 2].rgb * _PunchyCaveStyles[slot * 9 + 4].y;
                    slot = 0;
                }
                if (slot > 0)
                {
                    int i = slot * 9;
                    s0 = _PunchyCaveStyles[i];     s1 = _PunchyCaveStyles[i + 1];
                    s2 = _PunchyCaveStyles[i + 2]; s3 = _PunchyCaveStyles[i + 3];
                    s4 = _PunchyCaveStyles[i + 4]; s5 = _PunchyCaveStyles[i + 5];
                    s6 = _PunchyCaveStyles[i + 6]; s7 = _PunchyCaveStyles[i + 7];
                    s8 = _PunchyCaveStyles[i + 8];
                }
                // W-162 row 4: the ground share ends in the cliff toe's ragged band (as ScarpPaint), not on one depth line.
                float toeRag = (PunchyValueNoise(p.xz * 0.4 + p.y * 0.3) - 0.5) * 0.7 + (PunchyValueNoise(p.xz * 1.7 + p.y * 1.3 + 7.3) - 0.5) * 0.35;
                float toeEdge = clamp(0.5 + toeRag - 0.2 * smoothstep(0.55, 0.85, shape.y), 0.2, 0.8);
                float terrain = smoothstep(toeEdge - 0.12, toeEdge + 0.12, saturate(input.data.x));
                float floorShare = smoothstep(0.55, 0.8, shape.y) * (1 - terrain);
                float gloss = lerp(s3.x, s6.w, floorShare);
                float metallic = s3.y * (1 - floorShare), facets = s3.z * (1 - floorShare), translucency = s3.w * (1 - floorShare);
                gloss *= 1 - terrain;
                metallic *= 1 - terrain;
                translucency *= 1 - terrain;

                float glassMask = PunchyValueNoise(p.xz * 0.18 + p.y * 0.11 + 13) * 0.65 + PunchyValueNoise(p.zy * 0.4 - 5) * 0.35;
                float sheetMask = PunchyValueNoise(p.xz * 0.25 + 31) * 0.7 + PunchyValueNoise(p.xz * 0.9 - 7) * 0.3;
                float slick = s3.w * saturate(smoothstep(0.55, 0.62, glassMask) * (1 - floorShare) + smoothstep(0.5, 0.57, sheetMask) * floorShare) * (1 - terrain);

                float3 nudge;
                float4 detail = Detail(p, shape, s7.y, nudge);
                float detailScale = s7.y;
                // R-26 row 3: at a mouth the walls take the grain at the ground's scale (TerrainSurface), so neither side reads finer.
                const float groundGrainScale = 0.6;
                [branch] if (openness > 0.001 && s7.y > groundGrainScale)
                {
                    float3 mouthNudge;
                    float4 mouthDetail = Detail(p, shape, groundGrainScale, mouthNudge);
                    detail = lerp(detail, mouthDetail, openness);
                    nudge = lerp(nudge, mouthNudge, openness);
                    detailScale = lerp(s7.y, groundGrainScale, openness);
                }
                // G-35: towards the terrain's level at a mouth.
                float detailStrength = s7.x * lerp(1, 0.35, openness);
                float detailPxm = (_PunchyTerrainDetailPpm > 0 ? _PunchyTerrainDetailPpm : 935) / max(distance(p, GetCameraPositionWS()), 0.01);
                detailStrength *= saturate(detailPxm / (8 * detailScale) * 0.5 - 1);
                // No double noise on the shared stone below; only the mouth keeps the ground's grain on it.
                float stoneShare = _PunchyCaveDetailReady > 0 && _Dressed > -0.5 && _Dressed < 1.5 ? (1 - terrain) * (1 - slick) * (1 - s3.z) : 0;
                half mouthGrain = openness * stoneShare * smoothstep(15.6, 31.2, detailPxm) * 0.7;
                n = normalize(n + nudge * (detailStrength * (1 - terrain) * (1 - slick) * (1 - stoneShare) + mouthGrain * 0.8));

                if (facets > 0)
                {
                    float3 face = normalize(cross(ddy(p), ddx(p)));
                    face *= dot(face, shape) < 0 ? -1 : 1;
                    n = normalize(lerp(n, face, facets));
                }

                // W-162 row 2: strata are the one rock wall's beds (Materials/RockWall.hlsl), as on the cliffs outside.
                float blotch = PunchyValueNoise(p.xz * 0.35 + p.y * 0.2) * 0.6 + PunchyValueNoise(p.xy * 1.3 + p.z) * 0.4;
                float bedPxm = 1 / max(length(fwidth(p)), 1e-4);
                half bedFace = (1 - floorShare) * (1 - terrain) * (1 - slick) * (1 - facets) * (1 - smoothstep(0.4, 0.85, abs(shape.y)));
                half bedBand, bedLedge;
                float bedY;
                PunchyRockBeds(p.xz, p.y, clamp(1 / (6 * max(s1.w, 0.01)), 0.25, 2), bedPxm, true, bedFace, n, bedBand, bedLedge, bedY);
                float strata = saturate(0.5 + bedBand * 1.4);
                float tone = saturate(blotch * 0.8 + strata * s0.w + (detail.b - 0.5) * detailStrength * (1 - stoneShare));
                // W-162 row 2: country rock colour at the mouth; ice, ore and crystal keep their own.
                half3 pale = s0.rgb, dark = s1.rgb;
                half3 country = input.country.rgb;
                country = lerp(country, dot(country, half3(0.3, 0.5, 0.2)) * half3(1.03, 0.98, 0.92), 0.6);
                PunchyRockCountry(country, input.country.a * (1 - saturate(max(s3.y, max(s3.z, s3.w)) * 2)), pale, dark);
                half themed = smoothstep(0.2, 0.5, max(s3.y, max(s3.z, s3.w)));
                half3 rock = lerp(dark, pale, tone) * input.data.y;
                rock = PunchyRockShade(rock, 0, 0, bedLedge * bedFace, 0);
                half3 floorTone = lerp(s6.rgb, s5.rgb, saturate(blotch * 0.7 + detail.b * 0.5));
                floorTone *= lerp(1, 0.6 + 0.8 * detail.a, s5.w);
                rock = lerp(rock, floorTone, floorShare);

                // W-138 row 4b.
                float roadShare = 0;
                if (tunnel)
                {
                    float stoneShare = saturate(-input.data.z);
                    roadShare = saturate(input.data.z) * smoothstep(0.5, 0.75, shape.y) * (1 - terrain);
                    float bareWall = (1 - stoneShare) * (1 - roadShare) * (1 - terrain);
                    // W-172 row 4: a clear top edge under the vault, no ragged noise.
                    float slope = abs(shape.y);
                    float courses = built ? (1 - smoothstep(0.43, 0.45, slope)) * bareWall : 0;
                    float capping = built ? smoothstep(0.35, 0.36, slope) * courses : 0;
                    float overhang = built ? smoothstep(0.44, 0.46, slope) * (1 - smoothstep(0.46, 0.52, slope)) * bareWall : 0;
                    float3 coarseNudge;
                    float4 coarse = Detail(p, shape, s7.y * 0.23, coarseNudge);
                    n = normalize(n + coarseNudge * 0.6 * bareWall * (1 - 0.6 * courses));
                    rock *= lerp(1, 0.75 + 0.5 * coarse.b, bareWall);
                    [branch] if (courses > 0)
                    {
                        // W-166 row 3: the one built stone (Materials/Stone.hlsl); its footprint is safe in this branch (rule 24).
                        float footprint = length(_WorldSpaceCameraPos - p) * 2 / (_ScreenParams.y * UNITY_MATRIX_P._m11);
                        half3 cutN = n;
                        float body = courses * (1 - capping);
                        rock = lerp(rock, rock * PunchyStoneBlocks(p, cutN, footprint, false, 0), body);
                        n = normalize(lerp(n, cutN, body));
                        const float blockLength = 0.95;
                        bool acrossX = abs(shape.x) < 0.75;
                        float capJoint = acrossX ? frac(p.x / blockLength * 0.5) : frac(p.z / blockLength * 0.5);
                        capJoint = min(capJoint, 1 - capJoint) * blockLength * 2;
                        float capTone = 1.12 + 0.12 * (FacetHash(int3(floor(p.y / 0.42), floor(p.x / (blockLength * 2)), floor(p.z / (blockLength * 2)))).y);
                        rock = lerp(rock, rock * capTone, capping);
                        float capMortar = max(1 - smoothstep(0.018, 0.034, capJoint), smoothstep(0.35, 0.36, slope) * (1 - smoothstep(0.36, 0.372, slope))) * capping;
                        rock = lerp(rock, rock * 0.38, capMortar * courses);
                    }
                    rock *= 1 - overhang * 0.55;
                    half3 stone = half3(0.62, 0.6, 0.56) * (0.7 + 0.6 * detail.b) * (0.8 + 0.4 * coarse.b) * input.data.y;
                    rock = lerp(rock, stone, smoothstep(0.5, 0.9, stoneShare));
                    rock *= 1 - stoneShare * (1 - stoneShare) * 4 * 0.45;
                    [branch] if (roadShare > 0)
                    {
                        half3 road = input.color.rgb;
                        float3 roadNormal = n;
                        if (paved)
                        {
                            const float sett = 0.32;
                            float2 q = p.xz / sett;
                            int2 c = int2(floor(q));
                            float f1 = 9, f2 = 9;
                            float2 centre = q;
                            int2 id = c;
                            for (int y = -1; y <= 1; y++)
                            for (int x = -1; x <= 1; x++)
                            {
                                int2 cell = c + int2(x, y);
                                float2 at = float2(cell) + 0.5 + FacetHash(int3(cell, 7)).xy * 0.8;
                                float d = distance(q, at);
                                if (d < f1) { f2 = f1; f1 = d; centre = at; id = cell; }
                                else if (d < f2) f2 = d;
                            }
                            float edge = (f2 - f1) * sett;
                            float joint = 1 - smoothstep(0.012, 0.035, edge);
                            float settTone = 0.78 + 0.44 * (FacetHash(int3(id, 11)).x + 0.5);
                            road *= settTone * (0.85 + 0.3 * detail.b) * (1 - joint * 0.65) * (1 - (1 - smoothstep(0.03, 0.08, edge)) * 0.2);
                            float2 dome = (q - centre) * 0.55;
                            roadNormal = normalize(float3(dome.x, 1, dome.y) + nudge * 0.3);
                        }
                        else
                        {
                            // World xz, so it stays crisp where the shared detail smears along the bore.
                            const float pebble = 0.06;
                            float2 q = p.xz / pebble;
                            int2 c = int2(floor(q));
                            float f1 = 9, f2 = 9;
                            int2 id = c;
                            for (int y = -1; y <= 1; y++)
                            for (int x = -1; x <= 1; x++)
                            {
                                int2 cell = c + int2(x, y);
                                float d = distance(q, float2(cell) + 0.5 + FacetHash(int3(cell, 5)).xy * 0.9);
                                if (d < f1) { f2 = f1; f1 = d; id = cell; }
                                else if (d < f2) f2 = d;
                            }
                            float bed = 1 - smoothstep(0.04, 0.2, f2 - f1);
                            float pebbleTone = 0.75 + 0.5 * (FacetHash(int3(id, 13)).x + 0.5);
                            float patch = 0.8 + 0.4 * PunchyValueNoise(p.xz * 2 + 31);
                            road *= 0.8 * pebbleTone * patch * (1 - bed * 0.45);
                            roadNormal = normalize(shape + float3(FacetHash(int3(id, 17)).x, 0, FacetHash(int3(id, 19)).x) * 0.35 * (1 - bed));
                        }
                        float gutter = smoothstep(0.5, 0.6, shape.y) * (1 - smoothstep(0.68, 0.82, shape.y));
                        road *= 1 - gutter * 0.4;
                        rock = lerp(rock, road, roadShare);
                        n = normalize(lerp(n, roadNormal, roadShare));
                        gloss = lerp(gloss, lerp(0.08, 0.6, gutter), roadShare);
                    }
                }
                if (s7.w > 0)
                {
                    float clump = PunchyValueNoise(p.xz * 0.6 + p.y * 0.3 + 71) * 0.7 + detail.b * 0.3;
                    float moss = smoothstep(0.55, 0.7, clump + s7.w * 0.35 + openness * 0.25) * saturate(shape.y * 1.6 + 0.2) * s7.w * (1 - roadShare * 0.85);
                    rock = lerp(rock, s8.rgb * (0.7 + 0.6 * detail.b), moss);
                    gloss *= 1 - moss;
                }
                // A-91: all in albedo and gloss, no extra pass or texture.
                float dressed = _Dressed;
                float bare = (1 - terrain) * (1 - slick) * (1 - openness * 0.8);
                float dampMask = PunchyValueNoise(p.xz * 0.11 + p.y * 0.05 + 211) * 0.7 + PunchyValueNoise(p.xz * 0.43 - 17) * 0.3;
                float wall = 1 - abs(shape.y);
                float streak = smoothstep(0.7, 0.8, PunchyValueNoise(float2((p.x - p.z) * 2.3, p.y * 0.16 + 5)))
                             * smoothstep(0.45, 0.6, dampMask) * wall;
                float damp = max(smoothstep(0.58, 0.7, dampMask) * saturate(shape.y * 0.8 + 0.5), streak * 0.8) * bare;
                if (dressed > -0.5 && dressed < 0.5)
                {
                    rock *= lerp(1, 0.6, damp);
                    gloss = lerp(gloss, 0.8, damp * 0.85);
                    // Not in road tunnels: on bored rock they read as white squiggles (W-138 row 4b).
                    float veinPatch = PunchyValueNoise(p.xz * 0.06 + p.y * 0.08 + 401);
                    float veinLine = PunchyValueNoise(p.xy * 0.7 + p.z * 0.55 + 3) * 0.6 + PunchyValueNoise(p.zy * 1.9 - p.x * 0.4) * 0.4;
                    // Plain stone gets sparse seams near the rock's own tone; ore, crystal and ice keep the bright netting.
                    float vein = (1 - smoothstep(0.0, lerp(0.018, 0.03, themed), abs(veinLine - 0.5))) * smoothstep(lerp(0.72, 0.62, themed), lerp(0.8, 0.72, themed), veinPatch)
                               * (1 - floorShare * 0.7) * bare * (tunnel ? 0 : 1);
                    float calcite = step(0.5, PunchyValueNoise(p.xz * 0.015 + 77));
                    half3 mineral = lerp(half3(0.5, 0.25, 0.11), half3(0.88, 0.86, 0.8), calcite);
                    mineral = lerp(lerp(mineral, rock * 1.5, 0.65), mineral, themed);
                    rock = lerp(rock, mineral * (0.8 + 0.4 * detail.b), vein * lerp(0.4, 0.85, themed));
                    gloss = lerp(gloss, lerp(0.3, 0.55, themed), vein * calcite);
                }
                else if (dressed > 0.5 && dressed < 1.5)
                {
                    // Noise in the face's own plane, so it isn't sheared into diagonal scratches on walls.
                    float2 faceDir = normalize(float2(-shape.z, shape.x) + float2(1e-4, 0));
                    float2 face = abs(shape.y) > 0.7 ? p.xz : float2(dot(p.xz, faceDir), p.y);
                    float mottle = PunchyValueNoise(face * 1.7 + 13) * 0.6 + PunchyValueNoise(face * 4.1 - 29) * 0.4;
                    float pit = smoothstep(0.86, 0.9, PunchyValueNoise(face * 23 + 57));
                    float up = saturate(shape.y * 1.4 - 0.3);
                    float wear = smoothstep(0.35, 0.75, PunchyValueNoise(p.xz * 0.22 + 33)) * up;
                    float grime = smoothstep(0.55, 0.8, PunchyValueNoise(face * 0.35 + 71)) * (1 - up);
                    rock *= (0.84 + 0.28 * mottle) * (1 - pit * 0.2) * (1 + wear * 0.14) * (1 - grime * 0.25);
                    float clump = PunchyValueNoise(p.xz * 0.55 + p.y * 0.4 + 91) * 0.7 + detail.b * 0.3;
                    float creep = smoothstep(0.66, 0.72, clump) * saturate(1 - abs(shape.y)) * smoothstep(0.6, 0.75, PunchyValueNoise(p.xz * 0.12 - 9));
                    float stoneMoss = max(smoothstep(0.66, 0.74, clump) * smoothstep(0.3, 0.8, shape.y) * (1 - wear), creep) * (1 - terrain);
                    rock = lerp(rock, half3(0.24, 0.33, 0.13) * (0.7 + 0.6 * detail.b), stoneMoss);
                    // Pooled damp on the floors only: drip streaks read as scratches across cut blocks.
                    float pool = smoothstep(0.62, 0.72, dampMask) * up * bare;
                    rock *= lerp(1, 0.7, pool);
                    gloss = lerp(lerp(gloss * 0.5, 0.3, wear), 0.7, pool * 0.8) * (1 - stoneMoss);
                }
                else if (dressed > 1.5 && dressed < 2.5)
                {
                    // W-166: the shared wood (Materials/Wood.hlsl).
                    float2 along = normalize(float2(-shape.z, shape.x) + float2(1e-4, 0));
                    float2 g = abs(shape.y) > 0.7 ? p.xz : float2(dot(p.xz, along), p.y);
                    half3 timber = lerp(_DarkColor.rgb, _RockColor.rgb, 0.55) * input.data.y;
                    rock = PunchyWoodBoards(timber, g.x, g.y, length(fwidth(p)), PUNCHY_WOOD_DARK, 0.24);   // nan-safe: _Dressed is per material, a uniform branch
                    gloss = 0.12;
                }
                else if (dressed > 2.5)
                {
                    float rust = smoothstep(0.5, 0.7, PunchyValueNoise(p.xz * 3.1 + p.y * 2.3 + 5) * 0.7 + detail.b * 0.3 + saturate(-shape.y) * 0.3);
                    rock = lerp(_RockColor.rgb * (0.85 + 0.3 * detail.b), half3(0.42, 0.2, 0.08) * (0.7 + 0.6 * detail.b), rust) * input.data.y;
                    metallic = 0.65 * (1 - rust);
                    gloss = lerp(0.55, 0.05, rust);
                }
                rock = lerp(rock, s1.rgb * 0.8, slick * 0.7);
                gloss = lerp(gloss, 0.97, slick);
                // R-34 row 2: the shared stone (Stone.hlsl); the ground share takes it below, as the terrain does.
                half stoneOn = 0, stoneCrevice = 0, stoneWet = 0;
                half3 stoneN0 = n;
                float stoneFootprint = length(fwidth(p));
                [branch] if (stoneShare > 0.001)
                {
                    stoneOn = stoneShare;
                    stoneWet = PunchyStoneWetness(damp);
                    half3 stoneRock = rock, stoneN = n;
                    PunchyStoneSurface(p, stoneFootprint, stoneWet, damp * (dressed > -0.5 && dressed < 0.5), stoneRock, stoneN, stoneCrevice);
                    rock = lerp(rock, stoneRock, stoneOn);
                    n = normalize(lerp(n, (float3)stoneN, stoneOn));
                    gloss *= 1 - stoneOn * stoneWet;
                }
                rock *= 1 + (detail.b - 0.5) * 0.6 * mouthGrain;
                [branch] if (dressed > -0.5 && dressed < 0.5 && !tunnel)
                    PunchyStranglerRelief(p, shape, stoneFootprint, smoothstep(0.62, 0.74, PunchyValueNoise(p.xz * 0.03 + 913) + damp * 0.2 + s7.w * 0.15) * bare, rock, n, gloss);
                // Not in road tunnels (W-138 row 4b).
                half crackCore = 0, crackGlow = 0;
                [branch] if (!tunnel && (any(s2.rgb > 0) || s4.w > 0))
                {
                    float g = s4.x;
                    float patch = PunchyValueNoise(p.xz * 0.21 * g + p.y * 0.17 + 37) * 0.7 + PunchyValueNoise(p.zy * 0.5 * g - 11) * 0.3;
                    float crack = smoothstep(1 - s4.z, 1 - s4.z + 0.12, patch) * (1 - smoothstep(0.2, 0.45, shape.y)) * (1 - terrain);
                    [branch] if (crack > 0)
                    {
                        float3 warp = float3(PunchyValueNoise(p.xy * 0.9 + 3), PunchyValueNoise(p.yz * 0.9 - 5), PunchyValueNoise(p.zx * 0.9 + 9));
                        float3 grad;
                        float e = CellEdge(p * 1.3 * g + warp * 1.2, grad);
                        float width = s2.w * lerp(0.45, 1.7, PunchyValueNoise(p.xz * 0.43 * g + p.y * 0.37 - 51));
                        // Thinner than a pixel it widens and fades instead of aliasing into a drawn line.
                        float wide = max(max(width, length(grad) * 1.3 * g * stoneFootprint * 1.5), 1e-4);
                        float pieces = PunchyValueNoise(p.xz * 2.3 * g + p.y * 1.9) * 0.6 + PunchyValueNoise(p.zy * 4.1 * g - 7) * 0.4;
                        crack *= lerp(1, smoothstep(0.42, 0.62, pieces) * smoothstep(1 - s4.z, 1 - s4.z + 0.3, patch), max(s8.w, 0.35)) * width / wide;
                        float t = saturate(e / (wide * 1.6));
                        crackCore = crack * (1 - smoothstep(0.05, 0.75, t));
                        crackGlow = crack * pow(1 - smoothstep(0, 2.4 * wide, e), 2);
                        float3 across = grad - n * dot(grad, n);
                        n = normalize(n - across * rsqrt(max(dot(across, across), 1e-6)) * crack * 4 * t * (1 - t) * 0.9);
                        rock *= 1 - crackCore * s4.w * 0.35;
                        gloss *= 1 - crackCore;
                    }
                }
                half3 ground = input.color.rgb * (0.7 + 0.45 * blotch);
                half3 albedo = lerp(rock, ground, terrain);
                half3 diffuse = albedo * (1 - 0.7 * metallic);
                half3 specColor = lerp(half3(0.9, 0.9, 0.9), albedo * 1.6, metallic);
                float shininess = lerp(12, 256, saturate(gloss + facets * 0.5));
                float glint = 1 + 8 * smoothstep(0.93, 0.99, detail.a) * detailStrength * (1 - slick);
                float3 view = normalize(GetCameraPositionWS() - p);

                InputData inputData = (InputData)0;
                inputData.positionWS = p;
                inputData.normalWS = n;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

                // Openness is per vertex, so this branch is coherent: deep inside, whole wavefronts skip the sun's shadow taps.
                PunchySurface ps = PunchySurfaceAt(p, n);
                ps.caveAir = 0; // walls add their own cave's air per style (air, below)
                ps.sunReach = openness;
                // W-174: lifted off the skin, which lies on itself in the shadow map (Cull Off) and read half shadow everywhere.
                ps.shadowPositionWS = p + n * 0.4;
                ps.skyReach = lerp(_Darkness, 1, openness);
                half3 light = SampleSH(n) * ps.skyReach;
                // R-36: cloud shadow only where the sky is overhead; one read, shared with the terrain share below.
                half cloudShadow = 0;
                half3 shine = 0;
                if (openness > 0.001)
                {
                    PunchyLit lit = PunchySurfaceLight(ps);
                    cloudShadow = lit.cloud;
                    Light sun = lit.sun;
                    light += sun.color * saturate(dot(n, sun.direction)) * lit.shadow;
                    [branch] if (stoneOn * stoneWet > 0.01)
                        shine += sun.color * lit.shadow * PunchyStoneSpec(n, stoneN0, view, sun.direction, stoneCrevice, stoneWet) * stoneOn;
                }
                half3 through = 0;

                // R-10: Glow.hlsl's PunchyRealLight, so a lantern falls off alike on cave rock, ground and glow slots.
                uint count = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(count)
                #if PUNCHY_REAL_LIGHTS
                    half3 toLight;
                    half3 lit = PunchyRealLight(lightIndex, p, toLight) * AdditionalLightRealtimeShadow(lightIndex, p, toLight);
                #else
                    Light l = GetAdditionalLight(lightIndex, p, half4(1, 1, 1, 1));
                    half3 toLight = l.direction;
                    half3 lit = l.color * l.distanceAttenuation * l.shadowAttenuation;
                #endif
                    light += lit * saturate(dot(n, toLight) * 0.8 + 0.2);
                    float3 h = normalize(toLight + view);
                    shine += lit * pow(saturate(dot(n, h)), shininess) * saturate(gloss + metallic * 0.6 + facets * 0.8) * glint;
                    through += lit * (0.35 + 0.65 * saturate(0.5 - 0.5 * dot(n, toLight)));
                    [branch] if (stoneOn * stoneWet > 0.01)
                        shine += lit * PunchyStoneSpec(n, stoneN0, view, toLight, stoneCrevice, stoneWet) * stoneOn;
                LIGHT_LOOP_END

                for (int k = 0; k < _PunchyCaveHeatCount; k++)
                {
                    float3 toPool = _PunchyCaveHeat[k].xyz - p;
                    float4 hc = _PunchyCaveHeatColor[k];
                    float dist = length(toPool);
                    // Out of its reach it adds nothing: skip the rest (the slots are usually full of distant plants).
                    [branch] if (dist < hc.w)
                    {
                        float fall = saturate(1 - dist / hc.w);
                        fall *= fall;
                        float3 dir = toPool / max(dist, 1e-3);
                        half3 warm = hc.rgb * fall;
                        light += warm * saturate(dot(n, dir) * 0.7 + 0.3);
                        shine += warm * pow(saturate(dot(n, normalize(dir + view))), shininess) * saturate(gloss + metallic * 0.6);
                    }
                }

                light *= 1 - crackCore * lerp(0.4, 0.75, s4.w);
                shine *= 1 - crackCore;
                half3 color = diffuse * light + specColor * shine + s0.rgb * through * translucency * 0.6;
                half ribs = tunnel ? 1 : 1 - clamp(input.data.z * 2.4, -0.3, 0.75);
                color *= ribs;
                // Sheen's own floor keeps near-black rock (obsidian) shaped out of the lantern's reach.
                color += lerp(s0.rgb, 1, 0.3) * pow(1 - saturate(dot(n, view)), 3) * s7.z * (light * 0.5 + 0.035);
                [branch] if (stoneOn * stoneWet > 0.01)
                    color += SampleSH(reflect(-view, n)) * lerp(_Darkness, 1, openness) * PunchyStoneFresnel(n, view, stoneCrevice, stoneWet) * stoneOn;
                if (openness > 0.001)
                    color = lerp(color, PunchySurfaceFinish(color, ps), openness);

                // F-29: the ground share is shaded as TerrainSurface shades the ground beside it, so the seam doesn't show.
                [branch] if (terrain > 0.001)
                {
                    half wet = PunchyWetnessAt(p.xz).r * openness;
                    // R-34 row 2: the PBR call's own wet gloss stands in for the stone's lobes on this thin share.
                    float3 groundNormal = shape;
                    half3 groundAlbedo = input.color.rgb;
                    half steep = 1 - smoothstep(0.55, 0.8, shape.y);
                    // W-112: the ground's mouth grain at its scale (0.6), so the floor doesn't turn smooth at the mouth line.
                    float groundPxm = (_PunchyTerrainDetailPpm > 0 ? _PunchyTerrainDetailPpm : 935) / max(distance(p, GetCameraPositionWS()), 0.01);
                    half level = (1 - steep) * saturate((groundPxm - 15) / 16);
                    // TerrainSurface's MicroHeight; 0.8 stands in for the ground's per-vertex bump weight, which this mesh lacks.
                    float3 microW = saturate((float3(3.0, 0.7, 0.21) * groundPxm - 2) / 2) * 0.8 * smoothstep(0.574, 0.766, shape.y);
                    [branch] if (max(microW.x, max(microW.y, microW.z)) > 0.001)
                    {
                        float3 mh;
                        float mc = 0;
                        [unroll] for (int mi = 0; mi < 3; mi++)
                        {
                            float2 mp = p.xz + (mi == 1 ? float2(0.04, 0) : mi == 2 ? float2(0, 0.04) : float2(0, 0));
                            float swell = microW.x > 0.001 ? (PunchyValueNoise(mp / 3.0 + 41.7) - 0.5) * microW.x : 0;
                            float mhi = microW.y > 0.001 ? (PunchyValueNoise(mp / 0.7) - 0.5) * 0.6 * microW.y : 0;
                            mhi += microW.z > 0.001 ? (PunchyValueNoise(mp / 0.21 + 17.3) - 0.5) * 0.4 * microW.z : 0;
                            mh[mi] = mhi + swell * 1.6;
                            if (mi == 0) mc = mhi + swell * 0.4;
                        }
                        groundNormal = normalize(groundNormal - float3(mh.y - mh.x, 0, mh.z - mh.x) / 0.04 * 0.45);
                        groundAlbedo *= 1 + mc * 0.5;
                    }
                    [branch] if (level > 0.01 && _PunchyCaveDetailReady > 0)
                    {
                        half4 rd = SAMPLE_TEXTURE2D(_PunchyCaveDetail, sampler_PunchyCaveDetail, p.xz * 0.6);
                        half2 rb = (rd.rg - 0.5) * 0.56 * level;
                        groundNormal = normalize(groundNormal + float3(rb.x, 0, rb.y));
                        groundAlbedo *= 1 + (rd.b - 0.5) * 0.42 * level;
                        half grainFade = (1 - steep) * saturate((groundPxm * 0.03 - 1) / 3);
                        [branch] if (grainFade > 0.001)
                        {
                            float2 g[2] = { p.xz * 37, float2(p.x * 0.866 - p.z * 0.5, p.x * 0.5 + p.z * 0.866) * 15 + 11.3 };
                            half grain = 0;
                            [unroll] for (int gi = 0; gi < 2; gi++)
                            {
                                uint2 cell = (uint2)(int2)floor(g[gi]);
                                uint h = cell.x * 73856093u ^ cell.y * 19349663u;
                                h = (h ^ (h >> 13)) * 1274126177u;
                                grain += ((h >> 22) / 1023.0 - 0.5) * (gi == 0 ? 0.6 : 0.4);
                            }
                            groundAlbedo *= 1 + grain * 0.5 * grainFade;
                        }
                    }
                    [branch] if (steep > 0.01 && _PunchyCaveDetailReady > 0)
                    {
                        half3 sa = groundAlbedo, sn = shape;
                        half sc;
                        PunchyStoneSurface(p, stoneFootprint, PunchyStoneWetness(wet), wet, sa, sn, sc);
                        groundAlbedo = lerp(groundAlbedo, sa, steep);
                        groundNormal = normalize(lerp(shape, (float3)sn, steep));
                    }
                    InputData ground = (InputData)0;
                    ground.positionWS = p;
                    ground.positionCS = input.positionCS;
                    ground.normalWS = groundNormal;
                    ground.viewDirectionWS = view;
                    // W-174: lifted off the skin as the walls are (ps), or it shades itself to half shadow everywhere.
                    ground.shadowCoord = TransformWorldToShadowCoord(p + shape * 0.4);
                    ground.bakedGI = SampleSH(shape) * lerp(_Darkness, 1, openness) + PunchyGlowLight(p, shape);
                    ground.normalizedScreenSpaceUV = inputData.normalizedScreenSpaceUV;
                    ground.shadowMask = half4(1, 1, 1, 1);
                    SurfaceData groundSurface = (SurfaceData)0;
                    groundSurface.albedo = groundAlbedo * lerp(1, 0.72, wet);
                    groundSurface.smoothness = lerp(0.15, 0.45, wet);
                    groundSurface.occlusion = 1;
                    groundSurface.alpha = 1;
                    half3 groundColor = UniversalFragmentPBR(ground, groundSurface).rgb;
                    // R-36: keep the sun only where the sky is overhead, so a shallow cave's roof doesn't let sun or cloud shadows through.
                    {
                        Light sun = GetMainLight(ground.shadowCoord);
                        half direct = Luminance(sun.color) * saturate(dot(shape, sun.direction)) * sun.shadowAttenuation;
                        groundColor *= PunchySkyShade(cloudShadow, openness, direct, Luminance(ground.bakedGI));
                    }
                    groundColor *= lerp(ribs, 1, openness);
                    groundColor = lerp(groundColor, PunchySurfaceFinish(groundColor, ps), openness);
                    color = lerp(color, groundColor, terrain);
                }

                if (!tunnel && any(s2.rgb > 0))
                    color += s2.rgb * (s4.y * (1 - floorShare * 0.5) + crackGlow * (1 - s4.y) * (1 - s4.w)) * (1 - terrain);
                color += air * (1 - terrain);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            float4 Vert(CaveAttributes input) : SV_POSITION
            {
                float3 p = TransformObjectToWorld(input.positionOS);
                float3 n = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirection = normalize(_LightPosition - p);
            #else
                float3 lightDirection = _LightDirection;
            #endif
                return ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(p, n, lightDirection)));
            }

            half4 Frag() : SV_Target { return 0; }
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

            float4 Vert(CaveAttributes input) : SV_POSITION { return TransformObjectToHClip(input.positionOS); }
            half Frag(float4 positionCS : SV_POSITION) : SV_Target { return positionCS.z; }
            ENDHLSL
        }

        // Needed so the camera depth texture (built from the depth-normals prepass) includes caves.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
            };

            Varyings Vert(CaveAttributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            void Frag(Varyings input, out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                outNormalWS = half4(NormalizeNormalPerPixel(input.normalWS), 0);
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }
    }
}
