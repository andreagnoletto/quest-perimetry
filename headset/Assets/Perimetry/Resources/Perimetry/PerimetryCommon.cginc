#ifndef PERIMETRY_COMMON_INCLUDED
#define PERIMETRY_COMMON_INCLUDED

// Globals set every frame by Perimetry.Headset.HeadsetRig.
float _PerimetryEye;            // 0 left (OS), 1 right (OD), -1 both
float _PerimetryUntestedLevel;  // drive level of the untested eye (0 = black)
float _PerimetryMaxCode;        // 2^bits - 1
float _PerimetryDither;         // 0 none, 1 Bayer 4x4, 2 Bayer 4x4 temporal
float _PerimetryFrame;          // frame counter for temporal dithering

// Eye being rendered: 0 left, 1 right, -1 when not rendering in stereo.
float PerimetryEyeIndex()
{
#if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED) || defined(UNITY_SINGLE_PASS_STEREO)
    return (float)unity_StereoEyeIndex;
#else
    return -1;
#endif
}

float3 PerimetrySrgbToLinear(float3 v)
{
    float3 lo = v / 12.92;
    float3 hi = pow((v + 0.055) / 1.055, 2.4);
    return float3(v.r <= 0.04045 ? lo.r : hi.r, v.g <= 0.04045 ? lo.g : hi.g, v.b <= 0.04045 ? lo.b : hi.b);
}

float PerimetryBayer(float2 pixel)
{
    // Same matrix and frame shift as Perimetry.Headset.Logic.Dither.Threshold
    static const float m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
    int f = (int)_PerimetryFrame;
    int ox = _PerimetryDither > 1.5 ? (f & 3) : 0;
    int oy = _PerimetryDither > 1.5 ? ((f >> 2) & 3) : 0;
    int x = ((int)pixel.x + ox) & 3;
    int y = ((int)pixel.y + oy) & 3;
    return (m[y * 4 + x] + 0.5) / 16.0;
}

float3 PerimetryDitherToLinear(float3 level, float2 pixel)
{
    float t = _PerimetryDither < 0.5 ? 0.5 : PerimetryBayer(pixel);
    float3 code = min(floor(saturate(level) * _PerimetryMaxCode + t), _PerimetryMaxCode);
    return PerimetrySrgbToLinear(code / _PerimetryMaxCode);
}

#endif
