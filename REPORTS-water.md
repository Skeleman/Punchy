# Water lane reports

## W-125 water round 3 (branch water/round3, from water/overhaul bc49b74d)

### Row 1, item 37: lava orange and black; gold kept (2026-11-08) DONE, verified
- Commit 8bf99541. Water.shader only. The 11-08 look is kept, unused, behind a material toggle `_GoldLiquid`
  ("Gold Liquid", ToggleUI, default 0): any copy of the water material with it on draws its lava as the old molten gold.
  Checked: Water.mat toggled in memory in the editor, the gold draws and compiles (shot), then put back (asset unchanged).
- Lava: clock 0.03 (was 0.1, so 30% speed; flow drift slowed alike), scale 0.3 (was 0.45), octaves 4/3/3 by tier
  (was 6/5/4). Crust thinner than 0.34 glows deep red to orange (yellow only at the hottest), the rest is
  near-black rock lit faintly by the scene. Cheaper than before (fewer octaves).
- Shots: this island has no surface lava (a scan of every wet point found none), so the one cave lava pool
  (`-place cavelava`, pool at -183,6,-2760), camera above the rim. Check: gold before = swirled brown and yellow-orange
  over the whole pool; after = black crust over about 80-85% of it, thin orange veins. Sheet
  PunchyMilestones\water\W-125\r1-lava-before-after.png; gallery in PunchyMilestones\water\W-125\gallery.
- Gotcha for editor shots: async shader compile draws the water cyan for 10+ s after a shader edit; run the list twice.
- Next: row 2 (under-fluid look table, weather off under water).

### Row 2, items 33 and 36 (+ row 5's depth darkening): under-fluid look, weather off under water (2026-11-08) DONE, verified
- Commit 2960a93d. Underwater.cs: one table `Fluids` (fog colour, screen tint, Unity and shader fog per metre, low-pass
  cutoff, depth darkening), blended by the fluid at the lens: swamp from the water's murk (0.55-0.9, the shader's oily
  threshold), lava from the lava amount, cave lava pools as lava. Water keeps its old values; swamp green, fog x2.7;
  lava orange, fog x15 (a few metres of view). Fog colour darkens with depth (water 0.035/m, swamp 0.08/m, floor 15%).
- Under water: ambient mist/dust puffs (non-glow ambient particles) don't draw, Fog/Dust-conditioned particles go to
  0, heat haze off (AmbientParticleSystem, AmbienceSystem; weather precipitation and the dust emitter were already off).
- Shots (camera posed): sea `-goto 400,-160` camera 360,-2.5,-160 (teal, as before); swamp camera 420,-3,-72 (the sea
  inside the PoisonSwamp, murk 0.89: green, wall 5 m off barely visible); lava `-place cavelava` camera -183.1,5.9,-2760.1
  (orange, near-blind); Sandstorm above water at 360,1.5,-160 (sand haze) then at -2.5 m (none). Sheet
  PunchyMilestones\water\W-125\r2-under-fluids.png. Caveat: no before shot of the dust under water (the old build).
- Next: row 3 (swell from the weather's wind).
