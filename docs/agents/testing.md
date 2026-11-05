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
- To get a picture: `Shots.Take("name")` writes `Assets/Temp/shots/name-yyyyMMdd-HHmmss.png`. It fails the test
  unless the file exists, is newer than the call, and differs from the last shot of that name (a stale or frozen
  frame). Assets/Temp is never committed.
- Keep the whole suite under about 5 minutes.
