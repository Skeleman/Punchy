# World lane: state (2026-11-02, after W-02b)

Worktree `Punchy-world` (main PC), branch `world-features`. Backlog: `gh issue list -l lane:world`.
Released: v0.11.0 = world b39522b + animal c810406.
Pushed head: 3728ef9 (W-02 code ec53f8a, quit-on-done fe2dda9, moon animation 3728ef9). Built to Builds\Dev, checked.
No Punchy.exe of mine runs. The one running (animal tree path) is not ours.

## Done and seen in W-02b
- #34 moon: blue lit sphere, wavy tendrils that drift (cycle ~20-40 s), slow low shimmer. Needs `-bluemoondays 0`
  in the launch args for the shot (else the normal moon shows). Frame: -goto -255,27 -time 21
  -camlook -255,40,27,-184,108,6.
- #44 dunes puffs: haze at the Dunes cave mouth (`-place Dunes`); `-nosandpuffs` for compare. GPU ms NOT measured:
  -benchroute lives on unmerged b/bench-runner.
- #30 flower bend: meadow (-255,27) camlook -258,36.0,27,-250,35.9,27, Clear vs Storm: upright, no stretch. Oak fine.
- #4 `-menu Graphics` works launched alone (-nohud off effect not needed).
- Shot-list players Application.Quit at the end (was editor-only); batch build:
  `unity build . --target StandaloneWindows64 -o Builds/Dev/Punchy.exe -l Logs/x.log --no-tail` (no editor needed).
- Landmark hash unchanged (no placement code touched).

## Gotchas
- `-place prop:Daisy` finds nothing: ground-scatter flowers are not props. Use a meadow spot with a low camlook.
- Launch-wide flags (-nosandpuffs, -menu, -bluemoondays) mean one player per variant; run them one at a time.
- Assets/Resources (Unity test-runner json) and Assets/Temp stay untracked on purpose.

## Branches still unmerged
- `b/bench-runner` db3c1f2: `-benchday`, -benchroute. wip/ui-menu, wip/ui-wheel, world-laptop/z34-pool are inside world-features.

## Open, not started
- #45 shooting stars much rarer and thinner (sky code). #15 look checks owed (Z-30 posts, Z-10 fog over non-marsh river).
- #13 Z-35 fall speed (Zach in play). #14 cave-mouth shards (needs a spot). Design-note P3: #17, #18-#22. Roadmap #23, #24.
