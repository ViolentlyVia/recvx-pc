/* PC port, GS emulator: local memory (4 MB) with the GS pixel storage formats.
 *
 * Memory is 512 pages of 8 KB; a page holds 32 blocks of 256 bytes. The position of a pixel
 * depends on its storage format (PSM): pages, blocks and the "columns" inside a block are
 * swizzled differently per format. Texture data is sometimes written in one format and read
 * in another (the game pre-converts 4/8-bit textures and uploads them as 32-bit), so the
 * layouts must match the real hardware; gsMemSelfTest() checks them against the game's own
 * conversion routines (ps2_pxlconv.c).
 */
#include <string.h>

#include "gs_internal.h"

unsigned char gsVram[GS_VRAM_SIZE];
unsigned int gsPageGen[GS_PAGES];

static const unsigned char blockTable32[4][8] = {
    {  0,  1,  4,  5, 16, 17, 20, 21 },
    {  2,  3,  6,  7, 18, 19, 22, 23 },
    {  8,  9, 12, 13, 24, 25, 28, 29 },
    { 10, 11, 14, 15, 26, 27, 30, 31 },
};

static const unsigned char blockTable32Z[4][8] = {
    { 24, 25, 28, 29,  8,  9, 12, 13 },
    { 26, 27, 30, 31, 10, 11, 14, 15 },
    { 16, 17, 20, 21,  0,  1,  4,  5 },
    { 18, 19, 22, 23,  2,  3,  6,  7 },
};

static const unsigned char blockTable16[8][4] = {
    {  0,  2,  8, 10 },
    {  1,  3,  9, 11 },
    {  4,  6, 12, 14 },
    {  5,  7, 13, 15 },
    { 16, 18, 24, 26 },
    { 17, 19, 25, 27 },
    { 20, 22, 28, 30 },
    { 21, 23, 29, 31 },
};

static const unsigned char blockTable16S[8][4] = {
    {  0,  2, 16, 18 },
    {  1,  3, 17, 19 },
    {  8, 10, 24, 26 },
    {  9, 11, 25, 27 },
    {  4,  6, 20, 22 },
    {  5,  7, 21, 23 },
    { 12, 14, 28, 30 },
    { 13, 15, 29, 31 },
};

static const unsigned char blockTable16Z[8][4] = {
    { 24, 26, 16, 18 },
    { 25, 27, 17, 19 },
    { 28, 30, 20, 22 },
    { 29, 31, 21, 23 },
    {  8, 10,  0,  2 },
    {  9, 11,  1,  3 },
    { 12, 14,  4,  6 },
    { 13, 15,  5,  7 },
};

static const unsigned char blockTable16SZ[8][4] = {
    { 24, 26,  8, 10 },
    { 25, 27,  9, 11 },
    { 16, 18,  0,  2 },
    { 17, 19,  1,  3 },
    { 28, 30, 12, 14 },
    { 29, 31, 13, 15 },
    { 20, 22,  4,  6 },
    { 21, 23,  5,  7 },
};

static const unsigned char blockTable8[4][8] = {
    {  0,  1,  4,  5, 16, 17, 20, 21 },
    {  2,  3,  6,  7, 18, 19, 22, 23 },
    {  8,  9, 12, 13, 24, 25, 28, 29 },
    { 10, 11, 14, 15, 26, 27, 30, 31 },
};

static const unsigned char blockTable4[8][4] = {
    {  0,  2,  8, 10 },
    {  1,  3,  9, 11 },
    {  4,  6, 12, 14 },
    {  5,  7, 13, 15 },
    { 16, 18, 24, 26 },
    { 17, 19, 25, 27 },
    { 20, 22, 28, 30 },
    { 21, 23, 29, 31 },
};

/* word index inside a block (8x8 pixels) */
static const unsigned char columnTable32[8][8] = {
    {  0,  1,  4,  5,  8,  9, 12, 13 },
    {  2,  3,  6,  7, 10, 11, 14, 15 },
    { 16, 17, 20, 21, 24, 25, 28, 29 },
    { 18, 19, 22, 23, 26, 27, 30, 31 },
    { 32, 33, 36, 37, 40, 41, 44, 45 },
    { 34, 35, 38, 39, 42, 43, 46, 47 },
    { 48, 49, 52, 53, 56, 57, 60, 61 },
    { 50, 51, 54, 55, 58, 59, 62, 63 },
};

