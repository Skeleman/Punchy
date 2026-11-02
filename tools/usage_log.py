"""Write this machine's Claude Code token use for Punchy sessions to COSTS/<machine>.csv.

Reads the local transcripts (~/.claude/projects/**/*.jsonl), keeps sessions that mention "Punchy" anywhere,
and writes one row per session per day. No model call is involved, so it costs no tokens to run.
The file is rewritten in full each run, so running it twice is harmless.

    python tools/usage_log.py --machine laptop

Dollar figures are Claude Opus 5.5 API-rate equivalents (input 4, cache read 0.20, cache write 5 (5 min) or
8 (1 h), output 20 per million tokens), for comparing roles and weeks, not a bill. The repo is public: rows hold
only dates, short session ids, session titles and counts.
"""
import argparse, collections, csv, glob, json, os, pathlib

PRICE = {"opus": (4, .2, 5, 8, 20), "sonnet": (2, .2, 2.5, 4, 10), "haiku": (.1, .01, .125, .2, .5)}
FIELDS = ["date", "machine", "session", "title", "model", "subagent", "calls", "input", "cache_read",
          "cache_write", "output", "peak_context", "usd_equiv"]


def rates(model):
    for k, v in PRICE.items():
        if k in (model or ""):
            return v
    return PRICE["opus"]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--machine", required=True)
    ap.add_argument("--match", default="punchy", help="keep sessions whose transcript mentions this (any case)")
    ap.add_argument("--out", default=str(pathlib.Path(__file__).resolve().parent.parent / "COSTS"))
    a = ap.parse_args()

    rows = {}
    for path in glob.glob(os.path.expanduser("~/.claude/projects/**/*.jsonl"), recursive=True):
        session = os.path.basename(path)[:8]
        sub = "subagents" in path.replace("\\", "/")
        title, keep, seen = "", False, {}
        for line in open(path, encoding="utf-8", errors="replace"):
            try:
                d = json.loads(line)
            except ValueError:
                continue
            if not keep and a.match in line.lower():  # any session that worked on Punchy, whatever its cwd
                keep = True
            if d.get("type") == "custom-title":
                title = d.get("customTitle") or title
            m = d.get("message") if d.get("type") == "assistant" else None
            if m and m.get("usage") and m.get("model") != "<synthetic>":
                seen[m.get("id") or d.get("uuid")] = (d.get("timestamp", "")[:10], m.get("model"), m["usage"])
        if not keep:
            continue
        for day, model, u in seen.values():
            k = (day, session, model, sub)
            r = rows.setdefault(k, collections.Counter())
            w1 = (u.get("cache_creation") or {}).get("ephemeral_1h_input_tokens", 0)
            cw = u.get("cache_creation_input_tokens", 0)
            i, cr, o = u.get("input_tokens", 0), u.get("cache_read_input_tokens", 0), u.get("output_tokens", 0)
            p = rates(model)
            r["calls"] += 1
            r["input"] += i
            r["cache_read"] += cr
            r["cache_write"] += cw
            r["output"] += o
            r["peak_context"] = max(r["peak_context"], i + cr + cw)
            r["usd"] += (i * p[0] + cr * p[1] + (cw - w1) * p[2] + w1 * p[3] + o * p[4]) / 1e6
            r.title = title  # type: ignore[attr-defined]

    out = pathlib.Path(a.out)
    out.mkdir(exist_ok=True)
    f = out / f"{a.machine}.csv"
    with open(f, "w", newline="", encoding="utf-8") as fh:
        w = csv.writer(fh)
        w.writerow(FIELDS)
        for (day, session, model, sub), r in sorted(rows.items()):
            w.writerow([day, a.machine, session, getattr(r, "title", ""), model, int(sub), r["calls"], r["input"],
                        r["cache_read"], r["cache_write"], r["output"], r["peak_context"], f"{r['usd']:.2f}"])
    total = sum(r["usd"] for r in rows.values())
    print(f"{f}: {len(rows)} rows, ${total:,.0f} equivalent")


if __name__ == "__main__":
    main()
