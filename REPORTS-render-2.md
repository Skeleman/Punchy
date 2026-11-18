
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
| 2 | Trial on render/cloud-picks (new off main): bake A into CloudNoise `a`; B, C and D in Sky.hlsl/Clouds.hlsl, each behind its bit; E on (fixed metres move to PunchyReferenceDistance, exact at fov 60 Detail 1) | Compile 0 failed, NaN 0. Sheet: cumulus clear sky, towering cloud, low sun into the light, overcast; current vs each pick vs all, ground and peak; 6-frame fly-through. BENCH below. Zach picks |
| 3 | Ship Zach's picks on by default, E kept | Before/after; BENCH at or under today's cloud cost or Zach accepts the added cost; NaN 0 |

### DRAFT BENCH request, R-35 row 2 (for the coordinator to file when row 2 lands; not in BENCH-QUEUE)
#### Job (render-2 lane, R-35 r2 #333, branch render/cloud-picks b7a1f7cc): cloud look picks, cost per pick
- Arms (all on the branch build, by `-skyoff` mask): base 0; A 4096; B 8192; C 16384; D 32768; all 61440; clouds off 3 (the cloud share, as job 79); E off 65536 (fades in metres, the branch with E taken out). Also main for the E check. The branch carries #494 (merged in for ScreenSize.hlsl), so main vs branch base also includes #494's fades; the 0 vs 65536 pair isolates E.
- Profile build, 1920x1080, Medium and Ultra, uncapped, noon, arms rotated, 3 rounds, 300 frames.
- Views: job 79's peak (-3460,2425 facing west) in Clear and in Overcast; plus a cumulus-heavy view (PartlyCloudy preset, looking up about 20 degrees from the same peak).
- Record: GPU ms per arm and view; cloud share = arm minus clouds-off; main vs branch base, to confirm E is within noise at fov 60.
- Expect A, B and D within noise. C is the one to price (one extra density sample per lit step). Report only.

