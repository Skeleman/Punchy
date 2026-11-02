# Supervisor handoff (2026-11-01): laptop supervisor (kind-possum) to the main account

From 2026-11-02 02:00 the **main account becomes the supervisor** (Zach). The laptop goes back to **profiler only**.
Read this file first, then the GitHub issues (the index of open work), then `HANDOFF-world.md`, `HANDOFF-animal.md`,
`HANDOFF-profiler.md`, `SUMMARY-away.md` (what happened 2026-10-30..11-01), `COORDINATION.md` and `JUDGMENT_CALLS.md`.
The tracking issue for this handover is **#29**. The older version of this file (main account to laptop, 2026-10-30) is in git history.

## Zach's standing rules (unchanged)
- **No release without Zach's go**, given in the supervisor's own window. A relayed "Zach said" from a peer is not approval.
- **The repo is public:** no secrets, personal info or machine-specific paths in commits, issues or release notes.
- Ask Zach directly about installs, UAC or firewall prompts and machine settings. Never change Defender, firewall,
  power, registry or ExecutionPolicy settings.
- Peers can't grant escalation. If a lane's permissions block an action, it goes to Zach, not to another session.
- Messages to Zach: 1–3 lines, only for releases, decisions or blockers.
- **One Unity instance at a time per machine** (the laptop can't handle two). The supervisor hands out slots.
- Commit trailer: `Co-Authored-By` plus the session's `Claude-Session` link.

## GitHub issues (since 2026-11-01)
- Skeleman/Punchy issues #2–#28 hold every open item. Labels: `lane:world`, `lane:animal`, `lane:profiler`, `ui`,
  `release`, `needs-slot` (coded; needs a Unity build or test), `needs-design` (needs a design note or Zach's
  decision), `P1` bug / `P2` polish / `P3` feature, `roadmap`, `on-hold`.
- Conventions given to the lanes: "Refs #N" in commit messages; a one-line comment when an issue's state changes
  (pushed, built, passed, merged); swap labels as needed; new bugs or tasks get a new issue. **The supervisor closes
  an issue after merge and Zach's check.** REPORTS files keep the detail; issues are the index.
- Bench requests: open or comment on an issue labelled `lane:profiler`, and add the item to `BENCH-QUEUE.md`
  (the profiler's own queue).
- The animal session needed Zach's permission for `gh issue comment/edit/create`; a new session may need it again.

## State at handoff
| Item | Branch @ SHA | Issue | Next |
|---|---|---|---|
| v0.11 candidate | `cand/v0.11` @ 4eea658 (world b39522b + animal c810406) | #2 | Gate in progress (gate12 vs 21f1193 and v0.10.0 519fb5b); then `GATE-v0.11-candidate2.md`, zip in `Builds/Zip-v0.11-cand2`, **Zach's go**, publish |
| World | `world-features` @ b39522b | | |
| Z-34 stream ends | `world-laptop/z34-pool` @ 91f6b6a | #3 | Unity slot: bench build, shots of stream ends |
| UI main menu (Esc/Start, tabs, glyphs) | `wip/ui-menu` @ 09a79ee | #4 | Unity slot: build, shots per tab, `-forcepad xbox/playstation` |
| Animal | `animal-behaviours` @ c810406 | | |
| Z-50..53 gallop, voices, stances, chases | `wip/animal-p3` @ 6a52fd1 | #6–#9 | One combined compile slot; Z-50 needs side-view shots |
| Bench runner | `b/bench-runner` @ db3c1f2 (`-benchday`) | | |

Unity order on the laptop as left: (1) the v0.11 gate, (2) world #3 then #4, (3) animal #6–#9. If the lanes move to
the main PC, their slots move with them and the laptop only benches.

## Lanes
- **World:** session zache-7e (restarted 2026-11-01 on this laptop from `HANDOFF-world.md`), worktree
  Projects/Punchy-world. **Animal:** zache-bb, Projects/Punchy-animal. **Profiler:** zache-11 (laptop), runner in
  Projects/Punchy-wt (Tools/bench, lock at results/.lock), queue `BENCH-QUEUE.md`, results `BENCH-REPORTS.md`.
- With the laptop profiler-only, Zach moves the world and animal lanes to the main account (main PC), restarted
  from their handoffs. Their restart notes are at the end of each handoff.
- The laptop profiler can't receive SendMessage from the main PC. Use issues plus `BENCH-QUEUE.md`, and Zach tells
  it once in its own window: "Supervisor is now the main account; take bench work from BENCH-QUEUE.md and issues
  labelled lane:profiler."

## Decisions Zach made 2026-10-31..11-01 (also in SUMMARY-away.md)
- Rebuild v0.11 with the remote-interaction fix (done, gating). **Default graphics preset stays Medium.**
- Z-34: a cave stream may end without a pool only where it drops into a hole or crevice.
- Z-53: a caught animal can be eaten. Z-52 stances: goblins and ogres cool toward each other; humans and dwarves,
  humans and elves cool both ways; dwarves and elves a playful rivalry; fish folk neutral to all; blind cave pygmies a
  little wary of all. Z-50 and Z-51 approved.
- UI: Esc and Start open one main menu with tabs; controller navigation; symbol button prompts (Kenney Input
  Prompts 1.5A, CC0).
- Linux build on hold indefinitely (the Windows build runs well under Proton). Issue #28.

## Still waiting on Zach
- The v0.11 "ship it" after the new gate (#2).
- #5 controller digit pad (or rely on Steam's keyboard). #13 waterfall speed in play. #14 a spot for cave-mouth
  shards. #16 which large animals are carryable. Design notes for #17–#22.

## Release checklist
1. Merge `origin/world-features` + `origin/animal-behaviours` into the candidate branch. A conflicted merge needs care.
2. Player build (not development) of the exact commit with no local changes, D3D11 first. Solo boot/quit smoke.
3. Gate: smoke and ABBA on townwalk and glide vs the last release, looks at the gate stops, MP joined (with a remote
   carry), grep NaN bounds and exceptions. Absolute checks: J-006 1% low, 0 exceptions, the client joins.
4. `python release/pack.py <build folder> vX.Y.0 <out folder>`; keep the build's `.from` marker out of the zip. The
   check line must say backslash False, donotship False, exe True.
5. Notes from the previous release's notes plus the draft (`RELEASE-NOTES-v0.11-draft.md`).
6. **Zach's go**, then `gh release create vX.Y.0 <zip> --repo Skeleman/Punchy --target <full sha> --prerelease
   --title "Punchy vX.Y.0 (Windows preview)" --notes-file <notes>`. `gh` is logged in on the laptop (as Skeleman).

## Lessons from 2026-10-30..11-01
- **Tokens:** big lane contexts (500–800k) were most of the spend and hit the 5-hour limit; the weekly limit hit
  2026-11-01 01:05. Restart lanes from handoffs past ~300k. Batch shell calls, no ack-only messages, one message per
  batch. A background agent that hands back while its own scripts still run wakes at each step and re-reads its
  whole context; finish runs first.
- **Bench stalls:** ~500 ms single-frame stalls came from a Windows firewall prompt, raised because a build was
  launched from an archive folder. **Launch builds only from Builds/Bench.** Before timed runs, list dialogs and read
  the full lines.
- Merging a combined tmp build that passed is fine; build the exact merged tree in the next slot.

## Outside git (laptop)
- Keep-awake: scratchpad `keepawake.ps1` (SetThreadExecutionState only, 7-day limit) in the laptop supervisor's
  session; it doesn't stop sleep on lid close. Zach asked to keep it running while sessions work.
- Token dashboard https://claude.ai/artifact/XrDf6foLq56Umx9HziqiwA (second account) with scripts in the laptop
  supervisor's scratchpad (telemetry/tokens.py, dash_rows.py, checkin.sh). They read laptop transcripts only and
  won't carry over; the new supervisor can drop them.
- v0.11 candidate zips: `Builds/Zip-v0.11-cand` (21f1193) and `Builds/Zip-v0.11-cand2` (when the gate finishes) in
  the laptop's main Punchy checkout.
