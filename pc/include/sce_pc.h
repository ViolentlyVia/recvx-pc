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
typedef void (*sceSifEndFunc)(void* data);
typedef struct { void* paddr; u_int pid; int tid; u_int mode; } sceSifRpcData;
typedef struct { sceSifRpcData rpcd; u_int command; void* buff; void* cbuff; sceSifEndFunc func; void* para; void* serve; } sceSifClientData;

/* ---- libgraph double buffering (rendering is replaced on PC; kept for type compatibility) ---- */
typedef struct { u_long64 data[20]; } sceGsDispEnv;
typedef struct { u_long64 data[40]; } sceGsDrawEnv1;
typedef struct { sceGsDispEnv disp[2]; u_long64 giftag0[2]; sceGsDrawEnv1 draw0; u_long64 giftag1[2]; sceGsDrawEnv1 draw1; } sceGsDBuff;
typedef struct { sceGsDispEnv disp[2]; u_long64 giftag0[2]; sceGsDrawEnv1 draw01; sceGsDrawEnv1 draw02;
                 u_long64 giftag1[2]; sceGsDrawEnv1 draw11; sceGsDrawEnv1 draw12; } sceGsDBuffDc;
typedef struct { u_long64 data[14]; } sceGsLoadImage;
typedef struct { u_long64 data[14]; } sceGsStoreImage;

/* ---- EE kernel (threads, semaphores) ---- */
struct ThreadParam {
    int status;
    void (*entry)(void*);
    void* stack;
    int stackSize;
    void* gpReg;
    int initPriority;
    int currentPriority;
    u_int attr;
    u_int option;
    int waitType;
    int waitId;
    int wakeupCount;
};

struct SemaParam {
    int currentCount;
    int maxCount;
    int initCount;
    int numWaitThreads;
    u_int attr;
    u_int option;
};

extern char _gp[];

int  CreateThread(struct ThreadParam* param);
int  StartThread(int thid, void* arg);
int  DeleteThread(int thid);
int  TerminateThread(int thid);
int  GetThreadId(void);
int  ReferThreadStatus(int thid, struct ThreadParam* info);
int  ChangeThreadPriority(int thid, int prio);
int  RotateThreadReadyQueue(int prio);
int  SleepThread(void);
int  WakeupThread(int thid);
int  iWakeupThread(int thid);
int  SuspendThread(int thid);
int  ResumeThread(int thid);
int  CreateSema(struct SemaParam* param);
int  DeleteSema(int semid);
int  SignalSema(int semid);
int  iSignalSema(int semid);
int  WaitSema(int semid);
int  PollSema(int semid);
int  ReferSemaStatus(int semid, struct SemaParam* info);
int  AddIntcHandler(int cause, int (*handler)(int), int next);
int  RemoveIntcHandler(int cause, int hid);
int  EnableIntc(int cause);
int  DisableIntc(int cause);
void FlushCache(int op);
void SyncDCache(void* start, void* end);
void InvalidDCache(void* start, void* end);
void iSyncDCache(void* start, void* end);
void iInvalidDCache(void* start, void* end);
int  DIntr(void);
int  EIntr(void);
int  ExitHandler(void);
int  SetAlarm(unsigned short time, void (*handler)(int, unsigned short, void*), void* arg);
int  iSetAlarm(unsigned short time, void (*handler)(int, unsigned short, void*), void* arg);
int  ReleaseAlarm(int alarm_id);
int  iReleaseAlarm(int alarm_id);
int  sceGsSyncV(int mode);
int  sceGsSyncVCallback(int (*func)(int));

/* ---- SIF RPC (EE <-> IOP); the IOP side does not exist on PC ---- */
typedef void* (*sceSifRpcFunc)(unsigned int fno, void* data, int size);
typedef struct { int dummy[8]; } sceSifQueueData;
typedef struct { int dummy[16]; } sceSifServeData;
typedef struct { int dummy[4]; } sceSifReceiveData;

