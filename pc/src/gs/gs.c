/* PC port, GS emulator: GIF packets, GS registers and primitive assembly.
 *
 * The PS2 code builds GIF packets (DMA'd on PATH3) exactly as for the real GS; this file
 * interprets them: register writes update GsRegs, vertex kicks assemble primitives which are
 * handed to the OpenGL renderer (gs_gl.c), IMAGE data goes to local memory (gs_mem.c).
 */
#include <stdlib.h>
#include <string.h>

#include "gs_internal.h"
#include "../../include/pc_platform.h"

GsRegs gsRegs;
int gsDumpFrame;
static int kicks;

/* ------------------------------------------------------------------------------------------- */
/* vertex queue and kicks */

static GsVertex vq[3];      /* last vertices: vq[2] newest */
static int vq_count;        /* vertices since PRIM was written */
static GsVertex fan_first;

static GsVertex out_verts[1024 * 3];
static int out_count;
static GsDrawState out_state;
static int out_valid;

static void current_state(GsDrawState* st)
{
    u64 p = (GSF(gsRegs.prmodecont, 0, 1)) ? gsRegs.prim : (gsRegs.prmode | (gsRegs.prim & 7));
    int c;

    memset(st, 0, sizeof(*st));

    st->type = GSF(gsRegs.prim, 0, 3);
    st->iip = GSF(p, 3, 1);
    st->tme = GSF(p, 4, 1);
    st->fge = GSF(p, 5, 1);
    st->abe = GSF(p, 6, 1);
    st->aa1 = GSF(p, 7, 1);
    st->fst = GSF(p, 8, 1);
    st->ctxt = c = GSF(p, 9, 1);
    st->fix = GSF(p, 10, 1);

    st->tex0 = gsRegs.tex0[c];
    st->tex1 = gsRegs.tex1[c];
    st->clamp = gsRegs.clamp[c];
    st->alpha = gsRegs.alpha[c];
    st->test = gsRegs.test[c];
    st->fba = gsRegs.fba[c];
    st->frame = gsRegs.frame[c];
    st->zbuf = gsRegs.zbuf[c];
    st->scissor = gsRegs.scissor[c];
    st->texa = gsRegs.texa;
    st->fogcol = gsRegs.fogcol;
    st->pabe = gsRegs.pabe;
    st->dthe = gsRegs.dthe;
    st->colclamp = gsRegs.colclamp;
    st->texclut = gsRegs.texclut;
}

void gsFlushPrims(void)
{
    if (out_valid && (out_count > 0))
    {
        gsRendererDraw(&out_state, out_verts, out_count);
    }

    out_count = 0;
    out_valid = 0;
}

static void emit(const GsVertex* v, int n)
{
    GsDrawState st;
    int i;

    current_state(&st);

    /* sprites, points and lines become separate batches by type */
    if (out_valid && ((memcmp(&st, &out_state, sizeof(st)) != 0) || (out_count + n > (int)(sizeof(out_verts) / sizeof(out_verts[0])))))
    {
        gsFlushPrims();
    }

    if (!out_valid)
    {
        out_state = st;
        out_valid = 1;
    }

    for (i = 0; i < n; i++)
    {
        out_verts[out_count++] = v[i];
    }
}

