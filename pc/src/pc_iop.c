/* PC port: the IOP side of the PS2, as seen from the EE through SIF.
 *
 * IOP memory is a 2 MB array; "IOP addresses" handed to the game are offsets into it, so code
 * that passes them around as integers keeps working and every access goes through SIF calls
 * (sceSifSetDma, sceSifGetOtherData) exactly like on the PS2.
 *
 * IOP modules are replaced by PC "services" bound to their RPC ids (see pcIopRegisterService).
 */
#include <stdlib.h>
#include <string.h>

#include "sce_pc.h"
#include "pc_platform.h"
#include "pc_iop.h"

#define IOP_RAM_SIZE  0x200000
#define IOP_HEAP_BASE 0x100000

unsigned char pcIopRam[IOP_RAM_SIZE];

static u_int heap_next = IOP_HEAP_BASE;

typedef struct {
    u_int id;
    PcIopRpcHandler handler;
} Service;

static Service services[32];
static int service_count;

static u_int last_dma_addr;
static u_int last_dma_size;

void* pcIopPtr(u_int addr, u_int size)
{
    if ((addr >= IOP_RAM_SIZE) || (size > IOP_RAM_SIZE - addr))
    {
        pcPlatformFatal("IOP access out of range: 0x%08x (+%u)", addr, size);
    }

    return &pcIopRam[addr];
}

void pcIopLastDma(u_int* addr, u_int* size)
{
    *addr = last_dma_addr;
    *size = last_dma_size;
}

void pcIopRegisterService(u_int id, PcIopRpcHandler handler)
{
    services[service_count].id = id;
    services[service_count].handler = handler;
    service_count++;
}

static PcIopRpcHandler find_service(u_int id)
{
    int i;

    pcIopInitServices();

    for (i = 0; i < service_count; i++)
    {
        if (services[i].id == id)
        {
            return services[i].handler;
        }
    }

    return NULL;
}

/* ------------------------------------------------------------------------------------------- */

int  sceSifInitRpc(unsigned int mode)            { (void)mode; return 0; }
int  sceSifRebootIop(const char* img)            { (void)img; return 1; }
int  sceSifSyncIop(void)                         { return 1; }
int  sceSifInitIopHeap(void)                     { return 0; }
int  sceFsReset(void)                            { return 0; }

int sceSifLoadModule(const char* path, int args, const char* argp)
{
    (void)args; (void)argp;
    pcLog("IOP module (replaced on PC): %s", path);
    return 0;
}

void* sceSifAllocIopHeap(int size)
{
    u_int addr = (heap_next + 63) & ~63u;

    if (addr + (u_int)size > IOP_RAM_SIZE)
    {
        pcLog("sceSifAllocIopHeap: out of IOP memory (%d bytes)", size);
        return NULL;
    }

    heap_next = addr + size;
    memset(&pcIopRam[addr], 0, size);

    return (void*)(size_t)addr;
}

int sceSifFreeIopHeap(void* p)
{
    (void)p;   /* bump allocator; the game allocates once at startup */
    return 0;
}

int sceSifBindRpc(sceSifClientData* c, unsigned int request, unsigned int mode)
{
    (void)mode;

    c->command = request;
    c->serve = (void*)find_service(request);

    if (c->serve == NULL)
    {
        static int dummy;

        pcLog("sceSifBindRpc: no PC service for RPC id 0x%08x yet", request);
        c->serve = &dummy;
    }

    return 0;
}

int sceSifCallRpc(sceSifClientData* c, unsigned int fno, unsigned int mode, void* send, int ssize, void* receive, int rsize,
                  void (*end_func)(void*), void* end_para)
{
    PcIopRpcHandler handler = find_service(c->command);

    (void)mode;

    if (handler != NULL)
    {
        handler(fno, send, ssize, receive, rsize);
    }
    else if ((receive != NULL) && (rsize > 0))
    {
        memset(receive, 0, rsize);
    }

    if (end_func != NULL)
    {
        end_func(end_para);
    }

    return 0;
}

int sceSifCheckStatRpc(sceSifClientData* cd)
{
    (void)cd;
    return 0;
}

void sceSifRegisterRpc(void* sd, unsigned int request, void* func, void* buff, void* cfunc, void* cbuff, void* qd)
{
    (void)sd; (void)request; (void)func; (void)buff; (void)cfunc; (void)cbuff; (void)qd;
}

void sceSifSetRpcQueue(void* qd, int thread_id)
{
    (void)qd; (void)thread_id;
}

void sceSifRpcLoop(void* qd)
{
    /* EE-side RPC server for calls from the IOP: nothing calls it on PC */
    (void)qd;

    for (;;)
    {
        SleepThread();
    }
}

/* EE -> IOP transfer */
unsigned int sceSifSetDma(sceSifDmaData* sdd, int len)
{
    int i;

    for (i = 0; i < len; i++)
    {
        if (sdd[i].size == 0)
        {
            continue;
        }

        memcpy(pcIopPtr(sdd[i].addr, sdd[i].size), (void*)(size_t)sdd[i].data, sdd[i].size);

        last_dma_addr = sdd[i].addr;
        last_dma_size = sdd[i].size;
    }

    return 1;
}

unsigned int isceSifSetDma(sceSifDmaData* sdd, int len)
{
    return sceSifSetDma(sdd, len);
}

int sceSifDmaStat(unsigned int id)
{
    (void)id;
    return -1;   /* completed */
}

/* IOP -> EE transfer */
int sceSifGetOtherData(void* rd, void* src, void* dest, int size, unsigned int mode)
{
    (void)rd; (void)mode;

    memcpy(dest, pcIopPtr((u_int)(size_t)src, size), size);

    return 0;
}
