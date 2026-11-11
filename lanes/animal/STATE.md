# Animal lane: state (2026-11-04, A-67 done)

Worktree `Punchy` (animal lane only). `animal-behaviours` = origin/main 6f139d0 merged + c0277c9 (A-67 bat folk), pushed.
Fauna fingerprint 8726AADB on branch (115 species); main D0B9488A.

## A-68 (paused)
- Row 1 #132 done (3d66652): brain 4.92 -> 3.29 ms at v2 fishing village 3. Rows 2 (BatFolk.asset + LightNear, uncommitted on disk) and 3 (pygmies back with bat folk) left.

## A-67
- #35: BatFolk species in the bat folk chambers (1 chamber, 6 folk); hammock/goggle peg posts; `ShunsLight` -> Shield
  entry (player within 6 m underground), `SurfaceTrips` -> Surface entry with goggles by day. Pygmies moved to the largest
  chamber of a cave with no bat folk site. cpu main 3.23 -> 2.95 ms at `-place batfolk`. Commented, open.
- Weak: ears/hammock not clear in shots; no lantern so the shield pose isn't shown; fires/torches aren't lights.
- On `-island v3` a shot list needs a `-wait 25` warmup line before `-place`.

## A-66
- #10/#35: fish folk in the FishFolk settlements (3 villages, 12-21 each, `Wades`), blind cave pygmies in the bat-folk chambers (1 band
  of 6, `Blind`, ChamberFolk). Fingerprint 2DD98BAE -> D0B9488A. Merged as PR #127.
- #60: Decide = forced -> urgent -> weighted table (69340e1), same RNG order, fingerprint unchanged. Rest -2..3 / Wander +2..3 at
  both spots after; proven equal by differential test (0 mismatches, ~300k decisions), shift = noise. Both issues commented, open.
- Perf/mix recipe: shot list `Temp/a66b.txt` (3 perflog windows, then `-spincheck -wait 65` at HumanVillage and at `-goto 900,-1400`).

## A-65
- #61: already split in A-47c (7 files, max 1325 lines). #63: e4880c2 caches ambience/ear/swarm samples; cpu main 6.32 -> 5.88 ms
  (noise-level), fingerprint unchanged. Both commented, open.

## A-64 (no code)
- #57: stagger already on main (A-45, `SearchDue`, 0.2 s per creature). HumanVillage editor A/B: 3.17 vs 3.63 us/creature
  (-13%), searches 17x fewer. Issue can close if Zach agrees (townwalk bench number still optional).
- #58: 810955f (pre-#117) 3.08 us/creature vs branch 3.17 at the same spot and count: no measurable cost.
- Perf recipe: Temp/PlayArgs.txt `-fpscap 0 -shotdir Temp/a64 -shotlist Temp/a64.txt`; list `-place HumanVillage -time 12
  -wait 48 -perflog`, then `-wait 6|9 -perflog` lines, `-oldsearch` per line for the A/B. Read `perf2` log lines.
- Editor start wipes Temp/: write PlayArgs and shot lists only after the editor answers eval.
- Temp worktree for an old commit: copy Library with robocopy /MT (11 GB, ~1 min); remove with robocopy /MIR from an
  empty dir first (git worktree remove fails on long paths), then `git worktree prune`.

## Open from earlier
- A-63: `-guardtest` (#24) proven host + client. A-50: DungeonPost count 0 after spawn was unproven then.
- #63 partial (sky-bird/songbird caches). #64 waits on Zach.

## Notes kept
- DebugControls.QueriesLive() before any cached-query TryGetSingleton (A-48).
- Editor log: D:/Unity/Punchy/Logs/Editor.log. Target the editor by --project-path.
- Shot flags: see TESTING.md and older STATE in git history (A-50 version).

- 11-10 (supervisor): **crash fix first (#259)**: GameAudio occlusion job runs during the world switch on leave/rejoin (P-31 row 2 stack in REPORTS-player.md). Skip Occlude() while GameBootstrap is switching or no world exists. Small b/fix-* branch from main; the supervisor merges.
- 11-11 (supervisor): when W-134 merges (world/cave-water): the creature brain's swim choice and the fish body's SwimOrFlop still use the 2D SampleGround; switch them to SampleGround(float3) so creatures see cave pools and streams. Not before the merge.
- 11-11 (supervisor): heads-up: the player lane (P-29 row 2, branch player/hair) adds `CreatureHair.ForFolk` in CreatureData.cs, a folk hair call in CreatureRenderSystem.PoseGear.cs, and a separate hair WornCount in CreatureRenderSystem.cs / .Passes.cs (CompactJob total, PoseJob offset). Keep off those lines until it merges; no spawn/RNG change, fingerprint stays AD723954.
