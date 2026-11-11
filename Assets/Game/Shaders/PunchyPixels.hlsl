#ifndef PUNCHY_PIXELS_INCLUDED
#define PUNCHY_PIXELS_INCLUDED

// R-13 (rule 20): shader fades by how big something looks, as DetailPolicy measures it in C#. DetailPolicy.SetView sets
// _PunchyPixelsPerMetre once a frame: reference pixels (1080 lines) per metre at 1 m for the camera's field of view, times
// the Detail setting. A fade is then a smoothstep on PunchyPixels(size, distance) between two pixel counts, tuned so the
// 60 degree view at Detail 1 gets the old metres (size * 935.3 / metres); a narrower view keeps detail further out.

float _PunchyPixelsPerMetre;

// How many reference pixels something this big (m: its largest extent) covers at this distance (m).
float PunchyPixels(float size, float distance)
{
    // Before any SetView (an editor frame, a shot before the first Update) the reference view at Detail 1.
    float perMetre = _PunchyPixelsPerMetre > 0 ? _PunchyPixelsPerMetre : 935.3;
    return size * perMetre / max(distance, 0.01);
}

#endif