/* halfword index inside a block (16x8 pixels) */
static const unsigned char columnTable16[8][16] = {
    {   0,   2,   8,  10,  16,  18,  24,  26,   1,   3,   9,  11,  17,  19,  25,  27 },
    {   4,   6,  12,  14,  20,  22,  28,  30,   5,   7,  13,  15,  21,  23,  29,  31 },
    {  32,  34,  40,  42,  48,  50,  56,  58,  33,  35,  41,  43,  49,  51,  57,  59 },
    {  36,  38,  44,  46,  52,  54,  60,  62,  37,  39,  45,  47,  53,  55,  61,  63 },
    {  64,  66,  72,  74,  80,  82,  88,  90,  65,  67,  73,  75,  81,  83,  89,  91 },
    {  68,  70,  76,  78,  84,  86,  92,  94,  69,  71,  77,  79,  85,  87,  93,  95 },
    {  96,  98, 104, 106, 112, 114, 120, 122,  97,  99, 105, 107, 113, 115, 121, 123 },
    { 100, 102, 108, 110, 116, 118, 124, 126, 101, 103, 109, 111, 117, 119, 125, 127 },
};

/* byte index inside a block (16x16 pixels) */
static unsigned char columnTable8[16][16];
/* nibble index inside a block (32x16 pixels) */
static unsigned short columnTable4[16][32];

static const unsigned char columnTable8Base[8][16] = {
    {   0,   4,  16,  20,  32,  36,  48,  52,   2,   6,  18,  22,  34,  38,  50,  54 },
    {   8,  12,  24,  28,  40,  44,  56,  60,  10,  14,  26,  30,  42,  46,  58,  62 },
    {  33,  37,  49,  53,   1,   5,  17,  21,  35,  39,  51,  55,   3,   7,  19,  23 },
    {  41,  45,  57,  61,   9,  13,  25,  29,  43,  47,  59,  63,  11,  15,  27,  31 },
    {  96, 100, 112, 116,  64,  68,  80,  84,  98, 102, 114, 118,  66,  70,  82,  86 },
    { 104, 108, 120, 124,  72,  76,  88,  92, 106, 110, 122, 126,  74,  78,  90,  94 },
    {  65,  69,  81,  85,  97, 101, 113, 117,  67,  71,  83,  87,  99, 103, 115, 119 },
    {  73,  77,  89,  93, 105, 109, 121, 125,  75,  79,  91,  95, 107, 111, 123, 127 },
};

static const unsigned short columnTable4Base[8][32] = {
    {   0,   8,  32,  40,  64,  72,  96, 104,   2,  10,  34,  42,  66,  74,  98, 106,
        4,  12,  36,  44,  68,  76, 100, 108,   6,  14,  38,  46,  70,  78, 102, 110 },
    {  16,  24,  48,  56,  80,  88, 112, 120,  18,  26,  50,  58,  82,  90, 114, 122,
       20,  28,  52,  60,  84,  92, 116, 124,  22,  30,  54,  62,  86,  94, 118, 126 },
    {  65,  73,  97, 105,   1,   9,  33,  41,  67,  75,  99, 107,   3,  11,  35,  43,
       69,  77, 101, 109,   5,  13,  37,  45,  71,  79, 103, 111,   7,  15,  39,  47 },
    {  81,  89, 113, 121,  17,  25,  49,  57,  83,  91, 115, 123,  19,  27,  51,  59,
       85,  93, 117, 125,  21,  29,  53,  61,  87,  95, 119, 127,  23,  31,  55,  63 },
    { 192, 200, 224, 232, 128, 136, 160, 168, 194, 202, 226, 234, 130, 138, 162, 170,
      196, 204, 228, 236, 132, 140, 164, 172, 198, 206, 230, 238, 134, 142, 166, 174 },
    { 208, 216, 240, 248, 144, 152, 176, 184, 210, 218, 242, 250, 146, 154, 178, 186,
      212, 220, 244, 252, 148, 156, 180, 188, 214, 222, 246, 254, 150, 158, 182, 190 },
    { 129, 137, 161, 169, 193, 201, 225, 233, 131, 139, 163, 171, 195, 203, 227, 235,
      133, 141, 165, 173, 197, 205, 229, 237, 135, 143, 167, 175, 199, 207, 231, 239 },
    { 145, 153, 177, 185, 209, 217, 241, 249, 147, 155, 179, 187, 211, 219, 243, 251,
      149, 157, 181, 189, 213, 221, 245, 253, 151, 159, 183, 191, 215, 223, 247, 255 },
};