static void latch_vertex(u64 xyz, int has_f, int kick)
{
    int c = GSF(gsRegs.prmodecont, 0, 1) ? GSF(gsRegs.prim, 9, 1) : GSF(gsRegs.prmode, 9, 1);
    GsVertex v;
    int type = GSF(gsRegs.prim, 0, 3);

    v.x = ((int)GSF(xyz, 0, 16) - (int)GSF(gsRegs.xyoffset[c], 0, 16)) / 16.0f;
    v.y = ((int)GSF(xyz, 16, 16) - (int)GSF(gsRegs.xyoffset[c], 32, 16)) / 16.0f;
    v.z = has_f ? GSF(xyz, 32, 24) : GSF(xyz, 32, 32);
    v.f = has_f ? (unsigned char)GSF(xyz, 56, 8) : (unsigned char)GSF(gsRegs.fog, 56, 8);

    v.r = (unsigned char)GSF(gsRegs.rgbaq, 0, 8);
    v.g = (unsigned char)GSF(gsRegs.rgbaq, 8, 8);
    v.b = (unsigned char)GSF(gsRegs.rgbaq, 16, 8);
    v.a = (unsigned char)GSF(gsRegs.rgbaq, 24, 8);
    v.q = gsRegs.q;
    {
        unsigned int s = GSF(gsRegs.st, 0, 32), t = GSF(gsRegs.st, 32, 32);

        v.s = *(float*)&s;
        v.t = *(float*)&t;
    }
    v.u = GSF(gsRegs.uv, 0, 14) / 16.0f;
    v.v = GSF(gsRegs.uv, 16, 14) / 16.0f;

    vq[0] = vq[1];
    vq[1] = vq[2];
    vq[2] = v;

    if (vq_count == 0)
    {
        fan_first = v;
    }

    vq_count++;

    if (!kick)
    {
        return;
    }

    kicks++;

    switch (type)
    {
    case 0: /* point */
        emit(&vq[2], 1);
        break;
    case 1: /* line */
        if ((vq_count & 1) == 0)
        {
            emit(&vq[1], 2);
        }
        break;
    case 2: /* line strip */
        if (vq_count >= 2)
        {
            emit(&vq[1], 2);
        }
        break;
    case 3: /* triangle */
        if ((vq_count % 3) == 0)
        {
            emit(&vq[0], 3);
        }
        break;
    case 4: /* triangle strip */
        if (vq_count >= 3)
        {
            emit(&vq[0], 3);
        }
        break;
    case 5: /* triangle fan */
        if (vq_count >= 3)
        {
            GsVertex t[3];

            t[0] = fan_first;
            t[1] = vq[1];
            t[2] = vq[2];
            emit(t, 3);
        }
        break;
    case 6: /* sprite */
        if ((vq_count & 1) == 0)
        {
            emit(&vq[1], 2);
        }
        break;
    }
}

/* ------------------------------------------------------------------------------------------- */
/* registers */

static void clut_load(u64 tex0);

void gsWriteReg(int reg, u64 value)
{
    switch (reg)
    {
    case 0x00: gsRegs.prim = value; vq_count = 0; break;
    case 0x01: gsRegs.rgbaq = value; { unsigned int q = GSF(value, 32, 32); gsRegs.q = *(float*)&q; } break;
    case 0x02: gsRegs.st = value; break;
    case 0x03: gsRegs.uv = value; break;
    case 0x04: latch_vertex(value, 1, 1); break;
    case 0x05: latch_vertex(value, 0, 1); break;
    case 0x06: gsRegs.tex0[0] = value; clut_load(value); break;
    case 0x07: gsRegs.tex0[1] = value; clut_load(value); break;
    case 0x08: gsRegs.clamp[0] = value; break;
    case 0x09: gsRegs.clamp[1] = value; break;
    case 0x0a: gsRegs.fog = value; break;
    case 0x0c: latch_vertex(value, 1, 0); break;
    case 0x0d: latch_vertex(value, 0, 0); break;
    case 0x14: gsRegs.tex1[0] = value; break;
    case 0x15: gsRegs.tex1[1] = value; break;
    case 0x16: /* TEX2: replaces PSM and CLUT fields of TEX0 */
    case 0x17:
    {
        int c = reg - 0x16;
        u64 mask = (0x3FULL << 20) | (0xFFFFFFFULL << 37);

        gsRegs.tex2[c] = value;
        gsRegs.tex0[c] = (gsRegs.tex0[c] & ~mask) | (value & mask);
        clut_load(gsRegs.tex0[c] | (value & (7ULL << 61)));
        break;
    }
    case 0x18: gsRegs.xyoffset[0] = value; break;
    case 0x19: gsRegs.xyoffset[1] = value; break;
    case 0x1a: gsRegs.prmodecont = value; break;
    case 0x1b: gsRegs.prmode = value; break;
    case 0x1c: gsRegs.texclut = value; break;
    case 0x22: gsRegs.scanmsk = value; break;
    case 0x3b: gsRegs.texa = value; break;
    case 0x3d: gsRegs.fogcol = value; break;
    case 0x3f: break; /* TEXFLUSH */
    case 0x40: gsRegs.scissor[0] = value; break;
    case 0x41: gsRegs.scissor[1] = value; break;
    case 0x42: gsRegs.alpha[0] = value; break;
    case 0x43: gsRegs.alpha[1] = value; break;
    case 0x44: gsRegs.dimx = value; break;
    case 0x45: gsRegs.dthe = value; break;
    case 0x46: gsRegs.colclamp = value; break;
    case 0x47: gsRegs.test[0] = value; break;
    case 0x48: gsRegs.test[1] = value; break;
    case 0x49: gsRegs.pabe = value; break;
    case 0x4a: gsRegs.fba[0] = value; break;
    case 0x4b: gsRegs.fba[1] = value; break;
    case 0x4c: gsRegs.frame[0] = value; break;
    case 0x4d: gsRegs.frame[1] = value; break;
    case 0x4e: gsRegs.zbuf[0] = value; break;
    case 0x4f: gsRegs.zbuf[1] = value; break;
    case 0x50: gsRegs.bitbltbuf = value; break;
    case 0x51: gsRegs.trxpos = value; break;
    case 0x52: gsRegs.trxreg = value; break;
    case 0x53:
        gsRegs.trxdir = value;
        gsFlushPrims();
        gsMemStartTransfer(&gsRegs);
        break;
    default:
        break;
    }
}

