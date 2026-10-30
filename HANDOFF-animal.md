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

- **Climbing (F-03/F-04), done:** CreatureSpecies.ClimbsAs (Trees / AnySurface); trunks for monkeys, cats, squirrels,
  bears; lizards and spiders also huts and houses onto the roof; leap off with jump; mid-air latch under 8 m/s.
  Others see it (WIRE: NetPlayerLook.Climbing bool, 6d70335) and others' mantling hands (WIRE: NetPlayerLook.Rim byte,
  2a8007a): both await kind-possum's two-machine check. Cave-rock crawling stays on `wip/player-crawl`.
- **Worms, on `wip/worms` (fd5a752):** CreatureSpecies.Tunnels; SandWorm (Dunes, GlassDesert), SnowWorm (Glacier,
  Tundra); CreatureBurrowSystem feeds `_PunchyBurrowers[8]` (x, yaw, z, radius, negative while out) to the world's
  TerrainSurface.shader (agreed; their ripple pending). Merge into animal-behaviours once that lands and is checked.
- **Open question, BrainJob growth:** ~0.6 ms more on workers than at e4010f2, not EaseGait (kind-possum). Profiler
  markers Brain.Hunter/Predator/Chaser/Alarm/Playmate/Needs/Fidget/Decide/Advance/EaseGait are in (ea3bdad); a capture
  should name the culprit. Candidates changed in that range: Think wrapper + EaseGait call, Accelerate in the movers,
  3D threat distance (F-06), F-14 light choice.
- **NPC conversations stage 1, on `wip/dialogue` (84af2d4; out of v0.10 unless solid by the cut):** folk say a short
  line as you walk by, shown over their heads (FolkTalkSystem, DialogueArchive). Local only, nothing on the wire.
  Checked in a dwarf hold and a human village; the file format is below.
- Perching on joined clients (no FloraLookup there).
- Not yet seen in play: the oasis heron and swallows (maybe the settled cap was full).
- Old local branches safe to delete: `wip/fish-ghost`, `wip/gear`, `wip/living-plants`, `wip/moths-swallows`.

## Dialogue file: `Assets/StreamingAssets/dialogue.json`

In a build it's `Punchy_Data/StreamingAssets/dialogue.json`. Edit it while the game runs: it's read again a second
after it's saved. A broken edit keeps the last good file and logs the parse error once (with line and position).

```json
{
  "format": 1,
  "cultures": {
    "Any":   { "words": { "trade.Fish": ["fisher"], "town_start": ["Ash"], "town_end": ["ford"] },
               "lines": [ "Mind how you go." ] },
    "Dwarf": { "words": { "culture_greeting": ["Hail", "Stone keep you"] },
               "lines": [ "{culture_greeting}.",
                          { "text": "Ore's running thick today.", "trade": "Mine" },
                          { "text": "Sleep now.", "when": "night", "weight": 2 } ] }
  }
}
```

- **Cultures** are species asset names. A speaker looks in its own species first (`HumanChild`), then the species
  that one is based on (`Human`), then `Any`. Lines come from the first of the first two that has any that fit, plus
  `Any`'s at 0.3x weight. Seeded: Any, Human, HumanChild, Dwarf, Elf, Goblin, Ogre, Hermit.
- **Lines** are a plain string or an object with `text`. Optional fields:
  - `when`: day, night, dawn, morning, afternoon or evening/dusk.
  - `trade`: one CreatureTask name or a list (Hammer, Chop, Gesture, Tend, Watch, Hunt, Patrol, Fish, Mine, Travel,
    Hermit, Camper).
  - `weight`: default 1.
  - Unknown keys are kept on the line (`Line.Raw`), so dialogue options (`replies` and the like) can be added later
    without a format change.
- **Slots** are written `{name}` and filled in this order:
  - A word list with that name, looked up through the same chain. A culture's own list wins over Any's, and words can
    hold slots themselves.
  - `{trade}` reads the list `trade.<Task>`; `{time_of_day}` reads `time.<morning|afternoon|evening|night>`, falling
    back to the plain word.
  - The built-ins: `{home_town}`, `{species}` (the speaker's kind) and `{player}` (your body's species; filled only
    for folk of another kind).
  - A line with a slot that can't be filled is skipped (for example `{home_town}` for a traveller or a hermit). A
    filled slot at the start of a line gets a capital letter.
- **Town names** are `town_start` + `town_end`, picked from the culture of the town's folk (its landmark's upright
  residents) and hashed from where the town stands, so every machine names a town the same.
- **Pacing:** in FolkTalkSystem's constants. Folk speak within 5 m, each at most every 90 s, one new line every 2.5 s,
  and at most 3 lines up at once. Folk who are asleep, resting, fleeing or at home stay quiet.

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
