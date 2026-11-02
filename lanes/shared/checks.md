# Checking a feature in a controlled scene

Check each feature with nothing around that could hide it or be mistaken for it. Write the check as numbers where
you can; take shots only for things that have to be seen.

## Set up the stage
- Clear weather and a fixed time: `-weather Clear -time 12` (or the time the feature needs), `-weatherseed 4`.
- Hide the HUD and debug panel: `-nohud`, F1 panel off (`DebugControls.ShowReadout = false`).
- Keep the avatar out of the frame and out of the camera's line (the reveal cone cuts holes). Use `-camlook` or
  `-camrel`; a side view is `-goto x,z` plus `-camlook`.
- Remove what competes with the subject: other residents, plants and particles not under test. Stand in an open
  spot (lane TESTING.md lists known clear spots) or spawn just the subject (`-spawn <Species>:1`).
- Before/after: same camera, same time, same coats/seed. Light dark scenes.

## Run it in one go
- Put every view in one shot list and run it in one player session (`-shotlist`), not one launch per view.
- Log numbers from the game (probe flags, `Debug.Log` summary lines) and read the log once at the end.
- Wait for the run's own "done" line; don't poll in short sleeps.

## Shots
- 16:9, at most 3 per report, 800 px on the long edge (a sheet counts as one, 1200 px max). Frame close (3-8 m) or
  crop 1:1 for fine detail. Never reuse a shot across reports.
- Full-size extras go in `PunchyMilestones/<lane>/<feature>/gallery/` for Zach, unreviewed.
