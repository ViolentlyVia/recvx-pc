/* PC port: VU0 state and instruction helpers.
 *
 * Shared by the compiled VU0 microcode (pc/src/vu/vu0_micro.c, generated from vsm/ps2_vu0.vsm
 * by pc/tools/vu_micro2c.py) and the translated COP2 macro-mode inline assembly
 * (pc/tools/asm2c.py). Floats follow VU rules: no infinities/NaNs (results clamp to +-FLT_MAX)
 * and denormals flush to zero.
 *
 * Flags: the microcode reads MAC and clip flags with the FMAC pipeline latency (an instruction
 * sees the flags of the instruction issued 4 cycles earlier), so flag writes are kept in a
 * small history indexed by cycle. Q likewise becomes visible after the DIV/SQRT/RSQRT latency.
 */
#pragma once

#include <float.h>
#include <math.h>
#include <string.h>

typedef union {
    float f[4];
    unsigned int u[4];
    int i[4];
} VuVec;

#define VU_FLAG_HIST 16

typedef struct {
    VuVec vf[32];
    int vi[16];
    VuVec acc;
    float q, i, p;
    float q_pending;
    unsigned int q_ready;
    unsigned int clip;          /* current (latest) clip flags, 24 bits */
    unsigned int mac;           /* current MAC flags */
    unsigned int status;
    unsigned int cycle;
    struct { unsigned int cycle, mac, clip, status; } hist[VU_FLAG_HIST];
    unsigned int hist_pos;
    VuVec mem[256];             /* VU0 data memory, 4 KB */
} PcVu0;

extern PcVu0 pcVu0;

/* run a VU0 microprogram starting at instruction index addr (vcallms addr * 8) */
void pcVu0Micro(int addr);

/* ---- float helpers ---- */

static __inline float vu_f(float x)
{
    union { float f; unsigned int u; } v;

    v.f = x;

    if ((v.u & 0x7F800000) == 0x7F800000)
    {
        v.u = (v.u & 0x80000000) | 0x7F7FFFFF;   /* inf/nan -> max */
    }
    else if ((v.u & 0x7F800000) == 0)
    {
        v.u &= 0x80000000;                       /* denormal -> 0 */
    }

    return v.f;
}

/* ---- flags ---- */

/* MAC flags for the masked fields of a result: Z bits 0-3 (w..x), S bits 4-7 */
static __inline unsigned int vu_mac_of(const VuVec* r, int mask)
{
    unsigned int m = 0;
    int k;

    for (k = 0; k < 4; k++)
    {
        if (mask & (8 >> k))
        {
            unsigned int bit = 3 - k;   /* x -> bit 3, w -> bit 0 */

            if (r->f[k] == 0.0f)
            {
                m |= 1u << bit;
            }

            if (r->u[k] & 0x80000000)
            {
                m |= 0x10u << bit;
            }
        }
    }

    return m;
}

static __inline void vu_hist_push(PcVu0* v)
{
    v->hist_pos = (v->hist_pos + 1) % VU_FLAG_HIST;
    v->hist[v->hist_pos].cycle = v->cycle;
    v->hist[v->hist_pos].mac = v->mac;
    v->hist[v->hist_pos].clip = v->clip;
    v->hist[v->hist_pos].status = v->status;
}

static __inline void vu_set_mac(PcVu0* v, const VuVec* r, int mask)
{
    v->mac = vu_mac_of(r, mask);
    v->status = (v->mac & 0xF) ? (v->status | 1) : (v->status & ~1u);
    v->status = (v->mac & 0xF0) ? (v->status | 2) : (v->status & ~2u);
    vu_hist_push(v);
}

static __inline void vu_clip(PcVu0* v, const VuVec* a, float w)
{
    float aw = fabsf(w);
    unsigned int f = 0;

    if (a->f[0] > aw)  f |= 0x01;
    if (a->f[0] < -aw) f |= 0x02;
    if (a->f[1] > aw)  f |= 0x04;
    if (a->f[1] < -aw) f |= 0x08;
    if (a->f[2] > aw)  f |= 0x10;
    if (a->f[2] < -aw) f |= 0x20;

    v->clip = ((v->clip << 6) | f) & 0xFFFFFF;
    vu_hist_push(v);
}

/* flags as seen by a microcode instruction issued at the current cycle (4-cycle latency) */
static __inline const unsigned int* vu_flags_seen(PcVu0* v)
{
    static unsigned int zero[3];
    unsigned int k, best = VU_FLAG_HIST;

    for (k = 0; k < VU_FLAG_HIST; k++)
    {
        unsigned int idx = (v->hist_pos + VU_FLAG_HIST - k) % VU_FLAG_HIST;

        if (v->cycle - v->hist[idx].cycle >= 4)
        {
            best = idx;
            break;
        }
    }

    if (best == VU_FLAG_HIST)
    {
        zero[0] = v->mac;
        zero[1] = v->clip;
        zero[2] = v->status;
        return zero;
    }

    return &v->hist[best].mac;   /* mac, clip, status are consecutive */
}

static __inline unsigned int vu_mac_seen(PcVu0* v)  { return vu_flags_seen(v)[0]; }
static __inline unsigned int vu_clip_seen(PcVu0* v) { return vu_flags_seen(v)[1]; }

/* ---- Q ---- */

static __inline float vu_q(PcVu0* v)
{
    if ((v->q_ready != 0) && ((int)(v->cycle - v->q_ready) >= 0))
    {
        v->q = v->q_pending;
        v->q_ready = 0;
    }

    return v->q;
}

static __inline void vu_waitq(PcVu0* v)
{
    if (v->q_ready != 0)
    {
        v->q = v->q_pending;
        v->q_ready = 0;
    }
}

static __inline float vu_div(float a, float b)
{
    if (b == 0.0f)
    {
        union { float f; unsigned int u; } r, sa, sb;

        sa.f = a;
        sb.f = b;
        r.u = ((sa.u ^ sb.u) & 0x80000000) | 0x7F7FFFFF;
        return r.f;
    }

    return vu_f(a / b);
}

static __inline void vu_set_q(PcVu0* v, float value, int latency)
{
    vu_q(v);
    v->q_pending = value;
    v->q_ready = v->cycle + latency;

    if (v->q_ready == 0)
    {
        v->q_ready = 1;
    }
}

/* immediate update (COP2 macro mode: DIV results are waited for by the next COP2 op) */
static __inline void vu_set_q_now(PcVu0* v, float value)
{
    v->q = value;
    v->q_ready = 0;
}

static __inline float vu_rsqrt(float a, float b)
{
    float s = sqrtf(fabsf(b));

    return vu_div(a, s);
}

/* ---- conversions ---- */

static __inline int vu_ftoi(float f, float scale)
{
    double d = (double)f * scale;

    if (d >= 2147483647.0)  return 0x7FFFFFFF;
    if (d <= -2147483648.0) return (int)0x80000000;

    return (int)d;   /* truncation toward zero */
}

/* integer registers are 16 bits */
static __inline int vu_vi(int x)
{
    return (short)(x & 0xFFFF);
}
