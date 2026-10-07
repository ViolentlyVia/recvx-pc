/* PC port: libgraph / libdma functions and the DMA helpers of ps2_loadtim2.c, on top of the
 * GS emulator. */
#include "../../include/ps2/veronica/prog/ps2_loadtim2.h"
#include "../../include/ps2/veronica/prog/main.h"

#include "pc_platform.h"

void gsGifTransfer(const void* data, int qwc);
void gsDmaChain(const void* tag);
void gsRendererFlush(void);
void gsMemReadImageRegs(uint64_t bitbltbuf, uint64_t trxpos, uint64_t trxreg, void* out);
void pcGsSetDisplay(unsigned int fbp, unsigned int fbw);

/* ---- ps2_loadtim2.c DMA helpers ---- */

void loadImage(void* tags)
{
    gsDmaChain(tags);
}

void SyncPath()
{
}

void D2_SyncTag()
{
}

/* ---- libdma / libvu0 resets ---- */

void sceDmaReset(int mode)
{
}

void sceVpu0Reset(void)
{
}

/* ---- libgraph ---- */

int sceGsResetGraph(short mode, short inter, short omode, short ffmode)
{
    return 0;
}

void sceGsResetPath(void)
{
}

int sceGsSyncPath(int mode, unsigned short timeout)
{
    return 0;
}

static void set_giftag(sceGifTag* tag, int nloop, int flg, int nreg, uint64_t regs, int eop)
{
    *(uint64_t*)tag = SCE_GIF_SET_TAG(nloop, eop, 0, 0, flg, nreg);
    tag->REGS = regs;
}

static void set_drawenv1(sceGsDrawEnv1* d, int fbp, short psm, short w, short h, short ztest, short zpsm, int ctx)
{
    *(uint64_t*)&d->frame1 = SCE_GS_SET_FRAME(fbp, w / 64, psm, 0);
    d->frame1addr = ctx ? SCE_GS_FRAME_2 : SCE_GS_FRAME_1;
    *(uint64_t*)&d->zbuf1 = SCE_GS_SET_ZBUF(0, zpsm & 0xF, (ztest == 0) ? 1 : 0);
    d->zbuf1addr = ctx ? SCE_GS_ZBUF_2 : SCE_GS_ZBUF_1;
    *(uint64_t*)&d->xyoffset1 = SCE_GS_SET_XYOFFSET((2048 - w / 2) << 4, (2048 - h / 2) << 4);
    d->xyoffset1addr = ctx ? SCE_GS_XYOFFSET_2 : SCE_GS_XYOFFSET_1;
    *(uint64_t*)&d->scissor1 = SCE_GS_SET_SCISSOR(0, w - 1, 0, h - 1);
    d->scissor1addr = ctx ? SCE_GS_SCISSOR_2 : SCE_GS_SCISSOR_1;
    *(uint64_t*)&d->prmodecont = SCE_GS_SET_PRMODECONT(1);
    d->prmodecontaddr = SCE_GS_PRMODECONT;
    *(uint64_t*)&d->colclamp = SCE_GS_SET_COLCLAMP(1);
    d->colclampaddr = SCE_GS_COLCLAMP;
    *(uint64_t*)&d->dthe = SCE_GS_SET_DTHE(0);
    d->dtheaddr = SCE_GS_DTHE;
    *(uint64_t*)&d->test1 = SCE_GS_SET_TEST(0, 0, 0, 0, 0, 0, 1, (ztest == 0) ? 1 : ztest);
    d->test1addr = ctx ? SCE_GS_TEST_2 : SCE_GS_TEST_1;
}

static void set_clear(sceGsClear* c, short w, short h, short ztest)
{
    int ofx = (2048 - w / 2) << 4, ofy = (2048 - h / 2) << 4;

    *(uint64_t*)&c->testa = SCE_GS_SET_TEST(0, 0, 0, 0, 0, 0, 1, 1);
    c->testaaddr = SCE_GS_TEST_1;
    *(uint64_t*)&c->prim = SCE_GS_SET_PRIM(SCE_GS_PRIM_SPRITE, 0, 0, 0, 0, 0, 0, 0, 0);
    c->primaddr = SCE_GS_PRIM;
    *(uint64_t*)&c->rgbaq = SCE_GS_SET_RGBAQ(0, 0, 0, 0x80, 0x3F800000);
    c->rgbaqaddr = SCE_GS_RGBAQ;
    *(uint64_t*)&c->xyz2a = SCE_GS_SET_XYZ(ofx, ofy, 0);
    c->xyz2aaddr = SCE_GS_XYZ2;
    *(uint64_t*)&c->xyz2b = SCE_GS_SET_XYZ(ofx + (w << 4), ofy + (h << 4), 0);
    c->xyz2baddr = SCE_GS_XYZ2;
    *(uint64_t*)&c->testb = SCE_GS_SET_TEST(0, 0, 0, 0, 0, 0, 1, (ztest == 0) ? 1 : ztest);
    c->testbaddr = SCE_GS_TEST_1;
}

