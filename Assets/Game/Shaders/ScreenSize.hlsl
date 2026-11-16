#ifndef PUNCHY_SCREEN_SIZE_INCLUDED
#define PUNCHY_SCREEN_SIZE_INCLUDED

// DetailPolicy.ScreenSize for shaders (rule 20): how big something looks, in reference pixels (1080 lines at the
// camera's real field of view, times Graphics Detail), never fixed metres. Reference pixels come from this camera's own
// projection, so a zoom, a wider view or a reflection camera each get their own; Detail comes from DetailPolicy.SetView.
float _PunchyDetail;   // GraphicsSettings.Detail (1 = Medium, as tuned); unset reads as 1

// Reference pixels a metre spans at 1 m: DetailPolicy.PixelsPerMetreAt(fov) = 1080 / (2 tan(fov / 2)), times Detail.
float PunchyPixelsPerMetre()
{
    return 540.0 * abs(UNITY_MATRIX_P._m11) * (_PunchyDetail > 0 ? _PunchyDetail : 1.0);
}

// How big something of this size (m) looks at this distance (m), in reference pixels.
float PunchyScreenSize(float size, float distance)
{
    return size / max(distance, 0.01) * PunchyPixelsPerMetre();
}

// The distance at which the tuning view (Medium, 60 degrees, 1080 lines: 935.3 px a metre) sees things as big as
// this view sees them at this distance. A fade written in metres for that view keeps its exact curve there and keeps
// the same on-screen size everywhere else: smoothstep(a, b, PunchyReferenceDistance(d)).
float PunchyReferenceDistance(float distance)
{
    return distance * 935.3 / PunchyPixelsPerMetre();
}

#endif
