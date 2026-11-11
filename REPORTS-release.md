# REPORTS-release

Handoffs from the release subagent (tree Punchy-release). Earlier R-10 merge and R-13 row 1 reports are in REPORTS-world.md (around lines 4424 and 4446).

## R-13 row 2: convert visual ranges to screen size (2026-11-10, release subagent, branch `lod/screen-size`): done (verified)
- Commits: 2bcbbba9 (the conversion) and d9634f37 (Burst fix, see Regression). Both are pushed. Base is origin/main 0477dd07. `tools/lod-lint.sh lod/screen-size` is clean. Line endings were kept on every file.
- What changed: each kind gets one line in DetailPolicy (size in m, threshold in reference pixels), tuned so 1080 lines, a 60 degree FOV and Detail 1 give today's metres. Every C# use goes through `DetailPolicy.ExtraReach` (Detail scaled) or the new `FarReach`, which scales by View for the kinds GraphicsSettings.ViewRange scaled before. Shader fades use a smoothstep on `PunchyPixels(size, distance)` from the new shared `Assets/Game/Shaders/PunchyPixels.hlsl`. DetailPolicy.SetView sets `_PunchyPixelsPerMetre` (Detail included) once a frame.
  - Converted kinds: flames, chimney smoke, river falls, landmark effects, burning crowns (near and far), beacons, colossus lanterns, lightning bolts, fountain basins, dropped items, ground surface detail (DetailFar), driftwood, far-shadow sway stop (StillFrom; `-stillfrom` still sets metres), interior rooms, and ambient particle tiles.
  - Shaders: Flora.hlsl, Flora.shader, FloraImpostor.shader, LandmarkEffects.hlsl, TerrainSurface.shader and Water.shader. Only the fade lines changed, plus the include.
- **Interiors.cs (player lane), one line:** `CullNear` went from `const float 40f` to `static float CullNear => DetailPolicy.ExtraReach(RoomSize 8 m, RoomPixels 187.1)`. That is 40 m at the reference view. Nothing else in the file changed.
- Shifts on purpose (same look at the reference view):
  - River falls reach by their drop, max(drop, 10 m).
  - Basins go by their width.
  - Colossus lanterns scale with each point's scale.
  - Lanterns, items and rooms now scale with Detail, so they shrink at Medium. Item draw range had no scaling before.
  - Ambient particles are capped at max(spawn radius, type Range).
  - Shader fades scale with Detail, while the far C# kinds use View, as before.
  - A narrower FOV keeps detail further out.
- **Regression found and fixed (d9634f37):**
  - Cause: 2bcbbba9 put `static readonly int PixelsPerMetreId = Shader.PropertyToID(...)` in DetailPolicy. Burst jobs (CreatureRenderSystem and others) read DetailPolicy, so Burst runs its static constructor. That failed the compile with BC1091, and every job fell back to managed code.
  - Effect: terrain chunks took 60-100 s per shot instead of about 5 s, and play ran at roughly half the frame rate.
  - Fix: the ID now sits in a nested `ShaderIds` class.
  - Test: a bisect on the same editor and shot list. 0477dd07 code was fast. With half the C# reverted it was still slow, and the editor log then showed BC1091 at DetailPolicy's static constructor. With the fix, every shot reaches good in about 0.5-8 s, the same as before.
  - Rule for every lane: no Unity API calls in static field initializers of a type that Burst code touches.
- Shots, 1920x1080, all six reached "good" in the shot check:
  - Before (0477dd07): `PunchyMilestones/R-13/before/`
  - After (d9634f37): `PunchyMilestones/R-13/after/`
  - Shots are town-night-aerial, town-night-ground, meadow-noon, riverfall-noon, lake-noon and town-noon-aerial.
  - Same look: the same buildings, trees, falls and plants in every pair. The pixel differences come from cloud cover and cloud shadows, which differ between runs.
  - `PunchyMilestones/R-13/after-slow/` holds the shots from the Burst-broken build, kept for the record.
  - Narrow FOV (30 degrees, held by eval during play): `PunchyMilestones/R-13/fov30/` (town-night-ground, meadow-noon, town-noon-aerial). Detail holds at the longer zoom.
  - No forest-fire shot: there is no debug flag to start one.
  - lake-noon sometimes lands the camera inside the ground. The placement depends on where the player was, and the shot check flags it. It came out clean in this run.
- D3D11 magenta check: the editor runs Direct3D11, all shots are free of magenta, and the shader message check finds no errors in Assets/Game/Shaders.
- Full PlayMode suite on d9634f37 (rule 19): 25 passed, 0 failed, 17 skipped in 623 s, Unity exit 0. That matches the earlier 25/0/17. The landmark count in the suite is 112, and placement is unchanged.
- Cost: no timing by the release lane (rule 21). BENCH-QUEUE job 51 asks for village at night, forest at night, hilltop and a 300 m flight, at High and Medium, ABBA, against main. It now points at d9634f37 or newer, never 2bcbbba9.
- Risks:
  - The Medium look loses lanterns, items and rooms nearer than before, as intended. Job 51 checks the cost.
  - Caves are W-135 and creature extras are A-99; this row left both alone.
