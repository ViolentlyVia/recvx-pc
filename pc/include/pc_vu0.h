/* PC port: plain C equivalents of VU0 macro-mode idioms used by the game code.
 *
 * Matrices are NJS_MATRIX (float[16]) as laid out in memory; the VU loads them as four
 * row vectors (lqc2 vfN, 0x00/0x10/0x20/0x30) and transforms a vector v as
 *     out = row0 * v.x + row1 * v.y + row2 * v.z (+ row3 * w)
 */
#pragma once

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
