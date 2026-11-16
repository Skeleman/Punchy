
## R-23 r3 (#298), part A, editor-free: WIP render/one-surface-3@9795d6a3 (pasted by supervisor)
- Sky.hlsl split into SkyCommon / Clouds / Atmosphere / Sky. Lines moved as-is, line endings kept. 14 haze-only shaders include Atmosphere, Flame includes SkyCommon only.
- Not compiled yet. Next, in the editor: Refresh, ShaderHasError on 21 shaders, pixel-diff 8 shots vs main (part A must match), sparkle NaN 0, suite.
- Part B (drift fixes, one shader at a time): wide shadow on Creature/Colossus/Pebbles/SunkenTemple; PBR cloud dim becomes PunchySurfaceSunDim, so shade gets slightly brighter (a before/after pair for Zach). Grass/Water/Cave keep their look via flags.
- Compile time and variant count: BENCH job requested (rule 21).
