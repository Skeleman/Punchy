
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
