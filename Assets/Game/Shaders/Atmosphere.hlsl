#ifndef PUNCHY_ATMOSPHERE_INCLUDED
#define PUNCHY_ATMOSPHERE_INCLUDED

// Twilight colours and everything between the camera and a surface: weather volumes, the haze table, under water,
// PunchyAtmosphere (split from Sky.hlsl, R-23 row 3). Includes Clouds.hlsl, so PunchyCloudShadow comes with it.

#include "Clouds.hlsl"
#include "SharedClock.hlsl"

// ------------------------------------------------------------------------------------------------ twilight
// Sunrise and sunset (W-31), all keyed by the sun's elevation in degrees: a warm band low on the sun's side of the
// sky, violet on the far side and overhead, a bright glow round the sun, warm-lit clouds, then blue hour. Weather
// mutes it through _PunchyTwilightDull (0 clear … 1 overcast/storm; unset reads as clear).
half _PunchyTwilightDull;

float PunchySunElevation() { return degrees(asin(clamp(_PunchySunDirection.y, -1.0, 1.0))); }

// The warm colour of the low sun: gold at +12°, orange at +5°, red-orange at 0°, pink-red at -4°, violet by -8°.
half3 PunchyTwilightColor(float e)
{
    half3 c = lerp(half3(0.42, 0.26, 0.55), half3(0.96, 0.38, 0.46), saturate((e + 8) / 4));
    c = lerp(c, half3(1.00, 0.32, 0.10), saturate((e + 4) / 4));   // deeper red-orange (W-33)
    c = lerp(c, half3(1.00, 0.46, 0.16), saturate(e / 5));
    return lerp(c, half3(1.00, 0.76, 0.46), saturate((e - 5) / 7));
}

// How much twilight shows: none above +12° or below -8°, full from about -3° to +4°, less under cloud.
half PunchyTwilightAmount(float e)
{
    return smoothstep(-8, -3, e) * (1 - smoothstep(4, 12, e)) * (1 - 0.8 * _PunchyTwilightDull);
}

// How bright the twilight light is: full while the sun is up, dimming to a glow as it sinks towards -8°.
half PunchyTwilightLight(float e) { return lerp(0.1, 1, smoothstep(-8, 1, e)); }

// 0 looking away from the sun … 1 looking towards it, round the horizon.
half PunchySunSide(float3 dir)
{
    float2 s = _PunchySunDirection.xz, d = dir.xz;
    return saturate(dot(d, s) * rsqrt(max(dot(d, d) * dot(s, s), 1e-8)) * 0.5 + 0.5);
}

// The blue moon's light as the sky sees it (WeatherSystem; linear, 0 when it's down, new or by day): feedback 11-08
// item 35, so the haze and the clouds read as lit by it, not only the ground.
half3 _PunchyBlueMoonGlow;

// The blue moon's light scattered in the low air towards `dir`: a little all round, much more on its side of the sky.
half3 PunchyBlueMoonHaze(float3 dir)
{
    half toward = saturate(dot(normalize(dir), _PunchyBlueMoon) * 0.5 + 0.5);
    toward *= toward;
    return _PunchyBlueMoonGlow * (0.007 + 0.04 * toward * toward);
}

// The horizon colour in direction `dir` with twilight: warm on the sun's side, grey-violet on the far side. Also
// the haze colour, so land and sea fade into the same band. Under a blue moon it glows blue towards it.
half3 PunchyTwilightHorizon(float3 dir)
{
    half3 horizon = _PunchySkyHorizon + PunchyBlueMoonHaze(dir);
    float e = PunchySunElevation();
    half amount = PunchyTwilightAmount(e);
    if (amount <= 0)
        return horizon;
    half toward = PunchySunSide(dir);
    toward *= toward;
    half lum = dot(horizon, half3(0.3, 0.5, 0.2));
    half3 warm = PunchyTwilightColor(e) * PunchyTwilightLight(e) * 1.05;
    half3 cool = half3(0.62, 0.55, 0.9) * lum;
    return lerp(horizon, lerp(cool, warm, toward), amount * lerp(0.65, 1, toward));
}

