# Animal lane: state (2026-11-02, after batch A-33)

Worktree `Punchy` (animal lane only). On `animal-behaviours` = origin ab3c458 (A-33), on top of cbdf5d1 (A-32).
Untracked `Assets/_Recovery/` is old and not ours. Editor on port 7800.

## Job work poses and props (A-33)
- CreatureAnimator: Tend switches on FolkJobs.Of(s). Baker kneads (Knead), healer bends over (TendBody), well
  keeper hauls hand over hand (Haul). Guards/gatekeepers on watch (Watch at work, or Patrol standing still) get a
  slow head turn (LookAbout) and hold the spear in the right hand when nothing else is held.
- CreatureRenderSystem: GearParts 5 -> 8 (0-1 tool, 2 apron, 3-4 hat, 5-7 extras). HasGear covers Watch.
  Baker: dough, white cap and apron, table and loaf. Healer: bowl, satchel and strap, no hat. Well keeper: rope
  and bucket. Guard: 2 m spear. Smith: tongs and hot bar. Woodcutter: block, log, split piece. Farmer: hoe.
  Trader/merchant (Gesture): jar or fruit by Individual&1.
- All of it is derived from job/task. A client sees the task-implied job (Job is not a ghost field): a baker
  reads as a well keeper there. Rule 11 holds.
- Seen in shots: baker, healer, gatekeeper spear, well keeper (Human); dwarf guard spear, dwarf smith hammer.
  Not seen: woodcutter (log pile in front), merchant/ogre jobs (watch didn't frame them), human smith (idle).
- Bat folk roost keeper/tanner/forager: no bat-folk species walks yet, so there is nothing to show.

## Crossing fishers (A-31/32)
- Fauna/RoadCrossings.cs reads the settlement bake; the roll is Scramble(centre) % 3 == 0, seed-derived.
- FaunaDirector.SpawnCrossingFishers by day with a player within 120 m. Fallback: the lowest-Roll crossing.
  v1: 3 rolled; v2: 0 rolled, fallback at -2544.3,884.6. `-place crossing` frames it.

## Talk bubble (A-31)
- -chatshot sets FolkTalkLabels.BubblesWithoutHud; a32-talk-line.png shows a role line.

## Cave collision and camera (A-30/32)
- PlayerMovementSystem.PushOutOfSolids runs only in caves and only on a moving player's step (from -> position).
  ThirdPersonCamera.CaveArm sphere-traces. Bodies fade when the camera is within ~0.6 m.

## Test flags
- Shot lists: -place, -goto x,y,z, -view, -camrel, -watch Species:Job, -watchfrom h,d, -nopushout, -time,
  -weather, -nohud, -chatshot, -wait, -shot. Launch line: -shotlist, -shotdir, -quit. The player takes -island v1.
- Hashes: v2 125 / D709D95C (A-33 player log); v1 101 / E128CB1D (A-31, render-only change since).

## NEXT
- Slope before/after: still identical in A-33 (seg 87 and seg 9, mid-slope, -camrel). The push-out only acts on
  a step, so a player standing still never differs. It needs a walk: add a shot flag that walks the player down
  the slope (like -autowalk for climbs), or -goto the steep top point and compare after a fall.
- OgreCamp: -watch Ogre:Merchant did not frame (log: target -119 m up, anchor 2433 m). Check the species name
  and why the folk read far off; then retake merchant/ogre baker.
- Retakes: woodcutter from the other side (-watchfrom), human smith while working.
- Close-camera fade before shot; fords (0 found).
