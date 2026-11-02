# v0.11 candidate 2 gate (2026-11-01, task session; NOT tagged, NOT released)

**Candidate:** `cand/v0.11` 4eea658b8a98d3dfc9f3d9b65998fd8f79064d75 = 21f1193 + animal-behaviours c810406 (Z-20
InteractRpc). Clean merge, pushed; same tree as a4a5378. **Compared with** v0.10.0 519fb5b and the old candidate
21f1193, same island (every stop ends at the same x,z). Default preset Medium (unchanged).
Results: Punchy-wt/Tools/bench/results (g12-*, q-g12*, g12j-*, mp-4eea658-*, q-play-4eea658-*,
animal-slot/g12carry-*). No dialogs open before any run (full window list checked each time).

| Item | Result | Key numbers (4eea658 / 21f1193 / 519fb5b) |
|---|---|---|
| Release build (ReleaseBuild, BuildOptions.None) | PASS | Succeeded, product Punchy, 0 error CS, 0 shader errors; Builds/Play = 4eea658 (.from) |
| Boot/quit x2 (from Builds/Bench, no -screen-*) | PASS | exit 0 both, Direct3D 11.0, not development, 0 exceptions, 0 NaN bounds |
| Smoke townwalk ABBA, cap 60 / 0 (1% low) | PASS | 35.0 / 34.8 / 35.6 ; 43.0 / 40.2 / 45.9 |
| Smoke glide ABBA, cap 60 / 0 (1% low) | PASS (J-006 borderline, as v0.10) | 29.6 / 29.3 / 29.3 ; 37.1 / 35.0 / 34.5. Worst frames 29-48 ms on all; no ~500 ms stalls |
| Looks with shots (day 6 stops, night 2, spire) | PASS | 9 shots per build, exit 0, 0 exc; checked by sight: flora, lake, cave, glowing grove, spire arcs. Glide 1% low 34.7 / 32.3 / 36.5 |
| MP smoke (solo vs joined, host cap 60) | PASS | players 2 at every stop, 0 exc, 0 IOOR. Joined townwalk 25.1 vs solo 35.5 (two games on one laptop, as in the last gate) |
| MP joined ABBA, 3 builds (town / townwalk 1% low) | PASS | 26.0 / 25.9 ; 27.9 / 25.0* ; 25.0 / 24.9*. *One 639 ms (21f1193 r1) and one 409 ms (519fb5b r2) single-frame stall, run left out; none on the candidate. No dialog open; Windows Update worker and Defender were running |
| **Remote carry** (host -spawn Cat:2, client -join -interact Cat:carry) | **PASS on 4eea658; fails on 21f1193** | 4eea658: host logs "player 3 act 2 ... 94 cm", client sees "Cat is Held (holder 3); pose Carry" at 0.5 s, shot shows the cat in its arms. 21f1193: "Cat is Flee (holder 0); pose None", empty hands |
| MP climb (Monkey, client -shotlist) | PASS | players 2, 11 shots, 0 exc, 0 IOOR either side |
| NaN bounds / exceptions grep | PASS | all logs of all three builds: Mesh bounds 0, NaN bounds 0, Exception 0 |
| Zip (`python release/pack.py`, .from held out) | PASS | Punchy-v0.11.0-windows.zip: 237 files, 49,494,933 bytes, SHA-256 4544dbd2dc5748de1b1183b79d7c555c79df8a34292f9b0e05856f9949d4a046; backslash False, donotship False, exe True, no .from. Local only: Projects/Punchy/Builds/Zip-v0.11-cand2 |

## Verdict
All items pass. The candidate is level with 21f1193 and v0.10.0 everywhere (cap 0 glide is slightly better), and the
remote carry now works. The first gate's cap-0 glide spikes didn't come back (worst 33.7/37.5 ms on the candidate).
Ready for Zach's decision; nothing blocks it.

## Notes
- The first run of this gate stopped after the release build: a bad edit in the gate script (not the build). The
  build log showed Succeeded before the checkout moved, so the build was kept and the gate resumed from boot/quit.
- No tag, no GitHub release, nothing uploaded. Notes: RELEASE-NOTES-v0.11-draft.md (Multiplayer: the interaction fix).
