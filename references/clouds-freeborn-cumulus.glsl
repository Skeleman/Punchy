// Reference for R-35 (render lane). Pasted by Zach 11-14. MIT, see the notice below: adapted code must keep
// the copyright line and MIT notice in its file header.

uniform vec2 u_resolution; //Pass resolution in pixels

//[RES] 480p

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 @freeborn
//[LICENSE] https://opensource.org/licenses/MIT

// Puffy cumulus field: Shadertoy-style GLSL prototype (target: XorDev's reference photo #2).
//
// Rendered in one channel; colour is a single palette mix measured from the photo:
//   colour = mix(.14*vec3(1,2,3), vec3(1), s)   (6.6/255 RMSE against the reference)
//
// Shape: one cumulus tower per jittered 2D cell, wide at the base and narrowing upward, so
// neighbours merge into a bank with turrets; big and medium cauliflower lobes from
// hemispherical caps sqrt(1 - F1^2/R^2) around Worley feature points; fine lumps from
// billowed gradient noise (|n| octaves: rounded bumps, creases at zero crossings) pushing
// the surface in and out, faded out below ~2 px and drifting upward so the detail boils.
// Volume: density fades in over SOFT units inside that surface: translucent edges, but thin
// enough that the fine lumps stay crisp and shade each other.
// Light: sun shadow march (first steps at full detail) with multiple-scattering octaves,
// sky ambient from a short upward occlusion march, dual-lobe phase for the silver lining,
// exponential distance haze toward the sky colour, then a highlight-compressing tonemap
// 1 - exp(-1.4 x) (keeps haze at the photo's s = 0.5).

precision highp int;   // default mediump int can be 16-bit on mobile GPUs, which breaks the hash

#define CELL 4.0
#define TOP 10.5
#define BASE -8.0
#define SIGMA 12.0      // extinction at full density
#define SOFT 0.15       // depth over which density fades in below the surface
#define FOG 0.025
#define MAX_OCT 3.0     // Worley lobe octaves (big/medium); fine detail comes from BUMP noise
#define BUMP 0.9        // fine billow lumps: surface push std ~0.12, 99th pct ~0.3 outward

// Cap height / cap radius per lobe octave (before the 0.6x island fade): soft big lobes,
// crisp medium ones, flat fine ones.
const float RELIEF[6] = float[6](1.0, 1.3, 1.0, 0.75, 0.6, 0.5);
const mat3 ROT = mat3(0.00, 0.80, 0.60, -0.80, 0.36, -0.48, -0.60, -0.48, 0.64);

const vec3 SUN = vec3(-0.2, 0.45, 1.0);

// PCG3D (Jarzynski & Olano 2020): one round, three outputs.
vec3 hash33(ivec3 c) {
    uvec3 v = uvec3(c) * 1664525u + 1013904223u;
    v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
    v ^= v >> 16u;
    v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
    return vec3(v >> 8u) / 16777215.0;
}

// Gradient (Perlin-style) noise, roughly [-1, 1], quintic fade.
float grad(ivec3 c, vec3 r) {
    return dot(hash33(c) * 2.0 - 1.0, r);
}

float gnoise(vec3 p) {
    ivec3 i = ivec3(floor(p));
    vec3 f = fract(p);
    vec3 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
    return mix(mix(mix(grad(i, f), grad(i + ivec3(1, 0, 0), f - vec3(1, 0, 0)), u.x),
                   mix(grad(i + ivec3(0, 1, 0), f - vec3(0, 1, 0)), grad(i + ivec3(1, 1, 0), f - vec3(1, 1, 0)), u.x), u.y),
               mix(mix(grad(i + ivec3(0, 0, 1), f - vec3(0, 0, 1)), grad(i + ivec3(1, 0, 1), f - vec3(1, 0, 1)), u.x),
                   mix(grad(i + ivec3(0, 1, 1), f - vec3(0, 1, 1)), grad(i + ivec3(1, 1, 1), f - vec3(1, 1, 1)), u.x), u.y), u.z);
}

