# Supervisor handoff (2026-10-30, FINAL: main account at 99%): to kind-possum on the second account

This answers `TAKEOVER-CHECKLIST.md`. The supervisor (main account) keeps this file current until it stops. Read it
together with `HANDOFF-solo.md`, `HANDOFF-world.md`, `HANDOFF-animal.md`, `COORDINATION.md` and `JUDGMENT_CALLS.md`.

## Two modes
- **Solo (main account out of tokens).** The world and animal sessions run on the main account too, so they stop
  as well. You're the only session: follow `HANDOFF-solo.md` (branch `solo/v0.10`, no publishing without Zach's go).
- **Supervisor (lanes live on the main account, you supervising).** You can't SendMessage them, so lanes talk
  through files on `coordination`, using your proposal (adopted):
  - `QUEUE-world.md`, `QUEUE-animal.md`: written by you, top item first;
  - `REPORTS-world.md`, `REPORTS-animal.md`: written by the lane, newest first, one entry per batch;
  - each lane pulls `coordination` before each task and after each push.
  - When you take over in this mode, Zach tells each lane in its own window: "Supervisor is now kind-possum; read
    QUEUE-<lane>.md and report in REPORTS-<lane>.md." A relayed note isn't enough (rule 6).

## Lanes (state at the time of writing; each lane's handoff has the detail)
| Lane | Session (main PC) | Branch, tree | In flight / next |
|---|---|---|---|
| World | "world feature manager" | `world-features`, D:\Unity\Punchy-world | worm ripples, G-37 decoration rocks, F-40; G-32 and G-22 wait on Zach for a spot |
| Animal | "Animal behavior manager" | `animal-behaviours`, D:\Unity\Punchy | emote conversations/animal reactions; worms on wip/worms |
| Profiler | kind-possum (you) | `b/*` | `BENCH-QUEUE.md` items 8-12 |
| Release | supervisor | `rc/vX.Y.0`, D:\Unity\Punchy-rc | v0.9.0 shipped from rc/v0.9.0 39f5558 |
"Unity multiplayer debugging setup" (laptop) has been offline all of v0.9 and has no lane.

## v0.10 (J-040)
- **In so far:**
  - World: G-15/F-25 wakes, G-17, G-18 + G-23 roads (landmark hash 7AFF6C9F, 104 → 100), G-28, G-29, G-30 + G-36,
    G-31/F-46, G-33/F-47, G-34 blue moon, G-35, breached lakes, net/terrain audit items, F-50 bubbles, G-37
    MagneticRock (hash now E128CB1D, 101 landmarks).
  - Animal: G-02..G-09, G-11..G-14, G-16, F-14..F-19, gold variants, climbing F-03/F-04 + F-07 mantle (wire: Climbing
    bool, Rim byte), the join fix, the wake feeder, brain markers, dialogue stage 1.
- **Still to land:** see "In flight" above. Anything not done by the cut is left out, not waited for.
- **Gate:** the light gate (smoke vs v0.9.0 39f5558, plus host/join with the climbing pose and join fix in it).
  The island moved, so use absolute checks (J-006 1% low, 0 exceptions, the client joins), not cross-island A/Bs.
- **Who decides "ship it":** Zach, in your own window. Ask him once the gate passes.

## Release checklist
1. Merge `origin/world-features` + `origin/animal-behaviours` into a new `rc/vX.Y.0` (from the previous rc or
   world-features). A conflicted merge needs care: read both lanes' handoffs.
2. Build a player build (not development), D3D11 first with D3D12 fallback, at the commit's hash. **A laptop build
   is acceptable** if it's a clean player build of the exact rc commit with no local changes. Solo boot/quit smoke,
   no `-screen-*` flags.
3. Run the light gate on that commit.
4. Zip it: `python release/pack.py <build folder> vX.Y.0 <out folder>`. The check line must say backslash False,
   donotship False, exe True.
5. Notes: copy `release/release-notes-v0.9.0.md`, then "New since vPrev", bold sections, plus Multiplayer and
   Download.
6. **Zach's go**, then `gh release create vX.Y.0 <zip> --repo Skeleman/Punchy --target <full sha> --prerelease
   --title "Punchy vX.Y.0 (Windows preview)" --notes-file <notes>`. gh is installed on the laptop (2026-10-30); if `gh auth status` fails, Zach runs `gh auth login`. Otherwise Zach
   logs in, or publishes himself from the release page with the same fields.

## Waiting on Zach
- G-32 (mushroom tree LOD colour shift) and G-22 (forum base stretching): the world session can't reproduce them.
  It needs a -goto spot or a description.
- The v0.10 "ship it".
- `gh` on the laptop; sharing the milestone page (below).

## Milestone page
- https://claude.ai/artifact/YCS5QeFTV4KDFEUTwiXC7D, on the main account. It wasn't updated during v0.9 (only finished
  page features go on it, and Zach asked for frugality).
- For you to update it, Zach shares it with edit access to the second account. Its source and images are in
  `D:\Unity\PunchyMilestones\_page` on the main PC. Zach copies that folder to the laptop if he wants it continued.
  Otherwise **it pauses**, and the GitHub release notes are the record.

## Main-PC memory (J-015)
With no supervisor on the main PC, each lane watches its own editor: restart it past ~10 GB, close yours first if
free memory goes under 3 GB, and take editor turns when both need one. Zach is the backstop.

## Outside git
- `D:\Unity\PunchyMilestones\` (main PC): `_page` (milestone page source, release notes), `_release` (the v0.9.0
  zip), `<lane>\<feature>\gallery` (unreviewed full-res shots for Zach).
- `D:\Unity\PunchyHooks` (main PC): the installed commit hooks (source in `hooks/`). Telemetry CSV:
  `~/.punchy-telemetry/commits.csv` on each machine.
- Builds: world tree `Builds\Dev\Punchy.exe` (candidates); animal tree `Builds\Windows` (its own tests).
- Scheduled check-ins live only in a running session (CronCreate). Recreate them at every 2-3 h.
- A keep-awake script (supervisor scratchpad `keepawake.ps1`) may be running on the main PC; it ends on reboot.

## How the supervisor has run things (keep or change)
- Push without asking, except shared files, network tables, landmark placement, conflicted merges and releases
  (rules 11-13).
- Reports come per batch, not per feature. Zach gets 1-3 lines, only for releases, decisions or blockers (he's low
  on tokens).
- Log every decision of Zach's as a J-entry, and check the log before asking him anything.
- Zach's new ideas go in `FEEDBACK-*-rc09.md` (G items, for the current release) or `ROADMAP-IDEAS.md` (long range).
