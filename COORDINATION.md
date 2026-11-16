# Coordination

How the Claude sessions on Punchy stay out of each other's way. Lives on the `coordination` branch.

**This file is the core: read it once per card.** Detail is in the modules named in the rules table; read only
those your card lists. Don't read JUDGMENT_CALLS.md, SUPERVISOR-HANDOFF.md, REFACTOR-ROADMAP.md, ROADMAP-IDEAS.md
or another lane's REPORTS unless your card names a section.

The supervisor hands out cards, reviews and merges. It writes no feature code and never edits another session's
tree. Reach it by SendMessage (main account) or through your REPORTS file (other accounts).

## Aim (Zach, 11-13)

A world that feels expansive, yet full of life and detail. Distant views matter most: the whole island should feel
grounded in beautiful nature.

Priority when work competes for a lane, a file or the profiler: (1) the far view; (2) life and small details, near
and far; (3) shared foundations (one lighting path, one detail policy, one item and save model); (4) new feature
lines. A card's Why says which it serves. Programme: #298.

The island's layout is a test bed. Systems and procedural generation come before the map; the world may change
(`lanes/shared/world-data.md`).

## Lanes

| Lane | Tree | Cards | Owns (under `Assets/Game/`) |
|---|---|---|---|
| world | `D:/Unity/Punchy-world` | W, some R | `Scripts/Terrain/{Caves,Anchors,Landmarks,Settlements}/**`, water surfaces, `Shaders/Cave.shader`, landmark, cave-style, settlement and glacier assets, `WORLD.md` |
| folk | `D:/Unity/Punchy-folk` | A (folk, races, households) | `Scripts/Fauna/**` and `Fauna/**`, shared with fauna and split by card |
| fauna | `D:/Unity/Punchy-fauna` | A (creatures, far life) | as folk; also `Resources/Domestics.asset`, `Scripts/Debugging/DebugControls.cs`, `Scripts/Audio/**`, `Scripts/Ambience/`, Sun handling in `WeatherSystem.cs`, `Scripts/Player/PlayerMovementSystem.cs`, `GLOSSARY.md` |
| render | `D:/Unity/Punchy-render` | R (refactors, rule 28) | `Shaders/*.hlsl`, the surface lighting path, `DetailPolicy` |
| player (2nd account) | `D:/Unity/Punchy-player` | P | `Scripts/Settings/**`, `Scripts/UI/**`, `Scripts/Camera/ThirdPersonCamera.cs`; interiors, doors, pickups, inventory (#26) |
| profiler (other PC) | its own | BENCH-QUEUE jobs | `Scripts/Debugging/BenchmarkRoute.cs`, `b/*` branches; small measured perf or crash fixes on `b/fix-*` (`lanes/shared/bench.md`) |

- **Shared, additive only, tell the other owner:** `Flora/FloraGenerator.cs`, `FloraPlacement.cs`, `FloraSystem.cs`,
  biome assets (landmark lists), `Water.shader`, `TerrainSource.cs` and `TerrainAuthoring.cs` (entry points and bake calls).
- **No owner, through the supervisor:** `CLAUDE.md`, `MULTIPLAYER.md`, `.gitignore`, scenes, `ProjectSettings/`,
  `Packages/`, renderer assets.

## Rules

Numbers are stable; cards cite them. Rules with no module are complete as written here.

| # | Rule | Module (`lanes/shared/`) |
|---|---|---|
| 1 | Per card, read: this file, your lane's STATE.md, the card, the modules it names. Nothing more | `../README.md` |
| 2 | Work inside your lane. Ask the supervisor before touching anything outside it | |
| 3 | Commit small and often, and push. Merge, never rebase or rewrite pushed history | git.md |
| 4 | Report DONE / FAILED / BLOCKED / NEXT when a task ends or blocks | report.md |
| 5 | Never discard, reset or overwrite work you didn't write. Uncommitted work outside your lane: stop and report | |
| 6 | Questions for Zach go to the supervisor | |
| 7 | Use the terms in `GLOSSARY.md` | |
| 8 | Batch related changes: one evaluation, one report, separate commits | checks.md |
| 9 | Every NPC and animal heads for an identifiable goal | design.md |
| 11 | Push your own lane without asking. Ask first for the listed exceptions | git.md |
| 12 | Shot rules, who looks at what, the full-res gallery (12, 12a, 12b) | checks.md |
| 13 | Before blocking on another session, ask it whether you need to wait; no answer in an hour, ask the supervisor | |
| 14 | Fixed build paths | unity.md |
| 15 | Editors and RAM | unity.md |
| 16 | Hand off after every verified row | report.md |
| 17 | Don't steal Zach's focus: start-quiet.ps1 | unity.md |
| 18 | Every process ends on its own | unity.md |
| 19 | Lean test suite, no rework | checks.md |
| 20 | LOD by screen size, never fixed metres | rendering.md |
| 21 | Only the profiler profiles | bench.md |
| 22 | Placement is data, ready for a world file | world-data.md |
| 23 | Profiling targets per preset | bench.md |
| 24 | NaN-safe rendering | rendering.md |
| 25 | Far view first | rendering.md |
| 26 | One lighting path, one atmosphere | rendering.md |
| 27 | Foundations before stand-ins | design.md |
| 28 | Refactors have right of way | design.md |
| 29 | Docs: one home per fact, paraphrase, size limits | `../README.md` |
| 30 | One effects kit: effects come from the shared kit, never one-offs | rendering.md |
| 31 | No branch goes unmerged: the supervisor opens a PR (draft if it waits) for every branch with a DONE handoff, and sweeps unmerged branches daily (`BRANCH-TRIAGE.md`) | |
| 32 | Issues stay true: every card and handoff names its issue; the supervisor, in the same turn as each merge, closes the issue or comments what is left, updates the ROADMAP-IDEAS status and logs PLAYTEST.md; when Zach reports something, search open issues first and comment on a match rather than open a duplicate; epics close once the base ships, with the rest moved to ROADMAP-IDEAS or a child issue; the `punchy-sync` audit runs after every merge batch and at least daily | lanes/ISSUES-MAP.md |
| 33 | One system per kind of thing: what appears in several places shares one implementation; cards name the system they extend and carry a feature across its whole family | design.md |
| 35 | One port per lane for host/joiner tests (`-port N`, b/fix-port-flag): world 7981, player/fishing 7982, player-2 7983, folk 7984, fauna 7985, render 7986; 7979 is for Zach's builds | |

Rules 10 and 12c are retired (replaced by 15 and by 21 + 23).

## Who decides what (Zach, 11-13)

- Lanes ask the supervisor for everything on rule 11's ask-first list.
- The supervisor decides without waiting for Zach: placement, landmark, river-course and fingerprint changes;
  releases; `ProjectSettings/`; renderer assets. Each renderer or settings change is its own commit with a BENCH job
  before merge.
- Zach decides: design direction and look approvals the card marks as his. The supervisor sends him the shots.

History before 11-13 (old session table, task table, shared-file queue, log): `git show fac41bfb:COORDINATION.md`.

## Rule 34: the board and resumable cards (Zach 11-15)
- `lanes/BOARD.md` has one line per open card. Every handoff updates the lane's own line (status, branch@sha, next step).
- When usage runs low, or before any long run that could be cut off, commit the work in progress to the lane branch
  (`WIP:` subject), push, and set the line to **resumable** with the exact next step. Nothing is left only on disk.
- On resume, read your board line and your card before anything else.
