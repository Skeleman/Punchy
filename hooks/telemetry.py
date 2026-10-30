"""Commit telemetry for Punchy (J-041). Run by the post-commit and post-merge hooks; never fails the commit.

Appends one row per commit to ~/.punchy-telemetry/commits.csv (outside every repo tree):
when, machine, tree, branch, commit, files/lines changed, and, when the commit was made from a Claude Code
session, that session's model and the tokens it used since its previous commit, plus its running total.

Tokens come from the session's transcript (~/.claude/projects/*/<CLAUDE_CODE_SESSION_ID>.jsonl). Each API
response is counted once (by message id). The numbers are what the transcript records, so treat them as
approximate: subagents and work in other trees between commits are counted against the next commit made from
this session.
"""
import csv, glob, json, os, platform, subprocess, sys, datetime

OUT_DIR = os.path.join(os.path.expanduser("~"), ".punchy-telemetry")
CSV_PATH = os.path.join(OUT_DIR, "commits.csv")
STATE_DIR = os.path.join(OUT_DIR, "sessions")
FIELDS = ["time", "machine", "tree", "branch", "commit", "parents", "files", "insertions", "deletions",
          "session", "model", "in", "cache_write", "cache_read", "out",
          "total_in", "total_cache_write", "total_cache_read", "total_out", "subject"]
USAGE_KEYS = [("input_tokens", "in"), ("cache_creation_input_tokens", "cache_write"),
              ("cache_read_input_tokens", "cache_read"), ("output_tokens", "out")]


def git(*args):
    return subprocess.run(["git", *args], capture_output=True, text=True, encoding="utf-8",
                          errors="replace").stdout.strip()


def session_tokens(session):
    """Tokens the session used since this function last ran for it, and its running totals."""
    delta = {k: 0 for _, k in USAGE_KEYS}
    paths = glob.glob(os.path.join(os.path.expanduser("~"), ".claude", "projects", "*", session + ".jsonl"))
    if not paths:
        return None, delta, None
    path = paths[0]
    os.makedirs(STATE_DIR, exist_ok=True)
    state_path = os.path.join(STATE_DIR, session + ".json")
    try:
        with open(state_path, encoding="utf-8") as f:
            state = json.load(f)
    except (OSError, ValueError):
        state = {"offset": 0, "last_id": "", "totals": {k: 0 for _, k in USAGE_KEYS}}
    model = state.get("model")
    seen = {state.get("last_id", "")}
    with open(path, "rb") as f:
        if os.path.getsize(path) < state["offset"]:
            state["offset"] = 0   # transcript was rewritten; start over
        f.seek(state["offset"])
        data = f.read()
    end = data.rfind(b"\n") + 1   # only whole lines; a half-written last line waits for next time
    for line in data[:end].splitlines():
        if b'"usage"' not in line:
            continue
        try:
            msg = json.loads(line).get("message") or {}
        except ValueError:
            continue
        usage, mid = msg.get("usage"), msg.get("id", "")
        if not usage or mid in seen:
            continue
        seen.add(mid)
        state["last_id"] = mid
        model = msg.get("model") or model
        for src, k in USAGE_KEYS:
            delta[k] += int(usage.get(src) or 0)
    state["offset"] += end
    for k in delta:
        state["totals"][k] = state["totals"].get(k, 0) + delta[k]
    state["model"] = model
    with open(state_path, "w", encoding="utf-8") as f:
        json.dump(state, f)
    return model, delta, state["totals"]


def main():
    commit = git("rev-parse", "HEAD")
    stat = git("show", "--shortstat", "--format=", "--first-parent", "-m", commit).splitlines()
    files = ins = dels = 0
    for part in (stat[0].split(",") if stat else []):
        n = int(part.strip().split()[0])
        if "file" in part: files = n
        elif "insertion" in part: ins = n
        elif "deletion" in part: dels = n
    session = os.environ.get("CLAUDE_CODE_SESSION_ID", "")
    model, delta, totals = session_tokens(session) if session else (None, {}, None)
    row = {
        "time": datetime.datetime.now().astimezone().isoformat(timespec="seconds"),
        "machine": platform.node(),
        "tree": git("rev-parse", "--show-toplevel"),
        "branch": git("rev-parse", "--abbrev-ref", "HEAD"),
        "commit": commit[:10],
        "parents": len(git("rev-list", "--parents", "-n1", commit).split()) - 1,
        "files": files, "insertions": ins, "deletions": dels,
        "session": session, "model": model or "",
        "subject": git("log", "-1", "--format=%s", commit)[:120],
    }
    row.update(delta or {})
    for k, v in (totals or {}).items():
        row["total_" + k] = v
    os.makedirs(OUT_DIR, exist_ok=True)
    new = not os.path.exists(CSV_PATH)
    with open(CSV_PATH, "a", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=FIELDS, extrasaction="ignore")
        if new:
            w.writeheader()
        w.writerow(row)


if __name__ == "__main__":
    try:
        main()
    except Exception as e:   # telemetry must never break a commit
        print("punchy telemetry skipped: %s" % e, file=sys.stderr)
