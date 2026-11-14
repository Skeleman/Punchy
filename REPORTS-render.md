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
