/* PC port: stand-in for the PS2 SDK header <libgraph.h>.
 * GS register numbers, field encoders and the libgraph structures/functions the game uses.
 * Everything that reaches the GS ends up in the GS emulator (pc/src/pc_gs*.c). */
#pragma once
#include "sce_pc.h"

typedef uint64_t u_long64_t;
#define GSL(x) ((u_long64_t)(x))

/* ---- GS registers ---- */
#define SCE_GS_PRIM        0x00
#define SCE_GS_RGBAQ       0x01
#define SCE_GS_ST          0x02
#define SCE_GS_UV          0x03
#define SCE_GS_XYZF2       0x04
#define SCE_GS_XYZ2        0x05
#define SCE_GS_TEX0_1      0x06
#define SCE_GS_TEX0_2      0x07
#define SCE_GS_CLAMP_1     0x08
#define SCE_GS_CLAMP_2     0x09
#define SCE_GS_FOG         0x0a
#define SCE_GS_XYZF3       0x0c
#define SCE_GS_XYZ3        0x0d
#define SCE_GS_AD          0x0e
#define SCE_GS_NOP         0x0f
#define SCE_GS_TEX1_1      0x14
#define SCE_GS_TEX1_2      0x15
#define SCE_GS_TEX2_1      0x16
#define SCE_GS_TEX2_2      0x17
#define SCE_GS_XYOFFSET_1  0x18
#define SCE_GS_XYOFFSET_2  0x19
#define SCE_GS_PRMODECONT  0x1a
#define SCE_GS_PRMODE      0x1b
#define SCE_GS_TEXCLUT     0x1c
#define SCE_GS_SCANMSK     0x22
#define SCE_GS_MIPTBP1_1   0x34
#define SCE_GS_MIPTBP1_2   0x35
#define SCE_GS_MIPTBP2_1   0x36
#define SCE_GS_MIPTBP2_2   0x37
#define SCE_GS_TEXA        0x3b
#define SCE_GS_FOGCOL      0x3d
#define SCE_GS_TEXFLUSH    0x3f
#define SCE_GS_SCISSOR_1   0x40
#define SCE_GS_SCISSOR_2   0x41
#define SCE_GS_ALPHA_1     0x42
#define SCE_GS_ALPHA_2     0x43
#define SCE_GS_DIMX        0x44
#define SCE_GS_DTHE        0x45
#define SCE_GS_COLCLAMP    0x46
#define SCE_GS_TEST_1      0x47
#define SCE_GS_TEST_2      0x48
#define SCE_GS_PABE        0x49
#define SCE_GS_FBA_1       0x4a
#define SCE_GS_FBA_2       0x4b
#define SCE_GS_FRAME_1     0x4c
#define SCE_GS_FRAME_2     0x4d
#define SCE_GS_ZBUF_1      0x4e
#define SCE_GS_ZBUF_2      0x4f
#define SCE_GS_BITBLTBUF   0x50
#define SCE_GS_TRXPOS      0x51
#define SCE_GS_TRXREG      0x52
#define SCE_GS_TRXDIR      0x53
#define SCE_GS_HWREG       0x54
#define SCE_GS_SIGNAL      0x60
#define SCE_GS_FINISH      0x61
#define SCE_GS_LABEL       0x62

/* ---- constants ---- */
#define SCE_GS_FALSE 0
#define SCE_GS_TRUE  1

#define SCE_GS_NOINTERLACE 0
#define SCE_GS_INTERLACE   1
#define SCE_GS_FRAME       0
#define SCE_GS_FIELD       1
#define SCE_GS_NTSC        2
#define SCE_GS_PAL         3

#define SCE_GS_PSMCT32  0x00
#define SCE_GS_PSMCT24  0x01
#define SCE_GS_PSMCT16  0x02
#define SCE_GS_PSMCT16S 0x0a
#define SCE_GS_PSMT8    0x13
#define SCE_GS_PSMT4    0x14
#define SCE_GS_PSMT8H   0x1b
#define SCE_GS_PSMT4HL  0x24
#define SCE_GS_PSMT4HH  0x2c
#define SCE_GS_PSMZ32   0x30
#define SCE_GS_PSMZ24   0x31
#define SCE_GS_PSMZ16   0x32
#define SCE_GS_PSMZ16S  0x3a

