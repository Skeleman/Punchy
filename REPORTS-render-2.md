
## R-23 r3 (#298), part A, editor-free: WIP render/one-surface-3@9795d6a3 (pasted by supervisor)
- Sky.hlsl split into SkyCommon / Clouds / Atmosphere / Sky. Lines moved as-is, line endings kept. 14 haze-only shaders include Atmosphere, Flame includes SkyCommon only.
- Not compiled yet. Next, in the editor: Refresh, ShaderHasError on 21 shaders, pixel-diff 8 shots vs main (part A must match), sparkle NaN 0, suite.
- Part B (drift fixes, one shader at a time): wide shadow on Creature/Colossus/Pebbles/SunkenTemple; PBR cloud dim becomes PunchySurfaceSunDim, so shade gets slightly brighter (a before/after pair for Zach). Grass/Water/Cave keep their look via flags.
- Compile time and variant count: BENCH job requested (rule 21).

## R-23 r3A DONE 11-16 (pasted by supervisor)
R-23 r3A #298 render/one-surface-3@9795d6a3: Sky.hlsl split into SkyCommon, Clouds and Atmosphere. 118 of 122 compiled stages are the same code; 4 are reordered `mad`s. Branch vs base pixels are within frame-to-frame noise at lake, village at night and temple. Compile time and variant count are for BENCH.