## R-35 row 2 (#333) 11-16: picks A-E written, editor-free (unverified, needs editor)
Branch render/cloud-picks b7a1f7cc (new off main 25558ed8). The BENCH draft above now has this sha.
- A, -skyoff 4096: the cloud noise's unused alpha now holds dome lobes, x / sqrt(x + 0.04) with x = 1 - F1²/0.36 over Worley cells at 8, 16 and 32, plus small |Perlin| bumps. It is stored as erosion and remapped to g's mean (0.48), so overall erosion is about the same (checked offline on a 32³ sample). Clouds.hlsl reads it in place of g on billowy tops, in both octaves. The MIT notice (SPDX line, copyright @freeborn, licence URL) is in the CloudNoise.cs header.
- B, 8192: the light's soft*phase becomes beer*phase + (0.5 e^-0.25od + 0.25 e^-0.06od) * lerp(phase, 1, 0.6), times 0.6, so a thin edge keeps today's brightness and bodies glow deeper. The MIT notice is in the Sky.hlsl header.
- C, 16384: one density sample 300 m straight up, taken only when the sun samples are taken (reused otherwise). Ambient is multiplied by exp(-0.6 sigma od), and the height ramp is softened to 0.8-1.1.
- D, 32768: the 90 m sun sample reads edge detail at the step's own detail level. It fades out with mergeFirst by 6 km.
- E, on by default: the detail and fine fades read PunchyMainReferenceDistance(t). That is exact at fov 60 Detail 1, and the same in every pass. -skyoff 65536 puts back fixed metres for same-tree pairs.
- Shadows and cover (PunchyCloudShadow, CoverAlong) read detail 0, so A and D don't change them.
- Dependency: E needs ScreenSize.hlsl, which only exists on render/one-surface-5 (#494). I merged origin/render/one-surface-5 into this branch, so #494 must merge before this branch.
- Editor turn (on the coordinator's ping):
  1. #494's frozen-time drift pair at cave, lake and colossus, and the colossus clouds on/off pair from one view.
  2. Compile 0 failed, NaN 0.
  3. From one cumulus view, at noon and at golden hour, shots with each of A-D on and off, and with E on and off (65536). Plus fov 60 Detail 1 branch vs main for E.
  4. Close with EditorApplication.Exit.

## R-35 row 2 (#333) + R-23 r4+r5 drift pair (#298) 11-16: editor turn, done (verified by numbers; shots unviewed for Zach)
Punchy-release tree, branch render/cloud-picks b7a1f7cc (no code change in this turn; tree clean). Editor closed with EditorApplication.Exit.
- **#494 drift pair** (frozen time, clouds off, fov 60, the same view shot twice): cave 2.18 mean / 5.38% >8, lake 2.92 / 3.08%, colossus 9.15 / 36.13%, NaN 0. Compare with R-23's main-vs-branch at fov 60 (cave 7.5, lake 4.2, colossus 11.1): the colossus and lake gaps are mostly drift (animated things freeze at different moments). Cave keeps about 5 above drift, which is unexplained. That is small, but worth a look if Job 109 flags the cave. **Colossus clouds on vs off** from one frozen view: 1.12 / 5.27%. Shots are in PunchyMilestones/R-35/drift494/.
- **Compile:** 122 shaders, 0 failed. NaN/Inf/negative 0 on every checked shot.
- **Picks** (job 79 peak -3460,2425, looking west about 20 deg up, PartlyCloudy, coverage 0.12, Detail 1.5 from prefs, ppm 1403). Noon has sunY -0.819 and golden hour -0.107. Pixel diff vs base (mask 0), mean / % of pixels >8:

  | Pick | Noon | Golden |
  |---|---|---|
  | A lobes 4096 | 0.44 / 1.09 | 0.55 / 1.76 |
  | B multi-scatter 8192 | 1.03 / 5.99 | 1.17 / 6.69 |
  | C skylight dim 16384 | 0.36 / 0.58 | 0.68 / 3.41 |
  | D nearest sun sample 32768 | 0.11 / 0.00 | 0.11 / 0.00 |
  | E off (fixed metres) 65536 | 0.33 / 0.29 | 0.47 / 1.34 |
  | A-D all 61440 | 1.24 / 6.72 | 1.52 / 9.16 |
  | base again (noise) | 0.06 / 0 | 0.08 / 0 |

  D is at noise level, so Zach probably can't see it. I suggest dropping D in row 3 unless he sees it. B is the biggest single change. C matters most at golden hour.
- **E at fov 60, Detail 1** (in one frozen frame, mask 0 vs 65536 = main's fixed-metre path): 0.026 mean, 0% >8 (base-again noise 0.031). E is identical to main at fov 60 and Detail 1, as designed. An earlier try that swapped in main's shader files with a live AssetDatabase.Refresh drifted by 12-14 mean even between two copies of the same shader, so the reload method doesn't work for this. Those shots are deleted.
- **Gotcha (worth a docs line):** in host+client play, WorldSync.ApplyHostState copies the server WorldState.TimeOfDay into the DayNightCycle every Update, so setting `cycle.TimeOfDay` alone snaps back. My first pick shots were all at 14 h and were retaken. Set the hour on the server ghost too, or set it again after the view settles. R23Probe.SetHour (untracked, in Assets/Temp) now does both.
- **Shots (unviewed, for Zach to pick):** D:/Unity/PunchyMilestones/R-35/picks/: noon-* and golden-*, named by mask (0 base, 4096 A, 8192 B, 16384 C, 32768 D, 65536 E off, 61440 A-D, again), plus e-fov60-d1-{0,65536,again}. D:/Unity/PunchyMilestones/R-35/drift494/: cave60a/b, lake60a/b, colossus60a/b, colossus-cloudson/off.
- **#494:** the drift pair is in. It is still waiting on BENCH Job 109 (QUEUED after 108), so it is not ready yet. Merge order stays #494, then render/cloud-picks.
- **Next:** row 3 once Zach picks (ship the picks on by default, E kept), plus BENCH on the draft above (the coordinator files it).
- Risks: the picks were shot at Detail 1.5 (Zach's prefs), and E's diff is larger there by design (screen-size fades move with Detail).
- Shares: the sky/cloud shared path (SkyCommon.hlsl `_PunchySkyOff` bits, ScreenSize.hlsl from #494). No parallel left unfolded.

## Coordinator asks 11-16 (editor-free)
### 1. Hour-sync doc line: ready for PR
Branch render/hour-sync-doc @ 0fb62bf8 (off main fd2184ce). It adds one bullet under "How to take shots" in docs/agents/testing.md (LF, as before). Correction to my earlier wording: the snap-back is on a **joined client**, not the host. WorldSync.Update calls ApplyHostState only when `joined` (a client with no local server). The host's own LateUpdate PublishState sends its cycle's hour. The line says to set the time on the host, because `-time` and a direct TimeOfDay set on a joined client snap back. No issue.

### 2. #494 cave gap above drift: not a code change. Explained by measuring method plus a clock that doesn't freeze.
- **Code at fov 60, Detail 1:** every #494 fade in the cave view reads `PunchyReferenceDistance(d) = d * 935.3 / (540 * |P._m11| * Detail)`.
  - Those fades are the TerrainSurface rock-face `steep` 40-90, cave-mouth `mouth` 30-60, grain 6-22, sheen, cobble and rain; Flora relief, leaf, sheen, glow and cell; and Creature fur.
  - At fov 60, |P._m11| = 1/tan 30 = 1.73205, so the factor is 935.3 / 935.307 = 0.99999. A fade edge moves under 1 mm at 90 m.
  - `abs()` covers the flipped D3D projection, and TAA jitter only touches _m02/_m12. No ortho camera draws terrain (checked in row 5).
  - The Flora vertex breath/twitch/flutter read `_PunchyViewPpm` = 935.3074 at Detail 1, which this turn's e-fov60 state line confirms, so that factor is also 1.
  - So at fov 60, Detail 1, the branch draws the cave like main. The rock-face and cave-mouth grain are not the cause.
- **Why R-23 still showed cave 7.5 against today's drift 2.2:** the two numbers measure different things.
  - R-23's main-vs-branch swapped shader files in with a live AssetDatabase.Refresh. Those shots were minutes apart.
  - Today's drift pair is the same frozen view shot twice, seconds apart, with no reload.
  - This turn's E test showed that the swap-and-reload method alone drifts by 12-14 mean even between two copies of the same shader.
- **The clock that doesn't freeze (found reading the code):** `_PunchyClock` is set by WorldSync.SharedClock(). On a joined client it is the server tick time, not Time.time, so `Time.timeScale = 0` does not stop it.
  - The probe editor was a joined client: ApplyHostState ran and the server ghost wasn't local.
  - So everything that animates on `_PunchyClock` keeps moving in a "frozen" shot. That includes cave glow flora (part 5 pulse, 11 s period, x2.5 brighter underground), flora breath (4 s) and twitch, cloth, puddle rings and water.
  - Shots seconds apart catch nearby phases (small diff). Shots minutes apart catch random phases (bigger diff). This fits cave 2.2 vs 7.5, and lake and colossus too.
  - Not proven in editor. R-23's fov 90 cave at 0.7, below its fov 60, also argues against a fade cause, since fov 90 is where the fades do move.
- **Intended?** There is no real change at fov 60 to approve. The only intended change is the rule 20 shift at other fovs and Details.
- **Suggestion (not done):** for future frozen pairs, freeze on the host, or pin `_PunchyClock` too. Shader.SetGlobalFloat after WorldSync's Update won't hold, because WorldSync sets it every frame, so the probe would need to disable WorldSync for the shot. That could be a second docs line on render/hour-sync-doc if you want it.

### 3. Freeze line: ready for PR
render/hour-sync-doc @ **349c274e** (on 0fb62bf8). It adds one bullet to docs/agents/testing.md: take frozen comparison shots on the host, or with WorldSync off for the shot. On a joined client `_PunchyClock` follows the server tick, and `Time.timeScale = 0` doesn't stop shader animation. LF checked, pushed.

## R-29
### Row 1: effects inventory and kit proposal (editor-free, 11-16). Needs approval before row 3.
**How each effect draws today**
- **Electricity** (none of these share code):
  - *ArcBolt.shader/.mat*: two crossed ribbons fixed in object space (FloraSystem.BoltMesh). It re-jags on a **fixed 8/s beat**, uses one envelope and one flat colour, and flickers on and off at 40 Hz. It has no forks, glow or afterglow, and no fog.
  - *FloraSystem.DrawBolts* (Flora lane): spire-to-spire and spire-to-ground strikes timed from the shared clock, with a Zip crack. It uses a **fixed reach** of 160 m × ViewRange, which breaks rule 20.
  - *Fauna/GolemArcs.cs* (W-89): continuous arcs with the same mesh at ×7 thickness, so the jag scales ×7 too. The rock bob runs on local `Time.time` and the crack on `UnityEngine.Random`, so it isn't shared between players.
  - *FloraPart.Arc* (FloraGenerator.Crystals, Flora.shader): crystal arcs are **baked tubes** in the flora mesh. They only jitter by 0.18 × scale and flash in brightness bursts. **This is most likely the "one fixed shape that flashes colour" Zach saw.** The magnetic rock uses the same tubes.
  - *Storm lightning* (WeatherSystem, LightningRpc): a sky flash and light, and Sky.hlsl does draw a bolt in the sky (correction 11-16: the first inventory said no visible bolt). It is a candidate kit consumer.
- **Fire (the model):** FireEffect/FireSystem/Flame.shader. It is used by landmark flames, beacons, forest fire and colossus lanterns.
- **Steam, smoke and spray on the GPU:** LandmarkEffectSystem plus LandmarkPlume.shader draw procedural emitters with no CPU sim. The landmark assets have 52 Plume, 18 Flame, 5 Bubbles and 1 Waterfall entries. Code also spawns emitters for chimney smoke (W-43c), river fall mist, foam and lip spray (R-21), and the grand falls.
- **Smoke, steam, spray and sparks on the CPU (Shuriken one-offs, the cost risk):**
  - AmbienceSystem: breath vapour (players, creatures), snow footstep powder and diving-bird spray.
  - SkunkGas, WorkDust, and the ColossusWalker and ColossusRigs dust.
  - The DunesColossus storm (600 particles).
  - The StoneColossi.Puffs helper (max 300 each), shared by CarnivorousPlant, CharredTreant (smoke, ash, embers, coals), CinderSnail, HermitCrab, IceMammoth, MagmaSerpent (smoke, sparks), MireWalker, Puffball and SkyWhale.
  - Underwater motes and bubbles.
  - Weather: rain, snow, hail and DustVeils.
- **Ambient (GPU):** AmbientParticleSystem types (AshFlecks, ColdMist, Embers, FogWisps, Pollen, SandPuffs, Sparkles, Spores, SwampMist), plus CaveDrips and the HeatHaze feature.
- **Wind:** there is **no visible-wind effect**. Wind shows only as motion in grass, trees, hair and drapes, and as DustVeils, SandPuffs and Pollen drift.

**Kit proposal.** Each family gets one asset, one draw path and presets by index (rule 30, the FireSystem model). The kit lives in `Scripts/Rendering/Effects/` (namespace Punchy.Rendering.Effects).
1. **Electricity** (row 2, sketched below). It replaces ArcBolt, the golem arcs and the baked FloraPart.Arc tubes. The spire and crystal callers keep their placement, so there are no placement changes.
2. **Smoke and steam:** turn the LandmarkPlume emitter into a callable `PlumeSystem.Add(pos, preset, strength)` with presets for chimney, steam, fumarole, toxic and smoke. The Shuriken smoke users move onto it one per commit.
3. **Sparks and embers:** take Fire's ember path and the AmbientGlow sprite into one `SparkSystem` (burst plus drift), for MagmaSerpent sparks, CharredTreant embers and hammer sparks later.
4. **Spray and gushing water:** reuse the R-21 fall spray and foam emitters as a `SprayPreset` on the plume path. HermitCrab, diving birds and colossus pools join it.
5. **Wind (new):** gust streaks, plus leaves, dust or snow per biome, drifting on the shared wind. This is AmbientParticle drift with a gust field. Zach should OK the look first, since it is new.
6. **Dust and puffs** (breath, footsteps, WorkDust, colossus dust) go onto the plume path as short bursts.

Row 3 keeps every look the same. Weather precipitation and underwater stay as they are (they are not effects in this sense). Fire stays put unless you want FireEffect/FireSystem moved from fauna's Ambience into the kit folder; that would be a pure move.

**Questions for you:**
- (a) Is `Scripts/Rendering/Effects/` OK as the kit folder, owned by render-2?
- (b) `LightKind.Electric` lives in player-owned LightPolicy.cs. For now the sketch uses LightKind.Fire.
- (c) The call-site edits for row 2 are in FloraSystem.DrawBolts and FloraGenerator.Crystals (Flora lane) and in Fauna/GolemArcs.cs. I'll keep them to the call site and tell both owners when I get there.
- (d) Should the Shuriken one-offs get a BENCH job before row 3, as a cost baseline?

### Row 2 sketch: electricity on render/effects-kit @ 7a6ac495 (WIP, uncompiled, not wired)
- `ElectricityEffect` (Resources/StandardElectricity) sets:
  - colours by index (blue, violet, green, gold), each with core, glow and light;
  - jag, core and glow width;
  - fork chance, length and miss share;
  - re-jag rate, afterglow, dropout and brightness jitter;
  - light intensity and reach;
  - crack chance, reach, volume and pitch.
- `ElectricitySystem` is static, like FireSystem:
  - `AddArc(from, to, seed, colour, width)` for steady arcs (golem) and `AddStrike(..., Rhythm)` for arcs that fire now and then (spires), with seeded jittered strike times.
  - It does one GraphicsBuffer upload and one `RenderPrimitives` call. The fade is `DetailPolicy.ExtraReach(length, ArcPixels)` (rule 20).
  - Light goes through `LightBudget.Add` keyed by seed. The crack goes through GameAudio Zip once per strike, plus a `Struck` event hook.
- `Electricity.shader`:
  - Path k starts at (k + 0.85·h_k)/rate, so the gaps are irregular and there is no beat.
  - Each path gets three octaves of hashed jag, its own brightness, and two fork slots that come and go, most of them missing.
  - The previous path stays as an afterglow that fades by exp(-age/afterglow), and 1/30 s dropouts give the flicker.
  - It is camera-facing core plus glow, additive and fogged. All timing comes from `_PunchyClock` with no ghost field. The CPU and GPU use the same PCG hash.

**Editor slot request:** I'm ready for one on port 7986. I'll check ≥ 6 GB free first and close with EditorApplication.Exit. In that slot I would compile, make the asset, add .meta files, and take 12-frame strips near and at 50 m with a 3-line call from a probe spot, with `-sparkleprobe` NaN 0.

### Row 2 handoff (2026-11-16, render/effects-kit 05a8043f, main 0834e28b merged in)
- Status: call sites wired, still uncompiled. I stayed editor-free; this waits for the editor slot after render (R-31).
- Kit: `Scripts/Rendering/Effects/` (render-2 owns): ElectricityEffect, ElectricitySystem, EffectsDriver (draws the kit once a frame from the pipeline's begin-frame hook, material from Resources), and Shaders/Electricity.shader. The editor slot makes the Electricity material and effect asset.
- Flora (call sites only): FloraSystem.DrawBolts now calls `ElectricitySystem.AddStrike` per bolt. Its own mesh, material and the struck-restore table are gone. Bolt rhythm and seed come from the same seeded draw as before. The 160 m x ViewRange gather radius is fixed metres (rule 20); it is left as is and flagged for a later card.
- Fauna (call sites only): GolemArcs.LateUpdate calls `ElectricitySystem.AddArc` per arc. Only arc 0 cracks, and the seed comes from the golem's position, so every peer sees the same pattern. The StoneColossi.RockGolem comment now names ElectricitySystem.
- Removed: ArcBolt.shader and Resources/ArcBolt.mat, since no code uses them now.
- For the player lane: LightPolicy.cs gets one enum value, `LightKind.Electric`, plus its policy row (Real at best, Emissive at worst, importance 2). LightBudget.cs needs `Kinds` changed from 7 to 8 so the log arrays fit. That one line goes beyond the agreed enum value and row. LightKind is never sent over the network or saved (checked by grep).
- Crystal arcs (FloraGenerator.Crystals): deferred to the editor slot. The plan is to keep the baked far tubes (W-101 far crackle) and swap the near LODs to kit arcs from model-space endpoints. The magnetic rock's orbiting shards need care.
- Asked: a BENCH-QUEUE note for the Shuriken baseline (towns and the StoneColossi.Puffs giants), to measure against in row 3.
- Next: editor slot (compile, material, 12-frame strips near and at 50 m, `-sparkleprobe` NaN 0, tune the ArcPixels fade), then row 3 one family per commit, with wind last for Zach's OK.
- Coordinator (11-16): Kinds 7 to 8 is approved as part of the same change. Row 3 now also replaces the DrawBolts 160 m gather radius with a gather picked by screen size (rule 20), since render-2 owns that call site now.
- Editor slot (11-16, port 7987): not opened. Render's editor (Punchy-render) had not loaded, the fauna editor was still starting, and only 7-9 GB was free. With render's editor still to load, opening mine would leave less than 6 GB. Waiting for RAM to free up.
- Row 3, bolt gather (11-16, editor-free): `ElectricitySystem.Reach(width)` now gives how far an arc shows, from its glow band's thickness on screen. The fade used to work on arc length, which reached kilometres. FloraSystem gathers spire bolts out to that reach, read again on each regather, in place of the fixed 160 m x ViewRange. ArcPixels 3 puts a spire's 0.6 m band at about 190 m at the reference view and Detail 1, near the old range. Golem arcs (width 7) reach further. The pairing loop grows as the square of the spire count, so a wider reach on a High preset could cost more: a check to queue on BENCH after the editor pass. Still uncompiled.

## 11-17 resume
- R-29 (#320) row 3: main merged into render/effects-kit, pushed 87426738. Compiles; Electricity.mat and StandardElectricity.asset added under Resources. Spire grove probe: 20 bolts, reach 281 m, sparkle `r29spire` 360 frames, bad 0 hot 0. 16 shots (near and 50 m, 2560x1600, unviewed) in PunchyMilestones/R-29; strikes fire intermittently by pixel count. Golem arcs not shot. BENCH request filed (pairing cost).
- BC1091: DetailPolicy's static readonly PropertyToID ids gave the struct a managed static ctor; ids now looked up inline. Fix 5939a4e0 on render/one-surface-5 (unblocks jobs 109 and 111).
- R-35 (#333) row 3: pick E only, on render/cloud-e 25fbc044 (one-surface-5 + E + main).  Compiles; sparkle `r35e` (PartlyCloudy noon) 360 frames, bad 0 hot 0. 65536 bit kept as the metres fallback for BENCH. Note: the diff vs main carries 3 Water.shader lines; they come from R-23 r5 (#494), not E. Job 111 cut to E only.
- R-28 r2: cobble-depth is NOT ready for PR. Job 86 BLOCKED: FXC crashes compiling Terrain Surface ForwardLit fresh, likely the paving parallax `[loop]` with an inner `[unroll]`. BOARD corrected.

## R-29 pre-merge check (PR #540, e7b96f9c)
- (a) A/B vs main at the spire pose, clear weather (core px / core lum / glow lift): main day 2921/0.915/0.073, night 3180/0.919/0.119; kit as pushed lost night glow (0.104). Fix: glow falls off softly to the ribbon edge instead of hiding under the core, GlowWidth 6->9, GlowStrength 0.35->1. Kit now day 3039/0.915/0.123, night 3286/0.923/0.141, so it beats main on both. It still looks like a white core with a modest blue halo; a strong bloom halo would need volume/bloom settings (supervisor's call). Shots: PunchyMilestones/R-29/ab/main-*, kitj-*.
- (b) Golem arcs: blue arcs with visible glow, day and night (PunchyMilestones/R-29/golem-g2-*). Pass.
- (c) FloraSystem.cs diff vs main only moves spire bolts into ElectricitySystem. WorldTests.Landmarks_V3 PASS (landmarks 114 9786243F), Flora_PlacementCost PASS. FloraHabitatTests are all Explicit shot tests, so they were not run.
- Note: the open-editor CLI `test run` returns 0 tests for PlayMode here (sync is refused; async status stays empty). scripts/run-playmode-tests.sh with the editor closed works.

## R-29 spoke fix (#540) - render/effects-kit a5b0f8d6
- Fix is in the shared Electricity.shader, so golem arcs and spire bolts both get it. Each joint now has one side vector, taken from the tangent through it, so neighbouring segments share their edge. The path ends get round caps: a half-width extension with a radial falloff.
- Night glow was weaker after the geometry change, so the glow is now width 12 and strength 1.5 (asset and code default).
- Spire A/B glow_lift, day/night: main 0.073/0.119, kit 0.106/0.124.
- I viewed golem-g5-night-1 myself: the arcs are clean continuous lines with a soft halo and no spoke fans.
- Shots are golem-g5-{day,night}-0..3 and ab/kitm-* in PunchyMilestones/R-29/.
- Next: resuming R-28 r2, the FXC crash in the paving parallax loop.

### R-29 correction (11-17)
- The earlier R-29 report (a5b0f8d6) was wrong. Its Electricity vertex stage failed to compile (FXC X8000), so no ribbons drew. The g3/g5 shots and the A/B scores only measured flora decor.
- Fixed in 50cdacb6 on render/effects-kit: branch-free PathPoint and arithmetic selects; short wide links take their side from the path axis. Both stages compile with 0 messages. Glow is back at main's 9 / 1.
- Spire A/B at 9 / 1 (ab/m9-*): glow_lift is 0.101 by day and 0.133 at night, against main's 0.073 / 0.119.
- Golem with arcs forced on (Dropout 0) at the g2 pose: R-29/golem-f2-{day,night,closeday,closenight}-0..3. I checked golem-f2-night-1 by eye: arcs on every link, soft round halo, no spokes. There is faint vertical banding inside the halo.

### R-28 r2 FXC fix (11-17)
- render/cobble-depth 6290b06e: the paving parallax is one `[loop]` with a fixed count (steps+3) and arithmetic selects. The two secant refines run as its last iterations, so CobbleHeight is inlined once instead of four times. There is no branch around the sample.
- The crash did not reproduce in the editor (the old shader compiled there too), so I treated it as compile cost. Editor cold compile of 2 ForwardLit variants: base 80 s, ce3ff5a7 141 s, 6290b06e 101 s. All variants are ok, with warnings only.
- Look: town paving at (80,-352), old vs new (R-28/row2/fxc-pold-* vs fxc-pnew-*). I checked the graze pair by eye: stones, bevels and grout match. The pixel differences come from grass, clouds and the player model. Town 73 (2972,-885) has no paving any more.
- Cost: B now samples steps+3 times on every paving pixel. BENCH job 86 is updated to B = 6290b06e and is READY; build with a clean shader cache.

## Cave cut flash fix (11-17) - b/fix-cave-cut-flash 1efa2a5d
- Cause (W-180): the surface skip culls cave chunks in Update with that frame's pose; a cut moves the camera afterwards, so the mouth drew with no passage behind it.
- Fix: new partial CaveSystem.CutGuard.cs plus 6 lines in CaveSystem.cs (OnEnable/OnDisable hook, NoteGathered in GatherOpenings, skipped list in Draw). In beginCameraRendering, if the main camera is 3 m+ or 25 deg+ from the gathered pose that frame, the skipped chunks draw too, that frame only, no shadows. Normal frames cost nothing extra.
- Check: pose then cam.Render in the same eval, no warm-up, at cave 0's mouth from 70 m away. Hook off: the mouth is a white hole through to the sky. Hook on: dark passage, same as the settled frame. I viewed the pair myself. Shots: PunchyMilestones/cave-cut-flash/c0-cut-noguard, c0-cut-guard, c0-settled.
- WorldTests 20/20 pass. Works the same solo, host or client (camera-only, no net state).

## R-41 Beach glare fix (11-17) - render/beach-glare f36e362b
- Cause: not sand specular, bloom or exposure (BeachSand albedo test and bloom/post toggles changed nothing). 15 always-on warm glow slots (3.0, 1.03, 0.15, 14 m) sat 4-49 m from the camera at noon: the fishing kit's dock/shore/wharf/boardwalk lanterns (GlowingAccents) went out as plant glow.
- Fix: FloraPlacement.GlowLight (+13 lines) gives those walkways the PierLamp street-light entry, so they light only after dark through the pooled street-light path (night check: 15 pooled near lights at time 22). Night look: dimmer, less orange than before. No shader change, so no BENCH line; fewer glow slots by day.
- Shots, noon, Clear, same pose, sun frozen: PunchyMilestones/beach-glare/g0-base (before, lower-frame mean 180/157/129, yellow wash) vs g2-base (after, 156/144/135, pale sand, deck shadows show). I viewed the pair.
- Left alone: some white haze at the lower left is Water.shader's shore swash/foam (off limits). Camera-only lighting, so it works the same solo, host or client.

## R-42 Shore foam thin band (11-17) - render/shore-foam 95153679
- Cause: in Water.shader the sea surge started at 1.5x _FoamDistance, its trail decayed over 0.12 + 0.25x FD, and the wash was 0.15. On gentle beaches the view-ray thickness stays small for metres, so this spread a white wash over the clear shallows, the pale "sand" at the lower left in g2.
- Fix: surge starts at 0.4x FD (ends at 0.03x), trail 0.03 + 0.06x FD, wash 0.1. Constants only, so instruction count is unchanged. Measured foam vs thickness after: about 0.27 at the front, under 0.01 by 0.4.
- Pair at the g2 pose and noon: PunchyMilestones/shore-foam/pair.png (before = g2-base on main, after = g5-base). The after sky came out overcast even with -weather Clear, so brightness differs; the foam shape is the point. An in-session shader swap gave stale frames, so the two shots come from separate play sessions.
- BENCH line added (water GPU at Medium, budget +0.1 ms).
- Rework (fca46f65): pair retaken matched, both shaders under one script: Sunny day type plus Clear held (-weather Clear only clears cells, so an overcast day type stayed grey), hour paused at 12, _PunchyClock pinned per shot so the swell and tide match. Edges softened, peak 0.75. Pair: PunchyMilestones/shore-foam/pair-matched.png (main vs after at clocks 1000 and 1005).

## R-43 Matched-shot helper (11-17) - render/matched-shot fdbd6db6
- New tracked helper `Assets/Game/Scripts/Debugging/MatchedShot.cs` (my file; DebugControls is fauna's, so no flag there). Any lane calls it from its shot eval after landing: `Punchy.Debugging.MatchedShot.Hold(new Vector2(x, z), 12f, 1000f);`, waits about 25 s, shoots; `MatchedShot.Clock` can change between shots for other swell phases; `Release()` frees it.
- Holds: Sunny day type plus Clear forced and held, the day/night cycle paused at the hour, `_PunchyClock` pinned in a late Update/LateUpdate and before rendering (after WorldSync writes it).
- Checked in play: clock stayed 1000, day Sunny; two shots 8 s apart differ by 0.97 mean (life only). Usage line in lanes/shared/checks.md (Stage); lanes/README.md has no shots section and docs keep one home per fact.
- Follow-up for fauna if wanted: a one-line `-matched` flag in DebugControls.Flags.cs calling Hold, for -shotlist runs in player builds.
- R-35 (#333) row 3 E before/after: one session on render/cloud-e, PartlyCloudy noon, clock pinned (MatchedShot), -skyoff 65536 (before, fixed metres) vs 0 (E), fov 25/60/90 from 80 m up. Mean abs diff 0.15-0.28 of 255 (under 0.3% of pixels moved more than 8 levels), so no visible change at these views; the visible clouds sit within 3.5 km, where both fade curves keep full detail. Sheet: PunchyMilestones/R-35/e-ab/pair-before-after-diffx8.png (before, after, diff x8; unviewed full size).
- R-28 r2: main merged into render/cobble-depth, e098ea4d. The TerrainSurface conflict with #553 is resolved (the collar now multiplies occlusion); main's collar Mix now lerps R-28's half4 Soak (it failed to compile). Fresh ForwardLit compile, D3D, 2 build keyword sets: ok, warnings only. Job 86 READY again, A = main 14a8de92.

## R-29 row 3 steam + smoke families (11-17) - render/effects-kit-2 e6df4844
- New PlumeSystem (Steam, Smoke families) + PlumeEffect assets Resources/StandardSteam, StandardSmoke; EffectsDriver draws both with the Landmark Plume material (moved to Resources, GUID kept). Fade by screen size (GonePixels 16.8, rule 20).
- Callers moved: LandmarkEffectSystem geyser, hot springs, mud pits plumes (steam) and fumarole plume + chimney smoke (smoke); 5 emitter kinds, picked by authored colour so no landmark asset changed. Still off-kit: FireSystem smoke, toxic geyser, fumarole embers, river/cave mists.
- Matched pairs (one session, main then branch, MatchedShot, 3 clocks): PunchyMilestones/R-29/plume/pair-{Geyser,HotSprings,HumanVillage,FumaroleField}-before-after-diffx8.png. Mean abs diff 1.4-1.8 (geyser), 0.8 (village), 2.1-2.7 (fumarole): plume motion only. Hot springs: plumes match, but the before run had haze/no clouds and one blank frame (session weather, not the kit). NaN 0, compile clean.
- One commit for both families (they share the plumbing). LandmarkEffectSystem is fauna's (Ambience): change kept to the plume call sites. BENCH request added.

## R-29 row 4a spray family (11-17) - render/effects-kit-3 bff9d4ec
- PlumeSystem.Spray + Resources/StandardSpray (defaults = no look change). Callers moved, all in LandmarkEffectSystem (fauna's Ambience; call sites only, no river/waterfall mesh code touched): grand tier foot/pool/mist, lip spray, river fall foot mist + foam, cave falls (under-rock lit), waterfall landmarks' landing plume. 10 emitter call sites + 1 routed by type.
- No caller exists for river rapids or sea spray on rocks (nothing to route; new effects would be a card).
- Matched pairs (one session, main then branch, 3 clocks): PunchyMilestones/R-29/spray/pair-{RiverFall,FallLandmark}-before-after-diffx8.png. Landmark fall mean diff 0.7-0.8 (match). River fall 12: same spray, but drifting mist and water ripples differ between the two plays (viewed; no missing or extra spray). This world has no grand falls, so tiers are unshot. BENCH request added.

## R-29 row 4b sparks family (11-17) - render/effects-kit-4 cd82ecf3 (from main 93d2c4a1)
- PlumeSystem.Sparks + Resources/StandardSparks (defaults = no look change). FamilyOf routes hot orange glow plumes (r>=0.8, r-b>=0.5, g<=0.7): HumanVillage, HumanMiningCamp, DwarfHold, DwarfMiningCamp, OgreCamp, FumaroleField, VolcanoCone. Cold sparkles (crystal, elf, fairy, goblin green) and ToxicGeyser stay on the old path. LandmarkEffectSystem: call site only.
- FireSystem embers are drawn in the fire quad shader, not plumes, so they stay. The forge on the colossus is mesh only, with no effect to move.
- Matched pairs (main then branch, 3 clocks): PunchyMilestones/R-29/sparks/pair-*-before-after-diffx8.png, incl. close-ups 9 m from the ember plume (HumanVillageClose, FumaroleFieldClose). Wide c0/c1 mean 0.4-1.0, volcano 2.6, close-ups 4-8 from cloud drift and NPCs (viewed: same embers and smoke). Some c2 frames are blank sky in both branches (capture flake, not the change). BENCH request added.
