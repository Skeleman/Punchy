# Animal lane: state (2026-11-02, after batch A-30)

Worktree `Punchy` (animal lane only). On `animal-behaviours` = origin f50f7a0 (A-30: bd26880 push-out + camera,
f50f7a0 test flags), on top of de2e8e0 (A-29b). Untracked `Assets/_Recovery/` is old and not ours. The editor is on
port 7800 and may still be in Play mode from the A-30 probe.

## Cave collision (A-30)
- PlayerMovementSystem.PushOutOfSolids runs when in or near a cave. Three body spheres (knee, middle, head) take
  the body's size: r = clamp(0.35*size, 0.1, 1). Up to 4 push passes, capped at r a frame; the body re-floors
  after each pass when grounded. A floor steeper than SteepFloor (0.64) slides you back downhill. A head pushed
  further into the ceiling than last frame undoes the move. Open ground, gaits and swimming are untouched (gated
  on inCave/caveHere).
- SolidDistance is the hook for landmarks: math.min another signed distance (the tower, the W-32 temple) into it.
  KeepOutOfTower is unchanged.
- Probe (v2, walks along and +-35 deg into walls, sizes 0.45/1/1.6), max penetration, old -> new:
  seg 246 (65 deg) size 1 0.456 -> 0.035, size 1.6 0.600 -> 0.039; seg 9 0.344 -> 0; chamber 0.139 -> 0.
  Left over: seg 87 (55 deg) brief spikes up to 0.20 (6 of ~624 frames); size 0.45 wedges at about 0.05 on top
  of seg 246.
- Causes: the radius did not scale with size; the ground probe was one point; one wall test at floor+1 m and no
  ceiling test; steps only blocked over 0.6 m, so steep slopes stayed climbable.

## Camera (A-30)
- ThirdPersonCamera.CaveArm sphere-traces SolidDistance at half-steps (lens 0.3, margin 0.05) out from the head.
  It pulls in at once and eases back out at 3 m/s. The pivot drops under low ceilings. When pulled in the near
  plane goes to 0.1, or 0.05 inside 1.2 m. Over 10368 probe views, view-in-rock went 589 -> 12 and lens-in-rock
  239 -> 25.
- Avatar fade when very close is not done: no hook in the body renderer yet.

## For the world lane (card)
- TerrainSource.CaveDistance can overstate the distance by up to 1.8x below a passage's axis. That is why the
  camera traces at half-steps and the push-out needs several passes. Ask for a true lower bound (Lipschitz-1).

## Open
- Slope shots (A-30) did not frame the body: `-camlook` landed on dark rock. Retake them with `-view`.
- Talk bubble shows "..." in a29-talk-healer-line.png; not looked at.
- v1 hash not run for A-30 (generation untouched). v2 build 125 / D709D95C.
- Bubble on the tall elder (A-28) still optional; two-minute talk gaps need Zach's OK to change.

## Testing recipe
- Editor evals: use the Bash tool with `unity command --port 7800 eval "$(cat Temp/x.cs)"` (PowerShell mangles
  quotes). No `using` lines: fully qualify names. Enter Play with EditorApplication.EnterPlaymode(); "Default
  World" holds the v2 caves. The probe is Temp/a30_probe2.cs and writes Temp/a30_probe.txt.
- New shot flags: `-goto x,y,z` (stand on the cave floor there), `-view yaw,pitch`, `-nopushout` (old behaviour,
  for before shots). The player needs `-shotdir` or the list never advances.
- Build shots: shotlist lines like `-place HumanVillage -time 10 -weather Clear -watch Human:Smith -wait 20`;
  `-quit` ends after the list.
