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
8. Main PC memory: restart your Unity editor (after committing) when it grows past about 10 GB or after heavy
   recompile runs. Keep Play-mode tests short and exit when done. Ask the supervisor before opening a player or
   build next to the two editors.

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
| Animal behavior manager | `Scripts/Fauna/**`, `Fauna/**` assets, `Resources/Domestics.asset`, `Scripts/Debugging/DebugControls.cs` (shared test flags), `Scripts/Weather/WeatherSystem.cs` Sun handling, `GLOSSARY.md` |
| world feature manager | `Scripts/Terrain/Caves/**`, `Scripts/Terrain/Anchors/**`, `Scripts/Terrain/Landmarks/**`, `Scripts/Terrain/Settlements/**`, `Scripts/Editor/CaveTools.cs`, `Biomes/Glacier.asset`, `Biomes/Surfaces/GlacierIce.asset`, `Landmarks/*.asset`, cave style and settlement assets, `Shaders/Cave.shader`, `WORLD.md` |
| kind-possum | `Scripts/Debugging/BenchmarkRoute.cs` and its own `b/*` branches |

**Shared, additive only (tell the other owner):** `Flora/FloraGenerator.cs`, `Flora/FloraPlacement.cs`,
`Flora/FloraSystem.cs`, biome assets (landmark lists only), `Water.shader` (world session is doing the surface
work; animal session gets told first), `TerrainSource.cs` and `TerrainAuthoring.cs` (entry points and bake calls only).

**No owner, changes go through the supervisor:** `CLAUDE.md`, `MULTIPLAYER.md`, `.gitignore`, scenes,
`ProjectSettings/`, `Packages/`.

## Current tasks

| Session | Task | State |
|---|---|---|
| Animal behavior manager | Lairs (nests at Nest anchors); then bat rework (J-013) | In progress |
| world feature manager | Wet band (Terrain.shader), stream follow-up; then phase 4 towns | In progress |
| kind-possum | Route determinism, WeatherField MapJob fix, round 5 | In progress |

## Shared-file queue

| # | File | Session | Why | State |
|---|---|---|---|---|
| 1 | `CLAUDE.md` | Animal behavior manager | Pointer to `GLOSSARY.md` (J-004) | Written, uncommitted, waiting on Zach's direct word |
| 2 | `Shaders/Terrain.shader` | world feature manager | Wet band above the waterline (3b), additive, own commit, animal session told | Lent |

## Open questions

(none; see `JUDGMENT_CALLS.md`)

## Gotchas

- A merge that changes a scene can raise Unity's "reload the scene?" dialog, which silently blocks the editor and
  the CLI. Check for it if the editor seems hung after a merge.
- Watch line endings in shaders and assets; the world session added a pre-commit check after two accidents.

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
