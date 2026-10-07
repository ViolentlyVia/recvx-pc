/* PC port: platform services shared by the pc/src modules. */
#pragma once

#include "pc_kernel.h"

void pcLog(const char* fmt, ...);
void pcPlatformFatal(const char* fmt, ...);

/* Directory containing recvx.exe (no trailing slash). */
void pcPlatformExeDir(char* out, int size);

/* First file in dir whose name ends with ext (case-insensitive); full path in out. */
int pcPlatformFindFile(const char* dir, const char* ext, char* out, int size);

/* Window / frame */
void pcVideoInit(void);
void pcVideoPresent(void);
int  pcVideoPumpEvents(void);   /* returns 0 when the window was closed */

/* Disc */
int   pcCdOpen(void);
void* pcCdLoadFile(const char* path, unsigned int* size_out);

/* Controller */
void pcPadInit(void);
void pcPadUpdate(void);
void pcPadGetState(int port, unsigned char* rdata);  /* 32-byte DualShock 2 report */
