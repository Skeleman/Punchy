# For chosen events in a RenderDoc capture: the vertex and pixel shaders bound there, by name, so a costly draw can be
# matched to its Unity shader. Run in RenderDoc's own Python:
#   RD_CAP=x.rdc RD_EVENTS="21975,24792" RD_OUT=out.txt qrenderdoc.exe --python rdevents.py
import os
import renderdoc as rd
out = open(os.environ.get("RD_OUT", "rdevents.txt"), "w")
rd.InitialiseReplay(rd.GlobalEnvironment(), [])
cap = rd.OpenCaptureFile()
cap.OpenFile(os.environ["RD_CAP"], "", None)
res, ctrl = cap.OpenCapture(rd.ReplayOptions(), None)
names = {r.resourceId: r.name for r in ctrl.GetResources()}
for e in [int(x) for x in os.environ["RD_EVENTS"].split(",") if x]:
    ctrl.SetFrameEvent(e, True)
    st = ctrl.GetPipelineState()
    vs = names.get(st.GetShader(rd.ShaderStage.Vertex), "?")
    ps = names.get(st.GetShader(rd.ShaderStage.Pixel), "?")
    out.write(f"event {e}: vs {vs} | ps {ps}\n")
    out.flush()
out.close()
os._exit(0)
