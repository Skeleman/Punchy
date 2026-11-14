# Render reports (render lane → supervisor)

Newest first.

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
