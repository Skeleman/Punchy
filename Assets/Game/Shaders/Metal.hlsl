// W-166 row 3: the metal look now lives in Materials/Metal.hlsl. This forwards to it so Creature.shader (fauna's)
// keeps compiling unchanged until fauna points its include there; then delete this file.
#include "Materials/Metal.hlsl"
