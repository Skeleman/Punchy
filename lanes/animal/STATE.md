# Animal lane: state (2026-11-02, after batch A-32)

Worktree `Punchy` (animal lane only). On `animal-behaviours` = origin cbdf5d1 (A-32), on top of 6d12e17 (A-31).
Untracked `Assets/_Recovery/` is old and not ours. Editor on port 7800.

## Crossing fishers (A-31)
- Fauna/RoadCrossings.cs: read-only over the settlement bake. A bridge is a run of consecutive Bridge plots
  (under 14 m apart, same facing). A ford is a 1-60 m gap between consecutive road streets with water at least
  0.05 deep (roads skip water stretches). Crossings within 25 m merge.
- Bank spot: walk along the road to dry ground (up to 70 m), then try 4-10 m to either side, -4..6 m back: dry,
  outside towns (+5 m), flat (0.8 m over 2 m), water at least 0.2 deep within 5 m (gives Face).
- Roll: Scramble(hash of the centre to the metre) % 3 == 0, so it is seed-derived and the same on every machine.
  Refresh() rebuilds when the settlement key changes; Version counts the rebuilds.
- FaunaDirector.SpawnCrossingFishers: by day (5.5-19.5 h) with a player within 120 m. Job Fisher, task Fish, on
  the spot facing the water. Home is the nearest village if its edge is within 250 m, else lone (Leash 8); at
  night a lone fisher walks off up the road. Race comes from the nearest settlement's free-folk residents, else
  Folk. Existing creature/ghost path, no new network fields.
- Fallback (A-32): if an island rolls no fisher, the spot crossing with the lowest Roll gets one (Fallback=true;
  log says "best score"). v1: 3 rolled, no fallback. v2: 0 rolled, fallback at -2544.3,884.6.
- `-place crossing` parks the camera on the first bridge with a fisher.

## Talk bubble (A-31)
- FolkTalkSystem caches a bubble line only once it is non-null. RoleLine falls back to a plain archive line.
- Why A-31 showed none: -nohud sets InteractionPrompt.Hidden, which hid bubbles too. -chatshot now sets
  FolkTalkLabels.BubblesWithoutHud (bubbles draw, name labels stay hidden). a32-talk-line.png shows a role line.

## Cave collision and camera (A-30)
- PlayerMovementSystem.PushOutOfSolids: knee/middle/head spheres scaled to size, 4 passes, steep floor slide,
  ceiling undo, gated on caves. SolidDistance is the landmark hook. A-32: shift cap 2r per frame; the ceiling
  undo needs rock overhead (SolidNormal y < -0.3), which unwedged size 0.45 on seg 246 (progress 0.23 -> 0.74).
- ThirdPersonCamera.CaveArm: sphere trace, ease out, pivot drop, near plane 0.1 / 0.05.
- Probe after A-32: seg 87 and 246 penetration over 5 cm only in frames 0-2 (probe starts in rock); none after.
- CreatureRenderSystem: bodies fade when the camera is within ~0.6 m (fully by 0.35 m). a32-close-cam.png.
- World card (open): TerrainSource.CaveDistance overstates the distance by up to 1.8x below a passage's axis.

## Test flags
- Shot lists: -place (crossing, ...), -goto x,y,z, -view yaw,pitch, -nopushout, -time, -weather, -nohud,
  -chatshot, -wait, -shot. The player takes -island v1.
- Hashes: v2 125 / D709D95C, v1 101 / E128CB1D (both checked in A-31).

## NEXT
- Slope before/after: -goto 3044.1,17.1,-2141.2 is a cave pool, not the slope; both pairs look the same. Use a
  mid-slope spot: seg 9 (3047.5,22.1,-2142.1 -> 3040.7,12.2,-2140.4) or seg 87 (-3993.8,54.4,812.7 ->
  -3989.4,44.3,807.3), standing on the floor halfway, side-on -camrel.
- Close-camera fade: no before shot; a probe or a side-by-side with the fade off would prove it.
- Fords: 0 found on either island. Check whether roads always bridge.
- Job work poses and props.
