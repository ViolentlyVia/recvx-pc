/* PC port: runtime helpers for C translated from EE/COP2 inline assembly (pc/tools/asm2c.py).
 * EE general purpose registers are 128 bits; 32-bit results are sign-extended to 64 bits and
 * ordinary 64-bit instructions leave the upper 64 bits alone. */
#pragma once

#include <stdint.h>
#include <string.h>

#include "pc_vu.h"

typedef union {
    uint64_t d[2];
    int64_t sd[2];
    uint32_t w[4];
    int32_t sw[4];
    uint16_t h[8];
    int16_t sh[8];
    uint8_t b[16];
    float f[4];
} PcGpr;

#define A2C_SX32(x) ((uint64_t)(int64_t)(int32_t)(uint32_t)(x))
#define A2C_PTR(addr) ((unsigned char*)(uintptr_t)(uint32_t)(addr))

static __inline uint64_t a2c_ld64(uint32_t addr)  { uint64_t v; memcpy(&v, A2C_PTR(addr), 8); return v; }
static __inline void a2c_sd64(uint32_t addr, uint64_t v) { memcpy(A2C_PTR(addr), &v, 8); }

/* LDL/LDR/SDL/SDR, little endian */
static __inline uint64_t a2c_ldl(uint64_t rt, uint32_t addr)
{
    unsigned int byte = addr & 7;
    uint64_t mem = a2c_ld64(addr & ~7u);
    unsigned int sh = 56 - 8 * byte;
    uint64_t keep = (sh == 0) ? 0 : (rt & ((1ull << sh) - 1));

    return (mem << sh) | keep;
}

static __inline uint64_t a2c_ldr(uint64_t rt, uint32_t addr)
{
    unsigned int byte = addr & 7;
    uint64_t mem = a2c_ld64(addr & ~7u);
    uint64_t keep = (byte == 0) ? 0 : (rt & ~(~0ull >> (8 * byte)));

    return (mem >> (8 * byte)) | keep;
}

static __inline void a2c_sdl(uint64_t rt, uint32_t addr)
{
    unsigned int byte = addr & 7;
    uint32_t base = addr & ~7u;
    uint64_t mem = a2c_ld64(base);
    unsigned int sh = 56 - 8 * byte;
    uint64_t keepmask = (byte == 7) ? 0 : (~0ull << (8 * (byte + 1)));

    a2c_sd64(base, (mem & keepmask) | (rt >> sh));
}

static __inline void a2c_sdr(uint64_t rt, uint32_t addr)
{
    unsigned int byte = addr & 7;
    uint32_t base = addr & ~7u;
    uint64_t mem = a2c_ld64(base);
    uint64_t keepmask = (byte == 0) ? 0 : (~0ull >> (8 * (8 - byte)));

    a2c_sd64(base, (mem & keepmask) | (rt << (8 * byte)));
}

static __inline float a2c_bits_f(uint32_t u) { float f; memcpy(&f, &u, 4); return f; }
static __inline uint32_t a2c_f_bits(float f) { uint32_t u; memcpy(&u, &f, 4); return u; }

/* COP2 control registers (cfc2/ctc2) */
static __inline uint32_t a2c_cfc2(int reg)
{
    PcVu0* v = &pcVu0;

    if (reg < 16) return (uint32_t)(v->vi[reg] & 0xFFFF);

    switch (reg)
    {
    case 16: return v->status;
    case 17: return v->mac;
    case 18: return v->clip;
    case 20: return 0;
    case 21: return a2c_f_bits(v->i);
    case 22: vu_waitq(v); return a2c_f_bits(v->q);
    case 29: return 0;          /* VPU-STAT: VU0 idle */
    }

    return 0;
}

static __inline void a2c_ctc2(int reg, uint32_t val)
{
    PcVu0* v = &pcVu0;

    if (reg == 0) return;
    if (reg < 16) { v->vi[reg] = vu_vi((int)val); return; }

    switch (reg)
    {
    case 16: v->status = val; break;
    case 17: v->mac = val; break;
    case 18: v->clip = val & 0xFFFFFF; vu_hist_push(v); break;
    case 21: v->i = a2c_bits_f(val); break;
    case 22: v->q = a2c_bits_f(val); v->q_ready = 0; break;
    }
}

static __inline uint32_t a2c_plzcw1(uint32_t x)
{
    /* leading bits equal to the sign bit, minus one */
    uint32_t n = 0;
    uint32_t s = x >> 31;
    int k;

    for (k = 30; k >= 0; k--)
    {
        if (((x >> k) & 1) != s) break;
        n++;
    }

    return n;
}