#define SCE_GS_PRIM_POINT     0
#define SCE_GS_PRIM_LINE      1
#define SCE_GS_PRIM_LINESTRIP 2
#define SCE_GS_PRIM_TRI       3
#define SCE_GS_PRIM_TRISTRIP  4
#define SCE_GS_PRIM_TRIFAN    5
#define SCE_GS_PRIM_SPRITE    6

#define SCE_GS_DEPTH_NEVER   0
#define SCE_GS_DEPTH_ALWAYS  1
#define SCE_GS_DEPTH_GEQUAL  2
#define SCE_GS_DEPTH_GREATER 3

#define SCE_GS_ZNOUSE   0
#define SCE_GS_ZALWAYS  1
#define SCE_GS_ZGEQUAL  2
#define SCE_GS_ZGREATER 3

#define SCE_GS_ALPHA_NEVER    0
#define SCE_GS_ALPHA_ALWAYS   1
#define SCE_GS_ALPHA_LESS     2
#define SCE_GS_ALPHA_LEQUAL   3
#define SCE_GS_ALPHA_EQUAL    4
#define SCE_GS_ALPHA_GEQUAL   5
#define SCE_GS_ALPHA_GREATER  6
#define SCE_GS_ALPHA_NOTEQUAL 7

#define SCE_GS_AFAIL_KEEP     0
#define SCE_GS_AFAIL_FB_ONLY  1
#define SCE_GS_AFAIL_ZB_ONLY  2
#define SCE_GS_AFAIL_RGB_ONLY 3

#define SCE_GS_ALPHA_CS   0
#define SCE_GS_ALPHA_CD   1
#define SCE_GS_ALPHA_ZERO 2
#define SCE_GS_ALPHA_AS   0
#define SCE_GS_ALPHA_AD   1
#define SCE_GS_ALPHA_FIX  2

#define SCE_GS_MODULATE   0
#define SCE_GS_DECAL      1
#define SCE_GS_HIGHLIGHT  2
#define SCE_GS_HIGHLIGHT2 3

#define SCE_GS_NEAREST                0
#define SCE_GS_LINEAR                 1
#define SCE_GS_NEAREST_MIPMAP_NEAREST 2
#define SCE_GS_NEAREST_MIPMAP_LINEAR  3
#define SCE_GS_LINEAR_MIPMAP_NEAREST  4
#define SCE_GS_LINEAR_MIPMAP_LINEAR   5

#define SCE_GS_REPEAT        0
#define SCE_GS_CLAMP         1
#define SCE_GS_REGION_CLAMP  2
#define SCE_GS_REGION_REPEAT 3

/* ---- register encoders ---- */
#define SCE_GS_SET_PRIM(prim, iip, tme, fge, abe, aa1, fst, ctxt, fix) \
    (GSL(prim) | (GSL(iip) << 3) | (GSL(tme) << 4) | (GSL(fge) << 5) | (GSL(abe) << 6) | \
     (GSL(aa1) << 7) | (GSL(fst) << 8) | (GSL(ctxt) << 9) | (GSL(fix) << 10))
#define SCE_GS_SET_RGBAQ(r, g, b, a, q) \
    (GSL(r) | (GSL(g) << 8) | (GSL(b) << 16) | (GSL(a) << 24) | (GSL(q) << 32))
#define SCE_GS_SET_ST(s, t)     (GSL(s) | (GSL(t) << 32))
#define SCE_GS_SET_UV(u, v)     (GSL(u) | (GSL(v) << 16))
#define SCE_GS_SET_XYZF(x, y, z, f) (GSL(x) | (GSL(y) << 16) | (GSL(z) << 32) | (GSL(f) << 56))
#define SCE_GS_SET_XYZF2 SCE_GS_SET_XYZF
#define SCE_GS_SET_XYZF3 SCE_GS_SET_XYZF
#define SCE_GS_SET_XYZ(x, y, z) (GSL(x) | (GSL(y) << 16) | (GSL(z) << 32))
#define SCE_GS_SET_XYZ2 SCE_GS_SET_XYZ
#define SCE_GS_SET_XYZ3 SCE_GS_SET_XYZ
#define SCE_GS_SET_FOG(f)       (GSL(f) << 56)
#define SCE_GS_SET_TEX0(tbp, tbw, psm, tw, th, tcc, tfx, cbp, cpsm, csm, csa, cld) \
    (GSL(tbp) | (GSL(tbw) << 14) | (GSL(psm) << 20) | (GSL(tw) << 26) | (GSL(th) << 30) | \
     (GSL(tcc) << 34) | (GSL(tfx) << 35) | (GSL(cbp) << 37) | (GSL(cpsm) << 51) | \
     (GSL(csm) << 55) | (GSL(csa) << 56) | (GSL(cld) << 61))
