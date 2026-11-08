# GPU time per pass from RenderDoc captures (Job 31 addendum). Run inside RenderDoc's own Python, before its UI opens:
#   RD_CAPS="a.rdc;b.rdc" RD_OUT=out.txt qrenderdoc.exe --python rdpasses.py
# For each capture: every event's GPU duration (GPUCounter.EventGPUDuration, D3D11 timestamp queries: the numbers the
# Event Browser shows), summed under each marker path (Unity's debug markers: the camera, URP passes such as
# MainLightShadow, DrawOpaqueObjects, ...), with the draw count under each, to RD_MAXDEPTH levels (default 4). Draws are
# also split into indirect (FloraSystem's DrawIndexedInstancedIndirect) and other, as names alone can't tell terrain
# from flora. Exits without opening the UI.
import os
import collections
import renderdoc as rd

caps = [c for c in os.environ.get("RD_CAPS", "").split(";") if c]
out = open(os.environ.get("RD_OUT", "rdpasses.txt"), "w")
maxdepth = int(os.environ.get("RD_MAXDEPTH", "4"))


def log(line=""):
    out.write(line + "\n")
    out.flush()


def walk(actions, path, sf, dur, totals, draws, indirect, depth, each):
    for a in actions:
        name = a.GetName(sf)
        if a.children:
            walk(a.children, path + [name], sf, dur, totals, draws, indirect, depth, each)
            continue
        ms = dur.get(a.eventId, 0.0) * 1000.0
        if a.flags & rd.ActionFlags.Drawcall:
            each.append((ms, a.eventId, a.numIndices, a.numInstances, name, " / ".join(path[-3:])))
        is_draw = bool(a.flags & rd.ActionFlags.Drawcall)
        ind = bool(a.flags & rd.ActionFlags.Indirect)
        for d in range(0, min(len(path), maxdepth) + 1):
            key = " / ".join(path[:d]) if d else "(frame)"
            totals[key] += ms
            if is_draw:
                draws[key] += 1
                if ind:
                    indirect[key] += 1
        if not path:
            totals["(no marker) " + name.split("(")[0]] += ms


def main():
    for path in caps:
        log("== " + os.path.basename(path))
        cap = rd.OpenCaptureFile()
        if cap.OpenFile(path, "", None) != rd.ResultCode.Succeeded:
            log("  could not open")
            continue
        status, ctrl = cap.OpenCapture(rd.ReplayOptions(), None)
        if status != rd.ResultCode.Succeeded:
            log("  could not replay: " + str(status))
            cap.Shutdown()
            continue
        # Several fetches: the timings vary between replays; the median of 3 per event.
        runs = [ctrl.FetchCounters([rd.GPUCounter.EventGPUDuration]) for _ in range(3)]
        per = collections.defaultdict(list)
        for r in runs:
            for c in r:
                per[c.eventId].append(c.value.d)
        dur = {e: sorted(v)[len(v) // 2] for e, v in per.items()}
        totals = collections.defaultdict(float)
        draws = collections.Counter()
        indirect = collections.Counter()
        each = []
        walk(ctrl.GetRootActions(), [], ctrl.GetStructuredFile(), dur, totals, draws, indirect, 0, each)
        for key in sorted(totals, key=lambda k: (k.count(" / "), -totals[k])):
            if totals[key] >= 0.05 or draws[key]:
                log("  %8.3f ms  %5d draws (%4d indirect)  %s" % (totals[key], draws[key], indirect[key], key))
        # The most expensive single draws (RD_TOPDRAWS, default 15): event, indices, instances (indirect args are in the
        # name), and the last markers above it, to tell e.g. the grass draws apart.
        log("  -- top draws")
        for ms, eid, ni, inst, name, where in sorted(each, reverse=True)[: int(os.environ.get("RD_TOPDRAWS", "15"))]:
            log("  %8.3f ms  event %6d  %8d idx x %6d inst  %s  [%s]" % (ms, eid, ni, inst, name[:80], where))
        ctrl.Shutdown()
        cap.Shutdown()


# Any error goes into the output and the process always exits: an exception left qrenderdoc open (its UI) until
# the caller's timeout. (First-run prompts block --python too: RenderDoc's UI.config needs Analytics_TotalOptOut and
# Tips_HasSeenFirst set.)
try:
    main()
except Exception:
    import traceback
    log(traceback.format_exc())
finally:
    out.close()
    os._exit(0)