// The zenith with twilight: a deeper blue with a touch of purple.
half3 PunchyTwilightZenith()
{
    half amount = PunchyTwilightAmount(PunchySunElevation());
    return lerp(_PunchySkyZenith, _PunchySkyZenith * half3(0.95, 0.72, 1.05) + half3(0.03, 0.0, 0.05), amount);
}

// How strongly clouds glow at sunset (W-33, Zach wanted much more glow): from +12° down to blue hour, and weather
// mutes it by at most half (the sky band is muted by up to 80%).
half PunchyCloudGlowAmount(float e)
{
    return smoothstep(-8, -3, e) * (1 - smoothstep(9, 13, e)) * (1 - 0.5 * _PunchyTwilightDull);
}

// Twilight light on a cloud at height y: rich gold, orange or red at about 2.2x the W-31 strength, and after sunset
// only the high clouds still catch it (low ones lose it at once, the highest about 9° later).
half3 PunchyTwilightCloudLight(float e, float y)
{
    half reach = lerp(0.8, 9.0, saturate((y - 800) / 6500));
    half caught = e >= 0 ? 1 : saturate(1 + e / reach);
    half3 c = PunchyTwilightColor(e);
    c = saturate(lerp(dot(c, half3(0.3, 0.5, 0.2)).xxx, c, 1.35));
    return c * PunchyTwilightLight(e) * caught * 2.2;
}

void PunchyCloudLight(out float3 direction, out half3 color)
{
    // The sun by day, the moon by night; through twilight (down to -8°) the sun still lights the high clouds.
    bool sunUp = _PunchySunDirection.y > -0.14;
    direction = sunUp ? _PunchySunDirection : _PunchyMoonDirection;
    // The moon lights the clouds by its phase: a new moon leaves them a dim grey (Zach, 11-08).
    half3 moonlight = half3(0.16, 0.19, 0.27) * _PunchyNight * lerp(0.15, 1, _PunchyMoonPhase.y);
    color = sunUp ? _PunchySunColor : moonlight;
    // Moonlight takes over gradually through blue hour, so the switch at -8° doesn't pop.
    if (sunUp)
        color += moonlight * (1 - smoothstep(-0.14, -0.05, _PunchySunDirection.y));
    // The blue moon lights the clouds too (item 35), shaded along the main light's path (its own would cost a second
    // march): its glow is zero by day, so this only shows at night.
    color += _PunchyBlueMoonGlow * 0.09;
}


// ------------------------------------------------------------------------------------------------ atmosphere

// Dust and rain haze along a ray, read from the weather map: a distant sandstorm is a tan wall, rain hangs in
// grey curtains below the local cloud base. Steps are packed towards the camera, where detail matters most.
// Gives the light scattered in and the fraction of what's behind that still shows through. The volumes thin out
// with height (dust above its billowing top, rain above the cloud base), judged at min(ray height, heightCap):
// the haze table caps it at ground level (see PunchyHazeLutHeight), a true ray passes a huge cap.
void PunchyWeatherVolumesAlong(float3 start, float3 dir, float rayLength, int steps, float heightCap, out half3 scatter, out float transmit)
{
    half brightness = dot(_PunchySkyHorizon, half3(0.3, 0.5, 0.2));
    scatter = 0;
    transmit = 1;
    float previous = 0;
    [loop]
    for (int i = 1; i <= steps; i++)
    {
        float s = (float)i / steps;
        float t = rayLength * s * s;
        float segment = t - previous;
        float3 p = start + dir * (previous + segment * 0.5);
        previous = t;
        float height = min(p.y, heightCap);

        // The rain falls from where the cloud's rim is (R-12): the maps are read at the cloud's warp at its base, so the
        // shaft's sides follow the cloud above instead of the smooth disc in the map.
        float cloudBase = PunchyCloudShapeAt(p.xz).x;
        half4 w = PunchyWeatherAt(p.xz + PunchyCloudWarpAt(float3(p.x, cloudBase, p.z)));
        if (w.a + w.b <= 0.004)
            continue;

        // Drifting noise breaks the volumes up: dust billows with an uneven top, rain falls in streaky curtains.
        float2 drift = _PunchyCloudOffset * PUNCHY_CLOUD_FEATURE;
        float billow = PunchyValueNoise((p.xz - drift) / 260) * 0.65 + PunchyValueNoise((p.xz - drift * 1.6) / 90) * 0.35;
        float dustTop = PUNCHY_DUST_TOP * (0.45 + billow);
        float dust = w.a * PUNCHY_DUST_DENSITY * saturate((dustTop - height) / 180) * (0.4 + billow);
        // The shaft is densest in the middle of the cell and frays at its edges. R-12: thin enough to see through, in
        // streaks with clear gaps between them (a coarse and a fine layer, cut off below), thinning with depth under
        // the cloud base as it falls, so the land behind shows.
        float streaks = PunchyValueNoise((p.xz - drift) / 110) * 0.6 + PunchyValueNoise((p.xz - drift * 1.3) / 30) * 0.4;
        streaks = saturate((streaks - 0.3) * 2.5);
        float fall = cloudBase - height;
        float rain = w.b * w.b * PUNCHY_RAIN_DENSITY * 0.6 * saturate(fall / 120) * lerp(1, 0.4, saturate(fall / 1500)) * (0.15 + streaks);
        float density = dust + rain;
        if (density <= 1e-6)
            continue;
        half3 rainTint = _PunchySkyHorizon * 0.72 * (1 - 0.5 * w.g);
        half3 tint = (dust * _PunchyDustColor * (brightness * 1.4 + 0.03) + rain * rainTint) / density;
        float absorbed = 1 - exp(-density * segment);
        scatter += tint * absorbed * transmit;
        transmit *= 1 - absorbed;
    }
}