## R-23 r3 main merge 11-16, ready for PR
- render/one-surface-3@1fbf15bb merges origin/main. Only Colossus.shader conflicted (#476 uv3 family). Main's family path is kept, with Surface.hlsl lighting on top; the metal highlight takes lit.shadow. LF kept.
- Colossus compiles: 8 variants, 0 failed. The other moved shaders and scripts compile clean.
- Colossus, main vs merged (Clear, hour 14, frozen; noise 2151 px): 308k px differ, max 155. Most is the cloud dim, since local cloud lies over the colossus even in Clear: sunlit faces about 25% darker, shade slightly brighter. With the wide shadow off it is still 337k vs main, so the wide shadow is a small part (48k px, max 46). Shots: PunchyMilestones/R-23/row3/colossus-main.png, colossus-merged.png, colossus-merged-nowide.png.
- BENCH Job 104 now points at 1fbf15bb. Editor closed with EditorApplication.Exit.

## R-23 r4 editor-free part 11-16 (render/one-surface-4@329aebad off main 03b49287, WIP, unverified)
Fixed-metre fades in scope, and their DetailPolicy.ScreenSize mapping. The tuning view is Medium (Detail 1), 60 degrees and 1080 lines, so 935.3 reference px per metre at 1 m. "px/m" below is ScreenSize of 1 m at the fade's ends.
| Fade | Where | Metres today | Ref px/m (full -> gone) | At fov 90 |
|---|---|---|---|---|
| Fur strands | Creature.shader fur (`near`, smoothstep 3-9) | 3 -> 9 | 311.8 -> 103.9 | 1.7 -> 5.2 m |
| Feather/scale/skin/chitin/paw patterns | Creature.shader (`near`, 4-14) | 4 -> 14 | 233.8 -> 66.8 | 2.3 -> 8.1 m |
| Rock and bark relief, plants/rocks | Flora.shader `detailFade` (18-45) | 18 -> 45 | 52.0 -> 20.8 | 10.4 -> 26 m |
| Relief on built things | Flora.shader `detailFade`, built (60-140) | 60 -> 140 | 15.6 -> 6.7 | 34.6 -> 80.8 m |
| Leaf tint (added, the same kind) | Flora.shader `leafNear` (40-90) | 40 -> 90 | 23.4 -> 10.4 | 23.1 -> 52 m |
- How: new `Shaders/ScreenSize.hlsl`, with PunchyPixelsPerMetre = 540 x |P._m11| x Detail (each camera's own projection) plus PunchyScreenSize and PunchyReferenceDistance(d) = d x 935.3 / ppm. Each fade keeps its metre numbers but reads the reference distance, so at the tuning view it is the same expression (exact match, same curve), and elsewhere it holds the on-screen size. Detail comes from `_PunchyDetail`, set in DetailPolicy.SetView; unset reads as 1. Already screen-size, left alone: the creature gear and wing fades (fwidth footprint) and the bark grain (fwidth).
- Left out, out of scope (fixed metres still): Flora.hlsl breath/twitch vertex settle at 60-100 m (lines 184, 191), and FloraImpostor sheenFar at 30-120 m (a far look blend, not detail). Either can move the same way if wanted.
- Editor to-do when my turn comes: compile (0 failed) for Creature and Flora; NaN 0; shots at fov 60 (must match main) and fov 90 (fades at the same on-screen size); and the colossus with clouds off, to confirm the r3 darkening is the cloud dim.

## R-23 r4 update 11-16 (render/one-surface-4@3fefa203, WIP, unverified)
- Moved as asked: the Flora.hlsl breath/twitch settle (60-100 m) and the bark/rock sheenFar (30-120 m). Mesh (Flora.shader) and impostor moved together, so the R-11 hand-off still matches. Same file and same kind, also moved: cloth flutter (Flora.hlsl 30-80), glow shimmer (Flora.shader 80-100) and cactus cells (4-10).
- Vertex fades in Flora.hlsl run in shadow, depth, cache and wide passes too, whose projection isn't the view's. They use the new PunchyMainReferenceDistance, which reads `_PunchyViewPpm` (main camera ppm x Detail), so every pass agrees. Fragment fades keep each camera's own P._m11.
- SetView risk fixed inside DetailPolicy (render-owned, no player file). A RuntimeInitializeOnLoadMethod hooks RenderPipelineManager.beginContextRendering and sets `_PunchyDetail` and `_PunchyViewPpm` before every frame draws, and once at init. A Detail change in GraphicsSettings reaches shaders next frame with no SetView caller. If unset, the shader falls back to the tuning view.
- R-23 r5 (next row, for the board): TerrainSurface sheen/cobble/steep/mouth/grain/rain, Water rippleNear, HeatHaze shimmer/mirage. LandmarkEffects 600-750 is not mine: render's plume-reach WIP moves it.
- Editor to-do unchanged; also compile FloraImpostor (both passes) and check flutter/breath at fov 90.

## R-23 r5 editor-free part 11-16 (render/one-surface-5@a7e2c2b3 off one-surface-4, WIP, unverified)
Same method as r4: the metre numbers stay, but each fade reads PunchyReferenceDistance (exact at the tuning view, same on-screen size elsewhere). Both shaders are CRLF, kept.
| Fade | Metres | Ref px/m (full -> gone) | At fov 90 |
|---|---|---|---|
| TerrainSurface wet sheen | 120 -> 450 | 7.8 -> 2.1 | 69 -> 260 m |
| Cobbles | 30 -> 80 | 31.2 -> 11.7 | 17.3 -> 46 m |
| Rock-face grain (steep) | 40 -> 90 | 23.4 -> 10.4 | 23.1 -> 52 m |
| Cave-mouth grain | 30 -> 60 | 31.2 -> 15.6 | 17.3 -> 34.6 m |
| Sand/dirt grain | 6 -> 22 | 155.9 -> 42.5 | 3.5 -> 12.7 m |
| Puddle rain rings | 20 -> 60 | 46.8 -> 15.6 | 11.5 -> 34.6 m |
| Water reflection ripples (rippleNear) | 25 -> 110 | 37.4 -> 8.5 | 14.4 -> 63.5 m |
- Left as is: the grass tint (`_PunchyGrassTint.y`, the grass radius from GroundDetailSystem) and the octave, puddle and stone fades, which are already on ppm.
- **HeatHaze: pushback, left in metres.** The shimmer (15-220 m) and mirage (250-700 m) are physical: how much hot air the ray crosses, and the grazing angle at range. They are not detail LOD. A zoomed view of a mirage 500 m away should still show it, and its wobble is already sized in screen pixels. Also, that blit's projection isn't the camera's, so it could only use _PunchyViewPpm. I'll move it if you still want it moved.
- No orthographic camera draws terrain or water (grep), so per-camera P._m11 is safe in both ForwardLit passes.
- Editor to-do: compile TerrainSurface and Water (0 failed); fov 60 shots must match one-surface-4; fov 90 cobble, rock-face and ripple fades at the same on-screen size. Cost note for BENCH: at narrow fov, rock-face grain is drawn further out (and at wide fov less far).

## R-23 r4+r5 verified in editor 11-16 (render/one-surface-5@9701f8a3, includes one-surface-4)
- Compile: 40 variants, 0 failed; all 30 game shaders compile with no errors (Creature, Flora, FloraImpostor, TerrainSurface, Water included).
- NaN/inf/negative pixels: 0 in all 18 shots (float render target), on both branch and main.
- Method: Detail set to 1 (the editor had 1.5 from the High preset), so viewPpm is 935.3 at fov 60 and 540 at fov 90. Clouds off and time frozen in every shot. Four views (village, cave mouth, lake in rain, colossus) at fov 60, plus fov 90 from the same target at 0.577 of the distance. Branch shaders against main's six shaders swapped in, in the same editor session.
- Noise floor (village60 retaken at the end of the run): mean 1.9 on branch, 3.3 on main.
- fov 60, main vs branch: village 2.2 (noise, so exact as designed). Cave 7.5, lake 4.2 and colossus 11.1 are above noise. Likely cause: drift from things that animate on Time (colossus pose, flora breath/sway, rain on water), which freeze at slightly different times on each run. **Not confirmed.** A frozen-time pair on one view would settle it, in a later editor turn.
- fov 90, main vs branch: village 2.9, cave 0.7, lake 8.6, colossus 13.6. That is expected, because at fov 90 the fades end sooner in metres (same on-screen size: reference distance = d x 935.3 / ppm, and ppm at fov 90 is 540). Lake and colossus also carry the drift above.
- Colossus clouds-off (the r3 darkening check): not settled. The r3 shots use a different view, so their brightness can't be compared with these, and the clouds-on set came from a different weather state. It needs a same-view clouds on/off pair on main.
- Shots: PunchyMilestones/R-23/r45/branchD1 and mainD1 (Detail 1, clouds off). Detail 1.5 sets are in branch2 and main. Editor closed with EditorApplication.Exit; release tree clean.

## BENCH request R-23 r4+r5: filed as Job 109 (11-16)
### Job (render-2 lane, R-23 r4+r5 #298, branch render/one-surface-5 9701f8a3, includes one-surface-4): detail fades on screen size
- r4: the Creature fur/pattern and Flora relief/leaf/sheen/glow/cell fades read PunchyReferenceDistance (each camera's projection x Detail). The Flora.hlsl breath, twitch and flutter read PunchyMainReferenceDistance (`_PunchyViewPpm`). DetailPolicy sets `_PunchyDetail` and `_PunchyViewPpm` from beginContextRendering each frame. r5: TerrainSurface sheen/cobble/rock-face/cave-mouth/grain/rain and Water rippleNear do the same. Per-pixel cost added: one multiply and one divide per fade. At fov 60 the fade distances match main; elsewhere they move. Rule 21: the lane did not time it.
- Reach note: at a narrow fov (zoom) the gated branches reach further, at a wide fov less far. Cobbles (`cobbleFade > 0.001`) and the rock-face/cave-mouth grain (`steep > 0.01`, cave detail sampling) are the costly ones. At fov 90 the rock-face grain ends at 52 m instead of 90 m; at fov 40 it reaches about 141 m.
- Arms: main (A) against render/one-surface-5 (B), editor on each tree, ShaderCache (Library/ShaderCache) cleared first, -fpscap 0.
- Record:
  - full shader import time after the cache clear, and the variant count for Creature, Flora, FloraImpostor, TerrainSurface and Water;
  - frame time at fov 60 and at fov 90, standing at the village (117,-470) with deer spawned, at a cliff face with a cave mouth in view, and at a lakeshore at noon in rain (puddles, ripples);
  - one fov 40 sample at the cliff, for the rock-grain reach.
- Expect fov 60 to be within noise of A, and fov 90 to be equal or cheaper. Report only; nothing fails.

## R-35 row 1 (#333) 11-16: reference cumulus vs our clouds, audit and plan (editor-free, no code)
Compared the reference (MIT @freeborn, PunchyReferences: a full-res, 500-step analytic march) with our cloud system on main fce56e84: PunchyCloudMarch (Sky.hlsl), PunchyCloudDensityFine and PunchyCloudShadow (Clouds.hlsl), the low-res buffer (CloudsLowRes.shader), and the baked 3D noise (CloudNoise.cs: r shape, g Worley detail, b wispy fBm, **a unused**). Cost baseline is job 79: clouds take 0.0-0.4 ms GPU at Medium and 0.4-0.8 at Ultra. That is small, so this row is mostly about look; the cost ideas mainly matter as the price of the look picks.

| Reference feature | Ours today | Cost to us | Kind | Verdict |
|---|---|---|---|---|
| Cauliflower lobes: hemisphere caps on Worley points, added outward, 2-3 sizes, smoother lower body | Worley `g` *carves* edges (erosion), so tops read eroded rather than domed | Live: 8 hashes x 3 octaves per sample, too costly. Baked into CloudNoise `a`: 0 extra reads, because the erosion fetch is already float4 | look | **Pick A** (baked) |
| Billowed fine lumps, drifting up so the detail boils | The `fine` octave (uvwFine) near the camera, 2.5-6 km | Folded into A (bake \|n\| billow into the same channel's mix) | look | in A |
| Multiple-scattering octaves (3 Beer terms, flatter phase) | One floor: `max(beer, exp(-0.25 sigma od) * 0.55)` | 2 exp per lit step, no reads | look | **Pick B** |
| Dual-lobe phase / silver lining | Have it: HG(-0.2)/HG(0.65) blend plus `silver` and `lining` | - | - | already have |
| Sky ambient from a short upward occlusion test | Height ramp `lerp(0.6, 1.1, height)`, so overhangs and gaps between towers can't darken | 1 extra detail-0 density sample per lit step, under the same reuse gate as the light samples (`lastOpticalDepth`) | look | **Pick C** (trial; price it) |
| First sun step at full detail (lumps shade each other) | All 4 light samples at detail 0 | +1 erosion read on the 90 m sample, near clouds only (`fine > 0`) | look | **Pick D** (small) |
| Octave count from pixel footprint | Detail fades in **fixed metres** (full to 3.5 km, gone 17.5 km; fine 2.5-6 km), against rule 20 | 1 mul + 1 div; same at the tuning view, moves with fov/Detail | rule 20 / cost | **Pick E** (on regardless, like R-23 r4/r5) |
| Geometric sun steps | Have it: 90/360/810/1440 m, merged with distance (R-8) | - | - | already have |
| Hierarchical empty-space skipping | Have it: empty-air and thin strides plus back-up (job 32) | - | - | already have |
| Early exit at low transmittance | Have it: 0.02 | - | - | already have |
| One tower per jittered cell, wide base narrowing up | Weather/shape maps drive cover; towers come from the profile and the R-12 warp | 9-cell loop per sample, and it would fight the weather maps | look | **Pushback:** skip; A gives the turret look within our maps |
| Highlight-compressing tonemap | URP tonemap for the whole frame | - | - | skip (global look, Zach's call) |

- Rule 33 (one cloud system): A and D only touch the detail>0 path of PunchyCloudDensityFine, so the ground cloud shadow and reflection cover (detail 0) stay matched. B and C only change the march light. Nothing forks.
- MIT: any adapted code (the cap formula in CloudNoise.cs) keeps the reference's copyright line and MIT notice in that file's header.
- Ownership to confirm: CloudNoise.cs (Scripts/Weather) has no listed owner. Switches use new free `-skyoff` bits (4096 A, 8192 B, 16384 C, 32768 D, default off). DebugControls passes the mask through, so no fauna file is touched.

| Row | Do | Done when |
|---|---|---|
| 1 | This audit, with picks A-E | Supervisor confirms the picks |
| 2 | Trial on render/cloud-ideas (new off main): bake A into CloudNoise `a`; B, C and D in Sky.hlsl/Clouds.hlsl, each behind its bit; E on (fixed metres move to PunchyReferenceDistance, exact at fov 60 Detail 1) | Compile 0 failed, NaN 0. Sheet: cumulus clear sky, towering cloud, low sun into the light, overcast; current vs each pick vs all, ground and peak; 6-frame fly-through. BENCH below. Zach picks |
| 3 | Ship Zach's picks on by default, E kept | Before/after; BENCH at or under today's cloud cost or Zach accepts the added cost; NaN 0 |

### DRAFT BENCH request, R-35 row 2 (for the coordinator to file when row 2 lands; not in BENCH-QUEUE)
#### Job (render-2 lane, R-35 r2 #333, branch render/cloud-ideas <sha>): cloud look picks, cost per pick
- Arms (all on the branch build, by `-skyoff` mask): base 0; A 4096; B 8192; C 16384; D 32768; all 61440; clouds off 3 (the cloud share, as job 79). Also main for the E check (E is always on in the branch).
- Profile build, 1920x1080, Medium and Ultra, uncapped, noon, arms rotated, 3 rounds, 300 frames.
- Views: job 79's peak (-3460,2425 facing west) in Clear and in Overcast; plus a cumulus-heavy view (PartlyCloudy preset, looking up about 20 degrees from the same peak).
- Record: GPU ms per arm and view; cloud share = arm minus clouds-off; main vs branch base, to confirm E is within noise at fov 60.
- Expect A, B and D within noise. C is the one to price (one extra density sample per lit step). Report only.