/* ------------------------------------------------------------------------------------------- */
/* GIF (PATH3) */

static struct {
    int loop_left;      /* remaining NLOOP */
    int nreg;
    int flg;
    int reg_index;
    u64 regs;
    int eop;
    int in_packet;
} gif;

static void packed_write(int reg, const unsigned int* w)
{
    u64 lo = ((u64)w[1] << 32) | w[0];

    switch (reg)
    {
    case 0x00: /* PRIM */
        gsWriteReg(0x00, lo & 0x7FF);
        break;
    case 0x01: /* RGBAQ: Q comes from the last packed ST */
    {
        u64 v = (w[0] & 0xFF) | ((w[1] & 0xFF) << 8) | ((w[2] & 0xFF) << 16) | ((u64)(w[3] & 0xFF) << 24);
        float q = gsRegs.q;

        gsRegs.rgbaq = v | ((u64)(*(unsigned int*)&q) << 32);
        break;
    }
    case 0x02: /* ST, sets Q */
        gsRegs.st = lo;
        gsRegs.q = *(const float*)&w[2];
        break;
    case 0x03: /* UV */
        gsRegs.uv = (w[0] & 0x3FFF) | ((u64)(w[1] & 0x3FFF) << 16);
        break;
    case 0x04: /* XYZF2 (ADC bit -> XYZF3) */
    {
        u64 v = (w[0] & 0xFFFF) | ((u64)(w[1] & 0xFFFF) << 16) | ((u64)((w[2] >> 4) & 0xFFFFFF) << 32) |
                ((u64)((w[3] >> 4) & 0xFF) << 56);

        gsWriteReg((w[3] & 0x8000) ? 0x0c : 0x04, v);
        break;
    }
    case 0x05: /* XYZ2 (ADC bit -> XYZ3) */
    {
        u64 v = (w[0] & 0xFFFF) | ((u64)(w[1] & 0xFFFF) << 16) | ((u64)w[2] << 32);

        gsWriteReg((w[3] & 0x8000) ? 0x0d : 0x05, v);
        break;
    }
    case 0x0a: /* FOG */
        gsWriteReg(0x0a, (u64)((w[3] >> 4) & 0xFF) << 56);
        break;
    case 0x0e: /* A+D */
        gsWriteReg(w[2] & 0xFF, lo);
        break;
    case 0x0f: /* NOP */
        break;
    default:
        gsWriteReg(reg, lo);
        break;
    }
}

