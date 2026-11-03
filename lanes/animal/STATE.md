# Animal lane: state (2026-11-02, A-42b done)

Worktree `Punchy` (animal lane only). On `animal-behaviours`: 89270af (A-42b, pushed, built) on 237d226 (A-42) on e3b8521 (A-41) on b2b6a70 (A-40) on e75918e (A-39). Lane hashes (A-40 build): v2 126 / 053C63D5 (A-41 touches no world code).
Untracked `Assets/_Recovery/` is old and not ours. `Temp/PlayArgs.txt` holds an old a29b line (editor Play mode
reads it as extra flags); swap it out only for an editor shot run and put it back (restored after A-41).
Editor: ports are swapped (7800 reaches Punchy-world). Target the animal editor by --pid (26472 on 2026-11-02;
the Unity.exe whose -projectpath is the Punchy tree). Check `Application.dataPath` first.

## A-42 / A-42b (237d226, 89270af, built)
- SkunkGas.cs: FleeGas species puff on flee start; logs `SkunkGas:` lines. A-42b: bigger, brighter, more puffs.
  Editor close-up shows a faint green haze (Puffs.mat soft shader dims it a lot); build shots still show no cloud.
- Goblin HumanArm direction 155. Lying leg fold in CreatureAnimator (OldLie SharedStatic, `-oldlie`).
- A-42b: lying body raised by side slope x half width: probe 0 cases below -0.05 m. Probe: Temp/a42_probe.cs.
- `-pose` is reset per shot-list line and waits 5 s (settle) after each line: repeat `-pose X:how` on every line.
- World moved: -255,27 is now a steep snowy mountain (~166 m). Use `-place Start` plus `-watch Species -watchfrom h,d`
  (without -watch the camera snaps back to the player). `-place Dunes` did not move the player.

## Warp keys (A-36)
- F5 biome, F6 settlement, F7 cave, F8 landmark; Shift back. `-place Start` returns to the spawn.

## Shot flags
- `-warp`, `-walk x,y,z`, `-untilwork S`, `-watch *:Job`, -place, -goto, -view, -camrel, -camlook, -watchfrom h,d,
  -nopushout, -time, -weather, -nohud, -chatshot, -wait, -shot. Launch: -shotlist, -shotdir, -quit, -logFile.
- A shot-list line without -shot still saves `shot-N.png`; delete those. -camrel is relative to the player at
  line start: put the -goto on its own line first so the player has landed.
- Editor shots: put `-shotdir Temp/X -shotlist Temp/X.txt` in Temp/PlayArgs.txt and enter Play mode.
- Editor walk-probe log lines don't reach %LOCALAPPDATA%/Unity/Editor/Editor.log for this editor.
- 800 px copies: Python PIL thumbnail.

## NEXT
- Skunk gas visibility: own unlit/alpha puff material or no depth fade, side view on dark flat ground.
- A-41 follow-up if Zach asks: the after runner looks a bit crouched at mid-stance (knee bend in run keys).
- From A-40: if diag -1 feels sticky at the cave corner, try a wall slide that keeps the 0.10 m limit.