int sceGsSetDefDBuffDc(sceGsDBuffDc* db, short psm, short w, short h, short ztest, short zpsm, short clear)
{
    int i;
    int nregs = (sizeof(sceGsDrawEnv1) + sizeof(sceGsDrawEnv2) + (clear ? sizeof(sceGsClear) : 0)) / 16;

    memset(db, 0, sizeof(*db));

    for (i = 0; i < 2; i++)
    {
        *(uint64_t*)&db->disp[i].pmode = 0x66;     /* EN2, MMOD, ALP 0 */
        *(uint64_t*)&db->disp[i].dispfb = 0;
        db->disp[i].dispfb.FBW = w / 64;
        db->disp[i].dispfb.PSM = psm;
        db->disp[i].display.DW = w * 4 - 1;
        db->disp[i].display.DH = h - 1;
    }

    db->disp[1].dispfb.FBP = (w * h * 4 + 8191) / 8192;

    set_giftag(&db->giftag0, nregs, SCE_GIF_PACKED, 1, SCE_GIF_PACKED_AD, 1);
    set_drawenv1(&db->draw01, 0, psm, w, h, ztest, zpsm, 0);
    set_drawenv1((sceGsDrawEnv1*)&db->draw02, 0, psm, w, h, ztest, zpsm, 1);
    set_clear(&db->clear0, w, h, ztest);

    set_giftag(&db->giftag1, nregs, SCE_GIF_PACKED, 1, SCE_GIF_PACKED_AD, 1);
    set_drawenv1(&db->draw11, db->disp[1].dispfb.FBP, psm, w, h, ztest, zpsm, 0);
    set_drawenv1((sceGsDrawEnv1*)&db->draw12, db->disp[1].dispfb.FBP, psm, w, h, ztest, zpsm, 1);
    set_clear(&db->clear1, w, h, ztest);

    return 0;
}

int sceGsSwapDBuffDc(sceGsDBuffDc* db, int id)
{
    const sceGifTag* tag = id ? &db->giftag1 : &db->giftag0;
    const sceGsDispEnv* disp = &db->disp[id & 1];

    gsGifTransfer(tag, 1 + (int)tag->NLOOP);

    /* the frame is complete: show the display buffer */
    pcGsSetDisplay(disp->dispfb.FBP, disp->dispfb.FBW);
    pcVideoPresent();

    return id;
}

void sceGsSetDefLoadImage(sceGsLoadImage* lp, short dbp, short dbw, short dpsm, short x, short y, short w, short h)
{
    int bits = (dpsm == SCE_GS_PSMT8 || dpsm == SCE_GS_PSMT8H) ? 8 :
               (dpsm == SCE_GS_PSMT4 || dpsm == SCE_GS_PSMT4HL || dpsm == SCE_GS_PSMT4HH) ? 4 :
               (dpsm == SCE_GS_PSMCT16 || dpsm == SCE_GS_PSMCT16S || dpsm == SCE_GS_PSMZ16 || dpsm == SCE_GS_PSMZ16S) ? 16 :
               (dpsm == SCE_GS_PSMCT24 || dpsm == SCE_GS_PSMZ24) ? 24 : 32;

    memset(lp, 0, sizeof(*lp));
    set_giftag(&lp->giftag0, 4, SCE_GIF_PACKED, 1, SCE_GIF_PACKED_AD, 0);
    *(uint64_t*)&lp->bitbltbuf = SCE_GS_SET_BITBLTBUF(0, 0, 0, dbp, dbw, dpsm);
    lp->bitbltbufaddr = SCE_GS_BITBLTBUF;
    *(uint64_t*)&lp->trxpos = SCE_GS_SET_TRXPOS(0, 0, x, y, 0);
    lp->trxposaddr = SCE_GS_TRXPOS;
    *(uint64_t*)&lp->trxreg = SCE_GS_SET_TRXREG(w, h);
    lp->trxregaddr = SCE_GS_TRXREG;
    *(uint64_t*)&lp->trxdir = SCE_GS_SET_TRXDIR(0);
    lp->trxdiraddr = SCE_GS_TRXDIR;
    set_giftag(&lp->giftag1, (w * h * bits / 8 + 15) / 16, SCE_GIF_IMAGE, 0, 0, 1);
}

int sceGsExecLoadImage(sceGsLoadImage* lp, u_long128* srcaddr)
{
    gsGifTransfer(lp, 6);
    gsGifTransfer(srcaddr, (int)lp->giftag1.NLOOP);

    return 0;
}

void sceGsSetDefStoreImage(sceGsStoreImage* sp, short sbp, short sbw, short spsm, short x, short y, short w, short h)
{
    memset(sp, 0, sizeof(*sp));
    *(uint64_t*)&sp->bitbltbuf = SCE_GS_SET_BITBLTBUF(sbp, sbw, spsm, 0, 0, 0);
    *(uint64_t*)&sp->trxpos = SCE_GS_SET_TRXPOS(x, y, 0, 0, 0);
    *(uint64_t*)&sp->trxreg = SCE_GS_SET_TRXREG(w, h);
}

int sceGsExecStoreImage(sceGsStoreImage* sp, u_long128* dstaddr)
{
    gsRendererFlush();
    gsMemReadImageRegs(*(uint64_t*)&sp->bitbltbuf, *(uint64_t*)&sp->trxpos, *(uint64_t*)&sp->trxreg, dstaddr);

    return 0;
}

/* ---- libdma ---- */

#define DMA_CH_GIF 0x1000A000

int sceDmaSend(sceDmaChan* d, void* tag)
{
    /* only the GIF channel feeds the GS; VIF0/VIF1 carried VU microcode, which is C on PC */
    if ((unsigned int)(size_t)d == DMA_CH_GIF)
    {
        gsDmaChain(tag);
    }

    return 0;
}

int sceDmaSync(sceDmaChan* d, int mode, int timeout)
{
    return 0;
}

/* VU microprogram images (vsm/*.vsm); never uploaded on PC */
int ps2_vu0sub0;
int ps2_vu1sub0;
int ps2_vu1sub1;
