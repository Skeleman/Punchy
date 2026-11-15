"""Does a build carry the profiler's runner (b/bench-stress)? Looks for its switches in Assembly-CSharp.dll's strings
(UTF-16, as .NET stores literals). Exit 0 if all are there. Usage: python runnercheck.py <build folder>"""
import sys, os
dll = os.path.join(sys.argv[1], 'Punchy_Data', 'Managed', 'Assembly-CSharp.dll')
data = open(dll, 'rb').read()
need = ['-benchline', '-benchface', '-benchcycle', '-renderdocstop']
missing = [s for s in need if s.encode('utf-16-le') not in data]
print('runner ok' if not missing else 'runner missing ' + ' '.join(missing))
sys.exit(1 if missing else 0)
