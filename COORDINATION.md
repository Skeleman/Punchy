# Coordination

Three Claude sessions work on Punchy at once: two on Zach's main PC, one on the less powerful PC. This file says
who owns what so they don't collide. It lives on the `coordination` branch; read it with
`git fetch origin coordination && git show origin/coordination:COORDINATION.md`. Zach approved this light process
on 2026-10-27 (see J-016 in `JUDGMENT_CALLS.md` on the same branch).

The supervisor is the Claude session **claude-41 [21fe18]**. Message it with SendMessage. It doesn't write
feature code, and it never edits files in another session's checkout.

## The supervisor's responsibilities

1. **Lanes and ownership:** each file has one owner. It approves edits outside a lane and every change to a
   shared file.
2. **Handing out work:** it relays Zach's direction and gives each session its next task.
3. **Judgment calls:** it checks `JUDGMENT_CALLS.md` before asking Zach, asks with the question tool, and records
   the answer.
4. **Reporting:** one-screen updates for Zach, idle tracking of the local sessions, and the milestone page for
   complete features.
5. **Resources:** it watches free memory on the main PC (two editors run at once; see J-015).

## Rules for every session

1. Read this file and `JUDGMENT_CALLS.md` before each new task.
2. Work inside your lane. Ask the supervisor before touching anything outside it.
3. Commit small and often, and push. Merging another lane's branch into yours is fine (J-016); never rewrite
   pushed history.
4. Report to the supervisor when a task is done or blocked: DONE / FAILED / BLOCKED / NEXT. Screenshots only for
   complete features (J-005).
5. Never discard, reset or overwrite work you didn't write. If you find uncommitted work outside your lane,
   stop and report it.
6. Send questions for Zach to the supervisor, not to Zach, unless Zach must confirm something to you directly.
7. Use the terms in `GLOSSARY.md`.
8. **Batch related changes (Zach, 2026-10-27):** implement related items together, evaluate them in one shot-list
   session, and send one report and one compare sheet. Keep separate commits per item. Split a batch only for risk
   or a decision Zach must make.
9. **Everyone has a destination (Zach, 2026-10-28):** NPCs, messenger birds and wandering animals always head
   for an identifiable goal (door, well, field, seat, gate, next town or landmark, water, lair, roost); no aimless
   random-point wandering over distance. Most important for folk and messenger birds.
10. Main PC memory: restart your Unity editor (after committing) when it grows past about 10 GB or after heavy
   recompile runs. Keep Play-mode tests short and exit when done. Ask the supervisor before opening a player or
   build next to the two editors.
11. **Push without asking (Zach, 2026-10-28):** push your own lane's commits as soon as they compile, your checks pass
   and your shots are taken; then report. The supervisor reviews after the fact and files follow-ups. Still ask
   first for: shared or unowned files, network tables (ghost fields, enums sent over the wire, `PlayableSpecies`),
   anything that changes landmark placement or the world fingerprint, merges with conflicts in another lane's
   files, and releases.
12. **Shot rules:** 16:9 at the true aspect (set `cam.aspect`); fixed noon and clear weather unless the feature
   needs otherwise; the player avatar out of frame and out of the camera's line (the reveal cone cuts holes);
   before and after from the same camera, seed, time and coats; light dark scenes so the feature is visible;
   frame the feature tightly, 3-8 m away, whole subject in frame. At most 3 images per report, 800 px on the long
   edge (a sheet at most 1200 px; crop a detail rather than raising resolution) (a sheet counts as one). No images for changes you can't see (culling, audio, networking, perf): give numbers.
12a. **Who looks at what (Zach, 2026-10-28):** check your own shots at about 480 px while iterating. Send the
   supervisor one 800 px final only for a finished, visible feature meant for the milestone page; for fixes,
   follow-ups and interim steps send your own written check instead (what you looked for, what you saw, caveats).
   The supervisor relays those without opening images.