void gsGifTransfer(const void* data, int qwc)
{
    const unsigned int* q = (const unsigned int*)data;

    while (qwc > 0)
    {
        if (!gif.in_packet)
        {
            u64 tag = ((u64)q[1] << 32) | q[0];

            gif.loop_left = GSF(tag, 0, 15);
            gif.eop = GSF(tag, 15, 1);
            gif.flg = GSF(tag, 58, 2);
            gif.nreg = GSF(tag, 60, 4);
            gif.regs = ((u64)q[3] << 32) | q[2];
            gif.reg_index = 0;

            if (gif.nreg == 0)
            {
                gif.nreg = 16;
            }

            if (GSF(tag, 46, 1) && (gif.flg == 0))
            {
                gsWriteReg(0x00, GSF(tag, 47, 11));
            }

            q += 4;
            qwc--;

            gif.in_packet = (gif.loop_left > 0);
            continue;
        }

        switch (gif.flg)
        {
        case 0: /* PACKED: one register per qword */
            packed_write((int)((gif.regs >> (gif.reg_index * 4)) & 15), q);
            q += 4;
            qwc--;

            if (++gif.reg_index >= gif.nreg)
            {
                gif.reg_index = 0;

                if (--gif.loop_left == 0)
                {
                    gif.in_packet = 0;
                }
            }
            break;

        case 1: /* REGLIST: two 64-bit register values per qword */
        {
            int half;

            for (half = 0; (half < 2) && gif.in_packet; half++)
            {
                int reg = (int)((gif.regs >> (gif.reg_index * 4)) & 15);
                u64 v = ((u64)q[half * 2 + 1] << 32) | q[half * 2];

                if (reg != 0x0e && reg != 0x0f)
                {
                    gsWriteReg(reg, v);
                }

                if (++gif.reg_index >= gif.nreg)
                {
                    gif.reg_index = 0;

                    if (--gif.loop_left == 0)
                    {
                        gif.in_packet = 0;   /* rest of the qword is padding */
                    }
                }
            }

            q += 4;
            qwc--;
            break;
        }

        default: /* IMAGE / disabled: NLOOP qwords of transfer data */
        {
            int n = (gif.loop_left < qwc) ? gif.loop_left : qwc;

            gsMemTransferData((const unsigned char*)q, n);
            q += n * 4;
            qwc -= n;
            gif.loop_left -= n;

            if (gif.loop_left == 0)
            {
                gif.in_packet = 0;
            }
            break;
        }
        }
    }
}

/* ------------------------------------------------------------------------------------------- */
/* DMA channel 2 (GIF) source chain */

extern unsigned char* pcScratchpad;

static const void* dma_ptr(unsigned int addr, int spr)
{
    if (spr || ((addr & 0xFFFFC000) == 0x70000000))
    {
        return pcScratchpad + (addr & 0x3FF0);
    }

    return (const void*)(size_t)addr;
}

void gsDmaChain(const void* tag_ptr)
{
    int ntags = 0, nqwc = 0, k0 = kicks;

    unsigned int tag_addr = (unsigned int)(size_t)tag_ptr;
    unsigned int stack[2];
    int sp = 0;
    int guard = 0;

    for (;;)
    {
        int tag_spr = (tag_addr & 0x80000000) != 0;
        const unsigned int* tag = (const unsigned int*)dma_ptr(tag_addr & 0x7FFFFFFF, tag_spr);
        unsigned int qwc = tag[0] & 0xFFFF;
        unsigned int id = (tag[0] >> 28) & 7;
        unsigned int addr = tag[1] & 0x7FFFFFFF;
        int spr = (tag[1] & 0x80000000) != 0;
        unsigned int next = tag_addr + 16;
        int end = 0;

        if (++guard > 1000000)
        {
            pcLog("gs: DMA chain runaway, aborting");
            return;
        }

        switch (id)
        {
        case 0: /* refe */
            gsGifTransfer(dma_ptr(addr, spr), qwc);
            end = 1;
            break;
        case 1: /* cnt */
            gsGifTransfer(tag + 4, qwc);
            next = tag_addr + 16 + qwc * 16;
            break;
        case 2: /* next */
            gsGifTransfer(tag + 4, qwc);
            next = addr | (spr ? 0x80000000 : 0);
            break;
        case 3: /* ref */
        case 4: /* refs */
            gsGifTransfer(dma_ptr(addr, spr), qwc);
            break;
        case 5: /* call */
            gsGifTransfer(tag + 4, qwc);

            if (sp < 2)
            {
                stack[sp++] = tag_addr + 16 + qwc * 16;
            }

            next = addr | (spr ? 0x80000000 : 0);
            break;
        case 6: /* ret */
            gsGifTransfer(tag + 4, qwc);

            if (sp > 0)
            {
                next = stack[--sp];
            }
            else
            {
                end = 1;
            }
            break;
        case 7: /* end */
            gsGifTransfer(tag + 4, qwc);
            end = 1;
            break;
        }

        ntags++;
        nqwc += qwc;

        if (end)
        {
            if (gsDumpFrame)
            {
                pcLog("dma chain %08x: %d tags, %d qwc, %d kicks, last id %u", (unsigned int)(size_t)tag_ptr, ntags, nqwc, kicks - k0, id);
            }

            return;
        }

        tag_addr = next;
    }
}