/* ---- libcdvd error / status codes ---- */
#define SCECdErNO     0x00
#define SCECdErEOM    0x32
#define SCECdErTRMOPN 0x31
#define SCECdErREAD   0x30
#define SCECdErPRM    0x22
#define SCECdErILI    0x21
#define SCECdErIPI    0x20
#define SCECdErABRT   0x01
#define SCECdErCMD    0x14
#define SCECdErOPENS  0x15
#define SCECdErNODISC 0x16
#define SCECdErNORDY  0x17
#define SCECdErCUD    0x18
#define SCECdErFAIL   (-1)
#define SCECdNODISC   0x00
#define SCECdDETCT    0x01
#define SCECdDETCTCD  0x02
#define SCECdDETCTDVDS 0x03
#define SCECdDETCTDVDD 0x04
#define SCECdUNKNOWN  0x05
#define SCECdPSCD     0x10
#define SCECdPSCDDA   0x11
#define SCECdPS2CD    0x12
#define SCECdPS2CDDA  0x13
#define SCECdPS2DVD   0x14
#define SCECdCDDA     0xfd
#define SCECdDVDV     0xfe
#define SCECdIllgalMedia 0xff
#define SCECdEXIT     0x10
#define SCECdINIT     0x00
#define SCECdINon     0x01
#define SCECdINoD     0x02
#define SCECdCD       1
#define SCECdDVD      2
#define SCECdSpinMax  0
#define SCECdSpinNom  1
#define SCECdSpinStm  0
#define SCECdSecS2048 0

/* ---- libcdvd functions (pc/src/pc_cdvd.c) ---- */
int sceCdInit(int mode);
int sceCdMmode(int media);
int sceCdSearchFile(sceCdlFILE* fp, const char* name);
int sceCdRead(u_int lsn, u_int sectors, void* buf, sceCdRMode* mode);
int sceCdSync(int mode);
int sceCdGetError(void);
int sceCdDiskReady(int mode);
int sceCdBreak(void);
int sceCdPause(void);
int sceCdStandby(void);
int sceCdGetDiskType(void);
int sceCdReadClock(sceCdCLOCK* rtc);
int sceCdStInit(u_int bufmax, u_int bankmax, u_int iop_bufaddr);
int sceCdStStart(u_int lsn, sceCdRMode* mode);
int sceCdStRead(u_int sectors, u_int* buf, u_int mode, u_int* err);
int sceCdStStop(void);
int sceCdStSeek(u_int lsn);
int sceCdStPause(void);
int sceCdStResume(void);

/* ---- libpad ---- */
int scePadInit(int mode);
int scePadEnd(void);
int scePadPortOpen(int port, int slot, void* addr);
int scePadPortClose(int port, int slot);
int scePadRead(int port, int slot, unsigned char* rdata);
int scePadGetState(int port, int slot);
int scePadGetReqState(int port, int slot);
int scePadInfoMode(int port, int slot, int term, int offs);
int scePadSetMainMode(int port, int slot, int offs, int lock);
int scePadInfoPressMode(int port, int slot);
int scePadEnterPressMode(int port, int slot);
int scePadExitPressMode(int port, int slot);
int scePadSetActAlign(int port, int slot, const unsigned char* data);
int scePadSetActDirect(int port, int slot, const unsigned char* data);
int scePadInfoAct(int port, int slot, int actno, int term);

/* ---- libmc ---- */
int sceMcInit(void);
int sceMcGetInfo(int port, int slot, int* type, int* free, int* format);
int sceMcOpen(int port, int slot, const char* name, int mode);
int sceMcClose(int fd);
int sceMcRead(int fd, void* buf, int size);
int sceMcWrite(int fd, const void* buf, int size);
int sceMcMkdir(int port, int slot, const char* name);
int sceMcChdir(int port, int slot, const char* newdir, char* olddir);
int sceMcGetDir(int port, int slot, const char* name, unsigned int mode, int maxent, sceMcTblGetDir* table);
int sceMcFormat(int port, int slot);
int sceMcDelete(int port, int slot, const char* name);
int sceMcSync(int mode, int* cmd, int* result);
