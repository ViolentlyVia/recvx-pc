/* PC port: checks the GS memory layouts against the game's own texture conversion
 * (ps2_pxlconv.c): an 8/4-bit image converted to 32-bit and stored as PSMCT32 must read back
 * unchanged as PSMT8/PSMT4, exactly as on the real GS. */
#include "../../include/ps2/veronica/prog/ps2_pxlconv.h"
#include "../../include/ps2/veronica/prog/main.h"

#include "pc_platform.h"

unsigned int gsMemRead(int psm, unsigned int bp, unsigned int bw, unsigned int x, unsigned int y);
void gsMemWrite(int psm, unsigned int bp, unsigned int bw, unsigned int x, unsigned int y, unsigned int v);

static int run(int bits)
{
    enum { W = 256, H = 256 };
    static unsigned char in[W * H], out[W * H];
    int cw = 128, ch = (bits == 8) ? 128 : 64;      /* converted PSMCT32 image size */
    int x, y, errors = 0;
    unsigned int seed = 12345;

    for (x = 0; x < (int)sizeof(in); x++)
    {
        seed = seed * 1103515245 + 12345;
        in[x] = (unsigned char)(seed >> 16);
    }

    if (bits == 8)
    {
        Conv8to32(W, H, in, out);
    }
    else
    {
        Conv4to32(W, H, in, out);
    }

    for (y = 0; y < ch; y++)
    {
        for (x = 0; x < cw; x++)
        {
            gsMemWrite(0x00, 0, cw / 64, x, y, *(unsigned int*)&out[(y * cw + x) * 4]);
        }
    }

    for (y = 0; y < H; y++)
    {
        for (x = 0; x < W; x++)
        {
            unsigned int want = (bits == 8) ? in[y * W + x] : ((in[(y * W + x) / 2] >> ((x & 1) * 4)) & 15);
            unsigned int got = gsMemRead((bits == 8) ? 0x13 : 0x14, 0, W / 64, x, y);

            if (want != got)
            {
                if (errors < 4)
                {
                    pcLog("gs selftest: PSMT%d mismatch at %d,%d (want %u got %u)", bits, x, y, want, got);
                }

                errors++;
            }
        }
    }

    return errors;
}

void pcGsSelfTest(void)
{
    int e8 = run(8), e4 = run(4);

    pcLog("gs selftest: PSMT8 %s, PSMT4 %s", e8 ? "FAILED" : "ok", e4 ? "FAILED" : "ok");
}

