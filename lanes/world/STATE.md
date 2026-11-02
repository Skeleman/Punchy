# World lane: state (2026-11-02, after W-09)

Worktree `Punchy-world` (main PC), branch `world-features`. Backlog: `gh issue list -l lane:world`.
Released: v0.11.0 = world b39522b + animal c810406.
Pushed head: world-features 0e19871 (W-09). Builds/Dev holds a build of it. v1 101 / E128CB1D holds. Ponds now 219 blobs, 361 wet cells, 165 single-cell.
No Punchy.exe of mine runs.

## W-05 / W-05b
- #23 done: `Terrain/Island/IslandV2.cs`. v1 stays default (101 landmarks, E128CB1D, rechecked in W-05b). `-island v2` (or env
  PUNCHY_ISLAND=v2 in the editor) rebuilds the TerrainSource at launch; log lines `[island]` (now 129 landmarks, hash 62B8C643, bake 3.5 s) and rivers.
- #18 done for v2: RiverSettings.Smooth (Hermite curves), width 9-45 m. #42 done for v2: RiverSettings.FallDrop = 4 m, bed step, 98 falls, drawn
  by LandmarkEffectSystem. Both off for v1. Not done: springs on ridge crests; a close look at a 4-8 m fall (the one shot is the 32 m cliff).
  Maps/shots: PunchyMilestones/world/w05b.

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

## W-09
- #19: ponds need the field above the edge at +-17 m on both axes (BiomeData.PondErode, PondEdgeShift 0.17). 1287 -> 219 blobs, 3850 -> 361 wet cells, min 1 / median 1 / p90 3 / max 19. Single-cell 165 of 219 (75%): worse ratio, not solved at the 16 m grid.
- #20: v2 top-down map via WorldProbe.V2Map (untracked): realms and ridge bands read clearly. PunchyMilestones/world/w09.
- #44: puffs alpha 0.6, size 4-9, height 0-0.4, drift 3.5, Dunes puff density 1.5 -> 0.5. 1229 particles in range at the Dunes spot (all types).
- #39: terrain vertex Strata.y = paving; Cobbles 1, Flagstones 0.8; shader Cobbles() fades out 30-80 m. In the town shot the effect is faint (flat brown ground, no visible stones).

## Open
- #23 follow-ups: volcano markers, v2 default decision. Rivers: springs on high ground; shot of a small fall. #15 look checks (Z-30 posts, Z-10 fog). #13 Z-35 fall speed. #14 cave-mouth shards.
- Design-note P3: #17, #18-#22. Roadmap #24.