#define SCE_GS_SET_TEX0_1 SCE_GS_SET_TEX0
#define SCE_GS_SET_TEX0_2 SCE_GS_SET_TEX0
#define SCE_GS_SET_TEX2(psm, cbp, cpsm, csm, csa, cld) \
    ((GSL(psm) << 20) | (GSL(cbp) << 37) | (GSL(cpsm) << 51) | (GSL(csm) << 55) | (GSL(csa) << 56) | (GSL(cld) << 61))
#define SCE_GS_SET_TEX2_1 SCE_GS_SET_TEX2
#define SCE_GS_SET_TEX2_2 SCE_GS_SET_TEX2
#define SCE_GS_SET_CLAMP(wms, wmt, minu, maxu, minv, maxv) \
    (GSL(wms) | (GSL(wmt) << 2) | (GSL(minu) << 4) | (GSL(maxu) << 14) | (GSL(minv) << 24) | (GSL(maxv) << 34))
#define SCE_GS_SET_CLAMP_1 SCE_GS_SET_CLAMP
#define SCE_GS_SET_CLAMP_2 SCE_GS_SET_CLAMP
#define SCE_GS_SET_TEX1(lcm, mxl, mmag, mmin, mtba, l, k) \
    (GSL(lcm) | (GSL(mxl) << 2) | (GSL(mmag) << 5) | (GSL(mmin) << 6) | (GSL(mtba) << 9) | \
     (GSL(l) << 19) | (GSL(k) << 32))
#define SCE_GS_SET_TEX1_1 SCE_GS_SET_TEX1
#define SCE_GS_SET_TEX1_2 SCE_GS_SET_TEX1
#define SCE_GS_SET_XYOFFSET(ofx, ofy) (GSL(ofx) | (GSL(ofy) << 32))
#define SCE_GS_SET_XYOFFSET_1 SCE_GS_SET_XYOFFSET
#define SCE_GS_SET_XYOFFSET_2 SCE_GS_SET_XYOFFSET
#define SCE_GS_SET_PRMODECONT(ac) GSL(ac)
#define SCE_GS_SET_PRMODE(iip, tme, fge, abe, aa1, fst, ctxt, fix) \
    ((GSL(iip) << 3) | (GSL(tme) << 4) | (GSL(fge) << 5) | (GSL(abe) << 6) | (GSL(aa1) << 7) | \
     (GSL(fst) << 8) | (GSL(ctxt) << 9) | (GSL(fix) << 10))
#define SCE_GS_SET_TEXCLUT(cbw, cou, cov) (GSL(cbw) | (GSL(cou) << 6) | (GSL(cov) << 12))
#define SCE_GS_SET_SCANMSK(msk) GSL(msk)
#define SCE_GS_SET_TEXA(ta0, aem, ta1) (GSL(ta0) | (GSL(aem) << 15) | (GSL(ta1) << 32))
#define SCE_GS_SET_FOGCOL(r, g, b) (GSL(r) | (GSL(g) << 8) | (GSL(b) << 16))
#define SCE_GS_SET_SCISSOR(scax0, scax1, scay0, scay1) \
    (GSL(scax0) | (GSL(scax1) << 16) | (GSL(scay0) << 32) | (GSL(scay1) << 48))
#define SCE_GS_SET_SCISSOR_1 SCE_GS_SET_SCISSOR
#define SCE_GS_SET_SCISSOR_2 SCE_GS_SET_SCISSOR
#define SCE_GS_SET_ALPHA(a, b, c, d, fix) \
    (GSL(a) | (GSL(b) << 2) | (GSL(c) << 4) | (GSL(d) << 6) | (GSL(fix) << 32))
