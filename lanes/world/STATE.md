# World lane: state (2026-11-02, after W-12)

Worktree `Punchy-world` (main PC), branch `world-features`. Backlog: `gh issue list -l lane:world`.
Released: v0.11.0 = world b39522b + animal c810406.
Pushed head: world-features 482e7c8 (W-12). Builds/Dev holds a build of it. v1 101 / E128CB1D, v2 127 / 831FF2EE hold. v1 ponds 33 blobs, 154 wet cells, none under 3 cells.
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

## W-11
- #19 v2 had 0 ponds (not the mask): its swamp patches are small, so blurred shape weights never pass 0.4. Ponds() now tests the core on the
  map's sharp weights when UsesBiomeMap. v2: 1 pond, 15 wet cells (-2282,574), min/median/p90 15/15/15, 0 single-cell. Only ~0.16 km2 of v2 swamp core; more ponds need generator changes (hash).
- #34 blue moon radius 0.13 -> 0.22 rad (Sky.hlsl), 4 crater scales + finer rock octave. Night/dusk shots w11.
- #15 hall timber shot reads (dark posts/beams). river-fog shot missed: camera at 259,55,880 framed forest and a dirt track, no river or fog visible.

## W-12
- #44 SandPuffs: DensityScale 0.06, GroundTint 1, Volume 1, 5-10 m. 126-132 -> ~30 in range. Wide view: patches faint on sand.
- #34 blue moon: 5th crater scale + crisp rims; halo still softens the edge.
- #15 NOT DONE: forced weather is masked by the biome's weather list (WeatherField BilinearAllowed). Grassland has no MarshFog;
  Temperate (MarshFog 0.2) stayed clear. River spots: Grassland 305,-581 bank 197,-663 H 19.1; Temperate -1875,-1562 bank -1885,-1562 H 30.5.
- Probe: WorldProbe.RiverSpots(ext, step, k, biome).

## Open
- #23 follow-ups: volcano markers, v2 default decision. Rivers: springs on high ground; shot of a small fall. #15 fog-over-river: make -weather bypass the biome mask for tests, retake at the W-12 spots. #13 Z-35 fall speed. #14 cave-mouth shards.
- Design-note P3: #17, #18-#22. Roadmap #24.