float smin(float a, float b, float k) {
    float h = max(k - abs(a - b), 0.0) / k;
    return min(a, b) - h * h * k * 0.25;
}

float smax(float a, float b, float k) {
    return -smin(-a, -b, k);
}

// Squared distance to the nearest jittered feature point (Worley F1^2), exact whenever it is
// below 0.36, which is all the lobe caps use: feature points sit in [0.15, 0.85] of their
// cell, so only this cell and the nearer neighbour on each axis can hold one within 0.6.
float worley2(vec3 p) {
    ivec3 i = ivec3(floor(p));
    vec3 f = fract(p);
    ivec3 n = ivec3(step(0.5, f)) * 2 - 1;
    float m = 9.0;
    for (int k = 0; k < 8; k++) {
        ivec3 o = ivec3(k & 1, (k >> 1) & 1, k >> 2) * n;
        vec3 r = vec3(o) + 0.15 + 0.7 * hash33(i + o) - f;
        m = min(m, dot(r, r));
    }
    return m;
}

// Coarse shape: one tower per jittered 2D cell, wide at the base and narrowing upward.
float heaps(vec3 p) {
    ivec2 cell = ivec2(floor(p.xz / CELL));
    float d = 1e3;   // no tower in the 3x3 block (the march has its own empty-space bound)
    for (int j = -1; j <= 1; j++)
    for (int i = -1; i <= 1; i++) {
        ivec3 c = ivec3(cell.x + i, 0, cell.y + j);
        vec3 h = hash33(c);
        // Rows by depth, like the photo: clear flight corridor, near row, haze gap, middle row,
        // then a thinner far field. The camera flies along x, so the rows never pop.
        int cz = c.z;
        float cov = cz < 2 ? 0.0 : cz < 5 ? 0.45 : cz < 7 ? 0.08 : cz < 11 ? 0.4 : 0.3;
        if (h.z >= cov) continue;
        vec3 g = hash33(c + ivec3(0, 1, 0));
        float r = CELL * (0.24 + 0.16 * g.x);
        // Measured from the photo: near-row tops sit ~13 deg below the camera, middle-row
        // tops ~3 deg above it, so the middle row is the taller one.
        float top = cz < 5 ? 1.5 + 2.0 * g.y : cz < 11 ? 4.5 + 3.0 * g.y : 1.5 + 3.5 * g.y;
        vec2 ctr = (vec2(c.xz) + 0.3 + 0.4 * h.xy) * CELL;
        vec3 q = p - vec3(ctr.x, 0.0, ctr.y);
        // Wobbling column from BASE up to 'top': radius 0.65r at the rounded top, widening
        // to 1.85r six units below it.
        float y = clamp(q.y, BASE, top);
        vec2 wob = vec2(sin(y * 0.7 + 6.28 * g.z), cos(y * 0.6 + 6.28 * h.x)) * 0.35 * r;
        float rad = r * (0.65 + 1.2 * smoothstep(top, top - 6.0, y));
        float tower = (length(q - vec3(wob.x, y, wob.y)) - rad) * 0.7;
        d = smin(d, tower, 0.2 * CELL);
    }
    return d;
}

// Add cauliflower lobes (at most 1.45 outward) to the tower distance d. lod = number of
// octaves; the last one fades in fractionally so detail never pops. Each cap shrinks with
// how far p already is outside the surface, so caps only grow out of it, never as islands.
float lobes(vec3 p, float d, float lod) {
    vec3 q = p;
    float fr = 0.5;
    float fine = 0.35 + 0.65 * smoothstep(-5.0, 3.0, p.y);   // smoother, broader lower bodies
    for (int o = 0; o < 6; o++) {
        float wgt = clamp(lod - float(o), 0.0, 1.0);
        if (wgt <= 0.0) break;
        float x = max(1.0 - worley2(q * fr) / 0.36, 0.0);
        float h = 0.6 / fr * RELIEF[o] * (o == 0 ? 1.0 : fine) * wgt;
        d -= h * x * inversesqrt(x + 0.04) * clamp(1.0 - d / (1.5 * h), 0.0, 1.0);
        q = ROT * q + 3.1;
        fr *= 2.1;
    }
    return d;
}

