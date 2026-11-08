"""Job 28 D: the far shadow cache redraw's cost per frame, from the runner's -benchframes CSVs.

FrameTiming lags a few frames, so each timing (its frameStartTimestamp) is matched to the frame that began then: frame f
began at the end-of-frame stamp of frame f-1. Each matched frame then has its own main thread, render thread and GPU ms.
Frames that drew a cache slice are compared with the frames within 15 of them that didn't (the same stretch of the walk,
so streaming and the view are about the same), and with all other frames.
    python q56frames.py <stop.csv>...   (e.g. results/q56frames/enter-*/towncross.csv)
"""
import csv
import statistics
import sys

def load(path):
    rows = list(csv.DictReader(open(path)))
    for r in rows:
        for k in ("frame", "slice", "step", "redraws", "qpc", "timingstart"):
            r[k] = int(r[k])
        for k in ("ms", "main", "render", "gpu"):
            r[k] = float(r[k])
    # A row's "ms" (unscaledDeltaTime) is the frame before's length; a frame's own is the gap between its end-of-frame
    # stamp and the previous one, in ms by the QPC ticks per ms over the whole file.
    if len(rows) > 2:
        per_ms = (rows[-1]["qpc"] - rows[0]["qpc"]) / sum(r["ms"] for r in rows[1:])
        for i in range(len(rows) - 1, 0, -1):
            rows[i]["ms"] = (rows[i]["qpc"] - rows[i - 1]["qpc"]) / per_ms
        rows = rows[1:]
    # Match timings to frames. The timing clock ticks with QPC but from another base, so the stamps can't be compared
    # directly; instead the gaps between successive timings' frameStartTimestamp (the timed frames' own lengths) are
    # lined up with the frames' lengths: the lag with the best correlation wins. The first row's timing is stale (from
    # before measuring) and is skipped.
    gaps = [(rows[i + 1]["timingstart"] - rows[i]["timingstart"]) / per_ms for i in range(1, len(rows) - 1)]
    lens = [r["ms"] for r in rows]
    best, lag = -2.0, 0
    for k in range(0, 8):
        a = [gaps[i] for i in range(len(gaps)) if 0 <= i + 1 - k < len(lens)]
        b = [lens[i + 1 - k] for i in range(len(gaps)) if 0 <= i + 1 - k < len(lens)]
        if len(a) > 10:
            c = statistics.correlation(a, b)
            if c > best:
                best, lag = c, k
    timed = {}
    for i in range(1, len(rows)):
        f = i - lag   # row i's timing (whose start is row i's frame start) is frame i - lag's
        if 0 <= f < len(rows):
            timed[f] = rows[i]
    lags = [lag, round(best, 3)]
    return rows, timed, lags

def mean(xs):
    return statistics.mean(xs) if xs else float("nan")

def main():
    allslice = {"ms": [], "main": [], "render": [], "gpu": []}
    allnear = {"ms": [], "main": [], "render": [], "gpu": []}
    allother = {"ms": [], "main": [], "render": [], "gpu": []}
    for path in sys.argv[1:]:
        rows, timed, lags = load(path)
        slices = [i for i, r in enumerate(rows) if r["slice"]]
        near = set()
        for i in slices:
            near.update(k for k in range(i - 15, i + 16) if 0 <= k < len(rows) and not rows[k]["slice"])
        redraws = rows[-1]["redraws"] - rows[0]["redraws"] if rows else 0
        print(f"{path}: {len(rows)} frames, {len(slices)} slice frames, {redraws} redraws finished, "
              f"{len(timed)} timed (lag {lags[0]} frames, correlation {lags[1]})")
        for i, r in enumerate(rows):
            group = allslice if r["slice"] else allnear if i in near else allother
            group["ms"].append(r["ms"])
            if i in timed:
                for k in ("main", "render", "gpu"):
                    group[k].append(timed[i][k])
    print(f"{'frames':>18} {'n':>6} {'frame ms':>9} {'main ms':>8} {'render ms':>10} {'gpu ms':>7}")
    for name, g in (("cache slice", allslice), ("within 15, none", allnear), ("all others", allother)):
        print(f"{name:>18} {len(g['ms']):6d} {mean(g['ms']):9.2f} {mean(g['main']):8.2f} {mean(g['render']):10.2f} {mean(g['gpu']):7.2f}")

main()
