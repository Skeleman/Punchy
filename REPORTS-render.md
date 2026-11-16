# Render reports (render lane → supervisor)

Newest first.

## 2026-11-14 R-25 row 4: night vantage shots, far lights on and off (no code)
Branch render/far-lights 17177829, unchanged (no code this row, so no new suite run; row 3's 40/40 stands).
- DONE: on/off pairs at 22:00 Clear, High, clock paused, for the six R-22 row 0 vantages plus two new ones. "Off" turns
  off both far points and the town glow (`-farlights 0 -townglow 0` equivalents); near real lights stay.
  Shots: D:/Unity/PunchyMilestones/R-25/row4/ (six R-22 vantages, sheet row4-vantages-sheet.png) and row4c/ (retakes
  with a 40 s settle plus the two new vantages, sheet row4c-sheet.png). Left = on, right = off.
- Vantages re-picked: the R-22 "town" vantage (2800,-850) looks west, away from the town, so no lit town is in frame.
  Added "townfar" (2044,-1261, 52 m absolute, looking at 2968,15,-878: the town 1 km off) and "emberlava"
  (-500,-2600, 300 m absolute, looking at -530,20,-3395: Ember Forest, with the lava fissure about 19 deg left).
  The first summit and town pairs differed in geometry between on and off (streaming 4 s apart); the 40 s retakes match.
- What reads (lit pixels on/off, warm / bright, 2560x1600):
  - fly300 231/14904 vs 23/4950, fly1000 70/2990 vs 0/3: a field of far points across the island at night; off, the
    far view is dark. This is the clearest win.
  - summit 42/40156 vs 0/38574: town and plain points along the far edge on, gone off.
  - emberlava 277/22713 vs 0/3399: Ember Forest reads as a dense field of blue-white points; off, only near plants glow.
  - townfar 66/3886 vs 32/3192: the town at 1 km is a small white cluster on the horizon; it reads but small, and white
    rather than warm.
  - coast 35/17145 vs 6/10782, lake 59/10992 vs 24/10964: nearly the same on and off (little lit in those views).
- NOT DONE / does not read: lava. No orange glow at the fissure from 800 m in emberlava (crop checked), on or off.
  Lava's far form is missing or too dim at night; proposed as a follow-up item on the card, not fixed here.
- Observation, UNVERIFIED: in emberlava the far points 300-500 m out look larger and brighter than the same plants'
  own glow in the off shot. From 300 m up their screen size is small, so this may be the far tier working as meant;
  a ground-level walk-in would tell whether a step shows at the hand-over.
- Red ground: the red blotches under the blue-flame plants show in both on and off shots (emberlava and townfar), so
  they are not from far points or the town glow. Stays the open item on the card.
- Drift (card list): glow on pebbles, colossi and water not judged from these vantages; nothing obvious in frame.
- HomeSign: not wired; A-109 row 3 is not on main.
- Checks: night confirmed (clock 22, paused, read during the run). No NaN probe: no shader or mesh change this row.
- Asks: none. Proposal for the card: a lava far-form follow-up, and warmer town far points (the town reads white at 1 km).
- NEXT: R-23 row 2 on render/one-surface from origin/main.

## 2026-11-14 R-25 row 3: pooled fire lights, campfire far point, screen-size town glow, flora off the cluster variant
Branch render/far-lights 17177829 (6 commits on a40a4ae6), pushed. BENCH job 76 queued (below).
- DONE: 96cb62ca: LightBudget's street-light gap applies to Fixture requests only.
- DONE: 5a3afa22 (own commit, the two allowed calls only): LandmarkEffectSystem.cs:138 and .Beacons.cs:64 use
  `AddPooled`, so campfires and the beacon get a pooled real light near the camera at night.
- DONE: 0577702e: new Campfire far-light kind. A landmark flame shows as a point in its own colour from past where
  its flames are drawn; beacons keep kind Fire. Pooled flame lights fade by DetailPolicy screen size, no metres.
- DONE: 5c3c7853: the town glow map fades in by screen size (new TownGlow constants in DetailPolicy), not metres.
- DONE: 768ff9ae: fix for R-10's light log. With the log on and a pooled light granted it threw
  IndexOutOfRangeException every frame (indexed by a key's z, a world position for pooled lights), and requests were
  never cleared. Real lights are now counted by the request's kind.
- DONE: 17177829, item 6: grass, pebbles and the sunken temple leave the Forward+ cluster variant (job 45j: half
  of R-10's town cost was the variant). They take real lights from a list of the 16 best, unshadowed. Flora and
  creatures stay on the variant. Side-by-side at High, 22:00: grass beside the campfire and tufts and ground beside
  the lamp read lit the same; campfire strip warm px at 12 m 44633 vs 46552 without it.
- Dropped: item 5 (burning-only species glow in the fire channel, guarded so shared species stay normal). The
  Ember Forest strips with and without it matched, so it did nothing visible; not committed. Ember Forest far
  points still read blue-white, and red ground bands under the blue flames at 1200-850 m remain; their source is
  not the plant-glow path and was not found (noted on the card as open). Item 4 skipped as not needed (on the card).
- Light log, High, 22:00, Clear: FirePit pose requests 145, glow slots 16/16, real 1/48 (the pooled campfire),
  dropped 78 (the Fire kind: 93 requests, the Ember Forest's). Town: requests 142-147, slots 16/16, real 48/48,
  dropped 26-33, indoor 11 (Fixture: 80 requests, 46 real, about 30 dropped; Fire: 2 real). The two pooled fires
  push about 30 street lights out of the real budget; budget not raised. Does the hand-over hide it: on the town
  strip yes, no step from 1200 m to 12 m; a dropped lamp keeps its lit lantern and the town glow map's pool, but
  loses its real lit radius on walls and creatures. Which lamps drop near the square was not checked side by side.
  Ember Forest: 282 Fire requests, real 0; burning trees stay on glow slots as asked.
- Walk-ins (bearing 205 town, 100 campfire; High 22:00 Clear): no visible step on either. Town strip warm px,
  replacing row 1's: 1200 m 0, 600 2, 420 23, 300 53, 150 20, 105 55, 74 522, 52 2913 (a boulder leaves the
  frame at 62-52 m, not a light step), 31 3077, 22 3758, 12 5750. Campfire: warm point from 600-420 m (5, 151 px),
  flames from about 26 m (5852), 12 m 46552. Lamp and Ember Forest strips also taken.
- `-farlights 0` launch flag (Temp/PlayArgs.txt in the editor): FarLightsEnabled reads false in play. Medium,
  town walk-in off/on, warm px: 600 m 15/112, 300 m 84/435, 150 m 123/819.
- NOT DONE / UNVERIFIED: pebbles by a lamp not resolved at shot size (item 6). Low preset not looked at. Beacon
  view not walked in. CaveSystem.cs still 2548 lines (nothing in this row fell out of it). No pixel-diff vs main
  (row 3 changes the look by design). LightBudget, LightPolicy and FarLightPolicy sit in Scripts/Settings (player
  lane's folder); LandmarkEffectSystem's `FlameRange = 150f` is a fixed-metre flame cut this lane may not edit.
  No timing (rule 21).
- Checks: compiles, 0 errors. NaN probe in the town at night, Medium: 0 of 360 frames with markers (53 hot, no
  NaN). Full PlayMode suite on 17177829: `Run finished: 40 total, 40 passed, 0 failed` (no skips or seconds on
  this route).
- Shots: PunchyMilestones/R-25/row3/B-town-strip.png, B-fire-strip.png (1200 px), D-medium-farlights-off-on.png
  (off top, on bottom); side-by-sides and frames in R-25/row3/ and R-25/raw/.
- Asks: BENCH job 76 (town far points on/off, campfire, beacon, item 6 as its own arm). Merge after it passes.
- NEXT: R-25 row 4 (night vantage shots on/off), then R-23 rows 2-4, R-26, R-24.

## 2026-11-13 R-25 row 2: R-10 on main behind switches, surface glow out of CaveSystem.cs
Branch render/far-lights a40a4ae6 (b/r10-trims a1cb1899 merged with main d95e7ff1, 0 behind main), pushed.
- DONE: R-10 sits on main by a merge, not a rebase (pushed history is never rewritten); the CaveSystem.cs conflict
  is resolved. With defaults the player sees R-10's night: far towns, street lights, fires, glowing plants and
  crystals as points of light, the town glow map, pooled real street lights near.
- DONE: item 7. The surface glow's frame update moved unchanged into the new partial CaveSystem.SurfaceGlow.cs
  (rule 28). CaveSystem.cs 2719 -> 2548 lines (main: 2654); still over 2500, but every R-10 addition is now in a
  partial and the file is smaller than main's.
- DONE: item 1, switches. Existing: `-townglow 0` (town map, window and street-light points, domes), `-towndome 1`
  (dome, off by default), `-lightbudget N`, `-realreach 0`, `-floraglowworld 0`, `-lightlog`. New: `-farlights 0`
  (SurfaceGlow.FarLightsEnabled) turns every far point off; before, fire, plant and crystal points had no switch.
- DONE: R-10's shots against its last handoff (row 4c), High, 22:00, Clear, on = defaults, off = town glow and far
  points off. Warm px / bright px (2560x1600 frame): town 500 m 2174 on, 4 off / 2193, 138; town 1500 m 353, 261 /
  10866, 7062; Ember Forest 500 m 196, 1 / 22939, 6741; Ember Forest 1500 m 315, 17 / 25895, 4038. On the sheet the
  town is a warm cluster of points at 500 m (a pale patch with the switches off) and the forest a wide field of
  points, as 4c described.
- NOT DONE / UNVERIFIED: my reading of the card: row 2 is items 7 and 1 plus verification; items 2-6 (campfire
  far kind, ground hand-over, impostor window emissive, Ember Forest colour, cluster variants) are row 3. The
  forest's far points read blue-white, not flame-coloured (item 5). The Ember Forest vantages are re-picked (the
  layout moved since 4c: now about -261,-3393), so the match is by look, not pixel-diff; 4c's fungal (F) and BOTH
  shots not retaken. Not checked: Medium and Low, the `-lightbudget 0` arm, the `-farlights 0` launch flag itself
  (the on/off shots set the same statics from the editor), pixel-diff against main. No timing (rule 21).
- Checks: compiles, 0 errors. NaN probe in the town at night 0 of 360 frames. Full PlayMode suite after the last
  code change: `Run finished: 40 total, 40 passed, 0 failed` (this route prints no skips or seconds).
- Shots: PunchyMilestones/R-25/row2-far-on-off-sheet.png (1200 px, on left, off right); frames in R-25/row2/.
- Asks: the branch carries R-10's change to Settings/GraphicsSettings.cs (RealLights per preset, OwnRealLights),
  a folder the player lane owns: say if it should be split out or cleared with that lane before merge. No BENCH
  job yet; it comes with row 3 (item 6 changes the cost).
- NEXT: R-25 row 3 (items 2-6, walk-in strips for the five cases).

## 2026-11-13 R-19 row 2: three steep-rock cost switches, defaults unchanged
Branch render/steep-triplanar 1e556c4b (on b30c967f), pushed. For PR #285 / bench job 66.
- DONE: three switches, all off by default, in TerrainSurface.shader (reads `_PunchyRockCost`) and the new
  Rendering/SteepRockCost.cs (sets it from the command line). With none set the player sees what b30c967f showed;
  the default path does the same arithmetic.
  - `-rockhighonly`: beds and slabs at High and up only; Low and Medium keep the grain alone.
  - `-rockoctaves 1`: one noise octave for beds and slabs instead of two.
  - `-rockslabpx n`: slabs fade out below n reference pixels per 2.5 m slab (DetailPolicy-style screen size, rule
    20; default 2).
- DONE: sheet of six panels at the mountain 20 m pose (-3126,2898; player y 194; view yaw -169, pitch -40), Medium,
  12:00, Clear. Difference against the default frame (mean abs level, share of pixels changed):
  high-only 4.54, 47.7 %; one octave 4.92, 39.7 %; slab 40 px 0.04, 0.1 %; slab 150 px 1.49; one octave + slab 40
  px 4.92, 39.6 %. All arms still read as grey layered rock. High-only is the flattest (no beds, no slabs); one
  octave keeps the broad beds and loses the thin ones.
- NOT DONE / UNVERIFIED: slab 40 px changes nothing at this pose (every rock pixel is within about 58 m), so its
  saving has to be measured at a farther pose. Not checked: other poses, High preset (high-only should equal
  default there, not shot), fov 90. No timing done (rule 21).
- Checks: compiles, 0 errors. NaN probe 0 of 360 frames (one octave + slab 40). Full PlayMode suite after the last
  code change: `Run finished: 28 total, 28 passed, 0 failed` (this route prints no skips or seconds). The first
  suite attempt ended in an editor crash on leaving play mode, inside Unity's own scene-backup restore
  (ExitPlayMode -> RestoreSceneBackups); no frame of this row's code in the stack, not seen again in the second run.
- Shots: PunchyMilestones/R-19/row2-switches-sheet.png (1200 px); frames in PunchyMilestones/R-19/row2/.
- Asks: BENCH-QUEUE job on render/steep-triplanar 1e556c4b, GPU ms at the mountain 20 m pose (-3126,2898, yaw
  -169, pitch -40, 12:00 Clear, Medium), six arms: default; `-rockhighonly`; `-rockoctaves 1`; `-rockslabpx 40`;
  `-rockslabpx 150`; `-rockoctaves 1 -rockslabpx 40`. A second pose with rock at 100-300 m would show what the slab
  fade saves. The supervisor picks the default from the numbers.
- NEXT: R-25 row 2 on render/far-lights, CaveSystem surface-glow partial first.

## 2026-11-13 R-25 row 1: night walk-in survey, main against R-10 (no code)
Filed by the supervisor from the lane's hand-back. Branch render/far-lights ef41491d (b/r10-trims a1cb1899 merged
with main d95e7ff1); compiles, no suite run yet.
- DONE: #302 survey only, nothing changes for the player. Five approaches at 22:00, Clear, Medium, fov 60, 23
  distances from 1200 m to 12 m, on main d95e7ff1 and render/far-lights ef41491d. "Warm px" = warm pixels in the
  centre crop of an 800 px frame.
  - Town from the south (2985,-945). main: nothing warm from 1200 m to 125 m (0-37 warm px); lit ground arrives
    between 74 and 62 m (603 -> 2350). R-10: light points read from 1200 m; the same ground jump at 74 -> 62 m
    (587 -> 4584). Step remains on R-10.
  - Town-edge lamp from the south-east (2967.6,-961.8). main: town dark to about 175-125 m, jump 2646 -> 11598
    at 44 -> 37 m. R-10: a row of points from 1200 m, smooth rise to 10556 at 37 m; points hand over to lit panes
    and walls at 150-105 m. No step seen on R-10.
  - Campfire (FirePit 140,-545). Both: flame and warm hut wall read from 105-125 m; lit grass appears only at
    12 m (main 27215 -> 45898, R-10 8812 -> 16479 at 15 -> 12 m). R-10 has no far point for campfires. Step remains.
  - Ember Forest (-261,-3393). Both: flames visible from 1200 m, no step; far ground glows red from the world
    glow map while the flames are blue and the near snow shows little matching light.
  - Lava field (fissure -805,-3398). No surface lava on the current island (world glow bake logs 0 lava texels):
    no target.
  - Likely causes from code, not proven by a toggle: the 74 -> 62 m jump matches the window spill rule (60 to
    85 m) arriving with the 9 m fixture ground glow; the 44 -> 37 m lamp jump on main matches CaveSystem
    NearFixtures 40 m (fixed metres); the 15 -> 12 m campfire jump is the Flame effect's ground light, untraced.
- DONE: main already has near real and glow lights (16 glow slots), the 640 m far glow map, the baked-once world
  glow map, W-127 window spill near, per-pane window hours in Flora.shader. Only R-10 has far light points, the
  town glow map past the far map, the pooled real-light reach cut, and Forward+ cluster variants in creature,
  plant, grass, pebble and temple shaders (the +0.55 ms of jobs 45e-45j).
- DONE: what must land (rows 2-3): 1. R-10's far points and town map behind their switches. 2. A far-light kind
  for campfires and other landmark flames. 3. Ground light handing over by screen size with no jump; NearFixtures
  40 m and TownGlowFadeFrom/To 250/330 moved to DetailPolicy.ScreenSize. 4. Window emissive on FloraImpostor.
  5. Ember Forest: world glow colour taken from the flame colour. 6. Cluster variants only where a shader is lit
  by pooled lights near the camera, to get the town view under +0.5 ms. 7. CaveSystem.cs is 2719 lines after the
  merge (limit 2500): R-10's additions move into a SurfaceGlow partial first.
- DONE: HomeSign design note (wire only once A-109 row 3 is on main). One float per building instance, written
  when hour or day changes, read by panes (Flora.shader), the impostor (FloraImpostor emissive) and the far
  points and town glow map. Towns leave the baked-once world glow map and use the town map, redrawn on change.
- NOT DONE / UNVERIFIED: no isolated lamp exists, so the lamp case is a town-edge lamp. Cloud cover differed
  between the two runs. The campfire strip has one black frame at 1200 m. No toggle test of the likely causes.
  The two fissure sheets were not looked at (numbers only).
- Checks: 10 walk-in sets x 23 frames, every log shows hour 22; warm-pixel and luminance numbers per frame; 8 of
  10 sheets looked at, at 1200 px.
- Shots (milestones folder R-25): main-lamp-walkin.png, r10-lamp-walkin.png, r10-town-walkin.png.
- Asks: no renderer or ProjectSettings change needed (PC_Renderer is already Forward+ on main). Folk lane's
  FarFigureMaxPixels in DetailPolicy: no clash. Rule 20 leftovers: NearFixtures 40 m, TownGlowFadeFrom/To 250/330.
- NEXT: R-19 row 2, then R-25 row 2.

## #309 impostor squares (render/impostor-props f38e10bf), 2026-11-14

- DONE: cause proven. Reimporting FloraImpostor.shader during play reset the impostor draw materials' code-set
  values: frames went from 8 to 0 and the atlases were dropped. Impostors then drew as big lit squares. This is
  on main; the atlases themselves were never harmed.
- DONE: the fix, in two parts.
  - FloraImpostor.shader now declares the atlases, frames, full, pixels and model base in Properties.
  - FloraSystem.Impostors.cs gets SetImpostorMaterial, moved out of StartImpostors, and KeepImpostorMaterial,
    which sets every value again when _ImpostorFrames reads 0.
  - FloraSystem.cs gets one added line, the per-layer call in the cull loop. World lane: I touched your file
    with this single line only.
- Checks:
  - Repro on the branch: a forced reimport during play left frames at 8, and the window showed trees drawn
    normally.
  - The fallback path: I zeroed frames and the atlas from eval. The next frame logged "values were reset ...
    set again" for layers 1 and 2, and frames was back to 8.
  - WorldTests: Run finished: 40 total, 40 passed, 0 failed.
  - Shader has no errors, and the scripts compile.
- NOT DONE: no pixel diff. The values are the same when nothing resets them, so no look change is expected.
  Not tested in a player build.
- Shots (milestones folder R-23/i309): before-reimport.png, after-reimport.png, after-forced-reset.png.
- Note: the first proof of the cause (a reimport in play on main) put the squares on Zach's screen without
  warning. From now on I'll ask first before breaking a rule on purpose.
- Asks: merge render/impostor-props. No BENCH job: the cost is one float read per layer per frame.
- NEXT: R-23 row 2, then R-20 row 3 (#308).

## R-23 row 2 (2026-11-14): terrain, flora and impostors light through Surface.hlsl
- Branch `render/one-surface` 88719cc9, on main 3eeb72d4. New `Shaders/Surface.hlsl` (108 lines):
  `PunchySurfaceAt`, `PunchySurfaceLight` (sun, ambient, glow), `PunchySurfaceSunDim` (cloud and wide shadow) and
  `PunchySurfaceFinish` (haze). TerrainSurface, Flora and FloraImpostor call these instead of building the light
  by hand. 5 files, +164/-45. Nothing else changed.
- DONE: the move, with pixel-diff proof (rule 28) and NaN 0.
- Checks:
  - A/B in one play session: both paths in a throwaway build behind a global float. a = old path, a2 = old path
    again (noise), b = new path, a3 = old path after b (drift bracket). Max channel difference per pixel, mean over
    the frame:
    - neardusk: noise 0.0124, rewire 0.0134 (max 19). nearnight 0.0017 / 0.0017. nearrain 0.0023 / 0.0021.
    - farday 0.0013 / 0.0013. fardusk 0.0012 / 0.0012. farnight 0.0061 / 0.0057. farrain 0.0085 / 0.0083.
    - nearday: the first take drifted (terrain was still streaming in; a vs a3 17.3, b vs a3 5.1). Retaken with the
      world settled: noise 0.0107, rewire 0.0105 (max 12).
    - Explanation: no real difference. What's left is rain, particles and weather noise, the same size as a vs a2.
    - Positive control: a deliberate magenta break showed 52702 px, so the capture would catch a real change.
  - NaN: `[sparkle] r23row2 summary: 360 frames, 0 with markers`.
  - ShaderHasError is False for all 3 shaders.
  - Suite: Run finished: 40 total, 38 passed, 2 failed. The two fails are the known flakes
    FolkTalkTests.WalkBy_NeverThroughWall ("no walk-by lines came up") and
    WorldTests.Place_PropFindsAndFramesWharf (stale LogWatch [measure] line), both logged in REPORTS-world.
    Neither touches shaders. A rerun of just those two didn't start: `test run --filter` gives 0/0 in this CLI.
- NOT DONE:
  - No player-build check.
  - The BENCH job is queued, not run.
- Shots (milestones folder R-23/row2): ab4/ (8 views x a, a2, b, a3), ab5/ (nearday retake), abctl*/ (magenta
  control), main-a, main-b.
- Notes:
  - Merge note: the world lane's TerrainSurface edit (RiverCurtains include, `shore = max(shore,
    PunchyFallWet(p))`) is not on main yet. Whoever merges second has to carry it into the new call order.
  - My A/B runs briefly showed cyan terrain in my editor window. That's Unity's async-compile placeholder after a
    shader reimport, not a bug. If Zach saw it on screen, that's what it was.
  - rendering.md has two new gotchas. Swapping `material.shader` on a material Entities Graphics has registered (the
    terrain) breaks its per-instance data. `DayNightCycle.TimeOfDay` gets set back to 12 while paused.
- Asks: review/merge `render/one-surface` after BENCH job 82 (queued after 81: main vs branch, 3 views, Medium and
  High, ABBA, 3 rounds). Row 3 will add its commit as arm C before the job runs.
- NEXT: R-27 (#311, shadow-cache ddx), then R-28, then R-20 row 3 (#308).

## R-27 (2026-11-14): shadow-cache receiver takes its derivatives before the early returns (#311)
- Branch `render/shadowcache-ddx` 9906a3a9, on main 3eeb72d4. `FloraShadowCache.shader` only (+8/-3): `ddx`/`ddy`
  of the rebuilt world position now come before the sky, fade and out-of-cache returns. The normal is rebuilt from them as before.
- DONE: the change, with an A/B pixel diff against main's version and NaN 0.
- Checks:
  - A/B in one play session: main's receiver as a throwaway copy, swapped on the runtime material. a = new, a2 =
    new again (noise), b = main's, a3 = new after b. Max channel difference per pixel, mean over the frame, High:
    - townday: noise 0.0478, diff 0.0448 (max 8). forestday 0.0813 / 0.0856. forestdusk 0.1024 / 0.1055.
    - hillday 0.0513 / 0.0525. hilldusk 0.0870 / 0.0895.
    - towndusk: the first take drifted (a vs b 6.88, b vs a3 0.21). Retaken after a warm-up view: noise 0.0661,
      diff 0.0647 (max 17).
    - Explanation: nothing visible changed. The forest views have cached tree shadows on the ground in frame. The
      town and hillside views have few trees in the 25-120 m cache band, so they show less.
  - NaN: `[sparkle] r27 summary: 360 frames, 0 with markers` (town, High, -stopnan off).
  - Suite (also the rerun the coordinator asked for): `completed: 39/40 passed, 1 failed`. Both earlier flakes
    (WalkBy_NeverThroughWall, Place_PropFindsAndFramesWharf) passed. The fail looks like
    WorldTests.Player_CrowFliesOutOfCaveThroughHitches: its measure line says "3 stuck", worst frame 722 ms. That's a
    cave flight test, not the shader.
- NOT DONE: no player-build check. No BENCH job, since the cost doesn't change (same instructions, moved).
- Shots (milestones folder R-27): ab1/, ab2/ (towndusk retake), sheet-a.jpg (contact sheet of the a shots).
- Asks: review/merge `render/shadowcache-ddx`.
- NEXT: R-28 row 1 (cobble grout), then row 2, then R-20 row 3 (#308).

## R-28 row 1 (2026-11-14): cobble grout from the ground under the paving (#312)
- Branch `render/cobble-depth` fc00aa06, on main 1746d62a. 5 files, +50/-14: TerrainSource.cs, BiomeData.cs,
  BuildChunkMeshJob.cs, TerrainSurface.hlsl, TerrainSurface.shader.
- DONE:
  - `SurfaceProps.Under`: `SampleSurface` keeps the biome ground's albedo from before landmark paving is painted on.
    It rides the spare yzw of the porosity vertex stream (half4, +4 B per vertex). The grout is that ground,
    darkened and dirtied, so gaps read as sand in the desert town and wet soil in the fungal town. Moss only stays
    where the ground under is green. Hand-built SurfaceProps (scarp skin, debris, ScarpClay) leave Under at 0, and
    the shader then falls back to the albedo (the coordinator's ruling). The shader reads Under only under the cobble mask.
  - The cobble fade now uses screen size (`_PunchyTerrainDetailPpm`) instead of metres. Flagstones fade at twice the
    cobble distance. With the policy off it falls back to the old 30-80 m.
  - No fingerprint change: surface data only, and the landmark hash is unchanged (`landmarks 117 689D5348`).
- NOT DONE:
  - There is no cobbled temperate or ash town, so the fungal town (-2511,-1530) stands in for the damp, green case.
    The tundra town's street sits on soil, not snow, so its gaps are soil up close (lighter and snowier at 30 m).
  - Metre ranges still in the cobble path, left for row 2 or later: steep 40-90, mouth 30-60, sheen 120-450, rain
    ripple, grain 6-22, detailFade.
- Checks:
  - Compile clean, 0 shader messages. NaN `[sparkle] r28 summary: 360 frames, 0 with markers` (High).
  - merge-tree clean against origin/main and origin/world/scarps. Whoever merges second fixes the one adjacent line
    after SampleSurface in BuildChunkMeshJob.
  - Suite after the last change: `completed: 38/40 passed, 2 failed`. FolkTalkTests.WalkBy_NeverThroughWall ("no
    walk-by lines came up") and WorldTests.Place_PropFindsAndFramesWharf (stale LogWatch line). Both are the known
    flakes on a rerun in the same editor domain (REPORTS-world). It was the third PlayMode run in this editor in 35 min.
  - Crow cave (passed this run, no world changes on the branch): worst frame 725 ms, hitch 700 ms.
  - Other PlayMode suite runs (not mine) ran in the render editor during the shot runs (about 12:20-12:48). The first
    desert and fungal after shots came out cyan or white with no terrain, and I retook them once (`after_streamfail/`).
- Shots (milestones folder R-28/row1): `before/`, `after/`, `ba-Clear.jpg` and `ba-Rain.jpg` (before beside after,
  9 rows each), `C-check.jpg` (tundra: before / first factor / final), `rain-check.jpg`. The desert and fungal
  after shots use the first grout darkening factor. The final one differs only slightly in the desert (about 0.64 vs
  0.68 brightness), and the fungal town is unchanged.
- Asks: the +4 B per vertex goes into the row 2 BENCH ask (terrain mesh memory and GPU ms in a town, A = main,
  B = branch). Don't merge yet: row 2 adds commits to this branch.
- NEXT: R-28 row 2 (bevel-height parallax, stone normal replaces the ground normals under the mask), then R-20 row 4.

## R-28 row 2 (2026-11-14): bevel-height cobble parallax, stone normal under the mask (#312)
- Branch `render/cobble-depth` 4cd4edd8 (on fc00aa06, main 1746d62a). 2 files: TerrainSurface.shader, DetailPolicy.cs.
- DONE:
  - Height comes from the stone mask's edge-distance bevel: tops high and fairly flat (own height, slight lean,
    faint lumps), a sharp fall at a 1-2.5 cm rim (never under 1.5 pixels, so no sparkle), grout low and flat.
  - Offset parallax up close: steps from `DetailPolicy.TerrainParallaxSteps` (Low 0, Medium 2, High 3, Ultra 4).
    It fades in by the stones' size on screen (40-80 px across) and goes flat under puddles.
  - Rib fix (Zach's 11-14 note): a linear search, then the bracket is refined twice (regula falsi) and closed
    with a secant. Grazing views take up to twice the steps (8 at most), and each pixel's first step is dithered
    by a fixed screen pattern, so it can't crawl. The ribs on the stone rims are gone in the grazing shot.
  - Under the stone mask the stone normal replaces the ground's base and detail normals. The grout keeps the
    ground normal plus its own 2 cm grit, crossing over at the rim. Stone albedo comes from the paving colour,
    not the ground grain, so no ground pattern crosses a stone.
  - The remaining metre ranges in the paving path (steep/detail fade, mouth, grain, rain ripple, sheen) now use
    screen size (`_PunchyTerrainDetailPpm`). With the policy off they fall back to the old metres.
- NOT DONE:
  - No valid close (2 m) before shot of the pre-fix parallax survived: those runs came out cyan or white while
    the shader was still compiling. The close shot is after only. The grazing before beside after is valid.
  - No GPU cost number yet (BENCH ask below).
- Checks:
  - Compile clean, 0 shader messages. NaN `[sparkle] r28ribs summary: 360 frames, 0 with markers` (High).
  - merge-tree clean against origin/main 1746d62a.
  - Suite after the last change: `completed: 34/40 passed`. Failures:
    - LiveFolk_Answer, LiveFolk_BeastResponse and Player_CrowFliesOutOfCaveThroughHitches: BlobAssetReference
      "not valid" exceptions on a rerun in the same editor domain.
    - WalkBy_NeverThroughWall and Place_PropFindsAndFramesWharf: the known same-domain flakes (REPORTS-world).
    - Landmarks_V3 got `117 358257FC` instead of `689D5348`. Fresh bakes of the same code in this editor
      alternate between island hashes FA8F24B1 and A30264B9, so the bake looks nondeterministic. The branch's only
      script changes are row 1's Under/Soak (passed 689D5348 at row 1) and one DetailPolicy global. For world.
  - Transient: the first two rib-fix shot runs drew no terrain (async compile). The next run drew it, with no
    code change in between.
- Shots (milestones folder R-28/row2): `*-before-after.png` (row 2 against row 1, 10 views), `walk-00..05.png`,
  `graze-ribs-before-after.png` (rib fix, crop), `close-2m-ribfix-after.png`, `B-graze-ribfix-after.png`. The
  sun differs between the rib before and after (cloud cover), not the paving.
- Asks: BENCH job, Medium in a town, A = main, B = render/cobble-depth: terrain GPU ms (target < 0.1 ms
  added) and terrain mesh memory (+4 B per vertex from row 1). Review/merge after that.
- NEXT: R-32 rows 1-2 (branch render/steady-shadows), then R-34 row 1, R-32 rows 3-4, R-20 row 4, R-30 row 1,
  R-31, R-33, R-29.

## R-32 row 1 (#328): steady near sun shadows. Branch render/steady-shadows, head 67039809 (from main 1746d62a)
- DONE:
  - Cause confirmed: the sun turning. Still camera at the stable yard (2972,-891), noon, High, `-fixeddt`,
    pixels changed per 10 frames:
    - Clock frozen: board 0, bench 0, stair about 15.
    - Clock running: board 205-5725, bench 2275-3847, stair 623-1185, every frame pair.
  - Fix in WeatherSystem: the Sun's direction is held until the true direction is 0.1 degrees away, then moved
    in one step (about every 0.4 s at the default day length). Colour and strength stay smooth. Big jumps
    (sun-moon swap, clock set) are taken at once. Running after the fix: changes only on step frames, held in
    between (board 843/0/521/692/0).
  - Cascades, WideShadow and the flora cache: no code change needed. The stable fit already holds the cascade
    still for a still camera (frozen = 0), and the flora cache logged no redraws in the shots.
  - Editor versus player on main (fauna build of 11-13, main 1746d62a plus stables, which already has R-27
    #319), same spot, noon, High, same camera, shots about 2 s apart with the clock running:
    - The player's near shadows move between shots too (board 0.7-1.3%, stair 1.0-1.1% of pixels per pair;
      the bench crop also has a chicken in it). Editor on main over a similar span: board 6.2%, stair 2.1%.
      Frozen editor: 0. So the crawl is the sun turning in both, not an editor-only effect.
    - R-27 isn't the difference: the build has it, and so does this tree.
    - The player ran at 1920x1080, the editor game view at 2560x1600. Each shadow-texel step covers more
      screen pixels in the editor, and editors sharing the GPU pace frames unevenly. That probably explains why
      the crawl looked worse there. Not timed (rule 21).
- NOT DONE:
  - The player can't capture frame by frame (shot lists wait at least 1.5 s per line), so the player figures
    are per shot, not per frame. A/B of the fix in a player needs a build with this branch.
- Checks:
  - Compile clean. NaN `[sparkle] r32row1 summary: 360 frames, 0 with markers` (High).
  - Suite after the last change: `completed: 38/40 passed, 2 failed`. The failures are WalkBy_NeverThroughWall
    and Place_PropFindsAndFramesWharf, the known flakes (REPORTS-world).
  - Dusk (18:40) holds between steps. The cave 1 mouth renders correctly. Slow dolly walk: shadows steady.
- Shots (milestones folder R-32/row1): `still-strip-before-after.png` (6 frames, board and stair crops),
  `still-before-full.png`, `still-after-full.png`, `walk-after-00..05.png`, `dusk-after.png`,
  `cave-mouth-after.png`, `player/p0..p5.png` and `player/contact-sheet.jpg` (player on main).
  The stable in these shots is on the animal lane's A-117 work, not on main. The paving-wide diff is
  polluted by a dog and the fire, so the crops are the measure.
- Asks: none. Merge when convenient. The next step for Zach to see it is a player build with this branch.
- NEXT: R-19 -rockhighonly default commit on render/steep-triplanar, then R-20 row 1 (High-only 4096 map and
  wide filter), then R-32 rows 2-4.

## R-19 (#281): -rockhighonly is the default (2026-11-14)

- DONE: render/steep-triplanar head 2439c116 (PR #285). SteepRockCost.HighOnly now defaults on: steep-face beds
  and slabs from High up, Low and Medium keep colour and grain (BENCH job 73). "-rockhighonly 0" turns it off.
  One small commit. main (1746d62a) was merged into the branch (56ff0584) instead of a rebase, because the branch
  was already pushed and rebasing would rewrite it.
- Checks: compiles, 0 errors; _PunchyRockCost.x is 1 at Medium. NaN probe at Medium, 360 frames: 0 marked.
  Suite: completed: 39/40 passed, 1 failed (Place_PropFindsAndFramesWharf, a known flake).
- Shots: none (no change from what job 73 measured with the flag).
- Asks: none. Ready to merge for the 21:00 build.
- NEXT: R-36 row 1 (cloud shadows in caves), then R-20 row 1.

## R-36 row 1 (#338): no cloud shadows or sunlight under cave roofs (2026-11-14)

- DONE: render/cave-cloudshadow 41254017, from origin/main. Cave.shader and Sky.hlsl only.
  - Cause: Cave.shader's terrain share (the ground skin it blends in) was lit by the full sun and dimmed by
    the cloud shadow at any openness. In a shallow tunnel (cave 6, about 6 m under the ground) a wall glowed
    orange and showed drifting cloud shadows.
  - Fix: a shared `PunchySkyShade` in Sky.hlsl keeps the sun's share only as far as the sky is overhead
    (cave openness), cloud-shadowed. The cave's own daylight term takes the same single cloud read. Mouths fade
    with openness, so they keep sun and cloud.
  - TerrainSurface, Grass and Pebbles are unchanged: they showed no leak (they sit on open ground).
- Checks:
  - Same sequence for before and after (fresh play, noon pinned, Overcast). The cloud field was shifted about
    540 m by a hook, so only clouds differ.
    - Cave 12 walls: change 1.12 -> 0.05 grey levels (upper third), 1.35 -> 0.03 (right wall).
    - Cave 6 tunnel: the sunlit wall is gone (before against after, mean 13 levels). The cloud change is
      below 1 level on the walls. What is left is cave water, which animates over time.
    - Cave 6 mouth: unchanged, before against after mean 0.49 levels (wind). Outside cloud shadows unchanged.
  - NaN: `[sparkle] r36 summary: 360 frames, 0 with markers`.
  - Suite after the last change: `completed: 40/40 passed`.
- Shots (milestones folder R-36): `R36-c6-before-after.png`, `R36-c12-before-after.png`,
  `R36-m6-before-after.png`. Top row before, bottom row after: 4 frames about 6 s apart, and on the right the
  cloud-shift difference (grey means no change).
- Asks: none. Merge when convenient.
- NEXT: R-20 row 1 (High-only 4096 map and wide filter, new branch from origin/main).

## R-20 row 1 (#286): wide sun-shadow filter and 4096 map at High and above (2026-11-14)

- DONE: render/shadow-high, from origin/main 4df85493, two commits:
  - aacca6ac: SunShadowFilter.cs (ported from 1a380545). The sun's soft-shadow filter is High when the Shadows
    setting casts four cascades (High and Ultra); Medium and Low keep the scene's own (Low). `-sunshadowfilter
    scene|low|medium|high` overrides it. One line in docs/agents/rendering.md.
  - 59377453: GraphicsSettings.cs, 6 lines under the Job 71 exception: the sun's shadow map is 4096 at four
    cascades, else the asset's 2048. Set at run time on the pipeline asset, put back on quit (and on leaving
    play mode in the editor). No second pipeline asset, no asset file change.
- Checks:
  - Presets in play: Low and Medium give 2048 and filter Low; High and Ultra give 4096 and filter High. After
    leaving play the asset reads 2048 and `git status` is clean.
  - Still camera at the stable yard, High, noon, world frozen (time scale 0, so NPCs hold still), sun moved by
    its held 0.1-degree steps (R-32). Pixels changed (>4 levels, 960x540) on the three step frames:
    before (2048, scene filter) 947 / 593 / 3113, after 148 / 152 / 2466 (total 4677 -> 2820). The near
    shadows (tower, roofs, stair) nearly stop moving. The third step's remaining change sits on far roofs in the
    last cascade, and is the same in both.
  - With the clock running the frame pairs are dominated by walking NPCs and the sun's real turn, so they were
    not used as the measure.
  - Slow walk (camera sliding 0.3 m per frame, sun held): no swimming in either; after, the edges are softer.
  - Editor against player: R-32 row 1 already compared them (the crawl is the sun turning in both; the editor's
    2560x1600 game view shows each texel step over more pixels). An A/B of this row in a player needs a build.
  - No shader changed, so no NaN probe. Suite after the last change: `completed: 40/40 passed`.
- Shots (milestones folder R-20): `R20-row1-sunstep-before-after.png` (top before, bottom after; right, what one
  sun step changed, x10), `R20-row1-walk-before-after.png`.
- Asks: BENCH job for the cost of the 4096 map plus the wide filter at High (rule 21, not timed here). Merge when
  convenient.
- NEXT: R-32 row 2 (grass in every shadow tier), then rows 3-4, from a new branch off origin/main.

## R-32 row 2 (#328): grass blades take every shadow tier (2026-11-14)

- DONE: render/grass-shadow-tiers, from origin/main 3b192717, one commit 34f1b684 (two shaders only):
  - Grass.shader: blades now use URP's faded real-time shadow (the 3-argument GetMainLight) and the darker of that
    and WideShadow, as the ground does. The ForwardLit pass marks blade pixels with stencil bit 8.
  - Resources/FloraShadowCache.shader: the receiver code is shared. It has a second pass for blade pixels (stencil
    bit 8) that uses the ground's up normal, not a normal rebuilt from the blades' depth, which flips from pixel to
    pixel and made the cache's bias and facing test speckle. Every other pixel goes through the old pass, unchanged.
  - FloraSystem.ShadowCache.cs was not changed: RenderPrimitives draws both passes. So there is no world-lane note.
    Placement and hashes were not touched. The far tone is unchanged (rule 25).
- Checks:
  - A/B at the lakeshore meadow (470, 1840): a fresh play session per side, -weather Clear, two views
    (tree shadows on grass at about 20-110 m), 16:00 and 12:00, 1920x1080. After minus before: mean -0.2 to -0.8
    levels, and 3-6% of pixels move by more than 8 levels. The heat maps show that change is clouds, wind on the
    blades and a few animals that moved. No banding, no seam where the shadow tiers hand over, nothing brighter or
    darker over a whole area.
  - The blade pass runs: a temporary red tint on that pass alone turned every grass blade red, and only the blades
    (reverted, and the file matches the commit).
  - Not shown: the shaded-grass speckle getting less. On shaded grass in the middle distance a high-frequency
    measure is the same before and after, within 1% (a16 7.61/7.63, b16 13.08/13.18). These views have few
    cache-band shadows on grass, so the row's proof is weak. It needs Zach's eye where he saw it, or a spot with
    cliff shadow at about 60 m.
  - NaN: `-sparkleprobe` with Stop NaN off, meadow view: 360 frames, other bad 0, hot 0.
  - Suite after the last change: `completed: 40/40 passed`.
- Shots (milestones folder R-32): `row2-grass-shadow-ab.png` (left before, right after; top a16, bottom b12), and
  each view at full size as `row2-<view>-before/after.png`.
- Asks: a BENCH job for the blades' cost: two extra WideShadow taps per blade pixel, and the second full-screen
  receiver pass drawn over blade pixels only (rule 21, not timed here). Merge when convenient.
- NEXT: R-32 rows 3-4 (blue moon as the shadow light; blue-moon nights about one in ten), new branch from
  origin/main.

## R-37 row 1 (#354): sun shadow bias test sheet - DONE (Zach picks; supervisor sets PC_RPAsset)
- Branch `render/shadow-bias-test`, commit 2353f3d4 (one new file, off origin/main 244805ae). Not merged.
  - Rendering/SunShadowBias.cs: `-shadowbias <d>` and `-shadownormalbias <n>` (or the statics, from eval) put the
    biases on the scene's sun light for that run (light set to not use the pipeline's values). Same units as the
    asset's. The asset is never written (the card said "set the active URP asset's"; this avoids touching the
    supervisor-owned asset). Off unless a flag is given; the light gets its own values back on a sun change.
- Checks:
  - Three pairs (depth/normal 0.1/0.5 = the asset now, 0.2/0.25, 0.3/0) x noon and dusk (17:00, sun about 10 deg)
    x three subjects: grass (lakeshore meadow 470,1840), crop-plot fence and house roof (town at 2972,-885).
    Pinned camera, cycle paused, -weather Clear, 1920x1080.
  - Each set has a control (the 0.1/0.5 still shot again after the other two). Pixels moved > 8 levels vs 0.1/0.5,
    as mean levels (pair 0.2/0.25 | pair 0.3/0 | control): grass noon 3.0 | 3.5 | 3.2, grass dusk 3.0 | 4.0 | 4.4,
    fence noon 2.0 | 1.7 | 2.2, fence dusk 1.9 | 1.9 | 2.2, roof noon 9.0 | 9.0 | 9.9 (clouds drifting over the
    roof view), roof dusk 1.0 | 1.7 | 1.7. Every pair is within the control's noise (wind, walkers, clouds).
  - Read per pair, on 1:1 crops: 0.1/0.5 (now): no acne, no visible peter-panning. 0.2/0.25: looks the same in
    stills. 0.3/0: looks the same in stills; no acne at dusk even with zero normal bias. Thin casters (fence rails,
    grass) keep their shadows in all three. So on these subjects the stills can't separate the pairs; the
    edge break-up Zach asked about is a motion effect, and the clips are where to judge.
  - Clips: the roof view at dusk, cycle running, 5 s game time at 20 fps (fixed capture rate, so every clip covers
    the same sun steps; about 12 steps of 0.1 deg in 5 s, the shadow edges visibly move). Frame-to-frame pixels
    moving > 20 levels (median per frame): 0.1/0.5 423, 0.2/0.25 850, 0.3/0 420. The 0.2/0.25 clip's higher count
    is likely villagers walking through (not separated out); I wouldn't read it as worse.
  - NaN: `-sparkleprobe` with Stop NaN off, 0.3/0 set, town view: 360 frames, other bad 0 (hot 8).
  - Suite after the last change: `completed: 40/40 passed`.
- Shots (milestones folder R-37): `R-37-sheet.png` (rows: subject x noon/dusk, columns: the three pairs),
  the 18 stills as `<subject>-t<12|17>-d<depth>-n<normal>.png`, and the clips `roof-dusk-d<depth>-n<normal>.mp4`.
- Asks: Zach's pick from the clips; the supervisor sets PC_RPAsset. My read: none of the three changes the look
  in stills, so if he sees no difference in the clips, keep 0.1/0.5.
- NEXT: R-32 rows 3-4 (blue moon as the shadow light, blue-moon nights about one in ten), new branch from origin/main.

## R-32 rows 3-4: blue moon as the shadow light, blue nights about one in ten - DONE (hand-over strip UNVERIFIED)
- Branch `render/blue-moon-shadow-2`, commit f3cdb4e8 (off origin/main 244805ae; DayNightCycle.cs, WeatherSystem.cs).
  Not merged.
  - Row 4: `BlueMoonDays` default 5.3 -> 10. The blue light now follows `BlueMoonStrength` = (cosine fullness of
    the blue round)^`BlueMoonSharpness` (20), so only the full-blue night is strongly blue. Deterministic from the
    world clock. The sky disc (BlueMoonLit) is unchanged.
  - Row 3: when the blue light's luminance beats the big moon's (floor 0.08, about 45% of a full blue moon; 10%
    hysteresis), the shadowed main light and the unshadowed extra slot swap. Shadow strength fades out over 2.5 s,
    the swap happens at zero, then it fades back in. During the fade the main intensity eases halfway toward the
    extra slot's wrapped term (Glow.hlsl 0.85 N.L + 0.15) to hide the swap.
- Checks:
  - 30-night table (strength / peak glow): nights 10, 20 and 30 are 1.00. The nights either side peak at 0.22-0.25,
    nights 1, 9 and 11 are about 0.13, and all others are 0. That's 3 strong nights in 30.
  - Full blue, big moon new, from 18:00: the swap comes at about 18:30 (blue glow about 0.7). After it, the sun
    light points along the blue moon (intensity 1.84, colour 0.09/0.19/1) and shadows follow it all night.
  - Ordinary night (glow 0): the big moon casts, as before. Night before full (glow 0.13): the big moon keeps the
    shadows (the floor stops a faint blue moon taking them).
  - Hand-over clip (town, still camera, 120 frames at 10 fps game time). Mean frame-to-frame change at the swap
    frame against the clip median of 0.82: no ease 7.88 (blue drops 6.4 levels), full ease 6.67 (blue rises 1.6,
    mostly on walls and roofs facing the moon), half ease (committed) 3.03 (blue drops 2.2). It's a small, soft
    step, but still a step, and I'd used my one retake, so the strip is UNVERIFIED. Interpolating the three runs
    puts the zero at about 0.8 of the ease (a one-number change, not yet run).
  - NaN: `SparkleProbe` with Stop NaN off, across the hand-over: 360 frames, other bad 0 (hot 2).
  - Suite after the last change: `completed: 40/40 passed`.
- Shots (milestones folder R-32): `before-/after-town-bluemoon.png`, `before-/after-town-ordinary.png`,
  `before-/after-trees-bluemoon.png`, `after-town-nightbefore.png`, `after-bluemoonrise-handover.mp4`,
  `after-handover-strip.png` (frames 44, 64, 74, 83, 84 (swap), 94, 114).
- Asks: (1) the bench scripts final.sh, look-d3d12.sh and visual.sh pin `-moonphase 0.5 -bluemoondays 0` (full
  blue), so with this branch the blue moon becomes the shadow light in those benches. The profiler should
  re-baseline or pin a non-blue day. (2) Should I take the ease to 0.8 and re-clip as a follow-up row?
- NEXT: R-34 row 1 (wet-stone switch, default off; sheet to the coordinator first).

### R-32 rows 3-4 follow-up: hand-over ease 0.8 - NOT DONE (swap step)
- Branch `render/blue-moon-shadow-2`, commit 4b349531 (WeatherSystem.cs: `BlueMoonHandOverEase` 0.5 -> 0.8, and
  the comment now says what the ease can't do). Not merged. Stopped tuning as asked.
- Checks:
  - Same town clip, 120 frames. Swap-frame mean change 3.2 against a clip median of 0.85 (p95 0.88), so it is
    still a visible step. The mean colour barely moves (R +0.4, G +0.7, B +0.7), but the diff map splits:
    walls, roofs and the tree line brighten, the flat ground darkens. 15.5% of pixels go up by more than 8 levels
    and 11% go down. By thirds of the frame (top to bottom) the step is 3.7 / 7.0 / 7.2.
  - Reading: one scalar on the main light can't match both the extra slot's wrapped term on flat ground and its
    near-equal strength on faces toward the moon. A real fix would keep both lights' shares continuous, e.g.
    crossfade the extra slot's intensity across the fade rather than easing the main light. I haven't tried it.
  - Suite after the last code change: `completed: 40/40 passed`. A comment-only edit followed, and the compile
    after it is clean (0 `error CS`). NaN was measured at 0.5 (0 bad); the ease doesn't touch any division.
- Shots (milestones folder R-32): `after-bluemoonrise-handover.mp4` and `after-handover-strip.png` (frames 17,
  27, 37, 46, 47 (swap), 57, 77) are replaced by the 0.8 run.
- Asks: should the crossfade fix become a card row, or is the soft step acceptable for a one-in-ten night?
  Noted: BENCH-QUEUE note 93 (profiler pins an ordinary night in the three bench scripts, plus a blue-night
  baseline). I'm not touching the bench scripts.
- NEXT: R-34 row 1 (wet-stone switch, default off; sheet to the coordinator first).

### R-32 row 5: hand-over crossfade - DONE
- Branch `render/blue-moon-shadow-2`, commit 493499fd (WeatherSystem.cs + Glow.hlsl). Not merged; rows 3-5 merge
  together. It replaces the 0.8 ease.
- How: as the shadows fade out, the main light fades out with them and the same moon fades into a new unshadowed
  slot in Glow.hlsl (`_PunchyHandOverLight*`, shaded like the blue moon's term). At the bottom of the fade both
  moons are in unshadowed slots, so the swap only relabels them. The slot is 0 outside a hand-over, so the shader
  cost is one uniform branch. All eight PunchyGlowLight users get it (rule 26).
- Checks:
  - Same town clip, 120 frames. Clip noise is median 0.78, p95 0.88. The fade bottom (luma minimum) is frame 65;
    the steps into and out of it are 0.65 and 0.65. Every step from frame 2 to 119 is 0.90 or less (the largest
    is frame 33, mid fade-down). The swap step was 3.2 with the 0.8 ease and 7.88 with no ease. Frames 0-1 (1.30)
    are the clip start right after the time jump. The swap frame is inferred from the fade bottom; the probe's
    per-frame shadow log wasn't kept.
  - Ordinary night (moon phase 0.5, blue 5 days): hand-over slot reads 0. Town shot against the rows 3-4 shot
    differs by mean 4.14; a rerun of the same build against itself differs by 4.81, so it's within run noise.
  - NaN across a hand-over (SparkleProbe r32r5, 360 frames): 0 bad.
  - Suite after the last code change: `completed: 40/40 passed`.
- Shots (milestones folder R-32): `after-bluemoonrise-handover.mp4` and `after-handover-strip.png` (frames 40,
  52, 60, 64, 65 (fade bottom), 66, 70, 78, 90) are replaced by the row 5 run.
- Asks: none.
- NEXT: R-34 row 1 (wet-stone switch, default off; sheet to the coordinator first).

## R-34 row 1 (paused for Zach's playtest, HOLD)
- NOT DONE. Paused: the code is written but not compiled, shot or tested. It is pushed as WIP, unverified, on
  render/stone-trial (1c12cd97 + c2b2c0d1 the meta). Stone.hlsl holds the shared look behind `_PunchyStoneTrial` (0
  off by default, 1 dry, 2 wet). It is hooked into Flora (boulders; built stone for now = non-living, non-timber part 0
  pieces), TerrainSurface (rock faces) and Cave (walls).
- NEXT once HOLD is gone: compile check, then off == main pixel diff, the four-stone sheet (current / dry / wet, 2 m
  and 15 m, noon and low sun, plus a far view), NaN probe and suite. After that, the handoff.

### R-34 row 1 (#332): wet-stone trial - DONE (verified)
- Branch `render/stone-trial`, f6c80956 (on top of the earlier WIP commits 1c12cd97, c2b2c0d1 and 2bfad516). Not merged.
  `-stonetrial 0/1/2` (off by default, dry, wet) sets `_PunchyStoneTrial`, a cosmetic, local global.
- Fixes since the WIP: an FXC crash in Terrain Surface ForwardLit (a second inline copy of the glow loops; now only
  the sky term follows the stone normal), explicit mips from the footprint so the octaves are safe inside branches,
  and guarded weights and half vector (rule 24).
- Checks (pinned clouds, local helper only, not committed):
  - Off == main: stone poses (cliff, cave, far) differ by a mean of 0.1-1.0 /255. Boulder and wall poses differ by
    0.8-5.4, from grass, sway and animals rather than stone.
  - Far view: no tone shift. Off vs dry is 1.5 (luma 153.4 -> 154.3); off vs wet is 2.6 (-> 153.7).
  - Biggest change: the close cliff (dry 14.7, wet 27.2) and wet cave at 2 m (luma 38 -> 52.5, lantern gloss). Walls
    barely change.
  - Sparkle probe, wet, NaN guard off, 4 close poses x 360 frames: 0 marked, 0 bad, 0 hot.
  - Suite: 40 passed, 0 failed, 17 skipped.
- Sheet: milestones folder R-34, `stone-sheet.jpg`. Rows are boulder, cliff, cave and wall at 2 m and 15 m, plus a far
  view; columns are off, dry and wet, at noon and 17:30. Raw shots are in `R-34/off|dry|wet|main/`, probes in `R-34/wet/`.
- For Zach / row 2: the trial thins boulder moss when dry and removes it when wet (wet reads as glossy bare stone).
- Cost class: 3 octaves x 3 triplanar taps (the finest one skipped when sub-pixel), plus two spec lobes when wet. Off
  costs one uniform branch. BENCH ask: dry and wet against off at the cliff and boulder poses.
- World lane: Cave.shader has a small hook (the walls call PunchyStoneSurface behind the global). Tell them before
  they touch cave walls.
- Shares: Stone.hlsl is one stone include shared by TerrainSurface (rock faces), Flora (boulders, built stone) and
  Cave (walls). It should become, or fold into, W-166's stone family include once that lands; W-166 has no code yet.
  "Built stone" is picked inside Flora (non-living, non-timber part 0) until W-166 states materials per kit piece.
- NEXT: merge origin/main into render/stone-trial, then R-38 row 0 (sun step trial).

## R-38 row 0 (#366): 0.1 s sun step trial, done (verified)

- Branch `render/playtest-1114` (new from origin/main), commit cf8ef6e9, pushed. The supervisor can build it for Zach.
- Launch flag `-sunstep <seconds>`: the shadow light steps by time instead of by angle. `-sunstep 0` steps every frame.
  A jump of 2 degrees or more (sun to moon swap, clock set) is still taken at once. Without the flag, today's
  0.1 degree step (about every 0.4 s) is unchanged. No Graphics setting yet: it is a trial flag. A setting can follow if
  Zach keeps it.
- Fauna owns WeatherSystem.cs. Only `SteppedLightDirection` and a new small `SunStepArg` reader beside it changed.
  The call site needed no change.
- Strips: 20 frames, 0.1 s of game time apart (fixed step), still camera, clock running from 17.55. The spot is the
  village dip at -3212.6,1611.6. Default: D:/Unity/PunchyMilestones/R-38/default/strip. `-sunstep 0.1`:
  D:/Unity/PunchyMilestones/R-38/step01/strip. Contact sheet: D:/Unity/PunchyMilestones/R-38/R-38-strip-contact.png.
- Numbers (ground region, share of pixels changing by more than 2 grey levels frame to frame):
  - Default: about 0.25% per frame, with a spike to about 0.37% every 4th frame. That spike is the 0.4 s shadow step.
  - 0.1 s: about 0.36% on 3 frames of every 4 (one frame in 4 is skipped by the 1/30 s game step), and no spikes.
  - Changes of more than 8 grey levels are grass and wind only, and match in both runs (about 0.1%).
- Crawl note: near shadow edges (house base, cobbles) move by at most about 2 grey levels per step in both modes, so
  neither mode shows visible crawl here at a low evening sun. Default gives a faint tick every 0.4 s. 0.1 s spreads
  the same motion into a fainter shimmer on most frames. Not tested: a midday sun, or long shadows on flat ground.
- Risk and cost: with 0.1 s, the shadow maps redraw about 4 times as often. Cost goes to BENCH (rule 21), not timed here.
- Shares: this extends WeatherSystem's existing R-32 held shadow direction. It is the same function with a second
  step rule, not a new system.
- NEXT: land render/far-lights, audit-fixes, cobble-depth and one-surface on main, then R-38 rows 1-5.

## Landing batch (supervisor task): old render branches on main, profiler branch review, archive tags

- Ready for PR (origin/main merged in, compile clean, all Assets/Game shaders 0 errors, play smoke 0 errors):
  - `render/one-surface` 086e56b1.
  - `render/far-lights` 5fe0ce36. CaveSystem.cs keeps main's fuller `ReleaseLandmarkPools` (in CaveSystem.LandmarkWater.cs).
    Street lamps go through `LampRoutes.LitAt` (A-115) once, in `AddStreetLight`, so far and near fixtures share it.
    Night play at tod 21.5, about 3,870 frames: 0 errors, 2 warnings.
  - `render/audit-fixes` 9cbf889b. WaterPatchSystem.cs: main's vents/knocks/viscosity kept, with HEAD's `Room` cap
    and turn stats on top. 2,634 frames: 0 errors (spawn not near water, so the water path is only lightly run).
- BLOCKED, `render/cobble-depth` (merge aborted, nothing pushed): it clashes with world's W-169 in BuildChunkMeshJob.cs,
  TerrainSurface.hlsl and TerrainSurface.shader. Both use the terrain `Soak` vertex value (TEXCOORD4): R-28 makes it
  half4 (porosity + under-paving colour), W-169 makes it half2 (porosity + coarser-LOD hollow depth). Keeping both
  needs 5 values: a vertex format change in world-owned files. Decision needed: world packs it, or R-28 drops the
  under-paving colour (rule 11).
- Profiler branches: `b/fix-swapchain-stall` and `b/cloud-shadow-map` merge clean onto main together and compile
  (30 shaders, 0 errors). swapchain: `-vsync`/`-maxqueued` flags, defaults as today. cloud-shadow-map: a 1024 RHalf
  cloud shadow map baked every 2 frames, ON by default (`-cloudshadowmap 0` turns it off). It edits fauna's
  WeatherSystem.cs, but merges clean with R-38 row 0 (cf8ef6e9).
- Archive tags: covered on main: light/forwardplus, b/r10-trims, b/r10-nocluster (far-lights), lod/screen-size,
  shadow-still, cloud-shadow-soft, b/r12-trims, r12-warp-bake, water/fine-grid, render/shadow-bias-test.
  Not really covered:
  - `b/water-rows-fixes`: far wave field (normals-only wakes past the fine field), wave fields on cave water, WaterTess
    tessellation, and the Water shader split into pass/material includes. Bears on R-38 row 5 (#269).
  - `b/fix-waterfield-step`: the wave field sleeping when idle, and step rate fixes. Nothing like it on main.
  - R-20 row 1's 4096 sun shadow map: main is still 2048. Bears on the sun crawl (#286, R-38 row 0).
- Shares: no new systems. far-lights folds near and far lamp lit-state into one `LampRoutes.LitAt` path; audit-fixes
  folds its cap into main's source job. Parallel found, not folded: cobble-depth's terrain wetness vs W-169 puddle fade.
- NEXT: R-38 rows 1-5.

## R-38 (#366), branch render/playtest-1114

- Row 0 (sun every 0.1 s, trial behind a flag): cf8ef6e9, reported earlier. WeatherSystem.cs is fauna's.
- Row 1, done (verified) for (a)(c)(d), d1b79af6: window spill fades out at its box's top and sides (no hard line
  where stacked panes meet, no light up through roofs or eaves), glass glow 2.2 -> 1.5 (Flora.shader).
  (b) BLOCKED: a lit hour per pane needs world's SurfaceGlow.Spill.cs and its scan, plus the shared
  FloraGenerator.Kit Windows list. Ask world, or give the row to world.
- Row 2 (#302, lights drop out as you walk towards them): NOT DONE here. The far/middle/near handover is the
  `render/far-lights` branch (R-10 row 3 "real lights hand over to glow without pops", R-25 row 3 "town glow map fades
  in by screen size"), ready for PR as 5fe0ce36. The fades it sets are in world's CaveSystem.cs / SurfaceGlow. Shoot
  the 8-frame walk-in strip on main once far-lights lands; fix whatever drop-out is left then.
- Row 3, done (verified), 635c59da: town and island glow maps read with a smooth B-spline filter (4 bilinear taps)
  in Glow.hlsl, not 1. The light pool round a town from about 400 m is now round, not a blocky cross.
  Shots: D:/Unity/PunchyMilestones/R-38/row3/ (before, after3, pair_town_after3.jpg). The sizes are #defines that
  must match SurfaceGlow.MapSize (160) and WorldMapSize (512). GetDimensions broke some variants.
  BENCH request: glow map reads cost 4 taps instead of 1 on terrain, flora, grass, creature and cave pixels at night.
  Note: the first play after the edit showed magenta sea or flora while variants were still compiling. A second
  run was clean.
- Row 4, done (verified by construction, shots show the shafts still render), c1b30b46: underwater shafts trace
  each point up the one refracted sun ray (parallel, fixed in the world), not towards a source tied to the camera,
  so they don't slide as you swim. The 7-frame swim strip at (2950..2959, -6, 2970) can't prove the anchoring,
  because the shaft noise drifts with _PunchyClock between frames (frame-to-frame match: before 0.40-0.70, after
  -0.15-0.55). Shots: D:/Unity/PunchyMilestones/R-38/row4/{before,after}.
- Row 5 (#269, real vertex displacement for touched water): BLOCKED on world. The water surfaces and
  WaterPatchSystem are theirs. The archived `b/water-rows-fixes` (WaterTess tessellation, wave fields on cave water)
  and `b/fix-waterfield-step` (wave field sleeping, step rate) already hold most of it. Revive those with world.
- Shares: Glow.hlsl's PunchySampleSmooth is the one smooth read for every coarse light map (town, night, island).
  Every surface shader picks it up through the shared include. Parallel found, not folded: Water.shader doesn't
  include Glow.hlsl, so water gets no glow-map light.
- NEXT: R-34 row 2.

## R-20 row 4 (no open issue; #286 closed) - blocky cloud shadows - done (verified, effect small at test spots)
- Branch `render/cloud-blocks` a0311856 (from main 37750588), Sky.hlsl only. PunchyMapSmoothUV: one tap per read, the read point eased inside its texel (quintic), used by PunchyWeatherAt, PunchyCloudShapeAt, PunchyWetnessAt and PunchyCloudStyleAt, so sky and shadow stay aligned. Cloud shadow edge: smoothstep penumbra widening ~9 m per km of cloud height. `-skyoff 2048` = old. No BENCH needed (no extra taps).
- Shots: D:/Unity/PunchyMilestones/R-20/row4/run1 (noon and 17:00; top 900 m, ground 30 m, sky; _before = skyoff 2048), pairs_t12.jpg, pairs_t17.jpg. Sky volume unchanged in shape (diff mean 1.5/255). NaN lint: no hits on the new lines. Variant-compile magenta on first play only (q1), clean on rerun.
- Finding: under forced PartlyCloudy, cloud shadows on the ground are faint at every spot scanned (6 top views, scan/), so the edge change shows little. The big square/stair-step blocks seen from the air are static terrain geometry (unchanged with clouds, weather volumes and maps off: vol/sheet.jpg at 0,0) - world/scarp (W-162), not cloud shadow. Worth asking Zach which view showed him the squares.
- Shares: extends the shared Sky.hlsl map reads (one fix serves sky march, cloud shadow, wetness, puddles); parked render/cloud-shadow-soft 560d14e7 not folded in (screen-size sharpness, also unmeasurable).
- NEXT: R-30 row 1.

## R-30 row 1 (#323) - leaf look exploration - started, stopped for v0.16 (not verified)
- Branch `render/foliage-realism` (from main 40478293), WIP commit: FloraMeshBuilder leaf-trial helpers only (cards, normal-offset shells, sphere normals); nothing calls them yet, not compiled or shot.
- Plan for resuming: mark cut-out foliage in vertex colour alpha (unused on foliage), cut in FloraMossClip so every pass (lit, shadow, depth, cache, impostor bake) matches; leaf-shaped 3D cell noise in the plant frame, fading to solid by pixel footprint (rule 20). Trials by `-leaftrial a|b|c`, one play session each; (d) as analysis plus atlas memory. Shoot with `-place tree:Oak` / Pine / Bush.
- NEXT: resume R-30 row 1, then R-31, R-33, R-29, R-35.

## R-34 row 2 (#332) - wet stone as the base rock - done (verified)
- Branch `render/stone-trial` 550ba9e0 (merged main 4c423eba first, at da1b9777). Stone.hlsl is now the base surface for terrain rock faces (CliffKit pieces too, through the terrain material), boulders and built stone (Flora part 0, not wood, not living), dressed cave walls, and the terrain share at cave mouths. It replaces the rock grain/mottle and the cave detail nudge and tone, so the noise isn't doubled. R-19 beds, ledges and slabs, biome tints and far tone are unchanged. The trial switch is gone. StoneTrial became StoneWetDebug: `-stonewet 0..1` forces a minimum wetness, for shots only.
- Wetness: the spec lobes, Fresnel and darkening follow real wetness only. Terrain uses max(rain wetness, shore band / fall spray). Caves use damp. Flora uses rain only (gap: boulders by rivers get no spray wetness, because Flora has no shore term).
- Shots: D:/Unity/PunchyMilestones/R-34/row2/stone-row2-sheet.jpg has before (row 1 `off`) / dry / wet at noon and 17:30, at all row 1 spots; the raw images are in row2/{dry,wet}. Dry: the rock grain is now the stone relief, the change is small at mid range, and the far view is unchanged. Wet (-stonewet 1): glossy, darker stone on boulders, cliffs, cave walls and wall blocks. NaN/sparkle probe: 0 marked frames at boulder-2, cliff-2, cave-2, wall-2. Shader messages are warnings only (existing ones).
- Suite: 39/41 PlayMode. The fails are the known pair: Player_CrowFliesOutOfCaveThroughHitches timed out, and CaveWater_SurfaceMatchesAndPoolStirs failed as its knock-on (both seen before on R-27 and world runs; gameplay only, not shaders).
- Blocky squares: a 700 m top-down shot in clear weather (row2/dry/high-t12.png, over 3330,-430) shows dark blocks only on the mesa/scarp relief and its sun shadows, with no clouds in the sky. This agrees with R-20 r4: they are terrain geometry (W-162), not cloud shadow.
- BENCH ask: compare main vs render/stone-trial at the cliff-2 and boulder-2 poses (GPU ms). Stone replaces two triplanar detail reads with its own octaves, and wet adds two lobes.
- Tell world: Cave.shader (world-owned) now calls the shared stone surface for dressed walls and the terrain share at cave mouths (ice, crystal, bone and wood are excluded by stoneShare). W-162/W-166 should use PunchyStoneSurface/Spec/Fresnel for scarp and library stone, not new rock noise (rule 33).
- Shares: Stone.hlsl is the one stone family (terrain, flora, cave). Parallel found, not folded: the steep/mouth grain on non-rock steep ground in TerrainSurface, and plaster still gets stone until W-166 gives it a marker.
- One slip: the first commit 8353ef43 holds only the rename (a failed add); 550ba9e0 holds the code. Squash on merge.
- NEXT: R-25 r3 (far-lights merge + approach check).

## R-25 row 3 (#302) + R-38 row 2 (#366) - far lights land, no pop on approach - done (verified)
- Branch `render/far-lights` 4b95c112 (merged main; merges clean with main 243c7eb1). One conflict, Glow.hlsl: both reads use PunchySampleSmooth, the night read gated by `[branch] if (nightFade > 0)`. Night street-light layer weight checked nonzero after the merge.
- Walk-in strip, human village at 22:00 Clear, 23 frames from 1200 m to 12 m: D:/Unity/PunchyMilestones/R-25/r3/town-walkin-strip.jpg. Lights read as continuous points from about 600 m inward. Warm px climb 7 (600 m) to 5843 (12 m), with two dips, both explained: 250 m (a ridge in the way) and 37 m (a lit house leaving the frame). No frame where the lights drop out. Elf grove strip (elf-walkin-strip.jpg) is supporting only: there the glowing ground fungi set the count.
- R-38 row 2 is met by the same strip: R-38 rows 0/1/3/4 (635c59da, c1b30b46) are already in this branch.
- NaN/sparkle probe in town at night: 282 frames, 0 marked.
- Suite: 41/43 PlayMode. The fails are the known pair (Player_CrowFliesOutOfCaveThroughHitches timed out; CaveWater_SurfaceMatchesAndPoolStirs failed as its knock-on).
- Still asked: BENCH job 76 (R-25 row 3 cost) should pass before merge.
- Shares: Glow.hlsl PunchySampleSmooth stays the one coarse light map read (town, night, island).
- Leftover reviews: b/fix-swapchain-stall c9672fcc looks fine and merges clean. Small note: `-maxqueued` accepts 0, but Unity's range is 1-10, so clamp q>=1. b/cloud-shadow-map 88ee4c89 merges clean but needs rework: its baked path returns saturate(density*3)*thickness, which skips main's R-20 r4 penumbra. It should store raw density and apply the edge after the read. It also needs a BENCH check, and WeatherSystem.cs is shared. render/audit-fixes, cobble-depth and one-surface now conflict with main. audit-fixes touches WaterPatchSystem (world-owned), so I'm asking before resolving it (rule 11).
- NEXT: R-30 row 1.

## R-34 row 2 (#332) - main merged after #423 - ready for PR (verified)
- Branch `render/stone-trial` 03e09d9d. Merged origin/main. Flora and TerrainSurface merged without conflicts. In Cave.shader, the light loop now uses R-10's `toLight` (no `l` under real lights), so the wet stone highlight from lanterns uses toLight too. Ran AssetDatabase.Refresh, compile clean.
- Re-shot with -stonewet 1: D:/Unity/PunchyMilestones/R-34/row2b/wet-merged-sheet.jpg (boulder-2, cliff-2, cave-2, wall-2 at noon; boulder-2 and cave-2 at 17:30). Wet stone is glossy and dark on boulders, wall blocks and the cave wall, the same as before the merge. NaN/sparkle probe at cave-2: 172 frames, 0 marked.
- BENCH note: on the current layout the cliff-2 pose (3258.6,-428.7) now looks at open water, not cliff. Use boulder-2 and wall-2 (-3224.5,1602.0) for the main-vs-branch job, or tell me and I'll scout a new cliff pose.
- NEXT: R-30 row 1.