12b. **Full-res gallery (Zach, 2026-10-28):** besides the reviewed shots, save a fuller set for Zach to browse in
   `D:\Unity\PunchyMilestones\<lane>\<feature>\gallery\`: full resolution, more angles, before/after sets, short
   sequences if handy. Never open or view these yourself (no token cost), and don't send them to the supervisor;
   just mention the folder in the report. Keep each gallery under about 40 images.
14. **Fixed build paths (Zach, 2026-10-29):** Windows Firewall rules are per executable path, so every new build
   folder raises a new prompt. Always build or copy the runnable build to the same path and overwrite it:
   main PC `Builds/Windows/Punchy.exe` (player) and `Builds/Dev/Punchy.exe` (development); kind-possum its own two
   fixed paths. To keep an older build, archive a copy elsewhere, but run only from the fixed path. Never click or
   change firewall prompts or rules yourself.
12c. **Bench cadence (Zach via the animal session, 2026-10-29):** kind-possum runs one short smoke bench (town walk
   plus glide: avg and 1% lows) per lane batch push, not per feature. Full profiles only when a smoke bench
   regresses, then bisect between batches. Full multiplayer and GPU passes before each release candidate. Reports
   are numbers only unless something regressed. Bench stops must use fixed, named places that exist on every
   island version, or compare same-island builds only (tonight's v0.7.0 vs RC compare was apples to oranges).
13. **Waiting on someone:** before blocking on another session, ask that session whether you actually need to
   wait. If they don't answer within the hour, ask the supervisor.
14. **Editors and RAM (Zach, 2026-11-03):** close your Unity editor at the end of a card unless your lane's next
   card is already queued. Before opening one, check free RAM is at least 6 GB; if not, wait. Never close or kill
   another lane's editor. Idle import workers are set to shut down (standby 0) as each lane merges main.
15. **Editors (Zach 2026-11-03, relaxed 2026-11-09 after the RAM upgrade to 64 GB):** no queue. Before opening an
   editor, check free RAM: at least 6 GB, open it (through tools/start-quiet.ps1); under 6 GB, tell the supervisor
   instead of waiting. Keep your editor open across rows and tests of the same card; don't restart it per test.
   Close it (by your own PID) only when the suite needs it closed, when you hand the card back, or before you stop.
   EDITOR-QUEUE.md is retired: don't write to it.
16. **Handoff after every row (Zach, 2026-11-04):** as soon as a row is finished and verified, commit and push it,
   then append it to your REPORTS-<lane>.md and push that: verified, UNVERIFIED, and what's next. Do this before
   starting the next row, so a usage limit never loses work. On the main account the `handoff` skill does all of it.
17. **Don't steal Zach's focus (Zach, 2026-11-04):** start every Unity editor and every build window through
   `tools/start-quiet.ps1` (in the coordination tree), run in the background:
   `powershell -ExecutionPolicy Bypass -File <coordination>/tools/start-quiet.ps1 -Exe "<exe>" -ArgLine "<args>"`.
   It starts the program minimized without focus, and for the life of the process it hands focus back if the program
   grabs it on its own (load finished, play mode entered). In each editor, also set the Game view's play-mode
   dropdown to Play Unfocused before the first play-mode test. Batch-mode runs (`-batchmode`) don't need this.
18. **Every process must end on its own (Zach, 2026-11-07):** on Windows, when a Monitor, background task or agent stops, only
   its top shell dies. Children such as `tail -f`, `grep --line-buffered`, `while true` loops, sleep and Punchy.exe keep running. On
   11-06, 180 orphaned watchers and their greps had piled up this way. So: wrap every Monitor/background command in `timeout <s>`
   (for example `timeout 1800 tail -f log | grep --line-buffered X`, which stops tail and lets grep exit), and give every loop an
   end time (`end=$((SECONDS+1700)); while [ $SECONDS -lt $end ]`). Run one watcher at a time, and stop the old one before
   starting a new one. Launch every Unity/Punchy run with `timeout` and kill it by its own PID, never by name: other lanes'
   games and benches share the machine. After a test, check `tasklist | grep -c bash` has not grown.
19. **Lean suite, no rework (supervisor, 2026-11-09):** every card assumes these; a report that misses one goes back.
   (a) Shot, plan-map and look tests are `[Explicit]` (the runner prints SKIP); the default suite holds only fast checks with
   numbers, and a new default test runs under about 30 s. (b) Re-run the full suite after your last code change, before the
   report, and quote its line (passed / failed / skipped, seconds). (c) Cost limits in a card are measured on the same spot
   before and after, and quoted as numbers. (d) Shots: use the shared screenshot tool (T-1, once merged; see docs/agents) and
   quote its written checks. If a shot can't be made to show the change, say UNVERIFIED with the reason and move on: don't
   loop on retakes; Zach judges the look in the playtest.
20. **LOD by screen size, never fixed metres (Zach, 2026-11-10):** every visual detail choice (fade, cull, tier, shells, seams,
   effects, impostor hand-offs, light reach) uses `DetailPolicy.ScreenSize` (object size over distance in reference pixels,
   FOV and resolution aware) with a per-kind threshold in DetailPolicy, plus hysteresis. A fixed-metre visual range in new
   code goes back. Gameplay and simulation ranges (senses, AI, collision, streaming) are not LOD and may stay in metres.
   Old code counts too: a file you touch that still has a metre visual range gets converted in that branch, or listed in your
   handoff for R-13. Mark a metre range that is truly gameplay with a `// gameplay:` (or sim/streaming/collision) comment.
   The supervisor runs `tools/lod-lint.sh <branch>` on every branch before merge; unexplained hits go back.
