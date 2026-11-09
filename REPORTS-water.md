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
