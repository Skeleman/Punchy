# World lane: state (2026-11-02, after W-16)

Worktree `Punchy-world` (main PC), branch `world-features`. Backlog: `gh issue list -l lane:world`.
Released: v0.11.0 = world b39522b + animal c810406.
Pushed head: world-features d69a710 (W-16). Builds/Dev holds the W-16 build minus the v1 hash log line (added after the build). v1 101 / E128CB1D, v2 127 / 831FF2EE hold. v1 ponds 33 blobs, 154 wet cells, none under 3 cells.
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

## W-13
- #15 DONE: `-weather` (ForceAt) forces a cell with Forced=true, which skips the biome weather list (CPU Sample/Describe and MapJob). ] key unchanged. Shots w13 fog-grassland / fog-temperate.
- #34 DONE: blue moon face *1.6 -> *0.7 (was clipping + blooming over itself), corona smoothstep from 1.0 r. Edge reads crisp.
- Blue moon on day 0 (dir): 20:36 near zenith (0.95 up); 23:30 (-0.86,0.49,-0.15). Frame: -goto -255,27 -time 23.5 -camlook -255,72,27,-341,121,12 (y 37 is behind a tree).

## W-14
- #23 load: v2 player ready at 7.9 s (was 15.8). Pond grid ran 4x per bake (3 diagnostic counts); now 1 parallel pass. `[bake] stages ms` log line per stage. Biggest left: caves ~1.4 s, settlements ~1.0 s.
- #23 cave render: v1 106 / v2 127 creatures at the chamber, both SurfaceHidden 1.00, 1 roost (cap 21 / 28), 0 pools. Not world-owned; handed to animal lane.
- Probe: WorldProbe.Creatures(r), CaveGo(x,z) (deepest node of nearest cave), CaveRender(). v2 cave by bench stop: mouth -1665,2520, deepest -1703,41,2377.

## W-15
- #23 cave bake: rubble + plants per cave in Parallel.For, joined in cave order. Editor 1350 -> 555 ms; player caves stage 1380 -> 591, v2 ready 7.2 s. Cave hash 9CB26E0C6AEB39BA unchanged. Biggest left: settlements ~1.0 s.
- #35 white pool: `-whitepools` (draw-only, launch-wide). Look (0.92,0.95,0.97, murk 0.24), glow 0.28. Murk > ~0.3 = hard opaque slab (snow). Build shot still reads as a glowing white disc: needs softer rim alpha / ripples (NOT judged done).
- #35 bat folk: lanes/world/BATFOLK-PLAN.md (after-caves stage, own RNG, v1/v2 hashes untouched).
- Probe: WorldProbe.CaveHash, CaveBench(n). v2 pool spot: cam 2998.6,-0.77,-2133.2 -> 3002.04,-3.69,-2135.88.

## W-16
- #23 settlements ~970 -> ~290 ms (editor bench, roads 833 -> 150): routes run in parallel (managed grid copy, wander array, flat A*); Emit and bridge sampling run in parallel and are laid in pair order. Hashes unchanged: bench B4E4858C9FE1CF46 / 14650FB0739D0383, baked 2433C3F949E9CE3E. v2 player: settlements 344 ms.
- #35 `_SoftRim` (white pools only, via property block): alpha and glow fade with depth. The build edge is soft, but the pool still reads as a milky blob. Next: lower whiteness and opacity in the centre too.
- v1 player logs no hash before d69a710; IslandV2System now logs `[island] generator version 1 (baked); N landmarks, hash X`. Check it in the next build.
- The editor camera alone doesn't render cave interiors (Underground follows the player); judge cave looks in a build.
- Probe: WorldProbe.TownBench(n) (needs play mode).

## Open
- #35 white pool centre whiteness + bat folk step 1. Confirm v1 101 / E128CB1D in a player log. #23 follow-ups: volcano markers (need a spec), v2 default decision. Rivers: springs on high ground; shot of a small fall. #13 Z-35 fall speed. #14 cave-mouth shards.
- Design-note P3: #17, #18-#22. Roadmap #24.
