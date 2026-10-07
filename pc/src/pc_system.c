/* PC port: startup code of ps2_dummy.c (Ps2Init and friends). Everything that is not about
 * PS2 hardware/IOP setup is kept as in the original. */
#include "../../include/ps2/veronica/prog/ps2_NaSystem.h"
#include "../../include/ps2/veronica/prog/ps2_dummy.h"
#include "../../include/ps2/veronica/prog/ps2_loadtim2.h"
#include "../../include/ps2/veronica/prog/ps2_NaTextureFunction.h"
#include "../../include/ps2/veronica/prog/main.h"

#include "pc_platform.h"

extern unsigned char Ps2_tex_mem[10485760];

void _builtin_set_imask(int mask)
{
}

void Ps2Init()
{
    Ps2_tex_buff = &Ps2_tex_mem;

    pcVideoInit();

    sceGsResetPath();
    sceGsResetGraph(0, SCE_GS_INTERLACE, SCE_GS_NTSC, SCE_GS_FIELD);

    sceGsSetDefDBuffDc(&Db, SCE_GS_PSMCT32, DISP_WIDTH, DISP_HEIGHT, SCE_GS_ZGEQUAL, SCE_GS_PSMZ16S, 1);

    Db.disp[0].dispfb.FBP = 150;
    Db.disp[0].dispfb.PSM = SCE_GS_PSMCT32;

    Db.draw01.frame1.FBP = 0;
    Db.draw01.frame1.PSM = SCE_GS_PSMCT32;

    Db.draw01.zbuf1.ZBP = 300;

    Db.draw02.frame2.FBP = 0;
    Db.draw02.frame2.PSM = SCE_GS_PSMCT32;

    Db.draw02.zbuf2.ZBP = 300;

    Db.disp[1].dispfb.FBP = 150;
    Db.disp[1].dispfb.PSM = SCE_GS_PSMCT32;

    Db.draw11.frame1.FBP = 0;
    Db.draw11.frame1.PSM = SCE_GS_PSMCT32;

    Db.draw11.zbuf1.ZBP = 300;

    Db.draw12.frame2.FBP = 0;
    Db.draw12.frame2.PSM = SCE_GS_PSMCT32;

    Db.draw12.zbuf2.ZBP = 300;

    Db.disp[0].display.DY = 636;
    Db.disp[1].display.DY = 32;

    {
        extern void pcGsSelfTest(void);

        pcGsSelfTest();
    }

    ClearVram();

    pcCdOpen();

    Ps2SetVSyncCounter();

    Ps2InitFunc();

    Ps2DispScreenClear();
    Ps2ScreenClear();

    Db.disp[0].pmode.EN2 = 0;
    Db.disp[1].pmode.EN2 = 0;

    Ps2SwapDBuff();

    Db.disp[0].pmode.EN2 = 1;
    Db.disp[1].pmode.EN2 = 1;

    Ps2InitTexCache();
    Ps2InitPS2_GS_SAVE();

    njColorBlendingMode(0, 8);
    njColorBlendingMode(1, 6);

    njUserClipping(0, NULL);

    _Make_SinTable();

    /* Ps2Vu0ProgSend / Ps2Vu1ProgSend: the VU microprograms are C code on PC */
}

void Ps2LoadModule(char* p)
{
}

void Snd_init()
{
}

void Cd_init()
{
}

void Card_init()
{
}

void PS2_jikken()
{
    pdGetPeripheral(PDD_PORT_A0);

    Ps2SwapDBuff();
}

/* debug: one line of game state for the periodic log */
void pcGameStateLog(void)
{
    extern SYS_WORK* sys;

    pcLog("state tk %06x ts %06x gm %08x st %08x stg %d rom %d gm_mode %d", sys->tk_flg, sys->ts_flg, sys->gm_flg,
          sys->st_flg, sys->stg_no, sys->rom_no, sys->gm_mode);
}
