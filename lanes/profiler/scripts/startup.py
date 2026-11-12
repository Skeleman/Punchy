#!/usr/bin/env python3
"""Job 64: the start-up time line, settle time and hitch causes from q104's logs and analyses.
usage: startup.py <results dir> <job tag, e.g. q104>

Per start (spawn, town, forest):
- the time line of every launch from the game's own lines (s since process start): island ready (cache hit or not),
  streamed world ready, player in the world (BenchRoute), and the settled stop's median frame;
- for the profiled launches (hit 1, fresh): when frame time settles (the first 2 s window whose median is within 10%
  of the median of the capture's last 30 s), the frames over twice that median in the first 60 s, and what they spent:
  each frame's biggest self sample that isn't waiting, grouped by name and by kind (draw/cull, build/upload, shader
  compile, GC, other), to say whether tiered culling would help.
"""
import csv
import os
import re
import statistics
import sys
from collections import defaultdict

WAITS = ('Idle', 'WaitForJobGroupID', 'Semaphore.WaitForSignal', 'DXGI.WaitOnSwapChain', 'WaitForLastPresentation',
         'Gfx.WaitFor', 'WaitForTargetFPS', 'Profiler.Flush')
KINDS = [
    # (Named systems first: what a hitch frame was spent in, by the kind of work tiered culling could or couldn't cut.)
    ('flora cull/draw', ('FloraSystem.LateUpdate', 'FloraCull', 'FloraSystem.Draw')),
    ('creature render/pose', ('CreatureRenderSystem', 'PoseJob')),
    ('shader compile', ('CreateGPUProgram', 'Shader.', 'ShaderCompil', 'CompileShader', 'WarmupShader', 'Pipeline state')),
    ('GC', ('GC.Collect', 'GC.Alloc', 'GarbageCollect')),
    ('draw/cull', ('Cull', 'SRPBatcher', 'RenderLoop', 'DrawIndexed', 'Camera.Render', 'Batch', 'Draw ', '.Draw', 'Render.')),
    ('build/upload', ('Build', 'Mesh', 'Upload', 'Bake', 'Place', 'Spawn', 'Snapshot', 'Stream', 'Chunk', 'Apply',
                      'Instantiate', 'Load', 'Texture', 'Integrate', 'Awake', 'Scene')),
]


def kind(name):
    for k, keys in KINDS:
        if any(key in name for key in keys):
            return k
    return 'other'


def marks(log):
    t = open(log, encoding='utf-8', errors='replace').read()
    def find(pat):
        m = re.search(pat, t)
        return float(m.group(1)) if m else None
    cache = re.search(r'\[island\] cache (\w+)', t)
    here = re.search(r'BenchRoute: here: avg .*?median ([\d.]+) ms.*?gpu ([\d.]+) ms', t)
    return {
        'cache': cache.group(1) if cache else '?',
        'island': find(r'\[island\].*?ready at ([\d.]+) s'),
        'stream': find(r'\[stream\] world ready at ([\d.]+) s'),
        'player': find(r'BenchRoute: world ready at ([\d.]+) s'),
        'median': float(here.group(1)) if here else None,
        'gpu': float(here.group(2)) if here else None,
    }


def settle(frames_csv):
    rows = [(float(r['t']), float(r['ms'])) for r in csv.DictReader(open(frames_csv))]
    if not rows:
        return None, None, []
    end = rows[-1][0]
    ref = statistics.median([ms for t, ms in rows if t >= end - 30]) if end > 40 else statistics.median(ms for _, ms in rows)
    t = 0.0
    while t + 2 <= end:
        win = [ms for tt, ms in rows if t <= tt < t + 2]
        if win and abs(statistics.median(win) - ref) <= 0.1 * ref:
            return ref, t, rows
        t += 0.5
    return ref, None, rows


GENERIC = re.compile(r'PlayerLoop|SystemGroup|UpdateFunction|ScriptRunBehaviour|BehaviourUpdate|TimeUpdate|PostLateUpdate|'
                     r'PreLateUpdate|^Update\.|Initialization|FixedUpdate|EarlyUpdate|PreUpdate|JobHandle\.Complete|WaitFor|'
                     r'^Idle|Semaphore|DXGI|Gfx\.|Profiler\.')


def hitches(analysis, limit_ms, until_s=60.0):
    """(time, frame ms, (ms, name)) per frame over limit_ms in the first until_s s: the name is the frame's biggest
    inclusive sample that isn't a player-loop group or a wait (the system or script the frame was spent in)."""
    out = []
    for line in open(analysis, encoding='utf-8', errors='replace'):
        m = re.match(r'OVER frame (\d+) at ([\d.]+) s: ([\d.]+) ms \| self: (.*?) \| incl: (.*)', line)
        if not m:
            continue
        t, ms = float(m.group(2)), float(m.group(3))
        if t > until_s or ms < limit_ms:
            continue
        top = None
        for part in m.group(5).split(' | ')[0].split('; '):
            mm = re.match(r'([\d.]+) (.*?)( < |$)', part)
            if not mm:
                continue
            name = mm.group(2).replace('Default World ', '')
            if GENERIC.search(name):
                continue
            top = (float(mm.group(1)), name)
            break
        out.append((t, ms, top))
    return out


def main():
    rdir, q = sys.argv[1], sys.argv[2]
    logs = os.path.join(rdir, q + 'logs')
    for s in ('spawn', 'town', 'forest'):
        print(f'\n## start: {s}')
        print('| launch | island cache | island ready | streamed world ready | player in | settled (s) | settled median frame | GPU |')
        print('|---|---|---|---|---|---|---|---|')
        for n, r in (('hit', 1), ('hit', 2), ('hit', 3), ('fresh', 1)):
            log = os.path.join(logs, f'q-{q}-{s}-{n}-cs100-{r}.log')
            if not os.path.exists(log):
                continue
            mk = marks(log)
            st = '—'
            fr = os.path.join(rdir, f'{q}-{s}-{n}-cs100-{r}-frames.csv')
            if os.path.exists(fr):
                ref, t, _ = settle(fr)
                st = f'{t:.1f} (capture time)' if t is not None else 'never'
            print(f"| {n} {r} | {mk['cache']} | {mk['island']} | {mk['stream']} | {mk['player']} | {st} | {mk['median']} | {mk['gpu']} |")
        for n in ('hit', 'fresh'):
            an = os.path.join(rdir, f'{q}-{s}-{n}-cs100-1.txt')
            fr = os.path.join(rdir, f'{q}-{s}-{n}-cs100-1-frames.csv')
            if not (os.path.exists(an) and os.path.exists(fr)):
                continue
            ref, t, rows = settle(fr)
            hs = hitches(an, 2 * ref)
            by_name, by_kind = defaultdict(lambda: [0, 0.0]), defaultdict(lambda: [0, 0.0])
            for _, ms, top in hs:
                name = top[1] if top else '(waiting only)'
                by_name[name][0] += 1
                by_name[name][1] += ms
                by_kind[kind(name)][0] += 1
                by_kind[kind(name)][1] += ms
            print(f'\n{n} 1: settled median {ref:.1f} ms; {len(hs)} frames over {2 * ref:.0f} ms in the first 60 s of the capture')
            for name, (c, ms) in sorted(by_name.items(), key=lambda x: -x[1][1])[:5]:
                print(f'- {name}: {c} frames, {ms:.0f} ms')
            print('- by kind: ' + '; '.join(f'{k} {c} frames {ms:.0f} ms' for k, (c, ms) in sorted(by_kind.items(), key=lambda x: -x[1][1])))


if __name__ == '__main__':
    main()
