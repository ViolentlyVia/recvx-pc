/* PC port: platform services shared by the pc/src modules. */
#pragma once

#include "pc_kernel.h"

void pcLog(const char* fmt, ...);
void pcPlatformFatal(const char* fmt, ...);
/* The game files are missing: offer to open recvx-config.exe to set the game up, then quit. */
void pcPlatformNotSetUp(void);

/* Directory containing recvx.exe (no trailing slash). */
void pcPlatformExeDir(char* out, int size);

/* recvx.ini (next to the exe): value of the last "key=value" line, trailing whitespace trimmed.
 * Returns 0 (out untouched) when the key is missing. */
int pcIniGet(const char* key, char* out, int size);
int pcIniGetInt(const char* key, int def);
/* Set (or with value NULL remove) "key=value" in recvx.ini, keeping every other line. */
void pcIniSet(const char* key, const char* value);

/* First file in dir whose name ends with ext (case-insensitive); full path in out. */
int pcPlatformFindFile(const char* dir, const char* ext, char* out, int size);

/* Window / frame */
void pcVideoInit(void);
void pcVideoPresent(void);
int  pcVideoPumpEvents(void);   /* returns 0 when the window was closed */
float pcVideoAspect(void);      /* display aspect from recvx.ini (4:3 = 1.333) */
float pcVideoWideScale(void);   /* (4/3) / aspect: horizontal squeeze of the 640 wide frame */

/* Disc */
int   pcCdOpen(void);
void* pcCdLoadFile(const char* path, unsigned int* size_out);

/* Controller */
void pcPadInit(void);
void pcPadUpdate(void);
void pcPadGetState(int port, unsigned char* rdata);  /* 32-byte DualShock 2 report */
