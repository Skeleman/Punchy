# World lane: state (2026-11-02, after W-03)

Worktree `Punchy-world` (main PC), branch `world-features`. Backlog: `gh issue list -l lane:world`.
Released: v0.11.0 = world b39522b + animal c810406.
Pushed head: 8d83102 (shooting stars rarer/thinner/fading, bench-runner merged a428b48). No new player build of it.
Also pushed: `wip/island-v2` (IslandV2Preview.cs, editor-only, not merged). No Punchy.exe of mine runs.

## Done in W-03
- #45 shooting stars: gate 0.25 -> 0.06 (about 2.5 -> 0.6 per night minute), width halved, fade-out 1.0-1.5 of life.
- #44 Dunes GPU 4.1 ms with puffs, 3.1 ms without (one run each). Bench: Tools/bench (bench.sh, BenchmarkRoute).
  Bench build copies BenchmarkRoute + BProfileTools into Assets, builds, removes them; flags
  `-benchroute -benchstops here -place X -benchgpupasses -fpscap 0`.
- #23 island v2 spike: maps in PunchyMilestones/world/island-v2. Realms (warped Voronoi), typed border ridges with
  passes, central lake bowl, rivers by priority-flood + steepest descent. Run:
  `Unity -batchmode -projectPath Punchy-world -executeMethod Punchy.EditorTools.IslandV2Preview.Render -out <dir>`.

## Gotchas
- Shot lists: `-camlook` is per line; repeat it on every line. `-meteors` forces a shower; `-weather Clear` for sky.
- `-place prop:Daisy` finds nothing: ground-scatter flowers are not props.
- Launch-wide flags (-nosandpuffs, -menu, -bluemoondays) mean one player per variant.
- Assets/Resources, Assets/Temp (+ .meta) stay untracked on purpose.
- Hashes without avalanche (math.hash of small ints) need a Scramble before comparing draws.

## Open
- #23: choose a layout, thicker rivers, volcano markers, then wire behind generator version (landmark hash must stay
  E128CB1D for version 1). #15 look checks (Z-30 posts, Z-10 fog). #13 Z-35 fall speed. #14 cave-mouth shards.
- Design-note P3: #17, #18-#22. Roadmap #24.