#define SCE_GS_SET_ALPHA_1 SCE_GS_SET_ALPHA
#define SCE_GS_SET_ALPHA_2 SCE_GS_SET_ALPHA
#define SCE_GS_SET_DIMX(d00, d01, d02, d03, d10, d11, d12, d13, d20, d21, d22, d23, d30, d31, d32, d33) 0
#define SCE_GS_SET_DTHE(dthe) GSL(dthe)
#define SCE_GS_SET_COLCLAMP(clamp) GSL(clamp)
#define SCE_GS_SET_TEST(ate, atst, aref, afail, date, datm, zte, ztst) \
    (GSL(ate) | (GSL(atst) << 1) | (GSL(aref) << 4) | (GSL(afail) << 12) | (GSL(date) << 14) | \
     (GSL(datm) << 15) | (GSL(zte) << 16) | (GSL(ztst) << 17))
#define SCE_GS_SET_TEST_1 SCE_GS_SET_TEST
#define SCE_GS_SET_TEST_2 SCE_GS_SET_TEST
#define SCE_GS_SET_PABE(pabe) GSL(pabe)
#define SCE_GS_SET_FBA(fba) GSL(fba)
#define SCE_GS_SET_FBA_1 SCE_GS_SET_FBA
#define SCE_GS_SET_FBA_2 SCE_GS_SET_FBA
#define SCE_GS_SET_FRAME(fbp, fbw, psm, fbmask) \
    (GSL(fbp) | (GSL(fbw) << 16) | (GSL(psm) << 24) | (GSL(fbmask) << 32))
#define SCE_GS_SET_FRAME_1 SCE_GS_SET_FRAME
#define SCE_GS_SET_FRAME_2 SCE_GS_SET_FRAME
#define SCE_GS_SET_ZBUF(zbp, psm, zmsk) (GSL(zbp) | (GSL(psm) << 24) | (GSL(zmsk) << 32))
#define SCE_GS_SET_ZBUF_1 SCE_GS_SET_ZBUF
#define SCE_GS_SET_ZBUF_2 SCE_GS_SET_ZBUF
#define SCE_GS_SET_BITBLTBUF(sbp, sbw, spsm, dbp, dbw, dpsm) \
    (GSL(sbp) | (GSL(sbw) << 16) | (GSL(spsm) << 24) | (GSL(dbp) << 32) | (GSL(dbw) << 48) | (GSL(dpsm) << 56))
#define SCE_GS_SET_TRXPOS(ssax, ssay, dsax, dsay, dir) \
    (GSL(ssax) | (GSL(ssay) << 16) | (GSL(dsax) << 32) | (GSL(dsay) << 48) | (GSL(dir) << 59))
#define SCE_GS_SET_TRXREG(rrw, rrh) (GSL(rrw) | (GSL(rrh) << 32))
#define SCE_GS_SET_TRXDIR(xdr) GSL(xdr)
#define SCE_GS_SET_SIGNAL(id, msk) (GSL(id) | (GSL(msk) << 32))
#define SCE_GS_SET_FINISH(a) GSL(a)
#define SCE_GS_SET_LABEL(id, msk) (GSL(id) | (GSL(msk) << 32))

/* ---- GIF tags ---- */
#define SCE_GIF_PACKED    0
#define SCE_GIF_REGLIST   1
#define SCE_GIF_IMAGE     2
#define SCE_GIF_PACKED_AD 0x0e

#define SCE_GIF_SET_TAG(nloop, eop, pre, prim, flg, nreg) \
    (GSL(nloop) | (GSL(eop) << 15) | (GSL(pre) << 46) | (GSL(prim) << 47) | (GSL(flg) << 58) | (GSL(nreg) << 60))

typedef struct {
    u_long64_t NLOOP:15, EOP:1, pad16:30, PRE:1, PRIM:11, FLG:2, NREG:4;
    u_long64_t REGS;
} sceGifTag;

