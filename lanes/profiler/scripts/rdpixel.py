# Pixel history of one pixel in a RenderDoc capture (R-18: the NaN bloom dot). Run inside RenderDoc's Python:
#   RD_CAPS="a.rdc" RD_OUT=out.txt RD_X=1060 RD_Y=224 qrenderdoc.exe --python rdpixel.py
# For each colour target written by a draw under DrawOpaqueObjects / DrawTransparentObjects (the scene's HDR target),
# lists every event that touched the pixel: the event, its marker path, the shader's output and the value after it,
# and flags the first non-finite (NaN or infinite) value. Exits without opening the UI.
import math
import os
import renderdoc as rd

caps = [c for c in os.environ.get("RD_CAPS", "").split(";") if c]
out = open(os.environ.get("RD_OUT", "rdpixel.txt"), "w")
X, Y = int(os.environ.get("RD_X", "0")), int(os.environ.get("RD_Y", "0"))


def log(line=""):
    out.write(line + "\n")
    out.flush()


def flat(actions, path, sf, acc):
    for a in actions:
        name = a.GetName(sf)
        if a.children:
            flat(a.children, path + [name], sf, acc)
        else:
            acc.append((a, name, " / ".join(path[-3:])))


def bad(v):
    return any(math.isnan(c) or math.isinf(c) for c in v)


def main():
    for path in caps:
        log("== " + os.path.basename(path) + " pixel (%d, %d)" % (X, Y))
        cap = rd.OpenCaptureFile()
        if cap.OpenFile(path, "", None) != rd.ResultCode.Succeeded:
            log("  could not open")
            continue
        status, ctrl = cap.OpenCapture(rd.ReplayOptions(), None)
        if status != rd.ResultCode.Succeeded:
            log("  could not replay: " + str(status))
            cap.Shutdown()
            continue
        sf = ctrl.GetStructuredFile()
        acts = []
        flat(ctrl.GetRootActions(), [], sf, acts)
        names = {a.eventId: (n, w) for a, n, w in acts}
        # The colour targets the scene's draws write (the first draw of each pass that names one).
        targets = []
        for a, n, w in acts:
            if not (a.flags & rd.ActionFlags.Drawcall):
                continue
            if "DrawOpaqueObjects" not in w and "DrawTransparentObjects" not in w and "Skybox" not in w:
                continue
            for o in a.outputs:
                if o != rd.ResourceId.Null() and o not in targets:
                    targets.append(o)
        log("  scene colour targets: %d" % len(targets))
        for t in targets:
            desc = None
            for tex in ctrl.GetTextures():
                if tex.resourceId == t:
                    desc = tex
            if desc is None or X >= desc.width or Y >= desc.height:
                continue
            log("  -- target %s %dx%d %s" % (str(t), desc.width, desc.height, str(desc.format.Name())))
            hist = ctrl.PixelHistory(t, X, Y, rd.Subresource(0, 0, 0), rd.CompType.Typeless)
            first_bad = None
            for m in hist:
                n, w = names.get(m.eventId, ("?", "?"))
                so = m.shaderOut.col.floatValue
                pm = m.postMod.col.floatValue
                flag = ""
                if bad(so) or bad(pm):
                    flag = "  <== NON-FINITE"
                    if first_bad is None:
                        first_bad = m.eventId
                log("    event %6d  out (%.3g %.3g %.3g %.3g)  after (%.3g %.3g %.3g %.3g)%s  %s  [%s]%s" % (
                    m.eventId, so[0], so[1], so[2], so[3], pm[0], pm[1], pm[2], pm[3],
                    "" if m.Passed() else " (failed: depth/stencil/etc.)", n[:70], w, flag))
            log("  first non-finite: %s" % (first_bad if first_bad is not None else "none in this target"))
        ctrl.Shutdown()
        cap.Shutdown()


try:
    main()
except Exception:
    import traceback
    log(traceback.format_exc())
finally:
    out.close()
    os._exit(0)
