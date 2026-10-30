# Solo handoff: one session runs Punchy alone (J-042)

For the session Zach starts on his second Claude account (the laptop, normally kind-possum) when his main account
runs out of tokens. You work alone: there are no world, animal or supervisor sessions to message, and none to wait
for. The supervisor keeps this file current; read it first, then the files it points to.

## Read first (on `coordination`)
1. This file.
2. `COORDINATION.md` rules 11-14 and 12a-c (shot sizes, bench cadence, fixed build paths). Lane ownership is
   suspended while you're solo: you may edit any file, but keep the shared-file care those rules describe.
3. `HANDOFF-world.md` and `HANDOFF-animal.md`: each lane's gotchas and unfinished work, refreshed at the end of
   their last batch.
4. `FEEDBACK-2026-10-29-rc09.md` (G items), `FEEDBACK-2026-10-29.md` (F items), `ROADMAP-IDEAS.md` (long range,
   don't start these solo), `JUDGMENT_CALLS.md` (J-001..J-042: decisions already made; don't re-ask them).
5. `BENCH-QUEUE.md` / `BENCH-REPORTS.md`: your own bench work.

## State (supervisor updates this)
- **Released:** v0.9.0, https://github.com/Skeleman/Punchy/releases/tag/v0.9.0 (target 39f5558 on rc/v0.9.0).
- **In progress:** v0.10.0 (J-040: same light gate, no playtest by Zach). The work is on `origin/world-features`
  and `origin/animal-behaviours`; they merge cleanly with each other.
- **Island:** v0.10 roads changed the roadside landmarks once (hash D82C9D7F to 7AFF6C9F, 104 to 100). Don't change
  landmark placement again before v0.10 ships.
- Status of the lanes when you took over: see "Last lane status" at the bottom.

## How to work solo
- **Branch:** create `solo/v0.10` from `origin/world-features` merged with `origin/animal-behaviours`, and commit
  there. Never push to `world-features` or `animal-behaviours`: those sessions resume later and merge
  `solo/v0.10` themselves.
- **One editor at a time** on the laptop (4 cores). Restart the editor if it passes ~10 GB.
- **Batch:** several items, then one compile and in-game check, then a push. Separate commits per item. Commit
  messages say what changed and how you checked it, as in the existing history.
- **Check before you push:** console 0 errors; for visual items a written check, plus at most 3 shots ~1000 px in
  `PunchyMilestones\<lane>\<feature>\gallery\` if that folder is reachable, otherwise in your own shots folder.
- **Bench** your own batches with your runner (smoke at townwalk and glide, ABBA order). Anything over noise:
  fix it or revert it.
- **Telemetry:** the commit hooks are installed (J-041); nothing to do.
- **Releases:** don't publish. If v0.10 looks ready, run the light gate (smoke vs v0.9.0 39f5558, host/join),
  write the result in `BENCH-REPORTS.md` and wait for Zach's go in your own window. If he gives it, follow
  "Publishing" below.
- **Ask Zach only** for releases, machine settings, firewall/UAC prompts, and installs. Decide everything else
  yourself and log real judgment calls in `JUDGMENT_CALLS.md`.

## Gotchas (from both lanes; the details are in their handoffs)
- **Line endings:** CRLF files silently go LF when edited. Run `git ls-files --eol <file>` before every add, and
  restore CRLF where the index has it.
- **Shaders:** after editing .hlsl or a shader, run `AssetDatabase.Refresh` before any shot, or it shows the old shader.
- **Hashing:** `math.hash(uintN)` has no avalanche; scramble before comparing per-name draws.
- **Landmarks:** new landmark types take only their own spots. Check the landmark hash after any terrain, road,
  biome or settlement edit.
- **Test runs:** never pass `-screen-fullscreen/-width/-height` to a player that uses Zach's prefs key (F-01).
  Bench builds use the PunchyBench product name.
- **Friend's repo** (anweather/5e-compatible-game) has no licence: ideas only, no copied code or assets.
- **Network:** a change to ghost fields, RPCs or the world fingerprint breaks compatibility. Allowed before
  v0.10 ships, but say so in the commit message.

## What to work on (top first)
1. Anything the lanes left half-done ("Last lane status" below): finish it, or park it on a `wip/` branch.
2. Open v0.10 items still not done in the G and F lists, the most visible first.
3. Your own perf queue (`BENCH-QUEUE.md`).
Don't start roadmap systems (items, quests, dialogue, dungeons): they need design decisions Zach hasn't made yet.

## Publishing (only on Zach's go)
- Build a player build (not development), D3D11 first with D3D12 fallback, to your fixed build path.
- Zip it with a Python zipfile script: forward-slash paths, leave out `*_BurstDebugInformation_DoNotShip`.
- `gh release create vX.Y.0 <zip> --repo Skeleman/Punchy --target <full sha> --prerelease
  --title "Punchy vX.Y.0 (Windows preview)" --notes-file <notes>`. Notes follow
  `D:\Unity\PunchyMilestones\_page\release-notes-v0.9.0.md` on the main PC: "New since vPrev", bold sections.
  If you can't reach that file, the v0.9.0 release page shows the format.

## When the main account is back
Push everything, write a short "Solo summary" section here (what landed on `solo/v0.10`, what's on wip/, and the
bench results), and stop. The supervisor merges from there.

## Last lane status (supervisor, final, 2026-10-30)
The lane handoffs (HANDOFF-world.md, HANDOFF-animal.md) were refreshed at this point and are the detailed source.
- **World** (world-features ~1cbdd9f):
  - Landed for v0.10: the island hash is now **E128CB1D** (101 landmarks; MagneticRock added).
  - Next:
    - worm ground ripples (terrain side of the animal's wip/worms);
    - G-37 smaller floating rocks scattered as a biome decoration (no landmark change);
    - F-40 stall footprint;
    - the caveapproach hitch (BENCH-QUEUE item 10).
  - Waiting on Zach: G-22 forum base and G-32 mushroom LOD colour (need a spot); a gold-leaf leak in normal
    forests that the world session couldn't reproduce (gate already tightened; ask Zach which build and where).
- **Animal** (animal-behaviours):
  - All G and F items done except F-02 (held).
  - v0.10 also has climbing (wire: Climbing bool + Rim byte), gold variants, the join fix, dialogue stage 1
    (StreamingAssets/dialogue.json, format in HANDOFF-animal.md).
  - Open: emote-started conversations and animal reactions (maybe on wip); worms on wip/worms, merging after the
    world ripples.
  - BrainJob is still ~0.6 ms heavier on the workers: capture with the Brain.* markers (BENCH-QUEUE item 8).
- **v0.10 cut:** merge both lanes into rc/v0.10.0. Run the light gate: smoke vs v0.9.0 39f5558 with absolute checks
  (island moved), plus the MP check in BENCH-QUEUE item 12. Then wait for Zach's "ship it" (HANDOFF-supervisor.md
  has the release checklist).
