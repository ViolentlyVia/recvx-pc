/* PC port: thin OS layer (kept free of game headers so windows.h never meets them). */
#pragma once

void* pcOsFiberFromThread(void);
void* pcOsFiberCreate(unsigned int stack_size, void (*entry)(void*), void* arg);
void  pcOsFiberSwitch(void* fiber);
void  pcOsFiberDelete(void* fiber);

double pcOsTime(void);          /* seconds, monotonic */
void   pcOsSleep(double seconds);

void pcOsOpenConsole(void);
void pcOsSetWindowIcon(void* hwnd);  /* the exe's icon (pc/res/recvx.ico) on a window */          /* a console window for stdout (RECVX_CONSOLE=1) */
void pcOsInstallDiagnostics(void);   /* crash handler + hang watchdog (stack traces to the log) */
void pcOsHeartbeat(void);            /* call once per presented frame */

void* pcOsAllocScratchpad(void);    /* 16 KB at 0x70000000, or NULL */