/* ------------------------------------------------------------------------------------------- */
/* texture decoding */

static unsigned int expand16(unsigned int c, u64 texa)
{
    unsigned int r = (c & 31) << 3, g = ((c >> 5) & 31) << 3, b = ((c >> 10) & 31) << 3;
    unsigned int a;

    if (c & 0x8000)
    {
        a = GSF(texa, 32, 8);
    }
    else
    {
        a = (GSF(texa, 15, 1) && ((c & 0x7FFF) == 0)) ? 0 : GSF(texa, 0, 8);
    }

    return r | (g << 8) | (b << 16) | (a << 24);
}

static unsigned int expand24(unsigned int c, u64 texa)
{
    unsigned int a = (GSF(texa, 15, 1) && ((c & 0xFFFFFF) == 0)) ? 0 : GSF(texa, 0, 8);

    return (c & 0xFFFFFF) | (a << 24);
}

/* GS CLUT buffer: 512 16-bit halves. A CT32 entry e keeps its low half at e and its high half
 * at e + 256; CT16 entries use one half each. Palettes are copied in from GS memory when TEX0
 * or TEX2 is written (per CLD) and stay there until the next load, so a texture can use a
 * palette loaded earlier at another CSA offset. */
static unsigned short clut_buf[512];
static unsigned int clut_cbp0, clut_cbp1;

static int clut_entries(unsigned int psm)
{
    if ((psm == GS_PSMT8) || (psm == GS_PSMT8H)) return 256;
    if ((psm == GS_PSMT4) || (psm == GS_PSMT4HL) || (psm == GS_PSMT4HH)) return 16;
    return 0;
}

static void clut_load(u64 tex0)
{
    unsigned int psm = GSF(tex0, 20, 6);
    unsigned int cbp = GSF(tex0, 37, 14);
    unsigned int cpsm = GSF(tex0, 51, 4);
    unsigned int csm = GSF(tex0, 55, 1);
    unsigned int csa = GSF(tex0, 56, 5);
    unsigned int cld = GSF(tex0, 61, 3);
    int entries = clut_entries(psm);
    int i, load;

    if (entries == 0)
    {
        return;
    }

    switch (cld)
    {
    case 1: load = 1; break;
    case 2: load = 1; clut_cbp0 = cbp; break;
    case 3: load = 1; clut_cbp1 = cbp; break;
    case 4: load = (cbp != clut_cbp0); clut_cbp0 = cbp; break;
    case 5: load = (cbp != clut_cbp1); clut_cbp1 = cbp; break;
    default: load = 0; break;
    }

    if (!load)
    {
        return;
    }

    /* primitives already queued use the old palette */
    gsFlushPrims();

    for (i = 0; i < entries; i++)
    {
        unsigned int x, y, bw = 1, e = ((entries == 16) ? csa * 16 : 0) + i;

        if (csm == 0)
        {
            if (entries == 256)
            {
                /* CSM1: bits 3 and 4 of the index are swapped in the 16x16 layout */
                unsigned int j = (i & 0xE7) | ((i & 0x08) << 1) | ((i & 0x10) >> 1);

                x = j & 15;
                y = j >> 4;
            }
            else
            {
                x = i & 7;
                y = i >> 3;
            }
        }
        else
        {
            bw = GSF(gsRegs.texclut, 0, 6);
            x = GSF(gsRegs.texclut, 6, 6) * 16 + i;
            y = GSF(gsRegs.texclut, 12, 10);
        }

        if ((cpsm == 0) || (cpsm == 1))
        {
            unsigned int c = gsMemRead(GS_PSMCT32, cbp, bw, x, y);

            e &= 255;
            clut_buf[e] = (unsigned short)c;
            clut_buf[e + 256] = (unsigned short)(c >> 16);
        }
        else
        {
            clut_buf[e & 511] = (unsigned short)gsMemRead((cpsm == 10) ? GS_PSMCT16S : GS_PSMCT16, cbp, bw, x, y);
        }
    }
}

