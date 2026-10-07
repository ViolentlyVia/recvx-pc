"""Port the VU0 code in objitm.c (bhObj005 water surface) to C for TARGET_PC."""
import sys, os
sys.path.insert(0, os.path.dirname(__file__))
from srcedit import Source

src = Source("src/ps2/veronica/prog/objitm.c")
blocks = [
# 0: update the water mesh from the height field. Vertices are 32 bytes (position, normal) and
#    position.x of the source mesh holds a pointer into the height field. Each frame updates half
#    the mesh: j == 0 refreshes positions of even vertices and normals of odd ones, j == 1 the
#    opposite. Normals come from VU0_WAVE_CALC: (h[-zp] - h[+zp], -2, h[-1] - h[+1]) / L1 norm.
"""
{
    unsigned char* s = (unsigned char*)pCnk;
    unsigned char* d = (unsigned char*)pOrg;
    int zp = sys->wt_zp;
    int cnt = nb >> 1;
    float* h;

    if (j == 0)
    {
        *(float*)(d + 4) = *(float*)(s + 4) + **(float**)s;

        cnt--;

        s += 32;
        d += 32;
    }

    h = *(float**)s;

    s += 16;
    d += 16;

    do
    {
        float nx = h[zp] - h[-zp];
        float ny = -2.0f;
        float nz = h[1] - h[-1];
        float q = 1.0f / ((fabsf(nx) + fabsf(ny)) + fabsf(nz));

        *(float*)(d + 0x14) = *(float*)(s + 0x14) + **(float**)(s + 0x10);

        h = *(float**)(s + 0x30);

        ((float*)d)[0] = (0 - nx) * q;
        ((float*)d)[1] = ny * q;
        ((float*)d)[2] = (0 - nz) * q;
        ((float*)d)[3] = 0;

        s += 64;
        d += 64;
    } while (--cnt != 0);
}
""",
# 1: let the waves settle: h = max(h - 0.2, 0) over whole 16-float blocks (the PS2 loop never
#    stores the remainder, so neither do we)
"""
{
    float* w = (float*)sys->wt_wvp;
    int k, n = (sys->wt_nbpt >> 4) * 16;

    for (k = 0; k < n; k++)
    {
        float v = w[k] - 0.2f;

        w[k] = (v > 0) ? v : 0;
    }
}
""",
]
assert len(src.asm_blocks()) == len(blocks), src.asm_blocks()
for code in blocks:
    src.port_asm(0, code)
src.save()
