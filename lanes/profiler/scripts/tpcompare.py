#!/usr/bin/env python3
"""Job 50: compares the host's and clients' TPCHECK lines stop by stop (TwoPlayerRoute.cs.txt).
usage: tpcompare.py <logdir>   (host.log, client1.log, client2.log)

For each host stop, the client lines taken while that client stood within 60 m of the host's stop position are paired
with the host's lines at that stop. Per stop: positions (each side's view of the other player), creature counts and
the ghost ids of the nearest five on each side, the hour and the weather, and the exceptions so far."""
import os
import re
import sys

PAT = re.compile(r'TPCHECK (\w+): t (\d+) stop (\S+) \| me ([-\d.]+),([-\d.]+),([-\d.]+)(.*)')


def parse(path):
    out = []
    if not os.path.exists(path):
        return out
    for line in open(path, encoding='utf-8', errors='replace'):
        m = PAT.search(line)
        if not m:
            continue
        rest = m.group(7)
        rec = {'role': m.group(1), 't': int(m.group(2)), 'stop': m.group(3),
               'me': tuple(float(m.group(i)) for i in (4, 5, 6)), 'raw': line.strip()}
        rec['players'] = [tuple(float(v) for v in p) for p in re.findall(r'player#\S+ ([-\d.]+),([-\d.]+),([-\d.]+)', rest)]
        rec['look'] = re.findall(r'species (-?\d+) coat (-?\d+)', rest)
        c = re.search(r'creatures (\d+) \(of (\d+)\)', rest)
        rec['creatures'] = int(c.group(1)) if c else None
        rec['ids'] = set(int(x) for x in re.findall(r'\[(-?\d+) s', rest))
        h = re.search(r'hour ([\d.]+)', rest)
        rec['hour'] = float(h.group(1)) if h else None
        w = re.search(r'precip ([\d.]+)', rest)
        rec['precip'] = float(w.group(1)) if w else None
        e = re.search(r'exceptions (\d+) errors (\d+)', rest)
        rec['exc'] = (int(e.group(1)), int(e.group(2))) if e else (0, 0)
        out.append(rec)
    return out


def dist(a, b):
    return ((a[0] - b[0]) ** 2 + (a[2] - b[2]) ** 2) ** 0.5


def main():
    d = sys.argv[1]
    host = parse(os.path.join(d, 'host.log'))
    clients = {n: parse(os.path.join(d, n + '.log')) for n in ('client1', 'client2')}
    stops = []
    for r in host:
        if not stops or stops[-1][0] != r['stop']:
            stops.append((r['stop'], []))
        stops[-1][1].append(r)
    for name, recs in stops:
        if name == 'start' and len(stops) > 1 and recs is stops[0][1]:
            pass
        pos = recs[-1]['me']
        print(f'\n## stop {name} (host at {pos[0]:.0f},{pos[2]:.0f}; {len(recs)} host checks)')
        for cn, crecs in clients.items():
            near = [c for c in crecs if dist(c['me'], pos) < 60]
            if not near:
                continue
            hlast, clast = recs[-1], near[-1]
            other_h = hlast['players'][0] if hlast['players'] else None
            other_c = clast['players'][0] if clast['players'] else None
            print(f'- {cn}: {len(near)} checks near the host')
            print(f"  - host sees the client at {fmt(other_h)}; the client is at {fmt(clast['me'])}"
                  f" (gap {dist(other_h, clast['me']):.1f} m)" if other_h else '  - host sees no other player')
            print(f"  - client sees the host at {fmt(other_c)}; the host is at {fmt(hlast['me'])}"
                  f" (gap {dist(other_c, hlast['me']):.1f} m)" if other_c else '  - client sees no other player')
            print(f"  - creatures within 120 m: host {hlast['creatures']}, client {clast['creatures']}; nearest ids host "
                  f"{sorted(hlast['ids'])} client {sorted(clast['ids'])} (shared {len(hlast['ids'] & clast['ids'])})")
            print(f"  - hour host {hlast['hour']} client {clast['hour']}; precipitation host {hlast['precip']} client {clast['precip']}")
            print(f"  - looks: host sees {hlast['look']}, client sees {clast['look']}")
            print(f"  - exceptions/errors so far: host {hlast['exc']}, client {clast['exc']}")
    for n, recs in [('host', host)] + list(clients.items()):
        if recs:
            print(f"\n{n}: last check {recs[-1]['exc'][0]} exceptions, {recs[-1]['exc'][1]} errors")


def fmt(p):
    return '—' if p is None else f'{p[0]:.1f},{p[2]:.1f}'


if __name__ == '__main__':
    main()
