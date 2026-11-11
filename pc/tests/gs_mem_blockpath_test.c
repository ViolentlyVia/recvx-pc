/* Test (runs without the game): the block paths of gs_mem.c's image transfers (32, 8 and 4 bit)
 * must leave GS memory, the page generations and the transfer-done calls exactly as the pixel by
 * pixel reference does. Build it twice and compare what the two print:
 *
 *   cc -O2 -DGS_BLOCK_FASTPATH=1 gs_mem_blockpath_test.c -o fast
 *   cc -O2 -DGS_BLOCK_FASTPATH=0 gs_mem_blockpath_test.c -o ref
 *   ./fast > fast.txt; ./ref > ref.txt; cmp fast.txt ref.txt
 *
 * (python pc/tools/test_gs_mem.py does this with MSVC.) */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "../src/gs/gs_mem.c"

static unsigned long long done_hash;
static unsigned int done_count;

void gsXferDone(unsigned int bp, unsigned int bw, unsigned int psm, unsigned int x, unsigned int y, unsigned int w, unsigned int h, int changed)
{
    done_hash = (done_hash * 1099511628211ULL) ^ (bp | ((u64)bw << 14) | ((u64)psm << 20) | ((u64)x << 26) | ((u64)y << 38) | ((u64)changed << 50));
    done_hash = (done_hash * 1099511628211ULL) ^ (w | ((u64)h << 16));
    done_count++;
}

void gsFlushLocalToLocal(unsigned int bp, unsigned int bw, unsigned int psm, unsigned int x, unsigned int y, unsigned int w, unsigned int h)
{
    (void)bp; (void)bw; (void)psm; (void)x; (void)y; (void)w; (void)h;
}

static unsigned int rng = 1;
static unsigned int rnd(void) { rng = rng * 1664525u + 1013904223u; return rng >> 8; }

static unsigned long long fnv(const void* p, size_t n)
{
    const unsigned char* b = p;
    unsigned long long h = 1469598103934665603ULL;
    size_t i;
    for (i = 0; i < n; i++) h = (h ^ b[i]) * 1099511628211ULL;
    return h;
}

int main(void)
{
    static unsigned char data[1 << 20];
    int iter;

    gsMemInit();

    for (iter = 0; iter < 8000; iter++)
    {
        static const int formats[4] = { GS_PSMT8, GS_PSMT4, GS_PSMCT32, GS_PSMZ32 };
        int psm = formats[rnd() % 4];
        int t4 = (psm == GS_PSMT4), t32 = (psm == GS_PSMCT32) || (psm == GS_PSMZ32);
        int aligned = (rnd() % 4) != 0;
        unsigned int unit = t32 ? 8 : t4 ? 32 : 16;           /* a block's width and height in pixels */
        unsigned int unit_h = t32 ? 8 : 16;
        unsigned int bw = ((rnd() % 4) + 1) * 2;              /* even, as the game uses for 4/8-bit */
        unsigned int bp = (rnd() % 64) * 32;
        unsigned int w = aligned ? unit * (1 + rnd() % 6) : 1 + rnd() % 200;
        unsigned int h = aligned ? unit_h * (1 + rnd() % 6) : 1 + rnd() % 100;
        unsigned int sx = aligned ? unit * (rnd() % 3) : rnd() % 40;
        unsigned int sy = aligned ? unit_h * (rnd() % 3) : rnd() % 20;
        size_t total = t32 ? (size_t)w * h * 4 : t4 ? ((size_t)w * h + 1) / 2 : (size_t)w * h;
        size_t padded = (total + 15) & ~(size_t)15;
        size_t off = 0;
        GsRegs r;
        int same = (rnd() % 3) == 0;
        size_t k;

        memset(&r, 0, sizeof(r));
        r.bitbltbuf = ((u64)psm << 56) | ((u64)bw << 48) | ((u64)bp << 32);
        r.trxpos = ((u64)sy << 48) | ((u64)sx << 32);
        r.trxreg = ((u64)h << 32) | w;
        r.trxdir = 0;

        if (padded > sizeof(data)) continue;

        if (!same || iter == 0)
        {
            for (k = 0; k < padded; k++) data[k] = (unsigned char)((rnd() % 5) ? (k * 7 + iter) : rnd());
        }

        gsMemStartTransfer(&r);

        while (off < padded)
        {
            size_t chunk = ((rnd() % 3) == 0) ? 16 * (1 + rnd() % 64) : padded;   /* split the packet at random */
            if (off + chunk > padded) chunk = padded - off;
            gsMemTransferData(data + off, (int)(chunk / 16));
            off += chunk;
        }
    }

    printf("vram %016llx\n", fnv(gsVram, sizeof(gsVram)));
    printf("gen  %016llx\n", fnv(gsPageGen, sizeof(gsPageGen)));
    printf("done %016llx %u\n", done_hash, done_count);
    return 0;
}
