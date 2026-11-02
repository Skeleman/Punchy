# World lane: state (2026-11-02, after W-05)

Worktree `Punchy-world` (main PC), branch `world-features`. Backlog: `gh issue list -l lane:world`.
Released: v0.11.0 = world b39522b + animal c810406.
Pushed head: world-features b5c7f88 (generator version 2 behind `-island v2`; wip/island-v2 merged). Builds/Dev holds a build of it.
No Punchy.exe of mine runs.

## W-05
- #23 done: `Terrain/Island/IslandV2.cs`. v1 stays default (101 landmarks, E128CB1D, checked in play on SampleScene). `-island v2` (or env
  PUNCHY_ISLAND=v2 in the editor) rebuilds the TerrainSource at launch: 134 landmarks, hash 71F2AF0C, bake 3.2 s, log line `[island]`.
- #18 partly: v2 rivers wider (9-60 m), but still angular at bends (grid path). Needs smoothing of the river path. #42 waterfalls not started:
  v2 rivers sit near sea level, so no slope drops yet. Map: PunchyMilestones/world/w05.

## W-04b (verification, mostly inconclusive)
- world-features f4c7239 (W-04) builds clean to Builds/Dev. Unverified by shots: #32 roofs, #30 flowers, #14 cave shards,
  #19 ponds. #19 landmark hash (E128CB1D) NOT printed: editor has no TerrainSource in play. Don't merge #19 yet.
- #45 stars now 0.06/night minute (gate 0.006). Shots: PunchyMilestones/world/w04b. Shot lists must end with a quit.

## Done in W-03
- #45 shooting stars: gate 0.25 -> 0.06 (about 2.5 -> 0.6 per night minute), width halved, fade-out 1.0-1.5 of life.
- #44 Dunes GPU 4.1 ms with puffs, 3.1 ms without (one run each). Bench: Tools/bench (bench.sh, BenchmarkRoute).
  Bench build copies BenchmarkRoute + BProfileTools into Assets, builds, removes them; flags
  `-benchroute -benchstops here -place X -benchgpupasses -fpscap 0`.
- #23 island v2 spike: maps in PunchyMilestones/world/island-v2. Realms (warped Voronoi), typed border ridges with
  passes, central lake bowl, rivers by priority-flood + steepest descent. Run:
  `Unity -batchmode -projectPath Punchy-world -executeMethod Punchy.EditorTools.IslandV2Preview.Render -out <dir>`.

## Gotchas
- Play checks: open `SampleScene.unity` first (World.unity subscene holds the TerrainSource); a bare play has none.
- WorldProbe (untracked, Assets/Temp/Editor): eval `WorldProbe.LmList("")` gives count+hash; `RiverMap(path)` top-down; `Shot`, `GoTo`.
- Camera height = H(x,z) + offset; take H from the probe, never guess.
- Shot lists: `-camlook` is per line; repeat it on every line. `-meteors` forces a shower; `-weather Clear` for sky.
- `-place prop:Daisy` finds nothing: ground-scatter flowers are not props.
- Launch-wide flags (-nosandpuffs, -menu, -bluemoondays) mean one player per variant.
- Assets/Resources, Assets/Temp (+ .meta) stay untracked on purpose.
- Hashes without avalanche (math.hash of small ints) need a Scramble before comparing draws.

## Open
- #23 follow-ups: volcano markers, v2 default decision. #18: smooth river curves. #42: waterfalls. #15 look checks (Z-30 posts, Z-10 fog). #13 Z-35 fall speed. #14 cave-mouth shards.
- Design-note P3: #17, #18-#22. Roadmap #24.
