/* PC port, GS emulator: shared definitions between gs_mem.c, gs.c and gs_gl.c. */
#pragma once

#include <stdint.h>
typedef uint64_t u64;

#define GS_VRAM_SIZE (4 * 1024 * 1024)
#define GS_PAGES     512

#define GS_PSMCT32  0x00
#define GS_PSMCT24  0x01
#define GS_PSMCT16  0x02
#define GS_PSMCT16S 0x0a
#define GS_PSMT8    0x13
#define GS_PSMT4    0x14
#define GS_PSMT8H   0x1b
#define GS_PSMT4HL  0x24
#define GS_PSMT4HH  0x2c
#define GS_PSMZ32   0x30
#define GS_PSMZ24   0x31
#define GS_PSMZ16   0x32
#define GS_PSMZ16S  0x3a

/* field extraction */
#define GSF(v, shift, bits) ((unsigned int)(((v) >> (shift)) & ((1ULL << (bits)) - 1)))

typedef struct {
    u64 prim, prmode, prmodecont, rgbaq, st, uv, fog;
    u64 tex0[2], tex1[2], tex2[2], clamp[2], xyoffset[2], scissor[2], alpha[2], test[2], fba[2], frame[2], zbuf[2];
    u64 texclut, scanmsk, texa, fogcol, dimx, dthe, colclamp, pabe;
    u64 bitbltbuf, trxpos, trxreg, trxdir;
    float q;            /* internal Q (set by RGBAQ and packed ST) */
} GsRegs;

extern GsRegs gsRegs;

/* vertex as latched by the GS (window coordinates in pixels after XYOFFSET) */
typedef struct {
    float x, y;
    unsigned int z;
    unsigned char r, g, b, a;
    float s, t, q;
    float u, v;         /* texels (FST = 1) */
    unsigned char f;
} GsVertex;

/* everything that decides how a primitive is drawn */
typedef struct {
    int type;           /* SCE_GS_PRIM_* */
    int iip, tme, fge, abe, aa1, fst, ctxt, fix;
    u64 tex0, tex1, clamp, alpha, test, fba, frame, zbuf, scissor;
    u64 texa, fogcol, pabe, dthe, colclamp, texclut;
} GsDrawState;

/* gs_mem.c */
extern unsigned char gsVram[GS_VRAM_SIZE];
extern unsigned int gsPageGen[GS_PAGES];

void gsMemInit(void);
unsigned int gsMemRead(int psm, unsigned int bp, unsigned int bw, unsigned int x, unsigned int y);
void gsMemWrite(int psm, unsigned int bp, unsigned int bw, unsigned int x, unsigned int y, unsigned int v);
int  gsPsmBits(int psm);
int  gsBlockNumber(int psm, unsigned int bp, unsigned int bw, unsigned int x, unsigned int y);
void gsMemStartTransfer(const GsRegs* r);
void gsMemTransferData(const unsigned char* data, int qwc);
void gsMemReadImage(const GsRegs* r, unsigned char* out);

/* gs.c */
void gsWriteReg(int reg, u64 value);
void gsGifTransfer(const void* data, int qwc);
void gsDmaChain(const void* tag);

/* gs_gl.c (renderer) */
void gsRendererInit(void);
void gsRendererDraw(const GsDrawState* st, const GsVertex* v, int nverts);   /* triangle list / line list / points */
void gsRendererPresent(unsigned int dispfb_fbp, unsigned int dispfb_fbw);
void gsRendererFlush(void);

/* VRAM <-> render target coherency */
/* changed: the transfer altered GS memory (render targets that were drawn into are invalidated either way) */
void gsXferDone(unsigned int bp, unsigned int bw, unsigned int psm, unsigned int x, unsigned int y, unsigned int w, unsigned int h, int changed);
void gsFlushLocalToLocal(unsigned int bp, unsigned int bw, unsigned int psm, unsigned int x, unsigned int y, unsigned int w, unsigned int h);

/* texture decoding for the renderer: fills rgba (w*h) from VRAM per TEX0/TEXA/TEXCLUT */
u64 gsClutHash(u64 tex0);
void gsReadClut32(u64 tex0, u64 texa, int entries, unsigned int* clut);   /* palette as the texture sees it */
void gsDecodeTexture(u64 tex0, u64 texa, u64 texclut, int w, int h, unsigned int* rgba);