21. **Only the profiler profiles (Zach, 2026-11-10):** lane agents never measure frame times, ms tables, bench runs or RenderDoc
   captures, whatever an older card says. The profiler's weaker, isolated machine gives the real and steady numbers. A lane
   row that needs a cost check instead ends with a BENCH-QUEUE request in its handoff: branch, commit, spots (-goto coords),
   arms (main vs branch, any flags) and the pass line. The supervisor queues it. Cheap counts you get anyway (triangles, draw
   calls, index counts, memory from a script) are fine to report; no timing.
22. **Placement is data, ready for a world file (Zach, 2026-11-11, #260):** one day the island may be generated once at design
   time, saved to a file, hand-edited and loaded by the game. No layouts are final yet, but new and changed world code must stay
   compatible: (a) every placed thing (landmark, cave, dungeon, tunnel, road/river waypoint, settlement, building, colossus spot,
   hermitage, lone NPC, fence/sign/light) comes out of its bake step as a plain record (kind, position, rotation, size, params) with
   a stable id, held in a blob or list, not only as a mesh, entity or side effect; (b) later steps and runtime systems read those
   records, never re-run an earlier placement or its RNG to ask what is there; (c) placement RNG runs only in the bake. Runtime
   may scatter visuals (flora, small details, chunk meshes) from the records, ground and seed, but nothing gameplay places at
   runtime; (d) a bake step depends only on its inputs (seed, settings, earlier records), with no editor, scene or frame state.
   Old code that breaks this is fine until touched; list what you notice in your handoff.

## Sessions and lanes

| Session | Machine and checkout | Branch | Lane |
|---|---|---|---|
| **Animal behavior manager** | Main PC, `D:\Unity\Punchy` | `animal-behaviours` | Creatures: behaviour, Needs, Traces, Lairs, Domestic animals, flight, interactions |
| **world feature manager** | Main PC, `D:\Unity\Punchy-world` (worktree) | `world-features` | Caves, landmarks, settlements, roads, water surfaces, world events |
| **windows-7l945c9-kind-possum** | Other PC | `b/*` (e.g. `b/bench`, `b/weather-perf`) | Profiling, code quality, streaming readiness. Reviews both branches; doesn't edit their files |

## File ownership

Paths are under `Assets/Game/`.

| Owner | Paths |
|---|---|
| Animal behavior manager | `Scripts/Fauna/**`, `Fauna/**` assets, `Resources/Domestics.asset`, `Scripts/Debugging/DebugControls.cs` (shared test flags), `Scripts/Weather/WeatherSystem.cs` Sun handling, `GLOSSARY.md`, `Scripts/Audio/**` (shared sound system; others call its API), `Scripts/Ambience/` butterflies and fireflies |
| world feature manager | `Scripts/Terrain/Caves/**`, `Scripts/Terrain/Anchors/**`, `Scripts/Terrain/Landmarks/**`, `Scripts/Terrain/Settlements/**`, `Scripts/Editor/CaveTools.cs`, `Biomes/Glacier.asset`, `Biomes/Surfaces/GlacierIce.asset`, `Landmarks/*.asset`, cave style and settlement assets, `Shaders/Cave.shader`, `WORLD.md` |
| kind-possum | `Scripts/Debugging/BenchmarkRoute.cs` and its own `b/*` branches |
| kind-possum, perf fixes (Zach approved 11-04) | May fix perf, crash and stall bugs it found and measured, in any file, on a `b/fix-*` branch cut from current main, when: the file isn't touched by a running card (check the open branches' diff vs main and batches/), the fix is small (no features, no design changes), and it ships with before/after numbers. The supervisor reviews and merges like any lane. If the file is busy, or the fix is a redesign, report it and the supervisor cards it |
| player lane (2nd account, 11-03) | `Scripts/Settings/**`, `Scripts/UI/**`, `Scripts/Camera/ThirdPersonCamera.cs`, branch `player-client`; later interiors, doors, lights, pickups, inventory (#26) |

**Shared, additive only (tell the other owner):** `Flora/FloraGenerator.cs`, `Flora/FloraPlacement.cs`,
`Flora/FloraSystem.cs`, biome assets (landmark lists only), `Water.shader` (world session is doing the surface
work; animal session gets told first), `TerrainSource.cs` and `TerrainAuthoring.cs` (entry points and bake calls only).

**No owner, changes go through the supervisor:** `CLAUDE.md`, `MULTIPLAYER.md`, `.gitignore`, scenes,
`ProjectSettings/`, `Packages/`.

## Current tasks

| Session | Task | State |
|---|---|---|
| Animal behavior manager | Interactions: pet and carry done (bbb3194), hunt next (J-017); then swallows and glow moths | In progress |
| world feature manager | Phase 4 towns: SettlementLayout design first | In progress |
| kind-possum | Route determinism, WeatherField MapJob fix, round 5 | In progress |

## Long-term goals (Zach, 2026-10-28): fill gaps with intermediate steps

1. **Enterable buildings:** doors that open (world: hinged door part; animal: NPCs opening them at night and
   morning), clutter outside (rugs, pots, vases, weapon racks, tapestries, chairs), then hollow interiors.
2. **One large city per architecture:** (a) more building types per culture; (b) settlements on non-flat terrain
   (hills, terraces, canyons); (c) more NPC jobs (farming, well water, carts, groups, prisoners, carrying goods).
4. **Hermits:** lone folk at lone huts, cave camps and points of interest; some endlessly chased by a creature
   (world: spots and anchors; animal: behaviour).
5. **New folk races:** fish folk in the sea (reef or underwater settlement) and snow folk in the cold (igloo
   villages). Animal session: races; world session: settlements.
3. **Equipment layers** (animal session): head, torso, back and hand slots on folk and the player; tools and gear
   picked per job (rod, rake, bucket, axe, spear, torch); per-culture loadouts; a replicated equipment mask.

6. **Tree add-ons and weird-biome flourishes** (world session; beetles: animal session): bracket fungus, vines, moss, cocoons, crystal growths, glowing seams; pulsing veins, blinking bark faces, trees and fronds that bend toward or away from the player, breathing mushrooms. Optional FloraParts and shader motion from _PunchyClock; no new landmark types.

## Shared-file queue

| # | File | Session | Why | State |
|---|---|---|---|---|
| 1 | `CLAUDE.md` | Animal behavior manager | Pointer to `GLOSSARY.md` (J-004) | Committed on Zach's direct word (2026-10-29); ships with the post-RC push |
| 2 | `TerrainSurface.shader`, `BuildChunkMeshJob.cs` | world feature manager | Wet band above the waterline (3b), additive, animal session OK'd | Done (5e971a1) |
| 3 | `Scenes/World.unity` | Animal behavior manager | Remove the placeholder Body/Visor capsule from Player (feedback A-3) | Done (9887b9a) |
| 4 | `Grass.shader`, `GroundDetail.hlsl`, `GroundDetailSystem.cs` | world feature manager | Glow on grass (W-18), noise wind and density (19b/c); animal session told | Done (709aad7) |
| 5 | `FloraSystem.cs`, `Flora.hlsl`, flora culling compute | world feature manager | Tilt field for rocks (W-16), one commit, animal session told | Lent |
| 6 | URP renderer asset | world feature manager | SSAO tuning (W-14, J-024) | Done (c776a88) |
| 7 | `TerrainStreamingSystem.cs` | world feature manager | Per-frame chunk-mesh cap while moving (perf batch) | Lent |
| 10 | `Flora/FloraPlacement.cs` | world feature manager | Biome-border flora compatibility (19e) | Lent |
| 11 | Sky shader, `WeatherSystem` moon parts | world feature manager | Moon phases and the second blue moon; Sun stays the animal session's | Lent |
| 9 | `Scripts/Player/PlayerMovementSystem.cs` | animal feature manager | Back to animal after PR #89 (box collision, #79). World lane: ask before editing | Owned |
| 8 | Shader/PSO warm-up at load, `Weather/WeatherField` redraw rate | kind-possum | Glide hitches, weather job cost; own b/ branch | Lent |

## Open questions

(none; see `JUDGMENT_CALLS.md`)

## Gotchas

- A merge that changes a scene can raise Unity's "reload the scene?" dialog, which silently blocks the editor and
  the CLI. Check for it if the editor seems hung after a merge.
- **Adding a landmark type to a biome list reshuffles the whole island** (LandmarkBaking places from one sequential random stream): caves change style, towns move, every bench coordinate goes stale. Flag it to the supervisor and kind-possum before pushing one; prefer props on existing landmarks where that works.
- Watch line endings in shaders and assets; the world session added a pre-commit check after two accidents.
- **A Punchy.exe with no path, 0 threads and 0 MB is a dead process entry, not another account's game.** Every lane runs as the same Windows user, so check the command line and its -logFile folder (which session ran it) before reporting someone else is using your build.
- **No Unity API calls in static field initialisers of any type Burst code reads** (DetailPolicy, policy/tuning classes). Burst runs those initialisers; `Shader.PropertyToID` there failed the compile (BC1091) and silently dropped every job to managed code (terrain 60-100 s per shot instead of ~5 s, R-13 2bcbbba9). Put such IDs in a nested class Burst never touches. If shots suddenly crawl, check the console for Burst errors first.

## Log

- 2026-10-27: Lanes written down from the working arrangement. Light process adopted (J-016). Shared docs on the
  `coordination` branch (J-014). Both editors stay open (J-015).
- 2026-10-27: Animal session listed earlier additive edits outside Fauna, all pushed: Ambience (dive splashes),
  CreatureGhosts (Net), NetMenu stats line, ThirdPersonCamera watch mode, the Flora placement split. Accepted;
  further edits outside the lane go through the supervisor. Traces done (5a4dd10); b/weather-mapjob merged (a90cfd4).
- 2026-10-27: World session's lane gains Terrain/Anchors/**, Glacier.asset, GlacierIce.asset; TerrainAuthoring.cs is
  shared (bake calls). On record: World.unity (one line, Glacier in the biome list, ab49284), PlayerMovementSystem
  (lava block added 575986b, removed e9df26b, net zero), Water.shader (lava block). Main PC memory: commit 58/64 GB;
  animal editor restart requested; world session blocked on its subscene load until memory clears.
- 2026-10-27: Both editors restarted (animal 16.4 to about 7 GB, world 9.6 to 4.6 GB); commit 58.9 to 37 GB. Rule 8 added.
- 2026-10-27: Friend's repo (anweather/5e-compatible-game) has no licence: ideas only, no copied code or assets
  without the author's permission.
- 2026-10-27: Animal session's additive edits to Net/NetPlayer.cs and Net/World/CreatureGhosts.cs (carry
  replication) accepted; it wrote both. Multiplayer carry test goes on the other PC by Relay, between benchmark runs.
- 2026-11-03: #24 dungeons: `DungeonSocket` contract pushed on player-client d7ed5eb (Assets/Game/Scripts/Dungeon/DungeonSocket.cs:
  buffer element; SocketKind, SocketSize, SocketTags flags, DungeonSocket.MakeSeed). Animal lane: start the spawner from it.
  Changes to it get a heads-up here first.
- 2026-11-04: #24 socket buffer live on player-client be6d005 (Dungeon/TempleSockets.cs): TempleSocketSystem makes one entity per world
  "Dungeon (sunken temple)" with DungeonInfo {Id, Type=DungeonTypes.SunkenTemple (1), Sockets, Hash} and a DynamicBuffer<DungeonSocket>
  (68 on seed 8121: creature 15, chest 8, wall decor 15, interactable 15, clutter 15; hash 9C11B352, same every run). Biome is 0 for now.
  A-50 spawner: read it, never write it. F9 / -sockets draws them.
