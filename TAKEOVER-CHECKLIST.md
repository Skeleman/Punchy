# Supervisor takeover checklist (kind-possum → supervisor)

Zach says the supervisor will hand the whole project to kind-possum (the laptop session, second Claude account)
when its tokens run out. Written 2026-10-30 by kind-possum, from the laptop's side, so the handoff covers what this
machine and account can't see or do. Please answer in `HANDOFF-supervisor.md` (or inline here) before handing over.

## What the laptop can't do today
1. **Releases.** No `gh` CLI here. Installing it and `gh auth login` are Zach's to do; kind-possum will ask him.
   Until then a release needs Zach or the main PC.
2. **Milestone page.** https://claude.ai/artifact/YCS5QeFTV4KDFEUTwiXC7D is on the supervisor's account; this account
   can only update it with edit access. Its source, images and release notes are in `D:\Unity\PunchyMilestones\_page`
   on the main PC, which doesn't exist here.
3. **Messaging.** The world and animal sessions are on the main PC; kind-possum can't SendMessage them. Everything
   would go through files on `coordination`.
4. **Main-PC memory (J-015).** Not visible from the laptop.
5. **Release builds.** v0.8.0's zip was built on the main PC with a Python zipfile script. The laptop can make clean
   player builds (`Builds/Play`, rule 14).

## Please put in the final handoff
- [ ] Per lane (world, animal, solo, any other): current branch and hash, what's in flight, what's next, what's
      blocked. Which session names are live.
- [ ] The release checklist: version tagging, target branch, the zip script (or where it lives), release notes
      template, what Zach must approve. Whether a laptop build is acceptable for a release.
- [ ] v0.10 state: what's in, what's still to land, its gate (J-040 light gate), who decides "ship it".
- [ ] Open judgment calls and anything waiting on Zach.
- [ ] The milestone page: share it with this account (edit) and copy `_page` somewhere the laptop can reach
      (a branch, or Zach copies the folder), or say it stops being updated.
- [ ] How lanes report to the new supervisor: a file per lane on `coordination` (like BENCH-QUEUE/BENCH-REPORTS),
      pulled before each task. Proposed: `QUEUE-<lane>.md` written by the supervisor, `REPORTS-<lane>.md` by the
      lane, newest first.
- [ ] Main-PC memory: who watches it once the supervisor is gone (a lane session, or Zach).
- [ ] Anything kept outside git (scratchpads, D:\Unity\..., scheduled check-ins) that the next supervisor needs.

## After the handoff, kind-possum will
- Read COORDINATION.md, JUDGMENT_CALLS.md and the handoff before acting; keep the profiler queue running.
- Ask Zach directly for installs (gh), UAC, and every release go. A relayed "Zach said" isn't approval (rule 6).

## Answered
Answered in HANDOFF-supervisor.md (2026-10-30, supervisor). Release script and notes templates are in release/.
