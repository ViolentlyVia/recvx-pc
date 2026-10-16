/* PC port: replacements for the remaining PS2 SDK libraries (libpad, sif, libsdr,
 * libvu0, and a few libc extras the EE toolchain provided). */
#include <math.h>
#include <stdlib.h>
#include <string.h>

#include "sce_pc.h"
#include "pc_platform.h"

/* ------------------------------------------------------------------------------------------- */
/* libpad: always a DualShock 2 on port 0 */

static int pad_press_mode;

int scePadInit(int mode)                                     { (void)mode; return 1; }
int scePadEnd(void)                                          { return 1; }
int scePadPortOpen(int port, int slot, void* addr)           { (void)slot; (void)addr; return port == 0; }
int scePadPortClose(int port, int slot)                      { (void)port; (void)slot; return 1; }
int scePadGetState(int port, int slot)                       { (void)slot; return (port == 0) ? 6 : 0; }  /* scePadStateStable */
int scePadGetReqState(int port, int slot)                    { (void)port; (void)slot; return 0; }        /* scePadReqStateComplete */
int scePadSetMainMode(int port, int slot, int offs, int lock) { (void)port; (void)slot; (void)offs; (void)lock; return 1; }
int scePadInfoPressMode(int port, int slot)                  { (void)port; (void)slot; return 1; }
int scePadEnterPressMode(int port, int slot)                 { (void)port; (void)slot; pad_press_mode = 1; return 1; }
int scePadExitPressMode(int port, int slot)                  { (void)port; (void)slot; pad_press_mode = 0; return 1; }
int scePadSetActAlign(int port, int slot, const unsigned char* data)  { (void)port; (void)slot; (void)data; return 1; }
int scePadSetActDirect(int port, int slot, const unsigned char* data)
{
    (void)slot;

    if (port == 0)
    {
        pcPadRumble(data[0], data[1]);   /* Ps2_pad_actuater: small motor, large motor */
    }

    return 1;
}
int scePadInfoAct(int port, int slot, int actno, int term)   { (void)port; (void)slot; (void)actno; (void)term; return 0; }

int scePadInfoMode(int port, int slot, int term, int offs)
{
    (void)port; (void)slot; (void)offs;

    switch (term)
    {
    case InfoModeCurID:
        return 7;          /* DualShock (analog) */
    case InfoModeCurExID:
        return pcPadHasRumble() ? 7 : 0;   /* 7: DualShock with motors (pdVibMxIsReady) */
    case InfoModeIdTable:
        return (offs == -1) ? 2 : ((offs == 0) ? 4 : 7);
    }

    return 0;
}

int scePadRead(int port, int slot, unsigned char* rdata)
{
    (void)slot;

    pcPadGetState(port, rdata);

    if ((port == 0) && !pad_press_mode)
    {
        rdata[1] = 0x73;   /* analog mode without pressure data */
    }

    return (port == 0) ? 32 : 0;
}

/* libmc: pc_mc.c */

int  sceSdRemoteInit(void)                      { return 0; }
int  sceSSyn_SetOutputMode(int mode)            { (void)mode; return 0; }

/* ------------------------------------------------------------------------------------------- */
/* libvu0 helpers */

void sceVu0UnitMatrix(sceVu0FMATRIX m)
{
    int i, j;

    for (i = 0; i < 4; i++)
    {
        for (j = 0; j < 4; j++)
        {
            m[i][j] = (i == j) ? 1.0f : 0.0f;
        }
    }
}

void sceVu0AddVector(sceVu0FVECTOR v0, sceVu0FVECTOR v1, sceVu0FVECTOR v2)
{
    int i;
    for (i = 0; i < 4; i++) v0[i] = v1[i] + v2[i];
}

void sceVu0SubVector(sceVu0FVECTOR v0, sceVu0FVECTOR v1, sceVu0FVECTOR v2)
{
    int i;
    for (i = 0; i < 4; i++) v0[i] = v1[i] - v2[i];
}

void sceVu0ScaleVector(sceVu0FVECTOR v0, sceVu0FVECTOR v1, float s)
{
    int i;
    for (i = 0; i < 4; i++) v0[i] = v1[i] * s;
}

float sceVu0InnerProduct(sceVu0FVECTOR v0, sceVu0FVECTOR v1)
{
    return (v0[0] * v1[0] + v0[1] * v1[1]) + v0[2] * v1[2];
}

void sceVu0OuterProduct(sceVu0FVECTOR v0, sceVu0FVECTOR v1, sceVu0FVECTOR v2)
{
    float x = v1[1] * v2[2] - v2[1] * v1[2];
    float y = v1[2] * v2[0] - v2[2] * v1[0];
    float z = v1[0] * v2[1] - v2[0] * v1[1];

    v0[0] = x;
    v0[1] = y;
    v0[2] = z;
    v0[3] = 0;
}

void sceVu0Normalize(sceVu0FVECTOR v0, sceVu0FVECTOR v1)
{
    float l2 = (v1[0] * v1[0] + v1[1] * v1[1]) + v1[2] * v1[2];
    float q = (l2 == 0) ? 0 : 1.0f / sqrtf(l2);

    v0[0] = v1[0] * q;
    v0[1] = v1[1] * q;
    v0[2] = v1[2] * q;
    v0[3] = v1[3];
}

void sceVu0ITOF4Vector(sceVu0FVECTOR v0, sceVu0IVECTOR v1)
{
    int i;
    for (i = 0; i < 4; i++) v0[i] = v1[i] / 16.0f;
}



void sceVu0FTOI0Vector(sceVu0IVECTOR v0, sceVu0FVECTOR v1)
{
    int i;
    for (i = 0; i < 4; i++) v0[i] = (int)v1[i];
}

void sceVu0FTOI4Vector(sceVu0IVECTOR v0, sceVu0FVECTOR v1)
{
    int i;
    for (i = 0; i < 4; i++) v0[i] = (int)(v1[i] * 16.0f);
}

/* ------------------------------------------------------------------------------------------- */
/* EE libc extras */

int strcasecmp(const char* a, const char* b)
{
    return _stricmp(a, b);
}

char* index(const char* s, int c)
{
    return strchr(s, c);
}