void gsMemInit(void)
{
    int y, x;

    for (y = 0; y < 16; y++)
    {
        for (x = 0; x < 16; x++)
        {
            columnTable8[y][x] = columnTable8Base[y & 7][x] + ((y & 8) ? 128 : 0);
        }

        for (x = 0; x < 32; x++)
        {
            columnTable4[y][x] = columnTable4Base[y & 7][x] + ((y & 8) ? 256 : 0);
        }
    }

    memset(gsVram, 0, sizeof(gsVram));
}

/* ---- address functions: return a byte (or nibble for 4-bit) offset into gsVram ---- */

/* bp: base in 256-byte blocks, bw: width in 64-pixel units */
static unsigned int addr32(unsigned int bp, unsigned int bw, unsigned int x, unsigned int y, const unsigned char bt[4][8])
{
    unsigned int page = (y >> 5) * bw + (x >> 6);
    unsigned int block = bp + page * 32 + bt[(y >> 3) & 3][(x >> 3) & 7];

    return ((block * 64 + columnTable32[y & 7][x & 7]) * 4) & (GS_VRAM_SIZE - 1);
}

static unsigned int addr16(unsigned int bp, unsigned int bw, unsigned int x, unsigned int y, const unsigned char bt[8][4])
{
    unsigned int page = (y >> 6) * bw + (x >> 6);
    unsigned int block = bp + page * 32 + bt[(y >> 3) & 7][(x >> 4) & 3];

    return ((block * 128 + columnTable16[y & 7][x & 15]) * 2) & (GS_VRAM_SIZE - 1);
}

static unsigned int addr8(unsigned int bp, unsigned int bw, unsigned int x, unsigned int y)
{
    unsigned int page = (y >> 6) * (bw >> 1) + (x >> 7);
    unsigned int block = bp + page * 32 + blockTable8[(y >> 4) & 3][(x >> 4) & 7];

    return (block * 256 + columnTable8[y & 15][x & 15]) & (GS_VRAM_SIZE - 1);
}

/* nibble address */
static unsigned int addr4(unsigned int bp, unsigned int bw, unsigned int x, unsigned int y)
{
    unsigned int page = (y >> 7) * (bw >> 1) + (x >> 7);
    unsigned int block = bp + page * 32 + blockTable4[(y >> 4) & 7][(x >> 5) & 3];

    return (block * 512 + columnTable4[y & 15][x & 31]) & (GS_VRAM_SIZE * 2 - 1);
}

static void touch(unsigned int byte_addr)
{
    gsPageGen[(byte_addr >> 13) & (GS_PAGES - 1)]++;
}

unsigned int gsMemRead(int psm, unsigned int bp, unsigned int bw, unsigned int x, unsigned int y)
{
    unsigned int a;

    switch (psm)
    {
    case GS_PSMCT32:
    case GS_PSMCT24:
        return *(unsigned int*)&gsVram[addr32(bp, bw, x, y, blockTable32)];
    case GS_PSMZ32:
    case GS_PSMZ24:
        return *(unsigned int*)&gsVram[addr32(bp, bw, x, y, blockTable32Z)];
    case GS_PSMCT16:
        return *(unsigned short*)&gsVram[addr16(bp, bw, x, y, blockTable16)];
    case GS_PSMCT16S:
        return *(unsigned short*)&gsVram[addr16(bp, bw, x, y, blockTable16S)];
    case GS_PSMZ16:
        return *(unsigned short*)&gsVram[addr16(bp, bw, x, y, blockTable16Z)];
    case GS_PSMZ16S:
        return *(unsigned short*)&gsVram[addr16(bp, bw, x, y, blockTable16SZ)];
    case GS_PSMT8:
        return gsVram[addr8(bp, bw, x, y)];
    case GS_PSMT4:
        a = addr4(bp, bw, x, y);
        return (gsVram[a >> 1] >> ((a & 1) * 4)) & 15;
    case GS_PSMT8H:
        return gsVram[addr32(bp, bw, x, y, blockTable32) + 3];
    case GS_PSMT4HL:
        return gsVram[addr32(bp, bw, x, y, blockTable32) + 3] & 15;
    case GS_PSMT4HH:
        return gsVram[addr32(bp, bw, x, y, blockTable32) + 3] >> 4;
    }

    return 0;
}