// Towers plus lobes.
float cloud(vec3 p, float lod) {
    float d = heaps(p);
    return d > 2.0 ? d - 1.9 : lobes(p, d, lod);
}

// Soft volume density from the lobed surface distance.
float puff(float d) {
    return smoothstep(0.0, SOFT, -d);
}

// Density for lighting lookups with a ramp of width w. Coarse lookups use wide ramps so
// the light varies smoothly instead of printing sharp lobe rims into the shadows.
float puffL(float d, float w) {
    return smoothstep(-0.3 * w, w, -d);
}

// Billowed gradient noise for 'oct' octaves (last one fading in), first period ~0.33 units.
// Returns an outward surface push: measured n has mean 0.28, std 0.13, 99th pct 0.64.
float bumps(vec3 p, float oct) {
    p = p * 3.0 + vec3(0.0, -0.1 * u_time, 0.0);
    float n = 0.0, a = 1.0;
    for (int o = 0; o < 4; o++) {
        float w = clamp(oct - float(o), 0.0, 1.0);
        if (w <= 0.0) break;
        n += a * w * abs(gnoise(p));
        p = ROT * p * 2.03;
        a *= 0.5;
    }
    return BUMP * (n - 0.28);
}

// Optical depth toward the sun. The first short step uses more detail so lobes shadow each
// other; longer steps only need the bigger shapes.
float sunDepth(vec3 p, vec3 L, float lod) {
    float od = 0.0, s = 0.08;
    for (int k = 0; k < 6; k++) {
        p += L * s;
        float l = k == 0 ? min(lod, 4.0) : k < 3 ? 2.0 : 1.0;
        float d = cloud(p, l);
        if (k < 2 && d < 0.45) d -= bumps(p, 2.0);   // fine lumps shade each other
        od += puffL(d, SOFT * (1.0 + 2.0 * float(k))) * s;
        s *= 1.6;
    }
    return od;
}

// Optical depth straight up: how much sky the point can see. Coarse shape and a soft ramp,
// so overhangs give a broad gradient instead of a sharp projected silhouette.
float skyDepth(vec3 p) {
    return puffL(cloud(p + vec3(0.0, 0.5, 0.0), 2.0), 0.8) * 0.6
         + puffL(cloud(p + vec3(0.0, 1.5, 0.0), 1.0), 0.8) * 1.0;
}

// Henyey-Greenstein phase, scaled by 4*pi so isotropic = 1.
float hg(float c, float g) {
    return (1.0 - g * g) / pow(1.0 + g * g - 2.0 * g * c, 1.5);
}

// Background in palette units: haze below the horizon, brighter sky toward the sun.
float sky(vec3 rd, vec3 L) {
    float c = max(dot(rd, L), 0.0);
    return 0.5 + 0.15 * smoothstep(-0.05, 0.3, rd.y) + 0.2 * pow(c, 6.0) + 0.15 * pow(c, 40.0);
}

