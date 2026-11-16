
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
