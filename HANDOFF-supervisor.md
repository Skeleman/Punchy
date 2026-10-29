# Supervisor handoff (2026-10-29, after v0.8.0)

## State
- v0.8.0 published: https://github.com/Skeleman/Punchy/releases/tag/v0.8.0 (target ba87e14 on rc/v0.8.0; D3D11 first,
  D3D12 fallback, depth priming Forced; zip built on the main PC with a Python zipfile script, forward slashes, no
  `*_BurstDebugInformation_DoNotShip`). Release notes source: `D:\Unity\PunchyMilestones\_page\release-notes-v0.8.0.md`.
- Next goal: v0.9 = the held work (now on world-features, and animal-behaviours once it pushes) + the playtest bug
  fixes in `FEEDBACK-2026-10-29.md`.
  - Minimum, world: F-22 F-24 F-30 F-31 (entrance fix) F-34 F-36 F-42 F-43 F-44 F-48 F-49 F-52.
  - Minimum, animal: F-09 F-10 F-20 F-21 (F-02 only if Zach still sees it).
  - Then Tier 1 polish one item at a time while Zach has tokens. He chooses or says "ship it".
  - Tier 1: F-05 F-06 F-08 F-11 F-12 F-35 F-37 F-40 F-41 F-50 F-33 F-46 F-47 F-27 F-14/F-39 F-15 F-16 F-19.
  - Tier 2 (later): F-03 F-04 F-07 F-23 F-26 F-31 multi-entrance F-17 F-18 F-38 F-25.
- The v0.9 release gate is light: kind-possum's automatic runner smoke, plus a short host/join check. No long matrix.
  Zach is low on tokens this week: keep messages short and images rare.

## Milestone page
- https://claude.ai/artifact/YCS5QeFTV4KDFEUTwiXC7D. A durable copy of the source and images is in
  `D:\Unity\PunchyMilestones\_page\`.
- To update it from a new session: `Artifact read` the URL first, then publish with `url` set. Images are added
  through `files` with `root` set to the `_page` folder.
- Entries go after `<div class="log">`, as `<article class="entry" style="--c: var(--world|--animals)">`.
- Only finished, visible features go on it, each with one 800 px final (rules 12a/12b).

## How things run
- Rules 11-14 and 12a-c in COORDINATION.md:
  - push without asking, except shared or unowned files, network tables, landmark placement, releases;
  - shot sizes, an unreviewed gallery, a bench cadence per batch;
  - fixed build paths.
- Judgment calls are J-001..J-038 in JUDGMENT_CALLS.md. J-037 is the restart plan; J-038 is the D3D11 decision
  (wait for Unity 6.7 LTS before revisiting D3D12).
- The supervisor publishes releases itself with `gh`, only on Zach's go. Peers don't publish.
- Peers won't act on a relayed "Zach said" for things that need the user (CLAUDE.md commits, restarting
  memory-killed steps, installs, UAC). Tell Zach to say it in their window.
- Memory: two editors plus import workers can starve the main PC (31 GB). Take editor turns, and restart an editor
  past ~10 GB.
- Firewall and UAC prompts: nobody clicks them. Ask Zach.
- The hourly check-in cron dies with the session. Recreate it at every 2-3 h to save tokens.
- A keep-awake process may still be running on the main PC (scratchpad keepawake.ps1). It ends on reboot.
