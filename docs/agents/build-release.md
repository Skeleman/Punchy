# Building, testing builds and releasing

Read before building a player, running benches or preparing a release.

- Builds go to `Builds/Windows/Punchy.exe` (git-ignored); build with `BuildPipeline.BuildPlayer` on the scenes in
  Build Settings for `StandaloneWindows64`.
- Releases are GitHub releases with the zipped build attached (leave out
  `Punchy_BurstDebugInformation_DoNotShip`). The repo is public, so anyone can see and download them.
- After a build, Unity rewrites some settings files (URP shader prefiltering, preloaded input assets); check
  `git status` before committing so unrelated changes don't ride along.

## Learned in October 2026 (from the lane handoffs)

- Test players don't take `-screen-fullscreen/-screen-width/-screen-height` unless you then delete the
  non-"Default" `Screenmanager*` values under `HKCU\Software\DefaultCompany\Punchy`: Unity saves them as
  the player's own preference.
- Launch test builds only from the tree's fixed bench path. Windows keys firewall rules by exe path, so an exe
  started from a new folder raises a firewall prompt that stalls every later run until someone answers it.
- Benches: a smoke run can say NOT CLEAN from round order alone; an ABBA run decides.