half3 PunchyWeatherVolumes(half3 color, float3 start, float3 dir, float rayLength, int steps)
{
    half3 scatter;
    float transmit;
    PunchyWeatherVolumesAlong(start, dir, rayLength, steps, 1e7, scatter, transmit);
    return color * transmit + scatter;
}

// The weather volumes seen from the camera, marched once a frame into a small table (HazeLut.shader, rendered by
// WeatherSystem) instead of per pixel in every surface shader, which cost weak GPUs nearly half their frame rate
// in stormy weather. Columns are directions round the horizon, rows distances out to PUNCHY_HAZE_RANGE (square-root
// spaced, so near distances get more rows). Rays run almost level from the camera, like the horizon haze target.
// The table is indexed by direction and distance only, but the surfaces it hazes are mostly the ground, below the
// camera. Marching the air at the camera's own height made a camera high near a rain or snow cell (a player on a
// peak) haze the low ground by whatever its eye level held, which flips between directions near the cloud base and
// drew hard vertical edges on screen (A-87 row 2). So the volumes are judged at ground level instead: the full
// shaft below the cloud base, the dust below its top, whatever the camera's height.
// rgb = scattered light, a = 1 - transmittance, so an unset (black) table means no weather haze rather than black.
#define PUNCHY_HAZE_RANGE 8000.0
#define PUNCHY_HAZE_ELEVATION 0.02
#define PUNCHY_HAZE_ROWS 32.0
#define PunchyHazeLutHeight 0.0   // the height the table judges the volumes at (sea level: under every cloud base)
TEXTURE2D(_PunchyHazeLut);     SAMPLER(sampler_PunchyHazeLut);
float3 _PunchyHazeOrigin;      // where the table was marched from

// The ray through table column u (0..1 round the horizon).
float3 PunchyHazeLutDirection(float u)
{
    float angle = (u - 0.5) * 2 * PI;
    return normalize(float3(cos(angle), PUNCHY_HAZE_ELEVATION, sin(angle)));
}

// The distance of table row v (texel centres run from 0 m to PUNCHY_HAZE_RANGE).
float PunchyHazeLutDistance(float v)
{
    float s = saturate((v * PUNCHY_HAZE_ROWS - 0.5) / (PUNCHY_HAZE_ROWS - 1));
    return PUNCHY_HAZE_RANGE * s * s;
}

// Applies the tabled weather volumes for `distance` metres towards `offsetXZ` (from the table's origin).
half3 PunchyHazeFromLut(half3 color, float2 offsetXZ, float distance)
{
    float u = atan2(offsetXZ.y, offsetXZ.x) * (0.5 / PI) + 0.5;
    float s = sqrt(saturate(distance / PUNCHY_HAZE_RANGE));
    float v = (s * (PUNCHY_HAZE_ROWS - 1) + 0.5) / PUNCHY_HAZE_ROWS;
    half4 haze = SAMPLE_TEXTURE2D_LOD(_PunchyHazeLut, sampler_PunchyHazeLut, float2(u, v), 0);
    return color * (1 - haze.a) + haze.rgb;
}