/* Writes one pixel; returns nonzero if GS memory changed (the page generation and render
 * target bookkeeping only move on real changes, so re-uploading identical textures is free). */
static int mem_write(int psm, unsigned int bp, unsigned int bw, unsigned int x, unsigned int y, unsigned int v)
{
    unsigned int a, old;

    switch (psm)
    {
    case GS_PSMCT32:
    case GS_PSMZ32:
        a = addr32(bp, bw, x, y, (psm == GS_PSMZ32) ? blockTable32Z : blockTable32);
        old = *(unsigned int*)&gsVram[a & ~3u];
        *(unsigned int*)&gsVram[a] = v;
        if (*(unsigned int*)&gsVram[a & ~3u] == old)
        {
            return 0;
        }

        touch(a);
        break;
    case GS_PSMCT24:
    case GS_PSMZ24:
        a = addr32(bp, bw, x, y, (psm == GS_PSMZ24) ? blockTable32Z : blockTable32);
        old = *(unsigned int*)&gsVram[a & ~3u];
        *(unsigned int*)&gsVram[a] = (*(unsigned int*)&gsVram[a] & 0xFF000000) | (v & 0xFFFFFF);
        if (*(unsigned int*)&gsVram[a & ~3u] == old)
        {
            return 0;
        }

        touch(a);
        break;
    case GS_PSMCT16:
        a = addr16(bp, bw, x, y, blockTable16);
        old = *(unsigned int*)&gsVram[a & ~3u];
        *(unsigned short*)&gsVram[a] = (unsigned short)v;
        if (*(unsigned int*)&gsVram[a & ~3u] == old)
        {
            return 0;
        }

        touch(a);
        break;
    case GS_PSMCT16S:
        a = addr16(bp, bw, x, y, blockTable16S);
        old = *(unsigned int*)&gsVram[a & ~3u];
        *(unsigned short*)&gsVram[a] = (unsigned short)v;
        if (*(unsigned int*)&gsVram[a & ~3u] == old)
        {
            return 0;
        }

        touch(a);
        break;
    case GS_PSMZ16:
        a = addr16(bp, bw, x, y, blockTable16Z);
        old = *(unsigned int*)&gsVram[a & ~3u];
        *(unsigned short*)&gsVram[a] = (unsigned short)v;
        if (*(unsigned int*)&gsVram[a & ~3u] == old)
        {
            return 0;
        }

        touch(a);
        break;
    case GS_PSMZ16S:
        a = addr16(bp, bw, x, y, blockTable16SZ);
        old = *(unsigned int*)&gsVram[a & ~3u];
        *(unsigned short*)&gsVram[a] = (unsigned short)v;
        if (*(unsigned int*)&gsVram[a & ~3u] == old)
        {
            return 0;
        }

        touch(a);
        break;
    case GS_PSMT8:
        a = addr8(bp, bw, x, y);
        old = *(unsigned int*)&gsVram[a & ~3u];
        gsVram[a] = (unsigned char)v;
        if (*(unsigned int*)&gsVram[a & ~3u] == old)
        {
            return 0;
        }

        touch(a);
        break;
    case GS_PSMT4:
        a = addr4(bp, bw, x, y);
        old = gsVram[a >> 1];
        gsVram[a >> 1] = (unsigned char)((gsVram[a >> 1] & (0xF0 >> ((a & 1) * 4))) | ((v & 15) << ((a & 1) * 4)));

        if (gsVram[a >> 1] == old)
        {
            return 0;
        }

        touch(a >> 1);
        return 1;
    case GS_PSMT8H:
        a = addr32(bp, bw, x, y, blockTable32) + 3;
        old = *(unsigned int*)&gsVram[a & ~3u];
        gsVram[a] = (unsigned char)v;
        if (*(unsigned int*)&gsVram[a & ~3u] == old)
        {
            return 0;
        }

        touch(a);
        break;
    case GS_PSMT4HL:
        a = addr32(bp, bw, x, y, blockTable32) + 3;
        old = *(unsigned int*)&gsVram[a & ~3u];
        gsVram[a] = (unsigned char)((gsVram[a] & 0xF0) | (v & 15));
        if (*(unsigned int*)&gsVram[a & ~3u] == old)
        {
            return 0;
        }

        touch(a);
        break;
    case GS_PSMT4HH:
        a = addr32(bp, bw, x, y, blockTable32) + 3;
        old = *(unsigned int*)&gsVram[a & ~3u];
        gsVram[a] = (unsigned char)((gsVram[a] & 0x0F) | ((v & 15) << 4));
        if (*(unsigned int*)&gsVram[a & ~3u] == old)
        {
            return 0;
        }

        touch(a);
        break;
    default:
        return 0;
    }

    return 1;
}

