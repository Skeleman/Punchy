# Animal lane: state (2026-11-04, A-50 partly done)

## A-50 (cd9dda5 merge of be6d005, d243ab7; pushed, not built)
- Dungeon spawner, posts and the -dungeontest cave stand-in live in Assets/Game/Scripts/Fauna/Dungeon/ (ours).
  Temple picks 0x7c8c8b71, stand-in 0xa9995035, same on ServerWorld and ClientWorld.
- Open: 0 DungeonPost entities after spawn (row 2 unproven). Check whether the director culls the dungeon creatures.
- Builds/Dev is locked: two crashed Punchy.exe hold lib_burst_generated.dll (stale, 23:13). Kill them before building.
- Temp/PlayArgs.txt was deleted after the run (the old a29b line is gone; write a new one per shot run).

Worktree `Punchy` (animal lane only). On `animal-behaviours`: 5fe1f85 (A-48 #63 caches, pushed, built to Builds/Dev) on
f0be45c (A-48 guard; doesn't compile alone) on a merge of origin/main 3a4acb8 (PR #94). Older: 3ba679b (A-43).

## A-49 (7e05224, flags only, not built)
- New shot flags: `-goflat N` (Nth flat spot, each 150 m apart: a fresh herd per line), `-thirsty`, `-watch Species:Mode`.
- #46 holds: 30 m pass 0/10, 5 m 10/10 on fresh herds. #12: wander turn rates are about 0 now; Play turns at 77-116 deg/s.
- #64 waits on Zach (3-line proposal in REPORTS). The Drink shot needs a better frame (riverfall framed the waterfall).
- This editor's log is D:/Unity/Punchy/Logs/Editor.log. Editor shots: the shotdir must exist (mkdir first).

## A-48 (f0be45c, 5fe1f85, built)
- DebugControls.QueriesLive(): rebuilds cached queries when the default world changes (a client joins or loses the
  host), false if no world. Use it before any cached-query TryGetSingleton. Host+client: 0 exceptions both sides.
- #63: sky-bird cell grounds cached (CreatureRenderSystem.SkyGroundSamples), songbird ground perches cached
  (AmbientLife.PerchGroundSamples). The "flees this line" log prints both counters. #63 still open (partial).
- Net runs: joins started while host/client are loading time out or drop (Couldn't reach / ClosedByRemote).
  Wait for the host to finish loading before shots that need a real client; tell multiplayer lane.

## Older notes (A-43 and before)
On `animal-behaviours` at A-43: 3ba679b (A-43, pushed, built) on 89270af (A-42b) on 237d226 (A-42) on e3b8521 (A-41) on b2b6a70 (A-40) on e75918e (A-39). Lane hashes (A-40 build): v2 126 / 053C63D5 (A-41 touches no world code).
Untracked `Assets/_Recovery/` is old and not ours. `Temp/PlayArgs.txt` holds an old a29b line (editor Play mode
reads it as extra flags); swap it out only for an editor shot run and put it back (restored after A-41).
Editor: ports are swapped (7800 reaches Punchy-world). Target the animal editor by --pid (26472 on 2026-11-02;
the Unity.exe whose -projectpath is the Punchy tree). Check `Application.dataPath` first.

## A-43 (3ba679b, built)
- SkunkGas draws with Resources/SkunkGas.mat (Punchy/Gas Puff, unlit, no depth fade); falls back to PuffMaterial.
- RemoteAvatar.Species: local species index for remote bodies' sounds (bodies keep Species -1).
- Leap probe: Temp/a43_leap.cs -> Temp/a43leap.txt. `-leapphase p` works per shot-list line (frozen-phase strips).
- Readout logs `DebugControls: readout ...` on entering/leaving a cave. F8 skips Cave landmark types.
- Ashlands (`-place Ashlands`) is dark flat ground, good for side-on `-pose` shots without -watch.

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