// Under water (playtest 11-08 item 38): light streaming down through the surface in shafts. A few steps along the view
// ray, the first 40 m; each step's point is traced back up to the surface along the sun's (or moon's) refracted ray,
// and two drifting noise layers there say whether light got through (the ripples focusing it), sharpened into
// shafts. Each fades with the point's depth under the surface and with the water's fog towards the eye. Steps come
// from the water quality setting (WaterQuality.UnderwaterShaftSteps); none on Low.
half3 PunchyUnderwaterShafts(float3 camera, float3 dir, float distance)
{
    int steps = (int)_PunchyUnderwaterShafts.x;
    if (steps <= 0)
        return 0;
    bool day = _PunchySunDirection.y > 0;
    float3 toLight = day ? _PunchySunDirection : _PunchyMoonDirection;
    half3 light = day ? _PunchySunColor : half3(0.16, 0.19, 0.27) * 0.4 * _PunchyNight * saturate(_PunchyMoonDirection.y * 4);
    // (Into water the light bends towards the vertical; held a little off flat so the trace back stays short.)
    float3 down = refract(-toLight, float3(0, 1, 0), 0.75);
    down.y = min(down.y, -0.3);
    // (Item 39 wants them converging overhead and spreading round you. R-38 row 4: they did that from a point tied to
    // the camera, so swimming slid every stripe across the screen. Now each point is traced up the one refracted ray,
    // fixed in the world: parallel shafts, which perspective still draws converging on the light through the Snell
    // window, and which stay put as you swim.)
    float2 slope = down.xz / -down.y;   // across per metre up
    float reach = min(distance, 40);
    float stepLength = reach / steps;
    float t = _PunchyClock;
    float sum = 0;
    [loop] for (int i = 0; i < steps; i++)
    {
        float s = (i + 0.5) * stepLength;
        float3 q = camera + dir * s;
        float below = _PunchyUnderwaterSurface.x - q.y;
        if (below <= 0)
            continue;
        // Where the ray from this point back up towards the light crosses the surface.
        float2 at = q.xz - slope * below;
        float a = PunchyValueNoise(at * 0.42 + float2(t * 0.11, t * 0.05));
        float b = PunchyValueNoise(at * 0.71 - float2(t * 0.07, -t * 0.09) + 5.3);
        float shaft = smoothstep(0.3, 0.62, a * b * 2);
        sum += shaft * exp(-below * _PunchyUnderwaterShafts.z) * exp(-s * _PunchyUnderwater.w);
    }
    return light * ((half3)_PunchyUnderwater.rgb * 1.5 + 0.08) * sum * stepLength * _PunchyUnderwaterShafts.y * 0.06;
}

// The weather volumes seen against the sky (R-8): the skybox's own march along each view direction (8 steps out to
// 8 km, the true ray height), done once a frame into a small direction table (HazeLut.shader's second pass, rendered
// by WeatherSystem from the camera) instead of per full-res sky pixel, where it was over half the skybox's cost.
// Columns run round the horizon; rows are spaced by y|y|, so the directions near the horizon, where the volumes are,
// get most of them. Same layout as the haze table: rgb = scattered light, a = 1 - transmittance.
TEXTURE2D(_PunchySkyVolumes);  SAMPLER(sampler_PunchySkyVolumes);

// The view direction through table texel uv (0..1 both ways).
float3 PunchySkyVolumesDirection(float2 uv)
{
    float angle = (uv.x - 0.5) * 2 * PI;
    float r = uv.y * 2 - 1;
    float y = r * abs(r);
    float across = sqrt(saturate(1 - y * y));
    return float3(cos(angle) * across, y, sin(angle) * across);
}

