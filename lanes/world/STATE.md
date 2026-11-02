# World lane: state (2026-11-02, after W-18)

Worktree `Punchy-world` (main PC), branch `world-features`. Backlog: `gh issue list -l lane:world`.
Released: v0.11.0 = world b39522b + animal c810406.
Pushed head: world-features 73709c8 (W-18). Builds/Dev holds the W-18 build (= 73709c8). Player logs: v1 101 / E128CB1D, v2 127 / 831FF2EE. v1 ponds 33 blobs, 154 wet cells, none under 3 cells.
No Punchy.exe of mine runs.

## W-05 / W-05b
- #23 v2 island: `Terrain/Island/IslandV2.cs`, `-island v2` / env PUNCHY_ISLAND=v2 (the editor runs v2). #18/#42 smooth rivers + falls for v2 only. Shots PunchyMilestones/world/w05b.

## Gotchas
- Play checks: open `SampleScene.unity` first (World.unity subscene holds the TerrainSource); a bare play has none.
- WorldProbe (untracked, Assets/Temp/Editor): eval `WorldProbe.LmList("")` gives count+hash; `RiverMap(path)` top-down; `Shot`, `GoTo`.
- Camera height = H(x,z) + offset; take H from the probe, never guess.
- Shot lists: `-camlook` is per line; repeat it on every line. `-meteors` forces a shower; `-weather Clear` for sky.
- Shot lists: create the `-shotdir` folder first, or every shot fails ("Failed to store screen shot") and the run is wasted.
- `-place prop:Daisy` finds nothing: ground-scatter flowers are not props.
- Launch-wide flags (-nosandpuffs, -menu, -bluemoondays, -whitepools) mean one player per variant.
- Assets/Resources, Assets/Temp (+ .meta) stay untracked on purpose.
- Whole-world diagnostic passes (PondCount etc.) cost seconds each; log from the real pass's data, never re-sample.
- Hashes without avalanche (math.hash of small ints) need a Scramble before comparing draws.

## Caves and pools (W-13..W-17)
- #15 `-weather` (ForceAt) forces a cell, skipping the biome weather list. #34 blue moon face *0.7, corona smoothstep from 1.0 r.
- Blue moon frame: -goto -255,27 -time 23.5 -camlook -255,72,27,-341,121,12.
- Cave bake runs per cave in Parallel.For (hash 9CB26E0C6AEB39BA). Settlements ~290 ms editor (hashes B4E4858C9FE1CF46 / 14650FB0739D0383, baked 2433C3F949E9CE3E).
- v2 cave by bench stop: mouth -1665,2520, deepest -1703,41,2377. v2 white pool spot: cam 2998.6,-0.77,-2133.2 -> 3002.04,-3.69,-2135.88.
- #35 white pools (W-17): colour 0.72/0.80/0.82, murk 0.18, glow 0.12; `_SoftRim` fades the rim, darkens the middle, body alpha x0.55, glow is a band inside the rim. Build shot reads as a pale translucent pool. Murk > ~0.3 = opaque slab (snow).
- #35 bat folk: lanes/world/BATFOLK-PLAN.md. Steps 1-2 done (W-18): `Terrain/Caves/BatFolkBaking.cs`, own blob `TerrainSource.BatFolk` (sites + per-pool WhitePools), stage "batfolk" after caves. Qualify: chamber r >= 9 m, >= 3 nodes in, a water pool (own, else nearest in cave). 1 per 6, scrambled mouth cell. CaveSystem draws flagged pools with the white mesh; `-whitepools` = debug override (all pools).
- Bat folk logs: v1 `caves 0, picked 0 (of 17; biggest deep chamber 13.8 m)`; v2 `caves 3, picked 1; cave 2 chamber 3001,3,-2135 r 10.1 pool 1`. Non-picked pool for shots: v2 cave 31 pool 16 (1989.5,4.41,-362.1), cam 1986.1,7.3,-359.4 (framing poor, pool reads blue).
- Editor cave preview (W-17, tracked): play mode, `Punchy.EditorTools.CaveTools.PreviewAt(pos, target, whitePools)`, wait ~8 s, `CaveTools.HideReadout()`, `CaveTools.PreviewShot(path, 800)`. Underground and SurfaceHidden reach 1.00. Editor look is close to the build, a little brighter (lantern).
- Probe: WorldProbe.Creatures(r), CaveGo(x,z), CaveRender(), CaveHash, CaveBench(n), TownBench(n) (play mode).

## Load times (#23)
- v2 player ready ~7.2 s. `[bake] stages ms` logs each stage; `[island] generator version N ...; N landmarks, hash X` logs on v1 and v2.

## Open
- #35 bat folk step 3 huts/props, step 4 light. v1 has no qualifying cave (deep big chambers have no pool): decide whether to loosen the pool rule or MinChamber for v1.
- #23 follow-ups: volcano markers (need a spec), v2 default decision. Rivers: springs on high ground; shot of a small fall. #13 Z-35 fall speed. #14 cave-mouth shards.
- Design-note P3: #17, #18-#22. Roadmap #24.
