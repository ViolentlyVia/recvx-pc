/* PC port: system-level replacements for ps2_NaSystem.c and the startup/frame code of
 * ps2_dummy.c. Logic that is not about the PS2 hardware is kept as in the original. */
#include "../../include/ps2/veronica/prog/ps2_NaSystem.h"
#include "../../include/ps2/veronica/prog/ps2_dummy.h"
#include "../../include/ps2/veronica/prog/ps2_NaTextureFunction.h"
#include "../../include/ps2/veronica/prog/main.h"

extern unsigned char Ps2_tex_mem[10485760];

#include "pc_platform.h"

PS2_NJ_SAVE Ps2_nj_save_current;

/* ---- ps2_NaSystem.c ---- */

void njSetTextureMemorySize(Uint32 size)
{
}

void njSetVertexBuffer(Uint32* buffer, Sint32 size)
{
}

void njInitSystem(Int mode, Int frame, Int count)
{
    Ps2Init();
}

void njInitVertexBuffer(Sint32 op, Sint32 om, Sint32 tp, Sint32 tm, Sint32 pt)
{
}

void njWaitVSync(void)
{
}

static void null_func()
{
}

void Ps2InitFunc()
{
    EorFunc = null_func;
    VsyncFunc = null_func;
}

void njSetEORFunction(void (*func)(void))
{
    EorFunc = (func == NULL) ? null_func : func;
}

/* Present the frame. The game runs at 30 fps: wait until two fields have passed since the last
 * swap (busy wait on the PS2, so only higher-priority threads run meanwhile). */
void Ps2SwapDBuff()
{
    Ps2DrawOTag();
    Ps2ClearOT();

    EorFunc();

    while (Ps2_vcount < 2)
    {
        pcKernelBusyWait();
    }

    pcVideoPresent();

    Ps2_dbuff = (Ps2_dbuff + 1) & 0x1;
    Ps2_vcount = 0;

    VsyncFunc();
}

static int vsync_func(int cause)
{
    Ps2_vcount++;

    if ((((sys->tk_flg & 0x80)) && ((sys->gm_flg & 0x80000000))) && (sys->time < 216000000))
    {
        sys->time++;
    }

    ExitHandler();

    return 0;
}

void Ps2SetVSyncCounter()
{
    DisableIntc(2);
    AddIntcHandler(2, vsync_func, 0);
    EnableIntc(2);
}

void njSetVSyncFunction(void (*func)(void))
{
    Ps2_vcount = 0;
    VsyncFunc = (func == NULL) ? null_func : func;
}

void njExitSystem(void)
{
}

void njSetBackColor(Uint32 col1, Uint32 col2, Uint32 col3)
{
    /* TODO renderer: clear color */
}

void njTextureFilterMode(Int mode)
{
    Ps2_nj_save_current.mode = mode;
}

void njPolygonCullingMode(Int mode)
{
}

void njColorBlendingMode(Int target, Int mode)
{
    Ps2_nj_save_current.mode_bk[target] = mode;
    Ps2_nj_save_current.set_last = target;
    /* TODO renderer: blend state (njColorBlendingModeSys) */
}

void njTextureShadingMode(Int mode)
{
}

void njSetCheapShadowMode(Int mode)
{
}

void njUserClipping(Int mode, NJS_POINT2* p)
{
    /* TODO renderer: scissor */
}

void njGetSystemAttr(NJS_SYS_ATTR* attr)
{
    *(PS2_NJ_SAVE*)attr = Ps2_nj_save_current;
}

void njSetSystemAttr(NJS_SYS_ATTR* attr)
{
    Ps2_nj_save_current = *(PS2_NJ_SAVE*)attr;
}

void njChangeSystem(Int mode, Int frame, Int count)
{
}

void njAdjustDisplay(Sint32 xadjust, Sint32 yadjust)
{
}

void njSetBorderColor(Uint32 color)
{
}

/* ---- ps2_dummy.c: startup and frame ---- */

void _builtin_set_imask(int mask)
{
}

void Ps2Init()
{
    Ps2_tex_buff = &Ps2_tex_mem;

    pcVideoInit();
    pcCdOpen();

    Ps2SetVSyncCounter();
    Ps2InitFunc();

    /* TODO renderer: Ps2InitTexCache(), Ps2InitPS2_GS_SAVE() */

    njColorBlendingMode(0, 8);
    njColorBlendingMode(1, 6);

    njUserClipping(0, NULL);

    _Make_SinTable();
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