// Applies the tabled weather volumes along the view direction `dir` (unit length).
half3 PunchySkyVolumesFromLut(half3 color, float3 dir)
{
    float u = atan2(dir.z, dir.x) * (0.5 / PI) + 0.5;
    float v = sign(dir.y) * sqrt(abs(dir.y)) * 0.5 + 0.5;
    half4 haze = SAMPLE_TEXTURE2D_LOD(_PunchySkyVolumes, sampler_PunchySkyVolumes, float2(u, v), 0);
    return color * (1 - haze.a) + haze.rgb;
}

// 0..1: how fully clear and sunny the day is round the camera (WeatherSystem): the base haze thins there (item 30).
half _PunchyHazeClear;
half _PunchyNightHaze;            // R-39 row 2 (#402): the share of the haze's brightness kept at night (GlowLook; unset is 1)

// Everything between the camera and a surface point: weather volumes, then the base haze (fog coloured like the
// horizon, thick enough at the edge of the streamed terrain to hide it).
half3 PunchyAtmosphere(half3 color, float3 positionWS)
{
    float3 camera = _WorldSpaceCameraPos;
    float3 toPoint = positionWS - camera;
    float distance = length(toPoint);
    float3 dir = toPoint / max(distance, 1e-3);
    // Under water (P-20, Underwater.cs): the water's own fog by distance instead of the sky's haze, which can't be
    // seen from down here. Everything drawn through PunchyAtmosphere (terrain, flora, rocks) fades alike. Only the part
    // of the path under the surface counts (playtest 11-08 item 8): the shore above water stays clear enough to show
    // through Snell's window as the lens nears the top (Water.shader).
    if (_PunchyUnderwater.w > 0)
    {
        float wet = distance;
        float rise = positionWS.y - camera.y;
        if (positionWS.y > _PunchyUnderwaterSurface.x && rise > 1e-3)
            wet *= saturate(_PunchyUnderwaterSurface.y / rise);
        color = lerp(color, (half3)_PunchyUnderwater.rgb, 1 - exp(-pow(wet * _PunchyUnderwater.w, 2)));
        return color + PunchyUnderwaterShafts(camera, dir, distance);
    }
    color = PunchyHazeFromLut(color, positionWS.xz - _PunchyHazeOrigin.xz, distance);
    float haze = 1 - exp(-pow(distance * _PunchyHazeDensity, 2));
    if (haze < 0.01)
        return color;
    // Fully clear, sunny days (feedback 11-08 item 30): the haze stays thin further out and closes in near the edge of
    // the streamed terrain (distance cubed instead of squared, scaled to the same ~98% at that edge, so the edge stays
    // hidden), so distant land isn't greyed. Not in cold lands (_PunchyAurora), cloud or weather (WeatherSystem).
    half clearDay = _PunchyHazeClear * (1 - _PunchyAurora);
    if (clearDay > 0)
    {
        float far = distance * _PunchyHazeDensity;
        haze = lerp(haze, 1 - exp(-far * far * far / 1.98), clearDay);
    }

    // Haze towards exactly what the sky shows just above the horizon in this direction: the horizon colour with the
    // sun's glow, seen through the same rain and dust the skybox looks through. Hazing to the bare horizon colour
    // left distant land brighter than the sky behind it whenever rain or cloud darkened the sky.
    float3 flat = normalize(float3(dir.x, 0.02, dir.z));
    float clearness = 1 - _PunchyClouds.x * 0.7;
    // Distant land takes only half the sunset tint (luminance kept), so far hills don't go pink (W-33).
    half3 twilightHaze = PunchyTwilightHorizon(flat);
    float hazeElevation = PunchySunElevation();
    twilightHaze = lerp(twilightHaze, dot(twilightHaze, half3(0.3, 0.5, 0.2)) * half3(1.05, 0.97, 0.92),
                        0.5 * saturate(PunchyTwilightAmount(hazeElevation) * 3) * smoothstep(-3, 0, hazeElevation));
    half3 horizon = twilightHaze + _PunchySunColor * pow(saturate(dot(flat, _PunchySunDirection)), 12) * 0.25 * clearness;
    horizon = PunchyHazeFromLut(horizon, flat.xz, PUNCHY_HAZE_RANGE);
    // R-39 row 2 (#402, Zach 11-15): a darker night haze, so far land sinks into the night and glows stand out.
    horizon *= _PunchyNightHaze > 0 ? _PunchyNightHaze : 1;
    return lerp(color, horizon, haze);
}

#endif
