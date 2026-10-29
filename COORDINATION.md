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
13. **Waiting on someone:** before blocking on another session, ask that session whether you actually need to
   wait. If they don't answer within the hour, ask the supervisor.

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
| 1 | `CLAUDE.md` | Animal behavior manager | Pointer to `GLOSSARY.md` (J-004) | Written, uncommitted, waiting on Zach's direct word |
| 2 | `TerrainSurface.shader`, `BuildChunkMeshJob.cs` | world feature manager | Wet band above the waterline (3b), additive, animal session OK'd | Done (5e971a1) |
| 3 | `Scenes/World.unity` | Animal behavior manager | Remove the placeholder Body/Visor capsule from Player (feedback A-3) | Done (9887b9a) |
| 4 | `Grass.shader`, `GroundDetail.hlsl`, `GroundDetailSystem.cs` | world feature manager | Glow on grass (W-18), noise wind and density (19b/c); animal session told | Done (709aad7) |
| 5 | `FloraSystem.cs`, `Flora.hlsl`, flora culling compute | world feature manager | Tilt field for rocks (W-16), one commit, animal session told | Lent |
| 6 | URP renderer asset | world feature manager | SSAO tuning (W-14, J-024) | Done (c776a88) |
| 7 | `TerrainStreamingSystem.cs` | world feature manager | Per-frame chunk-mesh cap while moving (perf batch) | Lent |
| 10 | `Flora/FloraPlacement.cs` | world feature manager | Biome-border flora compatibility (19e) | Lent |
| 11 | Sky shader, `WeatherSystem` moon parts | world feature manager | Moon phases and the second blue moon; Sun stays the animal session's | Lent |
| 9 | `Scripts/Player/PlayerMovementSystem.cs` | Animal behavior manager | Player flight for flying species | Lent |
| 8 | Shader/PSO warm-up at load, `Weather/WeatherField` redraw rate | kind-possum | Glide hitches, weather job cost; own b/ branch | Lent |

## Open questions

(none; see `JUDGMENT_CALLS.md`)

## Gotchas

- A merge that changes a scene can raise Unity's "reload the scene?" dialog, which silently blocks the editor and
  the CLI. Check for it if the editor seems hung after a merge.
- **Adding a landmark type to a biome list reshuffles the whole island** (LandmarkBaking places from one sequential random stream): caves change style, towns move, every bench coordinate goes stale. Flag it to the supervisor and kind-possum before pushing one; prefer props on existing landmarks where that works.
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
- 2026-10-27: Friend's repo (anweather/5e-compatible-game) has no licence: ideas only, no copied code or assets
  without the author's permission.
- 2026-10-27: Animal session's additive edits to Net/NetPlayer.cs and Net/World/CreatureGhosts.cs (carry
  replication) accepted; it wrote both. Multiplayer carry test goes on the other PC by Relay, between benchmark runs.
