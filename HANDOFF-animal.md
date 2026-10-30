# Handoff: animal behavior manager (v0.10 in progress)

Refreshed 2026-10-30 for whoever carries the animal lane next, including a solo session (J-042, HANDOFF-solo.md).
Lane, rules and the shared log live in `COORDINATION.md`, `MULTIPLAYER.md` and `GLOSSARY.md`; this note is what those
don't say. Checkout: `D:\Unity\Punchy`, branch `animal-behaviours` (pushed; v0.9.0 published from 39f5558).

## State: done since v0.9.0 (all pushed on animal-behaviours, each checked in play)

- Bird and fish bodies (G-02..G-09): stick pitches gently with the camera, steep climb/dive, real dive speed-up, perching
  (FloraPlacement.NearPerches; solo and host only), trunk sliding, per-species PlayerFlySpeed; fish swim up/down with
  the camera, no sprint-dive, fast rise from deep, surface leap with speed, land hop; bone lizard walks on land.
- Creatures: cave-pool fish (drawn only), smith/woodcutter at the world's Work anchors (G-17), torches for goblins and
  ogres (F-14), great-tree fireflies (F-19), oasis/graveyard residents (F-15/16), songbird perches, darker crow, drink
  pose, F-07 mantle hands on the rim (local player only), F-17 ocotillo mimic, F-18 mud-pit pigs, rare golden animals
  (1 in 400 by Individual, nothing new on the wire), G-14 footsteps by ground (Hooves flag), F-25 swimmer wakes are the
  water's (CreatureWakeSystem feeds `_PunchySwimmers`, the world's Water.shader draws them).
- Fixes and perf: host IndexOutOfRange in TrySpawn on join (counts resized in GatherPlayers; not yet run on two
  machines), cheaper wake feeder (0.14 to 0.01 ms), cheaper F-10 easing (30 m, jitter skipped), AmbientLife.Near
  (0.36 to 0.05 ms), spawn queue, species Role/Voice fields instead of names.

## Unfinished

- **F-03/F-04 climbing bodies (next):** `wip/player-crawl` (local) has cave-rock crawling only, and its body sinks into
  rock on overhangs. Needed: trunk climbing for monkey and lizard (attach to a FloraObstacle trunk, up/down/around, jump
  lets go; avatar Vertical for the pose), walls for the lizard. Outdoor cliffs may already mostly work for a crawler
  (the player follows ground at any slope; the animator lays Crawls bodies on steep ground). Keep it on a wip branch
  until solid.
- **Open question, BrainJob growth:** ~0.6 ms more on workers than at e4010f2, not EaseGait (kind-possum). Profiler
  markers Brain.Hunter/Predator/Chaser/Alarm/Playmate/Needs/Fidget/Decide/Advance/EaseGait are in (ea3bdad); a capture
  should name the culprit. Candidates changed in that range: Think wrapper + EaseGait call, Accelerate in the movers,
  3D threat distance (F-06), F-14 light choice.
- Remote players' mantle hands (the rim isn't sent), perching on joined clients (no FloraLookup there).
- Not yet seen in play: the oasis heron and swallows (maybe the settled cap was full).
- Old local branches safe to delete: `wip/fish-ghost`, `wip/gear`, `wip/living-plants`, `wip/moths-swallows`.

## Gotchas worth keeping

- **Editor:** both editors may run at once (Zach's OK); keep play runs short. Never write an unbounded loop in a probe:
  a hung editor can't be killed by the session (permission), only by Zach. Close your own with a probe calling
  `EditorApplication.Exit(0)` on delayCall. After a forced close Unity leaves `Assets/_Recovery` (untracked; ignore).
- **Probes:** `unity command script run --file X.cs --entry Class.Method`; job markers on worker threads don't show in
  a ProfilerRecorder from a probe (ask kind-possum for captures). Puppet foot-slide runs vary about 2x run to run.
- **Editing files:** UTF-8 always, encode before writing (a cp1252 write truncated a file once); each file keeps its
  own line endings (never Git Bash `sed` on CRLF files: it strips them). Staging part of a file: hunks with full
  context (`git diff -U3`, `git apply --cached`), never `-U0` (it misplaced code once).
- **Assets by hand:** a YAML list written `Field: []` needs rewriting as a block before appending entries; after
  editing an asset on disk, Assets/Refresh and a ForceBake (subscene reimport, wait ~2 min) before play.
- **Hashing:** `math.hash(uint2)` keeps the low bit of its input; Individuals are odd, so use high bits (`>> 8`).
- **Wire:** append-only tables; new species or landmark residents move the fingerprint (supervisor's go, rule 11).
- **Shots:** look at your own at ~560 px; set the camera from a probe with its MonoBehaviours disabled; pass
  `-weather clear` for colour checks; no images to the supervisor unless asked.
- **Builds (rule 14):** only `Builds/Windows/Punchy.exe` or `Builds/Dev/Punchy.exe`. Captures: PrintWindow on the game
  window only; keys only when the game has focus.