/* ---- register structures (bit layouts as in libgraph) ---- */
typedef struct { u_long64_t PRIM:3, IIP:1, TME:1, FGE:1, ABE:1, AA1:1, FST:1, CTXT:1, FIX:1, pad11:53; } sceGsPrim;
typedef struct { u_long64_t R:8, G:8, B:8, A:8; float Q; } sceGsRgbaq;
typedef struct { u_long64_t X:16, Y:16, Z:32; } sceGsXyz;
typedef struct { u_long64_t X:16, Y:16, Z:24, F:8; } sceGsXyzf;
typedef struct { u_long64_t FBP:9, pad09:7, FBW:6, pad22:2, PSM:6, pad30:2, FBMSK:32; } sceGsFrame;
typedef struct { u_long64_t ZBP:9, pad09:15, PSM:4, pad28:4, ZMSK:1, pad33:31; } sceGsZbuf;
typedef struct { u_long64_t OFX:16, pad16:16, OFY:16, pad48:16; } sceGsXyoffset;
typedef struct { u_long64_t SCAX0:11, pad11:5, SCAX1:11, pad27:5, SCAY0:11, pad43:5, SCAY1:11, pad59:5; } sceGsScissor;
typedef struct { u_long64_t AC:1, pad01:63; } sceGsPrmodecont;
typedef struct { u_long64_t CLAMP:1, pad01:63; } sceGsColclamp;
typedef struct { u_long64_t DTHE:1, pad01:63; } sceGsDthe;
typedef struct { u_long64_t ATE:1, ATST:3, AREF:8, AFAIL:2, DATE:1, DATM:1, ZTE:1, ZTST:2, pad19:45; } sceGsTest;
typedef struct { u_long64_t A:2, B:2, C:2, D:2, pad8:24, FIX:8, pad40:24; } sceGsAlpha;
typedef struct { u_long64_t WMS:2, WMT:2, MINU:10, MAXU:10, MINV:10, MAXV:10, pad44:20; } sceGsClamp;
typedef struct { u_long64_t TA0:8, pad08:7, AEM:1, pad16:16, TA1:8, pad40:24; } sceGsTexa;
typedef struct { u_long64_t SBP:14, pad14:2, SBW:6, pad22:2, SPSM:6, pad30:2, DBP:14, pad46:2, DBW:6, pad54:2, DPSM:6, pad62:2; } sceGsBitbltbuf;
typedef struct { u_long64_t SSAX:11, pad11:5, SSAY:11, pad27:5, DSAX:11, pad43:5, DSAY:11, DIR:2, pad61:3; } sceGsTrxpos;
typedef struct { u_long64_t RRW:12, pad12:20, RRH:12, pad44:20; } sceGsTrxreg;
typedef struct { u_long64_t XDR:2, pad02:62; } sceGsTrxdir;
typedef struct { u_long64_t TEXFLUSH; } sceGsTexflush;
typedef struct { u_long64_t pad; } sceGsFinish;

/* privileged (display) registers */
typedef struct { u_long64_t EN1:1, EN2:1, CRTMD:3, MMOD:1, AMOD:1, SLBG:1, ALP:8, pad16:48; } sceGsPmode;
typedef struct { u_long64_t INT:1, FFMD:1, DPMS:2, pad04:60; } sceGsSmode2;
typedef struct { u_long64_t FBP:9, FBW:6, PSM:5, pad20:12, DBX:11, DBY:11, pad54:10; } sceGsDispfb;
typedef struct { u_long64_t DX:12, DY:11, MAGH:4, MAGV:2, pad29:3, DW:12, DH:11, pad55:9; } sceGsDisplay;
typedef struct { u_long64_t R:8, G:8, B:8, pad24:40; } sceGsBgcolor;

/* ---- libgraph environments ---- */
typedef struct {
    sceGsPmode pmode;
    sceGsSmode2 smode2;
    sceGsDispfb dispfb;
    sceGsDisplay display;
    sceGsBgcolor bgcolor;
} sceGsDispEnv;

