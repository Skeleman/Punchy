# Play-mode tests

A small play-mode test suite (`Assets/Tests/PlayMode`) that loads the game scene once and checks the things that
quietly regress: the landmark hash, the fauna fingerprint, river bends, critter and creature density, and folk talk.
The whole run takes about 3 minutes.

## Run it before a merge

1. Close any editor that has this project open (batch mode can't open a project another editor holds). Follow
   EDITOR-QUEUE and the RAM rule as for any editor.
2. `scripts/run-playmode-tests.sh` (Git Bash). Pass a test or class name to run part of it:
   `scripts/run-playmode-tests.sh FolkTalkTests`.
3. It prints one PASS/FAIL line per test, the first line of each failure, and every `measured:` value the tests
   logged. Exit code 0 means every test passed. Anything else means a failure, an error, or no results.

The script finds Unity from `$UNITY_EXE`, then the Hub's install folder for the project's editor version. Set
`UNITY_EXE` if it can't find it. The full log and the NUnit XML go to `Logs/playmode-tests.log` and
`Logs/playmode-results.xml`. They go in Logs/, not Temp/, because Unity empties Temp/ when it quits.

## The asmdef choice

All game code lives in Assembly-CSharp, which has no asmdef. Test code needs an asmdef, and an asmdef can't reference
Assembly-CSharp. So the tests live in their own assembly, `Punchy.Tests.PlayMode`, which is test-only (it only
compiles with `UNITY_INCLUDE_TESTS`, so it never ships in a player build). The tests reach the game through reflection
in `Game.cs`, and only through two doors:

- `Punchy.Debugging.TestHooks` (editor-only static methods that return plain types: bools, ints, floats, strings,
  Vector3). Add a hook here when a test needs to read something new.
- `DebugControls.RunOptions(line)`, which applies test flags (`-place`, `-time`, `-weather`, `-goto`, `-spawn`...) in
  the middle of a run, the same as `Temp/PlayArgs.txt` does at start.

We rejected moving the game into asmdefs (a large refactor with merge pain in every lane). We also rejected turning on
`playModeTestRunnerEnabled`, which would put the tests into player builds.

## Add a test

- Put it in a class in `Assets/Tests/PlayMode` and start the class with
  `[UnitySetUp] public IEnumerator Up() => TestSceneSetup.Load();` and
  `[OneTimeTearDown] public void Down() => TestSceneSetup.Restore();`. The scene loads once per run (island v3, noon,
  clear sky; the seed is the one authored in the subscene), so every extra test costs only its own seconds.
- Set the state you need with `TestSceneSetup`: `Place(name)`, `GoTo(x, z)`, `TimeOfDay(hour)`, `Weather(rain)`,
  `Spawn(species, x, z)`, and `Seconds(s)` to wait.
- Read the game with `Game.Hook<T>("HookName", args...)`. Read log lines with `LogWatch.Last("text")`.
- Log the value you measured with `LogWatch.Measure("...")`, so it shows up as a `measured:` line in the run output.
- Every assert needs a target taken from code, an issue or REPORTS. Name the source in the test's summary comment.
  If no target exists, measure today's value, set a tolerance around it, and write "measured on main <hash>" in the
  comment.
- To get a picture, use the shot tool (see "How to take shots" below). Shot tests stay `[Explicit]`.
- Keep the whole suite under about 5 minutes.

## How to take shots

One tool (`ShotTool`, T-1) takes every shot: it waits until the terrain, flora and kit around the camera are in and
the camera has held still, renders that frame, then writes a check line. Use it instead of `Shots.Take`, `cam.Render()`
in an eval, or a screen capture.

- In a test, posed by coordinates: `Game.RunOptions("-camlook x,y,z,tx,ty,tz");` then `yield return Shots.Settled("name");`
- In a test, framed by subject: `Game.RunOptions("-time 12 -weather Clear -nohud -frame flora:Saguaro@x,z -framing mid");`,
  wait for the `[frame]` log line, then `yield return Shots.Settled("name");`. Subjects: `x,z`, `#12` (a landmark),
  `CaveEntrance` (a landmark type), `colossus`, `flora:<species or building>`, `clearing`. Framings: `close`, `mid`,
  `aerial`, `ground`. Add `-from water` for a village seen from the water.
- Many shots in one session (editor or player build): `-shotlist shots.txt -shotdir <folder>`, one line per shot, such as
  `-goto x,z -time 18.2 -weather Clear -nohud -frame clearing -framing ground -shot clearing-dusk`.
- A run's contact sheet from a test: `Game.Hook<bool>("ShotRunStart")` first, then
  `Game.Hook<string[]>("ShotRunSheet", path, labels)` at the end. A `-shotlist` writes `contact-sheet.jpg` by itself.
- Full-res copies for people: set the `PUNCHY_GALLERY` environment variable to a folder before the run. Never commit
  that path.

Every shot logs one line: `[shot] 'name' ready after ... check: mean 0.56, near-black 0.00, near-white 0.00, sky 0.00,
target on-screen and clear; good`. A shot that is black, blown out, taken from under the ground, all sky (framed
shots only), or whose subject is off-screen or hidden gets one retry at another heading, then is kept and marked
`BAD <reason>`. Night and caves may be dark. Look only at the contact sheet (1280 wide, under 300 KB, red bars mark
BAD shots); leave the full-res files for people.

Shot checklist line for cards (paste it into a card's shot row):
`Shots: T-1 tool (Shots.Settled, -frame or -shotlist); quote each [shot] check line; look only at the contact sheet;
full res to PUNCHY_GALLERY; any BAD shot is retaken or reported with its reason.`