void gsMemWrite(int psm, unsigned int bp, unsigned int bw, unsigned int x, unsigned int y, unsigned int v)
{
    mem_write(psm, bp, bw, x, y, v);
}


int gsPsmBits(int psm)
{
    switch (psm)
    {
    case GS_PSMCT32: case GS_PSMCT24: case GS_PSMZ32: case GS_PSMZ24:
    case GS_PSMT8H: case GS_PSMT4HL: case GS_PSMT4HH:
        return 32;
    case GS_PSMCT16: case GS_PSMCT16S: case GS_PSMZ16: case GS_PSMZ16S:
        return 16;
    case GS_PSMT8:
        return 8;
    case GS_PSMT4:
        return 4;
    }

    return 32;
}

/* ---- transfers (BITBLTBUF / TRXPOS / TRXREG / TRXDIR) ---- */

static struct {
    int active;
    int dir;
    unsigned int bp, bw, psm;
    unsigned int sx, sy, w, h;
    unsigned int x, y;
    int changed;
} xfer;

static void advance(void)
{
    if (++xfer.x >= xfer.sx + xfer.w)
    {
        xfer.x = xfer.sx;

        if (++xfer.y >= xfer.sy + xfer.h)
        {
            xfer.active = 0;

            gsXferDone(xfer.bp, xfer.bw, xfer.psm, xfer.sx, xfer.sy, xfer.w, xfer.h, xfer.changed);
        }
    }
}

void gsMemStartTransfer(const GsRegs* r)
{
    unsigned int dir = r->trxdir & 3;

    xfer.dir = dir;

    if (dir == 0)          /* host -> local */
    {
        xfer.bp = (unsigned int)(r->bitbltbuf >> 32) & 0x3FFF;
        xfer.bw = (unsigned int)(r->bitbltbuf >> 48) & 0x3F;
        xfer.psm = (unsigned int)(r->bitbltbuf >> 56) & 0x3F;
        xfer.sx = (unsigned int)(r->trxpos >> 32) & 0x7FF;
        xfer.sy = (unsigned int)(r->trxpos >> 48) & 0x7FF;
        xfer.w = (unsigned int)r->trxreg & 0xFFF;
        xfer.h = (unsigned int)(r->trxreg >> 32) & 0xFFF;
        xfer.x = xfer.sx;
        xfer.y = xfer.sy;
        xfer.active = (xfer.w != 0) && (xfer.h != 0);
        xfer.changed = 0;
    }
    else if (dir == 2)     /* local -> local */
    {
        unsigned int sbp = (unsigned int)r->bitbltbuf & 0x3FFF;
        unsigned int sbw = (unsigned int)(r->bitbltbuf >> 16) & 0x3F;
        unsigned int spsm = (unsigned int)(r->bitbltbuf >> 24) & 0x3F;
        unsigned int dbp = (unsigned int)(r->bitbltbuf >> 32) & 0x3FFF;
        unsigned int dbw = (unsigned int)(r->bitbltbuf >> 48) & 0x3F;
        unsigned int dpsm = (unsigned int)(r->bitbltbuf >> 56) & 0x3F;
        unsigned int ssx = (unsigned int)r->trxpos & 0x7FF;
        unsigned int ssy = (unsigned int)(r->trxpos >> 16) & 0x7FF;
        unsigned int dsx = (unsigned int)(r->trxpos >> 32) & 0x7FF;
        unsigned int dsy = (unsigned int)(r->trxpos >> 48) & 0x7FF;
        unsigned int w = (unsigned int)r->trxreg & 0xFFF;
        unsigned int h = (unsigned int)(r->trxreg >> 32) & 0xFFF;
        unsigned int x, y;

        gsFlushLocalToLocal(sbp, sbw, spsm, ssx, ssy, w, h);

        for (y = 0; y < h; y++)
        {
            for (x = 0; x < w; x++)
            {
                gsMemWrite(dpsm, dbp, dbw, dsx + x, dsy + y, gsMemRead(spsm, sbp, sbw, ssx + x, ssy + y));
            }
        }

        xfer.active = 0;
        gsXferDone(dbp, dbw, dpsm, dsx, dsy, w, h, 1);
    }
    else                   /* local -> host: handled by gsMemReadImage */
    {
        xfer.active = 0;
    }
}

