# Bench queue (supervisor → kind-possum)

Since J-041 kind-possum runs on a different Claude account, so cross-session messages may not reach it.
The supervisor adds requests here; kind-possum pulls before each job and reports in BENCH-REPORTS.md
(newest first, one short entry per queue: gate results and real regressions only).

## Setup (once, after the account switch)
- Pull `coordination` and run `sh hooks/install.sh <fixed folder outside your trees>` (J-041 commit telemetry).
  Make one test commit on a b/ branch and check ~/.punchy-telemetry/commits.csv gets a row.

## Standing rule: uncapped (Zach, 2026-11-03)
- Profile and bench uncapped (no frame cap, vsync off). Capped runs hide CPU/GPU headroom. Use a cap only if a card
  asks for one, and label it.

## Standing rule: cost log (from 2026-11-02, main-account supervisor)
- After every bench job or session end, and once now for the backlog: pull `coordination`, run
  `python tools/usage_log.py --machine laptop`, and commit `COSTS/laptop.csv` (message "Costs: laptop <date>").
  It reads this laptop's local transcripts with a script (no model call, so no tokens spent) and rewrites the file
  in full, so extra runs are harmless. Don't summarise it; the supervisor reads the CSV for its milestone report.
- Put each job's cost line in its BENCH-REPORTS entry: `Cost: <calls> calls, peak <k> context, $<equiv>`.

## Queue (top first). 2026-11-02: everything older was cleared (v0.9-v0.11 era, done or stale)
1. Smoke world-features 8d83102 vs v0.11.0 (townwalk, glide, cave, caveapproach; ABBA). Same island (hash E128CB1D).
   New since v0.11: Dunes sand puffs, blue moon with drifting tendrils, rarer fading shooting stars, bench-runner merge.
2. Dunes GPU on the laptop: `-place Dunes` fixed view, with and without `-nosandpuffs`, 2 runs each. Main PC saw
   about +1 ms (4.1 vs 3.1). Pass = under +1.5 ms here. Also night sky GPU at one fixed view (moon in frame) vs v0.11.0.
3. Issue #31, houses lit wrong on the laptop: one town shot at noon and one at dusk on world-features 8d83102, plus
   the quality level and light/shadow settings in use. Name the likely cause in BENCH-REPORTS (no edits); the world
   lane fixes it.
4. Smoke animal-behaviours b5e9715 vs v0.11.0 (townwalk cap 60 plus one profile; ABBA). New: dodge/roll, fish
   sprint, wall climb (A-04), fear height check while flying.
5. Host/join on animal-behaviours b5e9715: a remote player's roll (B/Circle) shows the somersault on the other
   screen; no join errors.
6. Later: when world-features gains `-island v2` (batch W-05), smoke `-island v2` vs default on the same build.

## Added 2026-11-02 (afternoon). main = 0cc3ed7 (both lanes merged, #51). Zach: v2 becomes the default (W-22, pending)
7. Bench stops on v2, done on your b/bench branch: townwalk has to reach a village on v2 (item 6 ended in desert
   scrub). Also check cave/caveapproach land in a real cave on v2. Every stop must work on both islands (rule 12c).
8. Smoke main 0cc3ed7, `-island v2` vs v1, same build, ABBA, with your fixed stops. Compare against item 6: A-22 moved
   the cave cull to Burst (main PC: 7.1 → 0.33 ms) and W-15/W-16 cut bake time (v2 ready ~7.2 s on the main PC).
   Pass = v2 cave CPU within 1.3x of v1, and v2 load under 10 s on the laptop.
9. Review only, no edits: `BatFolkBaking`/`BatFolkMeshes` (world) and FolkChat in CreatureState/Idle (animal A-26):
   per-frame allocations, main-thread work, anything that grows with creature count. Findings go in BENCH-REPORTS.
10. W-22 has landed (world-features 5016957, "v2 default"): host + client with no `-island` flag. Both must log
   126 / 053C63D5 on world-features after W-27 (main 0bd91cb: 125 / D709D95C); then `-island v1` on both gives 102 / 51EA6463 (main: 101 / E128CB1D). No join errors.

## Added 2026-11-03. main = 762c16a (world lane merged, PR #66; animal lane not merged yet)
11. Slow caves on the laptop (Zach: "worth looking into the slow caves on the laptop"). Build main 762c16a to
    Builds/Bench. Bench cave and caveapproach, `-island v2` and v1, cap 60, ABBA vs v0.11.0. One profile per
    island at the cave stop. Name the top 5 main-thread and GPU costs inside a cave with ms each. Earlier leads:
    camera LateUpdate +1.8 ms on v2 (main now uses the sphere-trace CaveArm; `-perflog` prints probe calls/ms),
    CreatureRenderSystem 10.5 ms main thread at the v2 cave stop (item 6), and per-chunk cave timing (item 15).
    Say which lane owns each cost (world: caves/camera/cave cull; animal: creatures). No edits. Pass target
    (Zach 2026-11-03: "I would expect that caves run faster than open ground. They have much more limited scope."):
    cave fps and 1% low BETTER than open ground on the same build. So also list what still runs or draws inside a
    cave that the player can't see or reach: surface terrain chunks, grass/flora, sky/weather, water, far creatures,
    shadow cascades, settlements. Give the ms each would save if culled underground.

## Added 2026-11-03 (evening). main = 27bf8c1 (PR #77)
12. Box collision cost (#79, W-67). world-features 52b4af5 vs main 27bf8c1, `-island v2`, standard route plus the
    cave stop (-goto 530,958) and a wharf town if the route passes one. Per-frame ms and the top main-thread costs,
    with PlayerMovement, ReachJob and ThirdPersonCamera.LateUpdate shown separately. Interim number: W-67b is still
    changing this code, so expect a re-run. If 52b4af5 won't build, report that and stop.

## Added 2026-11-03 (night). main = 913d1b2 (PR #89 box collision, #90 P-1, #91 W-68 v3 flag)
13. Merged-main check after job 12. main 913d1b2 vs 27bf8c1, `-island v2`, uncapped, same stops as q12/q12b (route, hut cave floor `-goto 530,45,958`, FishWharf steps). Expect the hut-cave gap (ThirdPersonCamera, TraceSystem) closed now that A-45 and the collision are both on main. Numbers only unless something regressed.
