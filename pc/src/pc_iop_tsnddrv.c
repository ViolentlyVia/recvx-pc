/* PC port: stand-in for TSNDDRV.IRX, Tamsoft's IOP sound driver (not decompiled yet).
 *
 * Talks the same protocol as the real module so the EE sound code (ps2_snddrv.c, ps2_sg_sd.c)
 * runs unchanged:
 *   RPC id 0 - request stream: [cmd byte, port, ...] entries terminated by 0xFF
 *   RPC id 1 - "get state": fno is the query, the reply goes to receive[0]
 *   SND_STATUS block in IOP memory, read by the EE every frame through sceSifGetOtherData
 *
 * For now it plays no sound: it only acknowledges uploaded sound banks (the EE waits for a
 * checksum of each uploaded bank header) so the game can boot.
 */
#include <string.h>

#include "sce_pc.h"
#include "pc_platform.h"
#include "pc_iop.h"

/* IOP memory layout used by this stand-in */
#define STATUS_ADDR 0x1000    /* SND_STATUS */
#define TABLE_ADDR  0x1100    /* iop_data_adr[16] read by SdrInit() */
#define HD_ADDR     0x2000    /* bank headers (IOP_hd_size[] consecutive) */
#define SQ_ADDR     0x10000   /* sequences (IOP_tq_size[] consecutive) */
#define DATA_ADDR   0x20000   /* sample data staging buffer */

typedef struct {
    unsigned short se_info[6];
    unsigned short midi_info;
    short port_info[8];
    short midi_sum[4];
    short se_sum[5];
    unsigned short dummy[9];
} SndStatus;

static SndStatus* status(void)
{
    return (SndStatus*)pcIopPtr(STATUS_ADDR, sizeof(SndStatus));
}

/* signed-char sum of the bank header that was just uploaded, as computed by the EE */
static short last_upload_sum(u_int size)
{
    u_int addr, dma_size, i;
    const signed char* p;
    short sum = 0;

    pcIopLastDma(&addr, &dma_size);

    if (size > dma_size)
    {
        size = dma_size;
    }

    p = (const signed char*)pcIopPtr(addr, size);

    for (i = 0; i < size; i++)
    {
        sum += p[i];
    }

    return sum;
}

/* Length in bytes of one request, mirroring sending_req() in ps2_snddrv.c */
static int request_length(const unsigned char* p)
{
    int cd = p[0];

    switch (cd & 0xF0)
    {
    case 0x00:
        return 4 + ((cd & 1) ? 1 : 0) + ((cd & 2) ? 1 : 0) + ((cd & 4) ? 2 : 0);
    case 0x10:
        return (cd == 17) ? 3 : 1;
    case 0x20:
        if ((cd >= 34) && (cd <= 37)) return 3;
        if (cd == 38) return 4;
        if (cd == 32) return 5;
        if ((cd == 39) || (cd == 40) || (cd == 41) || (cd == 44) || (cd == 45)) return 8;
        return 2;
    case 0x40:
        if ((cd == 71) || (cd == 72) || (cd == 73) || (cd == 74) || (cd == 65) || (cd == 66)) return 2;
        if (cd == 75) return 3;
        if ((cd == 69) || (cd == 76)) return 4;
        if ((cd == 68) || (cd == 79)) return 6;
        if ((cd == 77) || (cd == 78)) return 21;
        return 1;
    case 0x50:
    case 0x60:
        return ((cd >= 81) && (cd <= 84)) ? 8 : 2;
    }

    return 1;
}

static void request_rpc(unsigned int fno, void* send, int ssize, void* receive, int rsize)
{
    const unsigned char* p = (const unsigned char*)send;
    const unsigned char* end = p + ssize;

    (void)fno; (void)receive; (void)rsize;

    while ((p < end) && (p[0] != 0xFF))
    {
        int cd = p[0];
        int port = p[1];

        if ((cd == 0x28) || (cd == 0x29))
        {
            u_int size = p[4] | (p[5] << 8) | (p[6] << 16) | (p[7] << 24);
            short sum = last_upload_sum(size);

            if ((cd == 0x28) && (port < 4))
            {
                status()->midi_sum[port] = sum;
            }
            else if ((cd == 0x29) && (port < 5))
            {
                status()->se_sum[port] = sum;
            }
        }

        p += request_length(p);
    }
}

static void get_state_rpc(unsigned int fno, void* send, int ssize, void* receive, int rsize)
{
    int* reply = (int*)receive;

    (void)send; (void)ssize;

    if ((reply == NULL) || (rsize < 4))
    {
        return;
    }

    switch (fno)
    {
    case 18:
        reply[0] = STATUS_ADDR;
        break;
    case 19:
        reply[0] = TABLE_ADDR;
        break;
    default:
        reply[0] = 0;
        break;
    }
}

static void tsnddrv_init(void)
{
    int* table = (int*)pcIopPtr(TABLE_ADDR, 64);

    memset(status(), 0, sizeof(SndStatus));
    memset(table, 0, 64);

    table[0] = HD_ADDR;
    table[1] = SQ_ADDR;
    table[2] = DATA_ADDR;

    pcIopRegisterService(0, request_rpc);
    pcIopRegisterService(1, get_state_rpc);
}

void pcIopInitServices(void)
{
    static int done;

    if (done)
    {
        return;
    }

    done = 1;

    tsnddrv_init();
}
