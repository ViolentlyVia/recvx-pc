/* PC port: emulated IOP memory and RPC services (pc/src/pc_iop.c). */
#pragma once

typedef void (*PcIopRpcHandler)(unsigned int fno, void* send, int ssize, void* receive, int rsize);

extern unsigned char pcIopRam[];

/* Host pointer for an IOP address range (fatal error when out of range). */
void* pcIopPtr(unsigned int addr, unsigned int size);

/* Destination and size of the most recent EE -> IOP DMA. */
void pcIopLastDma(unsigned int* addr, unsigned int* size);

void pcIopRegisterService(unsigned int id, PcIopRpcHandler handler);

/* Registers every PC replacement for an IOP module (called once, lazily). */
void pcIopInitServices(void);
