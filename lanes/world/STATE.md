# World lane: state (2026-11-02, after W-14)

Worktree `Punchy-world` (main PC), branch `world-features`. Backlog: `gh issue list -l lane:world`.
Released: v0.11.0 = world b39522b + animal c810406.
Pushed head: world-features d473bf1 (W-14). Builds/Dev holds a build of it. v1 101 / E128CB1D, v2 127 / 831FF2EE hold. v1 ponds 33 blobs, 154 wet cells, none under 3 cells.
No Punchy.exe of mine runs.

## W-05 / W-05b
- #23 v2 island: `Terrain/Island/IslandV2.cs`, `-island v2` / env PUNCHY_ISLAND=v2. #18/#42 smooth rivers + falls for v2 only. Shots PunchyMilestones/world/w05b.

## Gotchas
- Play checks: open `SampleScene.unity` first (World.unity subscene holds the TerrainSource); a bare play has none.
- WorldProbe (untracked, Assets/Temp/Editor): eval `WorldProbe.LmList("")` gives count+hash; `RiverMap(path)` top-down; `Shot`, `GoTo`.
- Camera height = H(x,z) + offset; take H from the probe, never guess.
- Shot lists: `-camlook` is per line; repeat it on every line. `-meteors` forces a shower; `-weather Clear` for sky.
- `-place prop:Daisy` finds nothing: ground-scatter flowers are not props.
- Launch-wide flags (-nosandpuffs, -menu, -bluemoondays) mean one player per variant.
- Assets/Resources, Assets/Temp (+ .meta) stay untracked on purpose.
- Whole-world diagnostic passes (PondCount etc.) cost seconds each; log from the real pass's data, never re-sample.
- Hashes without avalanche (math.hash of small ints) need a Scramble before comparing draws.

## W-07
- #19: ponds are back (Ponds() in TerrainSource) behind TerrainSource.PondsOn, set only at the end of BuildPlaces. The first try moved roadside landmarks because settlements/caves/anchors sampled the dug ground. v2: 243 ponds, 359 wet cells.
- #18: RiverSettings.SourceHeight (v2: 45 m): a river cell under it counts only if a river cell drains into it. v2 springs: bed min 18/median 64 -> 44/90 m, 31 -> 22 sources, 67 -> 60 falls, v2 130 landmarks DA354F63. `PUNCHY_SPRINGS=old` env restores old springs.
- #30: storm flag is `-weather Storm` (per shot-list line). Flowers lean with the wind in the shot; no swivel judged from one still.

## W-08
- #19: ponds dug only in a biome core (weight > 0.4), noise scale 0.2. Blobs 2614 -> 1287; cells per pond min 1, median 1, p90 7, max 39. Single-cell still ~51%: NOT fully dropped. Inland swamp pond at -4488,1438 (7x7 wet).
- #44: SandPuffs already existed (W-02); alpha 0.3 -> 0.4. 1 quad (6 verts) per puff, ~1.5 per 100 m2, est ~380 in range (not measured). Dunes shot shows haze, no distinct puffs.
- #20 v2: ridged chains with passes + height variation. Ridge cells 1408 -> 9760; border-line ridge height min 13, p10 20, median 81, max 170 m. v2 127 landmarks, hash 831FF2EE. 4600 m top-down shot renders blank sky: needs the IslandV2Preview map instead.
- #30: flowers calm/storm pair taken at the same spot (not viewed).

## W-10
- #19: TerrainSource.PondMask (baked HeightmapBlob, 16 m grid) zeroes ponds under PondMinCells=3 after BuildPlaces. 219 -> 33 blobs, min 1/median 1/p90 3 -> 3/4/7, max 19. Log line `[ponds]`.
- #44: SandPuffs colour (1.3, 0.55, 0.2, a 2.5), size 1.2-2.4, height 0.6-1.4. 126-132 puffs in range at the Dunes spot (SandPuffs only, WorldProbe.PuffType). Earlier tints matched the sand: that was why no puff showed.
- #39: cobbles 0.5 x 0.6 m, grout 0.04-0.12, stone greyed and brightened, bump 0.8. Stones read clearly on the village plaza at 2-15 m.
- Probe: WorldProbe.PuffType, Paving(x,z), BigPonds(k). Spots: Dunes 1800,-1800 (H 10.7); plaza 115,-405 (H 25.7); swamp ponds round -2700,1450.
- Editor: cam.Render misses ambient particles (Graphics.RenderPrimitives); use ScreenCapture in play to judge them.

## W-13
- #15 DONE: `-weather` (ForceAt) forces a cell with Forced=true, which skips the biome weather list (CPU Sample/Describe and MapJob). ] key unchanged. Shots w13 fog-grassland / fog-temperate.
- #34 DONE: blue moon face *1.6 -> *0.7 (was clipping + blooming over itself), corona smoothstep from 1.0 r. Edge reads crisp.
- Blue moon on day 0 (dir): 20:36 near zenith (0.95 up); 23:30 (-0.86,0.49,-0.15). Frame: -goto -255,27 -time 23.5 -camlook -255,72,27,-341,121,12 (y 37 is behind a tree).

## W-14
- #23 load: v2 player ready at 7.9 s (was 15.8). Pond grid ran 4x per bake (3 diagnostic counts); now 1 parallel pass. `[bake] stages ms` log line per stage. Biggest left: caves ~1.4 s, settlements ~1.0 s.
- #23 cave render: v1 106 / v2 127 creatures at the chamber, both SurfaceHidden 1.00, 1 roost (cap 21 / 28), 0 pools. Not world-owned; handed to animal lane.
- Probe: WorldProbe.Creatures(r), CaveGo(x,z) (deepest node of nearest cave), CaveRender(). v2 cave by bench stop: mouth -1665,2520, deepest -1703,41,2377.

## Open
- #23 follow-ups: volcano markers (need a spec), v2 default decision. Rivers: springs on high ground; shot of a small fall. #13 Z-35 fall speed. #14 cave-mouth shards.
- Design-note P3: #17, #18-#22. Roadmap #24.
