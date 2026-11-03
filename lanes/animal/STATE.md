# Animal lane: state (2026-11-02, after batch A-31)

Worktree `Punchy` (animal lane only). On `animal-behaviours` = origin 6d12e17 (A-31), on top of f50f7a0 (A-30).
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
- Counts: v1 has 5 bridges, 0 fords, 5 spots, 3 fishers (all lone). v2 has 3 bridges, 0 fords, 3 spots and
  0 rolled, so v2 has no fisher.
- `-place crossing` parks the camera on the first bridge with a fisher.

## Talk bubble (A-31)
- FolkTalkSystem caches a bubble line only once it is non-null. RoleLine falls back to a plain archive line for
  jobless talkers. The log shows role lines, but no shot has caught the bubble yet (see NEXT).

## Cave collision and camera (A-30)
- PlayerMovementSystem.PushOutOfSolids: knee/middle/head spheres scaled to size, 4 passes, steep floor slide,
  ceiling undo, gated on caves. SolidDistance is the landmark hook.
- ThirdPersonCamera.CaveArm: sphere trace, ease out, pivot drop, near plane 0.1 / 0.05.
- Left: seg 87 (55 deg) spikes up to 0.20 m; size 0.45 wedges at about 0.05 on seg 246 (65 deg).
- World card (open): TerrainSource.CaveDistance overstates the distance by up to 1.8x below a passage's axis.

## Test flags
- Shot lists: -place (crossing, ...), -goto x,y,z, -view yaw,pitch, -nopushout, -time, -weather, -nohud,
  -chatshot, -wait, -shot. The player takes -island v1.
- Hashes: v2 125 / D709D95C, v1 101 / E128CB1D (both checked in A-31).

## NEXT
- A bubble shot: the chatshot frames talkers but the bubble did not show. Check whether -nohud hides the bubble
  or the shot comes before the line.
- Slope before/after: framing now shows the body, but the slope reads poorly. Pull the view back and up.
- seg 87 spikes and the small-body wedge (A-30 row 6, not reached).
- Fords: 0 found on either island. Check whether the bake ever leaves a water gap, or whether roads always bridge.
- Avatar fade when the camera is very close; job work poses and props.
