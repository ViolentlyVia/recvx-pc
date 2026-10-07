/* PC port: minimal replacements for PS2 SDK types and functions.
 * Only what the game code needs is declared here; implementations live in pc/src. */
#pragma once

typedef unsigned char      u_char;
typedef unsigned short     u_short;
typedef unsigned int       u_int;
typedef unsigned long long u_long;   /* "long" is 64-bit on the EE */

typedef struct { unsigned int w[4]; } u_long128;
typedef struct { unsigned int w[4]; } u_int128;
typedef u_long128 long128;
#define __int128 u_long128
typedef unsigned long long u_long64;

/* ---- libvu0 ---- */
typedef float sceVu0FVECTOR[4];
typedef int   sceVu0IVECTOR[4];
typedef float sceVu0FMATRIX[4][4];
typedef int   sceVu0IMATRIX[4][4];

void  sceVu0UnitMatrix(sceVu0FMATRIX m);
void  sceVu0AddVector(sceVu0FVECTOR v0, sceVu0FVECTOR v1, sceVu0FVECTOR v2);
void  sceVu0SubVector(sceVu0FVECTOR v0, sceVu0FVECTOR v1, sceVu0FVECTOR v2);
void  sceVu0ScaleVector(sceVu0FVECTOR v0, sceVu0FVECTOR v1, float s);
float sceVu0InnerProduct(sceVu0FVECTOR v0, sceVu0FVECTOR v1);
void  sceVu0OuterProduct(sceVu0FVECTOR v0, sceVu0FVECTOR v1, sceVu0FVECTOR v2);
void  sceVu0Normalize(sceVu0FVECTOR v0, sceVu0FVECTOR v1);
void  sceVu0ITOF4Vector(sceVu0FVECTOR v0, sceVu0IVECTOR v1);
void  sceVu0ITOF12Vector(sceVu0FVECTOR v0, sceVu0IVECTOR v1);
void  sceVu0FTOI0Vector(sceVu0IVECTOR v0, sceVu0FVECTOR v1);
void  sceVu0FTOI4Vector(sceVu0IVECTOR v0, sceVu0FVECTOR v1);

/* ---- libgraph (GS registers are 64-bit bitfields) ---- */
typedef struct {
    u_long64 TBP0:14, TBW:6, PSM:6, TW:4, TH:4, TCC:1, TFX:2,
             CBP:14, CPSM:4, CSM:1, CSA:5, CLD:3;
} sceGsTex0;

typedef struct {
    u_long64 LCM:1, pad01:1, MXL:3, MMAG:1, MMIN:3, MTBA:1, pad10:9,
             L:2, pad21:11, K:12, pad44:20;
} sceGsTex1;

/* ---- libcdvd ---- */
typedef struct { u_char stat, second, minute, hour, pad, day, month, year; } sceCdCLOCK;
typedef struct { u_char trycount, spindlctrl, datapattern, pad; } sceCdRMode;
typedef struct { u_int lsn; u_int size; char name[16]; u_char date[8]; } sceCdlFILE;

/* ---- libipu / libmpeg (movie playback is reimplemented on PC) ---- */
typedef struct { u_int d3madr, d3tadr, d3qwc, d3chcr, d4madr, d4tadr, d4qwc, d4chcr, ipuctrl; } sceIpuDmaEnv;
typedef struct { int width, height, frameCount; long long pts, dts; int flags; long long pts2nd, dts2nd;
                 int flags2nd; void *sys; } sceMpeg;
typedef enum { sceMpegCbError, sceMpegCbNodata, sceMpegCbStopDMA, sceMpegCbRestartDMA,
               sceMpegCbBackground, sceMpegCbTimeStamp, sceMpegCbStr } sceMpegCbType;
typedef enum { sceMpegStrM2V = 0, sceMpegStrIPU = 1, sceMpegStrPCM = 2, sceMpegStrADPCM = 3,
               sceMpegStrDATA = 4 } sceMpegStrType;
typedef struct { sceMpegCbType type; } sceMpegCbDataCommon;
typedef struct { sceMpegCbType type; char *errMessage; } sceMpegCbDataError;
typedef struct { sceMpegCbType type; long long pts, dts; } sceMpegCbDataTimeStamp;
typedef struct { sceMpegCbType type; u_char *header; u_char *data; u_int len; long long pts, dts; } sceMpegCbDataStr;
typedef union {
    sceMpegCbType type;
    sceMpegCbDataError error;
    sceMpegCbDataTimeStamp ts;
    sceMpegCbDataStr str;
} sceMpegCbData;
typedef int (*sceMpegCallback)(sceMpeg *mp, sceMpegCbData *cbdata, void *anyData);

/* ---- libpad ---- */
#define scePadDmaBufferMax 16
#define scePadStateStable   6
#define scePadStateFindCTP1 7
#define InfoModeCurID       1
#define InfoModeCurExID     2
#define InfoModeIdTable     4

/* ---- libmc ---- */
typedef struct {
    struct { u_char Resv2, Sec, Min, Hour, Day, Month; u_short Year; } _Create, _Modify;
    u_int FileSizeByte;
    u_short AttrFile, Reserve1;
    u_int Reserve2, PdaAplNo;
    u_char EntryName[32];
} sceMcTblGetDir;

/* ---- libdma / sif ---- */
typedef struct { volatile u_int chcr, p0[3], madr, p1[3], qwc, p2[3], tadr, p3[3],
                 as0, p4[3], as1, p5[3], p6[8], sadr, p7[3]; } sceDmaChan;
typedef struct { u_int data, addr, size, mode; } sceSifDmaData;
typedef struct { void *serve; void *buff; void *cbuff; int command; void *paddr; u_int pdata; void *rdata; int rsize; int mode; } sceSifClientData;

/* ---- libgraph double buffering (rendering is replaced on PC; kept for type compatibility) ---- */
typedef struct { u_long64 data[20]; } sceGsDispEnv;
typedef struct { u_long64 data[40]; } sceGsDrawEnv1;
typedef struct { sceGsDispEnv disp[2]; u_long64 giftag0[2]; sceGsDrawEnv1 draw0; u_long64 giftag1[2]; sceGsDrawEnv1 draw1; } sceGsDBuff;
typedef struct { sceGsDispEnv disp[2]; u_long64 giftag0[2]; sceGsDrawEnv1 draw01; sceGsDrawEnv1 draw02;
                 u_long64 giftag1[2]; sceGsDrawEnv1 draw11; sceGsDrawEnv1 draw12; } sceGsDBuffDc;
typedef struct { u_long64 data[14]; } sceGsLoadImage;
typedef struct { u_long64 data[14]; } sceGsStoreImage;
