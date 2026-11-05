#!/usr/bin/env bash
# Runs the play-mode test suite (Assets/Tests/PlayMode) in batch mode and prints a compact pass/fail list.
# Exit code: 0 when every test passed, 1 otherwise (a failure, an error, or no results at all).
#
# Usage: scripts/run-playmode-tests.sh [test filter]   (filter: a test or class name, e.g. FolkTalkTests)
# The Unity editor comes from $UNITY_EXE, else the Hub's default install of the project's editor version.
# Close any editor that has this project open first: batch mode can't open a project another editor holds.
set -u

project="$(cd "$(dirname "$0")/.." && pwd)"
# (Unity on Windows reads C:/... paths, not Git Bash's /c/...: a bad -testResults path silently goes elsewhere.)
if command -v cygpath >/dev/null 2>&1; then project="$(cygpath -m "$project")"; fi
version="$(sed -n 's/^m_EditorVersion: *//p' "$project/ProjectSettings/ProjectVersion.txt" | tr -d '\r')"

unity="${UNITY_EXE:-}"
if [ -z "$unity" ]; then
  # (The Hub's own install folder when it was moved: "Installs location" in Hub settings.)
  hubDir=""
  if [ -n "${APPDATA:-}" ] && [ -f "$APPDATA/UnityHub/secondaryInstallPath.json" ]; then
    hubDir="$(tr -d '"\r\n' < "$APPDATA/UnityHub/secondaryInstallPath.json" | sed 's#\\\\#/#g; s#\\#/#g')"
  fi
  for candidate in \
    ${hubDir:+"$hubDir/$version/Editor/Unity.exe"} \
    "/c/Program Files/Unity/Hub/Editor/$version/Editor/Unity.exe" \
    "/Applications/Unity/Hub/Editor/$version/Unity.app/Contents/MacOS/Unity" \
    "$HOME/Unity/Hub/Editor/$version/Editor/Unity"; do
    if [ -x "$candidate" ] || [ -f "$candidate" ]; then unity="$candidate"; break; fi
  done
fi
if [ -z "$unity" ]; then
  echo "Unity $version not found: set UNITY_EXE to the editor executable." >&2
  exit 1
fi

# (Logs/, not Temp/: Unity empties Temp/ when it quits, results file included. Logs/ is git-ignored.)
results="$project/Logs/playmode-results.xml"
log="$project/Logs/playmode-tests.log"
rm -f "$results"
mkdir -p "$project/Logs"

args=(-batchmode -projectPath "$project" -runTests -testPlatform PlayMode -testResults "$results" -logFile "$log")
if [ $# -gt 0 ]; then args+=(-testFilter "$1"); fi

echo "Unity $version: running play-mode tests (log: Logs/playmode-tests.log)..."
start=$(date +%s)
"$unity" "${args[@]}"
code=$?
elapsed=$(( $(date +%s) - start ))

if [ ! -f "$results" ]; then
  echo "No results written (Unity exit $code). Is an editor open on this project? See Logs/playmode-tests.log:" >&2
  grep -E "error|Error|Exception" "$log" | grep -v "^UnityEngine" | head -20 >&2
  exit 1
fi

# One line per test case: PASS/FAIL, name, seconds; then the first line of each failure's message.
passed=0; failed=0
while IFS= read -r line; do
  name=$(printf '%s' "$line" | sed -E 's/.* fullname="([^"]*)".*/\1/')
  result=$(printf '%s' "$line" | sed -E 's/.* result="([^"]*)".*/\1/')
  seconds=$(printf '%s' "$line" | sed -E 's/.* duration="([0-9]+)(\.[0-9])?[0-9]*".*/\1\2/')
  name=${name#Punchy.Tests.}
  if [ "$result" = "Passed" ]; then
    passed=$((passed + 1)); printf '  PASS  %-48s %ss\n' "$name" "$seconds"
  else
    failed=$((failed + 1)); printf '  FAIL  %-48s %ss  (%s)\n' "$name" "$seconds" "$result"
  fi
done < <(grep -o '<test-case [^>]*>' "$results")

# Failure messages (CDATA inside <message>), first line each.
grep -A1 '<message>' "$results" | grep -o 'CDATA\[[^]]*' | grep -v 'One or more child tests' | sed 's/^CDATA\[/        /' | head -20

# The values tests measured along the way ("[measure]" lines).
grep -h '^\[measure\]' "$log" | sed 's/^\[measure\]/  measured:/' | sort -u

echo "$passed passed, $failed failed in ${elapsed}s (Unity exit $code)."
if [ "$failed" -gt 0 ] || [ "$passed" -eq 0 ] || [ "$code" -ne 0 ]; then exit 1; fi
exit 0