typedef struct {
    sceGsFrame frame1;       u_long64_t frame1addr;
    sceGsZbuf zbuf1;         u_long64_t zbuf1addr;
    sceGsXyoffset xyoffset1; u_long64_t xyoffset1addr;
    sceGsScissor scissor1;   u_long64_t scissor1addr;
    sceGsPrmodecont prmodecont; u_long64_t prmodecontaddr;
    sceGsColclamp colclamp;  u_long64_t colclampaddr;
    sceGsDthe dthe;          u_long64_t dtheaddr;
    sceGsTest test1;         u_long64_t test1addr;
} sceGsDrawEnv1;

typedef struct {
    sceGsFrame frame2;       u_long64_t frame2addr;
    sceGsZbuf zbuf2;         u_long64_t zbuf2addr;
    sceGsXyoffset xyoffset2; u_long64_t xyoffset2addr;
    sceGsScissor scissor2;   u_long64_t scissor2addr;
    sceGsPrmodecont prmodecont; u_long64_t prmodecontaddr;
    sceGsColclamp colclamp;  u_long64_t colclampaddr;
    sceGsDthe dthe;          u_long64_t dtheaddr;
    sceGsTest test2;         u_long64_t test2addr;
} sceGsDrawEnv2;

typedef struct {
    sceGsTest testa;  u_long64_t testaaddr;
    sceGsPrim prim;   u_long64_t primaddr;
    sceGsRgbaq rgbaq; u_long64_t rgbaqaddr;
    sceGsXyz xyz2a;   u_long64_t xyz2aaddr;
    sceGsXyz xyz2b;   u_long64_t xyz2baddr;
    sceGsTest testb;  u_long64_t testbaddr;
} sceGsClear;

typedef struct {
    sceGsDispEnv disp[2];
    sceGifTag giftag0;
    sceGsDrawEnv1 draw0;
    sceGsClear clear0;
    sceGifTag giftag1;
    sceGsDrawEnv1 draw1;
    sceGsClear clear1;
} sceGsDBuff;

typedef struct {
    sceGsDispEnv disp[2];
    sceGifTag giftag0;
    sceGsDrawEnv1 draw01;
    sceGsDrawEnv2 draw02;
    sceGsClear clear0;
    sceGifTag giftag1;
    sceGsDrawEnv1 draw11;
    sceGsDrawEnv2 draw12;
    sceGsClear clear1;
} sceGsDBuffDc;

typedef struct {
    sceGifTag giftag0;
    sceGsBitbltbuf bitbltbuf; u_long64_t bitbltbufaddr;
    sceGsTrxpos trxpos;       u_long64_t trxposaddr;
    sceGsTrxreg trxreg;       u_long64_t trxregaddr;
    sceGsTrxdir trxdir;       u_long64_t trxdiraddr;
    sceGifTag giftag1;
} sceGsLoadImage;

typedef struct {
    u_int vifcode[4];
    sceGifTag giftag;
    sceGsBitbltbuf bitbltbuf; u_long64_t bitbltbufaddr;
    sceGsTrxpos trxpos;       u_long64_t trxposaddr;
    sceGsTrxreg trxreg;       u_long64_t trxregaddr;
    sceGsFinish finish;       u_long64_t finishaddr;
    sceGsTrxdir trxdir;       u_long64_t trxdiraddr;
} sceGsStoreImage;

/* ---- libgraph functions (pc/src/pc_libgraph.c) ---- */
int  sceGsResetGraph(short mode, short inter, short omode, short ffmode);
void sceGsResetPath(void);
int  sceGsSetDefDBuffDc(sceGsDBuffDc* db, short psm, short w, short h, short ztest, short zpsm, short clear);
int  sceGsSwapDBuffDc(sceGsDBuffDc* db, int id);
void sceGsSetDefLoadImage(sceGsLoadImage* lp, short dbp, short dbw, short dpsm, short x, short y, short w, short h);
int  sceGsExecLoadImage(sceGsLoadImage* lp, u_long128* srcaddr);
void sceGsSetDefStoreImage(sceGsStoreImage* sp, short sbp, short sbw, short spsm, short x, short y, short w, short h);
int  sceGsExecStoreImage(sceGsStoreImage* sp, u_long128* dstaddr);
int  sceGsSyncPath(int mode, unsigned short timeout);
int  sceGsSyncV(int mode);
int  sceGsSyncVCallback(int (*func)(int));
