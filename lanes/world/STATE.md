# World lane: state (2026-11-02, after W-05b)

Worktree `Punchy-world` (main PC), branch `world-features`. Backlog: `gh issue list -l lane:world`.
Released: v0.11.0 = world b39522b + animal c810406.
Pushed head: world-features 380d59a (W-07: swamp ponds on, dug only after all places bake; v2 springs need 45 m height). Builds/Dev holds a build of it. v1 101 / E128CB1D holds with ponds on (2614 pond blobs on a 16 m grid, 4329 wet cells). Island.meta now tracked; empty Assets/Resources removed.
No Punchy.exe of mine runs.

## W-05 / W-05b
- #23 done: `Terrain/Island/IslandV2.cs`. v1 stays default (101 landmarks, E128CB1D, rechecked in W-05b). `-island v2` (or env
  PUNCHY_ISLAND=v2 in the editor) rebuilds the TerrainSource at launch; log lines `[island]` (now 129 landmarks, hash 62B8C643, bake 3.5 s) and rivers.
- #18 done for v2: RiverSettings.Smooth (Hermite curves), width 9-45 m. #42 done for v2: RiverSettings.FallDrop = 4 m, bed step, 98 falls, drawn
  by LandmarkEffectSystem. Both off for v1. Not done: springs on ridge crests; a close look at a 4-8 m fall (the one shot is the 32 m cliff).
  Maps/shots: PunchyMilestones/world/w05b.

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

## W-07
- #19: ponds are back (Ponds() in TerrainSource) behind TerrainSource.PondsOn, set only at the end of BuildPlaces. The first try moved roadside landmarks because settlements/caves/anchors sampled the dug ground. v2: 243 ponds, 359 wet cells.
- #18: RiverSettings.SourceHeight (v2: 45 m): a river cell under it counts only if a river cell drains into it. v2 springs: bed min 18/median 64 -> 44/90 m, 31 -> 22 sources, 67 -> 60 falls, v2 130 landmarks DA354F63. `PUNCHY_SPRINGS=old` env restores old springs.
- #30: storm flag is `-weather Storm` (per shot-list line). Flowers lean with the wind in the shot; no swivel judged from one still.

## Open
- #23 follow-ups: volcano markers, v2 default decision. Rivers: springs on high ground; shot of a small fall. #15 look checks (Z-30 posts, Z-10 fog). #13 Z-35 fall speed. #14 cave-mouth shards.
- Design-note P3: #17, #18-#22. Roadmap #24.
