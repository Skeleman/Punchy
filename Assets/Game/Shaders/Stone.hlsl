// R-34 + W-166 row 3: the shared stone now lives in Materials/Stone.hlsl, with the built stone families (cut and
// rubble). This forwards to it so TerrainSurface.shader (render's) and Cave.shader keep compiling unchanged until each
// points its include there; then delete this file.
#include "Materials/Stone.hlsl"
