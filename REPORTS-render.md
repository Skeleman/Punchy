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

## R-39 (#401 #402 #302 #269) - render-2 - all four rows done (verified), ready for PR
- Branch `render/playtest-1115` f836b277, based on far-lights (b934101b merges origin/main into far-lights; Glow.hlsl keeps far-lights' tiers with main's smooth map reads). The previous editor session's scene backup was moved (not deleted) to my scratchpad.
- Row 1 (#401, also #302), ba994915: SurfaceGlow near scan now reaches at least the AmbientGlow hand-off's 3D far end (was cut at 30 m ground distance inside the 37 m hand-off; CaveSystem.SurfaceGlow passes the reach); LandmarkEffectSystem flames and beacons pick by on-screen size (TorchFlamePixels, 3D distance) instead of a flat 150 m, crossfading to the far flame; TownDome.shader near fade by 3D camera distance. LandmarkEffectSystem is in Scripts/Ambience (fauna lane): additive, tell fauna. Glide at 150 m over the human village at night: R-39/row1 g00-g09, town glow steady in every frame, no switch.
- Row 2 (#402), f836b277: Zach's o3 is the only path (GlowLook constants: nights keep 35% ambient/moon, glow slots x2.6, reach x1.15, power 3; the -glowlook flag and o0-o2 are gone). Darker night haze (his OK 11-15): Sky.hlsl PunchyAtmosphere scales the haze colour by _PunchyNightHaze = lerp(1, 0.5, Night). Re-shot: R-39/row2c/compare-row2b-vs-row2c.jpg (left the trial o3, right landed; raw in row2e igloo, row2f mushroom, row2c crystals). Far hills sink darker behind the igloos, glow pools as picked. Day/dusk check (numbers): 12:00 Night 0 haze 1.0 (day unchanged exactly), 18:00 Night 0.16 haze 0.92, 19:00+ Night 1 haze 0.5.
- Row 2 caveat: the purple gill glow on the big mushroom cap differs between frames (it was also faint in the trial's o0-o2 shots), so it is not from this change. The first shot after a long shader compile can come out a flat dark frame; retakes are clean.
- Row 2 cross-lane: WeatherSystem.cs (fauna-owned, CRLF kept) gains one additive line after the night look: GlowLook.UploadNight(Cycle.Night). CaveSystem.SurfaceGlow (world) reads GlowLook.Brightness.
- Row 3 (#302): walk-in strip R-39/row3c d01-d11 (200 m to 11 m, eye 25 m up, bearing 120, igloo town 22:00): igloo windows lit at 160 m and 120 m, warm pools from 90 m once the ridge stops hiding the ground. No frame without lights. "real 0" at every distance: igloo town has no real lights, slots and far maps carry it, so no pop. No code beyond row 1's 3D NearReach.
- Row 4 (#269), f54a3859 + 8ff34bfa: WaterPatchSystem (world-owned) additive only: new WadeRebound = 0.4 field, multiplying the speed share of the trailing dimple and shove for upright waders (about 2.5x less rebound); bow wave and stride rings untouched. Shots: R-39/row4/before (rebound 1) and row4/after (0.4), fox running through 0.95 m water at (-3207,-1605) to (-3189,-1587). Rings and leading swell kept in both; the trailing height is hard to judge from the side, so the 2.5x is from the code factor.
- No suite run, no timing (rule 21). BENCH ask: none needed beyond the haze multiply (one per pixel).
- Shares: Glow.hlsl's one glow falloff (all glow slots), Sky.hlsl's one haze path (every surface), DetailPolicy.ScreenSize for flames. Parallel not folded: none found.
- NEXT: ready for PR (supervisor). Then R-30 row 1.

## R-25 row 5 (#302): lava far form + Ember Forest walk-in (render-2, render/far-lights-2 2769270d)
- DONE (verified in part): surface lava's far points were FarLightKind.Fire, so they lit only with tonight's forest fire and took its colour. Lava is now its own row in FarLightPolicy (Lava, Fire tier, lava colour, lit every night, core 6 m + halo 4x so a field merges, hand-off from lava's 30 m glow reach at 90-56 px = 300-500 m by screen size). KindCount 8, FarLights.shader arrays 8.
- NOT VERIFIED, no target: the current island has no surface lava. The world glow bake logs 0 lava texels; a 6 m scan of the south half finds no lava glow; the VolcanoCone landmark type (the only lava water) has 0 instances, and the Volcano/VolcanoCone debug locations are at sea (h -18). The "fissure" at -805,-3398 isn't lava. Placing lava is a layout change, so I left it alone; the code is ready for when lava returns. Ask: should a VolcanoCone (or another lava source) go back into the layout? That's the world lane's call.
- Ember Forest walk-in at ground level, 22:00, Clear, far lights on/off, 500 m to 10 m, bearing north (target -261,-3393): D:/Unity/PunchyMilestones/R-25/row5/ember-on, ember-off, sheet ember-sheet-a.png (500/400/200 m). On and off look the same: centre-crop mean 81/83, 88/86, 109/113. The far points 200-500 m out are no brighter than the plants' own glow, so nothing to match. Row 4's "larger and brighter" was from 300 m up, not at ground level.
- Sparkle probe (rule 24) at the emberlava vantage, night, far lights on: 360 frames, 0 marked, other bad 0.
- Suite: runs at the row 6 tip (one editor close for both rows). No timing (rule 21). BENCH ask: none; the lava kind adds no points today and is one more table row.
- Shares: FarLightPolicy's one kind table and FarLights.shader (all far points). Parallel not folded: beacon fires are also Fire kind (gated on the forest fire); left alone, not on this card.
- NEXT: row 6 (warm town far points).

## R-25 row 6 (#302): warm town far points keep their colour (render-2, render/far-lights-2 92253cd3)
- DONE (verified): town window and street-lamp far points drew at 5-7x HDR and clipped to white under the Neutral tonemap, so the town read white from afar but orange up close. FarLights.shader now eases each point's brightest channel under FarLightPolicy.CoreCap = 1.5 (knee at 0.4 of the cap), scaling the other channels by the same factor. The faint halo is untouched. The cap is divided by the bat-eyes exposure lift so it holds on screen. 3 files, +22/-4.
- Townfar (1 km, 22:00), town crop: near-white pixels 53 old -> 7 new (cap 2.5 left 30, cap 1.0 left 4 but dimmed). Lit pixel count 615 -> 603, so brightness holds. D:/Unity/PunchyMilestones/R-25/row6/deliver: townfar-old/-off, townfar-old-crop vs townfar-new-crop, fly1000-oc/o1/o0.
- Fly1000 (1000 m up): the town is tiny either way. The blue Glow-tier points keep their count (501 new vs 512 old bright pixels), so the cap doesn't wash out the cool kinds.
- Walk-in strip, eye 25 m up, bearing south, 500 m to 100 m: deliver/walkin-strip.png (raw in row6/walk2). There is no colour step at the far-to-near switch. The bright-pixel hue moves gradually (g/r 0.91, 0.91, 0.85, 0.79, 0.75, 0.77, 0.84 from 500 m to 100 m) and the top-200 brightness stays at 226-212. The far dots still read creamier than the near orange ground pools; the remaining drift comes from the halo over the blue night and the tonemap, not a jump.
- Sparkle probe (rule 24), night, far lights on: 360 frames, 0 marked, other bad 0.
- Playmode suite at the tip: 50 passed, 1 failed, 17 skipped. The failure is WorldTests.Fauna_Fingerprint. The branch is behind origin/main, and this row only touches far-light drawing (no placement or fauna), so I'm leaving it for a re-run after the merge. The known-flaky CrowFlies and CaveWater tests passed.
- Row 4 was done 11-14 by the render lane. Row 5 is partly verified (no surface lava to test against).
- No timing (rule 21). BENCH ask: none; this is a few ALU per far-point pixel.
- Shares: FarLightPolicy's kind table and FarLights.shader: the cap applies to every far light kind.
- NEXT: card done; #302 stays open for the lava re-verify.

## 11-15 render: rule 11 merges compile-checked; R-30 r1 blocked by editor hangs (RESUMABLE)
- cobble-depth 589ceeb6: TerrainSurface compiles and C# is clean. The vertex is 4 bytes larger than on main (Soak is half4, carrying R-28's under-paving albedo packed beside W-169's hollow). BENCH item.
- one-surface 1a3b0f12: Flora, FloraImpostor and TerrainSurface compile and C# is clean. Surface.hlsl gains PUNCHY_SURFACE_REAL_LIGHTS (lit.lights), glowWorldReach (0 skips the far glow lookup) and positionCS. The lantern fallback only runs without Forward+ real lights.
- audit-fixes fa29fc64 does NOT compile: my merge script rewrote the body of the Room property itself (CS0019). The one-line fix is on render/audit-fixes-wip 6d320e85 (fold into R-15). With it, C# is clean. Nothing was pushed to render/audit-fixes. The WaterPatchSystem hunks against main are all R-15: the SourceTurn struct and summary; the Turns/Stats fields and Room; MaxSources checks become Room inside SourceJob only; Creatures() turn order (players first, then nearest the centre) and Stats, plus a fallback after the loop; persistent presses/splashes/turns/sourceStats with OnCreate alloc, OnDestroy dispose and Clear() in place of TempJob; Turns and Stats passed to the job; the Sources/PlayerSources/SquareCreatures probes in place of the disposes.
- R-30 r1: the -leaftrial a/b/c build is on render/foliage-realism 9cec43a5. Note drafted in PunchyMilestones R-30/row1/note.md (cost classes, how the impostor and shadow cache follow, the screen-size fade, (d) at about 11 MB per species at 64 px or about 270 MB for 25 species). No shots yet: the editor hung twice ("Hold on" for 30+ minutes at idle CPU, after the Burst "Unable to create a SharedStatic" error) with 0.5-1 GB of RAM free and about 17 editors on the machine. I killed my own PID both times.
- NEXT: when >= 6 GB is free, start the editor on render/foliage-realism, then run runmode.sh trial.txt into row1/{off,a,b,c} (adding -leaftrial a|b|c). Then build the sheet, fill in the recommendation, and send the BENCH ask (in the note).

## 11-15 (later) R-30 r1: shot sheet and recommendation
- Ran -leaftrial off, a, b and c on render/foliage-realism 9cec43a5: 10 shots each (oak and pine, noon and backlit, 5 m and 30 m, plus the R-22 lake vantage), with no hang. Sheet: PunchyMilestones R-30/row1/sheet.png. Frames are in row1/{off,a,b,c}.
- What it shows: (b) shells reads most as leaf clusters up close and gives a lacy backlit edge. (a) cards reads as blobs and makes the crown heavier. (c) rim is almost the same as today. At 30 m and at the lake, all four look alike. Every backlit crown is near-black: no light comes through the leaves.
- Recommendation (row1/note.md): (b) shells at LOD 0-1; a backlit transmission term in Flora.hlsl as the next row; clip fade by screen size; the far shadow caster takes the clip. Drop (a). Keep (c) only as a LOD 1 bridge if (b) costs too much. Leave (d) for later (about a card of work, 70-270 MB).
- BENCH ask (for the supervisor to queue): the same scene with -leaftrial off/a/b/c, at the R-22 forest vantage and the oak glade, at noon. Report GPU frame time, shadow pass time and flora triangles. The questions: is (b) affordable at High, and what does (c) cost over today?
- Editor closed (own PID). RAM was 2-25 GB free during the run.

## 11-15 (later) R-30 row 2 (#323): light through leaves - done (verified)
- render/foliage-realism@d86dc216. Flora.hlsl gets one FloraLeafTransmission (sun and view directions, a back lobe bent by the leaf normal, scaled by leaf thinness from the vertex AO). The crown's own shadow only dims it; wide and cloud shadows still block it, and caves and underground stay dark. Flora.shader and FloraImpostor.shader both call it in place of their old through term, which the crown's own shadow had zeroed. Metal leaves are left out. No new geometry and no new passes.
- Shots: oak and pine, backlit at 5 m and 30 m, before (R-30 row1/off, same commit, today's leaves) and after. Sheet: PunchyMilestones R-30/row2/sheet.png (also Assets/Temp/shots/R-30-r2-backlight.png). Oak crowns now glow warm green through their edges, and the 30 m oaks read as lit volumes. The pine lifts only a little: its needles are dark and thick, as real conifers are. A stronger pine would need a per-species thinness, which I didn't add.
- Materials: world/material-library-2 doesn't touch Flora.hlsl. It edits Flora.shader's bark and built-stone lines (Stone.hlsl include, builtStone/rubble marks), a different hunk from mine (the lighting block), so it should merge clean.
- Risks: noon is unchanged in principle (the lobe needs the sun behind the leaf), but I didn't reshoot it. The cost is one normalize and a few multiplies per leaf pixel. BENCH 102 could add an off/on pair if wanted.
- Shares: extends the one flora lighting path (Flora.hlsl, used by the mesh and the impostor; rule 33). Parallel not folded in: grass and moss have their own lighting and no light through blades yet.

## R-30 r2b #323 - leaf thinness per species - done (verified)
- render/foliage-realism@b80b9cf8. New FloraSpecies.LeafThinness (0-2, default 1 = the r2 look), sent to the mesh and impostor shaders as one small buffer per flora layer, indexed by model like the bounds buffers. Set: Pine 1.8, Spruce 1.6, EmberPine and Juniper 1.4, Birch 1.25, cacti 0.25-0.3. Dark needles need more than 1 because the light is tinted by the leaf colour.
- Backlit sheet: PunchyMilestones R-30/row2b/sheet-backlit.png (before / r2 / r2b; also Assets/Temp/shots/R-30-r2b-pine.png). Pines now warm through clearly at 5 m and 30 m; oaks unchanged from r2. At 5 m the pine reaches about oak brightness and leans amber: if it reads too hot, lower Pine/Spruce to about 1.4 in their species assets (data only, no code).
- Noon sheet: row2b/sheet-noon.png (before vs r2b). Leaves look the same; only the sky and clouds differ (weather).
- Test merge with world/material-library-2@e4d2d902: git merge-tree clean (exit 0). Merged Flora.shader keeps both the thinness term and the Stone.hlsl lines. No scratch branch pushed.
- Risks: material-library-2 adds Fir and WhitePine without LeafThinness, so they get 1 (dim like r2 pines); set about 1.6 after it lands. Flora.shader gains one varying (TEXCOORD11); the impostor's ground varying is now float3.
- Shares: one per-species value on the shared flora species data (rule 33), read by both mesh and impostor. Not folded in: grass and moss still have no light through blades.

## R-28 r2 land (#312): render/cobble-depth merged up to main, BC1091 fixed, ready for PR (render-2, render/cobble-depth ce3ff5a7)
- Merges: origin/main merged in three times (75f6dcb6, 6c63b800, ce3120e1 = main 5b953a34), all clean. Main has since moved to 27b11437, but those changes are fauna only, and merge-tree against it is clean. The branch diff against main is 6 files: DetailPolicy, BiomeData, BuildChunkMeshJob, TerrainSource, TerrainSurface.hlsl/.shader.
- Fix (job 86 blocker): the `static readonly int TerrainParallaxId = Shader.PropertyToID(...)` field gave DetailPolicy a managed static constructor. Burst jobs read DetailPolicy, so player builds failed with BC1091. The id is now looked up inline at its one SetGlobalFloat. The batch compile is clean, and a Win64 player build succeeds (0 errors).
- Shots, desert town 73 cobbles (2972,-885), main aa064602 vs branch: D:/Unity/PunchyMilestones/R-28/merge/ (low2m, m5, graze, top, rain-m5 and rain-low before/after pairs; raw in before2/ and after2/). The stones sit in the ground with sandy grout from the ground under them, bevel depth and rims. Puddle edge: partly verified. The code is the same as main (#381's fade is kept, and R-28 only adds wet joints), but desert puddles are weak, and the branch's rain view reads brighter (mean 115 vs 92).
- Sparkle probe (rule 24): 360 frames, 0 on terrain. The 6 marked frames are all in the creature shader (body), which this branch doesn't touch.
- Suite at 6c63b800: 49 passed, 4 failed, 17 skipped. Rerun at ce3120e1: LiveFolk_Answer, LiveFolk_InnkeeperAtCounter, Species_BuiltInIndicesUnchanged and Fauna_Fingerprint pass. Player_CrowFliesOutOfCaveThroughHitches (180 s timeout) and CaveWater_SurfaceMatchesAndPoolStirs fail, and they fail the same way on origin/main 5b953a34, so they're already broken on main, not caused by this branch.
- BENCH (job 86, now unblocked): the terrain vertex grows by 4 B (Soak half2 -> half4; zw carry the ground albedo under the paving). Please rerun q128 with B = render/cobble-depth ce3ff5a7, A = main: terrain GPU ms and terrain mesh memory at Medium (and High for reference) in the desert town square and town 73. Pass is <= +0.1 ms terrain GPU at Medium.
- No timing in the lane (rule 21). Pins are not touched (shader/mesh only; the fingerprint passes).
- NEXT: ready for PR.

## R-30 r2c #323 - pines toned down (Pine/Spruce LeafThinness 1.4) - done (verified), ready for PR
- render/foliage-realism@326437af. Data only: Pine 1.8 -> 1.4 and Spruce 1.6 -> 1.4, so they match EmberPine/Juniper. Birch (1.25), the cacti and all code are unchanged.
- Backlit sheet at the same r2b spots (before main / r2b / r2c): PunchyMilestones R-30/row2c/sheet-backlit.png (also Assets/Temp/shots/R-30-r2c-backlit.png). Pines still glow through the needles, but with a dimmer, less amber tone than r2b, both at 5 m and at 30 m. The oaks are the same as in r2b. Frames are in row2c/after (noon frames too).
- git merge-tree with main ec1ea218 (has #450 and #451): clean (exit 0). No main merge commit on the branch.
- Fir/WhitePine follow-up noted at 1.4 (not 1.6), for after W-166 / material-library-2 lands.
- Ambiguity: I read "leaf option b" as this tone-down (data only). If it means r1's option (b) shells as the default crown, that is a separate code row (shells at LOD 0-1, a screen-size clip fade, the far shadow caster taking the clip; cost question in BENCH 102). It is not on this branch. Tell me and I'll card it as R-30 row 3.
- Shares: the same per-species LeafThinness on the shared flora species data (rule 33).

## R-23 row 2 land (#298): render/one-surface merged up to main, ready for PR (render-2, render/one-surface 5c25c3fd)
- Merged origin/main into 1a3b0f12 with no conflicts. Flora.shader auto-merged: main's W-166 changes there are albedo-only and one-surface moves the lighting. TerrainSurface already carries R-21's PunchyFallWet. merge-tree is clean against main, cobble-depth ce3ff5a7 and audit-fixes efec19b7.
- Checks: C# compiles clean. ShaderHasError is false for Flora, FloraImpostor, TerrainSurface and Water. Play-mode sparkle probe `r23land`: 360 frames, 0 with markers, creature 0, other bad 0, hot 0.
- Suite: 52 passed, 2 failed, 19 skipped. Both failures (Player_CrowFliesOutOfCaveThroughHitches, CaveWater_SurfaceMatchesAndPoolStirs) also fail on main 5b953a34.
- BENCH: job 82 PASSED on 88719cc9 (profile builds, so the player build is covered for the lighting change). 5c25c3fd only adds the main merge, so no rerun is needed. I made no new shots: row 2 is a pure move, and the earlier pixel-diff A/B showed no change.

## 11-16 render: R-22 r1 and #452 tail, both resumable (RAM)
- Free RAM fell to 1.3 GB during the R-22 shot runs (other editors about 29 GB). My editor is closed and no runs are going; it is back at 7.2 GB, and one more editor would put it under the 6 GB floor.
- R-22 r1 (render/far-island@51e8f3e0, WIP, pushed): Medium island-on shots from the earlier session show land out to the far coast. Still to do: Medium off, High on/off, sparkleprobe NaN, seam and coast-sawtooth check, then final commit and BENCH ask. Shots need -farhaze 12000 until row 2's haze lands.
- #452 tail (render/stone-include@006e6c49, from main aeb9a5a8, pushed): TerrainSurface.shader includes Materials/Stone.hlsl, the Shaders/Stone.hlsl forwarder and its meta are deleted, and MaterialLibraryTests lists TerrainSurface as a Stone user with Pending empty. The forwarder only did that same include, so the compiled shader text is identical. Still to do: Refresh compile, noon pixel-diff vs main, NaN 0, EditMode test. Not ready for PR until those pass.
- Ask: tell me when an editor slot (6 GB or more free) opens, and I'll run both sets of checks in one editor session.

## 11-16 render: R-30 merged with main (#454), text only
- render/foliage-realism@161ecc3a, pushed, merge-tree clean against main. The Flora.shader and FloraImpostor.shader conflicts with #454 are resolved to main's PunchySurfaceLight block. The leaf transmission now gets its open-sky factor from PunchyLit as (1 - lit.wide) * (1 - lit.cloud) * ps.sunReach, the same terms it used before, and its self-shadow from lit.sun.shadowAttenuation. Neither shader does its own shadow lookups any more, so there is one lighting path. Main's old `through` term stays replaced by R-30's transmission.
- Not compiled yet (no editor, RAM). Waiting for the 6 GB ping, then one editor session runs: R-30 compile and look check, R-22 r1 checks, stone-include checks.

## 11-16 render: R-30 checked after the main merge; stopped on RAM before the other two
- render/foliage-realism@161ecc3a (no new commits). Compiles clean after Refresh (only the usual include warnings). Sparkle probe (label r30, 360 frames, oak and pine at noon): creature marked 0, other bad 0, hot 0, so NaN 0.
- Look: main moved the trees, so the old row2c framings now miss them. Re-framed on a lone Oak at (614, 746) and a lone Pine at (696, 593) (found with -place tree:), noon and backlit at 5 and 30 m: PunchyMilestones/R-30/row2c/merged2. Leaves read as in r2b (toned down, Pine LeafThinness 1.4), no black or blown canopies, shadows land as before.
- Not done: the same framings on plain main for a side-by-side. The tree was on main aeb9a5a8 and compiled when free RAM fell to 2.5 GB (other editors at 4-7 GB each), so I closed my editor (graceful exit) and put the tree back on render/foliage-realism.
- Still to do, in the next editor slot: main A/B for R-30 (list R-30/work/m2.txt), the stone-include checks (diff vs main, NaN, EditMode test) and the R-22 r1 runs. Note: -sparkleprobe needs a label after it; the bare flag at the end of PlayArgs runs nothing.
- Ask: ping me when 6 GB or more is free again.

## 11-16 render: second editor slot cut short by RAM, no results
- Opened my editor on main aeb9a5a8 (14 GB was free) for the R-30 side-by-side. Free RAM fell from about 10 GB to 0.9 GB within a few minutes of entering play mode, while the first shot (warm-up) was being taken. I stopped the run and exited the editor cleanly. The tree is back on render/foliage-realism, with nothing to commit. A scene backup from the exit is moved out of Temp so the next start doesn't get the recovery dialog.
- No change to any card: the R-30 main side-by-side (R-30/work/m2.txt), stone-include (#452) and R-22 r1 are all still resumable as on BOARD.
- Note: my play session plus the other editors needs more than the 14 GB that was free at the ping. The next slot needs about 8 GB of headroom that stays free during play, not just at start.

## 11-16 render: no-editor work while waiting for the 9 GB ping
- Stone-include (#452, render/stone-include@006e6c49): re-read the diff. TerrainSurface.shader includes Materials/Stone.hlsl, the Shaders/Stone.hlsl forwarder and its .meta are gone, nothing else still includes the bare Stone.hlsl (Cave.shader already used the Materials path), and MaterialLibraryTests now lists TerrainSurface as a Stone user with Pending empty. It merges cleanly with main 893d70a0. Editor checks (Refresh, noon pixel-diff vs main, NaN, EditMode tests) still to run.
- R-22 r1 (render/far-island@818ac88b, WIP): merged main 893d70a0. The one conflict was the camera far plane in TerrainStreamingSystem.cs, where main (R-15) now sets the plane from its own value every frame. ApplyFarIsland no longer pushes the camera itself; it returns the plane it needs (sea edge, 0 when off) and that is folded into the same max as the chunk disc and far ring, so there is still only one writer. Not compiled yet (no editor); the shots taken before the merge will be redone in the next slot.
- R-30 (render/foliage-realism@161ecc3a) also merges cleanly with 893d70a0; I left it as is so the merged2 shots still match the branch for the main A/B.
- Next, once the ping comes: R-30 main A/B (m2.txt) and handoff, then stone-include checks, then R-22 r1 (compile, list2 runs, NaN, seams/coast).

## 11-16 render: R-30 main A/B done (#323, draft PR #457)
- Ran the m2.txt framings on main aeb9a5a8 (the main commit the branch merged) into R-30/row2c/main2. All 9 shots came out. The side-by-side is R-30/row2c/ab-main-vs-branch.jpg: main on the left, render/foliage-realism@161ecc3a (merged2) on the right, six rows (oak noon and back-lit at 5 m, pine noon at 5 m and 30 m, pine back-lit at 5 m and 30 m).
- What changed: tree shapes, sizes and placement are identical. On the branch, pine and oak leaves are a little darker, with more shade between the leaf layers, so they read less flat. Back-lit trees show no extra glow or halo, just a slightly warmer edge. This is the toned-down level Zach asked for (Pine/Spruce LeafThinness 1.4, leaf option b). Fir/WhitePine at 1.4 are still the planned follow-up.
- Caveat: the skies differ (the main run is clearer, the branch run has more cloud). R-30 doesn't touch the sky, so this looks like the clouds being at a different point in each run. It accounts for most of the whole-frame pixel difference (noon mean about 22-26 against about 4-15 back-lit). The tree crops still include some sky, so I'm not giving a trees-only number.
- Earlier, on this branch: compiles, NaN 0 (sparkleprobe r30).
- The editor closed cleanly. Free RAM fell from 9 GB to 3.5 GB at the last shot and then to about 1 GB after play mode stopped. All shots had already finished. Stone-include (#452) and R-22 r1 did not start and are still resumable. Next slot: those two, in that order.

## 11-16 render: stone-include verified (W-166 r3 tail, #452)
- Branch render/stone-include@006e6c49 (already pushed, no new commit). AssetDatabase.Refresh: compile clean, 0 console errors, no shader errors in the log.
- Sparkle probe (`-sparkleprobe stone`, 360 frames): 0 frames with markers, so NaN 0.
- EditMode MaterialLibraryTests: 4/4 passed.
- Before/after on stone.txt (summit, coast, lake, town at noon): before is main aeb9a5a8 (the branch's base), in W-166/stone-include/before and after; side-by-side in W-166/stone-include/ab.jpg. The terrain and stone faces look the same. The whole-frame diff is not zero (mean 7-23), but it comes from things that vary between runs: clouds, water waves, the drifting rocks over the coast and plant sway. The removed Shaders/Stone.hlsl only did `#include "Materials/Stone.hlsl"`, so the compiled TerrainSurface shader is the same code either way.
- Ready for PR.

## 11-16 render: R-22 r1 compiles on main, slot ended on RAM
- render/far-island@98bc7ca2 (WIP, pushed). The 893d70a0 merge left one compile error, a stray `return 0f;` in UpdateFarIsland that my earlier no-editor edit put in. It is fixed and the branch now compiles clean.
- Free RAM then fell to 3.5 GB in edit mode, before any play run, so I closed the editor cleanly. The tree is clean on render/far-island and there was no scene backup.
- Still to do next slot: list2 runs (medium-off, high-on with sparkle, high-off), seams/coast check, final commit, BENCH ask; then the lava far-light re-check (#302, -891,-2021 at dusk).

## 11-16 render: R-22 r1 Medium-on after merge done, slot ended on RAM again
- render/far-island@98bc7ca2 (no code change this slot). Waited for 9 GB free (10.2 GB at 04:21), then opened one editor.
- Medium, far island on, list2 (10 shots): all done, in PunchyMilestones/R-22/row1/m-on (contact-sheet.jpg). The island draws past the streamed ground out to the coast in fly300/fly1000. No holes or seams on the summit or pan shots.
- During that run free RAM fell from 12.9 to 0.5 GB. Five other editors use 4-6 GB each and mine used about 5 GB. I stopped play mode and closed with EditorApplication.Exit, which was slow but clean (no kill).
- Still to do: Medium-off, High-on with sparkle NaN, High-off, coast sawtooth A/B, final commit, BENCH ask; then the lava far-light re-check (#302; walk-in list ready in PunchyMilestones/R-25/lava-recheck/work/lava.txt). Need about 12 GB free at start, not 9, since one run takes about 7 GB on top of edit mode.

## 11-16 render: b/fix-maxqueued-clamp merged with main (BRANCH-TRIAGE Land)
- b/fix-maxqueued-clamp@edc256b2 (pushed). Merged origin/main cleanly. The net diff against main is still the one line in Settings/FrameRateCap.cs: `-maxqueued 0` now sets 1 instead of 0, and 1-10 are unchanged. CRLF kept.
- What it changes: maxQueuedFrames 0 can stall or misbehave on some drivers, and the review of #424 asked for at least 1. It only affects runs that pass `-maxqueued`, which are bench and test start-up flags. Normal play and builds without the flag keep Unity's default (MaxQueued -1).
- Risk: very low. A bench job that asked for `-maxqueued 0` would now get 1, so the profiler should know if any BENCH-QUEUE job used 0. The range check still accepts 0, which is intended (it maps to 1).
- Editor check folded into the next slot: one play start with `-maxqueued 0`, expecting the log line "maxQueuedFrames 1". After that it is ready for PR.

## 11-16 render: R-22 r1 High-on NaN check passed, slot ended on RAM (third time)
- render/far-island@98bc7ca2, no code change. The watch saw 14.4 GB free at 04:47.
- My first launch shut down during startup with a crash-handler report. Cause: I wrapped start-quiet.ps1 in `timeout 50`, which took the editor down with the launcher. This was not a project bug. Relaunched without the wrapper; it went fine.
- High, far island on, `-sparkleprobe r22`: summary 360 frames, 0 with markers (NaN 0, rule 24). Only 2 of 10 shots (warmup, summit) were taken before free RAM fell to 3.4 GB. My editor was at 4.6 GB; the other editors use the rest. I left play mode and closed with EditorApplication.Exit, which was clean in about 30 s.
- Still to do: High-on shots 3-10, Medium-off, High-off, the coast A/B, the final commit, the BENCH ask, the lava check (#302), and the clamp check on b/fix-maxqueued-clamp (one play start with `-maxqueued 0`, expecting "maxQueuedFrames 1").
- Starting at 12-14 GB free is not enough while about five other editors are open: a High run pushes the machine below 4 GB. Doing the remaining runs needs either fewer other editors or the coordinator allowing a run to finish down to about 2 GB.

## 11-16 render: b/fix-maxqueued-clamp verified, ready for PR
- b/fix-maxqueued-clamp@edc256b2 (main merged in, pushed). One play start with `-maxqueued 0` logged "FrameRateCap: vSyncCount 0, maxQueuedFrames 1". It compiled clean with 0 console errors at editor start.
- Added a BENCH-QUEUE note that `-maxqueued 0` now means 1. Ready for PR.

## 11-16 render: R-22 row 1 done, ready for PR (#298)
- render/far-island@3d03c3cb (pushed; merge-tree with current origin/main clean). Net diff: TerrainStreamingSystem.FarIsland.cs (new) and 12 lines in TerrainStreamingSystem.cs. No placement, landmark, river, ProjectSettings or renderer changes.
- list2 (10 shots, noon, Clear), each run on and off (`-farisland 0`), in PunchyMilestones/R-22/row1/: m-on, m-off, h-on, h-off, plus ab-high-on-vs-off.jpg.
  - With it on, fly300 and fly1000 show the whole island out to the coast. With it off, the same shots show only haze.
  - Summit and pan shots show no seams or holes where the streamed ground meets the far mesh.
  - At High the coast is a smooth shoreline with a beach band. There is a faint comb pattern in the sand band from 1000 m, but no stepped sawtooth.
- Sparkle probe at High with it on: 360 frames, 0 with markers (rule 24).
- BENCH ask: job 107 (on against off, Medium and High). It overlaps world/far-lite (job 56).
- Lava far-light re-check (#302) is running now in the same editor.

## 11-16 render: lava far-light re-check (#302, R-25 row 5)
- render/far-island@3d03c3cb, which includes main's far lights (#423) and the cone (#463). High preset, time 19.5, Clear. Walked in from the north toward the cone at -891,-2021 in 18 steps from 2500 m down to 100 m, plus a warm-up shot. Output is in PunchyMilestones/R-25/lava-recheck/walk1, with a contact sheet.
- Rim glow: a red rim shows from 1300 m and grows steadily as the camera nears, with no step where it pops on. It stays visible on the slope down to 100 m. Red lava patches on the ground show from 850 m in.
- The blue-white far lights cover the plain at every distance from 2500 m. There is no visible switch-on band between steps.
- Pop found: the summit smoke plume is missing at 850 m and present at 700 m, so it pops in between those steps. That is the smoke particle's draw distance, not a far light. A later card could fade or impostor it; I logged it but did not fix it.
- The hour reads as full night with aurora rather than dusk. That is fine for a glow check, but the shot list would need an earlier hour for a dusk look.
- Row 5's "no surface lava" caveat is now covered: the lava glow is verified.

## 11-16 render: R-22 row 3 far forest, canopy-map plan (editor-free prep)
- Branch render/far-forest off render/far-island; rebase onto main after #482. Code goes in a new partial, FloraSystem.Canopy.cs, plus a small block in TerrainSurface.shader (846 lines). No big files grow much.
- **Source.** The bake calls FloraPlacement.Place on every tree-layer cell (layer 2, 4.5 m cells), with no stride, keeping only models whose DetailClass is Tree. So the map is exactly where trees stand, and it stays a pure function of placement data: a `RebakeCanopy()` hook lets seasons or fire rewrite it later. Which trees exist does not change, so flora and world fingerprints stay put.
- **Resolution.** 1024² over the same extent and corner as the world glow map (max of World.WorldSize and IslandV2.WorldSizeM, 9216 m today), giving 9 m texels = exactly 2×2 tree cells. Each texel owns its own cells, so a parallel bake has no write races.
- **Bake cost at load.** About 4.2M Place calls at the ~3.5 µs each that the glow bake measures, so ~15 s of one thread. As a Burst IJobParallelFor over texel rows that is ~1-1.5 s wall on the worker threads, off the main thread; it runs alongside the impostor bake. Fallback if it is too slow: stride 2 (the glow map's choice), a quarter of the cost.
- **Data.** Two RGBA8 maps, mips on, a 3×3 tent blur, ~10.7 MB total.
  - A: crown coverage (Σ crown area / texel area, open or leafless crowns weighted ~0.4), mean tree size, mean snow, dead/leafless share.
  - B: mean crown colour (species foliage mean, bark for leafless trees).
- **Handoff to trees and impostors, no pop.** Always-drawn tree variants end at `clamp(size × scale × ppm × Detail / FloraEdgePixels, Radius, FarRadius)` with the cull's fade `1 - smoothstep(0.88·end, end, d)`. The shader rebuilds the same end per texel from the mean size, with C# setting `_CanopyReach` = (ppm × Detail / FloraEdgePixels, Radius, FarRadius) each frame from the same numbers the cull gets. Canopy alpha is the mirror fade, `smoothstep(0.88·end, end, d)`, so the canopy comes in exactly as the trees go out. At the handoff, coverage starts at density × ThinTo (0.5, the share the cull still draws there) and ramps toward full density over ~1.5× end.
- **Screen-size LOD (rule 20).** No fixed metres anywhere. The reach comes from DetailPolicy's ppm and Detail, as above. Crown-clump noise fades out when one clump falls under a few pixels.
- **Look.** Grazing views thicken coverage (`1-(1-c)^(1/max(cosθ,0.2))`). Slope shading comes from two extra taps of the coverage × height gradient, and a soft shade edge from one tap offset toward the sun. Snow lerps toward white at about 0.6; dead forest gets bark colour and lower coverage. Lighting goes through the existing PBR, sun-dim and finish path, so the far ring, the streamed chunks and the R-22 r1 island mesh all get it from one place.
- **Risk.** Matching the canopy colour to impostor colour (the green-tint lean and leaf shading) needs tuning by shots.
- **Verify.** Row 0 shots with forest reading to the far coast, a fly-in clip from 3 km to a forest edge with no pop, unchanged flora and world fingerprints (world pin 117 689D5348, Fauna 6A9E28E5), sparkle NaN 0. BENCH job after the shots (bake time at load + far-ground cost). The editor work comes after the plume run.

## 2026-11-16 plume reach (rule 20, #302), render/plume-reach @ 170f46a3: verified, ready for PR
- **Change.** Smoke plumes fade out by their own size on screen, not at the effect range. LandmarkEffectSystem gathers a landmark out to its plume reach (DetailPolicy.ExtraReach with PlumeGonePixels 16.8) and past the old range draws only its plume; LandmarkEffects.hlsl fades alpha over the last fifth of that reach (`_PunchyPlumeReach`). A 9 m plume now reaches about 5.8 km at High.
- **Verify.** Editor run with world's editor open (RAM was fine). Compiled clean, ShaderHasError false for every shader. Same walk-in list as the lava re-check (High, dusk, clear, 7 shots, 426 s): the plume shows steadily at 1600, 1050, 850 and 700 m. Before it was absent at 850 m and popped in at 700 m. Clouds differ between runs and are unrelated.
- **Shots.** PunchyMilestones/R-25/plume-reach/after/ (contact sheet there); A/B sheet against lava-recheck/walk1 at PunchyMilestones/R-25/plume-reach/ab-plume-before-after.jpg.
- **Not done.** Sparkle probe (rule 24) not run: free RAM was 6.3 GB by the end, too low for a second play session. No NaN lines in the log. The fade is a plain alpha multiply, so a NaN is unlikely, but I asked for the probe in BENCH job 108. The UI Toolkit exception lines in the log are editor UI stack traces, not game code.
- Editor closed with EditorApplication.Exit (clean, about 30 s). No placement touched, so fingerprints are unchanged.

## 2026-11-16 R-22 r3 far forest: editor-free prep done, render/far-forest @ 5f37a001 (not compiled yet)
- **Bake.** It steps by the tree cell size from the placement data (the size Place uses), not one recomputed from settings.
- **Thinning.** The stand-in for thinned-out trees follows the cull's own ramp, from ThinFrom x radius (about 375 m at High) to the radius, through `_PunchyCanopyThinFrom`. So the canopy pass now runs on terrain pixels from about 375 m, not 1275 m.
- **Clearing shade.** Closes the 9 m gap: an empty texel takes the nearest forest's means from up to 4 texels (36 m) off, with cover still 0. The terrain shades the ground beside a forest on the side away from the sun, only past the trees' end (nearer, they cast their own shadows).
- Editor run waits for render-2's handoff. It needs about 12 GB free at the start.
- **BENCH draft** (to queue after the editor run; the supervisor sets the number):
  - Arms: (A) render/far-forest with `-farforest 0`; (B) the same build as is. Profile builds, 1920x1080, Medium and High, uncapped, ABBA, 300 frames per arm.
  - Views: (a) a ground walk through open forest with clearings (forest cover inside 375-1500 m); (b) summit view `-goto -3460,2425` at noon, clear, with the far forest in view; (c) a 1000 m flight over forest toward the coast; (d) a coast or meadow walk with no forest in view as a control.
  - Record: terrain pass GPU ms (the canopy adds 2 to 5 texture taps per pixel past about 375 m, where forest or a forest edge is near) and whole-frame GPU/CPU ms. Also the bake time at load from the `[flora] far forest map baked in N ms` line, and whether start-up waits on it (it runs on workers; it should not).
  - Pass: B within 0.3 ms GPU of A at (a) and (d), within 0.8 ms at (b) and (c); bake under 3 s on workers with no main-thread hitch over 2x median. Report the numbers otherwise.

## 2026-11-16 R-22 r3 far forest: editor run done, render/far-forest @ 864cbb46 (works, but subtle at today's forest cover)
- **Bug fixed.** The first compile baked an empty map. The blur job's output arrays hit the parallel-for write restriction, so each row write was dropped. They are now `[WriteOnly, NativeDisableParallelForRestriction]`; each job writes only its own row, so there are no races. Commit 864cbb46, plus a missing `using Unity.Entities`.
- **Bake.** 1.7-2.1 s on worker threads at load, with no main-thread hitch seen. 5.9% of texels have tree cover.
- **Verify.** Compiles clean. Sparkle probe NaN 0 (360 frames, 0 marked frames, other bad 0), in the on, off and A/B runs alike. The 0-3000 m fly-in (fly2on) shows no visible pop. Fingerprints are unchanged against the branch pins: landmarks 117 547D4E79, Fauna 4BEFEA91. Free RAM stayed 10-28 GB. Editor closed with EditorApplication.Exit.
- **Method.** A/B in the same session: flipping FloraSystem.CanopyReady per shot keeps weather and clouds identical, so the diffs show only the canopy. Proof that the path draws: with cover forced to full and the colour set to magenta, magenta covers the far ground as expected (PunchyMilestones/R-22/row3/hold/full.jpg).
- **Finding: the effect is real but small.** Pixels that differ by more than 12/255 between on and off: far views 0.1-3%, mostly cloud drift, sway and water; dense-forest views east3k 2.0%, fly700 0.2%, summit-south 0.5%, summit-east 0.2%. Sheets are in PunchyMilestones/R-22/row3/ab/ and ab2/ (ab2-sheet.jpg shows on | off | diff x6).
  - Why: forests are sparse (most covered texels are under 12% crown cover; by crown area the densest 288 m blocks average only 0.36-0.43). Many far stands are dead or bare (alpha = leafless). Haze hides most ground past about 2 km. So past where the trees end, the ground already looks about the way the map says it should.
  - Impostors look denser than their crown area, so a canopy that matches crown area reads thinner than the trees just before the handoff.
- **Proposal (needs a tuning slot, about one editor hour):** a visual cover gain (about 2-3x, capped at 1) to match how dense impostors read, plus a darker crown tone, tuned by A/B at the handoff ring. Or, since the far-coast forest read is mostly hidden by haze anyway, merge as is (it is cheap, correct and pop-free) and pair the gain with the row 2 haze work once Zach OKs that. My lean: merge now, tune with row 2.
- **Flora.hlsl.** Not touched by this branch, so there is no clash with player-2's P-37 r3 edit.
- **BENCH.** The draft above (arms `-farforest 0` vs as is) is ready to queue. Rebase onto main once #482 merges.

## R-31
### 2026-11-16 R-31 grass fields (#325): editor-free prep pushed, render/grass-fields @ be206e54 (uncompiled)
- **Row 1 probe (code ready).** `GrassCoverageProbe.Measure()`, called by script eval: renders the main camera once with post off and blades flat magenta (`_PunchyGrassProbe`, Forward pass only). It counts the magenta share of the screen rows that look at 3, 8, 15 and 25 m on flat ground (middle 60% of each row). `GroundDetailSystem.BladeStats(xz)` reads one tile back: instances and blades per m², stored height, width and bend. Plan: flat temperate meadow, eye height, Detail 1, noon. Run today's grass plus `-grassclumps 0`, then each prototype.
- **Analytic numbers from the code (before measuring).**
  - About 70 blades/m² (14 tuft roots × 5 blades). Mean height about 0.25 m, full width 0.022-0.044 m.
  - A rough random-blade estimate gives about 45% cover at 3 m and 79% at 8 m. Tufts lower it, since blades bunch round roots 0.27 m apart.
  - Blade spacing is about 0.12 m against 0.24 m height, a ratio near 0.5. Dense-field references sit under 0.2.
- **Likely causes (to confirm by measurement).**
  - Roots are drawn at 0.45× albedo against lighter ground, so each tuft reads as a dark spot.
  - Too few, too thin blades near the camera.
  - Uniform tufts.
  - Thinning starts at about 20 m (FullDensityRange).
- **Row 2 sketch.** `-grassproto a|b|c|d` is read once; without it the grass is unchanged.
  - (a): 3× the count, 9-vertex blades, width ×0.8.
  - (b): height ×1.35, width ×2.2, tufts 1.6× wider, roots in the ground's tone and lighter tips.
  - (c): today's blades over 4 alpha-cut shell layers on the tile's ground lattice (0.12 m tall, 3 cm strands). Shells sink into the ground by the full-density range.
  - (d): Bezier blades in 0.35 m Voronoi clumps, which share lean, height band and colour. No tufts, 3× the count. Rounded normal and backlit tips. 15 vertices near, 9 past a screen-size reach (rule 20).
  - The (d) shape code is adapted from altunenes' MIT shader. The copyright line and full MIT notice are in the GrassBezier.hlsl header.
  - Wind and foot push now come from one shared GrassBladeDisplace, with the same maths as before. All three passes use the same builders, so depth priming keeps the shells.
- **Files.**
  - New: GroundDetailSystem.Prototypes.cs (partial), GrassBezier.hlsl, GrassShell.hlsl, Debugging/GrassCoverageProbe.cs.
  - Edited: GroundDetailSystem.cs (17 lines, CRLF kept), GroundDetail.hlsl, Grass.shader.
  - No Biome or Surfaces asset and no placement touched. The .meta files come with the first import.
- **Next.** I need an editor slot (port 7986, at least 6 GB free) to compile, run the row 1 numbers and shoot the prototypes. render/far-forest gets rebased once #482 lands.

### 2026-11-16 R-31 row 2 shot list for Zach's sheet (ready, waiting for the editor slot)
- **Setup, the same for every shot.**
  - One flat temperate meadow with no trees in frame, picked during row 1 and pinned by position and seed.
  - Detail 1 and 1920×1080. Post on as in play, haze as on main.
  - Time pinned with the sun fixed: noon (12:00), then evening (18:30).
- **Variants, one column each.** Today's grass, then (a), (b), (c) and (d), all from the same camera pins.
- **Camera pins, one row each.**
  - 1 Eye height (1.7 m) looking 10° down, so the frame shows ground from about 3 m out to past 25 m.
  - 2-4 Eye height, centred on ground at 3, 8 and 25 m.
  - 5 From 30 m up, looking 35° down across the meadow.
  - 6 Reach edge: eye height looking along the ground to where the blades stop (screen-size reach). This checks the far fill tone against the blades.
- **Noon:** all 6 pins × 5 variants, 30 shots. **Evening:** pins 1 and 5 × 5 variants, 10 shots.
- **Walk:** a 6-frame walk across the meadow at eye height for the leading prototype and today's grass, frames 0.5 s apart. This checks for flicker, pop and how tufts read in motion.
- **Numbers per variant:** the probe's coverage at 3, 8, 15 and 25 m (target at least 90% at 3-20 m) and the blades-per-m² line. For (d), also the spacing-to-height ratio. NaN count 0.
- **Output.** Shots go to PunchyMilestones/R-31/ as `<variant>-<time>-<pin>.png`. The sheet is one grid per time of day, with coverage under each column. It goes to you for Zach. The cost check goes to BENCH-QUEUE as a separate request (rule 21).

### R-31 row 1 handoff (11-16), render/grass-fields@8253b5a9, paused (budget)
- Ground hidden at 3/8/15/25 m (meadow, eye 1.7 m, noon): today 18/40/66/59%, a (3x count) 38/69/87/71, b (wider, taller blades) 46/79/92/73, c (shell) 28/49/70/60, d (Bezier singles) 20/44/69/62. None reaches 90% across 3-20 m; b is closest, d needs a higher count.
- Causes: blades are thin and short (3.3 cm wide, 0.23 m tall at 55/m2); tufts of 5 leave gaps between clumps; thinning past FullDensityRange lowers coverage at 25 m; a dark round disc near the camera with a hard edge shows in every variant, today's too (look into it in row 2).
- Sheet for Zach: PunchyMilestones/R-31/R-31-noon-pin1.jpg (5 tiles, coverage under each). Probe: GrassCoverageProbe.Measure; prototype switch: GroundDetailSystem.SetProto(0-4). NaN 0, Grass.shader compiles clean.
- Row 2 not started. Editor closed.

### R-40 handoff (11-16), render/soft-wide-shadow@251d95b1, PR #513
- Wide shadows use bilinear 3x3-tent PCF; the hand-off weight is URP's own real-shadow fade; one shadow per point, max(real, wide) normalised, so there is no washout band and no double shadow.
- Colossus, noon and 8:00, 45/90 m, clouds pinned: 0 errors, NaN 0, low-sun frames match main outside the shadow (2-3% px); noon 90 m shadow solid (main: faint outline).
- Sheet: PunchyMilestones/R-40/R-40-sheet.png. Lesson: wait for ShaderUtil.anythingCompiling and pin clouds before A/B shots.
- Next: R-31 row 2 per the queue.

### R-31 row 2 handoff (11-16), render/grass-fields@14f86b6d
- Proto e = a's count + b's height, narrower blades (1.4x); f = a's count + d's Bezier singles (7.5x). Default look unchanged.
- Ground hidden at 3/8/15/25 m (meadow, noon, pinned clouds): today 17.5/39.9/65.5/58.5%, e 69.0/93.7/96.8/73.8%, f 45.0/74.5/89.4/72.0%; both ~168 blades/m2 vs 55; 25 m dip is still FullDensityRange thinning.
- Dark disc near camera follows the player: moving it 40 m away brightens 21% of px (+14 levels). It is the player's shadow and/or the 0.9 m grass push zone, not the grass itself.
- Sheet: PunchyMilestones/R-31/R-31-row2-sheet.png. Next: Zach picks e or f; BENCH cost check before it becomes default.

### R-31 disc + row 3 handoff (11-16), render/grass-fields@c55dbd9f
- Disc: with time frozen, the push zone off (new -grassnopush flag) and the player toggle change 0.0% px (both sit out of frame); with time running the patch grows 7% to 40% of the lower frame over 80 s with the player still, and stays with grass hidden: a sun-moving ground shadow (Zach: a tree), not grass.
- Row 3: f is the default, today's clumps behind "-grassproto today" (a-d too), e dropped. Meadow pin 1: f 44.9/74.8/89.3/71.9% hidden at 3/8/15/25 m, today 17.7/40.0/65.6/58.5%; 0 compile errors, NaN 0.
- Merge waits on BENCH 114 (pinned to 14f86b6d, still has all three arms). Shots: PunchyMilestones/R-31/row3 and R-31/disc.
- PR #487 left as a draft (I briefly marked it ready, then undid that when told to skip).

### water-film (11-19), render/water-film@e36b5875, PR #671
- Thick-water film fades out at grazing angles (smoothstep 0.1-0.4 on dot(n,v)). The marsh pose showed no rainbow before or after. The lake-gully streaks (#663, -622,634) are on plain lake water, not thick water, so this row does not cover them. They look like a separate stretched-mesh/flow issue.
- Caustic web: Voronoi points roam the whole cell (no paving grid), damped up to 60% on bright beds. Ash and temperate read softer.
- Wake cross-hatch: the cause was speed x time varying per pixel, plus flow turns at long distances from the anchor. Streaks are now read about 6 m cells with a contrast-keeping blend and a constant scroll. No hatch, but the rock wakes read as soft pale sheets; finer streaks could be a follow-up.
- Shots: PunchyMilestones/water-film/{marsh,ash,temperate,rocks}-{before,after}.png

### wake-streaks (11-19), render/wake-streaks@a15d6f9a, PR #673 (merged)
- Rock wakes are thin downstream streaks that taper and break up with distance; no cross-hatch. Same rocks pose before/after.
- Shots: PunchyMilestones/wake-streaks/rocks{down,side,up}-{before,after} at -100,1824.

### lake-gully streaks (11-19), no PR: cause is outside Water.shader
- The streaks are the river flow lines (wakeSlope flowLines term) drawn on lake water that carries river flow. Probe: n and wakeSlope show the streaks, the flow-line term alone shows them, the rest is clean.
- TerrainSource.SampleGround at -628..-622,651: river surface and lake surface are both 56.1953, so `if (level > water)` (TerrainSource.cs ~808) ties and goes either way per point. Where it fails, the lake keeps river.Flow (unit), so the field is salt and pepper 0/1 and the mesh triangles show it.
- Fix suggestion (world/banks lane): in the lake branch, take the lake when level >= water - 0.01 (or zero the flow whenever the lake is not an outlet). Re-pin water fingerprints after.
- Lesson: AssetDatabase.Refresh during play resets TerrainSource to the noise fallback (seed 1337); toggle play mode to get v3 back.

## render-2: lake-flow tie fix (PR #678, b/fix-lake-flow-tie aa17257c) - DONE
- TerrainSource ~808 `level > water` -> `level >= water - 0.01f`; gully flow now 0, streaks gone (shots PunchyMilestones/lake-flow-tie, viewed).
- WorldTests 22/22. Water truth deep 1 -> 0 re-pinned; landmarks 120 B32C5306 and fauna unchanged.
- Note: the editor dirtied Water.mat (adds _GoldLiquid/_LatticeField/_UnderRock defaults); left out of the PR.

## 11-19 render-2: W-142 (#262) row 1, lava seeps: PR #683 (render/lava-seep@5615ad01)
- New partial LandmarkEffectSystem.Seeps.cs: hashed spots (no world/bake files). Steep vent-biome rock plus lava-tube walls (main has no lava pools, so the cave seeps run along tube nodes). Beads go through the Sparks family (new seep branch in BuildPlume, age 8..9). The 6 nearest spots ask for a Fire glow (40 had taken all 16 slots). Range is by screen size, capped at the 40 nearest. SeepKind is ready for a water trickle.
- Shots PunchyMilestones/lava-seep: cave-before/after, slope-before/after (viewed). Before shots taken with the seeps switched off at runtime.
- WorldTests 21/22: Village_CreatureDensity 52 (known flaky), passed alone. No fingerprint change. BENCH line added.

## 11-19 render-2: W-142 seep streak (#262 follow-up) - PR #684
- render/seep-streak@16e34e80. Particle 0 of each seep plume is a thin tapered glowing streak from the lip down the drop, so the trickle reads from about 20 m; beads take particles 1+. Seeps.cs summaries cut to reason lines (rule 35).
- Shots PunchyMilestones/seep-streak: slope-before (the #683 shot) / slope-after at 4 m, far-before / far-after at 20 m (viewed).
- WorldTests 23/23. Cost: one more quad per seep; BENCH line folded into the #683 run.

## 11-19 render-2: W-142 seeps as mini falls (#262) - PR #690
- render/seep-falls@44d06dc5. New partial LandmarkEffectSystem.SeepFalls.cs: each seep is a 0.3-1 m lava ribbon from the lip to the foot, in FallCurtain.Vertex, drawn with the curtains' Water material. Rows by screen size, fading over the last quarter of the reach. Cave seeps end in a small glow pool. Beads 2-3 per seep; the #684 streak is removed.
- Shaders (materials lane files): Water.shader lava block +2 lines and a reason comment (a curtain's height feeds the noise; its edge fade sets the crust, molten middle). Lava river fall curtains would share it. LandmarkEffects.hlsl: streak block removed.
- Shots PunchyMilestones/seep-falls: slope-before/after, far-before/after, cave-before (#683)/cave-after (viewed slope and cave).
- WorldTests 22/23: Setup_SpawnRainAndShot timed out (the known load flake). Cost: mesh rebuilt per 10 m seep cell move (up to 40 seeps, 4-17 height samples each) plus one draw; BENCH line added.

## 11-19 render-2: W-142 needles, cap flakes, cave ceiling grit (#262 row 2) - PR #695
- render/ambient-falls@80d3be7b. Leaves plume family gets per-species fall specs: tall conifers shed thin orange needles (0.16 m, 40% of tall trees), giant caps shed pale flakes from under the cap (glowing under glow caps, spread 0.6 of the cap). New partial LandmarkEffectSystem.CeilingFalls.cs: cave ceilings trickle pale grit (10-13 per spot, 30% of nodes within 40 m), hashed per cave node, lit as under rock.
- Shader hooks (materials lane files): LandmarkEffects.hlsl quad width from Fall.x and under-rock drift damping; LandmarkPlume.shader leaf glow from Color.a.
- Shots PunchyMilestones/ambient-falls: needles_before/after (against sky), capflakes_before/after (night), cavegrit_before/after (all viewed; all subtle by design).
- WorldTests 23/23. Cost: needle and cap plumes reuse the Leaves table; ceiling grit up to ~13 particles per grit spot in caves. BENCH line added.

## 11-19 render-2: W-142 water seeps (#262 row 3) - PR #699
- render/water-seeps@1c5ffefc. Seeps in wet non-vent biomes run water: faces above rivers/lakes (water within 7 m downhill, 60%), mossy scarps in rainy biomes (up to 25%), non-lava cave walls and pools. New partial LandmarkEffectSystem.WaterSeeps.cs; the shared #690 ribbon with the local water look, slowed to a thin film. Beads/lights stay lava-only.
- Shader hooks (materials lane files): new SeepStreaks.hlsl (wet streak, foot damp patch, moss clumps faded by fwidth); TerrainSurface.shader include + seep/sheen/realWet/albedo lines; Cave.shader include + damp and moss lines.
- Shots PunchyMilestones/water-seeps: s2-before/after-3m and -20m (river bank), c1-before/after-3m and -20m (cave wall; 20 m barely shows, subtle by design). All viewed; ignore s1-*.
- WorldTests 23/23. Cost: up to 8-seep loop per terrain/cave pixel inside the seep rect; sort per 10 m cell rebuild. BENCH line added. Freeze: merge after 16:30.

## 11-19 render-2: W-142 tail - cave-foliage leaves + placed vents (#262) - PR #703
- render/262-tail@04c65476. Cave leaves (new partial LandmarkEffectSystem.CaveLeaves.cs): leaves sift down hanging roots and grassy shafts (stacked streams on deep shafts), hashed per plant/shaft, range by leaf screen size. Island caves only have 8 hanging roots, so shafts are the second source.
- Placed vents: wisps spread sunflower-style over each vent landmark (count by area, 3-20), steam/smoke from the landmark's own plume, fixed per instance. Towns and camps are no longer vent types. Biome vents unchanged, and they skip vent footprints.
- Shader hooks: none. Shots PunchyMilestones/cave-leaves: a-before/after-3m and -20m (root cave), b-before/after-3m (shaft, from below). PunchyMilestones/placed-vents: v-before/after-3m and -20m (FumaroleField -215,-2013). Viewed: a3, b3, v3, v20.
- WorldTests 23/23. Cost: Leaves plumes (max 17 particles, under the existing 18 cap) refreshed every 3 s or 4 m; placed vents roughly 17 per fumarole field instead of about 18 hashed cells. BENCH line added.

## 11-19 render-2: far-view PRs brought up to main (#298)
- #482 R-22 r1 (render/far-island@3e2a988d): main merged; fixed the merge break (island batches lacked BuildChunkMeshJob's new TerrainLayout, so the island never built). 0 errors, NaN 0 (sparkle on/off, 360 frames). Shots PunchyMilestones/R-22/row1-main on/off and haze-on/off (-farhaze 12000): "on" drops the hard far-ring band at 1000 m; the whole island to the coast shows only with haze pushed out. Job 107 ran 11-17: flights fail, so check the air cost (mesh build vs shadows) before merge.
- #494 R-23 r4-5 (render/one-surface-5@fef37e5f): main merged; Flora/TerrainSurface conflicts take main's pxm fades. Reference-distance hooks stay only on 3 fades that main still keeps in metres (terrain sheen 120-450, cobble 30-80, flora sheenFar 30-120). 0 errors, NaN 0. Shots R-23/r45-main/on match main (mean diff 3-4/255). Job 109 never ran.
- cloud-e R-35 r3 (render/cloud-e@7760775b): clean merge of the above (delta Sky.hlsl + SkyCommon.hlsl). Compiles, shaders 0 errors, not play-checked. No PR exists (#461 is a merged world PR). Job 111 passed 11-19.
- No cost change from me (one NativeArray), so no BENCH line. Editor closed.
