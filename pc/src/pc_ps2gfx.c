/* PC port: C versions of VU0 code from the PS2 graphics glue (ps2_dummy.c). */
#include "../../include/ps2/veronica/prog/ps2_dummy.h"
#include "../../include/ps2/veronica/prog/ps2_NaDraw2D.h"
#include "../../include/ps2/veronica/prog/main.h"

/* VU CLIP flags for one vertex: +x, -x, +y, -y, +z, -z against |w| */
static unsigned int clip6(float x, float y, float z, float w)
{
    float aw = fabsf(w);
    unsigned int f = 0;

    if (x > aw)  f |= 0x01;
    if (x < -aw) f |= 0x02;
    if (y > aw)  f |= 0x04;
    if (y < -aw) f |= 0x08;
    if (z > aw)  f |= 0x10;
    if (z < -aw) f |= 0x20;

    return f;
}

void pcPs2Prim3DCore(const void* src, void* dst, unsigned int num, int ftoi_color)
{
    const float* s = (const float*)src;
    unsigned int* d = (unsigned int*)dst;
    unsigned int cf = 0;
    float zsum = 0;
    unsigned int i, k;

    for (i = 0; i < num; i++, s += 12, d += 12)
    {
        unsigned int flags = *(const unsigned int*)&s[3];   /* qword 0 .w carries per-vertex flags */
        float x = s[8], y = s[9], z = s[10], w = s[11];
        float sz;

        /* qword 0: S, T, Q (and the flag word) */
        for (k = 0; k < 4; k++)
        {
            d[k] = ((const unsigned int*)s)[k];
        }

        /* qword 1: RGBA */
        for (k = 0; k < 4; k++)
        {
            d[4 + k] = ftoi_color ? (unsigned int)(int)s[4 + k] : ((const unsigned int*)s)[4 + k];
        }

        /* clipping: x, y against the 4096 window around 2048, z against the near/far range */
        cf = ((cf << 6) | clip6(x - 2048.0f, y - 2048.0f, (z + -2048.0625f) + z * 0.062501907f, 2047.0f)) & 0xFFFFFF;

        /* depth from Q (1/w): sz = -zb_a - zb_b * q, clamped like the GS z range */
        sz = -Ps2_zbuff_a + s[2] * -Ps2_zbuff_b;

        if (sz < 0)
        {
            sz = 0;
        }

        if (sz > 65534.0f)
        {
            sz = 65534.0f;
        }

        zsum += sz;

        if (cf & 0x3FFFF)
        {
            flags |= 0x8000;   /* ADC: this vertex does not kick a primitive */
        }

        d[8] = (unsigned int)(int)(x * 16.0f);
        d[9] = (unsigned int)(int)(y * 16.0f);
        d[10] = (unsigned int)(int)(sz * 16.0f);
        d[11] = (unsigned int)(int)(w * 16.0f) | flags;
    }

    /* average depth after the last vertex (read back as ((float*)p)[12 * num + 3]) */
    ((float*)d)[3] = zsum / (float)num;
}
