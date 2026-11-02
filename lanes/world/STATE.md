# World lane: state (2026-11-02, after W-02, blocked)

Worktree `Punchy-world` (main PC), branch `world-features`. Backlog: `gh issue list -l lane:world`.
Released: v0.11.0 = world b39522b + animal c810406.
Pushed head: a96a1dc. Local only: ec53f8a (W-02 code: moon, sand puffs, flower bend), compiled, never built or seen.

## BLOCKER
A player from W-01 still runs: Builds\Dev\Punchy.exe pid 11484 + crash handler 16700. It locks the exe, so a
build fails ("Copying the file failed"). Stop it, then build. Always stop shot-list players by PID at the end.

## In ec53f8a (to verify in one build + one shot list)
- #34 moon: near #0000FF, lit sphere, rock mottling, mixed craters, no crack, wavy rim tendrils instead of a halo.
  Same framing as W-01 moonnight: -goto -255,27 -time 21 -camlook -255,40,27,-184,108,6 (pin -bluemoondays 0).
- #44 dunes: Assets/Game/Ambience/SandPuffs.asset on Dunes (density 1.5). `-nosandpuffs` drops it for GPU compare.
  Find dunes with `-place Dunes`.
- #30 flowers: cause is Flora.hlsl wind shear + mesh sway growing with distance from axis (no character push exists
  for flora). Bend now length-keeping; flowers by height only. Check trees and bushes did not change badly.
  Daisy etc: `-place prop:Daisy`; wind via `-weather Storm` vs Clear.
- #4 `-menu Graphics` no menu: MainMenu.Start reads -menu then SetOpen; code looks right, test alone with -nohud off.

## Checked
- Landmark hash a96a1dc: 101, E128CB1D (= G-37 baseline).
- W-01 items (#3 #32 #4 #5 build) as before; pad input behaviour still needs hands or a pad.
- Assets/Resources/ appeared untracked (editor-made test settings); not committed.

## Branches still unmerged
- `b/bench-runner` db3c1f2: `-benchday`. wip/ui-menu, wip/ui-wheel, world-laptop/z34-pool are inside world-features.

## Open, not started
- #15 look checks owed (Z-30 posts close up, Z-10 fog over non-marsh river). #13 Z-35 fall speed (Zach in play).
  #14 cave-mouth shards (needs a spot). P3 needing a design note: #17, #18-#22. Roadmap #23, #24.
