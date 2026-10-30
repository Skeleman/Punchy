"""Zip a Punchy player build for a GitHub release.
Usage: python release/pack.py <build folder> <version, e.g. v0.10.0> <output folder>
Forward-slash paths; leaves out *_BurstDebugInformation_DoNotShip; prints the file count, size and SHA-256."""
import sys, os, zipfile, hashlib
src, ver, outdir = sys.argv[1], sys.argv[2], sys.argv[3]
out = os.path.join(outdir, 'Punchy-%s-windows.zip' % ver)
n = 0
with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as z:
    for root, dirs, files in os.walk(src):
        dirs[:] = [d for d in dirs if 'DoNotShip' not in d]
        for f in files:
            p = os.path.join(root, f)
            z.write(p, os.path.relpath(p, src).replace(os.sep, '/')); n += 1
h = hashlib.sha256(open(out, 'rb').read()).hexdigest()
print(n, 'files', os.path.getsize(out) // 1048576, 'MB', h)
with zipfile.ZipFile(out) as z:
    names = z.namelist()
    print('backslash', any('\\' in x for x in names), 'donotship', any('DoNotShip' in x for x in names),
          'exe', 'Punchy.exe' in names)