/* host -> local data (IMAGE mode GIF data), qwc quadwords */
void gsMemTransferData(const unsigned char* data, int qwc)
{
    int bytes = qwc * 16;
    int i;

    if (!xfer.active)
    {
        return;
    }

    switch (xfer.psm)
    {
    case GS_PSMCT32:
    case GS_PSMZ32:
        for (i = 0; (i + 4 <= bytes) && xfer.active; i += 4)
        {
            xfer.changed |= mem_write(xfer.psm, xfer.bp, xfer.bw, xfer.x, xfer.y, *(const unsigned int*)&data[i]);
            advance();
        }
        break;
    case GS_PSMCT24:
    case GS_PSMZ24:
        for (i = 0; (i + 3 <= bytes) && xfer.active; i += 3)
        {
            xfer.changed |= mem_write(xfer.psm, xfer.bp, xfer.bw, xfer.x, xfer.y, data[i] | (data[i + 1] << 8) | (data[i + 2] << 16));
            advance();
        }
        break;
    case GS_PSMCT16:
    case GS_PSMCT16S:
    case GS_PSMZ16:
    case GS_PSMZ16S:
        for (i = 0; (i + 2 <= bytes) && xfer.active; i += 2)
        {
            xfer.changed |= mem_write(xfer.psm, xfer.bp, xfer.bw, xfer.x, xfer.y, *(const unsigned short*)&data[i]);
            advance();
        }
        break;
    case GS_PSMT8:
    case GS_PSMT8H:
        for (i = 0; (i < bytes) && xfer.active; i++)
        {
            xfer.changed |= mem_write(xfer.psm, xfer.bp, xfer.bw, xfer.x, xfer.y, data[i]);
            advance();
        }
        break;
    case GS_PSMT4:
    case GS_PSMT4HL:
    case GS_PSMT4HH:
        for (i = 0; (i < bytes) && xfer.active; i++)
        {
            xfer.changed |= mem_write(xfer.psm, xfer.bp, xfer.bw, xfer.x, xfer.y, data[i] & 15);
            advance();

            if (xfer.active)
            {
                xfer.changed |= mem_write(xfer.psm, xfer.bp, xfer.bw, xfer.x, xfer.y, data[i] >> 4);
                advance();
            }
        }
        break;
    }
}

/* local -> host (sceGsExecStoreImage) */
void gsMemReadImage(const GsRegs* r, unsigned char* out)
{
    unsigned int sbp = (unsigned int)r->bitbltbuf & 0x3FFF;
    unsigned int sbw = (unsigned int)(r->bitbltbuf >> 16) & 0x3F;
    unsigned int spsm = (unsigned int)(r->bitbltbuf >> 24) & 0x3F;
    unsigned int ssx = (unsigned int)r->trxpos & 0x7FF;
    unsigned int ssy = (unsigned int)(r->trxpos >> 16) & 0x7FF;
    unsigned int w = (unsigned int)r->trxreg & 0xFFF;
    unsigned int h = (unsigned int)(r->trxreg >> 32) & 0xFFF;
    unsigned int x, y;
    int bits = gsPsmBits(spsm);

    gsFlushLocalToLocal(sbp, sbw, spsm, ssx, ssy, w, h);

    for (y = 0; y < h; y++)
    {
        for (x = 0; x < w; x++)
        {
            unsigned int v = gsMemRead(spsm, sbp, sbw, ssx + x, ssy + y);

            if (bits == 32)
            {
                *(unsigned int*)out = v;
                out += 4;
            }
            else if (bits == 16)
            {
                *(unsigned short*)out = (unsigned short)v;
                out += 2;
            }
            else if (bits == 8)
            {
                *out++ = (unsigned char)v;
            }
        }
    }
}
