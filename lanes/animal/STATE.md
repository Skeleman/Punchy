# Animal lane: state (2026-11-02, after batch M-03)

Worktree `Punchy` (animal lane only). On `animal-behaviours` = origin a5c8e9e (A-34), on top of ab3c458 (A-33).
main 41a9259 (M-03, PR #53) has animal a5c8e9e and world fd91300 (not W-34 0bdc707). Merged build: v2 126 /
053C63D5, v1 102 / 51EA6463.
Untracked `Assets/_Recovery/` is old and not ours. Editor on port 7800.

## Shot flags added (A-34)
- `-walk x,y,z`: steers the player (PlayerInputSystem.TestMove) toward the point's xz until within 0.3 m. Logs every
  0.25 s: position, metres to go, height over the floor, cave biome, and PlayerMovementSystem.PushedTotal (metres
  the cave push-out has moved the player since the -walk line). Use with -goto start and -wait N; shot is mid-walk.
- `-untilwork S`: holds the shot until the watched creature has been in CreatureMode.Work for 1 s (max S seconds).
- `-watch *:Job`: any species. If the species is wrong, one warning names who holds the job. The readout says
  "no height" when TargetHeight is 0 (folk at work), plus species and FolkJobs.Of.

## -watch Ogre:Merchant (A-34)
- Not a camera bug: OgreCamp's merchants are Goblins (log: "that job is held by: Goblin Goblin"). "119 m below"
  was TargetHeight 0 at work. `-watch *:Merchant` frames the goblin merchant at the stall.
- Ogre baker shot came at Work stage 0 (walking to post); ogre gatekeeper at Work stage 1 with spear.

## Slope push-out pair (A-34): still no visible difference
- seg 87: -goto -3993.8,54.4,812.7 floors at y 47.4 (7 m below the given point); walking to -3989.4,44.3,807.3
  the player walks off a drop and falls (3-6 m over the floor), so it is airborne, not pressing a slope.
- Before (-nopushout): pushed 0 m. After: pushed 0.9 m in the first 0.25 s, 1.6 m in all, then nothing.
- The 1.0 s mid-walk shots lost the player off frame; the 0.5 s pair shows the player hanging beside the wall
  in both, a little lower and nearer in "after". Not an obvious clipping difference.

## Job work poses and props (A-33)
- CreatureAnimator Tend switches on FolkJobs.Of(s): Knead, TendBody, Haul, LookAbout + spear for guards.
- CreatureRenderSystem GearParts 8 (0-1 tool, 2 apron, 3-4 hat, 5-7 extras). All derived from job/task;
  a client sees the task-implied job. Rule 11 holds.
- Seen: baker, healer, gatekeeper, well keeper, dwarf guard/smith; A-34 added human woodcutter swing (axe over
  head, a34-woodcutter-a) and human smith hammering with sparks (a34-smith-side).

## Crossing fishers (A-31/32)
- Fauna/RoadCrossings.cs; roll Scramble(centre) % 3 == 0. v2 fallback at -2544.3,884.6. `-place crossing`.

## Cave collision and camera (A-30/32)
- PushOutOfSolids runs only in caves, only on a moving player's step, gated by NoPushOut.
  ThirdPersonCamera.CaveArm sphere-traces. Bodies fade when the camera is within ~0.6 m.

## Test flags
- Shot lists: -place, -goto, -view, -camrel, -watch Species:Job (or *:Job), -watchfrom h,d, -nopushout, -walk,
  -untilwork, -time, -weather, -nohud, -chatshot, -wait, -shot. Launch: -shotlist, -shotdir, -quit, -island v1.
- Hashes: v2 125 / D709D95C (A-34 player log); v1 101 / E128CB1D (A-31; debug/movement-probe change since).

## NEXT
- Merge origin/main (41a9259) into animal-behaviours.
- Slope pair: seg 87 is a drop, not a slope. Pick a walk with a continuous floor (probe floor height along the
  line first), keep -walk short (2-3 m), and frame wide with -camrel so the body stays in shot.
- Merchant gesture: the goblin merchant is framed but the gesture does not read; shoot closer, front-on, with
  -untilwork.
- Ogre baker settled at work (-untilwork 20).
- Close-camera fade before shot; fords (0 found).
