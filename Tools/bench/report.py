#!/usr/bin/env python3
"""Per-arm tables for a bench job's CSVs (lib.sh runcsv), in the shape the profiler's reports use.

    python report.py results/q89 --ref off            # every results/q89-*.csv; arms compared with "off" per view
    python report.py results/q95 --ref main --bar 0.3 # and a pass line against a bar (ms GPU over the reference)

A CSV named <job>-<view>-<arm>.csv holds one arm at one view (the q-scripts' naming); a -benchcycle CSV holds every arm,
as stops named <stop>/<arm>. A round with a frame over --stall ms (default 5000: a swapchain stall) is shown in brackets
and left out of the means. Prints a markdown table per view: GPU by round, mean GPU, mean median frame, mean CPU, mean
1% low (fps), and the difference in mean GPU from the reference arm.
"""
import argparse
import csv
import glob
import os
import statistics
from collections import defaultdict


def load(prefix, stall):
    rows = defaultdict(list)   # (view, arm) -> [row]
    base = os.path.basename(prefix)
    for path in sorted(glob.glob(prefix + '-*.csv')):
        name = os.path.basename(path)[len(base) + 1:-4]
        for r in csv.DictReader(open(path, newline='')):
            stop = r['stop']
            if '/' in stop:                       # a -benchcycle segment: the arm is in the stop
                view, arm = name, stop.split('/', 1)[1]
            elif '-' in name:                     # <view>-<arm>
                view, arm = name.rsplit('-', 1)
            else:
                view, arm = name, stop
            try:
                rec = {k: float(r[k]) if r[k] not in ('', None) else None
                       for k in ('gpu', 'cpu', 'median', 'low1', 'worst', 'slow')}
            except ValueError:
                continue
            rec['round'] = r['round']
            rec['stalled'] = rec['worst'] is not None and rec['worst'] > stall
            rows[(view, arm)].append(rec)
    return rows


def mean(values):
    values = [v for v in values if v is not None]
    return statistics.mean(values) if values else None


def fmt(v, digits=2):
    return '—' if v is None else f'{v:.{digits}f}'.rstrip('0').rstrip('.')


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('prefix', help='results/<job> (reads <job>-*.csv)')
    ap.add_argument('--ref', help='the reference arm to compare with, per view')
    ap.add_argument('--bar', type=float, help='pass line: mean GPU over the reference at most this (ms)')
    ap.add_argument('--stall', type=float, default=5000.0, help='a round with a frame over this (ms) is left out')
    a = ap.parse_args()
    rows = load(a.prefix, a.stall)
    if not rows:
        raise SystemExit(f'no CSVs at {a.prefix}-*.csv')
    views = sorted({v for v, _ in rows})
    for view in views:
        arms = sorted(arm for v, arm in rows if v == view)
        if a.ref in arms:
            arms.remove(a.ref)
            arms.insert(0, a.ref)
        print(f'\n### {view}')
        print('| arm | GPU ms by round | mean GPU | median frame | CPU | 1% low (fps) | slow frames | vs ' + (a.ref or '—') + ' |')
        print('|---|---|---|---|---|---|---|---|')
        means = {}
        for arm in arms:
            recs = rows[(view, arm)]
            ok = [r for r in recs if not r['stalled']]
            by_round = ', '.join(f"({fmt(r['gpu'], 1)})" if r['stalled'] else fmt(r['gpu'], 1) for r in recs)
            g = mean(r['gpu'] for r in ok)
            means[arm] = g
            delta = ''
            if a.ref and arm != a.ref and g is not None and means.get(a.ref) is not None:
                d = g - means[a.ref]
                delta = f'{d:+.2f}'
                if a.bar is not None:
                    delta += ' PASS' if d <= a.bar else ' FAIL'
            print(f"| {arm} | {by_round} | **{fmt(g)}** | {fmt(mean(r['median'] for r in ok))} | {fmt(mean(r['cpu'] for r in ok))} | "
                  f"{fmt(mean(r['low1'] for r in ok), 1)} | {fmt(mean(r['slow'] for r in ok), 1)} | {delta} |")
        stalled = sum(r['stalled'] for arm in arms for r in rows[(view, arm)])
        if stalled:
            print(f'\n({stalled} round(s) in brackets had a frame over {a.stall:.0f} ms and are left out of the means.)')


if __name__ == '__main__':
    main()