void main() {
  vec2 fragCoord = gl_FragCoord.xy;

    vec2 uv = (fragCoord - 0.5 * u_resolution.xy) / u_resolution.y;
    vec3 L = normalize(SUN);
    vec3 ro = vec3(u_time * 0.8, 6.0, 0.0);
    float pitch = -0.28;
    vec3 fw = vec3(0.0, sin(pitch), cos(pitch));
    vec3 up = cross(fw, vec3(1.0, 0.0, 0.0));
    vec3 rd = normalize(uv.x * vec3(1.0, 0.0, 0.0) + uv.y * up + 1.3 * fw);

    float ph = mix(hg(dot(rd, L), -0.2), hg(dot(rd, L), 0.6), 0.3);
    float haze = sky(rd, L);
    float tEnd = min(120.0, rd.y > 0.0 ? (TOP - ro.y) / rd.y : (BASE - ro.y) / rd.y);
    float t = 0.0, T = 1.0, S = 0.0;
    if (ro.y > TOP) t = (TOP - ro.y) / rd.y;
    float jit = fract(52.9829189 * fract(dot(fragCoord, vec2(0.06711056, 0.00583715))));
    float px = 1.0 / (u_resolution.y * 1.3);   // pixel footprint per unit of distance
    for (int i = 0; i < 500 && t < tEnd && T > 0.02; i++) {
        float dt = 0.03 + 0.003 * t;   // under SOFT out to the haze, so the ramp doesn't band
        vec3 p = ro + rd * (t + jit * dt);
        float hd = heaps(p);
        if (hd > 2.0) {
            // No lobe from the 3x3 block can be within 0.1. A tower plus lobes reaches at
            // most 2.32 + 1.52 outside its own cell, so anything farther is >= edge + 0.1 away.
            vec2 f = mod(p.xz, CELL);
            float edge = min(min(f.x, CELL - f.x), min(f.y, CELL - f.y));
            t += max(min(hd - 1.9, edge + CELL - 3.9), dt);
            continue;
        }
        // Octaves 3+ add at most 0.14 outward, so two octaves bound the distance cheaply
        // while crossing the lobe shell.
        float dc = lobes(p, hd, 2.0) - 0.25;
        if (dc > 0.0) {
            t += max(dc * 0.5, dt);
            continue;
        }
        // Octave count from footprint: octave o has lobe radius 1.2/2.1^o; keep those >= 2 px.
        float lod = clamp(log2(0.6 / (px * max(t, 0.1))) / 1.07 + 1.0, 2.0, MAX_OCT);
        float d = lobes(p, hd, lod);
        if (d > 0.45) {   // fine bumps push out by ~0.3 at the 99th percentile
            t += max((d - 0.45) * 0.5, dt);
            continue;
        }
        // Fine bump octaves down to ~2 px (first period 0.33 units).
        float foct = clamp(log2(0.33 / (2.0 * px * max(t, 0.1))) + 1.0, 1.0, 4.0);
        d -= bumps(p, foct);
        float dens = puff(d);
        if (dens <= 0.0) {
            t += dt;
            continue;
        }
        float od = sunDepth(p, L, lod);
        // Direct sun with forward-scattering phase, plus two softer multiple-scattering
        // octaves (less extinction, flatter phase) that carry light into the shadowed side.
        float sun = exp(-SIGMA * od) * ph
                  + (0.5 * exp(-0.25 * SIGMA * od) + 0.25 * exp(-0.06 * SIGMA * od)) * mix(ph, 1.0, 0.6);
        float hy = smoothstep(-4.0, 8.0, p.y);
        float amb = (0.02 + 0.2 * hy * hy) * exp(-1.5 * skyDepth(p));
        // Exponential distance haze toward the sky colour, starting past the near row.
        float lum = mix(0.8 * sun + amb, haze, 1.0 - exp(-FOG * max(t - 8.0, 0.0)));
        float tr = exp(-SIGMA * dens * dt);
        S += T * (1.0 - tr) * lum;
        T *= tr;
        t += dt;
    }
    float s = 1.0 - exp(-1.4 * (S + T * haze));
    // +-0.5/255 dither so smooth dark gradients don't step on 8-bit canvases.
    fragColor = vec4(mix(0.14 * vec3(1.0, 2.0, 3.0), vec3(1.0), s) + (jit - 0.5) / 255.0, 1.0);