static void read_clut(u64 tex0, u64 texa, int entries, unsigned int* clut)
{
    unsigned int cpsm = GSF(tex0, 51, 4);
    unsigned int base = (entries == 16) ? GSF(tex0, 56, 5) * 16 : 0;
    int i;

    for (i = 0; i < entries; i++)
    {
        unsigned int e = base + i;

        if ((cpsm == 0) || (cpsm == 1))
        {
            unsigned int c;

            e &= 255;
            c = clut_buf[e] | ((unsigned int)clut_buf[e + 256] << 16);
            clut[i] = (cpsm == 1) ? expand24(c, texa) : c;
        }
        else
        {
            clut[i] = expand16(clut_buf[e & 511], texa);
        }
    }
}

/* hash of the palette entries a texture would use (texture cache key) */
u64 gsClutHash(u64 tex0)
{
    int entries = clut_entries(GSF(tex0, 20, 6));
    unsigned int base = (entries == 16) ? GSF(tex0, 56, 5) * 16 : 0;
    u64 h = 1469598103934665603ULL;
    int i;

    for (i = 0; i < entries; i++)
    {
        unsigned int e = (base + i) & 511;

        h = (h ^ clut_buf[e]) * 1099511628211ULL;
        h = (h ^ clut_buf[(e + 256) & 511]) * 1099511628211ULL;
    }

    return h;
}

void gsDecodeTexture(u64 tex0, u64 texa, u64 texclut, int w, int h, unsigned int* rgba)
{
    unsigned int tbp = GSF(tex0, 0, 14);
    unsigned int tbw = GSF(tex0, 14, 6);
    unsigned int psm = GSF(tex0, 20, 6);
    unsigned int clut[256];
    int x, y;

    if ((psm == GS_PSMT8) || (psm == GS_PSMT8H))
    {
        read_clut(tex0, texa, 256, clut);
    }
    else if ((psm == GS_PSMT4) || (psm == GS_PSMT4HL) || (psm == GS_PSMT4HH))
    {
        read_clut(tex0, texa, 16, clut);
    }

    for (y = 0; y < h; y++)
    {
        for (x = 0; x < w; x++)
        {
            unsigned int c = gsMemRead(psm, tbp, tbw, x, y);

            switch (psm)
            {
            case GS_PSMCT32:
                break;
            case GS_PSMCT24:
                c = expand24(c, texa);
                break;
            case GS_PSMCT16:
            case GS_PSMCT16S:
                c = expand16(c, texa);
                break;
            case GS_PSMT8:
            case GS_PSMT8H:
            case GS_PSMT4:
            case GS_PSMT4HL:
            case GS_PSMT4HH:
                c = clut[c];
                break;
            default:
                c = 0xFFFF00FF;
                break;
            }

            rgba[y * w + x] = c;
        }
    }
}

/* ------------------------------------------------------------------------------------------- */
/* public entry points (pc_gs.h) */

unsigned char* pcScratchpad;

static unsigned int disp_fbp, disp_fbw;

void pcGsInit(unsigned char* scratchpad)
{
    pcScratchpad = scratchpad;
    gsMemInit();
    gsRendererInit();
}

void pcGsSetDisplay(unsigned int fbp, unsigned int fbw)
{
    disp_fbp = fbp;
    disp_fbw = fbw;
}

void pcGsPresent(void)
{
    gsFlushPrims();
    gsRendererPresent(disp_fbp, disp_fbw);
}

void gsMemReadImageRegs(uint64_t bitbltbuf, uint64_t trxpos, uint64_t trxreg, void* out)
{
    GsRegs r = gsRegs;

    gsFlushPrims();

    r.bitbltbuf = bitbltbuf;
    r.trxpos = trxpos;
    r.trxreg = trxreg;

    gsMemReadImage(&r, (unsigned char*)out);
}

unsigned int pcGsDisplayFbp(void)
{
    return disp_fbp;
}
