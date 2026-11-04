# Animal lane: state (2026-11-04, A-66 done)

Worktree `Punchy` (animal lane only). `animal-behaviours` = origin/main 8502bca (PR #127, A-66 row 1) + 69340e1 (A-66 #60 Decide tiers), pushed.
Fauna fingerprint D0B9488A (114 species; branch and main).

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
