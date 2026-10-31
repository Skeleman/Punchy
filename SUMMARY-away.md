# While Zach is away (2026-10-30 17:00, about 24 h)

Kept by the supervisor (kind-possum), newest first. The full detail is in REPORTS-*.md, BENCH-REPORTS.md and
FEEDBACK-v0.10.md (Z-IDs). No release goes out without Zach's go.

## 2026-10-30 21:25: usage limit; setup change
- All four sessions hit the account's 5-hour usage limit at 17:21 and stalled until it reset at 20:30 (about 3 h lost).
- Token telemetry: the world and animal lanes' contexts (~800k and ~530k tokens, re-read on every call) were ~75% of
  the spend. Dashboard: https://claude.ai/artifact/XrDf6foLq56Umx9HziqiwA
- **Decision:** world and animal write HANDOFF-<lane>.md and pause. Their queued code items run as short, fresh task
  sessions started from the handoff, one at a time. The profiler finishes slot 4 (worm/wake cost, gfx preset sweep).
- **For Zach on return:** restart the world and animal lane sessions from their handoff files (cheap), or keep the
  task-session setup.

## 2026-10-30 17:05: starting point
- **Merged so far (v0.11):**
  - World: graphics settings (F5 panel, presets, Quit), cave-mouth hitch, Blocked fast path, F-40 trodden ground,
    G-37 rocks, worm marks, the Z P1 fixes, creature graphics ranges.
  - Animal: climbing (trunk-hug, trunk-lean), worm colours, the Z P1 fixes, the wolf howl, the rattle, the moo and
    croak, jump heights, ghosts.
- **In code, waiting on a slot:**
  - World P2 (Z-30, 33, 36-42, Z-12 skirt, Z-38 caps).
  - Animal Z-20 carry, Z-21/22 poses, Z-28 worm arc + rocks, Z-07 spin check.
  - The worm trail and wakes, after the profiler's cost pass.
- **Unity now:** the profiler (item 17 worm/wake cost, the graphics preset sweep). Next the world lane, then the animal lane.
