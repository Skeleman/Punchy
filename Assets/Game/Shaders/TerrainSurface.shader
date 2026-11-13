Shader "Punchy/Terrain Surface"
{
    Properties
    {
        _EmissionScale("Emission Scale", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

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
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            // Reflect the flat ambient colour (which WeatherSystem keeps in step with the sky) rather than the
            // environment cubemap, which is baked once and would show the wrong sky as weather changes.
            #define _ENVIRONMENTREFLECTIONS_OFF
            #include "TerrainSurface.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Sky.hlsl"
            #include "Glow.hlsl"
            #include "WideShadow.hlsl"
            #include "ChemistryOverlay.hlsl"
            // x how much the ground under grass takes the blades' colour, y the grass radius (GroundDetailSystem).
            float4 _PunchyGrassTint;
            // The shared fine detail CaveSystem makes (rg a normal, b a height): grain on rock faces up close.
            TEXTURE2D(_PunchyCaveDetail);
            SAMPLER(sampler_PunchyCaveDetail);
            float _PunchyCaveDetailReady;
            // Weather globals (WeatherSystem), for the far grass's gusts: wind direction (xz) and strength 0..1.
            float2 _PunchyWindDirection;
            half _PunchyWind;
            // Worms under the sand and snow (the animal session's CreatureBurrowSystem): x, y its age (0..1 over its
            // life), z, w the radius (m) of the worm that made it: positive a bump (the mound over a worm's head, or one
            // it left in its trail), negative a crater where one broke out of the ground or dived back in.
            float4 _PunchyBurrowers[32];
            int _PunchyBurrowerCount;
            // How far out the marks are drawn (GraphicsSettings: DetailRange(BurrowRange), the feeder's range too).
            float _PunchyBurrowRange;
            float _PunchyTerrainDetailFar;   // where the fine relief has faded out (TerrainStreamingSystem, by Detail distance)
            // R-6: reference pixels a metre spans 1 m from the camera, times Detail (TerrainStreamingSystem). Each fine-relief
            // octave fades by its own on-screen size from this. Negative: the old fixed fade at _PunchyTerrainDetailFar.
            float _PunchyTerrainDetailPpm;
            // Where the marks are, coarsely (CreatureBurrowSystem's MarkMask): a cell 1 where some mark reaches into it,
            // and (x, z of the map's corner, cells per metre, cells across).
            TEXTURE2D(_PunchyBurrowMask);
            float4 _PunchyBurrowMaskAt;

            // Whether any worm's mark reaches p (its cell in the mask): elsewhere, most of the ground, the loop is skipped.
            bool NearBurrowMark(float2 p)
            {
                int2 c = (int2)floor((p - _PunchyBurrowMaskAt.xy) * _PunchyBurrowMaskAt.z);
                return all(c >= 0) && all(c < (int)_PunchyBurrowMaskAt.w) && LOAD_TEXTURE2D(_PunchyBurrowMask, c).r > 0;
            }

            // Worms' marks on the ground at p: the mound over a burrowing worm's head and the trail of bumps it leaves
            // behind, each staying where it was raised and sinking away as it ages; where one breaks out or dives back
            // in, a crater with a raised rim, fading. Shading only (the returned tilt for the normal, and how much darker
            // the disturbed ground is): the ground itself doesn't move.
            float2 BurrowMarks(float2 p, out half darken)
            {
                float2 tilt = 0;
                darken = 0;
                for (int k = 0; k < _PunchyBurrowerCount; k++)
                {
                    float4 b = _PunchyBurrowers[k];
                    float radius = abs(b.w);
                    float2 rel = p - b.xz;
                    [branch] if (radius > 0 && dot(rel, rel) < radius * radius * 9)
                    {
                        if (b.w > 0)
                        {
                            // A bump: a soft hump (its slope tilts the normal outwards), sinking away late in its life.
                            float fade = saturate(1 - b.y * b.y);
                            float spread = radius * radius;
                            float bump = exp(-dot(rel, rel) / spread) * fade;
                            tilt += rel / spread * bump * radius * 1.6;
                            darken = max(darken, bump * 0.25);
                        }
                        else
                        {
                            // A crater: a pit and a raised rim round it, fading as it ages. The rim's outer slope faces
                            // out and its inner slope (the pit's wall) in, as a bump's normals do.
                            float fade = saturate(1 - b.y);
                            float r = length(rel);
                            float rimAt = radius * 1.6, rimWidth = radius * 0.5;
                            float rim = exp(-(r - rimAt) * (r - rimAt) / (rimWidth * rimWidth));
                            tilt += rel / max(r, 1e-3) * rim * (r - rimAt) / rimWidth * 2.0 * fade;
                            darken = max(darken, ((1 - smoothstep(radius * 0.6, rimAt, r)) * 0.6 + rim * 0.2) * fade);
                        }
                    }
                }
                return tilt;
            }

            // W-113 (item 16): the SaltFlat colossus's furrow behind its rolling salt ball (SaltCrystalRoll), drawn into
            // the ground the way the worms' marks are rather than as a mesh lying on it. The ball rolls round a circle,
            // so the furrow is an arc: x, z its centre, the circle's radius, the furrow's half width (0: none);
            // the ball's angle, its direction (+1 or -1), the arc behind it (radians), how deep (m); and two whole-turn
            // width waves (counts, phases), so it presses in wider and narrower at the same spots for every player.
            float4 _PunchySaltTrail;
            float4 _PunchySaltTrailRun;
            float4 _PunchySaltTrailWave;

            // The furrow's height across it (x: -1..1 edge to edge): a pressed-in floor between two raised lips.
            float SaltFurrowHeight(float x, float depth)
            {
                float ax = abs(x);
                float lip = (ax - 0.72) / 0.2;
                return depth * (0.65 * exp(-lip * lip) - (1 - smoothstep(0.3, 0.62, ax)));
            }

            // The furrow at p: its tilt for the normal, and how much of the dark dirt under the salt it shows.
            float2 SaltFurrow(float2 p, out half dirt)
            {
                dirt = 0;
                float2 tilt = 0;
                float2 rel = p - _PunchySaltTrail.xy;
                float r = length(rel);
                // (Only near the circle; then only along the arc behind the ball, 0 at the ball to 1 at the tail.)
                [branch] if (abs(r - _PunchySaltTrail.z) < _PunchySaltTrail.w * 1.6)
                {
                    float a = atan2(rel.y, rel.x);
                    float t = frac((_PunchySaltTrailRun.x - a) * _PunchySaltTrailRun.y / 6.2831853) * 6.2831853 / _PunchySaltTrailRun.z;
                    [branch] if (t < 1)
                    {
                        float w = _PunchySaltTrail.w * (1 + 0.15 * sin(_PunchySaltTrailWave.x * a + _PunchySaltTrailWave.z)
                                                          + 0.09 * sin(_PunchySaltTrailWave.y * a + _PunchySaltTrailWave.w));
                        float x = (r - _PunchySaltTrail.z) / w;
                        // The salt creeps back in towards the tail: shallower, then gone (soft at both ends, no hard cut).
                        float fade = (1 - 0.7 * t) * smoothstep(1, 0.8, t) * smoothstep(0, 0.02, t);
                        float depth = _PunchySaltTrailRun.w * fade;
                        const float e = 0.05;
                        float slope = (SaltFurrowHeight(x + e, depth) - SaltFurrowHeight(x - e, depth)) / (2 * e * w);
                        float ax = abs(x);
                        dirt = ((1 - smoothstep(0.3, 0.62, ax)) * 0.8 + exp(-(ax - 0.5) * (ax - 0.5) / 0.01) * 0.2) * (1 - pow(saturate(t), 0.8));
                        tilt = -rel / max(r, 1e-3) * slope * 1.6;
                    }
                }
                return tilt;
            }

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
                half4  surface    : TEXCOORD2;
                half4  extra      : TEXCOORD3;
                half3  emission   : TEXCOORD4;
                half   fogFactor  : TEXCOORD5;
                half   bump       : TEXCOORD6;
                float  cave       : TEXCOORD7;
                half   strata     : TEXCOORD8;
                half2  paving     : TEXCOORD9;   // paving amount, flagstones (W-108)
                half2  hollow     : TEXCOORD10;  // hollow depth (m), porosity (item 22)
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // Fine relief: a three-octave height noise (swells ~3 m, clods ~0.7 m, grain ~0.2 m), each weighted by how much
            // of it is left at this distance (w: x 3 m, y 0.7 m, z 0.21 m), centred on 0. An octave that has faded out
            // costs nothing. The 3 m octave is taller in the slope (s) than in the colour, so far ground keeps its relief
            // without blotching.
            float2 MicroHeight(float2 p, float3 w)   // x for the slope, y for the colour
            {
                float h = 0, swell = 0;
                [branch] if (w.x > 0.001) swell = (PunchyValueNoise(p / 3.0 + 41.7) - 0.5) * w.x;
                [branch] if (w.y > 0.001) h += (PunchyValueNoise(p / 0.7) - 0.5) * 0.6 * w.y;
                [branch] if (w.z > 0.001) h += (PunchyValueNoise(p / 0.21 + 17.3) - 0.5) * 0.4 * w.z;
                return float2(h + swell * 1.6, h + swell * 0.4);
            }

            // How much of each fine-relief octave (x 3 m, y 0.7 m, z 0.21 m) is left at this distance: each fades from 4 to
            // 2 reference pixels of its own wavelength (DetailPolicy.ScreenSize), before it could shimmer.
            float3 MicroFades(float distance, float detailFar)
            {
                [branch] if (_PunchyTerrainDetailPpm < 0)
                {
                    half old = 1 - smoothstep(detailFar * 0.36, detailFar, distance);
                    return float3(0, old, old);
                }
                float ppm = _PunchyTerrainDetailPpm > 0 ? _PunchyTerrainDetailPpm : 935 * detailFar / 110;
                float3 pixels = float3(3.0, 0.7, 0.21) * ppm / max(distance, 0.01);
                return saturate((pixels - 2) / 2);
            }

            // Cobbles (W-34; rows of rounded boxes in W-09/W-10): rugged field stones, no grid. A jittered Voronoi on a
            // 0.42 m lattice with a size weight per stone (multiplicatively weighted, so the borders bow and stones
            // run from about 0.25 to 0.6 m), its input warped at two scales so the outlines wander. Each stone is a
            // dome that rises over 6-16 cm from its edge, with its own height, a lean and a lumpy top; between them,
            // gaps of varying width.
            uint CobbleHash(int2 c)
            {
                uint h = (uint)c.x * 73856093u ^ (uint)c.y * 19349663u;
                h = (h ^ (h >> 13)) * 1274126177u;
                return h ^ (h >> 16);
            }

            // Height of the paving at p in metres (0 in the gaps, up to about 4 cm on a stone); id = the stone's hash,
            // edge = distance into the stone (m, about; <= 0 in the gap).
            float CobbleHeight(float2 p, out uint id, out float edge)
            {
                float2 pw = p + (float2(PunchyValueNoise(p * 2.3), PunchyValueNoise(p * 2.3 + 7.7)) - 0.5) * 0.14
                              + (float2(PunchyValueNoise(p * 7.1 + 3.1), PunchyValueNoise(p * 7.1 + 9.4)) - 0.5) * 0.045;
                float2 q = pw / 0.42;
                float2 cell = floor(q);
                float2 f = q - cell;
                float d1 = 1e9, w1 = 1;
                float2 o1 = 0;
                float ds[9];
                id = 0;
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    uint h = CobbleHash((int2)cell + int2(x, y));
                    float2 jit = float2(h & 1023u, (h >> 10) & 1023u) / 1023.0;
                    float w = 0.62 + ((h >> 20) & 255u) / 255.0 * 0.76;
                    float2 o = float2(x, y) + 0.15 + jit * 0.7 - f;
                    float d = length(o) / w;
                    ds[(y + 1) * 3 + x + 1] = d;
                    if (d < d1) { d1 = d; w1 = w; id = h; o1 = o; }
                }
                // Distance to the border as a smooth minimum over every neighbour (not just the second nearest), so
                // where two borders meet the corner is rounded off: pebbles, not tiles.
                float sum = 0;
                [unroll] for (int k = 0; k < 9; k++)
                    sum += ds[k] > d1 ? exp(-(ds[k] - d1) / 0.09) : 0;
                float border = -0.09 * log(max(sum, 1e-6));
                // Gap half-width 1.5-4 cm, wandering along the joints.
                float gap = 0.015 + PunchyValueNoise(p * 3.7 + 1.3) * 0.025;
                edge = border * 0.42 * 0.5 * w1 - gap;
                float rnd = ((id >> 8) & 255u) / 255.0;
                float dome = saturate(edge / (0.07 + rnd * 0.1));
                dome = sqrt(dome * (2 - dome));   // a rounded shoulder (a quarter circle) up from the gap
                float lean = dot(o1, float2(((id >> 3) & 15u) / 15.0 - 0.5, ((id >> 13) & 15u) / 15.0 - 0.5)) * 0.35;
                float lumps = (PunchyValueNoise(p * 9.0 + rnd * 31) - 0.5) * 0.35 + (PunchyValueNoise(p * 23.0) - 0.5) * 0.12;
                return 0.05 * dome * (0.6 + ((id >> 16) & 15u) / 15.0 * 0.6 + lean + lumps);
            }

            // The paving at p: x = the stone's shade (-0.5..0.5), y = gap share (1 = gap), zw = slope (dh/dx, dh/dz).
            // hue: a warm/cool tint per stone (-0.5..0.5); dome: 0 at a stone's rim to 1 on top (dark rims).
            // W-108 (#39): about one stone in thirty is missing (its hole shows the gap's earth and grass) and one in
            // fifteen has sunk (flatter, darker, a puddle of shade); sunk = 1 for those.
            float4 Cobbles(float2 p, out half hue, out half dome, out half sunk)
            {
                uint id; float edge;
                const float e = 0.015;
                float h = CobbleHeight(p, id, edge);
                uint idx, idz; float ex, ez;
                float hx = CobbleHeight(p + float2(e, 0), idx, ex);
                float hz = CobbleHeight(p + float2(0, e), idz, ez);
                uint tone = CobbleHash(int2((int)id, 91));   // (id's low bits also set the jitter)
                hue = (tone & 255u) / 255.0 - 0.5;
                dome = saturate(h / 0.03);
                float shade = ((tone >> 8) & 255u) / 255.0 - 0.5;
                float mortar = 1 - smoothstep(-0.004, 0.006, edge);
                uint fate = (tone >> 16) & 63u;
                float2 slope = float2(hx - h, hz - h) / e;
                sunk = fate >= 2u && fate < 6u ? 1 : 0;
                if (fate < 2u)
                {
                    mortar = 1;
                    slope *= 0.2;
                }
                slope *= 1 - 0.65 * sunk;
                return float4(shade, mortar, slope);
            }

            // -0.5..0.5 per cell of p (an integer hash: far from the origin a sin hash breaks up).
            half Grain(float2 p)
            {
                uint2 cell = (uint2)(int2)floor(p);
                uint h = cell.x * 73856093u ^ cell.y * 19349663u;
                h = (h ^ (h >> 13)) * 1274126177u;
                return (h >> 22) / 1023.0 - 0.5;
            }

            // Raindrop rings on puddles: Water.shader's RainRipples (a ring per jittered cell at a random phase), so
            // puddles and rivers ripple alike in the rain.
            float2 PuddleRings(float2 p, float t)
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

            float3 MicroDetail(float2 p, float3 w)   // xy = slope (dh/dx, dh/dz), z = height (0.5 = level)
            {
                const float e = 0.04;
                float2 h = MicroHeight(p, w);
                return float3((MicroHeight(p + float2(e, 0), w).x - h.x) / e, (MicroHeight(p + float2(0, e), w).x - h.x) / e, 0.5 + h.y);
            }

            Varyings Vert(TerrainAttributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);

                VertexPositionInputs pos = GetVertexPositionInputs(TerrainPositionOS(input));
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.surface = input.surface;
                o.extra = input.extra;
                o.emission = input.emission.rgb;
                o.fogFactor = ComputeFogFactor(pos.positionCS.z);
                o.bump = input.morph.z;
                o.cave = input.morph.w;
                o.strata = input.strata.x;
                o.paving = input.strata.yw;
                o.hollow = half2(input.strata.z, input.soak.x);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                TerrainCaveClip(input.cave);

                float distance = length(input.positionWS - GetCameraPositionWS());
                half3 normalWS = NormalizeNormalPerPixel(input.normalWS);
                float2 p = input.positionWS.xz;
                half burrowDarken = 0;
                // (Worms' marks only out to _PunchyBurrowRange, as far as the feeder sends them, and only in the mask's
                // cells a mark reaches into: the rest of the terrain skips the loop.)
                [branch] if (_PunchyBurrowerCount > 0 && distance < _PunchyBurrowRange && NearBurrowMark(p))
                {
                    float2 burrowTilt = BurrowMarks(p, burrowDarken);
                    normalWS = normalize(normalWS + half3(burrowTilt.x, 0, burrowTilt.y));
                }
                // (The salt ball's furrow, W-113: only near its circle, out to the marks' range.)
                half saltDirt = 0;
                [branch] if (_PunchySaltTrail.w > 0 && distance < _PunchyBurrowRange * 1.5)
                {
                    float2 furrowTilt = SaltFurrow(p, saltDirt);
                    normalWS = normalize(normalWS + half3(furrowTilt.x, 0, furrowTilt.y));
                }

                // Fine relief, skipped on skirts. R-6: each octave fades by its own on-screen size (MicroFades), so the
                // clods carry on to about 300 m and a 3 m swell to about 1.4 km at Detail 1; detailFade (the old fixed
                // range, W-101: 110 m at Detail 1) still bounds the sand grains below.
                float detailFar = _PunchyTerrainDetailFar > 0 ? _PunchyTerrainDetailFar : 70;
                half detailMask = input.bump * (input.extra.w <= 0.01);
                half detailFade = (1 - smoothstep(detailFar * 0.36, detailFar, distance)) * detailMask;
                float3 octaves = MicroFades(distance, detailFar) * detailMask;
                float3 micro = max(octaves.x, max(octaves.y, octaves.z)) > 0.001 ? MicroDetail(p, octaves) : float3(0, 0, 0.5);

                // Rain: flat ground collects puddles (dark, mirror-smooth, no relief); elsewhere wet ground just
                // darkens and gets a soft sheen. Grass and snow don't glaze. Far away the sheen fades, since at
                // that distance it would only read as a bright smear.
                half wet = PunchyWetnessAt(p).r * (1 - saturate(input.extra.z * 2));
                // Shores: ground just above the water stays wet, a dark band with an uneven upper edge. The mesh
                // stores it as a negative skirt drop (surface vertices leave that channel 0; skirts are positive).
                half shore = 0;
                [branch] if (input.extra.w < -0.001)
                    // (W-43b: a broad wander as well as the fine one, so the band's edge breaks up along a whole reach.)
                    shore = smoothstep(0.2, 0.75, saturate(-input.extra.w) + (PunchyValueNoise(p * 0.8) - 0.5) * 0.35
                                                + (PunchyValueNoise(p * 0.13 + 2.9) - 0.5) * 0.5)
                          * (1 - saturate(input.extra.z * 2));
                // Item 22: only level ground pools (under about 10 degrees; the vertex normal, so fine relief can't
                // tilt a flat into a slope or a slope into a flat).
                half flatness = smoothstep(0.975, 0.99, normalize(input.normalWS).y);
                half puddleNoise = PunchyValueNoise(p / 2.3) * 0.65 + PunchyValueNoise(p / 0.9 + 5.1) * 0.35;
                // W-98: puddles gather in the dips (the mesh's hollow depth: how far below the ground round it, and
                // only where it rises on every side), the noise cutting them into separate pools with crisp edges (a
                // dip that glazes over whole reads as dark ground, not water), spreading as the rain goes on; none on
                // open flats. Item 22: porous ground (sand) soaks the rain up: its dips need far more to pool (only
                // the deepest, wettest spots, looking as anywhere else when they do), and it barely glazes, just
                // darkening (below).
                half porosity = input.hollow.y;
                half dip = smoothstep(0.0, 0.08, input.hollow.x) * (1 - 0.65 * porosity);
                half puddle = smoothstep(0.82, 0.9, dip * 0.4 + puddleNoise * 0.6 + wet * 0.15) * flatness * smoothstep(0.1, 0.3, wet)
                            * (1 - 0.6 * input.extra.y);   // grass pokes through, but meadow dips still pool
                // Spell chemistry (ChemistryOverlay.hlsl): water poured on land (Create Water, a Wave) stands as a sheet
                // that reads like a puddle on any slope, with a soft, wandering edge and damp ground round it; frozen
                // water on land is ice (both 0 with no bridge).
                half4 chem = PunchyChemAt(p);
                half chemWet = PunchyChem2At(p).r;
                half chemWater = PunchyChemSoft(chemWet, p);
                half chemIceRim;
                half chemIce = PunchyChemSoft(chem.b, p, chemIceRim);
                puddle = max(puddle, chemWater * (1 - chemIce));
                half sheen = max(wet * (1 - 0.85 * porosity), shore * 0.6) * (1 - 0.8 * input.extra.y) * (1 - smoothstep(120, 450, distance));

                half relief = 0.45 * (1 - puddle);   // (the octaves' fades are already in micro)
                normalWS = normalize(normalWS - half3(micro.x, 0, micro.y) * relief);
                half cobbleFade = smoothstep(0.3, 0.55, input.paving.x) * (1 - smoothstep(30, 80, distance));
                // W-108: paving above 1 is wear (SettlementSampling.Ground: the ruts down a cobbled road), and paving
                // a little under 1 (a road's verge, the fade at a town's edge) lets the grass in between the stones.
                half wear = saturate(input.paving.x - 1);
                half sparse = 1 - smoothstep(0.6, 1, input.paving.x);
                // W-108 (#39): a town's square is laid with big flagstones (the same stones at twice the size, flatter),
                // with a hard line where they meet the kerb.
                half slab = step(0.5, input.paving.y);
                float4 cb = float4(0, 0, 0, 0);
                half cbHue = 0, cbDome = 1, cbSunk = 0;
                [branch] if (cobbleFade > 0.001)
                {
                    cb = Cobbles(p * (1 - 0.5 * slab), cbHue, cbDome, cbSunk);
                    cb.zw *= 1 - 0.4 * slab;
                    cbDome = lerp(cbDome, sqrt(cbDome), slab);   // broad flat tops, only their edges darken
                    normalWS = normalize(normalWS - half3(cb.z, 0, cb.w) * cobbleFade * (1 - puddle * 0.7) * (1 - 0.6 * wear));
                }
                // Rock faces (steep ground) up close: the caves' shared grain and fine relief, read three ways, so cliffs
                // carry the same detail as cave walls.
                half rockShade = 1;
                half steep = (1 - smoothstep(0.55, 0.8, normalWS.y)) * (1 - smoothstep(40, 90, distance)) * (input.extra.w <= 0.01);
                // W-112: round a cave mouth (its cut field under 8 m) any ground takes the walls' grain at the walls' own
                // scale, so the terrain doesn't turn smooth where it meets the finer cave rock. Only there: the rest of
                // the ground pays nothing.
                half mouth = (1 - smoothstep(1.5, 8, input.cave)) * (1 - smoothstep(30, 60, distance));
                steep = max(steep, mouth * 0.7);
                if (steep > 0.01 && _PunchyCaveDetailReady > 0)
                {
                    float3 q = input.positionWS * lerp(0.6, 1.0, mouth);
                    float3 tw = pow(abs(normalWS), 4);
                    tw /= tw.x + tw.y + tw.z;
                    half4 rd = SAMPLE_TEXTURE2D(_PunchyCaveDetail, sampler_PunchyCaveDetail, q.zy) * tw.x
                             + SAMPLE_TEXTURE2D(_PunchyCaveDetail, sampler_PunchyCaveDetail, q.xz) * tw.y
                             + SAMPLE_TEXTURE2D(_PunchyCaveDetail, sampler_PunchyCaveDetail, q.xy) * tw.z;
                    half2 rb = (rd.rg - 0.5) * 0.8 * steep;
                    normalWS = normalize(normalWS + float3(0, rb.x, rb.y) * tw.x + float3(rb.x, 0, rb.y) * tw.y + float3(rb.x, rb.y, 0) * tw.z);
                    rockShade = 1 + (rd.b - 0.5) * 0.6 * steep;
                }

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                // Skirts take their shadowing from the surface edge they hang from.
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS + float3(0, max(input.extra.w, 0), 0));
                inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
                inputData.bakedGI = SampleSH(inputData.normalWS);
                // Glowing plants and molten pools nearby light the ground round them.
                inputData.bakedGI += PunchyGlowLight(input.positionWS, inputData.normalWS) + PunchyGlowWorld(input.positionWS, inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                half3 albedo = input.surface.rgb * (1 + (micro.z - 0.5) * 0.5);   // grain in the colour too (faded in micro)
                // Up close, bare ground (sand, dirt, gravel) is grainy: single grains a shade lighter or darker, at two sizes
                // (about 3 and 7 cm, hashed by cell so they hold still), gone before they could shimmer, and not under grass.
                if (detailFade > 0.001)
                {
                    half grain = Grain(p * 37) * 0.6 + Grain(float2(p.x * 0.866 - p.y * 0.5, p.x * 0.5 + p.y * 0.866) * 15 + 11.3) * 0.4;   // turned: no grid
                    albedo *= 1 + grain * 0.5 * (1 - smoothstep(6, 22, distance)) * (1 - input.extra.y) * detailFade;
                }
                // Paved ground (settlement streets and squares): cobbles up close, fading to plain stone grey before
                // the gaps could shimmer; the stones' domes catch the light.
                [branch] if (cobbleFade > 0.001)
                {
                    // W-34: stones paler and greyer than the ground, each its own shade and a little warmer or cooler,
                    // darker at the rim where they turn down into the gap, with broad patches of wear so the paving
                    // isn't one even tone from 30 m. The gaps hold dark earth with tufts of moss and grass.
                    half grey = dot(albedo, half3(0.3, 0.5, 0.2));
                    half patch = PunchyValueNoise(p * 0.21 + 4.7) - 0.5;
                    half3 stone = lerp(albedo, grey.xxx, 0.45) * (1.2 + cb.x * 0.55 + patch * 0.25)
                                * half3(1 + cbHue * 0.16, 1 + cbHue * 0.03, 1 - cbHue * 0.16) * lerp(0.6, 1, cbDome);
                    // W-108: worn stones in the ruts are rubbed flat and smooth: greyer, a touch darker, their rims
                    // less dark; sunk stones sit in shade with dirt washed over them.
                    stone = lerp(stone, grey.xxx * half3(0.86, 0.84, 0.8) * (0.9 + cb.x * 0.2) * lerp(0.9, 1, cbDome), wear);
                    stone *= lerp(1, half3(0.62, 0.58, 0.52), cbSunk);
                    half tuft = smoothstep(0.5 - sparse * 0.3, 0.72 - sparse * 0.2,
                                           PunchyValueNoise(p * 2.6 + 12.1) * 0.7 + PunchyValueNoise(p * 9.3) * 0.3)
                              * (1 - wear);   // nothing grows where the wheels run
                    half3 gap = lerp(albedo * half3(0.34, 0.29, 0.22), half3(0.10, 0.16, 0.05), tuft);
                    gap = lerp(gap, albedo * half3(0.62, 0.52, 0.4), wear);   // packed dust tamped into the joints
                    albedo = lerp(albedo, lerp(stone, gap, cb.y), cobbleFade);
                }
                // Rock strata (mesa cliffs, canyon walls): bands of darker red and paler cream rock by height, a thick
                // set and a thin one, wandering a little along the cliff. Broad enough to read from far off.
                [branch] if (input.strata > 0.01)
                {
                    float y = input.positionWS.y + (PunchyValueNoise(p * 0.02) - 0.5) * 6 + (PunchyValueNoise(p * 0.11) - 0.5) * 1.2;
                    half band = PunchyValueNoise(float2(y / 4.5, 7.1)) * 0.7 + PunchyValueNoise(float2(y / 1.1, 3.3)) * 0.3;
                    albedo *= lerp(1, lerp(half3(0.55, 0.45, 0.42), half3(1.45, 1.3, 1.08), smoothstep(0.3, 0.7, band)), input.strata);
                    // The rock grain's vertical streaks would drown the bands: mostly the bands, then.
                    rockShade = lerp(rockShade, 1, input.strata * 0.7);
                }
                albedo *= rockShade;
                albedo *= 1 - burrowDarken;
                albedo = lerp(albedo, half3(0.33, 0.26, 0.20), saltDirt);
                // Under the grass blades (near the camera, where they're drawn) the ground takes their colour, so the
                // gaps between blades read as more grass rather than bare soil (GroundDetailSystem.GroundTint).
                half underGrass = input.extra.y * _PunchyGrassTint.x * (1 - smoothstep(_PunchyGrassTint.y * 0.7, _PunchyGrassTint.y, distance));
                albedo = lerp(albedo, albedo * half3(1.05, 1.16, 0.85), underGrass * 0.8);
                // Far grass (Zach, 11-08: a field shouldn't stop moving where the blades stop): past where the blades are
                // drawn, grassy ground carries the same gusts they show (the same noise, scrolled downwind at the same
                // rate, GroundDetail.hlsl), lighter where a gust bows the grass and darker between, in broad rolling bands.
                half farGrass = input.extra.y * smoothstep(_PunchyGrassTint.y * 0.6, _PunchyGrassTint.y, distance);
                [branch] if (farGrass > 0.01 && _PunchyCaveDetailReady > 0)
                {
                    float2 gustUV = p * 0.011 - _PunchyWindDirection * _PunchyClock * (0.025 + 0.06 * _PunchyWind);
                    half gust = smoothstep(0.3, 0.75, SAMPLE_TEXTURE2D_LOD(_PunchyCaveDetail, sampler_PunchyCaveDetail, gustUV, 0).b);
                    albedo *= 1 + (gust - 0.5) * 0.3 * farGrass * (0.4 + _PunchyWind);
                }
                albedo *= lerp(1, 0.72 - 0.12 * porosity, wet);   // (sand drinks it in: darker, not shinier)
                // The wet band is patchy (W-43b): dark mud in some stretches, grey shingle and stones in others, so the
                // bank reads as worn earth rather than a painted stripe.
                [branch] if (shore > 0.01)
                {
                    half bandMix = smoothstep(0.35, 0.7, PunchyValueNoise(p * 0.21 + 8.3) * 0.75 + PunchyValueNoise(p * 1.9) * 0.25);
                    half3 shingle = lerp(albedo, dot(albedo, half3(0.3, 0.5, 0.2)).xxx * 1.35, 0.7) * (1 + Grain(p * 6) * 0.5);
                    albedo = lerp(albedo, shingle, bandMix * shore * (1 - input.extra.y));
                }
                albedo *= lerp(1, 0.55, shore * (1 - 0.4 * input.extra.y));
                // Spell chemistry (ChemistryOverlay.hlsl): scorched ground where grass burned off, white frost where it
                // glazed over, damp ground round poured water, ice, and burning ground glows below (soft edges, no
                // change with no bridge).
                albedo = lerp(albedo, half3(0.045, 0.04, 0.035) + albedo * 0.12, PunchyChemSoft(chem.g, p) * 0.9);
                albedo = lerp(albedo, half3(0.78, 0.86, 0.93), PunchyChemSoft(chem.a, p) * 0.75);
                albedo *= lerp(1, 0.6, saturate(chemWet * 2.5));
                albedo = lerp(albedo, lerp(half3(0.62, 0.78, 0.88), half3(0.88, 0.94, 0.98), PunchyChemNoise(p * 0.35) * 0.6 + chemIceRim * 0.4), chemIce);
                surface.albedo = lerp(albedo, albedo * 0.6, puddle);
                surface.smoothness = lerp(lerp(lerp(input.surface.a, max(input.surface.a, 0.45), max(sheen, saturate(chemWet * 2.5))), 0.92, puddle), 0.8, chemIce);
                surface.metallic = input.extra.x;
                surface.occlusion = 1;
                surface.emission = input.emission * _EmissionScale + PunchyChemFireGlow(chem.r, p, _PunchyClock) * 0.6;
                surface.alpha = 1;

                half4 color = UniversalFragmentPBR(inputData, surface);

                // Puddles read like the rivers (W-43b): looking down, a dark see-through film over the ground (a cool
                // tint, like the water body); at grazing angles the live sky, by the water's Fresnel, off a surface
                // that rain rings stir while it rains, plus the sun's glint. Was a flat 35-90% sky wash, which under
                // a rain sky read as plain grey.
                if (puddle > 0.01)
                {
                    float3 v = inputData.viewDirectionWS;
                    half rain = PunchyWetnessAt(p).g * (1 - smoothstep(20, 60, distance));
                    float2 ripple = rain > 0.01 ? PuddleRings(p, _PunchyClock) * rain * 0.35 : float2(0, 0);
                    float3 n = normalize(float3(-ripple.x, 1, -ripple.y));
                    float3 r = reflect(-v, n);
                    r.y = max(r.y, 0.03);
                    // (A shallow sheet mirrors more than deep water seen from above, so the floor is well over
                    // water's 2%: puddles must read as sky even when looked down on.)
                    half fresnel = 0.35 + 0.65 * pow(1 - saturate(dot(n, v)), 5);
                    half3 body = color.rgb * half3(0.42, 0.47, 0.5);
                    Light sun = GetMainLight(inputData.shadowCoord);
                    half glint = pow(saturate(dot(r, sun.direction)), 600) * sun.shadowAttenuation * 3;
                    half3 film = lerp(body, PunchySkyColor(r, false, true), fresnel) + sun.color * glint;
                    color.rgb = lerp(color.rgb, film, puddle);
                    // Spell chemistry: poured water is deeper than a rain puddle, so it takes a blue-green body
                    // instead of reading as a pale glaze (0 with no bridge).
                    color.rgb = lerp(color.rgb, color.rgb * half3(0.45, 0.62, 0.72), chemWater * (1 - chemIce) * 0.7);
                }

                // Cloud shadows and, past the real shadows, the wide cached ones of the huge things (R-5): dim the share
                // of the light that comes straight from the sun. The wide shadow takes the darker of itself and the real
                // shadow where they overlap, so it never doubles.
                float3 shadowAt = input.positionWS + float3(0, max(input.extra.w, 0), 0);
                half cloudShadow = PunchyCloudShadow(shadowAt);
                half wideShadow = PunchyWideShadow(shadowAt, inputData.normalWS);
                if (cloudShadow > 0 || wideShadow > 0)
                {
                    Light mainLight = GetMainLight(inputData.shadowCoord);
                    half sunlit = Luminance(mainLight.color) * saturate(dot(inputData.normalWS, mainLight.direction));
                    half direct = sunlit * mainLight.shadowAttenuation;
                    half kept = sunlit * min(mainLight.shadowAttenuation, 1 - wideShadow) * (1 - cloudShadow);
                    half ambient = Luminance(inputData.bakedGI);
                    color.rgb *= 1 - (direct - kept) / max(direct + ambient, 1e-3);
                }

                color.rgb = PunchyAtmosphere(color.rgb, input.positionWS);
                return color;
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
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "TerrainSurface.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float cave : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(TerrainAttributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                o.cave = input.morph.w;

                // Collapse skirts onto their top edge so they cast no shadow.
                float3 positionWS = TransformObjectToWorld(TerrainPositionOS(input) + float3(0, max(input.extra.w, 0), 0));
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                TerrainCaveClip(input.cave);
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
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "TerrainSurface.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float cave : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(TerrainAttributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                o.positionCS = TransformObjectToHClip(TerrainPositionOS(input));
                o.cave = input.morph.w;
                return o;
            }

            half Frag(Varyings input) : SV_Target
            {
                TerrainCaveClip(input.cave);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            #include "TerrainSurface.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3  normalWS   : TEXCOORD0;
                float  cave       : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(TerrainAttributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                o.positionCS = TransformObjectToHClip(TerrainPositionOS(input));
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.cave = input.morph.w;
                return o;
            }

            void Frag(Varyings input, out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                TerrainCaveClip(input.cave);
                outNormalWS = half4(NormalizeNormalPerPixel(input.normalWS), 0);
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }
    }
}
