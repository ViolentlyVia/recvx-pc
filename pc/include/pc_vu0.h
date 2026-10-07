/* PC port: plain C equivalents of VU0 macro-mode idioms used by the game code.
 *
 * Matrices are NJS_MATRIX (float[16]) as laid out in memory; the VU loads them as four
 * row vectors (lqc2 vfN, 0x00/0x10/0x20/0x30) and transforms a vector v as
 *     out = row0 * v.x + row1 * v.y + row2 * v.z (+ row3 * w)
 */
#pragma once

#include <float.h>
#include <math.h>

/* VU0 registers that the Ninja matrix library keeps state in between calls:
 * vf28-vf31 hold the "CN" (combined) matrix, vf18 the last transformed point. */
extern float pcVu0CN[16];
extern float pcVu0Last[4];

static __inline void pcVu0XformPoint(const float* m, const float* v, float* out)
{
    float x = v[0], y = v[1], z = v[2];

    out[0] = m[0] * x + m[4] * y + m[8]  * z + m[12];
    out[1] = m[1] * x + m[5] * y + m[9]  * z + m[13];
    out[2] = m[2] * x + m[6] * y + m[10] * z + m[14];
}

static __inline void pcVu0XformVector(const float* m, const float* v, float* out)
{
    float x = v[0], y = v[1], z = v[2];

    out[0] = m[0] * x + m[4] * y + m[8]  * z;
    out[1] = m[1] * x + m[5] * y + m[9]  * z;
    out[2] = m[2] * x + m[6] * y + m[10] * z;
}

/* 16-byte (qword) copy, the lq/sq pair the EE code uses for vertices. */
static __inline void pcCopyQword(void* dst, const void* src)
{
    const unsigned int* s = (const unsigned int*)src;
    unsigned int* d = (unsigned int*)dst;

    d[0] = s[0];
    d[1] = s[1];
    d[2] = s[2];
    d[3] = s[3];
}

/* VU RSQRT/DIV of 0 give the largest float instead of infinity (so 0 * result stays 0). */
static __inline float pcVu0Rsqrt(float x)
{
    x = fabsf(x);
    return (x == 0) ? FLT_MAX : 1.0f / sqrtf(x);
}

static __inline float pcVu0Div(float a, float b)
{
    if (b == 0)
    {
        return (a < 0) ? -FLT_MAX : FLT_MAX;
    }

    return a / b;
}

static __inline void pcCopyMatrix(float* d, const float* s)
{
    int i;

    for (i = 0; i < 16; i++)
    {
        d[i] = s[i];
    }
}

/* Two-row rotation used by njRotateX/Y/Z:  a' = a * c + b * s,  b' = a * -s + b * c  (xyz only) */
static __inline void pcRotRows(float* a, float* b, float s, float c)
{
    int i;

    for (i = 0; i < 3; i++)
    {
        float av = a[i], bv = b[i];

        a[i] = av * c + bv * s;
        b[i] = av * -s + bv * c;
    }
}

/* Core of Ps2AddPrim3D* (ps2_dummy.c, was VU0 assembly; implemented in pc/src/pc_ps2gfx.c).
 * Converts num vertices (ST/Q, RGBA, XYZW: 3 qwords each) from src to a GIF PACKED stream at
 * dst: depth from Q through Ps2_zbuff_a/b, clip flags -> ADC bit (no drawing kick), and stores
 * the average depth after the last vertex. ftoi_color: RGBA arrives as floats. */
void pcPs2Prim3DCore(const void* src, void* dst, unsigned int num, int ftoi_color);
