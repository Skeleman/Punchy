# Handoff: animal behavior manager (after v0.8.0)

Written 2026-10-29 by the outgoing animal session for its fresh restart (J-037). Lane, rules and the shared log live in
`COORDINATION.md`, `MULTIPLAYER.md` and `GLOSSARY.md`; this note is what those don't say.

## Branches

- `animal-behaviours` (pushed, e4010f2): the lane's branch, now v0.8.0 (ba87e14) + world-features (5268dc5) + all
  the post-release work below, compiled and smoke-tested (hermit and critters running).
- `local/doors-ruins` (local, now merged into `animal-behaviours`): my working branch since the RC. It carried
  world-next's then-unpushed doors, ruins, perches, camp and hermit anchors and bedrolls, which is why it waited for the
  world session's push.
- `wip/fish-ghost`, `wip/gear` (local): parked copies, now cherry-picked into the work above; safe to delete.
- `wip/living-plants`, `wip/moths-swallows`, `wip/player-crawl` (local): older parked work. Living plants and
  moths/swallows are in; `wip/player-crawl` (player crawling on surfaces) is not, and is the next to pick up for F-04.
- `rc-fix-ambient`, `rc-fix-critters`, `rc-fix-sprint`, `animals-realms` (local): their commits are on `rc/v0.8.0` or in
  `animal-behaviours`; safe to delete.

## Done since v0.7.0, and what's not verified

On the release (`rc/v0.8.0`, published as v0.8.0): the critter tier, AmbientLife, head-look, walkable tops and mantling,
caps, butterflies thinned, village animals cut, cat and horse eyes, wingbeats; RC fixes 03960d1 (host stall: critter
spots cached and settled in a Burst job), 194a57d (glide stall: AmbientLife settled in a Burst job, none high up),
ba87e14 (F-02 sprint reset, real-time timer; Zach's regression didn't reproduce solo in the editor).

After the release (pushed with this handoff), each checked in play unless noted:
- Realm animals: a Beetle body and 22 species for Fungal, Ember, Mesa, Glass, Electric (biome fauna lists, additive).
- Living plants: snapping flytrap (Rooted, IdleAction.Snap), shuffling shrub (Disguised).
- Player sit and lie (IdleAction.SitDown, LieBack; Seats.Nearest on Sit anchors).
- Ghosts at BoneGraveyard and Ruins; villagers opening doors (FloraDoors.SetOpen from replicated state).
- Sky birds (geese and cranes in a V at dawn and dusk, soarers by day; drawn only, from the shared clock).
- Hair in the wind (Flora.hlsl's gust formula); loose cloth (sash tail, ogre guard cape).
- Honey thief (a stranger raids a hive and runs); hermits and roadside campers (Camp, Bed, Home, Garden contracts).
- Songbirds on building perches (FloraPlacement.NearPerches); per-job gear (hammer, axe, bucket, aprons, hats).
- Playable fish (Trout) and ghost (Ghost), appended to PlayableSpecies; ghost controls to Zach's spec.
- Creatures well behind the camera aren't posed or drawn (40% of a village); Mute in background (GameAudio).
- Not verified in play: mute in background (needs alt-tab by hand); the deep-cave butterfly gate; campers leaving in the
  morning; travellers at the real roadside camps (test at `-goto -476,99`); a clean roof-bird final (my avatar was in
  every good frame; one candidate is in the perches gallery).

Wire changes since v0.7.0 (all append-only; all clients on the same build): CreatureNet LookPitch and Held;
CreatureTask Travel, Messenger, Critter, Hermit, Camper; HeldItem through Honey; IdleAction Poke, Mantle, Snap,
SitDown, LieBack; PlayableSpecies + Trout, Ghost; the species table grows (realm species, Hermit), shifting indices.

## Next queue

Zach's v0.8.0 feedback, `FEEDBACK-2026-10-29.md` (lane A items). Proposed v0.9 minimum for this lane:
- F-09 snake tail wag at speed; F-10 foot sliding (sync every cycle to speed, carrying folk especially);
- F-20 cave audio (no outside creature sounds inside; reverb by cave size); F-21 thin the ambient creature calls.
Then the rest of lane A: F-03/F-04 climbing bodies (start from `wip/player-crawl`), F-05 animal emote calls, F-06
fliers and the stick, F-07 mantle hands, F-08 animal petting, F-11 gallop, F-12 breath, F-13 smith at the forge (check
the Work anchor with W), F-14 torches by race, F-15 oasis life, F-16 graveyard scares, F-17 ocotillo mimic, F-18 mud-pit
pigs, F-19 great-tree fireflies, F-25 swimmer wakes (with W), F-48 waterfall life.
Perf headroom kind-possum measured (194a57d): PoseJob ~1.7 ms, BrainJob ~0.8 ms on workers, doubling on spike frames
(likely spawn bursts); the behind-camera cull is the first cut.

## Gotchas worth keeping

- **Memory-killed steps:** if Claude Code stops a command for low memory, restart it only when Zach asks in this
  window; a relayed "Zach said" doesn't count. Keep to one editor; stop your own idle AssetImportWorkers.
- **Line endings:** count with python on bytes (`b.count(b'\r\n')`); Git Bash grep matches every line. Edit with
  python `open(..., newline='')`, preserve each file's own endings; check `git diff --stat --ignore-cr-at-eol`.
- **Builds (rule 14):** only `Builds/Windows/Punchy.exe` or `Builds/Dev/Punchy.exe`, overwriting; new folders raise
  firewall prompts. Don't pass `-screen-*` to a player: it saves windowed mode into this PC's Unity prefs.
- **Captures and input:** PrintWindow on the game's own window only (a screen copy once caught Zach's chat); send keys
  only when the game is foreground (check before every step). Editor input can be simulated with
  `InputSystem.QueueStateEvent` (no focus needed), but a press and release in one probe call misses WasPressed.
- **Probes:** `unity command script run --file X.cs --entry Class.Method` (args as a JSON array); statics reset on
  every call; recompiles need play mode stopped (the Recompile.ps1 pattern checks the dll timestamp); a scene changed
  on disk raises a reload dialog that blocks the CLI (click Reload); after a forced close the editor may open an
  Untitled scene (reopen SampleScene) and ask to recover backups (Yes, then move Assets/_Recovery out).
- **World-next dependence:** world anchors and landmarks need a subscene reimport (ForceBake) after merging.
- **Pairs and shots:** before/after pairs hold time, weather and coat fixed; avatar out of frame.

## Shots

Full-resolution galleries under `D:\Unity\PunchyMilestones\animals\`: `eyes`, `sky-birds`, `honey-thief`,
`doors-ruins`, `wild-folk`, `perches`, `mp-rc080` (MP test logs and builds' logs).
